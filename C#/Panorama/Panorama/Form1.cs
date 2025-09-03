using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Windows.Forms;
using System.Xml;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.Formats.Jpeg;

using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Formats.Tiff;

using SDImage = System.Drawing.Image;

namespace Panorama
{

    public partial class Form1 : Form
    {
        private string imageFolderPath = string.Empty;
        private string coordFilePath = string.Empty;

        List<(string name, string path)> images = new List<(string name, string path)>();
        public List<(string name, float x, float y, float z, float viewfield)> Locations { get; } =
            new List<(string name, float x, float y, float z, float viewfield)>();

        private string outputFilePath = string.Empty;

        private bool imagesLoaded = false;
        private bool locationsLoaded = false;

        // Treat pixels darker than this as background (0..255). Start with 18–32.
        private byte bgThreshold = 24;
        // Feather the mask edges (in pixels) to avoid hard cut halos. 0..3 is typical.
        private float maskFeatherSigma = 0.8f;

        float globalScale = 0.25f;

        public Form1()
        {
            InitializeComponent();
        }

        private void ReadImages(string rootPath)
        {
            images.Clear();

            if (!Directory.Exists(rootPath))
            {
                MessageBox.Show("Selected folder does not exist.");
                return;
            }

            // Allowed image extensions
            var exts = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".tif", ".tiff", ".bmp"
    };

            int loaded = 0, skipped = 0;

            foreach (var dir in Directory.EnumerateDirectories(rootPath))
            {
                string folderName = Path.GetFileName(dir) ?? dir;

                // 1) Prefer exact filename "panorama_CL" with allowed extensions
                string candidate = Directory.EnumerateFiles(dir)
                    .FirstOrDefault(f =>
                        exts.Contains(Path.GetExtension(f)) &&
                        string.Equals(Path.GetFileNameWithoutExtension(f), "panorama_CL", StringComparison.OrdinalIgnoreCase));

                // 2) If not found, fall back to anything that contains "panorama" in the name (optional, helpful for messy data)
                if (candidate == null)
                {
                    candidate = Directory.EnumerateFiles(dir)
                        .FirstOrDefault(f =>
                            exts.Contains(Path.GetExtension(f)) &&
                            Path.GetFileNameWithoutExtension(f)
                                .Contains("panorama", StringComparison.OrdinalIgnoreCase));
                }

                if (candidate != null && File.Exists(candidate))
                {
                    // Use the folder name as the image "name" so it can match XML entries like Capture1, Capture2, etc.
                    images.Add((folderName, candidate));
                    loaded++;
                }
                else
                {
                    skipped++;
                }
            }

            imagesLoaded = loaded > 0;

            // Optional: quick summary for you
            MessageBox.Show($"Loaded {loaded} images from subfolders. Skipped {skipped} folders without a matching 'panorama_CL'.");
        }

        private void ReadXML(string path)
        {
            try
            {
                Locations.Clear();

                XmlDocument xmlDoc = new XmlDocument();
                xmlDoc.Load(path);

                XmlNodeList allObjectsElements = xmlDoc.GetElementsByTagName("*");
                foreach (XmlNode node in allObjectsElements)
                {
                    if (node.Name.Equals("Objects", StringComparison.OrdinalIgnoreCase))
                    {
                        var columnTypeAttr = node.Attributes?["columnType"];
                        var columnIndexAttr = node.Attributes?["columnIndex"];

                        if ((columnTypeAttr?.Value == "0") && (columnIndexAttr?.Value == "0"))
                        {
                            foreach (XmlNode objectNode in node.ChildNodes)
                            {
                                if (!objectNode.Name.Equals("Object", StringComparison.OrdinalIgnoreCase))
                                    continue;

                                string name = objectNode.Attributes?["name"]?.Value ?? "Unnamed";
                                float z = float.Parse(objectNode.Attributes?["z"]?.Value ?? "0", CultureInfo.InvariantCulture);


                                XmlNode firstPoint = null;
                                foreach (XmlNode child in objectNode.ChildNodes)
                                {
                                    if (child.Name.Equals("Point", StringComparison.OrdinalIgnoreCase))
                                    {
                                        firstPoint = child;
                                        break;
                                    }
                                }

                                if (firstPoint == null) continue;

                                float x = float.Parse(firstPoint.Attributes?["x"]?.Value ?? "0", CultureInfo.InvariantCulture);
                                float y = float.Parse(firstPoint.Attributes?["y"]?.Value ?? "0", CultureInfo.InvariantCulture);
                                float viewfield = float.Parse(objectNode.Attributes["viewfield"]?.Value ?? "0.4", CultureInfo.InvariantCulture);

                                Locations.Add((name, x, y, z, viewfield));
                            }
                        }
                    }
                }
                locationsLoaded = true;
                //MessageBox.Show($"Successfully loaded {Locations.Count} coordinates");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading XML: {ex.Message}");
            }
        }

        private SixLabors.ImageSharp.Image<SixLabors.ImageSharp.PixelFormats.Rgba32>
        AssembleImageSharp(string outputPath, IProgress<int> progress = null)
        {
            if (images.Count == 0 || Locations.Count == 0)
                throw new InvalidOperationException("Images or locations not loaded.");

            // Keying parameters
            byte bgThreshold = 24;         // raise if background isn’t pure black (e.g., 32–48)
            float maskFeatherSigma = 0.8f; // 0..1.2 typical feather

            // Case-insensitive name→path map
            var nameToPath = images.ToDictionary(i => i.name, i => i.path, StringComparer.OrdinalIgnoreCase);

            // ----- Determine pixel scale (px/µm) from first valid tile -----
            float scalePxPerUm = -1f;
            foreach (var loc in Locations)
            {
                if (loc.viewfield <= 0) continue;
                if (!nameToPath.TryGetValue(loc.name, out var path) || !File.Exists(path)) continue;

                int widthPx;
                var info = SixLabors.ImageSharp.Image.Identify(path);
                if (info != null) widthPx = info.Width;
                else { using var tmp = SixLabors.ImageSharp.Image.Load<SixLabors.ImageSharp.PixelFormats.Rgba32>(path); widthPx = tmp.Width; }

                float fov_um = loc.viewfield * 1000f; // mm → µm
                if (fov_um > 0) { scalePxPerUm = widthPx / fov_um; break; }
            }
            if (scalePxPerUm <= 0) throw new Exception("Could not determine scale from any image's viewfield.");

            // ----- Compute bounds in pixels -----
            float minY_um_all = Locations.Min(l => l.y * 1000f);
            float maxX_um_all = Locations.Max(l => l.x * 1000f);

            float maxX_px_needed = 0f, maxY_px_needed = 0f;

            foreach (var loc in Locations)
            {
                if (!nameToPath.TryGetValue(loc.name, out var path) || !File.Exists(path)) continue;

                int w, h;
                var info = SixLabors.ImageSharp.Image.Identify(path);
                if (info != null) { w = info.Width; h = info.Height; }
                else { using var tmpSz = SixLabors.ImageSharp.Image.Load<SixLabors.ImageSharp.PixelFormats.Rgba32>(path); w = tmpSz.Width; h = tmpSz.Height; }

                float x_um_flipped = maxX_um_all - (loc.x * 1000f);
                float y_um = (loc.y * 1000f) - minY_um_all;

                float x_px = x_um_flipped * scalePxPerUm;
                float y_px = y_um * scalePxPerUm;

                maxX_px_needed = Math.Max(maxX_px_needed, x_px + w);
                maxY_px_needed = Math.Max(maxY_px_needed, y_px + h);
            }

            int origW = Math.Max(1, (int)Math.Ceiling(maxX_px_needed));
            int origH = Math.Max(1, (int)Math.Ceiling(maxY_px_needed));

            // ----- Safety downscale for huge canvases -----
            long totalPx = (long)origW * origH;
            const long maxSafePx = 16000L * 16000L; // ~256 MPix cap (adjust if needed)
            float safeScale = totalPx > maxSafePx ? (float)Math.Sqrt((double)maxSafePx / totalPx) : 1f;

            int W = Math.Max(1, (int)(origW * safeScale));
            int H = Math.Max(1, (int)(origH * safeScale));
            float finalScalePxPerUm = scalePxPerUm * safeScale;

            // ----- Create canvas -----
            var canvas = new SixLabors.ImageSharp.Image<SixLabors.ImageSharp.PixelFormats.Rgba32>(
                W, H, SixLabors.ImageSharp.Color.Black);

            // ----- Composite tiles -----
            int processed = 0;
            int total = Locations.Count;

            foreach (var loc in Locations)
            {
                if (!nameToPath.TryGetValue(loc.name, out var path) || !File.Exists(path))
                {
                    // still report to keep pace if you want; or skip
                    processed++;
                    progress?.Report(processed);
                    continue;
                }

                using var tile = SixLabors.ImageSharp.Image.Load<SixLabors.ImageSharp.PixelFormats.Rgba32>(path);

                using (var mask = BuildAlphaMask(tile, bgThreshold, maskFeatherSigma))
                {
                    ApplyAlphaMask(tile, mask);
                }

                int newW = Math.Max(1, (int)(tile.Width * safeScale));
                int newH = Math.Max(1, (int)(tile.Height * safeScale));
                using var resized = tile.Clone(ctx => ctx.Resize(newW, newH, SixLabors.ImageSharp.Processing.KnownResamplers.Bicubic));

                float x_um_flipped = maxX_um_all - (loc.x * 1000f);
                float y_um = (loc.y * 1000f) - minY_um_all;

                int x_px = (int)(x_um_flipped * finalScalePxPerUm);
                int y_px = (int)(y_um * finalScalePxPerUm);

                canvas.Mutate(ctx => ctx.DrawImage(resized, new SixLabors.ImageSharp.Point(x_px, y_px), 1f));

                // report after placing each tile
                processed++;
                progress?.Report(processed);
            }

            // ----- Save -----
            var encoder = PickEncoderForPath(outputPath);
            canvas.Save(outputPath, encoder);

            return canvas;
        }

        // Builds an alpha mask where near-black becomes transparent (0), bright = opaque (255)
        private static SixLabors.ImageSharp.Image<SixLabors.ImageSharp.PixelFormats.L8>
        BuildAlphaMask(SixLabors.ImageSharp.Image<SixLabors.ImageSharp.PixelFormats.Rgba32> src,
               byte threshold, float featherSigma)
        {
            var mask = new SixLabors.ImageSharp.Image<SixLabors.ImageSharp.PixelFormats.L8>(src.Width, src.Height);

            for (int y = 0; y < src.Height; y++)
            {
                for (int x = 0; x < src.Width; x++)
                {
                    var p = src[x, y];
                    int lum = (int)(0.2126f * p.R + 0.7152f * p.G + 0.0722f * p.B); // BT.709 luma
                    mask[x, y] = lum <= threshold
                        ? new SixLabors.ImageSharp.PixelFormats.L8(0)
                        : new SixLabors.ImageSharp.PixelFormats.L8(255);
                }
            }

            if (featherSigma > 0f)
                mask.Mutate(ctx => ctx.GaussianBlur(featherSigma));

            return mask;
        }

        private static void ApplyAlphaMask(
        SixLabors.ImageSharp.Image<SixLabors.ImageSharp.PixelFormats.Rgba32> src,
        SixLabors.ImageSharp.Image<SixLabors.ImageSharp.PixelFormats.L8> mask)
        {
            if (src.Width != mask.Width || src.Height != mask.Height)
                throw new ArgumentException("Mask and image sizes must match.");

            for (int y = 0; y < src.Height; y++)
            {
                for (int x = 0; x < src.Width; x++)
                {
                    byte a = mask[x, y].PackedValue;
                    if (a == 0)
                    {
                        src[x, y] = new SixLabors.ImageSharp.PixelFormats.Rgba32(0, 0, 0, 0);
                    }
                    else
                    {
                        var p = src[x, y];
                        p.A = a;
                        src[x, y] = p;
                    }
                }
            }
        }

        private static IImageEncoder PickEncoderForPath(string path)
        {
            string ext = Path.GetExtension(path)?.ToLowerInvariant() ?? ".jpg";
            return ext switch
            {
                ".png" => new PngEncoder(),
                ".tif" or ".tiff" => new TiffEncoder(),
                _ => new JpegEncoder { Quality = 95 }
            };
        }

        private async void OnDataReady()
        {
            try
            {
                // Ensure output path chosen (reuse your helper if you have it)
                if (string.IsNullOrEmpty(outputFilePath))
                {
                    if (!PromptForOutputPath()) return; // user canceled
                }

                // Setup ToolStripProgressBar
                toolStripProgressBar1.Visible = true;                 // show in StatusStrip
                toolStripProgressBar1.Minimum = 0;
                toolStripProgressBar1.Maximum = Math.Max(1, Locations.Count);
                toolStripProgressBar1.Value = 0;
                toolStripProgressBar1.Style = ProgressBarStyle.Continuous;

                // Progress object marshals updates onto the UI thread automatically
                var progress = new Progress<int>(value =>
                {
                    int v = Math.Min(Math.Max(value, toolStripProgressBar1.Minimum), toolStripProgressBar1.Maximum);
                    toolStripProgressBar1.Value = v;
                });

                // Run heavy work off the UI thread
                await Task.Run(() => AssembleImageSharp(outputFilePath, progress).Dispose());

                MessageBox.Show($"Composite image assembled and saved:\n{outputFilePath}");
            }
            catch (Exception ex)
            {
                MessageBox.Show("Assembly failed: " + ex.Message);
            }
            finally
            {
                // Reset/hide the progress bar
                toolStripProgressBar1.Value = 0;
                toolStripProgressBar1.Visible = false;
            }
        }

        private void CheckIfDataReady()
        {
            if (imagesLoaded && locationsLoaded)
            {
                OnDataReady();
            }
        }


        private void toolStripButton_imageFolder_Click(object sender, EventArgs e)
        {
            using (var folderDialog = new FolderBrowserDialog())
            {
                folderDialog.Description = "Select a folder with images";
                folderDialog.ShowNewFolderButton = true;

                if (folderDialog.ShowDialog() == DialogResult.OK)
                {
                    imageFolderPath = folderDialog.SelectedPath;
                    // lblSelectedPath.Text = _selectedFolderPath; 
                    ReadImages(imageFolderPath);
                    CheckIfDataReady();
                }
            }
        }

        private void toolStripButton_LockationFile_Click(object sender, EventArgs e)
        {
            using (var fileDialog = new OpenFileDialog())
            {

                fileDialog.Title = "Select a File with coordinates";
                fileDialog.Filter = "XML Files (*.xml)|*.xml";


                if (fileDialog.ShowDialog() == DialogResult.OK)
                {
                    coordFilePath = fileDialog.FileName;
                    ReadXML(coordFilePath);
                    CheckIfDataReady();
                }
            }
        }

        private bool PromptForOutputPath()
        {
            using (var saveDialog = new SaveFileDialog())
            {
                saveDialog.Title = "Save Assembled Panorama";
                saveDialog.Filter = "JPEG Image (*.jpg)|*.jpg|PNG Image (*.png)|*.png|TIFF Image (*.tif)|*.tif";
                saveDialog.DefaultExt = "jpg";
                saveDialog.AddExtension = true;
                saveDialog.FileName = "assembled_output.jpg";

                if (saveDialog.ShowDialog() == DialogResult.OK)
                {
                    outputFilePath = saveDialog.FileName;
                    return true;
                }
            }
            return false;
        }

        private void saveOutput_Click(object sender, EventArgs e)
        {
            if (PromptForOutputPath())
            {
                MessageBox.Show($"Output file path set to:\n{outputFilePath}");
            }
        }
    }
}
