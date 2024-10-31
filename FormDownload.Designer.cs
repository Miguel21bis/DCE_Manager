namespace DCE_Manager
{
    partial class FormDownload
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        public System.ComponentModel.IContainer components = null;

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
        public void InitializeComponent()
        {
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(FormDownload));
            this.progressBarB = new System.Windows.Forms.ProgressBar();
            this.button1_OK = new System.Windows.Forms.Button();
            this.label1Form2 = new System.Windows.Forms.Label();
            this.progressBarA = new System.Windows.Forms.ProgressBar();
            this.label2Form2 = new System.Windows.Forms.Label();
            this.button_Cancel = new System.Windows.Forms.Button();
            this.progressBarC = new System.Windows.Forms.ProgressBar();
            this.labelError = new System.Windows.Forms.Label();
            this.labelNameFile = new System.Windows.Forms.Label();
            this.SuspendLayout();
            // 
            // progressBarB
            // 
            this.progressBarB.Location = new System.Drawing.Point(58, 93);
            this.progressBarB.Name = "progressBarB";
            this.progressBarB.Size = new System.Drawing.Size(467, 23);
            this.progressBarB.TabIndex = 0;
            this.progressBarB.Visible = false;
            this.progressBarB.Click += new System.EventHandler(this.progressBar1_Click);
            // 
            // button1_OK
            // 
            this.button1_OK.Location = new System.Drawing.Point(332, 151);
            this.button1_OK.Name = "button1_OK";
            this.button1_OK.Size = new System.Drawing.Size(75, 23);
            this.button1_OK.TabIndex = 1;
            this.button1_OK.Text = "OK";
            this.button1_OK.UseVisualStyleBackColor = true;
            this.button1_OK.Visible = false;
            this.button1_OK.Click += new System.EventHandler(this.button1_OK_Click);
            // 
            // label1Form2
            // 
            this.label1Form2.AutoSize = true;
            this.label1Form2.Location = new System.Drawing.Point(136, 53);
            this.label1Form2.Name = "label1Form2";
            this.label1Form2.Size = new System.Drawing.Size(16, 13);
            this.label1Form2.TabIndex = 2;
            this.label1Form2.Text = "...";
            this.label1Form2.Visible = false;
            this.label1Form2.Click += new System.EventHandler(this.label1_Click);
            // 
            // progressBarA
            // 
            this.progressBarA.Location = new System.Drawing.Point(58, 53);
            this.progressBarA.Name = "progressBarA";
            this.progressBarA.Size = new System.Drawing.Size(467, 23);
            this.progressBarA.TabIndex = 3;
            this.progressBarA.Visible = false;
            // 
            // label2Form2
            // 
            this.label2Form2.AutoSize = true;
            this.label2Form2.Location = new System.Drawing.Point(136, 93);
            this.label2Form2.Name = "label2Form2";
            this.label2Form2.Size = new System.Drawing.Size(16, 13);
            this.label2Form2.TabIndex = 4;
            this.label2Form2.Text = "...";
            this.label2Form2.Visible = false;
            // 
            // button_Cancel
            // 
            this.button_Cancel.Location = new System.Drawing.Point(450, 151);
            this.button_Cancel.Name = "button_Cancel";
            this.button_Cancel.Size = new System.Drawing.Size(75, 23);
            this.button_Cancel.TabIndex = 5;
            this.button_Cancel.Text = "Cancel";
            this.button_Cancel.UseVisualStyleBackColor = true;
            this.button_Cancel.Visible = false;
            this.button_Cancel.Click += new System.EventHandler(this.button_Cancel_Click);
            // 
            // progressBarC
            // 
            this.progressBarC.BackColor = System.Drawing.SystemColors.Window;
            this.progressBarC.ForeColor = System.Drawing.SystemColors.GrayText;
            this.progressBarC.Location = new System.Drawing.Point(58, 12);
            this.progressBarC.Name = "progressBarC";
            this.progressBarC.RightToLeft = System.Windows.Forms.RightToLeft.Yes;
            this.progressBarC.RightToLeftLayout = true;
            this.progressBarC.Size = new System.Drawing.Size(467, 23);
            this.progressBarC.TabIndex = 6;
            this.progressBarC.Visible = false;
            // 
            // labelError
            // 
            this.labelError.AutoSize = true;
            this.labelError.Location = new System.Drawing.Point(531, 12);
            this.labelError.Name = "labelError";
            this.labelError.Size = new System.Drawing.Size(29, 13);
            this.labelError.TabIndex = 7;
            this.labelError.Text = "Error";
            this.labelError.Visible = false;
            // 
            // labelNameFile
            // 
            this.labelNameFile.AutoSize = true;
            this.labelNameFile.Location = new System.Drawing.Point(65, 135);
            this.labelNameFile.Name = "labelNameFile";
            this.labelNameFile.Size = new System.Drawing.Size(0, 13);
            this.labelNameFile.TabIndex = 8;
            this.labelNameFile.Visible = false;
            // 
            // Form2
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(583, 186);
            this.Controls.Add(this.labelNameFile);
            this.Controls.Add(this.labelError);
            this.Controls.Add(this.progressBarC);
            this.Controls.Add(this.button_Cancel);
            this.Controls.Add(this.label2Form2);
            this.Controls.Add(this.progressBarA);
            this.Controls.Add(this.label1Form2);
            this.Controls.Add(this.button1_OK);
            this.Controls.Add(this.progressBarB);
            this.Icon = ((System.Drawing.Icon)(resources.GetObject("$this.Icon")));
            this.Name = "Form2";
            this.Text = "Progress";
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        public System.Windows.Forms.ProgressBar progressBarB;
        public System.Windows.Forms.Button button1_OK;
        public System.Windows.Forms.Label label1Form2;
        public System.Windows.Forms.ProgressBar progressBarA;
        public System.Windows.Forms.Label label2Form2;
        public System.Windows.Forms.Button button_Cancel;
        public System.Windows.Forms.ProgressBar progressBarC;
        public System.Windows.Forms.Label labelError;
        public System.Windows.Forms.Label labelNameFile;
    }
}