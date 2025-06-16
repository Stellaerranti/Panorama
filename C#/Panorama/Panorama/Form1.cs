using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Windows.Forms;
using System.Xml;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.Formats.Jpeg;

using SDImage = System.Drawing.Image;

namespace Panorama
{

    public partial class Form1 : Form
    {
        private string imageFolderPath = string.Empty;
        private string coordFilePath = string.Empty;

        List<(string, System.Drawing.Image)> images = new();
        public List<(string name, float x, float y, float z, float viewfield)> Locations { get; } = [];

        private bool imagesLoaded = false;
        private bool locationsLoaded = false;

        float globalScale = 0.25f;

        public Form1()
        {
            InitializeComponent();
        }

        private void ReadImages(string path)
        {
            foreach (string myFile in Directory.GetFiles(path, "*.jpg", SearchOption.AllDirectories))
            {
                images.Add((Path.GetFileNameWithoutExtension(myFile), System.Drawing.Image.FromFile(myFile)));
            }
            imagesLoaded = true;
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

        private SixLabors.ImageSharp.Image<Rgba32> AssembleImageSharp(string outputPath)
        {
            if (images.Count == 0 || Locations.Count == 0)
                throw new InvalidOperationException("Images or locations not loaded.");

            var nameToImage = images.ToDictionary(i => Path.GetFileNameWithoutExtension(i.Item1), i => i.Item2);

            // === STEP 1: Determine global scale from one valid image ===
            float scale = -1;
            foreach (var loc in Locations)
            {
                if (nameToImage.TryGetValue(loc.name, out var img) && img != null && loc.viewfield > 0)
                {
                    float fov_um = loc.viewfield * 1000f;
                    scale = img.Width / fov_um; // pixels per micron
                    break;
                }
            }

            if (scale <= 0)
                throw new Exception("Could not determine valid scale from image viewfield.");

            // === STEP 2: Determine spatial bounds in microns ===
            float minX_um = Locations.Min(loc => loc.x * 1000);
            float maxX_um = Locations.Max(loc => loc.x * 1000);
            float minY_um = Locations.Min(loc => loc.y * 1000);

            float maxX_px = 0, maxY_px = 0;

            foreach (var loc in Locations)
            {
                if (!nameToImage.TryGetValue(loc.name, out var img) || img == null)
                    continue;

                float x_um_flipped = maxX_um - (loc.x * 1000); // ✅ X flipped
                float y_um = (loc.y * 1000) - minY_um;

                float x_px = x_um_flipped * scale;
                float y_px = y_um * scale;

                maxX_px = Math.Max(maxX_px, x_px + img.Width);
                maxY_px = Math.Max(maxY_px, y_px + img.Height);
            }

            int origCanvasWidth = (int)Math.Ceiling(maxX_px);
            int origCanvasHeight = (int)Math.Ceiling(maxY_px);

            // === STEP 3: Adaptive downscaling if needed ===
            long totalPixels = (long)origCanvasWidth * origCanvasHeight;
            long maxSafePixels = 16000L * 16000L; // ~1 billion pixels

            float safeScale = totalPixels > maxSafePixels
                ? (float)Math.Sqrt((double)maxSafePixels / totalPixels)
                : 1.0f;

            int canvasWidth = (int)(origCanvasWidth * safeScale);
            int canvasHeight = (int)(origCanvasHeight * safeScale);

            float finalScale = scale * safeScale;

            var canvas = new SixLabors.ImageSharp.Image<Rgba32>(
                canvasWidth, canvasHeight, SixLabors.ImageSharp.Color.Black);

            // === STEP 4: Draw all tiles ===
            foreach (var loc in Locations)
            {
                if (!nameToImage.TryGetValue(loc.name, out var sysImg) || sysImg == null)
                    continue;

                using var ms = new MemoryStream();
                sysImg.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                ms.Seek(0, SeekOrigin.Begin);
                using var tile = SixLabors.ImageSharp.Image.Load<Rgba32>(ms);

                float x_um_flipped = maxX_um - (loc.x * 1000);
                float y_um = (loc.y * 1000) - minY_um;

                float x_px = x_um_flipped * finalScale;
                float y_px = y_um * finalScale;

                var resized = tile.Clone(ctx => ctx.Resize(
                    (int)(tile.Width * safeScale),
                    (int)(tile.Height * safeScale)));

                canvas.Mutate(ctx => ctx.DrawImage(resized, new SixLabors.ImageSharp.Point((int)x_px, (int)y_px), 1f));
            }

            canvas.Save(outputPath, new JpegEncoder { Quality = 95 });
            return canvas;
        }







        private void OnDataReady()
        {
            AssembleImageSharp("assembled_output.jpg");

            MessageBox.Show("Composite image assembled and saved.");
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

    }
}
