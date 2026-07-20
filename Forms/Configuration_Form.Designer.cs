namespace DCE_Manager
{
    partial class Configuration_Form
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
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(Configuration_Form));
            this.listBox_Config = new System.Windows.Forms.ListBox();
            this.But_Config_Delete = new System.Windows.Forms.Button();
            this.But_Config_Rename = new System.Windows.Forms.Button();
            this.But_AddConfig = new System.Windows.Forms.Button();
            this.but_Configuration_Close = new System.Windows.Forms.Button();
            this.pictureBox_roueDente = new System.Windows.Forms.PictureBox();
            this.label1_editConfiguration = new System.Windows.Forms.Label();
            this.label_sub_edit = new System.Windows.Forms.Label();
            this.panel1 = new System.Windows.Forms.Panel();
            ((System.ComponentModel.ISupportInitialize)(this.pictureBox_roueDente)).BeginInit();
            this.panel1.SuspendLayout();
            this.SuspendLayout();
            // 
            // listBox_Config
            // 
            this.listBox_Config.FormattingEnabled = true;
            this.listBox_Config.Location = new System.Drawing.Point(27, 18);
            this.listBox_Config.Name = "listBox_Config";
            this.listBox_Config.Size = new System.Drawing.Size(120, 95);
            this.listBox_Config.TabIndex = 24;
            // 
            // But_Config_Delete
            // 
            this.But_Config_Delete.Font = new System.Drawing.Font("Segoe UI Semibold", 9.75F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.But_Config_Delete.Location = new System.Drawing.Point(176, 77);
            this.But_Config_Delete.Name = "But_Config_Delete";
            this.But_Config_Delete.Size = new System.Drawing.Size(93, 25);
            this.But_Config_Delete.TabIndex = 22;
            this.But_Config_Delete.Text = "🗑 Delete";
            this.But_Config_Delete.UseVisualStyleBackColor = true;
            this.But_Config_Delete.Click += new System.EventHandler(this.But_Config_Delete_Click);
            // 
            // But_Config_Rename
            // 
            this.But_Config_Rename.Font = new System.Drawing.Font("Segoe UI Semibold", 9.75F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.But_Config_Rename.Location = new System.Drawing.Point(176, 46);
            this.But_Config_Rename.Name = "But_Config_Rename";
            this.But_Config_Rename.Size = new System.Drawing.Size(93, 25);
            this.But_Config_Rename.TabIndex = 21;
            this.But_Config_Rename.Text = "✎ Rename";
            this.But_Config_Rename.UseVisualStyleBackColor = true;
            // 
            // But_AddConfig
            // 
            this.But_AddConfig.Font = new System.Drawing.Font("Segoe UI Semibold", 9.75F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.But_AddConfig.Location = new System.Drawing.Point(176, 18);
            this.But_AddConfig.Name = "But_AddConfig";
            this.But_AddConfig.Size = new System.Drawing.Size(93, 22);
            this.But_AddConfig.TabIndex = 25;
            this.But_AddConfig.Text = "⨁ New";
            this.But_AddConfig.UseVisualStyleBackColor = true;
            this.But_AddConfig.Click += new System.EventHandler(this.But_AddConfig_Click);
            // 
            // but_Configuration_Close
            // 
            this.but_Configuration_Close.Font = new System.Drawing.Font("Segoe UI Semibold", 9.75F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.but_Configuration_Close.Location = new System.Drawing.Point(215, 224);
            this.but_Configuration_Close.Name = "but_Configuration_Close";
            this.but_Configuration_Close.Size = new System.Drawing.Size(93, 25);
            this.but_Configuration_Close.TabIndex = 25;
            this.but_Configuration_Close.Text = "Close";
            this.but_Configuration_Close.UseVisualStyleBackColor = true;
            this.but_Configuration_Close.Click += new System.EventHandler(this.but_Configuration_Close_Click);
            // 
            // pictureBox_roueDente
            // 
            this.pictureBox_roueDente.Image = global::DCE_Manager.Properties.Resources.icons8_paramètres_50_blue;
            this.pictureBox_roueDente.InitialImage = global::DCE_Manager.Properties.Resources.icons8_roue_dentée_50;
            this.pictureBox_roueDente.Location = new System.Drawing.Point(17, 12);
            this.pictureBox_roueDente.Name = "pictureBox_roueDente";
            this.pictureBox_roueDente.Size = new System.Drawing.Size(42, 37);
            this.pictureBox_roueDente.SizeMode = System.Windows.Forms.PictureBoxSizeMode.Zoom;
            this.pictureBox_roueDente.TabIndex = 25;
            this.pictureBox_roueDente.TabStop = false;
            // 
            // label1_editConfiguration
            // 
            this.label1_editConfiguration.AutoSize = true;
            this.label1_editConfiguration.Font = new System.Drawing.Font("Segoe UI", 12F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.label1_editConfiguration.Location = new System.Drawing.Point(64, 13);
            this.label1_editConfiguration.Name = "label1_editConfiguration";
            this.label1_editConfiguration.Size = new System.Drawing.Size(188, 21);
            this.label1_editConfiguration.TabIndex = 26;
            this.label1_editConfiguration.Text = "EDIT CONFIGURATIONS";
            // 
            // label_sub_edit
            // 
            this.label_sub_edit.AutoSize = true;
            this.label_sub_edit.Font = new System.Drawing.Font("Segoe UI Semibold", 8.25F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.label_sub_edit.Location = new System.Drawing.Point(65, 40);
            this.label_sub_edit.Name = "label_sub_edit";
            this.label_sub_edit.Size = new System.Drawing.Size(183, 13);
            this.label_sub_edit.TabIndex = 27;
            this.label_sub_edit.Text = "Manage your Configuration profils";
            // 
            // panel1
            // 
            this.panel1.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.panel1.Controls.Add(this.listBox_Config);
            this.panel1.Controls.Add(this.But_Config_Delete);
            this.panel1.Controls.Add(this.But_AddConfig);
            this.panel1.Controls.Add(this.But_Config_Rename);
            this.panel1.Location = new System.Drawing.Point(17, 69);
            this.panel1.Name = "panel1";
            this.panel1.Size = new System.Drawing.Size(291, 134);
            this.panel1.TabIndex = 28;
            // 
            // Configuration_Form
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(332, 273);
            this.Controls.Add(this.panel1);
            this.Controls.Add(this.label_sub_edit);
            this.Controls.Add(this.label1_editConfiguration);
            this.Controls.Add(this.pictureBox_roueDente);
            this.Controls.Add(this.but_Configuration_Close);
            this.Icon = ((System.Drawing.Icon)(resources.GetObject("$this.Icon")));
            this.Name = "Configuration_Form";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            this.Text = "Configurations";
            ((System.ComponentModel.ISupportInitialize)(this.pictureBox_roueDente)).EndInit();
            this.panel1.ResumeLayout(false);
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion
        private System.Windows.Forms.Button But_Config_Rename;
        private System.Windows.Forms.Button But_AddConfig;
        private System.Windows.Forms.Button But_Config_Delete;
        private System.Windows.Forms.Button but_Configuration_Close;
        private System.Windows.Forms.ListBox listBox_Config;
        private System.Windows.Forms.PictureBox pictureBox_roueDente;
        private System.Windows.Forms.Label label1_editConfiguration;
        private System.Windows.Forms.Label label_sub_edit;
        private System.Windows.Forms.Panel panel1;
    }
}