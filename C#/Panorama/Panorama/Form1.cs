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
                                float viewfield = float.Parse(objectNode.Attributes["viewfield"]?.Value ?? "0.4", CultureInfo.InvariantCulture);

                                // Use the average of ALL <Point> nodes as the tile CENTER.
                                var pointNodes = objectNode.SelectNodes("Point");
                                if (pointNodes == null || pointNodes.Count == 0) continue;

                                float sx = 0f, sy = 0f; int cnt = 0;
                                foreach (XmlNode pn in pointNodes)
                                {
                                    if (pn.Attributes?["x"] == null || pn.Attributes?["y"] == null) continue;
                                    sx += float.Parse(pn.Attributes["x"].Value, CultureInfo.InvariantCulture);
                                    sy += float.Parse(pn.Attributes["y"].Value, CultureInfo.InvariantCulture);
                                    cnt++;
                                }
                                if (cnt == 0) continue;

                                float x = sx / cnt;
                                float y = sy / cnt;

                                Locations.Add((name, x, y, z, viewfield));
                            }
                        }
                    }
                }
                locationsLoaded = Locations.Count > 0;
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

            // Use your field defaults
            byte bgThresholdLocal = bgThreshold;
            float featherSigmaLocal = maskFeatherSigma;

            // name -> path
            var nameToPath = images.ToDictionary(i => i.name, i => i.path, StringComparer.OrdinalIgnoreCase);

            // Determine px/µm scale from first valid tile (width pixels / viewfield µm)
            float scalePxPerUm = -1f;
            foreach (var loc in Locations)
            {
                if (loc.viewfield <= 0) continue;
                if (!nameToPath.TryGetValue(loc.name, out var pth) || !File.Exists(pth)) continue;

                int w;
                var info = SixLabors.ImageSharp.Image.Identify(pth);
                if (info != null) w = info.Width;
                else { using var tmp = SixLabors.ImageSharp.Image.Load<SixLabors.ImageSharp.PixelFormats.Rgba32>(pth); w = tmp.Width; }

                float fov_um = loc.viewfield * 1000f; // mm -> µm
                if (fov_um > 0) { scalePxPerUm = w / fov_um; break; }
            }
            if (scalePxPerUm <= 0) throw new Exception("Could not determine px/µm scale from any image's viewfield.");

            // Precompute physical rects (µm) for each tile using CENTER from Locations
            var tiles = new List<(string path, int w, int h, float left_um, float top_um, float right_um, float bottom_um)>();

            float minLeft_um = float.PositiveInfinity, minTop_um = float.PositiveInfinity;
            float maxRight_um = float.NegativeInfinity, maxBottom_um = float.NegativeInfinity;

            foreach (var loc in Locations)
            {
                if (!nameToPath.TryGetValue(loc.name, out var pth) || !File.Exists(pth)) continue;

                int w, h;
                var info = SixLabors.ImageSharp.Image.Identify(pth);
                if (info != null) { w = info.Width; h = info.Height; }
                else { using var tmp = SixLabors.ImageSharp.Image.Load<SixLabors.ImageSharp.PixelFormats.Rgba32>(pth); w = tmp.Width; h = tmp.Height; }

                float cx_um = loc.x * 1000f;
                float cy_um = loc.y * 1000f;

                float width_um = w / scalePxPerUm;   // should equal viewfield (µm)
                float height_um = h / scalePxPerUm;   // honors aspect ratio

                float left_um = cx_um - 0.5f * width_um;
                float top_um = cy_um - 0.5f * height_um;
                float right_um = left_um + width_um;
                float bottom_um = top_um + height_um;

                tiles.Add((pth, w, h, left_um, top_um, right_um, bottom_um));

                if (left_um < minLeft_um) minLeft_um = left_um;
                if (top_um < minTop_um) minTop_um = top_um;
                if (right_um > maxRight_um) maxRight_um = right_um;
                if (bottom_um > maxBottom_um) maxBottom_um = bottom_um;
            }
            if (tiles.Count == 0) throw new Exception("No tiles found for the provided XML/image set.");

            // Canvas size (in px)
            float totalWidth_um = maxRight_um - minLeft_um;
            float totalHeight_um = maxBottom_um - minTop_um;

            int origW = Math.Max(1, (int)Math.Ceiling(totalWidth_um * scalePxPerUm));
            int origH = Math.Max(1, (int)Math.Ceiling(totalHeight_um * scalePxPerUm));

            // Safety downscale for very large canvases
            long totalPx = (long)origW * origH;
            const long maxSafePx = 16000L * 16000L;
            float safeScale = totalPx > maxSafePx ? (float)Math.Sqrt((double)maxSafePx / totalPx) : 1f;

            int W = Math.Max(1, (int)(origW * safeScale));
            int H = Math.Max(1, (int)(origH * safeScale));
            float finalScalePxPerUm = scalePxPerUm * safeScale;

            var canvas = new SixLabors.ImageSharp.Image<SixLabors.ImageSharp.PixelFormats.Rgba32>(
                W, H, SixLabors.ImageSharp.Color.Black);

            int processed = 0, total = tiles.Count;

            foreach (var t in tiles)
            {
                using var tile = SixLabors.ImageSharp.Image.Load<SixLabors.ImageSharp.PixelFormats.Rgba32>(t.path);
                using (var mask = BuildAlphaMask(tile, bgThresholdLocal, featherSigmaLocal))
                    ApplyAlphaMask(tile, mask);

                int newW = Math.Max(1, (int)(t.w * safeScale));
                int newH = Math.Max(1, (int)(t.h * safeScale));
                using var resized = tile.Clone(ctx => ctx.Resize(newW, newH, SixLabors.ImageSharp.Processing.KnownResamplers.Bicubic));

                // If stage X is mirrored relative to image X, anchor by RIGHT edge:
                int x_px = (int)((maxRight_um - t.right_um) * finalScalePxPerUm);
                int y_px = (int)((t.top_um - minTop_um) * finalScalePxPerUm);

                // If NOT mirrored, use this instead:
                // int x_px = (int)((t.left_um - minLeft_um) * finalScalePxPerUm);

                canvas.Mutate(ctx => ctx.DrawImage(resized, new SixLabors.ImageSharp.Point(x_px, y_px), 1f));

                processed++;
                progress?.Report(processed);
            }

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

                // Inform + clear old inputs
                MessageBox.Show($"Composite image assembled and saved:\n{outputFilePath}");
                ClearLoadedData(clearOutputPath: false);
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

        private void ClearLoadedData(bool clearOutputPath = false)
        {
            // Clear data
            images.Clear();
            Locations.Clear();

            // Reset paths
            imageFolderPath = string.Empty;
            coordFilePath = string.Empty;

            // Reset flags
            imagesLoaded = false;
            locationsLoaded = false;

            // Optionally reset output file path
            if (clearOutputPath)
                outputFilePath = string.Empty;
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
