using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Windows.Forms;
using System.Xml;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Text;

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

        private float overlapFraction = 0f;

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

                                    if (overlapFraction == 0f && objectNode.Attributes?["overlapping"] != null)
                                    {
                                        if (float.TryParse(objectNode.Attributes["overlapping"].Value,
                                                           NumberStyles.Float, CultureInfo.InvariantCulture, out var ov))
                                        {
                                            overlapFraction = ov > 1f ? ov / 100f : ov; // "10" -> 0.10
                                        }
                                    }
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
            if (images == null || images.Count == 0)
                throw new InvalidOperationException("No images loaded.");
            if (Locations == null || Locations.Count == 0)
                throw new InvalidOperationException("No locations loaded.");

            // You can force a choice if you like; otherwise the code will auto-pick below.
            bool preferMirroredX = true; // set to false if your stage is not mirrored

            byte bgThresholdLocal = bgThreshold;
            float featherSigmaLocal = maskFeatherSigma;

            var nameToPath = images.ToDictionary(i => i.name, i => i.path, StringComparer.OrdinalIgnoreCase);

            // ---- 1) px/µm scale from full FOV (ignore overlap for scale) ----
            double scalePxPerUm = -1;
            foreach (var loc in Locations)
            {
                if (loc.viewfield <= 0) continue;
                if (!nameToPath.TryGetValue(loc.name, out var pth) || !File.Exists(pth)) continue;

                int w;
                var info = SixLabors.ImageSharp.Image.Identify(pth);
                if (info != null) w = info.Width;
                else { using var tmp = SixLabors.ImageSharp.Image.Load<SixLabors.ImageSharp.PixelFormats.Rgba32>(pth); w = tmp.Width; }

                double fov_um = loc.viewfield * 1000.0; // mm -> µm
                if (fov_um > 0) { scalePxPerUm = w / fov_um; break; }
            }
            if (scalePxPerUm <= 0)
                throw new Exception("Could not determine px/µm scale from any image/viewfield.");

            // ---- 2) Build tile rects (µm) using CENTER coords ----
            var tiles = new List<(string path, int w, int h, double left_um, double top_um, double right_um, double bottom_um)>();
            double minLeft_um = double.PositiveInfinity, minTop_um = double.PositiveInfinity;
            double maxRight_um = double.NegativeInfinity, maxBottom_um = double.NegativeInfinity;

            foreach (var loc in Locations)
            {
                if (!nameToPath.TryGetValue(loc.name, out var pth) || !File.Exists(pth)) continue;

                int w, h;
                var info = SixLabors.ImageSharp.Image.Identify(pth);
                if (info != null) { w = info.Width; h = info.Height; }
                else { using var tmp = SixLabors.ImageSharp.Image.Load<SixLabors.ImageSharp.PixelFormats.Rgba32>(pth); w = tmp.Width; h = tmp.Height; }

                double cx_um = loc.x * 1000.0;
                double cy_um = loc.y * 1000.0;

                double width_um = w / scalePxPerUm;
                double height_um = h / scalePxPerUm;

                double left_um = cx_um - 0.5 * width_um;
                double top_um = cy_um - 0.5 * height_um;
                double right_um = left_um + width_um;
                double bottom_um = top_um + height_um;

                tiles.Add((pth, w, h, left_um, top_um, right_um, bottom_um));

                if (left_um < minLeft_um) minLeft_um = left_um;
                if (top_um < minTop_um) minTop_um = top_um;
                if (right_um > maxRight_um) maxRight_um = right_um;
                if (bottom_um > maxBottom_um) maxBottom_um = bottom_um;
            }
            if (tiles.Count == 0)
                throw new Exception("No tiles found that match the XML Locations.");

            // Mirror axis = mosaic midline (this keeps mirrored coords inside the same bounds)
            double mirrorAxis_um = (minLeft_um + maxRight_um) / 2.0;

            // ---- 3) Canvas size + safety downscale ----
            double totalWidth_um = maxRight_um - minLeft_um;
            double totalHeight_um = maxBottom_um - minTop_um;

            int origW = Math.Max(1, (int)Math.Ceiling(totalWidth_um * scalePxPerUm));
            int origH = Math.Max(1, (int)Math.Ceiling(totalHeight_um * scalePxPerUm));

            long totalPx = (long)origW * (long)origH;
            const long maxSafePx = 16000L * 16000L;
            double safeScale = totalPx > maxSafePx ? Math.Sqrt((double)maxSafePx / totalPx) : 1.0;

            int W = Math.Max(1, (int)Math.Round(origW * safeScale));
            int H = Math.Max(1, (int)Math.Round(origH * safeScale));
            double finalScalePxPerUm = scalePxPerUm * safeScale;

            var canvas = new SixLabors.ImageSharp.Image<SixLabors.ImageSharp.PixelFormats.Rgba32>(
                W, H, SixLabors.ImageSharp.Color.Black);

            // ---- 4) Decide orientation (auto-pick) ----
            // Compute how far tiles would overflow the canvas for both orientations and pick the safer one.
            Func<bool, double> totalOverflow = mirrored =>
            {
                double overflow = 0;
                foreach (var t in tiles)
                {
                    int newW = Math.Max(1, (int)Math.Round(t.w * safeScale));
                    double left_um = mirrored ? (2.0 * mirrorAxis_um - t.right_um) : t.left_um;
                    int x_px = (int)Math.Round((left_um - minLeft_um) * finalScalePxPerUm);
                    int leftOverflow = Math.Max(0, -x_px);
                    int rightOverflow = Math.Max(0, (x_px + newW) - W);
                    overflow += leftOverflow + rightOverflow;
                }
                return overflow;
            };

            bool useMirroredX;
            double ovMir = totalOverflow(true);
            double ovNorm = totalOverflow(false);

            if (ovMir == 0 && ovNorm == 0)
                useMirroredX = preferMirroredX;    // both fit → respect preference
            else if (ovMir == 0)
                useMirroredX = true;               // only mirrored fits
            else if (ovNorm == 0)
                useMirroredX = false;              // only normal fits
            else
                useMirroredX = ovMir < ovNorm;     // pick the one with less overflow

            // ---- 5) Composite with rounded integer placement ----
            int processed = 0, total = tiles.Count;

            foreach (var t in tiles)
            {
                using var tile = SixLabors.ImageSharp.Image.Load<SixLabors.ImageSharp.PixelFormats.Rgba32>(t.path);

                using (var mask = BuildAlphaMask(tile, bgThresholdLocal, featherSigmaLocal))
                    ApplyAlphaMask(tile, mask);

                int newW = Math.Max(1, (int)Math.Round(t.w * safeScale));
                int newH = Math.Max(1, (int)Math.Round(t.h * safeScale));
                using var resized = tile.Clone(ctx => ctx.Resize(newW, newH, SixLabors.ImageSharp.Processing.KnownResamplers.Bicubic));

                double left_um = useMirroredX ? (2.0 * mirrorAxis_um - t.right_um) : t.left_um;
                int x_px = (int)Math.Round((left_um - minLeft_um) * finalScalePxPerUm);
                int y_px = (int)Math.Round((t.top_um - minTop_um) * finalScalePxPerUm);

                canvas.Mutate(ctx => ctx.DrawImage(resized, new SixLabors.ImageSharp.Point(x_px, y_px), 1f));

                processed++;
                progress?.Report(processed);
            }

            // ---- 6) Save ----
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

        private static string ToBase64Png(SixLabors.ImageSharp.Image<SixLabors.ImageSharp.PixelFormats.Rgba32> img)
        {
            using var ms = new MemoryStream();
            var png = new SixLabors.ImageSharp.Formats.Png.PngEncoder(); // use defaults (keeps alpha)
            img.Save(ms, png);
            return Convert.ToBase64String(ms.ToArray());
        }

        private void AssembleSvg(string outputPath, IProgress<int> progress = null)
        {
            if (images == null || images.Count == 0)
                throw new InvalidOperationException("No images loaded.");
            if (Locations == null || Locations.Count == 0)
                throw new InvalidOperationException("No locations loaded.");

            // ---- Settings ----
            const bool EMBED = true; // set to false to link external PNGs next to the SVG
            bool preferMirroredX = true;

            byte bgThresholdLocal = bgThreshold;
            float featherSigmaLocal = maskFeatherSigma;

            var nameToPath = images.ToDictionary(i => i.name, i => i.path, StringComparer.OrdinalIgnoreCase);

            // ---- 1) px/µm scale from full FOV ----
            double scalePxPerUm = -1;
            foreach (var loc in Locations)
            {
                if (loc.viewfield <= 0) continue;
                if (!nameToPath.TryGetValue(loc.name, out var pth) || !File.Exists(pth)) continue;

                int w;
                var info = SixLabors.ImageSharp.Image.Identify(pth);
                if (info != null) w = info.Width;
                else { using var tmp = SixLabors.ImageSharp.Image.Load<Rgba32>(pth); w = tmp.Width; }

                double fov_um = loc.viewfield * 1000.0; // mm -> µm
                if (fov_um > 0) { scalePxPerUm = w / fov_um; break; }
            }
            if (scalePxPerUm <= 0)
                throw new Exception("Could not determine px/µm scale from any image/viewfield.");

            // ---- 2) Build tile rects (µm) using CENTER coords ----
            var tiles = new List<(string name, string path, int w, int h, double left_um, double top_um, double right_um, double bottom_um)>();
            double minLeft_um = double.PositiveInfinity, minTop_um = double.PositiveInfinity;
            double maxRight_um = double.NegativeInfinity, maxBottom_um = double.NegativeInfinity;

            foreach (var loc in Locations)
            {
                if (!nameToPath.TryGetValue(loc.name, out var pth) || !File.Exists(pth)) continue;

                int w, h;
                var info = SixLabors.ImageSharp.Image.Identify(pth);
                if (info != null) { w = info.Width; h = info.Height; }
                else { using var tmp = SixLabors.ImageSharp.Image.Load<Rgba32>(pth); w = tmp.Width; h = tmp.Height; }

                double cx_um = loc.x * 1000.0;
                double cy_um = loc.y * 1000.0;

                double width_um = w / scalePxPerUm;
                double height_um = h / scalePxPerUm;

                double left_um = cx_um - 0.5 * width_um;
                double top_um = cy_um - 0.5 * height_um;
                double right_um = left_um + width_um;
                double bottom_um = top_um + height_um;

                tiles.Add((loc.name, pth, w, h, left_um, top_um, right_um, bottom_um));

                if (left_um < minLeft_um) minLeft_um = left_um;
                if (top_um < minTop_um) minTop_um = top_um;
                if (right_um > maxRight_um) maxRight_um = right_um;
                if (bottom_um > maxBottom_um) maxBottom_um = bottom_um;
            }
            if (tiles.Count == 0)
                throw new Exception("No tiles found that match the XML Locations.");

            // Mirror axis = mosaic midline
            double mirrorAxis_um = (minLeft_um + maxRight_um) / 2.0;

            // ---- 3) Canvas size + safety downscale ----
            double totalWidth_um = maxRight_um - minLeft_um;
            double totalHeight_um = maxBottom_um - minTop_um;

            int origW = Math.Max(1, (int)Math.Ceiling(totalWidth_um * scalePxPerUm));
            int origH = Math.Max(1, (int)Math.Ceiling(totalHeight_um * scalePxPerUm));

            long totalPx = (long)origW * (long)origH;
            const long maxSafePx = 16000L * 16000L;
            double safeScale = totalPx > maxSafePx ? Math.Sqrt((double)maxSafePx / totalPx) : 1.0;

            int W = Math.Max(1, (int)Math.Round(origW * safeScale));
            int H = Math.Max(1, (int)Math.Round(origH * safeScale));
            double finalScalePxPerUm = scalePxPerUm * safeScale;

            // ---- 4) Decide orientation (auto-pick) ----
            Func<bool, double> totalOverflow = mirrored =>
            {
                double overflow = 0;
                foreach (var t in tiles)
                {
                    int newW = Math.Max(1, (int)Math.Round(t.w * safeScale));
                    double left_um = mirrored ? (2.0 * mirrorAxis_um - t.right_um) : t.left_um;
                    int x_px = (int)Math.Round((left_um - minLeft_um) * finalScalePxPerUm);
                    int leftOverflow = Math.Max(0, -x_px);
                    int rightOverflow = Math.Max(0, (x_px + newW) - W);
                    overflow += leftOverflow + rightOverflow;
                }
                return overflow;
            };

            bool useMirroredX;
            double ovMir = totalOverflow(true);
            double ovNorm = totalOverflow(false);

            if (ovMir == 0 && ovNorm == 0) useMirroredX = preferMirroredX;
            else if (ovMir == 0) useMirroredX = true;
            else if (ovNorm == 0) useMirroredX = false;
            else useMirroredX = ovMir < ovNorm;

            // ---- 5) Prepare I/O if linking external PNGs ----
            string svgDir = Path.GetDirectoryName(outputPath) ?? Environment.CurrentDirectory;
            string tileDir = Path.Combine(svgDir, Path.GetFileNameWithoutExtension(outputPath) + "_tiles");
            if (!EMBED)
                Directory.CreateDirectory(tileDir);

            // ---- 6) Build SVG with per-tile <image> elements ----
            var sb = new StringBuilder();

            // Add both href forms for compatibility (xlink + modern)
            sb.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"no\"?>");
            sb.AppendLine($"<svg xmlns=\"http://www.w3.org/2000/svg\" xmlns:xlink=\"http://www.w3.org/1999/xlink\" version=\"1.1\" width=\"{W}\" height=\"{H}\" viewBox=\"0 0 {W} {H}\">");
            sb.AppendLine("  <desc>Editable panorama. Each tile is a separate PNG with alpha; move/adjust tiles if needed.</desc>");
            // background is click-through
            if (BlackCanvasCheck.Checked) { sb.AppendLine("  <rect x=\"0\" y=\"0\" width=\"100%\" height=\"100%\" fill=\"black\" style=\"pointer-events:none\"/>"); }
            //

            int processed = 0, total = tiles.Count;
            foreach (var t in tiles)
            {
                using var tile = SixLabors.ImageSharp.Image.Load<Rgba32>(t.path);
                using (var mask = BuildAlphaMask(tile, bgThresholdLocal, featherSigmaLocal))
                    ApplyAlphaMask(tile, mask);

                int newW = Math.Max(1, (int)Math.Round(t.w * safeScale));
                int newH = Math.Max(1, (int)Math.Round(t.h * safeScale));
                using var resized = tile.Clone(ctx => ctx.Resize(newW, newH, KnownResamplers.Bicubic));

                double left_um = useMirroredX ? (2.0 * mirrorAxis_um - t.right_um) : t.left_um;
                int x_px = (int)Math.Round((left_um - minLeft_um) * finalScalePxPerUm);
                int y_px = (int)Math.Round((t.top_um - minTop_um) * finalScalePxPerUm);

                string hrefVal;
                if (EMBED)
                {
                    // embed as base64 data URIs
                    hrefVal = "data:image/png;base64," + ToBase64Png(resized);
                }
                else
                {
                    // save external PNGs next to the SVG (smaller SVG, still fully editable)
                    string safeName = string.Join("_", t.name.Split(Path.GetInvalidFileNameChars()));
                    string tileFile = Path.Combine(tileDir, $"{safeName}_{processed + 1}.png");
                    using (var fs = File.Open(tileFile, FileMode.Create, FileAccess.Write))
                    {
                        resized.Save(fs, new SixLabors.ImageSharp.Formats.Png.PngEncoder());
                    }
                    hrefVal = Uri.EscapeUriString(Path.Combine(Path.GetFileName(tileDir), Path.GetFileName(tileFile)).Replace('\\', '/'));
                }

                string id = $"tile_{processed + 1}_{System.Security.Cryptography.SHA1.HashData(Encoding.UTF8.GetBytes(t.name)).Take(4).Aggregate(0, (acc, b) => (acc << 1) ^ b):X}";
                sb.AppendLine($"  <g id=\"{id}\" data-name=\"{System.Security.SecurityElement.Escape(t.name)}\">");
                sb.AppendLine($"    <title>{System.Security.SecurityElement.Escape(t.name)}</title>");
                // Provide both href and xlink:href for compatibility
                sb.AppendLine($"    <image x=\"{x_px}\" y=\"{y_px}\" width=\"{newW}\" height=\"{newH}\" href=\"{hrefVal}\" xlink:href=\"{hrefVal}\" preserveAspectRatio=\"none\" />");
                sb.AppendLine("  </g>");

                processed++;
                progress?.Report(processed);
            }

            sb.AppendLine("</svg>");
            File.WriteAllText(outputPath, sb.ToString(), new UTF8Encoding(false));
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
                await Task.Run(() =>
                {
                    string ext = System.IO.Path.GetExtension(outputFilePath)?.ToLowerInvariant();
                    if (ext == ".svg")
                    {
                        AssembleSvg(outputFilePath, progress);
                    }
                    else
                    {
                        AssembleImageSharp(outputFilePath, progress).Dispose();
                    }
                });

                // Inform + clear old inputs
                MessageBox.Show($"Composite image assembled and saved:\n{outputFilePath}");
                ClearLoadedData(clearOutputPath: true);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Assembly failed: " + ex.Message);
            }
            finally
            {
                // Reset/hide the progress bar
                toolStripProgressBar1.Value = 0;
                //toolStripProgressBar1.Visible = false;
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
                saveDialog.Filter = "SVG (editable; tiles preserved) (*.svg)|*.svg|JPEG Image (*.jpg)|*.jpg|PNG Image (*.png)|*.png|TIFF Image (*.tif)|*.tif";
                saveDialog.DefaultExt = "svg";
                saveDialog.AddExtension = true;
                saveDialog.FileName = "assembled_output.svg";

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

        private void button1_Click(object sender, EventArgs e)
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

        private void LoadXMLButton_Click(object sender, EventArgs e)
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
    }
}
