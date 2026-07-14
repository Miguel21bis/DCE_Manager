namespace DCE_Manager
{
    partial class ASTI_Form
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
            this.groupBox_staticTemplate = new System.Windows.Forms.GroupBox();
            this.but_GPS_LL = new System.Windows.Forms.Button();
            this.but_ASTI_Open_templateFolder = new System.Windows.Forms.Button();
            this.but_ASTI_Process = new System.Windows.Forms.Button();
            this.but_ASTI_Browse_MissionFile = new System.Windows.Forms.Button();
            this.but_ASTI_Browse_Template = new System.Windows.Forms.Button();
            this.textBox_ASTI_MissionFile = new System.Windows.Forms.TextBox();
            this.textBox_ASTI_importTemplateFolder = new System.Windows.Forms.TextBox();
            this.groupBox_staticTemplate.SuspendLayout();
            this.SuspendLayout();
            // 
            // groupBox_staticTemplate
            // 
            this.groupBox_staticTemplate.Controls.Add(this.but_GPS_LL);
            this.groupBox_staticTemplate.Controls.Add(this.but_ASTI_Open_templateFolder);
            this.groupBox_staticTemplate.Controls.Add(this.but_ASTI_Process);
            this.groupBox_staticTemplate.Controls.Add(this.but_ASTI_Browse_MissionFile);
            this.groupBox_staticTemplate.Controls.Add(this.but_ASTI_Browse_Template);
            this.groupBox_staticTemplate.Controls.Add(this.textBox_ASTI_MissionFile);
            this.groupBox_staticTemplate.Controls.Add(this.textBox_ASTI_importTemplateFolder);
            this.groupBox_staticTemplate.Location = new System.Drawing.Point(190, 37);
            this.groupBox_staticTemplate.Name = "groupBox_staticTemplate";
            this.groupBox_staticTemplate.Size = new System.Drawing.Size(420, 377);
            this.groupBox_staticTemplate.TabIndex = 3;
            this.groupBox_staticTemplate.TabStop = false;
            this.groupBox_staticTemplate.Text = "ASTI";
            this.groupBox_staticTemplate.Visible = false;
            // 
            // but_GPS_LL
            // 
            this.but_GPS_LL.Location = new System.Drawing.Point(44, 242);
            this.but_GPS_LL.Name = "but_GPS_LL";
            this.but_GPS_LL.Size = new System.Drawing.Size(97, 25);
            this.but_GPS_LL.TabIndex = 8;
            this.but_GPS_LL.Text = "Test_LL";
            this.but_GPS_LL.UseVisualStyleBackColor = true;
            this.but_GPS_LL.Visible = false;
            // 
            // but_ASTI_Open_templateFolder
            // 
            this.but_ASTI_Open_templateFolder.Location = new System.Drawing.Point(252, 34);
            this.but_ASTI_Open_templateFolder.Name = "but_ASTI_Open_templateFolder";
            this.but_ASTI_Open_templateFolder.Size = new System.Drawing.Size(114, 25);
            this.but_ASTI_Open_templateFolder.TabIndex = 7;
            this.but_ASTI_Open_templateFolder.Text = "Open folder";
            this.but_ASTI_Open_templateFolder.UseVisualStyleBackColor = true;
            this.but_ASTI_Open_templateFolder.Visible = false;
            // 
            // but_ASTI_Process
            // 
            this.but_ASTI_Process.Location = new System.Drawing.Point(185, 326);
            this.but_ASTI_Process.Name = "but_ASTI_Process";
            this.but_ASTI_Process.Size = new System.Drawing.Size(181, 25);
            this.but_ASTI_Process.TabIndex = 4;
            this.but_ASTI_Process.Text = "Process";
            this.but_ASTI_Process.UseVisualStyleBackColor = true;
            // 
            // but_ASTI_Browse_MissionFile
            // 
            this.but_ASTI_Browse_MissionFile.Location = new System.Drawing.Point(44, 140);
            this.but_ASTI_Browse_MissionFile.Name = "but_ASTI_Browse_MissionFile";
            this.but_ASTI_Browse_MissionFile.Size = new System.Drawing.Size(181, 25);
            this.but_ASTI_Browse_MissionFile.TabIndex = 3;
            this.but_ASTI_Browse_MissionFile.Text = "Select Mission file (.miz)";
            this.but_ASTI_Browse_MissionFile.UseVisualStyleBackColor = true;
            // 
            // but_ASTI_Browse_Template
            // 
            this.but_ASTI_Browse_Template.Location = new System.Drawing.Point(46, 34);
            this.but_ASTI_Browse_Template.Name = "but_ASTI_Browse_Template";
            this.but_ASTI_Browse_Template.Size = new System.Drawing.Size(181, 25);
            this.but_ASTI_Browse_Template.TabIndex = 2;
            this.but_ASTI_Browse_Template.Text = "Select Templates folder";
            this.but_ASTI_Browse_Template.UseVisualStyleBackColor = true;
            // 
            // textBox_ASTI_MissionFile
            // 
            this.textBox_ASTI_MissionFile.Location = new System.Drawing.Point(44, 178);
            this.textBox_ASTI_MissionFile.Name = "textBox_ASTI_MissionFile";
            this.textBox_ASTI_MissionFile.Size = new System.Drawing.Size(322, 20);
            this.textBox_ASTI_MissionFile.TabIndex = 1;
            // 
            // textBox_ASTI_importTemplateFolder
            // 
            this.textBox_ASTI_importTemplateFolder.Location = new System.Drawing.Point(44, 70);
            this.textBox_ASTI_importTemplateFolder.Name = "textBox_ASTI_importTemplateFolder";
            this.textBox_ASTI_importTemplateFolder.Size = new System.Drawing.Size(322, 20);
            this.textBox_ASTI_importTemplateFolder.TabIndex = 0;
            // 
            // ASTI_Form
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(800, 450);
            this.Controls.Add(this.groupBox_staticTemplate);
            this.Name = "ASTI_Form";
            this.Text = "ASTI_Form";
            this.groupBox_staticTemplate.ResumeLayout(false);
            this.groupBox_staticTemplate.PerformLayout();
            this.ResumeLayout(false);

        }

        #endregion

        public System.Windows.Forms.GroupBox groupBox_staticTemplate;
        private System.Windows.Forms.Button but_GPS_LL;
        public System.Windows.Forms.Button but_ASTI_Open_templateFolder;
        private System.Windows.Forms.Button but_ASTI_Process;
        public System.Windows.Forms.Button but_ASTI_Browse_MissionFile;
        public System.Windows.Forms.Button but_ASTI_Browse_Template;
        public System.Windows.Forms.TextBox textBox_ASTI_MissionFile;
        public System.Windows.Forms.TextBox textBox_ASTI_importTemplateFolder;
    }
}