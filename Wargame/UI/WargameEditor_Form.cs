using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using System.Text;
using DCE_Manager.Utils;

namespace DCE_Manager
{
    // Form principale d'édition du wargame pour une campagne : carte en fond +
    // zones cliquables (ucWargameMapView) + panneau d'édition (ucWargameZoneEditPanel).
    //
    // Deux radios en haut, Init / Active : basculent quelle version de l'état
    // wargame est affichée et modifiée. Le campaignMaker doit pouvoir revenir sur
    // l'Init pour l'affiner même après avoir commencé à jouer la campagne - rien
    // n'est deviné automatiquement à sa place.
    //
    //   Init     wargame_zones_init.lua + Init/targetlist_init.lua
    //   Active   Active/wargame_zones.lua + Active/targetlist.lua
    //
    // Changer de radio recharge l'affichage depuis la source correspondante (avec
    // confirmation si des modifications non enregistrées seraient perdues). Save
    // écrit toujours dans la paire de fichiers du mode actuellement affiché.
    internal class WargameEditor_Form : Form
    {
        private readonly string _campaignName;
        private readonly string _initLuaPath;
        private readonly string _mapImagePath;
        private readonly string _calibJsonPath;

        private List<WargameZoneData> _zones;
        private List<WargameObjective> _objectives = new List<WargameObjective>();
        private List<WargameMissionUnit> _missionUnits = new List<WargameMissionUnit>();
        private WargameMapCalibration _calibration;
        private WargameCampaignInfo _campaignInfo;
        private WargameTemplateCatalog _templateCatalog;

        // Zone affichée dans le panneau détaillé (uniquement en sélection simple).
        private WargameZoneData _currentZone;

        private WargameObjective _selectedObjective;
        private Panel _objectiveEditPanel;
        private NumericUpDown _numObjPriority;
        private TextBox _txtObjAttributes;
        private NumericUpDown _numObjFirepowerMin;
        private NumericUpDown _numObjFirepowerMax;

        // Passe à true dès qu'une valeur de zone est modifiée, repasse à false au Save.
        private bool _dirty;
        private Image _mapImage;

        // false = Init, true = Active. Piloté par les radios, jamais deviné.
        private bool _viewingActive;
        private bool _suppressModeChange;

        private ucWargameMapView _mapView;
        private ucWargameZoneEditPanel _editPanel;

        private RadioButton _radioInit;
        private RadioButton _radioActive;
        private Button _buttonSave;

        // Panneau d'assignation groupée, visible seulement quand 2+ zones sont
        // sélectionnées (Ctrl+clic sur la carte).
        private Panel _bulkPanel;
        private Label _bulkLabel;
        private ComboBox _bulkComboControl;

        public WargameEditor_Form(string campaignName)
        {
            _campaignName = campaignName;

            _initLuaPath = WargameZoneRepository.GetInitLuaPath(campaignName);
            _mapImagePath = WargameZoneRepository.GetMapImagePath(campaignName);
            _calibJsonPath = WargameZoneRepository.GetCalibrationPath(campaignName);

            Text = "Wargame - " + campaignName;
            Width = 1200;
            Height = 800;
            StartPosition = FormStartPosition.CenterParent;
            WindowState = FormWindowState.Maximized;

            LoadStaticData();
            BuildUi();

            LoadZonesForCurrentMode();
            _mapView.LoadMap(_mapImage, _calibration, _zones);
            _mapView.SetObjectives(_objectives);
            _mapView.SetMissionUnits(_missionUnits);

            List<string> warnings = BuildPrerequisiteWarnings();
            if (warnings.Count > 0)
            {
                MessageBox.Show(
                    "This campaign may not be fully ready for the wargame:\n\n- " + string.Join("\n\n- ", warnings),
                    "Wargame", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        // Rassemble les avertissements "il manque quelque chose pour que le wargame
        // fonctionne correctement", pour ne plus avoir à comparer un dossier et le
        // log.txt à la main pour comprendre pourquoi rien ne s'affiche (cas réel
        // rencontré : le chemin Saved Games actif dans la config DCE_Manager ne
        // correspondait pas à l'installation DCS réellement utilisée en jeu).
        private List<string> BuildPrerequisiteWarnings()
        {
            var warnings = new List<string>();

            string campaignFolder = WargameZoneRepository.GetCampaignFolder(_campaignName);

            if (!Directory.Exists(campaignFolder))
            {
                warnings.Add("Campaign folder not found:\n" + campaignFolder
                    + "\n\nThe active Saved Games path in DCE_Manager's configuration may not match "
                    + "the DCS install actually used in-game (multiple DCS installs?).");
                return warnings; // tout le reste en découle, inutile d'empiler les messages
            }

            if (!_campaignInfo.HasWargameConfig)
                warnings.Add("Wargame is not configured for this campaign (camp.wargame_config missing from Init/camp_init.lua).");

            if (!_calibration.IsCalibrated)
                warnings.Add("Map is not calibrated: zones won't be visible.");

            string activeZonesPath = WargameZoneRepository.GetActiveWargameZonesPath(_campaignName);
            bool wargameAlreadyRan = File.Exists(activeZonesPath);

            if (wargameAlreadyRan && _missionUnits.Count == 0)
            {
                warnings.Add("The wargame has already run for this campaign, but no wargame unit was found "
                    + "in the last generated mission.\nCheck that the active Saved Games path matches the "
                    + "one actually used in-game (see log.txt).");
            }

            return warnings;
        }

        // Ce qui ne dépend pas du mode Init/Active : calibration, catalogue, image
        // de fond. Chargé une seule fois.
        private void LoadStaticData()
        {
            _calibration = WargameMapCalibration.Load(_calibJsonPath);
            _campaignInfo = WargameCampaignInfo.Load(_campaignName);
            _templateCatalog = WargameTemplateCatalog.LoadAndSync(_campaignName, _campaignInfo);
            _mapImage = File.Exists(_mapImagePath) ? Image.FromFile(_mapImagePath) : null;

            // Unités de la dernière mission générée. Fichier absent = cas normal
            // (campagne jamais lancée) : la liste est simplement vide.
            string lastMissionPath = Path.Combine(
                WargameZoneRepository.GetCampaignFolder(_campaignName), "Active", "last_Mission.lua");

            _missionUnits = new Parser_WargameMissionUnits().Load(lastMissionPath);
        }

        // Recharge _zones depuis Init, puis - si on est en mode Active - applique
        // par-dessus l'état évolutif d'Active/wargame_zones.lua (control,
        // formations, irregular). Si ce fichier n'existe pas encore, l'état Init
        // sert de point de départ tel quel (comportement déjà géré par le loader).
        private void LoadZonesForCurrentMode()
        {
            _zones = WargameZoneRepository.LoadOrGenerateInit(_campaignName);

            if (_viewingActive)
            {
                string activePath = WargameZoneRepository.GetActiveWargameZonesPath(_campaignName);
                new WargameZoneActiveLoader().ApplyActiveState(activePath, _zones);
            }

            string targetlistPath = _viewingActive
            ? Path.Combine(WargameZoneRepository.GetCampaignFolder(_campaignName), "Active", "targetlist.lua")
            : Path.Combine(WargameZoneRepository.GetCampaignFolder(_campaignName), "Init", "targetlist_init.lua");

            _objectives = File.Exists(targetlistPath) ? new Parser_WargameObjectives().Load(targetlistPath) : new List<WargameObjective>();
        }

        private void BuildUi()
        {
            _mapView = new ucWargameMapView { ReadOnly = false };
            _mapView.SelectionChanged += MapView_SelectionChanged;
            _mapView.ObjectiveClicked += MapView_ObjectiveClicked;

            var mapPanel = new NoAutoScrollPanel { Dock = DockStyle.Fill, AutoScroll = true };
            mapPanel.Controls.Add(_mapView);

            _editPanel = new ucWargameZoneEditPanel { Dock = DockStyle.Fill };
            _editPanel.ZoneModified += EditPanel_ZoneModified;
            _editPanel.SetCampaignInfo(_campaignInfo, _templateCatalog);

            _bulkPanel = BuildBulkAssignPanel();

            _objectiveEditPanel = BuildObjectiveEditPanel();

            var modeRow = BuildModeSelectorRow();

            var bottomPanel = new TableLayoutPanel
            {
                Dock = DockStyle.Bottom,
                Height = 118,
                ColumnCount = 1,
                RowCount = 2,
            };
            bottomPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 80));
            bottomPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));

            var secondaryButtons = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = true,
            };

            var buttonHelp = new Button { Text = "?", Width = 40, Height = 30 };
            buttonHelp.Click += (s, e) => WargameHelp_Form.ShowHelp(this);

            var buttonZoomReset = new Button { Text = "Zoom 100%", Width = 100, Height = 30 };
            buttonZoomReset.Click += (s, e) => _mapView.ResetZoom();

            // Affichage des unités de la dernière mission générée, désactivable :
            // sur une grosse mission ça fait beaucoup de points sur la carte.
            var checkShowUnits = new CheckBox
            {
                Text = "Mission units (" + _missionUnits.Count + ")",
                Checked = true,
                AutoSize = false,
                Width = 150,
                Height = 30,
                TextAlign = ContentAlignment.MiddleLeft,
                Enabled = _missionUnits.Count > 0,
            };
            checkShowUnits.CheckedChanged += (s, e) =>
            {
                _mapView.ShowMissionUnits = checkShowUnits.Checked;
                _mapView.Invalidate();
            };

            var buttonCalibrate = new Button { Text = "Calibrate map...", Width = 140, Height = 30 };
            buttonCalibrate.Click += (s, e) => OpenCalibration();

            var buttonTemplates = new Button { Text = "Templates...", Width = 110, Height = 30 };
            buttonTemplates.Click += (s, e) => OpenTemplateCatalog();

            var buttonSpawnTest = new Button { Text = "Test spawn areas", Width = 140, Height = 30 };
            buttonSpawnTest.Click += (s, e) => TestSpawnAreas();

            var buttonSpawnSolverTest = new Button { Text = "Test spawn solver", Width = 140, Height = 30 };
            buttonSpawnSolverTest.Click += (s, e) => TestSpawnSolver();

            var buttonTestObjectives = new Button { Text = "Test objectives", Width = 130, Height = 30 };
            buttonTestObjectives.Click += (s, e) => TestObjectives();

            var buttonDetectNeighbors = new Button { Text = "Detect neighbors...", Width = 150, Height = 30 };
            buttonDetectNeighbors.Click += (s, e) => DetectNeighbors();

            // Rouge et à part : la seule action qui détruit du travail déjà fait
            // sans possibilité de retour. Force toujours le mode Init au passage
            // (voir CreateWarzone) puisqu'elle régénère wargame_zones_init.lua.
            var buttonCreateWarzone = new Button
            {
                Text = "Create Warzone",
                Width = 150,
                Height = 30,
                BackColor = Color.Firebrick,
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
            };
            buttonCreateWarzone.FlatAppearance.BorderColor = Color.DarkRed;
            buttonCreateWarzone.Click += (s, e) => CreateWarzone();

            secondaryButtons.Controls.Add(buttonHelp);
            secondaryButtons.Controls.Add(buttonZoomReset);
            secondaryButtons.Controls.Add(checkShowUnits);
            secondaryButtons.Controls.Add(buttonCalibrate);
            secondaryButtons.Controls.Add(buttonTemplates);
            secondaryButtons.Controls.Add(buttonSpawnTest);
            secondaryButtons.Controls.Add(buttonSpawnSolverTest);
            secondaryButtons.Controls.Add(buttonTestObjectives);
            secondaryButtons.Controls.Add(buttonDetectNeighbors);
            secondaryButtons.Controls.Add(buttonCreateWarzone);

            var saveRow = new Panel { Dock = DockStyle.Fill };

            _buttonSave = new Button
            {
                Width = 120,
                Height = 30,
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
            };
            _buttonSave.Click += (s, e) => SaveZones(true);
            UpdateSaveButtonLabel();
            _buttonSave.Location = new Point(saveRow.Width - _buttonSave.Width - 6, 4);

            saveRow.Controls.Add(_buttonSave);
            saveRow.Resize += (s, e) => _buttonSave.Location = new Point(saveRow.Width - _buttonSave.Width - 6, 4);

            bottomPanel.Controls.Add(secondaryButtons, 0, 0);
            bottomPanel.Controls.Add(saveRow, 0, 1);

            var rightPanel = new Panel { Dock = DockStyle.Fill };
            rightPanel.Controls.Add(_editPanel);
            rightPanel.Controls.Add(_bulkPanel);
            rightPanel.Controls.Add(_objectiveEditPanel);
            rightPanel.Controls.Add(modeRow);
            rightPanel.Controls.Add(bottomPanel);

            var splitContainer = new SplitContainer { Dock = DockStyle.Fill, FixedPanel = FixedPanel.Panel2 };
            splitContainer.Panel1.Controls.Add(mapPanel);
            splitContainer.Panel2.Controls.Add(rightPanel);

            Controls.Add(splitContainer);

            Load += (s, e) => splitContainer.SplitterDistance = Math.Max(100, splitContainer.Width - 500);
        }

        // Bandeau Init/Active tout en haut du panneau de droite - toujours visible,
        // quel que soit l'état de sélection sur la carte.
        private Panel BuildModeSelectorRow()
        {
            var panel = new Panel { Dock = DockStyle.Top, Height = 34, Padding = new Padding(10, 6, 10, 0) };

            var label = new Label { Text = "Viewing:", Location = new Point(10, 6), AutoSize = true };

            _radioInit = new RadioButton { Text = "Init", Location = new Point(75, 4), AutoSize = true, Checked = true };
            _radioActive = new RadioButton { Text = "Active", Location = new Point(140, 4), AutoSize = true };

            _radioInit.CheckedChanged += (s, e) => { if (_radioInit.Checked) TrySwitchMode(false); };
            _radioActive.CheckedChanged += (s, e) => { if (_radioActive.Checked) TrySwitchMode(true); };

            panel.Controls.Add(label);
            panel.Controls.Add(_radioInit);
            panel.Controls.Add(_radioActive);

            return panel;
        }

        // toActive : le mode vers lequel on essaie de basculer. Si des changements
        // non enregistrés seraient perdus, on demande confirmation et on revient en
        // arrière sur les radios en cas de refus (_suppressModeChange évite de
        // redéclencher TrySwitchMode pendant qu'on remet les radios en place).
        private void TrySwitchMode(bool toActive)
        {
            if (_suppressModeChange) return;
            if (toActive == _viewingActive) return;

            if (_dirty)
            {
                DialogResult answer = MessageBox.Show(
                    "You have unsaved changes in the current view. Switching will discard them.\n\nSwitch anyway?",
                    "Wargame", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);

                if (answer != DialogResult.Yes)
                {
                    _suppressModeChange = true;
                    _radioInit.Checked = !_viewingActive;
                    _radioActive.Checked = _viewingActive;
                    _suppressModeChange = false;
                    return;
                }
            }

            _viewingActive = toActive;
            LoadZonesForCurrentMode();

            _currentZone = null;
            _dirty = false;
            UpdateSaveButtonLabel();

            _mapView.ClearSelection();
            _mapView.LoadMap(_mapImage, _calibration, _zones);
            _editPanel.LoadZone(null, _zones);
        }

        private void UpdateSaveButtonLabel()
        {
            _buttonSave.Text = _viewingActive ? "Save -> Active" : "Save -> Init";
        }

        private Panel BuildBulkAssignPanel()
        {
            var panel = new Panel { Dock = DockStyle.Fill, Visible = false, Padding = new Padding(10) };

            _bulkLabel = new Label
            {
                Text = "0 zones selected",
                Location = new Point(10, 10),
                AutoSize = true,
                Font = new Font("Segoe UI", 11f, FontStyle.Bold),
            };

            var instructions = new Label
            {
                Text = "Ctrl+Click zones on the map to add or remove them from the selection.",
                Location = new Point(10, 40),
                Width = 260,
                Height = 45,
            };

            var controlLabel = new Label { Text = "Assign camp:", Location = new Point(10, 92), AutoSize = true };

            _bulkComboControl = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Location = new Point(10, 112),
                Width = 260,
            };
            _bulkComboControl.Items.Add(_campaignInfo.BlueLabel);
            _bulkComboControl.Items.Add(_campaignInfo.RedLabel);
            _bulkComboControl.Items.Add("Contested");

            var buttonApply = new Button
            {
                Text = "Apply to selected zones",
                Location = new Point(10, 148),
                Width = 260,
                Height = 30,
            };
            buttonApply.Click += ButtonBulkApply_Click;

            var buttonClear = new Button
            {
                Text = "Clear selection",
                Location = new Point(10, 186),
                Width = 260,
                Height = 30,
            };
            buttonClear.Click += (s, e) => _mapView.ClearSelection();

            panel.Controls.Add(_bulkLabel);
            panel.Controls.Add(instructions);
            panel.Controls.Add(controlLabel);
            panel.Controls.Add(_bulkComboControl);
            panel.Controls.Add(buttonApply);
            panel.Controls.Add(buttonClear);

            return panel;
        }

        private void ButtonBulkApply_Click(object sender, EventArgs e)
        {
            if (_bulkComboControl.SelectedIndex < 0)
            {
                MessageBox.Show("Pick a camp first.", "Wargame", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            string controlValue = BulkControlFromComboIndex(_bulkComboControl.SelectedIndex);

            foreach (WargameZoneData zone in _mapView.SelectedZones)
                zone.Control = controlValue;

            _dirty = true;
            _mapView.Invalidate();
        }

        private static string BulkControlFromComboIndex(int index)
        {
            switch (index)
            {
                case 0: return WargameSide.Blue;
                case 1: return WargameSide.Red;
                default: return WargameSide.Contested;
            }
        }

        private void MapView_SelectionChanged(List<WargameZoneData> selected)
        {
            _objectiveEditPanel.Visible = false;

            if (selected.Count == 1)
            {
                _currentZone = selected[0];
                _bulkPanel.Visible = false;
                _editPanel.Visible = true;
                _editPanel.LoadZone(_currentZone, _zones);
            }
            else if (selected.Count > 1)
            {
                _currentZone = null;
                _editPanel.Visible = false;
                _bulkPanel.Visible = true;
                _bulkLabel.Text = selected.Count + " zones selected";
            }
            else
            {
                _currentZone = null;
                _bulkPanel.Visible = false;
                _editPanel.Visible = true;
                _editPanel.LoadZone(null, _zones);
            }
        }

        private void EditPanel_ZoneModified()
        {
            _dirty = true;
            _mapView.Invalidate();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (_dirty)
            {
                DialogResult answer = MessageBox.Show(
                    "You have unsaved zone changes. Close without saving?\n\n"
                    + "Use the Save button first if you want to keep them.",
                    "Wargame",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning);

                if (answer == DialogResult.No)
                {
                    e.Cancel = true;
                    return;
                }
            }

            base.OnFormClosing(e);
        }

        // Ecrit toujours dans la paire de fichiers du mode actuellement affiché -
        // jamais devine, jamais les deux à la fois.
        private void SaveZones(bool showConfirmation)
        {
            string target;
            List<string> placementFailures;

            if (_viewingActive)
            {
                string activePath = WargameZoneRepository.GetActiveWargameZonesPath(_campaignName);
                Saver_WargameZoneActive.Save(activePath, _zones);
                placementFailures = Saver_TargetList_Wargame.WriteNewActiveFormations(_campaignName);
                WargameObjectiveWriter.ApplyChanges(
                    Path.Combine(WargameZoneRepository.GetCampaignFolder(_campaignName), "Active", "targetlist.lua"),
                    _objectives, BuildObjectiveDesiredSides());
                target = "Active/wargame_zones.lua + Active/targetlist.lua";
            }
            else
            {
                Saver_WargameZoneInit.Save(_initLuaPath, _zones, WargameZoneRepository.State);
                placementFailures = Saver_TargetList_Wargame.WriteInitialFormations(_campaignName);
                WargameObjectiveWriter.ApplyChanges(
                    Path.Combine(WargameZoneRepository.GetCampaignFolder(_campaignName), "Init", "targetlist_init.lua"),
                    _objectives, BuildObjectiveDesiredSides());
                target = "Init/wargame_zones_init.lua + Init/targetlist_init.lua";
            }

            _dirty = false;

            if (placementFailures.Count > 0)
            {
                MessageBox.Show(
                    "Saved to " + target + ", but " + placementFailures.Count + " formation(s) could not be placed "
                    + "(no valid spot found in their zone, even compressed):\n\n- " + string.Join("\n- ", placementFailures)
                    + "\n\nThey are missing from the map and won't spawn in the next mission. Try a smaller/lighter "
                    + "template for these, or check the zone's free space.",
                    "Wargame", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            else if (showConfirmation)
            {
                MessageBox.Show("Saved to " + target + ".", "Wargame", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        private void OpenTemplateCatalog()
        {
            using (var form = new WargameTemplateCatalog_Form(_campaignName, _campaignInfo))
            {
                form.ShowDialog(this);
            }

            _templateCatalog = WargameTemplateCatalog.LoadAndSync(_campaignName, _campaignInfo);
            _editPanel.SetCampaignInfo(_campaignInfo, _templateCatalog);
        }

        private void OpenCalibration()
        {
            if (_mapImage == null)
            {
                MessageBox.Show("No map image found (wargame_map.jpg).", "Wargame", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            using (var calibForm = new WargameCalibration_Form(_mapImage, _calibJsonPath))
            {
                if (calibForm.ShowDialog(this) == DialogResult.OK)
                {
                    _calibration = WargameMapCalibration.Load(_calibJsonPath);
                    _mapView.LoadMap(_mapImage, _calibration, _zones);
                    _mapView.SetObjectives(_objectives);
                }
            }
        }

        // Temporaire : vérifie que wargame_spawn.miz est lu correctement.
        private void TestSpawnAreas()
        {
            string spawnMizPath = WargameZoneRepository.GetSpawnMizPath(_campaignName);

            if (!File.Exists(spawnMizPath))
            {
                MessageBox.Show("No wargame_spawn.miz found in Init\\wargame.",
                    "Wargame", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            WargameSpawnAreas areas = new Parser_WargameSpawnAreas().LoadFromMiz(spawnMizPath);

            var report = new StringBuilder();
            report.AppendLine("Lines    : " + areas.Lines.Count);
            report.AppendLine("Polygons : " + areas.Polygons.Count);
            report.AppendLine();

            foreach (var group in areas.Polygons.GroupBy(p => p.Tag).OrderBy(g => g.Key))
                report.AppendLine("  " + group.Key + " : " + group.Count() + " (" + WargameSpawnTag.GetPolicy(group.Key) + ")");

            report.AppendLine();

            double totalKm = 0;
            foreach (WargameSpawnLine line in areas.Lines)
                totalKm += line.GetLength() / 1000.0;

            report.AppendLine("Total road length : " + totalKm.ToString("0.0") + " km");

            MessageBox.Show(report.ToString(), "Wargame - spawn areas",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        // Temporaire : place la première formation de la zone sélectionnée et
        // affiche où chaque unité a atterri.
        private void TestSpawnSolver()
        {
            if (_currentZone == null)
            {
                MessageBox.Show("Select a single zone first (not a multi-selection).",
                    "Wargame", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (_currentZone.Formations == null || _currentZone.Formations.Count == 0)
            {
                MessageBox.Show("This zone has no formation assigned yet (see the Units grid).",
                    "Wargame", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            WargameFormation formation = _currentZone.Formations[0];
            string templateName = formation.Template;

            if (string.IsNullOrEmpty(templateName))
            {
                MessageBox.Show("The first formation in this zone has no template selected.",
                    "Wargame", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            string stmPath = _campaignInfo.GetTemplateFilePath(templateName);
            List<WargameTemplateUnit> layout = new Parser_WargameTemplateLayout().LoadLayout(stmPath);
            if (layout.Count == 0)
            {
                MessageBox.Show("Template '" + templateName + "' has no readable units.",
                    "Wargame", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            string spawnMizPath = WargameZoneRepository.GetSpawnMizPath(_campaignName);
            WargameSpawnAreas spawnAreas = File.Exists(spawnMizPath)
                ? new Parser_WargameSpawnAreas().LoadFromMiz(spawnMizPath)
                : new WargameSpawnAreas();

            List<WargameZoneData> allZones = WargameZoneRepository.LoadOrGenerateInit(_campaignName);
            PointF? threatPoint = Saver_TargetList_Wargame.FindNearestEnemyZoneCenter(_currentZone, formation.Side, allZones);

            List<WargamePlacedUnit> placed = WargameSpawnSolver.PlaceTemplate(_currentZone, layout, spawnAreas, new Random(), threatPoint);

            if (placed == null)
            {
                MessageBox.Show("No valid position found for '" + templateName + "' in zone '" + _currentZone.Id + "'.",
                    "Wargame", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var report = new StringBuilder();
            report.AppendLine("Formation  : " + formation.Name);
            report.AppendLine("Template   : " + templateName);
            report.AppendLine("Zone       : " + _currentZone.Id);
            report.AppendLine("Units      : " + placed.Count);
            report.AppendLine();

            foreach (WargamePlacedUnit u in placed)
                report.AppendLine("  " + u.Name + " (" + u.Category + ") -> " + (int)u.Position.X + ", " + (int)u.Position.Y + " | hdg " + (int)u.Heading);

            MessageBox.Show(report.ToString(), "Wargame - spawn solver test",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        // Temporaire : vérifie que les objectifs (wargameObjective = true) sont
        // bien repérés et lus dans le fichier actuellement affiché (Init ou Active
        // selon le radio coché).
        private void TestObjectives()
        {
            string pathFile = _viewingActive
                ? WargameZoneRepository.GetActiveWargameZonesPath(_campaignName).Replace("wargame_zones.lua", "targetlist.lua")
                : Path.Combine(WargameZoneRepository.GetCampaignFolder(_campaignName), "Init", "targetlist_init.lua");

            if (!File.Exists(pathFile))
            {
                MessageBox.Show("File not found:\n" + pathFile, "Wargame", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            List<WargameObjective> objectives = new Parser_WargameObjectives().Load(pathFile);

            var report = new StringBuilder();
            report.AppendLine("File   : " + pathFile);
            report.AppendLine("Found  : " + objectives.Count + " objective(s) with wargameObjective = true");
            report.AppendLine();

            foreach (WargameObjective obj in objectives)
            {
                report.AppendLine(obj.Name + "  [" + obj.CurrentSide + "]");
                report.AppendLine("    pos = " + (int)obj.Position.X + ", " + (int)obj.Position.Y);
                report.AppendLine("    priority=" + obj.Priority + "  attributes=" + string.Join(",", obj.Attributes)
                    + "  firepower=" + obj.FirepowerMin + "/" + obj.FirepowerMax);

                WargameZoneData zone = _zones.FirstOrDefault(z => z.DcsPoints != null && z.DcsPoints.Count >= 3
                    && IsPointInPolygon(obj.Position, z.DcsPoints));
                report.AppendLine("    zone = " + (zone != null ? zone.Id + " (" + zone.Control + ")" : "AUCUNE ZONE NE LE CONTIENT"));
                report.AppendLine();
            }

            MessageBox.Show(report.ToString(), "Wargame - objectives test", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        // Camp cible souhaité pour chaque objectif, d'après le contrôle actuel de
        // la zone qui le contient. Une zone Contested ou sans camp assigné ->
        // l'objectif est absent du dictionnaire, donc jamais déplacé (voir
        // WargameObjectiveWriter.ApplyChanges).
        private Dictionary<string, string> BuildObjectiveDesiredSides()
        {
            var result = new Dictionary<string, string>();

            foreach (WargameObjective obj in _objectives)
            {
                WargameZoneData zone = _zones.FirstOrDefault(z => z.DcsPoints != null && z.DcsPoints.Count >= 3
                    && IsPointInPolygon(obj.Position, z.DcsPoints));

                if (zone == null) continue;
                if (zone.Control != WargameSide.Blue && zone.Control != WargameSide.Red) continue;

                result[obj.Name] = Saver_TargetList_Wargame.TargetTableSide(zone.Control);
            }

            return result;
        }

        // Même test point-dans-polygone que celui déjà utilisé côté zones/spawn.
        private static bool IsPointInPolygon(PointF p, List<PointF> polygon)
        {
            bool inside = false;
            int j = polygon.Count - 1;

            for (int i = 0; i < polygon.Count; i++)
            {
                PointF a = polygon[i], b = polygon[j];

                if ((a.Y > p.Y) != (b.Y > p.Y))
                {
                    float xCross = (b.X - a.X) * (p.Y - a.Y) / (b.Y - a.Y) + a.X;
                    if (p.X < xCross) inside = !inside;
                }

                j = i;
            }

            return inside;
        }

        private void DetectNeighbors()
        {
            bool confirmed = FormUtils.ShowDangerConfirm(this,
                "This replaces the neighbor list of EVERY zone with an automatic, "
                + "proximity-based detection. Any manual adjustment will be lost.\n\nContinue?",
                "Detect neighbors");

            if (!confirmed)
                return;

            Dictionary<string, List<string>> detected = WargameNeighborDetector.DetectNeighbors(_zones);

            foreach (WargameZoneData zone in _zones)
            {
                if (detected.TryGetValue(zone.Id, out List<string> neighbors))
                    zone.Neighbors = neighbors;
            }

            _dirty = true;
            _mapView.Invalidate();

            if (_currentZone != null)
                _editPanel.LoadZone(_currentZone, _zones);

            MessageBox.Show("Neighbors updated. Don't forget to Save.",
                "Detect neighbors", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        // Reparse wargame_zone.miz et ECRASE wargame_zones_init.lua depuis zéro.
        // Opère toujours sur l'Init, quel que soit le mode affiché au moment du
        // clic - on force donc le retour en mode Init ensuite pour que ce qui
        // s'affiche corresponde à ce qui vient d'être écrit.
        private void CreateWarzone()
        {
            bool confirmed = FormUtils.ShowDangerConfirm(this,
                "This will PERMANENTLY ERASE the current wargame setup for this campaign "
                + "(control, formations, neighbors, terrain, supply...) and recreate everything "
                + "from wargame_zone.miz.\n\nThis cannot be undone.\n\nAre you sure?",
                "Create Warzone");

            if (!confirmed)
                return;

            WargameZoneRepository.ForceRegenerate(_campaignName);

            _suppressModeChange = true;
            _radioInit.Checked = true;
            _radioActive.Checked = false;
            _suppressModeChange = false;

            _viewingActive = false;
            LoadZonesForCurrentMode();

            _currentZone = null;
            _dirty = false;
            UpdateSaveButtonLabel();

            _mapView.ClearSelection();
            _mapView.LoadMap(_mapImage, _calibration, _zones);
            _editPanel.LoadZone(null, _zones);

            MessageBox.Show("Warzone recreated from wargame_zone.miz.",
                "Create Warzone", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private Panel BuildObjectiveEditPanel()
        {
            var panel = new Panel { Dock = DockStyle.Fill, Visible = false, Padding = new Padding(10) };

            var label = new Label { Text = "", Location = new Point(10, 10), AutoSize = true, Font = new Font("Segoe UI", 11f, FontStyle.Bold) };
            panel.Tag = label; // pour retrouver le label facilement dans MapView_ObjectiveClicked

            var priorityLabel = new Label { Text = "tgt_Priority", Location = new Point(10, 45), AutoSize = true };
            _numObjPriority = new NumericUpDown { Location = new Point(10, 62), Width = 260, Minimum = 0, Maximum = 100 };
            _numObjPriority.ValueChanged += (s, e) =>
            {
                if (_loadingObjective || _selectedObjective == null) return;
                _selectedObjective.Priority = (int)_numObjPriority.Value;
                _dirty = true;
            };

            var attributesLabel = new Label { Text = "tgt_Attributes (comma separated)", Location = new Point(10, 94), AutoSize = true };
            _txtObjAttributes = new TextBox { Location = new Point(10, 111), Width = 260 };
            _txtObjAttributes.TextChanged += (s, e) =>
            {
                if (_loadingObjective || _selectedObjective == null) return;
                _selectedObjective.Attributes = _txtObjAttributes.Text.Split(',').Select(a => a.Trim()).Where(a => a.Length > 0).ToList();
                _dirty = true;
            };

            var fpMinLabel = new Label { Text = "tgt_FP_min", Location = new Point(10, 143), AutoSize = true };
            _numObjFirepowerMin = new NumericUpDown { Location = new Point(10, 160), Width = 120, Minimum = 0, Maximum = 100 };
            _numObjFirepowerMin.ValueChanged += (s, e) =>
            {
                if (_loadingObjective || _selectedObjective == null) return;
                _selectedObjective.FirepowerMin = (double)_numObjFirepowerMin.Value;
                _dirty = true;
            };

            var fpMaxLabel = new Label { Text = "tgt_FP_max", Location = new Point(150, 143), AutoSize = true };
            _numObjFirepowerMax = new NumericUpDown { Location = new Point(150, 160), Width = 120, Minimum = 0, Maximum = 100 };
            _numObjFirepowerMax.ValueChanged += (s, e) =>
            {
                if (_loadingObjective || _selectedObjective == null) return;
                _selectedObjective.FirepowerMax = (double)_numObjFirepowerMax.Value;
                _dirty = true;
            };

            panel.Controls.Add(label);
            panel.Controls.Add(priorityLabel);
            panel.Controls.Add(_numObjPriority);
            panel.Controls.Add(attributesLabel);
            panel.Controls.Add(_txtObjAttributes);
            panel.Controls.Add(fpMinLabel);
            panel.Controls.Add(_numObjFirepowerMin);
            panel.Controls.Add(fpMaxLabel);
            panel.Controls.Add(_numObjFirepowerMax);

            return panel;
        }

        private bool _loadingObjective;

        private void MapView_ObjectiveClicked(WargameObjective obj)
        {
            _selectedObjective = obj;

            _editPanel.Visible = false;
            _bulkPanel.Visible = false;
            _objectiveEditPanel.Visible = true;

            _loadingObjective = true;
            ((Label)_objectiveEditPanel.Tag).Text = "Objective: " + obj.Name;
            _numObjPriority.Value = Math.Max(_numObjPriority.Minimum, Math.Min(_numObjPriority.Maximum, obj.Priority));
            _txtObjAttributes.Text = string.Join(", ", obj.Attributes);
            _numObjFirepowerMin.Value = (decimal)Math.Max((double)_numObjFirepowerMin.Minimum, Math.Min((double)_numObjFirepowerMin.Maximum, obj.FirepowerMin));
            _numObjFirepowerMax.Value = (decimal)Math.Max((double)_numObjFirepowerMax.Minimum, Math.Min((double)_numObjFirepowerMax.Maximum, obj.FirepowerMax));
            _loadingObjective = false;
        }
    }
}
