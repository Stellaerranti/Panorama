﻿namespace Panorama
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
            panel1 = new Panel();
            pictureBox1 = new PictureBox();
            toolStrip.SuspendLayout();
            panel1.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)pictureBox1).BeginInit();
            SuspendLayout();
            // 
            // toolStrip
            // 
            toolStrip.Items.AddRange(new ToolStripItem[] { toolStripButton_imageFolder, toolStripButton_LockationFile });
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
            toolStripButton_LockationFile.Click += toolStripButton_LockationFile_Click;
            // 
            // panel1
            // 
            panel1.AutoScroll = true;
            panel1.Controls.Add(pictureBox1);
            panel1.Dock = DockStyle.Fill;
            panel1.Location = new Point(0, 25);
            panel1.Name = "panel1";
            panel1.Size = new Size(445, 254);
            panel1.TabIndex = 1;
            // 
            // pictureBox1
            // 
            pictureBox1.Location = new Point(0, 0);
            pictureBox1.Name = "pictureBox1";
            pictureBox1.Size = new Size(366, 197);
            pictureBox1.SizeMode = PictureBoxSizeMode.AutoSize;
            pictureBox1.TabIndex = 0;
            pictureBox1.TabStop = false;
            // 
            // Form1
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(445, 279);
            Controls.Add(panel1);
            Controls.Add(toolStrip);
            Name = "Form1";
            Text = "Panorama 0.1";
            toolStrip.ResumeLayout(false);
            toolStrip.PerformLayout();
            panel1.ResumeLayout(false);
            panel1.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)pictureBox1).EndInit();
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion
        private ToolStrip toolStrip;
        private ToolStripButton toolStripButton_imageFolder;
        private ToolStripButton toolStripButton_LockationFile;
        private Panel panel1;
        private PictureBox pictureBox1;
    }
}
