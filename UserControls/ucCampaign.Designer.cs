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
            this.radioButton_INIT_CAMP = new System.Windows.Forms.RadioButton();
            this.radioButton_ACTIVE_CAMP = new System.Windows.Forms.RadioButton();
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
            this.tabPageTargetsBlue = new System.Windows.Forms.TabPage();
            this.dataGridViewTargetsBlue = new System.Windows.Forms.DataGridView();
            this.tabPageTargetsRed = new System.Windows.Forms.TabPage();
            this.dataGridViewTargetsRed = new System.Windows.Forms.DataGridView();
            this.tabPage11 = new System.Windows.Forms.TabPage();
            this.tabPage12 = new System.Windows.Forms.TabPage();
            this.textBox_Bugs = new System.Windows.Forms.TextBox();
            this.tabPage_wargame = new System.Windows.Forms.TabPage();
            this.but_MakeWarZone = new System.Windows.Forms.Button();
            this.but_wargame_MAP = new System.Windows.Forms.Button();
            this.panel_but_monitoring_campaign.SuspendLayout();
            this.CampaignTab.SuspendLayout();
            this.tabPage6.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.pictureBoxCampImage)).BeginInit();
            this.tabPage14.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.dataGridViewBlue)).BeginInit();
            this.tabPage15.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.dataGridViewRed)).BeginInit();
            this.tabPageTargetsBlue.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.dataGridViewTargetsBlue)).BeginInit();
            this.tabPageTargetsRed.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.dataGridViewTargetsRed)).BeginInit();
            this.tabPage12.SuspendLayout();
            this.tabPage_wargame.SuspendLayout();
            this.SuspendLayout();
            // 
            // label_Right_Campaign_Name
            // 
            this.label_Right_Campaign_Name.AutoSize = true;
            this.label_Right_Campaign_Name.Font = new System.Drawing.Font("Segoe UI Semibold", 8.25F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.label_Right_Campaign_Name.Location = new System.Drawing.Point(24, 6);
            this.label_Right_Campaign_Name.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            this.label_Right_Campaign_Name.Name = "label_Right_Campaign_Name";
            this.label_Right_Campaign_Name.Size = new System.Drawing.Size(0, 19);
            this.label_Right_Campaign_Name.TabIndex = 29;
            this.label_Right_Campaign_Name.Visible = false;
            // 
            // panel_but_monitoring_campaign
            // 
            this.panel_but_monitoring_campaign.Controls.Add(this.radioButton_INIT_CAMP);
            this.panel_but_monitoring_campaign.Controls.Add(this.radioButton_ACTIVE_CAMP);
            this.panel_but_monitoring_campaign.Controls.Add(this.buttonSaveChgtCampaign);
            this.panel_but_monitoring_campaign.Controls.Add(this.buttonResetBackup);
            this.panel_but_monitoring_campaign.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.panel_but_monitoring_campaign.Location = new System.Drawing.Point(0, 619);
            this.panel_but_monitoring_campaign.Margin = new System.Windows.Forms.Padding(4);
            this.panel_but_monitoring_campaign.Name = "panel_but_monitoring_campaign";
            this.panel_but_monitoring_campaign.Size = new System.Drawing.Size(747, 46);
            this.panel_but_monitoring_campaign.TabIndex = 28;
            // 
            // radioButton_INIT_CAMP
            // 
            this.radioButton_INIT_CAMP.Anchor = System.Windows.Forms.AnchorStyles.Left;
            this.radioButton_INIT_CAMP.AutoSize = true;
            this.radioButton_INIT_CAMP.Location = new System.Drawing.Point(129, 7);
            this.radioButton_INIT_CAMP.Margin = new System.Windows.Forms.Padding(3, 2, 3, 2);
            this.radioButton_INIT_CAMP.Name = "radioButton_INIT_CAMP";
            this.radioButton_INIT_CAMP.Size = new System.Drawing.Size(44, 20);
            this.radioButton_INIT_CAMP.TabIndex = 16;
            this.radioButton_INIT_CAMP.TabStop = true;
            this.radioButton_INIT_CAMP.Text = "Init";
            this.radioButton_INIT_CAMP.UseVisualStyleBackColor = true;
            // 
            // radioButton_ACTIVE_CAMP
            // 
            this.radioButton_ACTIVE_CAMP.Anchor = System.Windows.Forms.AnchorStyles.Left;
            this.radioButton_ACTIVE_CAMP.AutoSize = true;
            this.radioButton_ACTIVE_CAMP.Location = new System.Drawing.Point(244, 7);
            this.radioButton_ACTIVE_CAMP.Margin = new System.Windows.Forms.Padding(3, 2, 3, 2);
            this.radioButton_ACTIVE_CAMP.Name = "radioButton_ACTIVE_CAMP";
            this.radioButton_ACTIVE_CAMP.Size = new System.Drawing.Size(65, 20);
            this.radioButton_ACTIVE_CAMP.TabIndex = 17;
            this.radioButton_ACTIVE_CAMP.TabStop = true;
            this.radioButton_ACTIVE_CAMP.Text = "Active";
            this.radioButton_ACTIVE_CAMP.UseVisualStyleBackColor = true;
            // 
            // buttonSaveChgtCampaign
            // 
            this.buttonSaveChgtCampaign.Anchor = System.Windows.Forms.AnchorStyles.Left;
            this.buttonSaveChgtCampaign.Location = new System.Drawing.Point(376, 6);
            this.buttonSaveChgtCampaign.Margin = new System.Windows.Forms.Padding(4);
            this.buttonSaveChgtCampaign.Name = "buttonSaveChgtCampaign";
            this.buttonSaveChgtCampaign.Size = new System.Drawing.Size(147, 28);
            this.buttonSaveChgtCampaign.TabIndex = 12;
            this.buttonSaveChgtCampaign.Text = "Save Campaign";
            this.buttonSaveChgtCampaign.UseVisualStyleBackColor = true;
            this.buttonSaveChgtCampaign.Click += new System.EventHandler(this.buttonSaveChgtCampaign_Click);
            // 
            // buttonResetBackup
            // 
            this.buttonResetBackup.Anchor = System.Windows.Forms.AnchorStyles.Left;
            this.buttonResetBackup.Location = new System.Drawing.Point(543, 6);
            this.buttonResetBackup.Margin = new System.Windows.Forms.Padding(4);
            this.buttonResetBackup.Name = "buttonResetBackup";
            this.buttonResetBackup.Size = new System.Drawing.Size(100, 28);
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
            this.CampaignTab.Controls.Add(this.tabPageTargetsBlue);
            this.CampaignTab.Controls.Add(this.tabPageTargetsRed);
            this.CampaignTab.Controls.Add(this.tabPage11);
            this.CampaignTab.Controls.Add(this.tabPage12);
            this.CampaignTab.Controls.Add(this.tabPage_wargame);
            this.CampaignTab.Location = new System.Drawing.Point(4, 50);
            this.CampaignTab.Margin = new System.Windows.Forms.Padding(4);
            this.CampaignTab.Name = "CampaignTab";
            this.CampaignTab.SelectedIndex = 0;
            this.CampaignTab.Size = new System.Drawing.Size(739, 546);
            this.CampaignTab.TabIndex = 27;
            // 
            // tabPage6
            // 
            this.tabPage6.BackColor = System.Drawing.SystemColors.Control;
            this.tabPage6.Controls.Add(this.pictureBoxCampImage);
            this.tabPage6.Controls.Add(this.textBoxCampBriefing);
            this.tabPage6.Location = new System.Drawing.Point(4, 25);
            this.tabPage6.Margin = new System.Windows.Forms.Padding(4);
            this.tabPage6.Name = "tabPage6";
            this.tabPage6.Padding = new System.Windows.Forms.Padding(4);
            this.tabPage6.Size = new System.Drawing.Size(731, 517);
            this.tabPage6.TabIndex = 0;
            this.tabPage6.Text = "Intro";
            // 
            // pictureBoxCampImage
            // 
            this.pictureBoxCampImage.Location = new System.Drawing.Point(25, 270);
            this.pictureBoxCampImage.Margin = new System.Windows.Forms.Padding(4);
            this.pictureBoxCampImage.Name = "pictureBoxCampImage";
            this.pictureBoxCampImage.Size = new System.Drawing.Size(695, 245);
            this.pictureBoxCampImage.SizeMode = System.Windows.Forms.PictureBoxSizeMode.Zoom;
            this.pictureBoxCampImage.TabIndex = 1;
            this.pictureBoxCampImage.TabStop = false;
            // 
            // textBoxCampBriefing
            // 
            this.textBoxCampBriefing.Location = new System.Drawing.Point(25, 15);
            this.textBoxCampBriefing.Margin = new System.Windows.Forms.Padding(4);
            this.textBoxCampBriefing.Multiline = true;
            this.textBoxCampBriefing.Name = "textBoxCampBriefing";
            this.textBoxCampBriefing.ScrollBars = System.Windows.Forms.ScrollBars.Vertical;
            this.textBoxCampBriefing.Size = new System.Drawing.Size(693, 219);
            this.textBoxCampBriefing.TabIndex = 0;
            // 
            // tabPage14
            // 
            this.tabPage14.Controls.Add(this.dataGridViewBlue);
            this.tabPage14.Location = new System.Drawing.Point(4, 25);
            this.tabPage14.Margin = new System.Windows.Forms.Padding(3, 2, 3, 2);
            this.tabPage14.Name = "tabPage14";
            this.tabPage14.Padding = new System.Windows.Forms.Padding(3, 2, 3, 2);
            this.tabPage14.Size = new System.Drawing.Size(731, 517);
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
            //this.dataGridViewBlue.Location = new System.Drawing.Point(3, 2);
            this.dataGridViewBlue.Margin = new System.Windows.Forms.Padding(3, 2, 3, 2);
            this.dataGridViewBlue.Name = "dataGridViewBlue";
            this.dataGridViewBlue.RowHeadersWidth = 51;
            this.dataGridViewBlue.RowTemplate.Height = 24;
            //this.dataGridViewBlue.Size = new System.Drawing.Size(720, 537);
            this.dataGridViewBlue.TabIndex = 14;
            this.dataGridViewBlue.Dock = System.Windows.Forms.DockStyle.Fill;
            // 
            // tabPage15
            // 
            this.tabPage15.Controls.Add(this.dataGridViewRed);
            this.tabPage15.Location = new System.Drawing.Point(4, 25);
            this.tabPage15.Margin = new System.Windows.Forms.Padding(3, 2, 3, 2);
            this.tabPage15.Name = "tabPage15";
            this.tabPage15.Padding = new System.Windows.Forms.Padding(3, 2, 3, 2);
            this.tabPage15.Size = new System.Drawing.Size(731, 517);
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
            //this.dataGridViewRed.Location = new System.Drawing.Point(3, 2);
            this.dataGridViewRed.Margin = new System.Windows.Forms.Padding(3, 2, 3, 2);
            this.dataGridViewRed.Name = "dataGridViewRed";
            this.dataGridViewRed.RowHeadersWidth = 51;
            this.dataGridViewRed.RowTemplate.Height = 24;
            //this.dataGridViewRed.Size = new System.Drawing.Size(720, 543);
            this.dataGridViewRed.TabIndex = 15;
            this.dataGridViewRed.Dock = System.Windows.Forms.DockStyle.Fill;
            // 
            // tabPageTargetsBlue
            // 
            this.tabPageTargetsBlue.Controls.Add(this.dataGridViewTargetsBlue);
            this.tabPageTargetsBlue.Location = new System.Drawing.Point(4, 25);
            this.tabPageTargetsBlue.Margin = new System.Windows.Forms.Padding(3, 2, 3, 2);
            this.tabPageTargetsBlue.Name = "tabPageTargetsBlue";
            this.tabPageTargetsBlue.Padding = new System.Windows.Forms.Padding(3, 2, 3, 2);
            this.tabPageTargetsBlue.Size = new System.Drawing.Size(731, 517);
            this.tabPageTargetsBlue.TabIndex = 9;
            this.tabPageTargetsBlue.Text = "Targets Blue";
            this.tabPageTargetsBlue.UseVisualStyleBackColor = true;
            // 
            // dataGridViewTargetsBlue
            // 
            this.dataGridViewTargetsBlue.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.dataGridViewTargetsBlue.ColumnHeadersHeightSizeMode = System.Windows.Forms.DataGridViewColumnHeadersHeightSizeMode.AutoSize;
            this.dataGridViewTargetsBlue.Location = new System.Drawing.Point(3, 2);
            this.dataGridViewTargetsBlue.Margin = new System.Windows.Forms.Padding(3, 2, 3, 2);
            this.dataGridViewTargetsBlue.Name = "dataGridViewTargetsBlue";
            this.dataGridViewTargetsBlue.RowHeadersWidth = 51;
            this.dataGridViewTargetsBlue.RowTemplate.Height = 24;
            this.dataGridViewTargetsBlue.Size = new System.Drawing.Size(720, 537);
            this.dataGridViewTargetsBlue.TabIndex = 16;
            // 
            // tabPageTargetsRed
            // 
            this.tabPageTargetsRed.Controls.Add(this.dataGridViewTargetsRed);
            this.tabPageTargetsRed.Location = new System.Drawing.Point(4, 25);
            this.tabPageTargetsRed.Margin = new System.Windows.Forms.Padding(3, 2, 3, 2);
            this.tabPageTargetsRed.Name = "tabPageTargetsRed";
            this.tabPageTargetsRed.Padding = new System.Windows.Forms.Padding(3, 2, 3, 2);
            this.tabPageTargetsRed.Size = new System.Drawing.Size(731, 517);
            this.tabPageTargetsRed.TabIndex = 10;
            this.tabPageTargetsRed.Text = "Targets Red";
            this.tabPageTargetsRed.UseVisualStyleBackColor = true;
            // 
            // dataGridViewTargetsRed
            // 
            this.dataGridViewTargetsRed.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.dataGridViewTargetsRed.ColumnHeadersHeightSizeMode = System.Windows.Forms.DataGridViewColumnHeadersHeightSizeMode.AutoSize;
            this.dataGridViewTargetsRed.Location = new System.Drawing.Point(3, 2);
            this.dataGridViewTargetsRed.Margin = new System.Windows.Forms.Padding(3, 2, 3, 2);
            this.dataGridViewTargetsRed.Name = "dataGridViewTargetsRed";
            this.dataGridViewTargetsRed.RowHeadersWidth = 51;
            this.dataGridViewTargetsRed.RowTemplate.Height = 24;
            this.dataGridViewTargetsRed.Size = new System.Drawing.Size(720, 543);
            this.dataGridViewTargetsRed.TabIndex = 17;
            // 
            // tabPage11
            // 
            this.tabPage11.Location = new System.Drawing.Point(4, 25);
            this.tabPage11.Margin = new System.Windows.Forms.Padding(4);
            this.tabPage11.Name = "tabPage11";
            this.tabPage11.Size = new System.Drawing.Size(731, 517);
            this.tabPage11.TabIndex = 5;
            this.tabPage11.Text = "Options";
            // 
            // tabPage12
            // 
            this.tabPage12.Controls.Add(this.textBox_Bugs);
            this.tabPage12.Location = new System.Drawing.Point(4, 25);
            this.tabPage12.Margin = new System.Windows.Forms.Padding(4);
            this.tabPage12.Name = "tabPage12";
            this.tabPage12.Padding = new System.Windows.Forms.Padding(4);
            this.tabPage12.Size = new System.Drawing.Size(731, 517);
            this.tabPage12.TabIndex = 6;
            this.tabPage12.Text = "Bugs";
            this.tabPage12.UseVisualStyleBackColor = true;
            // 
            // textBox_Bugs
            // 
            this.textBox_Bugs.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.textBox_Bugs.Location = new System.Drawing.Point(17, 4);
            this.textBox_Bugs.Margin = new System.Windows.Forms.Padding(4);
            this.textBox_Bugs.Multiline = true;
            this.textBox_Bugs.Name = "textBox_Bugs";
            this.textBox_Bugs.ScrollBars = System.Windows.Forms.ScrollBars.Vertical;
            this.textBox_Bugs.Size = new System.Drawing.Size(804, 543);
            this.textBox_Bugs.TabIndex = 1;
            // 
            // tabPage_wargame
            // 
            this.tabPage_wargame.Controls.Add(this.but_wargame_MAP);
            this.tabPage_wargame.Controls.Add(this.but_MakeWarZone);
            this.tabPage_wargame.Location = new System.Drawing.Point(4, 25);
            this.tabPage_wargame.Name = "tabPage_wargame";
            this.tabPage_wargame.Padding = new System.Windows.Forms.Padding(3);
            this.tabPage_wargame.Size = new System.Drawing.Size(731, 517);
            this.tabPage_wargame.TabIndex = 11;
            this.tabPage_wargame.Text = "Wargame";
            this.tabPage_wargame.UseVisualStyleBackColor = true;
            // 
            // but_MakeWarZone
            // 
            this.but_MakeWarZone.Location = new System.Drawing.Point(67, 77);
            this.but_MakeWarZone.Name = "but_MakeWarZone";
            this.but_MakeWarZone.Size = new System.Drawing.Size(320, 23);
            this.but_MakeWarZone.TabIndex = 0;
            this.but_MakeWarZone.Text = "Create Warzone";
            this.but_MakeWarZone.UseVisualStyleBackColor = true;
            this.but_MakeWarZone.Click += new System.EventHandler(this.but_MakeWarZone_Click);
            // 
            // but_wargame_MAP
            // 
            this.but_wargame_MAP.Location = new System.Drawing.Point(67, 131);
            this.but_wargame_MAP.Name = "but_wargame_MAP";
            this.but_wargame_MAP.Size = new System.Drawing.Size(320, 23);
            this.but_wargame_MAP.TabIndex = 1;
            this.but_wargame_MAP.Text = "Wargame MAP";
            this.but_wargame_MAP.UseVisualStyleBackColor = true;
            this.but_wargame_MAP.Click += new System.EventHandler(this.but_wargame_MAP_Click);
            // 
            // ucCampaign
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(8F, 16F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.Controls.Add(this.label_Right_Campaign_Name);
            this.Controls.Add(this.panel_but_monitoring_campaign);
            this.Controls.Add(this.CampaignTab);
            this.Margin = new System.Windows.Forms.Padding(4);
            this.Name = "ucCampaign";
            this.Size = new System.Drawing.Size(747, 665);
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
            this.tabPageTargetsBlue.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.dataGridViewTargetsBlue)).EndInit();
            this.tabPageTargetsRed.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.dataGridViewTargetsRed)).EndInit();
            this.tabPage12.ResumeLayout(false);
            this.tabPage12.PerformLayout();
            this.tabPage_wargame.ResumeLayout(false);
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        public System.Windows.Forms.Label label_Right_Campaign_Name;
        public System.Windows.Forms.Panel panel_but_monitoring_campaign;
        public System.Windows.Forms.RadioButton radioButton_INIT_CAMP;
        public System.Windows.Forms.RadioButton radioButton_ACTIVE_CAMP;
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
        public System.Windows.Forms.TabPage tabPageTargetsBlue;
        public System.Windows.Forms.DataGridView dataGridViewTargetsBlue;
        public System.Windows.Forms.TabPage tabPageTargetsRed;
        public System.Windows.Forms.DataGridView dataGridViewTargetsRed;
        public System.Windows.Forms.TabPage tabPage11;
        public System.Windows.Forms.TabPage tabPage12;
        public System.Windows.Forms.TextBox textBox_Bugs;
        private System.Windows.Forms.TabPage tabPage_wargame;
        private System.Windows.Forms.Button but_MakeWarZone;
        private System.Windows.Forms.Button but_wargame_MAP;
    }
}
