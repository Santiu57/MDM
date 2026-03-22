namespace Mari_Downloads
{
    partial class Main
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// Clean up any resources being used.
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
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            components = new System.ComponentModel.Container();
            sharpClipboard1 = new WK.Libraries.SharpClipboardNS.SharpClipboard(components);
            SuspendLayout();
            // 
            // sharpClipboard1
            // 
            sharpClipboard1.MonitorClipboard = true;
            sharpClipboard1.ObservableFormats.All = true;
            sharpClipboard1.ObservableFormats.Files = true;
            sharpClipboard1.ObservableFormats.Images = true;
            sharpClipboard1.ObservableFormats.Others = true;
            sharpClipboard1.ObservableFormats.Texts = true;
            sharpClipboard1.ObserveLastEntry = true;
            sharpClipboard1.Tag = null;
            // 
            // Main
            // 
            AutoScaleDimensions = new SizeF(8F, 20F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(800, 450);
            Name = "Main";
            Text = "Main";
            FormClosing += Main_FormClosing;
            Load += Main_Load;
            ResumeLayout(false);
        }

        #endregion

        private WK.Libraries.SharpClipboardNS.SharpClipboard sharpClipboard1;
    }
}