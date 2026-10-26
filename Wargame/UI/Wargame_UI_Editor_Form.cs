using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using DCE_Manager.Utils;

namespace DCE_Manager
{
    // Form principale d'édition du wargame pour une campagne : carte en fond +
    // zones cliquables (ucWargameMapView) + panneau d'édition (ucWargameZoneEditPanel).
    //
    // A gauche : une colonne d'ETAPES (de haut en bas, dans l'ordre où on monte un wargame),
    // affichable / cachable avec le bouton rouage. A droite : la zone d'édition. Une étape terminée est grisée (avec un ✓), la prochaine à faire
    // est en bleu. Une étape grisée reste utilisable : si elle peut casser ce qui a été fait
    // après elle, un avertissement est demandé avant.
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
        private NumericUpDown _numObjSupply;
        private CheckBox _chkObjBridge;

        // Passe à true dès qu'une valeur de zone est modifiée, repasse à false au Save.
        // Propriété : à chaque changement, on met à jour l'indicateur "Unsaved changes" et l'état
        // des étapes (une modification peut terminer ou défaire une étape).
        private bool _dirtyValue;
        private bool _dirty
        {
            get { return _dirtyValue; }
            set
            {
                _dirtyValue = value;
                if (_labelDirty != null) _labelDirty.Visible = value;
                RefreshSteps();
                RefreshSupplyOverlay();   // toute modification (zone, route, source, pont...) recolore tout de suite
            }
        }

        // Calque "Supply" : zones colorées selon leur ravitaillement, recalculé à chaque modification
        private CheckBox _chkSupply;

        private void RefreshSupplyOverlay()
        {
            if (_mapView == null || _chkSupply == null) return;

            if (!_chkSupply.Checked || _zones == null || _campaignInfo == null || _campaignInfo.CombatConfig == null)
            {
                _mapView.SetSupplyOverlay(null);
                return;
            }

            WargameEngineCombat.ApplyObjectivesToSupply(_zones, _objectives, _campaignInfo.CombatConfig);
            _mapView.SetSupplyOverlay(WargameEngineCombat.ComputeSupplyMap(_zones, _campaignInfo.CombatConfig));
        }

        private Image _mapImage;

        // false = Init, true = Active. Piloté par les radios, jamais deviné.
        private bool _viewingActive;
        private bool _suppressModeChange;

        private ucWargameMapView _mapView;
        private Label _landingHint;                 // bandeau "cliquez la plage" pendant le choix de la cible d'un plan
        private ContextMenuStrip _landingMenu;      // menu clic droit des plans de débarquement
        private ucWargameZoneEditPanel _editPanel;
        private ucWargameAxisPanel _axisPanel;

        // Liste d'axes en cours d'édition : c'est elle qui fait foi. WargameZoneRepository.State
        // est remplacé à chaque LoadOrGenerateInit (Save, tests...), pas cette liste.
        private List<WargameAxis> _axes = new List<WargameAxis>();

        private RadioButton _radioInit;
        private RadioButton _radioActive;
        private Button _buttonSave;
        private Label _labelDirty;
        private Panel _stepsPanel;   // colonne d'étapes, affichée / cachée par le rouage

        // Panneau d'assignation groupée, visible seulement quand 2+ zones sont
        // sélectionnées (Ctrl+clic sur la carte).
        private Panel _bulkPanel;
        private Label _bulkLabel;
        private ComboBox _bulkComboControl;

        // ---- Colonne d'étapes ----

        private class StepRow
        {
            public int Number;
            public string Title;
            public Button Button;
            public Label Caption;
            public Func<bool> IsDone;        // l'étape est-elle terminée ? (déduit des données, jamais mémorisé)
            public Action Run;               // ce que fait le bouton
            public string RedoWarning;       // null = pas d'avertissement supplémentaire quand on la refait
            public bool IsFinal;             // dernière étape (aperçu) : jamais grisée
            public string BaseCaption;       // texte normal sous le bouton
            public Func<string> Warning;     // null = aucune vérification ; sinon renvoie ce qui manque ("" = tout est bon)
        }

        private readonly List<StepRow> _steps = new List<StepRow>();
        private Font _fontStep;
        private Font _fontStepBold;

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

            RefreshSteps();

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

            // Axes : ceux de l'Init, éditables seulement en vue Init. On garde la liste dans
            // _axes : elle reste la même même si State est remplacé plus tard.
            _axes = WargameZoneRepository.State.Axes;
            _axisPanel.Bind(_axes, _zones, !_viewingActive);
            _mapView.SetAxes(_axes);
        }

        private void BuildUi()
        {
            _mapView = new ucWargameMapView { ReadOnly = false };
            _mapView.SelectionChanged += MapView_SelectionChanged;
            _mapView.ObjectiveClicked += MapView_ObjectiveClicked;

            // Plans de débarquement : boîtes déplaçables en vue Init (TrySwitchMode met l'interrupteur à jour)
            _mapView.LandingEditable = true;
            _mapView.MapRightClicked += MapView_MapRightClicked;
            _mapView.LandingRightClicked += MapView_LandingRightClicked;
            _mapView.LandingMoved += zone => { _dirty = true; };

            var mapPanel = new NoAutoScrollPanel { Dock = DockStyle.Fill, AutoScroll = true };
            mapPanel.Controls.Add(_mapView);

            // Bandeau d'aide au-dessus de la carte, visible seulement pendant le choix d'une plage.
            // Ajouté APRES la carte (Fill) : il se colle en haut et la carte prend le reste.
            _landingHint = new Label
            {
                Dock = DockStyle.Top,
                Height = 26,
                TextAlign = ContentAlignment.MiddleCenter,
                BackColor = Color.FromArgb(255, 235, 160),
                Font = new Font("Segoe UI", 9f, FontStyle.Bold),
                Visible = false,
            };
            mapPanel.Controls.Add(_landingHint);

            _editPanel = new ucWargameZoneEditPanel { Dock = DockStyle.Fill };
            _editPanel.ZoneModified += EditPanel_ZoneModified;
            _editPanel.SetCampaignInfo(_campaignInfo, _templateCatalog);

            _axisPanel = new ucWargameAxisPanel { Dock = DockStyle.Fill, Visible = false };
            _axisPanel.AxesChanged += () => { _dirty = true; _mapView.Invalidate(); };
            _axisPanel.SelectedAxisChanged += axis => { _mapView.SelectedAxis = axis; };
            _axisPanel.PickZoneRequested += request =>
                _mapView.BeginPickZone(zone => _axisPanel.SetPickedZone(request, zone.Id));
            _axisPanel.HighlightZoneRequested += zoneId => _mapView.HighlightAxisZone(zoneId);
            _mapView.ZonePickCancelled += () => { _axisPanel.EndPick(); _landingHint.Visible = false; };

            _bulkPanel = BuildBulkAssignPanel();

            _objectiveEditPanel = BuildObjectiveEditPanel();

            var modeRow = BuildModeSelectorRow();

            // ---- Ligne du bas : indicateur de modifications + Save ----
            var saveRow = new Panel { Dock = DockStyle.Bottom, Height = 40 };

            _labelDirty = new Label
            {
                Text = "Unsaved changes",
                ForeColor = Color.DarkOrange,
                Font = new Font("Segoe UI", 9f, FontStyle.Bold),
                Location = new Point(10, 11),
                AutoSize = true,
                Visible = false,
            };

            _buttonSave = new Button
            {
                Width = 120,
                Height = 30,
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
            };
            _buttonSave.Click += (s, e) => SaveZones(true);
            UpdateSaveButtonLabel();
            _buttonSave.Location = new Point(saveRow.Width - _buttonSave.Width - 6, 5);

            saveRow.Controls.Add(_labelDirty);
            saveRow.Controls.Add(_buttonSave);
            saveRow.Resize += (s, e) => _buttonSave.Location = new Point(saveRow.Width - _buttonSave.Width - 6, 5);

            // ---- Zone d'édition (à droite de la colonne d'étapes) ----
            var contentPanel = new Panel { Dock = DockStyle.Fill };
            contentPanel.Controls.Add(_editPanel);        // Fill d'abord, puis les bords
            contentPanel.Controls.Add(_bulkPanel);
            contentPanel.Controls.Add(_objectiveEditPanel);
            contentPanel.Controls.Add(_axisPanel);
            contentPanel.Controls.Add(modeRow);
            contentPanel.Controls.Add(saveRow);

            // Séparation carte / panneau : large (16 px) et visible, avec des points de prise,
            // qui s'éclaire au survol. La fine ligne d'origine était presque impossible à attraper.
            var splitContainer = new SplitContainer
            {
                Dock = DockStyle.Fill,
                FixedPanel = FixedPanel.Panel2,
                SplitterWidth = 16,
                Cursor = Cursors.VSplit,
            };

            bool splitHover = false;
            splitContainer.MouseEnter += (s, e) => { splitHover = true; splitContainer.Invalidate(); };
            splitContainer.MouseLeave += (s, e) => { splitHover = false; splitContainer.Invalidate(); };
            splitContainer.Paint += (s, e) =>
            {
                Rectangle bar = splitContainer.SplitterRectangle;
                bool dragging = Control.MouseButtons == MouseButtons.Left && splitHover;
                Color back = dragging ? Color.FromArgb(0, 90, 170) : splitHover ? Color.FromArgb(70, 140, 210) : Color.FromArgb(150, 160, 175);

                using (var brush = new SolidBrush(back))
                    e.Graphics.FillRectangle(brush, bar);

                // 5 points blancs au milieu : la "poignée"
                using (var dot = new SolidBrush(Color.White))
                {
                    int cx = bar.Left + bar.Width / 2;
                    int cy = bar.Top + bar.Height / 2;
                    for (int i = -2; i <= 2; i++)
                        e.Graphics.FillEllipse(dot, cx - 2, cy + i * 9 - 2, 4, 4);
                }
            };
            splitContainer.Panel1.Controls.Add(mapPanel);
            splitContainer.Panel2.Controls.Add(contentPanel);

            // A gauche de la fenêtre : une fine bande avec le bouton rouage, et le panneau des étapes
            // qu'il affiche / cache. Ordre d'ajout : le Fill d'abord, puis le panneau, puis la bande
            // (la bande se place donc tout à gauche).
            _stepsPanel = BuildStepsRail();

            var gearStrip = new Panel { Dock = DockStyle.Left, Width = 38, BackColor = Color.FromArgb(243, 243, 243) };
            var buttonGear = new Button
            {
                Text = "⚙",
                Font = new Font("Segoe UI Symbol", 15f),
                Location = new Point(3, 6),
                Size = new Size(32, 32),
                FlatStyle = FlatStyle.Flat,
                TabStop = false,
            };
            buttonGear.FlatAppearance.BorderColor = Color.Silver;
            buttonGear.Click += (s, e) => _stepsPanel.Visible = !_stepsPanel.Visible;
            new ToolTip().SetToolTip(buttonGear, "Show / hide the steps");
            gearStrip.Controls.Add(buttonGear);

            // Réglages du wargame (wargame_balance.lua) : toujours accessibles, sous le rouage
            var buttonSettings = new Button
            {
                Text = "⚖",
                Font = new Font("Segoe UI Symbol", 15f),
                Location = new Point(3, 44),
                Size = new Size(32, 32),
                FlatStyle = FlatStyle.Flat,
                TabStop = false,
            };
            buttonSettings.FlatAppearance.BorderColor = Color.Silver;
            buttonSettings.Click += (s, e) => OpenBalanceSettings();
            new ToolTip().SetToolTip(buttonSettings, "Wargame settings (combat, army, supply, convoys, terrain...)");
            gearStrip.Controls.Add(buttonSettings);

            Controls.Add(splitContainer);
            Controls.Add(_stepsPanel);
            Controls.Add(gearStrip);

            // Panneau de droite étroit à l'ouverture (la carte a la priorité) ; le campaignMaker l'élargit
            // avec la barre de séparation s'il veut voir toute la grille des unités.
            Load += (s, e) =>
            {
                int rightWidth = Math.Min(340, (int)(splitContainer.Width * 0.4));
                splitContainer.SplitterDistance = Math.Max(100, splitContainer.Width - rightWidth);
            };
        }

        // -------------------- Colonne d'étapes --------------------

        // De haut en bas : l'ordre dans lequel on monte un wargame. Chaque étape sait dire toute seule
        // si elle est terminée (d'après les données chargées) : rien n'est mémorisé, donc rien ne peut
        // se désynchroniser.
        private Panel BuildStepsRail()
        {
            _fontStep = new Font("Segoe UI", 9f, FontStyle.Regular);
            _fontStepBold = new Font("Segoe UI", 9f, FontStyle.Bold);

            var rail = new Panel
            {
                Dock = DockStyle.Left,
                Width = 226,
                BackColor = Color.FromArgb(243, 243, 243),
                Padding = new Padding(6, 8, 6, 6),
            };

            var title = new Label
            {
                Text = "Steps",
                Dock = DockStyle.Top,
                Height = 28,
                Font = new Font("Segoe UI", 11f, FontStyle.Bold),
            };

            var flow = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoScroll = true,
            };

            AddStep(flow, "Create zones", "Draw the zones in wargame_zone.miz, then import them here.",
                StepZonesDone, CreateWarzone, null, false);

            AddStep(flow, "Calibrate map", "Match the map picture to the DCS coordinates.",
                StepCalibrationDone, OpenCalibration,
                "The map is already calibrated.\n\nA new calibration changes how every zone, objective and unit sits on the picture.", false);

            AddStep(flow, "Reserve zones", "Two small zones at the map corner for waiting reinforcements.",
                StepReserveDone, AddReserveZones, null, false);

            AddStep(flow, "Detect neighbors", "Find which zones touch each other.",
                StepNeighborsDone, DetectNeighbors, null, false);

            AddStep(flow, "Templates", "Set the power of each formation template.",
                StepTemplatesDone, OpenTemplateCatalog,
                "Changing the power of a template changes the strength of every formation that uses it.", false, MissingTemplatesText);

            AddStep(flow, "Assign camps", "Click a zone (Ctrl+click for several) and pick its camp.",
                StepCampsDone, ShowZoneEditing, null, false);

            AddStep(flow, "Place forces", "Fill each zone's Units grid. Reserves: RESERVE zones. Invasion: right-click the map.",
                StepForcesDone, ShowZoneEditing, null, false);

            AddStep(flow, "Draw axes", "Attack paths between zones, started by campaign flags.",
                StepAxesDone, ToggleAxisPanel, null, false);

            AddStep(flow, "Preview combat", "Test the front on a copy. Nothing is saved.",
                () => false, OpenCombatPreview, null, true);

            // ---- Outils : ce qui n'est pas une étape ----
            var tools = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                Height = 112,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
            };

            tools.Controls.Add(new Label
            {
                Text = "Tools",
                Width = 200,
                Height = 20,
                Font = new Font("Segoe UI", 9f, FontStyle.Bold),
                Margin = new Padding(3, 4, 3, 0),
            });

            var buttonHelp = new Button { Text = "Help", Width = 200, Height = 28, Margin = new Padding(3, 2, 3, 0) };
            buttonHelp.Click += (s, e) => WargameHelp_Form.ShowHelp(this);

            var buttonZoomReset = new Button { Text = "Zoom 100%", Width = 200, Height = 28, Margin = new Padding(3, 2, 3, 0) };
            buttonZoomReset.Click += (s, e) => _mapView.ResetZoom();

            // Affichage des unités de la dernière mission générée, désactivable :
            // sur une grosse mission ça fait beaucoup de points sur la carte.
            var checkShowUnits = new CheckBox
            {
                Text = "Mission units (" + _missionUnits.Count + ")",
                Checked = true,
                AutoSize = false,
                Width = 200,
                Height = 24,
                Margin = new Padding(3, 2, 3, 0),
                TextAlign = ContentAlignment.MiddleLeft,
                Enabled = _missionUnits.Count > 0,
            };
            checkShowUnits.CheckedChanged += (s, e) =>
            {
                _mapView.ShowMissionUnits = checkShowUnits.Checked;
                _mapView.Invalidate();
            };

            tools.Controls.Add(buttonHelp);
            tools.Controls.Add(buttonZoomReset);
            tools.Controls.Add(checkShowUnits);

            rail.Controls.Add(flow);     // Fill d'abord, puis le bas, puis le haut
            rail.Controls.Add(tools);
            rail.Controls.Add(title);

            return rail;
        }

        private void AddStep(FlowLayoutPanel parent, string title, string caption,
            Func<bool> isDone, Action run, string redoWarning, bool isFinal, Func<string> warning = null)
        {
            var step = new StepRow
            {
                Number = _steps.Count + 1,
                Title = title,
                IsDone = isDone,
                Run = run,
                RedoWarning = redoWarning,
                IsFinal = isFinal,
                BaseCaption = caption,
                Warning = warning,
            };

            step.Button = new Button
            {
                Width = 200,
                Height = 32,
                FlatStyle = FlatStyle.Flat,
                TextAlign = ContentAlignment.MiddleLeft,
                Margin = new Padding(3, 6, 3, 0),
                UseVisualStyleBackColor = false,
            };
            step.Button.Click += (s, e) => RunStep(step);

            step.Caption = new Label
            {
                Text = caption,
                Width = 200,
                Height = 42,
                Font = new Font("Segoe UI", 8f),
                Margin = new Padding(3, 0, 3, 2),
            };

            _steps.Add(step);
            parent.Controls.Add(step.Button);
            parent.Controls.Add(step.Caption);
        }

        // Clic sur une étape. Si elle est déjà terminée et que la refaire peut casser la suite,
        // on prévient d'abord. (Create zones et Detect neighbors ont déjà leur propre confirmation.)
        private void RunStep(StepRow step)
        {
            if (step.IsDone() && step.RedoWarning != null)
            {
                DialogResult answer = MessageBox.Show(
                    step.RedoWarning + "\n\nThe steps after it may need to be redone.\n\nContinue?",
                    "Wargame - " + step.Title, MessageBoxButtons.YesNo, MessageBoxIcon.Warning);

                if (answer != DialogResult.Yes) return;
            }

            step.Run();
            RefreshSteps();
        }

        // Met à jour l'aspect des boutons : terminée = grisée avec un ✓, prochaine à faire = bleue
        // et en gras, les autres = normales.
        private void RefreshSteps()
        {
            if (_zones == null || _steps.Count == 0) return;

            StepRow current = _steps.FirstOrDefault(s => !s.IsFinal && !s.IsDone()) ?? _steps[_steps.Count - 1];

            foreach (StepRow step in _steps)
            {
                bool done = step.IsDone();
                string label = step.Number + ". " + step.Title;

                if (done)
                {
                    step.Button.Text = "✓ " + label;
                    step.Button.BackColor = Color.Gainsboro;
                    step.Button.ForeColor = Color.DimGray;
                    step.Button.Font = _fontStep;
                    step.Button.FlatAppearance.BorderColor = Color.Silver;
                    step.Caption.ForeColor = Color.Silver;
                }
                else if (step == current)
                {
                    step.Button.Text = label;
                    step.Button.BackColor = Color.FromArgb(47, 110, 165);
                    step.Button.ForeColor = Color.White;
                    step.Button.Font = _fontStepBold;
                    step.Button.FlatAppearance.BorderColor = Color.FromArgb(30, 80, 125);
                    step.Caption.ForeColor = Color.Black;
                }
                else
                {
                    step.Button.Text = label;
                    step.Button.BackColor = Color.White;
                    step.Button.ForeColor = Color.Black;
                    step.Button.Font = _fontStep;
                    step.Button.FlatAppearance.BorderColor = Color.Gray;
                    step.Caption.ForeColor = Color.DimGray;
                }

                // Vérification propre à l'étape : ce qui manque est écrit sous le bouton, en orange
                string missing = step.Warning != null ? step.Warning() : "";
                if (missing.Length > 0)
                {
                    step.Button.Text = "⚠ " + label;
                    step.Button.FlatAppearance.BorderColor = Color.DarkOrange;
                    step.Caption.Text = "⚠ Missing: " + missing;
                    step.Caption.ForeColor = Color.DarkOrange;
                }
                else
                {
                    step.Caption.Text = step.BaseCaption;
                }
            }
        }

        // Ce qui manque au catalogue de templates, en une ligne ("" = rien ne manque).
        // Les templates convoy / supplypoint ne sont exigés que si les convois sont activés dans les réglages (⚖).
        private string MissingTemplatesText()
        {
            bool convoys = _campaignInfo != null && _campaignInfo.CombatConfig != null && _campaignInfo.CombatConfig.SupplyConvoys;
            return string.Join(", ", WargameCombatPreview.MissingTemplates(_templateCatalog, _campaignInfo, convoys, true));
        }

        // ---- Critères "étape terminée" (déduits de ce qui est chargé en mémoire) ----

        private bool StepZonesDone()
        {
            return _zones != null && _zones.Count > 0;
        }

        private bool StepCalibrationDone()
        {
            return _calibration != null && _calibration.IsCalibrated;
        }

        // Les deux zones de réserve existent
        private bool StepReserveDone()
        {
            if (_zones == null) return false;

            bool blue = _zones.Any(z => string.Equals(z.Id, ReserveBlueId, StringComparison.OrdinalIgnoreCase));
            bool red = _zones.Any(z => string.Equals(z.Id, ReserveRedId, StringComparison.OrdinalIgnoreCase));
            return blue && red;
        }

        // Au moins la moitié des zones ont un voisin (une zone isolée, comme une île, est normale)
        private bool StepNeighborsDone()
        {
            if (_zones == null || _zones.Count == 0) return false;

            // Les zones RESERVE sont hors carte : elles n'ont jamais de voisin, on ne les compte pas
            List<WargameZoneData> map = _zones.Where(z => !WargameEngineCombat.IsReserveZone(z)).ToList();
            if (map.Count == 0) return false;

            int withNeighbors = map.Count(z => z.Neighbors != null && z.Neighbors.Count > 0);
            return withNeighbors * 2 >= map.Count;
        }

        // Le catalogue existe et contient des templates. (Ses valeurs restent à vérifier à la main.)
        private bool StepTemplatesDone()
        {
            return _templateCatalog != null && _templateCatalog.Entries.Count > 0 && MissingTemplatesText().Length == 0;
        }

        // Toutes les zones appartiennent à un camp
        private bool StepCampsDone()
        {
            if (_zones == null || _zones.Count == 0) return false;

            return _zones.All(z => z.Control == WargameSide.Blue || z.Control == WargameSide.Red);
        }

        // Chaque camp a au moins une formation quelque part
        private bool StepForcesDone()
        {
            if (_zones == null) return false;

            bool blue = _zones.Any(z => z.Control == WargameSide.Blue && z.Formations.Count > 0);
            bool red = _zones.Any(z => z.Control == WargameSide.Red && z.Formations.Count > 0);
            return blue && red;
        }

        private bool StepAxesDone()
        {
            return _axes != null && _axes.Count > 0 && _axes.All(a => a.IsValid);
        }

        private const string ReserveBlueId = "RESERVE_BLUE";
        private const string ReserveRedId = "RESERVE_RED";

        // Ajoute les deux zones de réserve dans le coin haut-gauche de l'image de la carte (l'une sous l'autre).
        // Ce sont de vraies zones : on clique dessus, on y met des formations, on les enregistre avec Save.
        // Une zone dont le nom commence par RESERVE est traitée par le moteur comme une remise (voir
        // WargameEngineCombat.IsReserveZone) : aucun voisin, jamais jouée dans DCS, jamais de combat.
        private void AddReserveZones()
        {
            if (_viewingActive)
            {
                MessageBox.Show("Switch to Init first: reserve zones are part of the initial setup.",
                    "Wargame", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (_mapImage == null || !_calibration.IsCalibrated)
            {
                MessageBox.Show("Calibrate the map first: the reserve zones are placed with the map coordinates.",
                    "Wargame", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            bool hasBlue = _zones.Any(z => string.Equals(z.Id, ReserveBlueId, StringComparison.OrdinalIgnoreCase));
            bool hasRed = _zones.Any(z => string.Equals(z.Id, ReserveRedId, StringComparison.OrdinalIgnoreCase));

            if (hasBlue && hasRed)
            {
                MessageBox.Show("Both reserve zones already exist: click one on the map to edit it.",
                    "Wargame", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            // Taille et marge en pixels de l'image : proportionnelles, pour rester visibles quelle que soit la carte
            float margin = _mapImage.Width * 0.015f;
            float width = _mapImage.Width * 0.08f;
            float height = _mapImage.Height * 0.06f;

            if (!hasBlue)
                _zones.Add(BuildReserveZone(ReserveBlueId, WargameSide.Blue, margin, margin, width, height));

            if (!hasRed)
                _zones.Add(BuildReserveZone(ReserveRedId, WargameSide.Red, margin, margin * 2 + height, width, height));

            _dirty = true;

            _mapView.LoadMap(_mapImage, _calibration, _zones);
            _mapView.SetObjectives(_objectives);

            MessageBox.Show("Reserve zones added in the top-left corner of the map.\n\n"
                + "Click one to add its formations (same panel as a normal zone), then Save.",
                "Wargame", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        // Rectangle dessiné en pixels sur l'image, converti en coordonnées DCS avec la calibration
        private WargameZoneData BuildReserveZone(string id, string control, float left, float top, float width, float height)
        {
            var corners = new[]
            {
                new PointF(left, top),
                new PointF(left + width, top),
                new PointF(left + width, top + height),
                new PointF(left, top + height),
            };

            var zone = new WargameZoneData { Id = id, Shape = WargameZoneShape.Polygon, Control = control };

            float sumX = 0, sumY = 0;
            foreach (PointF pixel in corners)
            {
                PointF dcs = _calibration.PixelToDcs(pixel);
                zone.DcsPoints.Add(dcs);
                sumX += dcs.X;
                sumY += dcs.Y;
            }

            zone.Center = new PointF(sumX / corners.Length, sumY / corners.Length);
            return zone;
        }

        // -------------------- Plans de débarquement --------------------
        //
        // Un plan = UNE boîte LANDING_n posée sur la carte (une zone comme une réserve : on y range des
        // formations) + la plage qu'elle vise (la flèche). Un plan par plage. Les vagues sont des numéros
        // dans la colonne Wave de la boîte : 2 plages x 3 vagues = 2 plans.
        // Tout se fait en vue Init (la géométrie et la plage ne sont enregistrées que dans l'Init).
        // Comme pour une réserve, le moteur ne donne aucun voisin à la boîte et ne la joue jamais dans DCS.

        private const string LandingIdPrefix = "LANDING_";

        // Clic droit dans le vide ou sur une zone : propose d'ajouter un plan à cet endroit
        private void MapView_MapRightClicked(PointF imagePoint, Point screenPoint)
        {
            if (_mapImage == null || _calibration == null || !_calibration.IsCalibrated)
                return;

            _landingMenu?.Dispose();
            _landingMenu = new ContextMenuStrip();

            if (_viewingActive)
            {
                _landingMenu.Items.Add(new ToolStripMenuItem("Landing plans are edited in the Init view") { Enabled = false });
            }
            else
            {
                var itemBlue = new ToolStripMenuItem("Add landing plan here - " + _campaignInfo.BlueLabel);
                itemBlue.Click += (s, e) => AddLandingPlan(imagePoint, WargameSide.Blue);

                var itemRed = new ToolStripMenuItem("Add landing plan here - " + _campaignInfo.RedLabel);
                itemRed.Click += (s, e) => AddLandingPlan(imagePoint, WargameSide.Red);

                _landingMenu.Items.Add(itemBlue);
                _landingMenu.Items.Add(itemRed);
            }

            _landingMenu.Show(screenPoint);
        }

        // Clic droit sur une boîte de débarquement
        private void MapView_LandingRightClicked(WargameZoneData plan, Point screenPoint)
        {
            _landingMenu?.Dispose();
            _landingMenu = new ContextMenuStrip();

            _landingMenu.Items.Add(new ToolStripMenuItem(plan.Id) { Enabled = false });
            _landingMenu.Items.Add(new ToolStripSeparator());

            var itemTarget = new ToolStripMenuItem("Change target beach") { Enabled = !_viewingActive };
            itemTarget.Click += (s, e) => PickLandingTarget(plan);

            var itemDelete = new ToolStripMenuItem("Delete this landing plan") { Enabled = !_viewingActive };
            itemDelete.Click += (s, e) => DeleteLandingPlan(plan);

            _landingMenu.Items.Add(itemTarget);
            _landingMenu.Items.Add(itemDelete);

            if (_viewingActive)
                _landingMenu.Items.Add(new ToolStripMenuItem("(switch to Init to edit the plan itself)") { Enabled = false });

            _landingMenu.Show(screenPoint);
        }

        // Premier numéro libre : LANDING_1, LANDING_2...
        private string NextLandingId()
        {
            int n = 1;

            while (_zones.Any(z => string.Equals(z.Id, LandingIdPrefix + n, StringComparison.OrdinalIgnoreCase)))
                n++;

            return LandingIdPrefix + n;
        }

        // Crée la boîte centrée sur le point cliqué, puis demande tout de suite la plage visée
        private void AddLandingPlan(PointF center, string side)
        {
            // Taille proportionnelle à l'image, comme les réserves
            float width = _mapImage.Width * 0.07f;
            float height = _mapImage.Height * 0.045f;

            WargameZoneData plan = BuildReserveZone(NextLandingId(), side, center.X - width / 2f, center.Y - height / 2f, width, height);
            _zones.Add(plan);

            _dirty = true;

            _mapView.LoadMap(_mapImage, _calibration, _zones);
            _mapView.SetObjectives(_objectives);

            PickLandingTarget(plan);
        }

        // Le prochain clic sur une zone la désigne comme plage visée. Clic droit = on garde la plage actuelle.
        private void PickLandingTarget(WargameZoneData plan)
        {
            _landingHint.Text = plan.Id + " (" + _campaignInfo.GetLabel(plan.Control) + ") : click the beach zone it lands on.   Right-click = cancel.";
            _landingHint.Visible = true;

            _mapView.BeginPickZone(zone =>
            {
                _landingHint.Visible = false;

                if (WargameEngineCombat.IsReserveZone(zone))
                {
                    MessageBox.Show("Pick a zone of the front, not a reserve or a landing box.",
                        "Wargame", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    PickLandingTarget(plan);
                    return;
                }

                plan.LandingTarget = zone.Id;
                _dirty = true;
                _mapView.Invalidate();

                // Le panneau affiche la plage : on le rafraîchit s'il montre ce plan
                if (ReferenceEquals(_currentZone, plan))
                    _editPanel.LoadZone(plan, _zones);
            });
        }

        private void DeleteLandingPlan(WargameZoneData plan)
        {
            if (plan.Formations.Count > 0)
            {
                DialogResult answer = MessageBox.Show(
                    "Delete " + plan.Id + " and its " + plan.Formations.Count + " unit(s)?",
                    "Wargame", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);

                if (answer != DialogResult.Yes)
                    return;
            }

            _zones.Remove(plan);
            _dirty = true;

            _mapView.ClearSelection();
            _currentZone = null;
            _editPanel.LoadZone(null, _zones);

            _mapView.LoadMap(_mapImage, _calibration, _zones);
            _mapView.SetObjectives(_objectives);
        }

        // Etapes "camps" et "forces" : on travaille dans le panneau de zone. Si le panneau des axes
        // est ouvert, le refermer ramène le panneau de zone.
        private void ShowZoneEditing()
        {
            if (_axisPanel.Visible)
                ToggleAxisPanel();

            _mapView.Focus();
        }

        // Bandeau Init/Active tout en haut de la zone d'édition - toujours visible,
        // quel que soit l'état de sélection sur la carte.
        private Panel BuildModeSelectorRow()
        {
            var panel = new Panel { Dock = DockStyle.Top, Height = 34, Padding = new Padding(10, 6, 10, 0) };

            var label = new Label { Text = "Viewing:", Location = new Point(10, 6), AutoSize = true };

            _radioInit = new RadioButton { Text = "Init", Location = new Point(75, 4), AutoSize = true, Checked = true };
            _radioActive = new RadioButton { Text = "Active", Location = new Point(140, 4), AutoSize = true };

            _radioInit.CheckedChanged += (s, e) => { if (_radioInit.Checked) TrySwitchMode(false); };
            _radioActive.CheckedChanged += (s, e) => { if (_radioActive.Checked) TrySwitchMode(true); };

            _chkSupply = new CheckBox { Text = "Show supply (colours)", Location = new Point(225, 4), AutoSize = true };
            _chkSupply.CheckedChanged += (s, e) => RefreshSupplyOverlay();
            new ToolTip().SetToolTip(_chkSupply, "Colours each zone by its supply: green = full, red = weak, hatched grey = cut off, S = source. Updates as soon as you change a zone, route, source or bridge. Hover a zone for details.");

            panel.Controls.Add(label);
            panel.Controls.Add(_radioInit);
            panel.Controls.Add(_radioActive);
            panel.Controls.Add(_chkSupply);

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

            // Un choix de plage en cours n'a plus de sens, et les boîtes ne se déplacent qu'en vue Init
            _mapView.CancelPickZone();
            _axisPanel.EndPick();
            _landingHint.Visible = false;
            _mapView.LandingEditable = !_viewingActive;

            LoadZonesForCurrentMode();

            _currentZone = null;
            _dirty = false;
            UpdateSaveButtonLabel();

            _mapView.ClearSelection();
            _mapView.LoadMap(_mapImage, _calibration, _zones);
            _editPanel.LoadZone(null, _zones);
        }

        // Affiche/masque le panneau des axes à la place des panneaux zone/objectif,
        // et les trace sur la carte tant qu'il est ouvert.
        private void ToggleAxisPanel()
        {
            bool show = !_axisPanel.Visible;

            _mapView.CancelPickZone();
            _axisPanel.Visible = show;
            _mapView.ShowAxes = show;

            if (show)
            {
                _editPanel.Visible = false;
                _bulkPanel.Visible = false;
                _objectiveEditPanel.Visible = false;
            }
            else
            {
                MapView_SelectionChanged(_mapView.SelectedZones.ToList());
            }

            _mapView.Invalidate();
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
            _axisPanel.Visible = false;
            _mapView.ShowAxes = false;

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
            _axisPanel.RefreshPaths();   // un voisin coché/décoché peut changer un chemin
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

        // Remet dans State ce que l'éditeur a en mémoire. State peut avoir été remplacé par un
        // rechargement depuis le disque (Save, Test spawn solver...) :
        //  - les axes : l'éditeur a la bonne liste, pas le fichier
        //  - le compteur d'identifiants : jamais en dessous du plus grand id déjà utilisé
        private void SyncStateFromEditor()
        {
            WargameZoneRepository.State.Axes = _axes;

            int maxId = 0;
            foreach (WargameZoneData zone in _zones)
            {
                foreach (WargameFormation f in zone.Formations)
                {
                    if (f.FormationId > maxId) maxId = f.FormationId;
                }
            }

            if (WargameZoneRepository.State.NextFormationId <= maxId)
                WargameZoneRepository.State.NextFormationId = maxId + 1;
        }

        // Ecrit toujours dans la paire de fichiers du mode actuellement affiché -
        // jamais devine, jamais les deux à la fois.
        private void SaveZones(bool showConfirmation)
        {
            string target;
            List<string> placementFailures;
            List<string> objectiveProblems;

            // Une valeur tapée dans un champ (priorité, supply...) n'est validée qu'à la sortie du champ :
            // on force la validation avant d'écrire, sinon la dernière saisie pourrait être perdue
            ValidateChildren();

            // Avant d'écrire : State doit refléter l'éditeur, pas un rechargement antérieur
            SyncStateFromEditor();

            if (_viewingActive)
            {
                string activePath = WargameZoneRepository.GetActiveWargameZonesPath(_campaignName);
                Saver_WargameZoneActive.Save(activePath, _zones);
                placementFailures = Saver_TargetList_Wargame.WriteNewActiveFormations(_campaignName);
                objectiveProblems = WargameObjectiveWriter.ApplyChanges(
                    Path.Combine(WargameZoneRepository.GetCampaignFolder(_campaignName), "Active", "targetlist.lua"),
                    _objectives, BuildObjectiveDesiredSides());
                target = "Active/wargame_zones.lua + Active/targetlist.lua";
            }
            else
            {
                Saver_WargameZoneInit.Save(_initLuaPath, _zones, WargameZoneRepository.State);
                placementFailures = Saver_TargetList_Wargame.WriteInitialFormations(_campaignName);
                objectiveProblems = WargameObjectiveWriter.ApplyChanges(
                    Path.Combine(WargameZoneRepository.GetCampaignFolder(_campaignName), "Init", "targetlist_init.lua"),
                    _objectives, BuildObjectiveDesiredSides());
                target = "Init/wargame_zones_init.lua + Init/targetlist_init.lua";
            }

            // Ancien DCE : ce qui est posé dans une zone tenue par l'autre camp est détruit
            // (Active : tout de suite ; Init : liste lue par scriptsMod au FirstMission + bâtiments de targetlist_init)
            string campaignFolder = WargameZoneRepository.GetCampaignFolder(_campaignName);
            List<string> oldDceReport = _viewingActive
                ? WargameOldDce.DestroyEnemyInZones(campaignFolder, _zones)
                : WargameOldDce.PrepareInitEnemyKills(campaignFolder, _zones);

            // Journal wargame : un petit fichier pour cet enregistrement (Debug\Wargame_DCEM\M0xx_EditorSave..._...)
            WargameLog.Begin(_campaignName, _viewingActive ? "EditorSaveActive" : "EditorSaveInit", WargameLogistics.ReadMission(campaignFolder));
            WargameLog.Line("saved to " + target);
            WargameLog.Line("zones : " + _zones.Count(z => z.Control == WargameSide.Blue) + " blue, " + _zones.Count(z => z.Control == WargameSide.Red) + " red, "
                + _zones.Sum(z => z.Formations.Count(fo => fo.ForcePower > 0)) + " living formation(s)");
            WargameLog.Lines(placementFailures, "NOT PLACED : ");
            WargameLog.Lines(objectiveProblems, "objective problem : ");
            WargameLog.Lines(oldDceReport, "old DCE : ");
            WargameLog.End();

            // Les écritures ci-dessus rechargent l'Init et remplacent State : on remet les axes
            // et le compteur de l'éditeur pour que la suite de la session reste cohérente.
            SyncStateFromEditor();

            // Les objectifs en mémoire sont relus depuis le fichier qui vient d'être écrit : ce qui s'affiche
            // ensuite est ce qui est VRAIMENT enregistré (et les clés/camps déplacés sont à jour)
            ReloadObjectivesFromFile();

            _dirty = false;

            if (oldDceReport.Count > 0)
            {
                MessageBox.Show(string.Join("\n\n", oldDceReport), "Wargame - old DCE units", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }

            if (objectiveProblems.Count > 0)
            {
                MessageBox.Show(
                    "Zones saved, but some OBJECTIVE changes were NOT written correctly:\n\n- "
                    + string.Join("\n- ", objectiveProblems)
                    + "\n\nThe objective panel now shows what is really in the file.",
                    "Wargame", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }

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
                string objectiveInfo = objectiveProblems.Count == 0 && _objectives.Count > 0
                    ? "\n" + _objectives.Count + " objective(s) written and checked."
                    : "";

                MessageBox.Show("Saved to " + target + "." + objectiveInfo, "Wargame", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        // Relit les objectifs du fichier de la vue affichée et remet l'affichage à jour.
        // L'objectif sélectionné est retrouvé par son nom pour que le panneau reste ouvert.
        private void ReloadObjectivesFromFile()
        {
            string selectedName = _selectedObjective != null ? _selectedObjective.Name : null;

            string path = _viewingActive
                ? Path.Combine(WargameZoneRepository.GetCampaignFolder(_campaignName), "Active", "targetlist.lua")
                : Path.Combine(WargameZoneRepository.GetCampaignFolder(_campaignName), "Init", "targetlist_init.lua");

            _objectives = File.Exists(path) ? new Parser_WargameObjectives().Load(path) : new List<WargameObjective>();
            _mapView.SetObjectives(_objectives);

            _selectedObjective = selectedName == null ? null : _objectives.FirstOrDefault(o => o.Name == selectedName);

            if (_selectedObjective != null && _objectiveEditPanel.Visible)
                MapView_ObjectiveClicked(_selectedObjective);
        }

        // Réglages du wargame : Init/Wargame/wargame_balance.lua, édité avec le même formulaire que conf_mod
        // (les tags "@ui" du fichier décident des champs, des groupes et de l'aide).
        private void OpenBalanceSettings()
        {
            string path = Path.Combine(WargameZoneRepository.GetCampaignFolder(_campaignName), "Init", "Wargame", "wargame_balance.lua");

            if (!File.Exists(path))
            {
                MessageBox.Show("wargame_balance.lua not found:\n" + path
                    + "\n\nIt is created by DCE_Manager when camp.wargame_config exists in Init/camp_init.lua.",
                    "Wargame settings", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            using (var form = new ConfModForm(_campaignName, path, "Wargame settings",
                "Used by the Preview and by the next turns. Values here win over camp_init.lua."))
            {
                form.ShowDialog(this);
            }

            // Relit les réglages : la carte de ravitaillement de l'éditeur et la prochaine Preview s'en servent tout de suite
            _campaignInfo.CombatConfig = WargameCampaignInfo.Load(_campaignName).CombatConfig;
            RefreshSupplyOverlay();
            RefreshSteps();   // convois ON/OFF change ce que l'étape Templates exige
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

        // Simulation à blanc sur la vue affichée (Init ou Active, modifications non
        // enregistrées comprises). Rien n'est écrit.
        private void OpenCombatPreview()
        {
            using (var form = new WargameCombatPreview_Form(
                _campaignName, _zones, _axes, _templateCatalog, _campaignInfo,
                _mapImage, _calibration, _objectives))
            {
                form.ShowDialog(this);
            }
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
                // Une boîte de débarquement n'est voisine de personne (et personne ne l'est d'elle)
                if (WargameEngineCombat.IsLandingZone(zone))
                {
                    zone.Neighbors = new List<string>();
                    continue;
                }

                if (detected.TryGetValue(zone.Id, out List<string> neighbors))
                {
                    neighbors.RemoveAll(n => n.StartsWith(WargameEngineCombat.LandingZonePrefix, StringComparison.OrdinalIgnoreCase));
                    zone.Neighbors = neighbors;
                }
            }

            _dirty = true;
            _mapView.Invalidate();

            if (_currentZone != null)
                _editPanel.LoadZone(_currentZone, _zones);

            _axisPanel.RefreshPaths();

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
            _mapView.LandingEditable = true;
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

            // Ravitaillement : valeur apportée à la zone (port, entrepôt, base...) et pont
            var supplyLabel = new Label { Text = "Supply capacity (0 = none, 100 = supplies a whole army)", Location = new Point(10, 192), AutoSize = true };
            _numObjSupply = new NumericUpDown { Location = new Point(10, 209), Width = 260, Minimum = 0, Maximum = 9999, DecimalPlaces = 1, Increment = 5m };
            _numObjSupply.ValueChanged += (s, e) =>
            {
                if (_loadingObjective || _selectedObjective == null) return;
                _selectedObjective.SupplyValue = (double)_numObjSupply.Value;
                _dirty = true;
            };

            _chkObjBridge = new CheckBox { Text = "Bridge (road flow drops if destroyed)", Location = new Point(10, 242), AutoSize = true };
            _chkObjBridge.CheckedChanged += (s, e) =>
            {
                if (_loadingObjective || _selectedObjective == null) return;
                _selectedObjective.IsBridge = _chkObjBridge.Checked;
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
            panel.Controls.Add(supplyLabel);
            panel.Controls.Add(_numObjSupply);
            panel.Controls.Add(_chkObjBridge);

            return panel;
        }

        private bool _loadingObjective;

        private void MapView_ObjectiveClicked(WargameObjective obj)
        {
            _selectedObjective = obj;

            _editPanel.Visible = false;
            _bulkPanel.Visible = false;
            _axisPanel.Visible = false;
            _mapView.ShowAxes = false;
            _objectiveEditPanel.Visible = true;

            // try/finally : si une exception survenait pendant le remplissage, _loadingObjective resterait
            // à true et TOUTES les modifications suivantes seraient ignorées sans le moindre message
            _loadingObjective = true;
            try
            {
                ((Label)_objectiveEditPanel.Tag).Text = "Objective: " + obj.Name;
                _numObjPriority.Value = Math.Max(_numObjPriority.Minimum, Math.Min(_numObjPriority.Maximum, obj.Priority));
                _txtObjAttributes.Text = string.Join(", ", obj.Attributes);
                _numObjFirepowerMin.Value = (decimal)Math.Max((double)_numObjFirepowerMin.Minimum, Math.Min((double)_numObjFirepowerMin.Maximum, obj.FirepowerMin));
                _numObjFirepowerMax.Value = (decimal)Math.Max((double)_numObjFirepowerMax.Minimum, Math.Min((double)_numObjFirepowerMax.Maximum, obj.FirepowerMax));
                _numObjSupply.Value = (decimal)Math.Max((double)_numObjSupply.Minimum, Math.Min((double)_numObjSupply.Maximum, obj.SupplyValue));
                _chkObjBridge.Checked = obj.IsBridge;
            }
            finally
            {
                _loadingObjective = false;
            }
        }
    }
}
