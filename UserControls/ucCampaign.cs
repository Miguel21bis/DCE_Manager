using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using DCE_Manager.Parameters;
using DCE_Manager.Update;
using DCE_Manager.Utils;

namespace DCE_Manager.UserControls
{
    public partial class ucCampaign : UserControl
    {
        public DataGridView DataGridViewBlue => dataGridViewBlue;

        public DataGridView DataGridViewRed => dataGridViewRed;

        public ucCampaign()
        {
            InitializeComponent();

            BuildWargameTab();

            CampaignTab.Selected += CampaignTab_Selected;

            // CheckedChanged part sur les DEUX radios à chaque changement :
            // celui qui se décoche ET celui qui se coche. Sans le filtre ci-dessous,
            // Main_Form recevait donc systématiquement deux appels au lieu d'un.
            radioButton_INIT_CAMP.CheckedChanged += (s, e) =>
            {
                if (!radioButton_INIT_CAMP.Checked)
                    return;

                Main_Form.Instance?.radioButton_OOB_INIT_CheckedChanged(s, e);
            };

            radioButton_ACTIVE_CAMP.CheckedChanged += (s, e) =>
            {
                if (!radioButton_ACTIVE_CAMP.Checked)
                    return;

                Main_Form.Instance?.radioButton_OOB_ACTIVE_CheckedChanged(s, e);
            };
        }

        private void CampaignTab_Selected(object sender, TabControlEventArgs e)
        {
            Main_Form.Instance.CampaignGridLeft.UpdateCampaignButtonsVisibility();

            // Init/Active, Save et Reset ne servent à rien dans l'onglet Wargame (il a ses propres commandes)
            panel_but_monitoring_campaign.Visible = CampaignTab.SelectedTab != tabPage_wargame;
        }


        //les RadioButtons
        public bool IsOobInit
        {
            get { return radioButton_INIT_CAMP.Checked; }
        }

        public bool IsOobActive
        {
            get { return radioButton_ACTIVE_CAMP.Checked; }
        }


        //Puis : les boutons

        // Une seule méthode pour choisir la version affichée.
        // Pourquoi : deux setters indépendants permettaient de mettre les deux radios
        //            à true (ou à false) et de se retrouver dans un état impossible.
        public void SetOobMode(bool init)
        {
            if (init)
                radioButton_INIT_CAMP.Checked = true;
            else
                radioButton_ACTIVE_CAMP.Checked = true;
        }

        // Conservées pour ne pas casser les appels existants.
        public void SetOobInitMode(bool init)
        {
            SetOobMode(init);
        }

        public void SetOobActiveMode(bool active)
        {
            SetOobMode(!active);
        }

        public void EnableSaveButton(bool enabled)
        {
            buttonSaveChgtCampaign.Enabled = enabled;
        }

        public void EnableResetButton(bool enabled)
        {
            buttonResetBackup.Enabled = enabled;
        }

        //Ensuite : CampaignTab
        public void SelectMissionTab()
        {
            CampaignTab.SelectedIndex = 0;
        }

        public void SelectBlueTab()
        {
            CampaignTab.SelectedIndex = 1;
        }

        public void SelectRedTab()
        {
            CampaignTab.SelectedIndex = 2;
        }
        public int SelectedTabIndex
        {
            get { return CampaignTab.SelectedIndex; }
        }
        public bool IsOobTabSelected
        {
            get
            {
                return CampaignTab.SelectedTab == tabPage14 ||
                       CampaignTab.SelectedTab == tabPage15 ||
                       CampaignTab.SelectedTab == tabPageTargetsBlue ||
                       CampaignTab.SelectedTab == tabPageTargetsRed;
            }
        }

        //Label
        public void ShowCampaignName(bool visible)
        {
            label_Right_Campaign_Name.Visible = visible;
        }

        private void buttonSaveChgtCampaign_Click(object sender, EventArgs e)
        {
            Main_Form.Instance.CampaignGridLeft.CurrentCampaignEdit?.buttonSaveChgtCampaign_Click(sender, e);
        }

        private void buttonResetBackup_Click(object sender, EventArgs e)
        {
            Main_Form.Instance.CampaignGridLeft.CurrentCampaignEdit?.buttonResetBackup_Click(sender, e);
        }

        // =====================================================================
        //  ONGLET WARGAME
        //  - visible seulement si la campagne a un wargame (voir CampaignHasWargame)
        //  - joueur : carte en lecture seule + dernier rapport + légende
        //  - campaignMaker : en plus Init/Active, bouton "Edit wargame..." et liste de contrôle
        // =====================================================================

        private string _wgCampaign = "";
        private Image _wgImage;
        private ucWargameMapView _wgMap;
        private TextBox _wgReport;
        private Label _wgChecklist;
        private Button _wgEditButton;
        private RadioButton _wgRadioInit;
        private RadioButton _wgRadioActive;
        private bool _wgLoading;

        // Un wargame existe quand ses zones ont été créées (Init/Wargame/DCE_Generated/wargame_zones_init.lua).
        // Ce n'est pas camp.wargame_config : celle-ci est recopiée de la référence dans TOUTES les campagnes.
        public static bool CampaignHasWargame(string campaignName)
        {
            if (string.IsNullOrEmpty(campaignName))
                return false;

            return File.Exists(WargameZoneRepository.GetInitLuaPath(campaignName));
        }

        private void BuildWargameTab()
        {
            _wgEditButton = new Button { Text = "Edit wargame...", Left = 8, Top = 8, Width = 150, Height = 30, Visible = false };
            _wgEditButton.Click += (s, e) => EditWargame();

            _wgRadioInit = new RadioButton { Text = "Init", Left = 172, Top = 13, AutoSize = true, Checked = true, Visible = false };
            _wgRadioActive = new RadioButton { Text = "Active", Left = 232, Top = 13, AutoSize = true, Visible = false };
            _wgRadioInit.CheckedChanged += (s, e) => { if (_wgRadioInit.Checked) LoadWargameView(); };
            _wgRadioActive.CheckedChanged += (s, e) => { if (_wgRadioActive.Checked) LoadWargameView(); };

            _wgChecklist = new Label { Left = 310, Top = 15, AutoSize = true, Visible = false };

            var top = new Panel { Dock = DockStyle.Top, Height = 46 };
            top.Controls.Add(_wgEditButton);
            top.Controls.Add(_wgRadioInit);
            top.Controls.Add(_wgRadioActive);
            top.Controls.Add(_wgChecklist);

            _wgMap = new ucWargameMapView { ReadOnly = true, ShowAxes = false, ShowMissionUnits = false };
            var mapPanel = new NoAutoScrollPanel { Dock = DockStyle.Fill, AutoScroll = true };
            mapPanel.Controls.Add(_wgMap);

            var legend = new Label
            {
                Dock = DockStyle.Bottom,
                Height = 34,
                Padding = new Padding(6, 4, 6, 0),
                Text = "Colored zones show who holds the ground. The report below tells what happened at the last turn."
            };

            _wgReport = new TextBox
            {
                Dock = DockStyle.Bottom,
                Height = 150,
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                Font = new Font("Consolas", 9f),
                BackColor = Color.White
            };

            // Ordre d'ajout : d'abord le Fill, ensuite Bottom puis Top
            tabPage_wargame.Controls.Add(mapPanel);
            tabPage_wargame.Controls.Add(_wgReport);
            tabPage_wargame.Controls.Add(legend);
            tabPage_wargame.Controls.Add(top);

            // Cachée par défaut : RefreshWargameTab la montre si la campagne a un wargame
            CampaignTab.TabPages.Remove(tabPage_wargame);
        }

        // Appelée à chaque changement de campagne affichée (campaignName vide = aucune campagne)
        // et quand le niveau d'utilisateur change.
        public void RefreshWargameTab(string campaignName)
        {
            _wgCampaign = campaignName ?? "";

            if (!CampaignHasWargame(_wgCampaign))
            {
                CampaignTab.TabPages.Remove(tabPage_wargame);
                return;
            }

            if (!CampaignTab.TabPages.Contains(tabPage_wargame))
                CampaignTab.TabPages.Add(tabPage_wargame);

            bool maker = ParamConf.UserLevel == UserLevel.CampaignMaker;
            _wgEditButton.Visible = maker;
            _wgRadioInit.Visible = maker;
            _wgRadioActive.Visible = maker;
            _wgChecklist.Visible = maker;

            // Joueur : l'état en cours s'il existe, sinon le départ. CampaignMaker : il choisit.
            _wgLoading = true;
            bool hasActive = File.Exists(WargameZoneRepository.GetActiveWargameZonesPath(_wgCampaign));
            if (!maker)
            {
                _wgRadioActive.Checked = hasActive;
                _wgRadioInit.Checked = !hasActive;
            }
            _wgLoading = false;

            LoadWargameView();
        }

        private void LoadWargameView()
        {
            if (_wgLoading || string.IsNullOrEmpty(_wgCampaign) || !CampaignHasWargame(_wgCampaign))
                return;

            try
            {
                string folder = WargameZoneRepository.GetCampaignFolder(_wgCampaign);
                bool showActive = _wgRadioActive.Checked;

                List<WargameZoneData> zones = WargameZoneRepository.LoadOrGenerateInit(_wgCampaign);
                List<WargameAxis> axes = WargameZoneRepository.State.Axes;
                WargameMapCalibration calibration = WargameMapCalibration.Load(WargameZoneRepository.GetCalibrationPath(_wgCampaign));

                // La liste de contrôle porte sur le départ (Init), donc AVANT d'appliquer l'état Active
                if (ParamConf.UserLevel == UserLevel.CampaignMaker)
                    _wgChecklist.Text = BuildChecklist(zones, axes, calibration);

                if (showActive)
                {
                    string activePath = WargameZoneRepository.GetActiveWargameZonesPath(_wgCampaign);
                    if (File.Exists(activePath))
                        new WargameZoneActiveLoader().ApplyActiveState(activePath, zones);
                }

                string targetlistPath = showActive
                    ? Path.Combine(folder, "Active", "targetlist.lua")
                    : Path.Combine(folder, "Init", "targetlist_init.lua");

                List<WargameObjective> objectives = File.Exists(targetlistPath)
                    ? new Parser_WargameObjectives().Load(targetlistPath)
                    : new List<WargameObjective>();

                // Image copiée en mémoire : le fichier ne reste pas verrouillé
                Image newImage = null;
                string imagePath = WargameZoneRepository.GetMapImagePath(_wgCampaign);
                if (File.Exists(imagePath))
                {
                    using (var fs = new FileStream(imagePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                    using (var tmp = Image.FromStream(fs))
                        newImage = new Bitmap(tmp);
                }

                _wgMap.LoadMap(newImage, calibration, zones);
                _wgMap.SetObjectives(objectives);
                _wgMap.SetAxes(axes);

                _wgImage?.Dispose();
                _wgImage = newImage;

                string reportPath = Path.Combine(folder, "Active", "wargame_report.txt");
                _wgReport.Text = File.Exists(reportPath)
                    ? File.ReadAllText(reportPath).Replace("\r\n", "\n").Replace("\n", "\r\n")
                    : "No wargame report yet. It appears after the first debriefing.";
            }
            catch (Exception ex)
            {
                FormUtils.LogRegister("LoadWargameView('" + _wgCampaign + "') a échoué : " + ex.Message);
                _wgReport.Text = "The wargame could not be displayed: " + ex.Message;
            }
        }

        // "Zones ✓  Map ✓  Camps ✗ ..." : ce qu'il reste à faire avant de livrer la campagne
        private static string BuildChecklist(List<WargameZoneData> zones, List<WargameAxis> axes, WargameMapCalibration calibration)
        {
            List<WargameZoneData> map = zones.Where(z => !WargameEngineCombat.IsReserveZone(z)).ToList();

            bool zonesOk = map.Count > 0;
            bool mapOk = calibration != null && calibration.IsCalibrated;
            bool campsOk = map.Count > 0 && map.All(z => z.Control == WargameSide.Blue || z.Control == WargameSide.Red);
            bool forcesOk = zones.Any(z => z.Control == WargameSide.Blue && z.Formations.Count > 0)
                         && zones.Any(z => z.Control == WargameSide.Red && z.Formations.Count > 0);
            bool axesOk = axes != null && axes.Count > 0 && axes.All(a => a.IsValid);

            Func<bool, string> mark = ok => ok ? "✓" : "✗";

            return "Zones " + mark(zonesOk) + "   Map " + mark(mapOk) + "   Camps " + mark(campsOk)
                 + "   Forces " + mark(forcesOk) + "   Axes " + mark(axesOk);
        }

        private void EditWargame()
        {
            if (string.IsNullOrEmpty(_wgCampaign))
                return;

            using (var form = new WargameEditor_Form(_wgCampaign))
            {
                form.ShowDialog(FindForm());
            }

            RefreshWargameTab(_wgCampaign);
        }
    }
}
