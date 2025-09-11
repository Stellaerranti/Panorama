namespace Panorama
{
    partial class Form1
    {
        /// <summary>
        ///  Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        ///  Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        ///  Required method for Designer support - do not modify
        ///  the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(Form1));
            toolStrip = new ToolStrip();
            toolStripButton_imageFolder = new ToolStripButton();
            toolStripButton_LockationFile = new ToolStripButton();
            saveOutput = new ToolStripButton();
            toolStripProgressBar1 = new ToolStripProgressBar();
            panel1 = new Panel();
            BlackCanvasCheck = new CheckBox();
            LoadXMLButton = new Button();
            ImageLoadButton = new Button();
            toolStrip.SuspendLayout();
            panel1.SuspendLayout();
            SuspendLayout();
            // 
            // toolStrip
            // 
            toolStrip.Items.AddRange(new ToolStripItem[] { toolStripButton_imageFolder, toolStripButton_LockationFile, saveOutput, toolStripProgressBar1 });
            toolStrip.Location = new Point(0, 0);
            toolStrip.Name = "toolStrip";
            toolStrip.Size = new Size(445, 25);
            toolStrip.TabIndex = 0;
            toolStrip.Text = "toolStrip";
            // 
            // toolStripButton_imageFolder
            // 
            toolStripButton_imageFolder.DisplayStyle = ToolStripItemDisplayStyle.Text;
            toolStripButton_imageFolder.Image = (Image)resources.GetObject("toolStripButton_imageFolder.Image");
            toolStripButton_imageFolder.ImageTransparentColor = Color.Magenta;
            toolStripButton_imageFolder.Name = "toolStripButton_imageFolder";
            toolStripButton_imageFolder.Size = new Size(78, 22);
            toolStripButton_imageFolder.Text = "Image folder";
            toolStripButton_imageFolder.Visible = false;
            toolStripButton_imageFolder.Click += toolStripButton_imageFolder_Click;
            // 
            // toolStripButton_LockationFile
            // 
            toolStripButton_LockationFile.DisplayStyle = ToolStripItemDisplayStyle.Text;
            toolStripButton_LockationFile.Image = (Image)resources.GetObject("toolStripButton_LockationFile.Image");
            toolStripButton_LockationFile.ImageTransparentColor = Color.Magenta;
            toolStripButton_LockationFile.Name = "toolStripButton_LockationFile";
            toolStripButton_LockationFile.Size = new Size(54, 22);
            toolStripButton_LockationFile.Text = "XML file";
            toolStripButton_LockationFile.Visible = false;
            toolStripButton_LockationFile.Click += toolStripButton_LockationFile_Click;
            // 
            // saveOutput
            // 
            saveOutput.DisplayStyle = ToolStripItemDisplayStyle.Text;
            saveOutput.Image = (Image)resources.GetObject("saveOutput.Image");
            saveOutput.ImageTransparentColor = Color.Magenta;
            saveOutput.Name = "saveOutput";
            saveOutput.Size = new Size(35, 22);
            saveOutput.Text = "Save";
            saveOutput.Visible = false;
            saveOutput.Click += saveOutput_Click;
            // 
            // toolStripProgressBar1
            // 
            toolStripProgressBar1.Name = "toolStripProgressBar1";
            toolStripProgressBar1.Size = new Size(100, 22);
            // 
            // panel1
            // 
            panel1.AutoScroll = true;
            panel1.Controls.Add(BlackCanvasCheck);
            panel1.Controls.Add(LoadXMLButton);
            panel1.Controls.Add(ImageLoadButton);
            panel1.Dock = DockStyle.Fill;
            panel1.Location = new Point(0, 25);
            panel1.Name = "panel1";
            panel1.Size = new Size(445, 64);
            panel1.TabIndex = 1;
            // 
            // BlackCanvasCheck
            // 
            BlackCanvasCheck.AutoSize = true;
            BlackCanvasCheck.Location = new Point(96, 6);
            BlackCanvasCheck.Name = "BlackCanvasCheck";
            BlackCanvasCheck.Size = new Size(96, 19);
            BlackCanvasCheck.TabIndex = 2;
            BlackCanvasCheck.Text = "Тёмный фон";
            BlackCanvasCheck.UseVisualStyleBackColor = true;
            // 
            // LoadXMLButton
            // 
            LoadXMLButton.Location = new Point(3, 32);
            LoadXMLButton.Name = "LoadXMLButton";
            LoadXMLButton.Size = new Size(87, 23);
            LoadXMLButton.TabIndex = 1;
            LoadXMLButton.Text = "XML file";
            LoadXMLButton.UseVisualStyleBackColor = true;
            LoadXMLButton.Click += LoadXMLButton_Click;
            // 
            // ImageLoadButton
            // 
            ImageLoadButton.Location = new Point(3, 3);
            ImageLoadButton.Name = "ImageLoadButton";
            ImageLoadButton.Size = new Size(87, 23);
            ImageLoadButton.TabIndex = 0;
            ImageLoadButton.Text = "Image folder";
            ImageLoadButton.UseVisualStyleBackColor = true;
            ImageLoadButton.Click += button1_Click;
            // 
            // Form1
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(445, 89);
            Controls.Add(panel1);
            Controls.Add(toolStrip);
            Name = "Form1";
            Text = "Panorama maker for Tescan, IPE RAS, ver. 08Sep2025";
            toolStrip.ResumeLayout(false);
            toolStrip.PerformLayout();
            panel1.ResumeLayout(false);
            panel1.PerformLayout();
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion
        private ToolStrip toolStrip;
        private ToolStripButton toolStripButton_imageFolder;
        private ToolStripButton toolStripButton_LockationFile;
        private Panel panel1;
        private ToolStripButton saveOutput;
        private ToolStripProgressBar toolStripProgressBar1;
        private Button LoadXMLButton;
        private Button ImageLoadButton;
        private CheckBox BlackCanvasCheck;
    }
}
