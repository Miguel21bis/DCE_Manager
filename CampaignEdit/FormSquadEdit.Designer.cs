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
            this.tabControl_SquadEditMain = new System.Windows.Forms.TabControl();
            this.tabPage1 = new System.Windows.Forms.TabPage();
            this.groupBoxGeneral = new System.Windows.Forms.GroupBox();
            this.checkBox_HumainOnly = new System.Windows.Forms.CheckBox();
            this.label_Callsign = new System.Windows.Forms.Label();
            this.comboBox_Callsign = new System.Windows.Forms.ComboBox();
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
            this.groupBox_Bases = new System.Windows.Forms.GroupBox();
            this.button_Base_Haut = new System.Windows.Forms.Button();
            this.button_Base_Down = new System.Windows.Forms.Button();
            this.button_Base_Moins = new System.Windows.Forms.Button();
            this.button_base_plus = new System.Windows.Forms.Button();
            this.comboBox_All_bases = new System.Windows.Forms.ComboBox();
            this.listBoxBasesAlternat = new System.Windows.Forms.ListBox();
            this.labelBasesAdd = new System.Windows.Forms.Label();
            this.groupBoxAircraft = new System.Windows.Forms.GroupBox();
            this.labelNumber = new System.Windows.Forms.Label();
            this.numericNumber = new System.Windows.Forms.NumericUpDown();
            this.labelInitNumber = new System.Windows.Forms.Label();
            this.numericInitNumber = new System.Windows.Forms.NumericUpDown();
            this.labelReserve = new System.Windows.Forms.Label();
            this.numericReserve = new System.Windows.Forms.NumericUpDown();
            this.labelInitReserve = new System.Windows.Forms.Label();
            this.numericInitReserve = new System.Windows.Forms.NumericUpDown();
            this.groupBoxTasks = new System.Windows.Forms.GroupBox();
            this.flowLayoutPanelTasks = new System.Windows.Forms.FlowLayoutPanel();
            this.tabPage2 = new System.Windows.Forms.TabPage();
            this.groupBox_sidenumber = new System.Windows.Forms.GroupBox();
            this.label_ModexNb_Max = new System.Windows.Forms.Label();
            this.label_ModexNb_Min = new System.Windows.Forms.Label();
            this.textBox_ModexNb_Max = new System.Windows.Forms.TextBox();
            this.textBox_ModexNb_Min = new System.Windows.Forms.TextBox();
            this.groupBox_Livery = new System.Windows.Forms.GroupBox();
            this.button_RemoveSkin = new System.Windows.Forms.Button();
            this.listBox_Livery = new System.Windows.Forms.ListBox();
            this.button_AddSkin = new System.Windows.Forms.Button();
            this.textBox_AddSkin = new System.Windows.Forms.TextBox();
            this.groupBox_MODEX = new System.Windows.Forms.GroupBox();
            this.comboBox_LiveryM = new System.Windows.Forms.ComboBox();
            this.button_LiveryModex_Moins = new System.Windows.Forms.Button();
            this.listBox_LiveryModex = new System.Windows.Forms.ListBox();
            this.button_LiveryModex_Plus = new System.Windows.Forms.Button();
            this.textBox_Modex = new System.Windows.Forms.TextBox();
            this.tabPage3 = new System.Windows.Forms.TabPage();
            this.groupBoxScore = new System.Windows.Forms.GroupBox();
            this.flowLayoutPanelScore = new System.Windows.Forms.FlowLayoutPanel();
            this.buttonOK = new System.Windows.Forms.Button();
            this.buttonCancel = new System.Windows.Forms.Button();
            this.tabPage4 = new System.Windows.Forms.TabPage();
            this.groupBox_PanelTables = new System.Windows.Forms.GroupBox();
            this.flowLayoutPanelTables = new System.Windows.Forms.FlowLayoutPanel();
            this.groupBox_ParkingId = new System.Windows.Forms.GroupBox();
            this.listBox_ParkingId = new System.Windows.Forms.ListBox();
            this.button_ParkingId_Remove = new System.Windows.Forms.Button();
            this.button_ParkingId_Add = new System.Windows.Forms.Button();
            this.textBox_ParkingId_Int = new System.Windows.Forms.TextBox();
            this.textBox_ParkingId_Prefix = new System.Windows.Forms.TextBox();
            this.label_parkingId_LetterPark = new System.Windows.Forms.Label();
            this.label_parkingId_serial = new System.Windows.Forms.Label();
            this.panelMain.SuspendLayout();
            this.tabControl_SquadEditMain.SuspendLayout();
            this.tabPage1.SuspendLayout();
            this.groupBoxGeneral.SuspendLayout();
            this.groupBox_Bases.SuspendLayout();
            this.groupBoxAircraft.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.numericNumber)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.numericInitNumber)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.numericReserve)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.numericInitReserve)).BeginInit();
            this.groupBoxTasks.SuspendLayout();
            this.tabPage2.SuspendLayout();
            this.groupBox_sidenumber.SuspendLayout();
            this.groupBox_Livery.SuspendLayout();
            this.groupBox_MODEX.SuspendLayout();
            this.tabPage3.SuspendLayout();
            this.groupBoxScore.SuspendLayout();
            this.tabPage4.SuspendLayout();
            this.groupBox_PanelTables.SuspendLayout();
            this.groupBox_ParkingId.SuspendLayout();
            this.SuspendLayout();
            // 
            // panelMain
            // 
            this.panelMain.AutoScroll = true;
            this.panelMain.Controls.Add(this.tabControl_SquadEditMain);
            this.panelMain.Controls.Add(this.buttonOK);
            this.panelMain.Controls.Add(this.buttonCancel);
            this.panelMain.Dock = System.Windows.Forms.DockStyle.Fill;
            this.panelMain.Location = new System.Drawing.Point(0, 0);
            this.panelMain.Name = "panelMain";
            this.panelMain.Size = new System.Drawing.Size(1282, 917);
            this.panelMain.TabIndex = 0;
            // 
            // tabControl_SquadEditMain
            // 
            this.tabControl_SquadEditMain.Controls.Add(this.tabPage1);
            this.tabControl_SquadEditMain.Controls.Add(this.tabPage2);
            this.tabControl_SquadEditMain.Controls.Add(this.tabPage3);
            this.tabControl_SquadEditMain.Controls.Add(this.tabPage4);
            this.tabControl_SquadEditMain.Location = new System.Drawing.Point(3, 3);
            this.tabControl_SquadEditMain.Name = "tabControl_SquadEditMain";
            this.tabControl_SquadEditMain.SelectedIndex = 0;
            this.tabControl_SquadEditMain.Size = new System.Drawing.Size(1267, 834);
            this.tabControl_SquadEditMain.TabIndex = 12;
            // 
            // tabPage1
            // 
            this.tabPage1.Controls.Add(this.groupBox_ParkingId);
            this.tabPage1.Controls.Add(this.groupBoxGeneral);
            this.tabPage1.Controls.Add(this.groupBox_Bases);
            this.tabPage1.Controls.Add(this.groupBoxAircraft);
            this.tabPage1.Controls.Add(this.groupBoxTasks);
            this.tabPage1.Location = new System.Drawing.Point(4, 25);
            this.tabPage1.Name = "tabPage1";
            this.tabPage1.Padding = new System.Windows.Forms.Padding(3);
            this.tabPage1.Size = new System.Drawing.Size(1259, 805);
            this.tabPage1.TabIndex = 0;
            this.tabPage1.Text = "General";
            this.tabPage1.UseVisualStyleBackColor = true;
            // 
            // groupBoxGeneral
            // 
            this.groupBoxGeneral.Controls.Add(this.checkBox_HumainOnly);
            this.groupBoxGeneral.Controls.Add(this.label_Callsign);
            this.groupBoxGeneral.Controls.Add(this.comboBox_Callsign);
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
            this.groupBoxGeneral.Location = new System.Drawing.Point(9, 27);
            this.groupBoxGeneral.Name = "groupBoxGeneral";
            this.groupBoxGeneral.Size = new System.Drawing.Size(811, 180);
            this.groupBoxGeneral.TabIndex = 4;
            this.groupBoxGeneral.TabStop = false;
            this.groupBoxGeneral.Text = "General";
            // 
            // checkBox_HumainOnly
            // 
            this.checkBox_HumainOnly.AutoSize = true;
            this.checkBox_HumainOnly.Location = new System.Drawing.Point(683, 75);
            this.checkBox_HumainOnly.Name = "checkBox_HumainOnly";
            this.checkBox_HumainOnly.Size = new System.Drawing.Size(105, 20);
            this.checkBox_HumainOnly.TabIndex = 14;
            this.checkBox_HumainOnly.Text = "Humain Only";
            // 
            // label_Callsign
            // 
            this.label_Callsign.AutoSize = true;
            this.label_Callsign.Location = new System.Drawing.Point(403, 74);
            this.label_Callsign.Name = "label_Callsign";
            this.label_Callsign.Size = new System.Drawing.Size(55, 16);
            this.label_Callsign.TabIndex = 13;
            this.label_Callsign.Text = "Callsign";
            // 
            // comboBox_Callsign
            // 
            this.comboBox_Callsign.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.comboBox_Callsign.Location = new System.Drawing.Point(467, 71);
            this.comboBox_Callsign.Name = "comboBox_Callsign";
            this.comboBox_Callsign.Size = new System.Drawing.Size(180, 24);
            this.comboBox_Callsign.TabIndex = 12;
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
            this.textBoxName.Size = new System.Drawing.Size(239, 22);
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
            this.comboBoxType.Size = new System.Drawing.Size(239, 24);
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
            this.comboBoxCountry.Size = new System.Drawing.Size(239, 24);
            this.comboBoxCountry.TabIndex = 5;
            // 
            // labelBase
            // 
            this.labelBase.AutoSize = true;
            this.labelBase.Location = new System.Drawing.Point(403, 38);
            this.labelBase.Name = "labelBase";
            this.labelBase.Size = new System.Drawing.Size(39, 16);
            this.labelBase.TabIndex = 6;
            this.labelBase.Text = "Base";
            // 
            // comboBoxBase
            // 
            this.comboBoxBase.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.comboBoxBase.Location = new System.Drawing.Point(467, 30);
            this.comboBoxBase.Name = "comboBoxBase";
            this.comboBoxBase.Size = new System.Drawing.Size(180, 24);
            this.comboBoxBase.TabIndex = 7;
            this.comboBoxBase.SelectedIndexChanged += new System.EventHandler(this.comboBoxBase_SelectedIndexChanged);
            // 
            // labelSkill
            // 
            this.labelSkill.AutoSize = true;
            this.labelSkill.Location = new System.Drawing.Point(403, 120);
            this.labelSkill.Name = "labelSkill";
            this.labelSkill.Size = new System.Drawing.Size(32, 16);
            this.labelSkill.TabIndex = 8;
            this.labelSkill.Text = "Skill";
            this.labelSkill.Click += new System.EventHandler(this.labelSkill_Click);
            // 
            // comboBoxSkill
            // 
            this.comboBoxSkill.Items.AddRange(new object[] {
            "Random",
            "Average",
            "Good",
            "High",
            "Excellent"});
            this.comboBoxSkill.Location = new System.Drawing.Point(467, 112);
            this.comboBoxSkill.Name = "comboBoxSkill";
            this.comboBoxSkill.Size = new System.Drawing.Size(180, 24);
            this.comboBoxSkill.TabIndex = 9;
            // 
            // checkBoxPlayer
            // 
            this.checkBoxPlayer.AutoSize = true;
            this.checkBoxPlayer.Location = new System.Drawing.Point(683, 38);
            this.checkBoxPlayer.Name = "checkBoxPlayer";
            this.checkBoxPlayer.Size = new System.Drawing.Size(109, 20);
            this.checkBoxPlayer.TabIndex = 10;
            this.checkBoxPlayer.Text = "Player squad";
            // 
            // checkBoxInactive
            // 
            this.checkBoxInactive.AutoSize = true;
            this.checkBoxInactive.Location = new System.Drawing.Point(683, 116);
            this.checkBoxInactive.Name = "checkBoxInactive";
            this.checkBoxInactive.Size = new System.Drawing.Size(75, 20);
            this.checkBoxInactive.TabIndex = 11;
            this.checkBoxInactive.Text = "Inactive";
            // 
            // groupBox_Bases
            // 
            this.groupBox_Bases.Controls.Add(this.button_Base_Haut);
            this.groupBox_Bases.Controls.Add(this.button_Base_Down);
            this.groupBox_Bases.Controls.Add(this.button_Base_Moins);
            this.groupBox_Bases.Controls.Add(this.button_base_plus);
            this.groupBox_Bases.Controls.Add(this.comboBox_All_bases);
            this.groupBox_Bases.Controls.Add(this.listBoxBasesAlternat);
            this.groupBox_Bases.Controls.Add(this.labelBasesAdd);
            this.groupBox_Bases.Location = new System.Drawing.Point(624, 226);
            this.groupBox_Bases.Name = "groupBox_Bases";
            this.groupBox_Bases.Size = new System.Drawing.Size(616, 130);
            this.groupBox_Bases.TabIndex = 8;
            this.groupBox_Bases.TabStop = false;
            this.groupBox_Bases.Text = "Alternatives AirBases";
            // 
            // button_Base_Haut
            // 
            this.button_Base_Haut.Location = new System.Drawing.Point(261, 27);
            this.button_Base_Haut.Name = "button_Base_Haut";
            this.button_Base_Haut.Size = new System.Drawing.Size(33, 30);
            this.button_Base_Haut.TabIndex = 20;
            this.button_Base_Haut.Text = "▲";
            this.button_Base_Haut.UseVisualStyleBackColor = true;
            this.button_Base_Haut.Click += new System.EventHandler(this.button_Base_Haut_Click);
            // 
            // button_Base_Down
            // 
            this.button_Base_Down.Location = new System.Drawing.Point(261, 94);
            this.button_Base_Down.Name = "button_Base_Down";
            this.button_Base_Down.Size = new System.Drawing.Size(33, 30);
            this.button_Base_Down.TabIndex = 19;
            this.button_Base_Down.Text = "▼";
            this.button_Base_Down.UseVisualStyleBackColor = true;
            this.button_Base_Down.Click += new System.EventHandler(this.button_Base_Down_Click);
            // 
            // button_Base_Moins
            // 
            this.button_Base_Moins.Location = new System.Drawing.Point(261, 61);
            this.button_Base_Moins.Name = "button_Base_Moins";
            this.button_Base_Moins.Size = new System.Drawing.Size(33, 30);
            this.button_Base_Moins.TabIndex = 18;
            this.button_Base_Moins.Text = "➖";
            this.button_Base_Moins.UseVisualStyleBackColor = true;
            this.button_Base_Moins.Click += new System.EventHandler(this.buttonBaseMoins_Click);
            // 
            // button_base_plus
            // 
            this.button_base_plus.Location = new System.Drawing.Point(294, 61);
            this.button_base_plus.Name = "button_base_plus";
            this.button_base_plus.Size = new System.Drawing.Size(33, 30);
            this.button_base_plus.TabIndex = 17;
            this.button_base_plus.Text = "➕";
            this.button_base_plus.UseVisualStyleBackColor = true;
            this.button_base_plus.Click += new System.EventHandler(this.button_base_plus_Click);
            // 
            // comboBox_All_bases
            // 
            this.comboBox_All_bases.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.comboBox_All_bases.Location = new System.Drawing.Point(16, 64);
            this.comboBox_All_bases.Name = "comboBox_All_bases";
            this.comboBox_All_bases.Size = new System.Drawing.Size(239, 24);
            this.comboBox_All_bases.TabIndex = 14;
            // 
            // listBoxBasesAlternat
            // 
            this.listBoxBasesAlternat.FormattingEnabled = true;
            this.listBoxBasesAlternat.ItemHeight = 16;
            this.listBoxBasesAlternat.Location = new System.Drawing.Point(380, 40);
            this.listBoxBasesAlternat.Name = "listBoxBasesAlternat";
            this.listBoxBasesAlternat.Size = new System.Drawing.Size(200, 84);
            this.listBoxBasesAlternat.TabIndex = 21;
            // 
            // labelBasesAdd
            // 
            this.labelBasesAdd.AutoSize = true;
            this.labelBasesAdd.Location = new System.Drawing.Point(426, 42);
            this.labelBasesAdd.Name = "labelBasesAdd";
            this.labelBasesAdd.Size = new System.Drawing.Size(98, 16);
            this.labelBasesAdd.TabIndex = 12;
            this.labelBasesAdd.Text = "Alternatives AB";
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
            this.groupBoxAircraft.Location = new System.Drawing.Point(9, 226);
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
            // groupBoxTasks
            // 
            this.groupBoxTasks.Controls.Add(this.flowLayoutPanelTasks);
            this.groupBoxTasks.Location = new System.Drawing.Point(9, 362);
            this.groupBoxTasks.Name = "groupBoxTasks";
            this.groupBoxTasks.Size = new System.Drawing.Size(593, 380);
            this.groupBoxTasks.TabIndex = 2;
            this.groupBoxTasks.TabStop = false;
            this.groupBoxTasks.Text = "Tasks / Coefficients";
            // 
            // flowLayoutPanelTasks
            // 
            this.flowLayoutPanelTasks.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.flowLayoutPanelTasks.AutoScroll = true;
            this.flowLayoutPanelTasks.FlowDirection = System.Windows.Forms.FlowDirection.TopDown;
            this.flowLayoutPanelTasks.Location = new System.Drawing.Point(15, 25);
            this.flowLayoutPanelTasks.Name = "flowLayoutPanelTasks";
            this.flowLayoutPanelTasks.Size = new System.Drawing.Size(572, 349);
            this.flowLayoutPanelTasks.TabIndex = 0;
            this.flowLayoutPanelTasks.WrapContents = false;
            // 
            // tabPage2
            // 
            this.tabPage2.Controls.Add(this.groupBox_sidenumber);
            this.tabPage2.Controls.Add(this.groupBox_Livery);
            this.tabPage2.Controls.Add(this.groupBox_MODEX);
            this.tabPage2.Location = new System.Drawing.Point(4, 25);
            this.tabPage2.Name = "tabPage2";
            this.tabPage2.Padding = new System.Windows.Forms.Padding(3);
            this.tabPage2.Size = new System.Drawing.Size(1259, 888);
            this.tabPage2.TabIndex = 1;
            this.tabPage2.Text = "Livery/MODEX";
            this.tabPage2.UseVisualStyleBackColor = true;
            // 
            // groupBox_sidenumber
            // 
            this.groupBox_sidenumber.Controls.Add(this.label_ModexNb_Max);
            this.groupBox_sidenumber.Controls.Add(this.label_ModexNb_Min);
            this.groupBox_sidenumber.Controls.Add(this.textBox_ModexNb_Max);
            this.groupBox_sidenumber.Controls.Add(this.textBox_ModexNb_Min);
            this.groupBox_sidenumber.Location = new System.Drawing.Point(30, 25);
            this.groupBox_sidenumber.Name = "groupBox_sidenumber";
            this.groupBox_sidenumber.Size = new System.Drawing.Size(185, 105);
            this.groupBox_sidenumber.TabIndex = 12;
            this.groupBox_sidenumber.TabStop = false;
            this.groupBox_sidenumber.Text = "MODEX Number";
            // 
            // label_ModexNb_Max
            // 
            this.label_ModexNb_Max.AutoSize = true;
            this.label_ModexNb_Max.Location = new System.Drawing.Point(17, 74);
            this.label_ModexNb_Max.Name = "label_ModexNb_Max";
            this.label_ModexNb_Max.Size = new System.Drawing.Size(32, 16);
            this.label_ModexNb_Max.TabIndex = 18;
            this.label_ModexNb_Max.Text = "Max";
            // 
            // label_ModexNb_Min
            // 
            this.label_ModexNb_Min.AutoSize = true;
            this.label_ModexNb_Min.Location = new System.Drawing.Point(17, 37);
            this.label_ModexNb_Min.Name = "label_ModexNb_Min";
            this.label_ModexNb_Min.Size = new System.Drawing.Size(28, 16);
            this.label_ModexNb_Min.TabIndex = 17;
            this.label_ModexNb_Min.Text = "Min";
            // 
            // textBox_ModexNb_Max
            // 
            this.textBox_ModexNb_Max.Location = new System.Drawing.Point(100, 68);
            this.textBox_ModexNb_Max.Name = "textBox_ModexNb_Max";
            this.textBox_ModexNb_Max.Size = new System.Drawing.Size(49, 22);
            this.textBox_ModexNb_Max.TabIndex = 16;
            // 
            // textBox_ModexNb_Min
            // 
            this.textBox_ModexNb_Min.Location = new System.Drawing.Point(100, 31);
            this.textBox_ModexNb_Min.Name = "textBox_ModexNb_Min";
            this.textBox_ModexNb_Min.Size = new System.Drawing.Size(49, 22);
            this.textBox_ModexNb_Min.TabIndex = 15;
            // 
            // groupBox_Livery
            // 
            this.groupBox_Livery.Controls.Add(this.button_RemoveSkin);
            this.groupBox_Livery.Controls.Add(this.listBox_Livery);
            this.groupBox_Livery.Controls.Add(this.button_AddSkin);
            this.groupBox_Livery.Controls.Add(this.textBox_AddSkin);
            this.groupBox_Livery.Location = new System.Drawing.Point(30, 158);
            this.groupBox_Livery.Name = "groupBox_Livery";
            this.groupBox_Livery.Size = new System.Drawing.Size(578, 123);
            this.groupBox_Livery.TabIndex = 7;
            this.groupBox_Livery.TabStop = false;
            this.groupBox_Livery.Text = "Livery";
            // 
            // button_RemoveSkin
            // 
            this.button_RemoveSkin.Location = new System.Drawing.Point(279, 75);
            this.button_RemoveSkin.Name = "button_RemoveSkin";
            this.button_RemoveSkin.Size = new System.Drawing.Size(33, 30);
            this.button_RemoveSkin.TabIndex = 23;
            this.button_RemoveSkin.Text = "➖";
            this.button_RemoveSkin.UseVisualStyleBackColor = true;
            this.button_RemoveSkin.Click += new System.EventHandler(this.button_RemoveSkin_Click);
            // 
            // listBox_Livery
            // 
            this.listBox_Livery.FormattingEnabled = true;
            this.listBox_Livery.ItemHeight = 16;
            this.listBox_Livery.Location = new System.Drawing.Point(329, 21);
            this.listBox_Livery.Name = "listBox_Livery";
            this.listBox_Livery.Size = new System.Drawing.Size(237, 84);
            this.listBox_Livery.TabIndex = 22;
            // 
            // button_AddSkin
            // 
            this.button_AddSkin.Location = new System.Drawing.Point(279, 29);
            this.button_AddSkin.Name = "button_AddSkin";
            this.button_AddSkin.Size = new System.Drawing.Size(33, 30);
            this.button_AddSkin.TabIndex = 16;
            this.button_AddSkin.Text = "➕";
            this.button_AddSkin.UseVisualStyleBackColor = true;
            this.button_AddSkin.Click += new System.EventHandler(this.button_AddSkin_Click);
            // 
            // textBox_AddSkin
            // 
            this.textBox_AddSkin.Location = new System.Drawing.Point(8, 30);
            this.textBox_AddSkin.Name = "textBox_AddSkin";
            this.textBox_AddSkin.Size = new System.Drawing.Size(249, 22);
            this.textBox_AddSkin.TabIndex = 15;
            // 
            // groupBox_MODEX
            // 
            this.groupBox_MODEX.Controls.Add(this.comboBox_LiveryM);
            this.groupBox_MODEX.Controls.Add(this.button_LiveryModex_Moins);
            this.groupBox_MODEX.Controls.Add(this.listBox_LiveryModex);
            this.groupBox_MODEX.Controls.Add(this.button_LiveryModex_Plus);
            this.groupBox_MODEX.Controls.Add(this.textBox_Modex);
            this.groupBox_MODEX.Location = new System.Drawing.Point(30, 310);
            this.groupBox_MODEX.Name = "groupBox_MODEX";
            this.groupBox_MODEX.Size = new System.Drawing.Size(578, 123);
            this.groupBox_MODEX.TabIndex = 10;
            this.groupBox_MODEX.TabStop = false;
            this.groupBox_MODEX.Text = "MODEX Livery";
            // 
            // comboBox_LiveryM
            // 
            this.comboBox_LiveryM.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.comboBox_LiveryM.Location = new System.Drawing.Point(77, 59);
            this.comboBox_LiveryM.Name = "comboBox_LiveryM";
            this.comboBox_LiveryM.Size = new System.Drawing.Size(180, 24);
            this.comboBox_LiveryM.TabIndex = 24;
            // 
            // button_LiveryModex_Moins
            // 
            this.button_LiveryModex_Moins.Location = new System.Drawing.Point(279, 75);
            this.button_LiveryModex_Moins.Name = "button_LiveryModex_Moins";
            this.button_LiveryModex_Moins.Size = new System.Drawing.Size(33, 30);
            this.button_LiveryModex_Moins.TabIndex = 23;
            this.button_LiveryModex_Moins.Text = "➖";
            this.button_LiveryModex_Moins.UseVisualStyleBackColor = true;
            this.button_LiveryModex_Moins.Click += new System.EventHandler(this.button_LiveryModex_Moins_Click);
            // 
            // listBox_LiveryModex
            // 
            this.listBox_LiveryModex.FormattingEnabled = true;
            this.listBox_LiveryModex.ItemHeight = 16;
            this.listBox_LiveryModex.Location = new System.Drawing.Point(329, 21);
            this.listBox_LiveryModex.Name = "listBox_LiveryModex";
            this.listBox_LiveryModex.Size = new System.Drawing.Size(237, 84);
            this.listBox_LiveryModex.TabIndex = 22;
            // 
            // button_LiveryModex_Plus
            // 
            this.button_LiveryModex_Plus.Location = new System.Drawing.Point(279, 29);
            this.button_LiveryModex_Plus.Name = "button_LiveryModex_Plus";
            this.button_LiveryModex_Plus.Size = new System.Drawing.Size(33, 30);
            this.button_LiveryModex_Plus.TabIndex = 16;
            this.button_LiveryModex_Plus.Text = "➕";
            this.button_LiveryModex_Plus.UseVisualStyleBackColor = true;
            this.button_LiveryModex_Plus.Click += new System.EventHandler(this.button_LiveryModex_Plus_Click);
            // 
            // textBox_Modex
            // 
            this.textBox_Modex.Location = new System.Drawing.Point(8, 61);
            this.textBox_Modex.Name = "textBox_Modex";
            this.textBox_Modex.Size = new System.Drawing.Size(49, 22);
            this.textBox_Modex.TabIndex = 15;
            // 
            // tabPage3
            // 
            this.tabPage3.Controls.Add(this.groupBoxScore);
            this.tabPage3.Location = new System.Drawing.Point(4, 25);
            this.tabPage3.Name = "tabPage3";
            this.tabPage3.Padding = new System.Windows.Forms.Padding(3);
            this.tabPage3.Size = new System.Drawing.Size(1259, 888);
            this.tabPage3.TabIndex = 2;
            this.tabPage3.Text = "Roster";
            this.tabPage3.UseVisualStyleBackColor = true;
            // 
            // groupBoxScore
            // 
            this.groupBoxScore.Controls.Add(this.flowLayoutPanelScore);
            this.groupBoxScore.Location = new System.Drawing.Point(68, 36);
            this.groupBoxScore.Name = "groupBoxScore";
            this.groupBoxScore.Size = new System.Drawing.Size(1076, 485);
            this.groupBoxScore.TabIndex = 1;
            this.groupBoxScore.TabStop = false;
            this.groupBoxScore.Text = "Roster / Score / Last Mission";
            // 
            // flowLayoutPanelScore
            // 
            this.flowLayoutPanelScore.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.flowLayoutPanelScore.AutoScroll = true;
            this.flowLayoutPanelScore.FlowDirection = System.Windows.Forms.FlowDirection.TopDown;
            this.flowLayoutPanelScore.Location = new System.Drawing.Point(15, 21);
            this.flowLayoutPanelScore.Name = "flowLayoutPanelScore";
            this.flowLayoutPanelScore.Size = new System.Drawing.Size(1041, 445);
            this.flowLayoutPanelScore.TabIndex = 0;
            this.flowLayoutPanelScore.WrapContents = false;
            // 
            // buttonOK
            // 
            this.buttonOK.Location = new System.Drawing.Point(1045, 856);
            this.buttonOK.Name = "buttonOK";
            this.buttonOK.Size = new System.Drawing.Size(100, 36);
            this.buttonOK.TabIndex = 5;
            this.buttonOK.Text = "Save";
            this.buttonOK.Click += new System.EventHandler(this.buttonOK_Click);
            // 
            // buttonCancel
            // 
            this.buttonCancel.Location = new System.Drawing.Point(1161, 856);
            this.buttonCancel.Name = "buttonCancel";
            this.buttonCancel.Size = new System.Drawing.Size(100, 36);
            this.buttonCancel.TabIndex = 6;
            this.buttonCancel.Text = "Cancel";
            this.buttonCancel.Click += new System.EventHandler(this.buttonCancel_Click);
            // 
            // tabPage4
            // 
            this.tabPage4.Controls.Add(this.groupBox_PanelTables);
            this.tabPage4.Location = new System.Drawing.Point(4, 25);
            this.tabPage4.Name = "tabPage4";
            this.tabPage4.Size = new System.Drawing.Size(1259, 888);
            this.tabPage4.TabIndex = 3;
            this.tabPage4.Text = "Divers";
            this.tabPage4.UseVisualStyleBackColor = true;
            // 
            // groupBox_PanelTables
            // 
            this.groupBox_PanelTables.Controls.Add(this.flowLayoutPanelTables);
            this.groupBox_PanelTables.Location = new System.Drawing.Point(16, 19);
            this.groupBox_PanelTables.Name = "groupBox_PanelTables";
            this.groupBox_PanelTables.Size = new System.Drawing.Size(697, 705);
            this.groupBox_PanelTables.TabIndex = 10;
            this.groupBox_PanelTables.TabStop = false;
            this.groupBox_PanelTables.Text = "Panel Table";
            // 
            // flowLayoutPanelTables
            // 
            this.flowLayoutPanelTables.AutoScroll = true;
            this.flowLayoutPanelTables.Dock = System.Windows.Forms.DockStyle.Fill;
            this.flowLayoutPanelTables.FlowDirection = System.Windows.Forms.FlowDirection.TopDown;
            this.flowLayoutPanelTables.Location = new System.Drawing.Point(3, 18);
            this.flowLayoutPanelTables.Name = "flowLayoutPanelTables";
            this.flowLayoutPanelTables.Size = new System.Drawing.Size(691, 684);
            this.flowLayoutPanelTables.TabIndex = 0;
            this.flowLayoutPanelTables.WrapContents = false;
            // 
            // groupBox_ParkingId
            // 
            this.groupBox_ParkingId.Controls.Add(this.label_parkingId_serial);
            this.groupBox_ParkingId.Controls.Add(this.label_parkingId_LetterPark);
            this.groupBox_ParkingId.Controls.Add(this.textBox_ParkingId_Prefix);
            this.groupBox_ParkingId.Controls.Add(this.textBox_ParkingId_Int);
            this.groupBox_ParkingId.Controls.Add(this.button_ParkingId_Remove);
            this.groupBox_ParkingId.Controls.Add(this.button_ParkingId_Add);
            this.groupBox_ParkingId.Controls.Add(this.listBox_ParkingId);
            this.groupBox_ParkingId.Location = new System.Drawing.Point(624, 374);
            this.groupBox_ParkingId.Name = "groupBox_ParkingId";
            this.groupBox_ParkingId.Size = new System.Drawing.Size(616, 130);
            this.groupBox_ParkingId.TabIndex = 9;
            this.groupBox_ParkingId.TabStop = false;
            this.groupBox_ParkingId.Text = "Parking Id";
            // 
            // listBox_ParkingId
            // 
            this.listBox_ParkingId.FormattingEnabled = true;
            this.listBox_ParkingId.ItemHeight = 16;
            this.listBox_ParkingId.Location = new System.Drawing.Point(380, 21);
            this.listBox_ParkingId.Name = "listBox_ParkingId";
            this.listBox_ParkingId.Size = new System.Drawing.Size(200, 84);
            this.listBox_ParkingId.TabIndex = 25;
            this.listBox_ParkingId.SelectedIndexChanged += new System.EventHandler(this.listBox1_SelectedIndexChanged);
            // 
            // button_ParkingId_Remove
            // 
            this.button_ParkingId_Remove.Location = new System.Drawing.Point(261, 47);
            this.button_ParkingId_Remove.Name = "button_ParkingId_Remove";
            this.button_ParkingId_Remove.Size = new System.Drawing.Size(33, 30);
            this.button_ParkingId_Remove.TabIndex = 24;
            this.button_ParkingId_Remove.Text = "➖";
            this.button_ParkingId_Remove.UseVisualStyleBackColor = true;
            this.button_ParkingId_Remove.Click += new System.EventHandler(this.button_ParkingId_Remove_Click);
            // 
            // button_ParkingId_Add
            // 
            this.button_ParkingId_Add.Location = new System.Drawing.Point(294, 47);
            this.button_ParkingId_Add.Name = "button_ParkingId_Add";
            this.button_ParkingId_Add.Size = new System.Drawing.Size(33, 30);
            this.button_ParkingId_Add.TabIndex = 23;
            this.button_ParkingId_Add.Text = "➕";
            this.button_ParkingId_Add.UseVisualStyleBackColor = true;
            this.button_ParkingId_Add.Click += new System.EventHandler(this.button_ParkingId_Add_Click);
            // 
            // textBox_ParkingId_Int
            // 
            this.textBox_ParkingId_Int.Location = new System.Drawing.Point(86, 51);
            this.textBox_ParkingId_Int.Name = "textBox_ParkingId_Int";
            this.textBox_ParkingId_Int.Size = new System.Drawing.Size(169, 22);
            this.textBox_ParkingId_Int.TabIndex = 26;
            // 
            // textBox_ParkingId_Prefix
            // 
            this.textBox_ParkingId_Prefix.Location = new System.Drawing.Point(16, 51);
            this.textBox_ParkingId_Prefix.Name = "textBox_ParkingId_Prefix";
            this.textBox_ParkingId_Prefix.Size = new System.Drawing.Size(49, 22);
            this.textBox_ParkingId_Prefix.TabIndex = 27;
            this.textBox_ParkingId_Prefix.TextChanged += new System.EventHandler(this.textBox2_TextChanged);
            // 
            // label_parkingId_LetterPark
            // 
            this.label_parkingId_LetterPark.AutoSize = true;
            this.label_parkingId_LetterPark.Location = new System.Drawing.Point(16, 29);
            this.label_parkingId_LetterPark.Name = "label_parkingId_LetterPark";
            this.label_parkingId_LetterPark.Size = new System.Drawing.Size(44, 16);
            this.label_parkingId_LetterPark.TabIndex = 28;
            this.label_parkingId_LetterPark.Text = "Ramp";
            // 
            // label_parkingId_serial
            // 
            this.label_parkingId_serial.AutoSize = true;
            this.label_parkingId_serial.Location = new System.Drawing.Point(83, 29);
            this.label_parkingId_serial.Name = "label_parkingId_serial";
            this.label_parkingId_serial.Size = new System.Drawing.Size(175, 16);
            this.label_parkingId_serial.TabIndex = 29;
            this.label_parkingId_serial.Text = "Parking Spot(s)/Spot Range";
            // 
            // FormSquadEdit
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(8F, 16F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(1282, 917);
            this.Controls.Add(this.panelMain);
            this.MinimizeBox = false;
            this.Name = "FormSquadEdit";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            this.Text = "Squad Editor";
            this.panelMain.ResumeLayout(false);
            this.tabControl_SquadEditMain.ResumeLayout(false);
            this.tabPage1.ResumeLayout(false);
            this.groupBoxGeneral.ResumeLayout(false);
            this.groupBoxGeneral.PerformLayout();
            this.groupBox_Bases.ResumeLayout(false);
            this.groupBox_Bases.PerformLayout();
            this.groupBoxAircraft.ResumeLayout(false);
            this.groupBoxAircraft.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.numericNumber)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.numericInitNumber)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.numericReserve)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.numericInitReserve)).EndInit();
            this.groupBoxTasks.ResumeLayout(false);
            this.tabPage2.ResumeLayout(false);
            this.groupBox_sidenumber.ResumeLayout(false);
            this.groupBox_sidenumber.PerformLayout();
            this.groupBox_Livery.ResumeLayout(false);
            this.groupBox_Livery.PerformLayout();
            this.groupBox_MODEX.ResumeLayout(false);
            this.groupBox_MODEX.PerformLayout();
            this.tabPage3.ResumeLayout(false);
            this.groupBoxScore.ResumeLayout(false);
            this.tabPage4.ResumeLayout(false);
            this.groupBox_PanelTables.ResumeLayout(false);
            this.groupBox_ParkingId.ResumeLayout(false);
            this.groupBox_ParkingId.PerformLayout();
            this.ResumeLayout(false);

        }

        #endregion

        private System.Windows.Forms.Panel panelMain;

        private System.Windows.Forms.GroupBox groupBoxGeneral;
        private System.Windows.Forms.GroupBox groupBoxAircraft;
        private System.Windows.Forms.GroupBox groupBoxTasks;
        private System.Windows.Forms.GroupBox groupBoxScore;

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
        private GroupBox groupBox_Livery;
        private ListBox listBoxBasesAlternat;
        private Label labelBasesAdd;
        private TextBox textBox_AddSkin;
        private Button button_AddSkin;
        private Button button_base_plus;
        private ComboBox comboBox_All_bases;
        private GroupBox groupBox_Bases;
        private Button button_Base_Moins;
        private Button button_Base_Haut;
        private Button button_Base_Down;
        private ListBox listBox_Livery;
        private Button button_RemoveSkin;
        private Label label_Callsign;
        private ComboBox comboBox_Callsign;
        private CheckBox checkBox_HumainOnly;
        private GroupBox groupBox_MODEX;
        private Button button_LiveryModex_Moins;
        private ListBox listBox_LiveryModex;
        private Button button_LiveryModex_Plus;
        private TextBox textBox_Modex;
        private ComboBox comboBox_LiveryM;
        private TabControl tabControl_SquadEditMain;
        private TabPage tabPage1;
        private TabPage tabPage2;
        private TabPage tabPage3;
        private GroupBox groupBox_sidenumber;
        private Label label_ModexNb_Max;
        private Label label_ModexNb_Min;
        private TextBox textBox_ModexNb_Max;
        private TextBox textBox_ModexNb_Min;
        private TabPage tabPage4;
        private GroupBox groupBox_PanelTables;
        private FlowLayoutPanel flowLayoutPanelTables;
        private GroupBox groupBox_ParkingId;
        private TextBox textBox_ParkingId_Int;
        private Button button_ParkingId_Remove;
        private Button button_ParkingId_Add;
        private ListBox listBox_ParkingId;
        private TextBox textBox_ParkingId_Prefix;
        private Label label_parkingId_LetterPark;
        private Label label_parkingId_serial;
    }
}
