namespace DCE_Manager.UserControls
{
    partial class ucCampaign
    {
        /// <summary> 
        /// Variable nécessaire au concepteur.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary> 
        /// Nettoyage des ressources utilisées.
        /// </summary>
        /// <param name="disposing">true si les ressources managées doivent être supprimées ; sinon, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Code généré par le Concepteur de composants

        /// <summary> 
        /// Méthode requise pour la prise en charge du concepteur - ne modifiez pas 
        /// le contenu de cette méthode avec l'éditeur de code.
        /// </summary>
        private void InitializeComponent()
        {
            this.label_Right_Campaign_Name = new System.Windows.Forms.Label();
            this.panel_but_monitoring_campaign = new System.Windows.Forms.Panel();
            this.radioButton_OOB_INIT = new System.Windows.Forms.RadioButton();
            this.radioButton_OOB_ACTIVE = new System.Windows.Forms.RadioButton();
            this.buttonSaveChgtCampaign = new System.Windows.Forms.Button();
            this.buttonResetBackup = new System.Windows.Forms.Button();
            this.CampaignTab = new System.Windows.Forms.TabControl();
            this.tabPage6 = new System.Windows.Forms.TabPage();
            this.pictureBoxCampImage = new System.Windows.Forms.PictureBox();
            this.textBoxCampBriefing = new System.Windows.Forms.TextBox();
            this.tabPage14 = new System.Windows.Forms.TabPage();
            this.dataGridViewBlue = new System.Windows.Forms.DataGridView();
            this.tabPage15 = new System.Windows.Forms.TabPage();
            this.dataGridViewRed = new System.Windows.Forms.DataGridView();
            this.tabPage11 = new System.Windows.Forms.TabPage();
            this.tabPage12 = new System.Windows.Forms.TabPage();
            this.textBox_Bugs = new System.Windows.Forms.TextBox();
            this.panel_but_monitoring_campaign.SuspendLayout();
            this.CampaignTab.SuspendLayout();
            this.tabPage6.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.pictureBoxCampImage)).BeginInit();
            this.tabPage14.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.dataGridViewBlue)).BeginInit();
            this.tabPage15.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.dataGridViewRed)).BeginInit();
            this.tabPage12.SuspendLayout();
            this.SuspendLayout();
            // 
            // label_Right_Campaign_Name
            // 
            this.label_Right_Campaign_Name.AutoSize = true;
            this.label_Right_Campaign_Name.Font = new System.Drawing.Font("Segoe UI Semibold", 8.25F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.label_Right_Campaign_Name.Location = new System.Drawing.Point(18, 5);
            this.label_Right_Campaign_Name.Name = "label_Right_Campaign_Name";
            this.label_Right_Campaign_Name.Size = new System.Drawing.Size(0, 13);
            this.label_Right_Campaign_Name.TabIndex = 29;
            this.label_Right_Campaign_Name.Visible = false;
            // 
            // panel_but_monitoring_campaign
            // 
            this.panel_but_monitoring_campaign.Controls.Add(this.radioButton_OOB_INIT);
            this.panel_but_monitoring_campaign.Controls.Add(this.radioButton_OOB_ACTIVE);
            this.panel_but_monitoring_campaign.Controls.Add(this.buttonSaveChgtCampaign);
            this.panel_but_monitoring_campaign.Controls.Add(this.buttonResetBackup);
            this.panel_but_monitoring_campaign.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.panel_but_monitoring_campaign.Location = new System.Drawing.Point(0, 503);
            this.panel_but_monitoring_campaign.Name = "panel_but_monitoring_campaign";
            this.panel_but_monitoring_campaign.Size = new System.Drawing.Size(560, 37);
            this.panel_but_monitoring_campaign.TabIndex = 28;
            // 
            // radioButton_OOB_INIT
            // 
            this.radioButton_OOB_INIT.Anchor = System.Windows.Forms.AnchorStyles.Left;
            this.radioButton_OOB_INIT.AutoSize = true;
            this.radioButton_OOB_INIT.Location = new System.Drawing.Point(97, 6);
            this.radioButton_OOB_INIT.Margin = new System.Windows.Forms.Padding(2);
            this.radioButton_OOB_INIT.Name = "radioButton_OOB_INIT";
            this.radioButton_OOB_INIT.Size = new System.Drawing.Size(65, 17);
            this.radioButton_OOB_INIT.TabIndex = 16;
            this.radioButton_OOB_INIT.TabStop = true;
            this.radioButton_OOB_INIT.Text = "OOB Init";
            this.radioButton_OOB_INIT.UseVisualStyleBackColor = true;
            // 
            // radioButton_OOB_ACTIVE
            // 
            this.radioButton_OOB_ACTIVE.Anchor = System.Windows.Forms.AnchorStyles.Left;
            this.radioButton_OOB_ACTIVE.AutoSize = true;
            this.radioButton_OOB_ACTIVE.Location = new System.Drawing.Point(183, 6);
            this.radioButton_OOB_ACTIVE.Margin = new System.Windows.Forms.Padding(2);
            this.radioButton_OOB_ACTIVE.Name = "radioButton_OOB_ACTIVE";
            this.radioButton_OOB_ACTIVE.Size = new System.Drawing.Size(81, 17);
            this.radioButton_OOB_ACTIVE.TabIndex = 17;
            this.radioButton_OOB_ACTIVE.TabStop = true;
            this.radioButton_OOB_ACTIVE.Text = "OOB Active";
            this.radioButton_OOB_ACTIVE.UseVisualStyleBackColor = true;
            // 
            // buttonSaveChgtCampaign
            // 
            this.buttonSaveChgtCampaign.Anchor = System.Windows.Forms.AnchorStyles.Left;
            this.buttonSaveChgtCampaign.Location = new System.Drawing.Point(282, 5);
            this.buttonSaveChgtCampaign.Name = "buttonSaveChgtCampaign";
            this.buttonSaveChgtCampaign.Size = new System.Drawing.Size(110, 23);
            this.buttonSaveChgtCampaign.TabIndex = 12;
            this.buttonSaveChgtCampaign.Text = "Save Campaign";
            this.buttonSaveChgtCampaign.UseVisualStyleBackColor = true;
            // 
            // buttonResetBackup
            // 
            this.buttonResetBackup.Anchor = System.Windows.Forms.AnchorStyles.Left;
            this.buttonResetBackup.Location = new System.Drawing.Point(407, 5);
            this.buttonResetBackup.Name = "buttonResetBackup";
            this.buttonResetBackup.Size = new System.Drawing.Size(75, 23);
            this.buttonResetBackup.TabIndex = 14;
            this.buttonResetBackup.Text = "Reset Init";
            this.buttonResetBackup.UseVisualStyleBackColor = true;
            // 
            // CampaignTab
            // 
            this.CampaignTab.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.CampaignTab.Controls.Add(this.tabPage6);
            this.CampaignTab.Controls.Add(this.tabPage14);
            this.CampaignTab.Controls.Add(this.tabPage15);
            this.CampaignTab.Controls.Add(this.tabPage11);
            this.CampaignTab.Controls.Add(this.tabPage12);
            this.CampaignTab.Location = new System.Drawing.Point(3, 41);
            this.CampaignTab.Name = "CampaignTab";
            this.CampaignTab.SelectedIndex = 0;
            this.CampaignTab.Size = new System.Drawing.Size(554, 444);
            this.CampaignTab.TabIndex = 27;
            // 
            // tabPage6
            // 
            this.tabPage6.BackColor = System.Drawing.SystemColors.Control;
            this.tabPage6.Controls.Add(this.pictureBoxCampImage);
            this.tabPage6.Controls.Add(this.textBoxCampBriefing);
            this.tabPage6.Location = new System.Drawing.Point(4, 22);
            this.tabPage6.Name = "tabPage6";
            this.tabPage6.Padding = new System.Windows.Forms.Padding(3);
            this.tabPage6.Size = new System.Drawing.Size(546, 418);
            this.tabPage6.TabIndex = 0;
            this.tabPage6.Text = "Intro";
            // 
            // pictureBoxCampImage
            // 
            this.pictureBoxCampImage.Location = new System.Drawing.Point(19, 219);
            this.pictureBoxCampImage.Name = "pictureBoxCampImage";
            this.pictureBoxCampImage.Size = new System.Drawing.Size(521, 199);
            this.pictureBoxCampImage.SizeMode = System.Windows.Forms.PictureBoxSizeMode.Zoom;
            this.pictureBoxCampImage.TabIndex = 1;
            this.pictureBoxCampImage.TabStop = false;
            // 
            // textBoxCampBriefing
            // 
            this.textBoxCampBriefing.Location = new System.Drawing.Point(19, 12);
            this.textBoxCampBriefing.Multiline = true;
            this.textBoxCampBriefing.Name = "textBoxCampBriefing";
            this.textBoxCampBriefing.ScrollBars = System.Windows.Forms.ScrollBars.Vertical;
            this.textBoxCampBriefing.Size = new System.Drawing.Size(521, 179);
            this.textBoxCampBriefing.TabIndex = 0;
            // 
            // tabPage14
            // 
            this.tabPage14.Controls.Add(this.dataGridViewBlue);
            this.tabPage14.Location = new System.Drawing.Point(4, 22);
            this.tabPage14.Margin = new System.Windows.Forms.Padding(2);
            this.tabPage14.Name = "tabPage14";
            this.tabPage14.Padding = new System.Windows.Forms.Padding(2);
            this.tabPage14.Size = new System.Drawing.Size(546, 418);
            this.tabPage14.TabIndex = 7;
            this.tabPage14.Text = "OOB Blue";
            this.tabPage14.UseVisualStyleBackColor = true;
            // 
            // dataGridViewBlue
            // 
            this.dataGridViewBlue.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.dataGridViewBlue.ColumnHeadersHeightSizeMode = System.Windows.Forms.DataGridViewColumnHeadersHeightSizeMode.AutoSize;
            this.dataGridViewBlue.Location = new System.Drawing.Point(2, 2);
            this.dataGridViewBlue.Margin = new System.Windows.Forms.Padding(2);
            this.dataGridViewBlue.Name = "dataGridViewBlue";
            this.dataGridViewBlue.RowHeadersWidth = 51;
            this.dataGridViewBlue.RowTemplate.Height = 24;
            this.dataGridViewBlue.Size = new System.Drawing.Size(540, 436);
            this.dataGridViewBlue.TabIndex = 14;
            // 
            // tabPage15
            // 
            this.tabPage15.Controls.Add(this.dataGridViewRed);
            this.tabPage15.Location = new System.Drawing.Point(4, 22);
            this.tabPage15.Margin = new System.Windows.Forms.Padding(2);
            this.tabPage15.Name = "tabPage15";
            this.tabPage15.Padding = new System.Windows.Forms.Padding(2);
            this.tabPage15.Size = new System.Drawing.Size(546, 418);
            this.tabPage15.TabIndex = 8;
            this.tabPage15.Text = "OOB Red";
            this.tabPage15.UseVisualStyleBackColor = true;
            // 
            // dataGridViewRed
            // 
            this.dataGridViewRed.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.dataGridViewRed.ColumnHeadersHeightSizeMode = System.Windows.Forms.DataGridViewColumnHeadersHeightSizeMode.AutoSize;
            this.dataGridViewRed.Location = new System.Drawing.Point(2, 2);
            this.dataGridViewRed.Margin = new System.Windows.Forms.Padding(2);
            this.dataGridViewRed.Name = "dataGridViewRed";
            this.dataGridViewRed.RowHeadersWidth = 51;
            this.dataGridViewRed.RowTemplate.Height = 24;
            this.dataGridViewRed.Size = new System.Drawing.Size(540, 441);
            this.dataGridViewRed.TabIndex = 15;
            // 
            // tabPage11
            // 
            this.tabPage11.Location = new System.Drawing.Point(4, 22);
            this.tabPage11.Name = "tabPage11";
            this.tabPage11.Size = new System.Drawing.Size(546, 418);
            this.tabPage11.TabIndex = 5;
            this.tabPage11.Text = "Options";
            // 
            // tabPage12
            // 
            this.tabPage12.Controls.Add(this.textBox_Bugs);
            this.tabPage12.Location = new System.Drawing.Point(4, 22);
            this.tabPage12.Name = "tabPage12";
            this.tabPage12.Padding = new System.Windows.Forms.Padding(3);
            this.tabPage12.Size = new System.Drawing.Size(546, 418);
            this.tabPage12.TabIndex = 6;
            this.tabPage12.Text = "Bugs";
            this.tabPage12.UseVisualStyleBackColor = true;
            // 
            // textBox_Bugs
            // 
            this.textBox_Bugs.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.textBox_Bugs.Location = new System.Drawing.Point(13, 3);
            this.textBox_Bugs.Multiline = true;
            this.textBox_Bugs.Name = "textBox_Bugs";
            this.textBox_Bugs.ScrollBars = System.Windows.Forms.ScrollBars.Vertical;
            this.textBox_Bugs.Size = new System.Drawing.Size(604, 442);
            this.textBox_Bugs.TabIndex = 1;
            // 
            // ucCampaign
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.Controls.Add(this.label_Right_Campaign_Name);
            this.Controls.Add(this.panel_but_monitoring_campaign);
            this.Controls.Add(this.CampaignTab);
            this.Name = "ucCampaign";
            this.Size = new System.Drawing.Size(560, 540);
            this.panel_but_monitoring_campaign.ResumeLayout(false);
            this.panel_but_monitoring_campaign.PerformLayout();
            this.CampaignTab.ResumeLayout(false);
            this.tabPage6.ResumeLayout(false);
            this.tabPage6.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.pictureBoxCampImage)).EndInit();
            this.tabPage14.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.dataGridViewBlue)).EndInit();
            this.tabPage15.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.dataGridViewRed)).EndInit();
            this.tabPage12.ResumeLayout(false);
            this.tabPage12.PerformLayout();
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        public System.Windows.Forms.Label label_Right_Campaign_Name;
        public System.Windows.Forms.Panel panel_but_monitoring_campaign;
        public System.Windows.Forms.RadioButton radioButton_OOB_INIT;
        public System.Windows.Forms.RadioButton radioButton_OOB_ACTIVE;
        public System.Windows.Forms.Button buttonSaveChgtCampaign;
        public System.Windows.Forms.Button buttonResetBackup;
        public System.Windows.Forms.TabControl CampaignTab;
        public System.Windows.Forms.TabPage tabPage6;
        public System.Windows.Forms.PictureBox pictureBoxCampImage;
        public System.Windows.Forms.TextBox textBoxCampBriefing;
        public System.Windows.Forms.TabPage tabPage14;
        public System.Windows.Forms.DataGridView dataGridViewBlue;
        public System.Windows.Forms.TabPage tabPage15;
        public System.Windows.Forms.DataGridView dataGridViewRed;
        public System.Windows.Forms.TabPage tabPage11;
        public System.Windows.Forms.TabPage tabPage12;
        public System.Windows.Forms.TextBox textBox_Bugs;
    }
}
