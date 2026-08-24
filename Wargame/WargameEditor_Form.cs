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
    // Deux modes de sélection sur la carte (voir ucWargameMapView) :
    //  - clic simple : panneau d'édition complet pour cette seule zone
    //  - Ctrl+clic (plusieurs zones) : panneau réduit "assigner ce camp aux N zones",
    //    pour ne pas avoir à changer le camp zone par zone
    //
    // Si la campagne n'a pas encore de calibration, les zones sont chargées mais
    // ne peuvent pas être positionnées sur l'image (ucWargameMapView affiche
    // "Map not calibrated yet") - le bouton "Calibrate map..." ouvre l'outil dédié.
    internal class WargameEditor_Form : Form
    {
        private readonly string _campaignName;
        private readonly string _initLuaPath;
        private readonly string _mapImagePath;
        private readonly string _calibJsonPath;

        private List<WargameZoneData> _zones;
        private WargameMapCalibration _calibration;
        private WargameCampaignInfo _campaignInfo;
        private WargameTemplateCatalog _templateCatalog;

        // Zone affichée dans le panneau détaillé (uniquement en sélection simple).
        private WargameZoneData _currentZone;

        // Passe à true dès qu'une valeur de zone est modifiée, repasse à false au Save.
        // Ne couvre PAS le catalogue de templates, qui a sa propre Form et son propre Save.
        private bool _dirty;
        private Image _mapImage;

        private ucWargameMapView _mapView;
        private ucWargameZoneEditPanel _editPanel;

        // Panneau d'assignation groupée, visible seulement quand 2+ zones sont
        // sélectionnées (Ctrl+clic sur la carte). Occupe le même espace que
        // _editPanel, un seul des deux visible à la fois.
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

            LoadData();
            BuildUi();

            _mapView.LoadMap(_mapImage, _calibration, _zones);

            if (!_calibration.IsCalibrated)
            {
                MessageBox.Show(
                    "This campaign has no map calibration yet. Zones won't be visible until it's calibrated.",
                    "Wargame", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void LoadData()
        {
            _zones = WargameZoneRepository.LoadOrGenerateInit(_campaignName);
            _calibration = WargameMapCalibration.Load(_calibJsonPath);
            _campaignInfo = WargameCampaignInfo.Load(_campaignName);
            _templateCatalog = WargameTemplateCatalog.LoadAndSync(_campaignName, _campaignInfo);
            _mapImage = File.Exists(_mapImagePath) ? Image.FromFile(_mapImagePath) : null;
        }

        private void BuildUi()
        {
            _mapView = new ucWargameMapView { ReadOnly = false };
            _mapView.SelectionChanged += MapView_SelectionChanged;

            var mapPanel = new NoAutoScrollPanel { Dock = DockStyle.Fill, AutoScroll = true };
            mapPanel.Controls.Add(_mapView);

            _editPanel = new ucWargameZoneEditPanel { Dock = DockStyle.Fill };
            _editPanel.ZoneModified += EditPanel_ZoneModified;
            _editPanel.SetCampaignInfo(_campaignInfo, _templateCatalog);

            _bulkPanel = BuildBulkAssignPanel();

            // Barre du bas, 2 lignes fixes :
            //  - ligne du haut : les outils, qui peuvent se répartir sur 2 rangées
            //    si la fenêtre est étroite (WrapContents).
            //  - ligne du bas : Save seul, ancré à droite, jamais caché par le wrap
            //    des autres boutons puisqu'il est dans un conteneur séparé.
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

            var buttonCalibrate = new Button { Text = "Calibrate map...", Width = 140, Height = 30 };
            buttonCalibrate.Click += (s, e) => OpenCalibration();

            var buttonTemplates = new Button { Text = "Templates...", Width = 110, Height = 30 };
            buttonTemplates.Click += (s, e) => OpenTemplateCatalog();

            var buttonSpawnTest = new Button { Text = "Test spawn areas", Width = 140, Height = 30 };
            buttonSpawnTest.Click += (s, e) => TestSpawnAreas();

            var buttonSpawnSolverTest = new Button { Text = "Test spawn solver", Width = 140, Height = 30 };
            buttonSpawnSolverTest.Click += (s, e) => TestSpawnSolver();

            var buttonDetectNeighbors = new Button { Text = "Detect neighbors...", Width = 150, Height = 30 };
            buttonDetectNeighbors.Click += (s, e) => DetectNeighbors();

            // Rouge et volontairement à part du reste : c'est la seule action de
            // cette barre qui détruit du travail déjà fait sans possibilité de retour.
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
            secondaryButtons.Controls.Add(buttonCalibrate);
            secondaryButtons.Controls.Add(buttonTemplates);
            secondaryButtons.Controls.Add(buttonSpawnTest);
            secondaryButtons.Controls.Add(buttonSpawnSolverTest);
            secondaryButtons.Controls.Add(buttonDetectNeighbors);
            secondaryButtons.Controls.Add(buttonCreateWarzone);

            var saveRow = new Panel { Dock = DockStyle.Fill };

            var buttonSave = new Button
            {
                Text = "Save",
                Width = 100,
                Height = 30,
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
            };
            buttonSave.Click += (s, e) => SaveZones(true);
            buttonSave.Location = new Point(saveRow.Width - buttonSave.Width - 6, 4);

            saveRow.Controls.Add(buttonSave);
            saveRow.Resize += (s, e) => buttonSave.Location = new Point(saveRow.Width - buttonSave.Width - 6, 4);

            bottomPanel.Controls.Add(secondaryButtons, 0, 0);
            bottomPanel.Controls.Add(saveRow, 0, 1);

            var rightPanel = new Panel { Dock = DockStyle.Fill };
            rightPanel.Controls.Add(_editPanel);
            rightPanel.Controls.Add(_bulkPanel);
            rightPanel.Controls.Add(bottomPanel);

            var splitContainer = new SplitContainer { Dock = DockStyle.Fill, FixedPanel = FixedPanel.Panel2 };
            splitContainer.Panel1.Controls.Add(mapPanel);
            splitContainer.Panel2.Controls.Add(rightPanel);

            Controls.Add(splitContainer);

            // SplitterDistance posé dans l'initialiseur ne tient pas : le contrôle n'a
            // pas encore sa taille finale, et le SplitContainer réajuste ensuite la
            // position proportionnellement. On le fixe donc au Load, taille connue.
            Load += (s, e) => splitContainer.SplitterDistance = Math.Max(100, splitContainer.Width - 500);
        }

        // Panneau réduit affiché à la place de _editPanel dès que 2 zones ou plus
        // sont sélectionnées (Ctrl+clic sur la carte) : assigner un camp à toutes
        // en un coup, plutôt que de rouvrir le panneau complet zone par zone.
        // Ne touche qu'au Control - les autres champs (formations, terrain...)
        // n'ont pas de sens à assigner en masse de la même façon.
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

        // Sélection simple (0 ou 1 zone) -> panneau détaillé habituel.
        // Sélection multiple (2+) -> panneau réduit d'assignation groupée.
        private void MapView_SelectionChanged(List<WargameZoneData> selected)
        {
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
                    "You have unsaved zone changes. Save before closing?",
                    "Wargame",
                    MessageBoxButtons.YesNoCancel,
                    MessageBoxIcon.Warning);

                if (answer == DialogResult.Cancel)
                {
                    e.Cancel = true;
                    return;
                }

                if (answer == DialogResult.Yes)
                    SaveZones(false);
            }

            base.OnFormClosing(e);
        }

        // showConfirmation = false quand on enregistre juste avant de fermer :
        // enchaîner deux boîtes de dialogue serait pénible.
        private void SaveZones(bool showConfirmation)
        {
            Saver_WargameZoneInit.Save(_initLuaPath, _zones, WargameZoneRepository.State);
            _dirty = false;

            if (showConfirmation)
                MessageBox.Show("Zones saved.", "Wargame", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void OpenTemplateCatalog()
        {
            using (var form = new WargameTemplateCatalog_Form(_campaignName, _campaignInfo))
            {
                form.ShowDialog(this);
            }

            // Le catalogue a pu changer (valeurs, xN par défaut) : on le recharge pour
            // que le panneau d'édition reparte sur les bonnes valeurs.
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
                }
            }
        }

        // Temporaire : vérifie que wargame_spawn.miz est lu correctement.
        // A retirer une fois le solveur de placement en place.
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

        // Temporaire : place le premier template disponible du camp de la zone
        // sélectionnée, et affiche où chaque unité a atterri. A retirer une fois
        // le solveur validé et branché sur le vrai saver.
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

            //string control = _currentZone.Control == WargameSide.Blue || _currentZone.Control == WargameSide.Red
            //    ? _currentZone.Control
            //    : WargameSide.Blue; // zone contestée : on teste avec un template bleu, faute de mieux

            //List<string> candidates = _campaignInfo.GetTemplatesFor(control);
            //if (candidates.Count == 0)
            //{
            //    MessageBox.Show("No template available for this zone's camp.",
            //        "Wargame", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            //    return;
            //}

            //string templateName = candidates[0];
            //string stmPath = _campaignInfo.GetTemplateFilePath(templateName);

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

            List<WargamePlacedUnit> placed = WargameSpawnSolver.PlaceTemplate(_currentZone, layout, spawnAreas, new Random());

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
                report.AppendLine("  " + u.Name + " (" + u.Category + ") -> " + (int)u.Position.X + ", " + (int)u.Position.Y);

            MessageBox.Show(report.ToString(), "Wargame - spawn solver test",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        // Ecrase la liste de voisins de TOUTES les zones par une détection
        // automatique. Destructif pour les réglages manuels -> icône rouge.
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

        // Reparse wargame_zone.miz et ECRASE wargame_zones_init.lua depuis zéro :
        // tout travail déjà fait (camp, formations, voisins, terrain...) est perdu
        // définitivement. Réservé au cas où le campaignMaker veut repartir propre
        // après avoir redessiné toute sa carte de zones.
        private void CreateWarzone()
        {
            bool confirmed = FormUtils.ShowDangerConfirm(this,
                "This will PERMANENTLY ERASE the current wargame setup for this campaign "
                + "(control, formations, neighbors, terrain, supply...) and recreate everything "
                + "from wargame_zone.miz.\n\nThis cannot be undone.\n\nAre you sure?",
                "Create Warzone");

            if (!confirmed)
                return;

            _zones = WargameZoneRepository.ForceRegenerate(_campaignName);
            _dirty = false;

            // Vide la sélection ET remet le panneau détaillé (vide) en place via
            // MapView_SelectionChanged, qui reçoit la liste vide déclenchée ici.
            _mapView.ClearSelection();
            _mapView.LoadMap(_mapImage, _calibration, _zones);

            MessageBox.Show("Warzone recreated from wargame_zone.miz.",
                "Create Warzone", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }
}
