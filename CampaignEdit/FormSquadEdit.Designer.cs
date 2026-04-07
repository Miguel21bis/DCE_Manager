// Fichier à créer / remplacer : FormSquadEdit.Designer.cs
// But : remplacer la petite fenêtre actuelle par une grande fenêtre éditable affichant tout le squad.
// Pourquoi : avoir toutes les informations visibles immédiatement sans sous-fenêtre ni onglet.

using System.Drawing;
using System.Windows.Forms;

namespace DCE_Manager
{
    partial class FormSquadEdit
    {
        private System.ComponentModel.IContainer components = null;

        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        private void InitializeComponent()
        {
            this.panelMain = new System.Windows.Forms.Panel();
            this.groupBoxAdditional = new System.Windows.Forms.GroupBox();
            this.flowLayoutPanelAdditional = new System.Windows.Forms.FlowLayoutPanel();
            this.groupBoxScore = new System.Windows.Forms.GroupBox();
            this.flowLayoutPanelScore = new System.Windows.Forms.FlowLayoutPanel();
            this.groupBoxTasks = new System.Windows.Forms.GroupBox();
            this.flowLayoutPanelTasks = new System.Windows.Forms.FlowLayoutPanel();
            this.groupBoxAircraft = new System.Windows.Forms.GroupBox();
            this.labelNumber = new System.Windows.Forms.Label();
            this.numericNumber = new System.Windows.Forms.NumericUpDown();
            this.labelInitNumber = new System.Windows.Forms.Label();
            this.numericInitNumber = new System.Windows.Forms.NumericUpDown();
            this.labelReserve = new System.Windows.Forms.Label();
            this.numericReserve = new System.Windows.Forms.NumericUpDown();
            this.labelInitReserve = new System.Windows.Forms.Label();
            this.numericInitReserve = new System.Windows.Forms.NumericUpDown();
            this.groupBoxGeneral = new System.Windows.Forms.GroupBox();
            this.labelName = new System.Windows.Forms.Label();
            this.textBoxName = new System.Windows.Forms.TextBox();
            this.labelType = new System.Windows.Forms.Label();
            this.comboBoxType = new System.Windows.Forms.ComboBox();
            this.labelCountry = new System.Windows.Forms.Label();
            this.comboBoxCountry = new System.Windows.Forms.ComboBox();
            this.labelBase = new System.Windows.Forms.Label();
            this.comboBoxBase = new System.Windows.Forms.ComboBox();
            this.labelSkill = new System.Windows.Forms.Label();
            this.comboBoxSkill = new System.Windows.Forms.ComboBox();
            this.checkBoxPlayer = new System.Windows.Forms.CheckBox();
            this.checkBoxInactive = new System.Windows.Forms.CheckBox();
            this.buttonOK = new System.Windows.Forms.Button();
            this.buttonCancel = new System.Windows.Forms.Button();
            this.panelMain.SuspendLayout();
            this.groupBoxAdditional.SuspendLayout();
            this.groupBoxScore.SuspendLayout();
            this.groupBoxTasks.SuspendLayout();
            this.groupBoxAircraft.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.numericNumber)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.numericInitNumber)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.numericReserve)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.numericInitReserve)).BeginInit();
            this.groupBoxGeneral.SuspendLayout();
            this.SuspendLayout();
            // 
            // panelMain
            // 
            this.panelMain.AutoScroll = true;
            this.panelMain.Controls.Add(this.groupBoxAdditional);
            this.panelMain.Controls.Add(this.groupBoxScore);
            this.panelMain.Controls.Add(this.groupBoxTasks);
            this.panelMain.Controls.Add(this.groupBoxAircraft);
            this.panelMain.Controls.Add(this.groupBoxGeneral);
            this.panelMain.Controls.Add(this.buttonOK);
            this.panelMain.Controls.Add(this.buttonCancel);
            this.panelMain.Dock = System.Windows.Forms.DockStyle.Fill;
            this.panelMain.Location = new System.Drawing.Point(0, 0);
            this.panelMain.Name = "panelMain";
            this.panelMain.Size = new System.Drawing.Size(1282, 956);
            this.panelMain.TabIndex = 0;
            // 
            // groupBoxAdditional
            // 
            this.groupBoxAdditional.Controls.Add(this.flowLayoutPanelAdditional);
            this.groupBoxAdditional.Location = new System.Drawing.Point(12, 780);
            this.groupBoxAdditional.Name = "groupBoxAdditional";
            this.groupBoxAdditional.Size = new System.Drawing.Size(1053, 320);
            this.groupBoxAdditional.TabIndex = 0;
            this.groupBoxAdditional.TabStop = false;
            this.groupBoxAdditional.Text = "Additional / Future Lua Variables";
            // 
            // flowLayoutPanelAdditional
            // 
            this.flowLayoutPanelAdditional.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.flowLayoutPanelAdditional.FlowDirection = System.Windows.Forms.FlowDirection.TopDown;
            this.flowLayoutPanelAdditional.Location = new System.Drawing.Point(15, 25);
            this.flowLayoutPanelAdditional.Name = "flowLayoutPanelAdditional";
            this.flowLayoutPanelAdditional.Size = new System.Drawing.Size(1018, 280);
            this.flowLayoutPanelAdditional.TabIndex = 0;
            this.flowLayoutPanelAdditional.WrapContents = false;
            // 
            // groupBoxScore
            // 
            this.groupBoxScore.Controls.Add(this.flowLayoutPanelScore);
            this.groupBoxScore.Location = new System.Drawing.Point(12, 585);
            this.groupBoxScore.Name = "groupBoxScore";
            this.groupBoxScore.Size = new System.Drawing.Size(1053, 180);
            this.groupBoxScore.TabIndex = 1;
            this.groupBoxScore.TabStop = false;
            this.groupBoxScore.Text = "Roster / Score / Last Mission";
            // 
            // flowLayoutPanelScore
            // 
            this.flowLayoutPanelScore.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.flowLayoutPanelScore.FlowDirection = System.Windows.Forms.FlowDirection.TopDown;
            this.flowLayoutPanelScore.Location = new System.Drawing.Point(15, 25);
            this.flowLayoutPanelScore.Name = "flowLayoutPanelScore";
            this.flowLayoutPanelScore.Size = new System.Drawing.Size(1018, 140);
            this.flowLayoutPanelScore.TabIndex = 0;
            this.flowLayoutPanelScore.WrapContents = false;
            // 
            // groupBoxTasks
            // 
            this.groupBoxTasks.Controls.Add(this.flowLayoutPanelTasks);
            this.groupBoxTasks.Location = new System.Drawing.Point(12, 350);
            this.groupBoxTasks.Name = "groupBoxTasks";
            this.groupBoxTasks.Size = new System.Drawing.Size(1053, 220);
            this.groupBoxTasks.TabIndex = 2;
            this.groupBoxTasks.TabStop = false;
            this.groupBoxTasks.Text = "Tasks / Coefficients";
            // 
            // flowLayoutPanelTasks
            // 
            this.flowLayoutPanelTasks.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.flowLayoutPanelTasks.FlowDirection = System.Windows.Forms.FlowDirection.TopDown;
            this.flowLayoutPanelTasks.Location = new System.Drawing.Point(15, 25);
            this.flowLayoutPanelTasks.Name = "flowLayoutPanelTasks";
            this.flowLayoutPanelTasks.Size = new System.Drawing.Size(1014, 180);
            this.flowLayoutPanelTasks.TabIndex = 0;
            this.flowLayoutPanelTasks.WrapContents = false;
            // 
            // groupBoxAircraft
            // 
            this.groupBoxAircraft.Controls.Add(this.labelNumber);
            this.groupBoxAircraft.Controls.Add(this.numericNumber);
            this.groupBoxAircraft.Controls.Add(this.labelInitNumber);
            this.groupBoxAircraft.Controls.Add(this.numericInitNumber);
            this.groupBoxAircraft.Controls.Add(this.labelReserve);
            this.groupBoxAircraft.Controls.Add(this.numericReserve);
            this.groupBoxAircraft.Controls.Add(this.labelInitReserve);
            this.groupBoxAircraft.Controls.Add(this.numericInitReserve);
            this.groupBoxAircraft.Location = new System.Drawing.Point(12, 205);
            this.groupBoxAircraft.Name = "groupBoxAircraft";
            this.groupBoxAircraft.Size = new System.Drawing.Size(588, 130);
            this.groupBoxAircraft.TabIndex = 3;
            this.groupBoxAircraft.TabStop = false;
            this.groupBoxAircraft.Text = "Aircraft / Numbers";
            // 
            // labelNumber
            // 
            this.labelNumber.AutoSize = true;
            this.labelNumber.Location = new System.Drawing.Point(20, 78);
            this.labelNumber.Name = "labelNumber";
            this.labelNumber.Size = new System.Drawing.Size(55, 16);
            this.labelNumber.TabIndex = 0;
            this.labelNumber.Text = "Number";
            // 
            // numericNumber
            // 
            this.numericNumber.Location = new System.Drawing.Point(140, 72);
            this.numericNumber.Maximum = new decimal(new int[] {
            9999,
            0,
            0,
            0});
            this.numericNumber.Name = "numericNumber";
            this.numericNumber.Size = new System.Drawing.Size(120, 22);
            this.numericNumber.TabIndex = 1;
            // 
            // labelInitNumber
            // 
            this.labelInitNumber.AutoSize = true;
            this.labelInitNumber.Location = new System.Drawing.Point(20, 35);
            this.labelInitNumber.Name = "labelInitNumber";
            this.labelInitNumber.Size = new System.Drawing.Size(74, 16);
            this.labelInitNumber.TabIndex = 2;
            this.labelInitNumber.Text = "Init Number";
            // 
            // numericInitNumber
            // 
            this.numericInitNumber.Location = new System.Drawing.Point(140, 32);
            this.numericInitNumber.Maximum = new decimal(new int[] {
            9999,
            0,
            0,
            0});
            this.numericInitNumber.Name = "numericInitNumber";
            this.numericInitNumber.Size = new System.Drawing.Size(120, 22);
            this.numericInitNumber.TabIndex = 3;
            // 
            // labelReserve
            // 
            this.labelReserve.AutoSize = true;
            this.labelReserve.Location = new System.Drawing.Point(341, 78);
            this.labelReserve.Name = "labelReserve";
            this.labelReserve.Size = new System.Drawing.Size(59, 16);
            this.labelReserve.TabIndex = 4;
            this.labelReserve.Text = "Reserve";
            // 
            // numericReserve
            // 
            this.numericReserve.Location = new System.Drawing.Point(435, 76);
            this.numericReserve.Maximum = new decimal(new int[] {
            9999,
            0,
            0,
            0});
            this.numericReserve.Name = "numericReserve";
            this.numericReserve.Size = new System.Drawing.Size(120, 22);
            this.numericReserve.TabIndex = 5;
            // 
            // labelInitReserve
            // 
            this.labelInitReserve.AutoSize = true;
            this.labelInitReserve.Location = new System.Drawing.Point(341, 38);
            this.labelInitReserve.Name = "labelInitReserve";
            this.labelInitReserve.Size = new System.Drawing.Size(78, 16);
            this.labelInitReserve.TabIndex = 6;
            this.labelInitReserve.Text = "Init Reserve";
            // 
            // numericInitReserve
            // 
            this.numericInitReserve.Location = new System.Drawing.Point(435, 32);
            this.numericInitReserve.Maximum = new decimal(new int[] {
            9999,
            0,
            0,
            0});
            this.numericInitReserve.Name = "numericInitReserve";
            this.numericInitReserve.Size = new System.Drawing.Size(120, 22);
            this.numericInitReserve.TabIndex = 7;
            // 
            // groupBoxGeneral
            // 
            this.groupBoxGeneral.Controls.Add(this.labelName);
            this.groupBoxGeneral.Controls.Add(this.textBoxName);
            this.groupBoxGeneral.Controls.Add(this.labelType);
            this.groupBoxGeneral.Controls.Add(this.comboBoxType);
            this.groupBoxGeneral.Controls.Add(this.labelCountry);
            this.groupBoxGeneral.Controls.Add(this.comboBoxCountry);
            this.groupBoxGeneral.Controls.Add(this.labelBase);
            this.groupBoxGeneral.Controls.Add(this.comboBoxBase);
            this.groupBoxGeneral.Controls.Add(this.labelSkill);
            this.groupBoxGeneral.Controls.Add(this.comboBoxSkill);
            this.groupBoxGeneral.Controls.Add(this.checkBoxPlayer);
            this.groupBoxGeneral.Controls.Add(this.checkBoxInactive);
            this.groupBoxGeneral.Location = new System.Drawing.Point(12, 12);
            this.groupBoxGeneral.Name = "groupBoxGeneral";
            this.groupBoxGeneral.Size = new System.Drawing.Size(1053, 180);
            this.groupBoxGeneral.TabIndex = 4;
            this.groupBoxGeneral.TabStop = false;
            this.groupBoxGeneral.Text = "General";
            // 
            // labelName
            // 
            this.labelName.AutoSize = true;
            this.labelName.Location = new System.Drawing.Point(20, 35);
            this.labelName.Name = "labelName";
            this.labelName.Size = new System.Drawing.Size(87, 16);
            this.labelName.TabIndex = 0;
            this.labelName.Text = "Squad Name";
            // 
            // textBoxName
            // 
            this.textBoxName.Location = new System.Drawing.Point(140, 32);
            this.textBoxName.Name = "textBoxName";
            this.textBoxName.Size = new System.Drawing.Size(260, 22);
            this.textBoxName.TabIndex = 1;
            // 
            // labelType
            // 
            this.labelType.AutoSize = true;
            this.labelType.Location = new System.Drawing.Point(20, 75);
            this.labelType.Name = "labelType";
            this.labelType.Size = new System.Drawing.Size(83, 16);
            this.labelType.TabIndex = 2;
            this.labelType.Text = "Aircraft Type";
            // 
            // comboBoxType
            // 
            this.comboBoxType.Location = new System.Drawing.Point(140, 72);
            this.comboBoxType.Name = "comboBoxType";
            this.comboBoxType.Size = new System.Drawing.Size(260, 24);
            this.comboBoxType.TabIndex = 3;
            // 
            // labelCountry
            // 
            this.labelCountry.AutoSize = true;
            this.labelCountry.Location = new System.Drawing.Point(20, 115);
            this.labelCountry.Name = "labelCountry";
            this.labelCountry.Size = new System.Drawing.Size(52, 16);
            this.labelCountry.TabIndex = 4;
            this.labelCountry.Text = "Country";
            // 
            // comboBoxCountry
            // 
            this.comboBoxCountry.Location = new System.Drawing.Point(140, 112);
            this.comboBoxCountry.Name = "comboBoxCountry";
            this.comboBoxCountry.Size = new System.Drawing.Size(260, 24);
            this.comboBoxCountry.TabIndex = 5;
            // 
            // labelBase
            // 
            this.labelBase.AutoSize = true;
            this.labelBase.Location = new System.Drawing.Point(470, 35);
            this.labelBase.Name = "labelBase";
            this.labelBase.Size = new System.Drawing.Size(39, 16);
            this.labelBase.TabIndex = 6;
            this.labelBase.Text = "Base";
            // 
            // comboBoxBase
            // 
            this.comboBoxBase.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.comboBoxBase.Location = new System.Drawing.Point(560, 32);
            this.comboBoxBase.Name = "comboBoxBase";
            this.comboBoxBase.Size = new System.Drawing.Size(280, 24);
            this.comboBoxBase.TabIndex = 7;
            // 
            // labelSkill
            // 
            this.labelSkill.AutoSize = true;
            this.labelSkill.Location = new System.Drawing.Point(470, 75);
            this.labelSkill.Name = "labelSkill";
            this.labelSkill.Size = new System.Drawing.Size(32, 16);
            this.labelSkill.TabIndex = 8;
            this.labelSkill.Text = "Skill";
            // 
            // comboBoxSkill
            // 
            this.comboBoxSkill.Items.AddRange(new object[] {
            "Random",
            "Average",
            "Good",
            "High",
            "Excellent"});
            this.comboBoxSkill.Location = new System.Drawing.Point(560, 72);
            this.comboBoxSkill.Name = "comboBoxSkill";
            this.comboBoxSkill.Size = new System.Drawing.Size(180, 24);
            this.comboBoxSkill.TabIndex = 9;
            // 
            // checkBoxPlayer
            // 
            this.checkBoxPlayer.AutoSize = true;
            this.checkBoxPlayer.Location = new System.Drawing.Point(920, 35);
            this.checkBoxPlayer.Name = "checkBoxPlayer";
            this.checkBoxPlayer.Size = new System.Drawing.Size(109, 20);
            this.checkBoxPlayer.TabIndex = 10;
            this.checkBoxPlayer.Text = "Player squad";
            // 
            // checkBoxInactive
            // 
            this.checkBoxInactive.AutoSize = true;
            this.checkBoxInactive.Location = new System.Drawing.Point(920, 70);
            this.checkBoxInactive.Name = "checkBoxInactive";
            this.checkBoxInactive.Size = new System.Drawing.Size(75, 20);
            this.checkBoxInactive.TabIndex = 11;
            this.checkBoxInactive.Text = "Inactive";
            // 
            // buttonOK
            // 
            this.buttonOK.Location = new System.Drawing.Point(742, 1117);
            this.buttonOK.Name = "buttonOK";
            this.buttonOK.Size = new System.Drawing.Size(120, 36);
            this.buttonOK.TabIndex = 5;
            this.buttonOK.Text = "Save";
            this.buttonOK.Click += new System.EventHandler(this.buttonOK_Click);
            // 
            // buttonCancel
            // 
            this.buttonCancel.Location = new System.Drawing.Point(887, 1117);
            this.buttonCancel.Name = "buttonCancel";
            this.buttonCancel.Size = new System.Drawing.Size(120, 36);
            this.buttonCancel.TabIndex = 6;
            this.buttonCancel.Text = "Cancel";
            this.buttonCancel.Click += new System.EventHandler(this.buttonCancel_Click);
            // 
            // FormSquadEdit
            // 
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.None;
            this.ClientSize = new System.Drawing.Size(1282, 956);
            this.Controls.Add(this.panelMain);
            this.MinimumSize = new System.Drawing.Size(1300, 800);
            this.Name = "FormSquadEdit";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            this.Text = "Squad Editor";
            //this.WindowState = System.Windows.Forms.FormWindowState.Maximized;
            //this.StartPosition = FormStartPosition.CenterParent;
            //this.WindowState = FormWindowState.Normal;
            //this.Size = new Size(1200, 850);
            this.panelMain.ResumeLayout(false);
            this.groupBoxAdditional.ResumeLayout(false);
            this.groupBoxScore.ResumeLayout(false);
            this.groupBoxTasks.ResumeLayout(false);
            this.groupBoxAircraft.ResumeLayout(false);
            this.groupBoxAircraft.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.numericNumber)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.numericInitNumber)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.numericReserve)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.numericInitReserve)).EndInit();
            this.groupBoxGeneral.ResumeLayout(false);
            this.groupBoxGeneral.PerformLayout();
            this.ResumeLayout(false);

        }

        #endregion

        private System.Windows.Forms.Panel panelMain;

        private System.Windows.Forms.GroupBox groupBoxGeneral;
        private System.Windows.Forms.GroupBox groupBoxAircraft;
        private System.Windows.Forms.GroupBox groupBoxTasks;
        private System.Windows.Forms.GroupBox groupBoxScore;
        private System.Windows.Forms.GroupBox groupBoxAdditional;

        private System.Windows.Forms.TextBox textBoxName;
        private System.Windows.Forms.ComboBox comboBoxType;
        private System.Windows.Forms.ComboBox comboBoxCountry;
        private System.Windows.Forms.ComboBox comboBoxBase;
        private System.Windows.Forms.ComboBox comboBoxSkill;

        private System.Windows.Forms.CheckBox checkBoxPlayer;
        private System.Windows.Forms.CheckBox checkBoxInactive;

        private System.Windows.Forms.NumericUpDown numericNumber;
        private System.Windows.Forms.NumericUpDown numericInitNumber;
        private System.Windows.Forms.NumericUpDown numericReserve;
        private System.Windows.Forms.NumericUpDown numericInitReserve;

        private System.Windows.Forms.FlowLayoutPanel flowLayoutPanelTasks;
        private System.Windows.Forms.FlowLayoutPanel flowLayoutPanelScore;
        private System.Windows.Forms.FlowLayoutPanel flowLayoutPanelAdditional;

        private System.Windows.Forms.Button buttonOK;
        private System.Windows.Forms.Button buttonCancel;
        private Label labelNumber;
        private Label labelInitNumber;
        private Label labelReserve;
        private Label labelInitReserve;
        private Label labelName;
        private Label labelType;
        private Label labelCountry;
        private Label labelBase;
        private Label labelSkill;
    }
}
