using System.Globalization;
using System.IO;
using System.Windows.Forms;
using System.Xml;
using System.Xml.Linq;

namespace Panorama
{

    public partial class Form1 : Form
    {
        private string imageFolderPath = string.Empty;
        private string coordFilePath = string.Empty;

        List<(string, Image)> images = new();
        public List<(string name, float x, float y, float z)> Locations { get; } = [];

        private bool imagesLoaded = false;
        private bool locationsLoaded = false;

        public Form1()
        {
            InitializeComponent();
        }

        private void ReadImages(string path)
        {
            foreach (string myFile in Directory.GetFiles(path, "*.jpg", SearchOption.AllDirectories))
            {
                images.Add((Path.GetFileNameWithoutExtension(myFile), Image.FromFile(myFile)));
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

                                Locations.Add((name, x, y, z));
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

        private Bitmap AssembleCompositeImage()
        {
            if (images.Count == 0 || Locations.Count == 0)
                throw new InvalidOperationException("Images or locations are not loaded.");

            // Match images and their locations by name
            var nameToImage = images.ToDictionary(i => i.Item1, i => i.Item2);

            // Find bounds for the canvas
            float maxX = 0, maxY = 0;
            foreach (var loc in Locations)
            {
                if (nameToImage.TryGetValue(loc.name, out var img))
                {
                    float right = loc.x + img.Width;
                    float bottom = loc.y + img.Height;

                    if (right > maxX) maxX = right;
                    if (bottom > maxY) maxY = bottom;
                }
            }

            int canvasWidth = (int)Math.Ceiling(maxX);
            int canvasHeight = (int)Math.Ceiling(maxY);
            Bitmap canvas = new Bitmap(canvasWidth, canvasHeight);

            using (Graphics g = Graphics.FromImage(canvas))
            {
                g.Clear(Color.Black); // or Color.Transparent

                foreach (var loc in Locations)
                {
                    if (nameToImage.TryGetValue(loc.name, out var img))
                    {
                        g.DrawImage(img, loc.x, loc.y);
                    }
                }
            }

            return canvas;
        }

        private void OnDataReady()
        {
            Bitmap assembled = AssembleCompositeImage();

            pictureBox1.Image?.Dispose(); // dispose previous image to free memory
            pictureBox1.Image = assembled;
            pictureBox1.Size = assembled.Size; // important for scrollbars to appear

            MessageBox.Show("Composite image assembled.");
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
