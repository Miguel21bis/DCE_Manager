using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using DCE_Manager.Utils;

namespace DCE_Manager
{
    // Panneau d'édition des champs "de jeu" d'une WargameZoneData (tout sauf la
    // géométrie, qui vient du parser et n'est pas éditable ici).
    //
    // Chaque contrôle écrit directement dans l'objet zone au fur et à mesure
    // (pas de bouton "Save" ici : l'écriture disque est gérée par la Form).
    // ZoneModified prévient l'extérieur qu'un redraw peut être nécessaire.
    internal class ucWargameZoneEditPanel : UserControl
    {
        private WargameZoneData _zone;
        private WargameCampaignInfo _campaignInfo;
        private WargameTemplateCatalog _templateCatalog;
        private List<WargameZoneData> _allZones = new List<WargameZoneData>();
        private bool _showAllNeighbors = false;

        // true pendant qu'on recharge les champs depuis _zone, pour ne pas
        // redéclencher les handlers de changement et écrire n'importe quoi dedans
        private bool _loading = false;

        public event Action ZoneModified;

        // -------------------- Contrôles --------------------

        private Label _labelZoneId;
        private ComboBox _comboControl;

        private DataGridView _gridFormations;
        private DataGridViewComboBoxColumn _colTemplate;
        private Label _labelTotalStock;

        private CheckBox _checkIrregularActive;
        private NumericUpDown _numResidualStock;

        private NumericUpDown _numResupplyModifier;
        private NumericUpDown _numSupplySource;
        private ComboBox _comboSupplyRoute;
        private ComboBox _comboTerrain;

        private CheckedListBox _listNeighbors;
        private Button _buttonShowAllNeighbors;

        // Gardés en champs pour pouvoir les redimensionner quand le panneau change
        // de largeur (voir ApplyLayout) : dans un FlowLayoutPanel TopDown, les
        // enfants ne suivent pas tout seuls la largeur du conteneur.
        private FlowLayoutPanel _flow;
        private GroupBox _groupControl, _groupUnits, _groupIrregular, _groupResupply, _groupTerrain, _groupNeighbors;

        public ucWargameZoneEditPanel()
        {
            BuildUi();
            SetZoneControlsEnabled(false); // rien de chargé au départ

            Resize += (s, e) => ApplyLayout();
            ApplyLayout();
        }

        // Fait suivre la largeur des groupes et de leurs contrôles internes quand on
        // déplace le splitter. Pas d'Anchor ici : dans un FlowLayoutPanel TopDown,
        // Anchor Top|Bottom fait s'effondrer la hauteur à zéro.
        private void ApplyLayout()
        {
            if (_flow == null) return;

            int width = _flow.ClientSize.Width - 28; // marge + barre de défilement éventuelle
            if (width < 250) width = 250;

            foreach (GroupBox g in new[] { _groupControl, _groupUnits, _groupIrregular, _groupResupply, _groupTerrain, _groupNeighbors })
            {
                if (g != null) g.Width = width;
            }

            int inner = width - 20;

            if (_comboControl != null) _comboControl.Width = inner;
            if (_gridFormations != null) _gridFormations.Width = inner;
            if (_listNeighbors != null) _listNeighbors.Width = inner;
            if (_buttonShowAllNeighbors != null) _buttonShowAllNeighbors.Width = inner;
            if (_comboTerrain != null) _comboTerrain.Width = inner;
            if (_comboSupplyRoute != null) _comboSupplyRoute.Width = Math.Max(80, inner - 110);
        }

        // Donne au panneau les libellés des camps, la liste des templates et leur
        // catalogue de caractéristiques. A appeler avant le premier LoadZone().
        public void SetCampaignInfo(WargameCampaignInfo campaignInfo, WargameTemplateCatalog templateCatalog)
        {
            _campaignInfo = campaignInfo;
            _templateCatalog = templateCatalog;
            RebuildControlCombo();
        }

        private void RebuildControlCombo()
        {
            _comboControl.Items.Clear();

            if (_campaignInfo == null)
                return;

            // On affiche les libellés (US / Iran / Contested), la valeur interne
            // (blue / red / contested) est retrouvée par position dans la liste.
            _comboControl.Items.Add(_campaignInfo.BlueLabel);
            _comboControl.Items.Add(_campaignInfo.RedLabel);
            _comboControl.Items.Add("Contested");
        }

        private string ControlFromComboIndex(int index)
        {
            switch (index)
            {
                case 0: return WargameSide.Blue;
                case 1: return WargameSide.Red;
                default: return WargameSide.Contested;
            }
        }

        private int ComboIndexFromControl(string control)
        {
            if (control == WargameSide.Blue) return 0;
            if (control == WargameSide.Red) return 1;
            return 2;
        }

        // -------------------- Chargement --------------------

        // allZones : toutes les zones connues (la zone en cours est retirée
        // automatiquement) - la géométrie sert à filtrer la liste des voisins
        // proposés par distance, pas juste à lister des noms.
        public void LoadZone(WargameZoneData zone, IEnumerable<WargameZoneData> allZones)
        {
            _zone = zone;

            if (_zone == null)
            {
                _allZones = new List<WargameZoneData>();
                SetZoneControlsEnabled(false);
                return;
            }

            _allZones = (allZones ?? Enumerable.Empty<WargameZoneData>())
                .Where(z => !z.Id.Equals(_zone.Id, StringComparison.OrdinalIgnoreCase))
                .ToList();

            SetZoneControlsEnabled(true);

            _loading = true;
            try
            {
                _labelZoneId.Text = "Zone: " + _zone.Id;

                _comboControl.SelectedIndex = ComboIndexFromControl(_zone.Control);

                RefreshTemplateChoices();
                LoadFormationsGrid();

                _checkIrregularActive.Checked = _zone.Irregular != null && _zone.Irregular.Active;
                _numResidualStock.Enabled = _checkIrregularActive.Checked;
                _numResidualStock.Value = _zone.Irregular != null
                    ? Clamp(_zone.Irregular.ResidualStock, _numResidualStock)
                    : 0;

                _numResupplyModifier.Value = Math.Max(_numResupplyModifier.Minimum,
                    Math.Min(_numResupplyModifier.Maximum, (decimal)_zone.ResupplyModifier));

                _numSupplySource.Value = Math.Max(_numSupplySource.Minimum,
                    Math.Min(_numSupplySource.Maximum, (decimal)_zone.SupplySource));

                _comboSupplyRoute.SelectedItem = _zone.SupplyRoute;
                _comboTerrain.SelectedItem = _zone.Terrain;

                _showAllNeighbors = false;
                LoadNeighborsList();
            }
            finally
            {
                _loading = false;
            }
        }

        private static decimal Clamp(int value, NumericUpDown numeric)
        {
            return Math.Max(numeric.Minimum, Math.Min(numeric.Maximum, value));
        }

        private void SetZoneControlsEnabled(bool enabled)
        {
            foreach (Control c in Controls)
                c.Enabled = enabled;
        }

        // -------------------- Force groups --------------------

        // Les templates proposés dépendent du camp de la zone : une zone tenue par
        // le camp bleu ne propose que Templates/wargame_blue, une zone contestée
        // propose les deux.
        private void RefreshTemplateChoices()
        {
            _colTemplate.Items.Clear();

            if (_campaignInfo == null || _zone == null)
                return;

            foreach (string template in _campaignInfo.GetTemplatesFor(_zone.Control))
                _colTemplate.Items.Add(template);

            // Les unités déjà posées peuvent référencer un template qui n'est plus
            // proposé (zone passée à l'autre camp, .stm supprimé). On les remet dans
            // la liste, sinon la cellule refuse la valeur -> DataError du DataGridView.
            foreach (WargameFormation f in _zone.Formations)
            {
                if (!string.IsNullOrEmpty(f.Template) && !_colTemplate.Items.Contains(f.Template))
                    _colTemplate.Items.Add(f.Template);
            }
        }

        private void LoadFormationsGrid()
        {
            _gridFormations.Rows.Clear();

            foreach (WargameFormation f in _zone.Formations)
            {
                // Si une formation référence un template qui n'est plus proposé (zone
                // passée à l'autre camp, fichier .stm supprimé), on l'ajoute quand
                // même à la liste : sinon la cellule refuserait la valeur et la perdrait.
                if (!string.IsNullOrEmpty(f.Template) && !_colTemplate.Items.Contains(f.Template))
                    _colTemplate.Items.Add(f.Template);

                int rowIndex = _gridFormations.Rows.Add(
                    f.Template, f.Name, f.Multiplier, f.ForcePower,
                    f.Priority, string.Join(", ", f.Attributes), f.FirepowerMin, f.FirepowerMax);

                // L'identifiant n'est pas affiché (non modifiable) mais doit suivre sa
                // ligne : on l'accroche au Tag pour le retrouver à la synchronisation.
                _gridFormations.Rows[rowIndex].Tag = f.FormationId;
            }

            UpdateTotalStockLabel();
        }

        private void SyncFormationsFromGrid()
        {
            var formations = new List<WargameFormation>();

            foreach (DataGridViewRow row in _gridFormations.Rows)
            {
                if (row.IsNewRow) continue;

                string template = row.Cells["colTemplate"].Value?.ToString();
                if (string.IsNullOrWhiteSpace(template)) continue;

                // xN vide -> on prend la valeur par défaut du catalogue pour ce template.
                // Une valeur saisie à la main reste prioritaire (surcharge par zone).
                int defaultMultiplier = _templateCatalog?.GetDefaultMultiplier(template) ?? 1;

                var f = new WargameFormation
                {
                    Template = template,
                    Name = row.Cells["colName"].Value?.ToString() ?? "",
                    Side = _campaignInfo?.GetSideOfTemplate(template) ?? "",
                    Multiplier = ParseCell(row.Cells["colMultiplier"], defaultMultiplier),
                };

                // ForcePower vide (nouvelle ligne) -> initialisée à la valeur nominale
                f.ForcePower = ParseCellDouble(row.Cells["colForcePower"], f.GetNominalForcePower(_templateCatalog));

                WargameTemplateEntry catalogEntry = _templateCatalog?.Find(template);

                f.Priority = (int)ParseCellDouble(row.Cells["colPriority"], catalogEntry?.Priority ?? 5);

                string attributesText = row.Cells["colAttributes"].Value?.ToString();
                f.Attributes = string.IsNullOrWhiteSpace(attributesText)
                    ? (catalogEntry?.Attributes ?? new List<string> { "Vehicles" })
                    : attributesText.Split(',').Select(a => a.Trim()).Where(a => a.Length > 0).ToList();

                f.FirepowerMin = ParseCellDouble(row.Cells["colFirepowerMin"], catalogEntry?.FirepowerMin ?? 2);
                f.FirepowerMax = ParseCellDouble(row.Cells["colFirepowerMax"], catalogEntry?.FirepowerMax ?? 2);

                // Identifiant : repris du Tag si la ligne existait déjà, sinon alloué
                // maintenant. Une fois attribué il ne bouge plus, même si le nom ou le
                // template change - c'est lui la vraie clé, pas le nom.
                if (row.Tag is int existingId && existingId > 0)
                {
                    f.FormationId = existingId;
                }
                else
                {
                    f.FormationId = WargameZoneRepository.State.AllocateFormationId();
                    row.Tag = f.FormationId;
                }

                formations.Add(f);
            }

            _zone.Formations = formations;
            UpdateTotalStockLabel();
        }

        private static double ParseCellDouble(DataGridViewCell cell, double fallback)
        {
            double value;
            if (double.TryParse(cell.Value?.ToString(), out value))
                return value;

            return fallback;
        }

        private static int ParseCell(DataGridViewCell cell, int fallback)
        {
            int value;
            if (int.TryParse(cell.Value?.ToString(), out value))
                return value;

            return fallback;
        }

        private void UpdateTotalStockLabel()
        {
            _labelTotalStock.Text = _zone != null
                ? "Zone total ForcePower: " + Math.Round(_zone.GetTotalForcePower(), 1)
                : "Zone total ForcePower: -";
        }

        private void GridFormations_CurrentCellDirtyStateChanged(object sender, EventArgs e)
        {
            // Force la validation immédiate d'une sélection dans une ComboBox, pour que
            // CellValueChanged parte tout de suite et remplisse les valeurs par défaut.
            if (_gridFormations.IsCurrentCellDirty &&
                _gridFormations.CurrentCell is DataGridViewComboBoxCell)
            {
                _gridFormations.CommitEdit(DataGridViewDataErrorContexts.Commit);
            }
        }

        private void GridFormations_CellValueChanged(object sender, DataGridViewCellEventArgs e)
        {
            if (_loading || _zone == null || e.RowIndex < 0) return;

            // Changer xN change l'échelle de la formation, donc sa force nominale :
            // on recalcule ForcePower. Elle reste éditable ensuite pour permettre de
            // saisir une formation déjà entamée, jusqu'au prochain changement de xN.
            if (e.ColumnIndex == _gridFormations.Columns["colMultiplier"].Index)
                RecomputeForcePower(e.RowIndex);

            FillRowDefaults(e.RowIndex);
            SyncFormationsFromGrid();
            ZoneModified?.Invoke();
        }

        private void RecomputeForcePower(int rowIndex)
        {
            DataGridViewRow row = _gridFormations.Rows[rowIndex];

            string template = row.Cells["colTemplate"].Value?.ToString();
            if (string.IsNullOrWhiteSpace(template)) return;

            int multiplier = ParseCell(row.Cells["colMultiplier"], 1);
            double power = _templateCatalog?.GetPower(template) ?? 10.0;

            _loading = true;
            try
            {
                row.Cells["colForcePower"].Value = multiplier * power;
            }
            finally
            {
                _loading = false;
            }
        }

        // Complète les cellules encore vides d'une ligne dès qu'un template y est choisi.
        // On écrit directement dans les cellules plutôt que de recharger toute la grille :
        // un Rows.Clear() pendant l'édition provoque un appel réentrant du DataGridView.
        private void FillRowDefaults(int rowIndex)
        {
            DataGridViewRow row = _gridFormations.Rows[rowIndex];

            string template = row.Cells["colTemplate"].Value?.ToString();
            if (string.IsNullOrWhiteSpace(template)) return;

            _loading = true;
            try
            {
                if (IsCellEmpty(row.Cells["colName"]))
                    row.Cells["colName"].Value = SuggestFormationName(template);

                if (IsCellEmpty(row.Cells["colMultiplier"]))
                    row.Cells["colMultiplier"].Value = _templateCatalog?.GetDefaultMultiplier(template) ?? 1;

                if (IsCellEmpty(row.Cells["colForcePower"]))
                {
                    int multiplier = ParseCell(row.Cells["colMultiplier"], 1);
                    double power = _templateCatalog?.GetPower(template) ?? 10.0;
                    row.Cells["colForcePower"].Value = multiplier * power;
                }

                if (IsCellEmpty(row.Cells["colPriority"]))
                    row.Cells["colPriority"].Value = _templateCatalog?.Find(template)?.Priority ?? 5;

                if (IsCellEmpty(row.Cells["colAttributes"]))
                {
                    List<string> attrs = _templateCatalog?.Find(template)?.Attributes;
                    row.Cells["colAttributes"].Value = attrs != null ? string.Join(", ", attrs) : "Vehicles";
                }

                if (IsCellEmpty(row.Cells["colFirepowerMin"]))
                    row.Cells["colFirepowerMin"].Value = _templateCatalog?.Find(template)?.FirepowerMin ?? 2;

                if (IsCellEmpty(row.Cells["colFirepowerMax"]))
                    row.Cells["colFirepowerMax"].Value = _templateCatalog?.Find(template)?.FirepowerMax ?? 2;

            }
            finally
            {
                _loading = false;
            }
        }

        private static bool IsCellEmpty(DataGridViewCell cell)
        {
            return string.IsNullOrWhiteSpace(cell.Value?.ToString());
        }

        // Nom proposé à la création, que le campaignMaker peut écraser librement.
        // Format : <camp>_<numéro>_<type>, ex: RED_003_ARMOR.
        private string SuggestFormationName(string template)
        {
            string side = _campaignInfo?.GetSideOfTemplate(template) ?? "";
            string sidePrefix = side == WargameSide.Blue ? "BLUE" : side == WargameSide.Red ? "RED" : "UNK";

            WargameTemplateEntry entry = _templateCatalog?.Find(template);
            string typeSuffix = (entry?.Type ?? "unit").ToUpperInvariant();

            // Numéro basé sur le prochain identifiant, sans le consommer : deux formations
            // peuvent donc recevoir le même numéro proposé, c'est un nom, pas une clé.
            int number = WargameZoneRepository.State.NextFormationId;

            return sidePrefix + "_" + number.ToString("000") + "_" + typeSuffix;
        }

        private void GridFormations_DataError(object sender, DataGridViewDataErrorEventArgs e)
        {
            // Remplace la boîte de dialogue par défaut du DataGridView (typiquement une
            // valeur de combo pas présente dans la liste) par une simple ligne de log.
            e.ThrowException = false;
            FormUtils.LogRegister("ucWargameZoneEditPanel | DataGridView DataError : " + e.Exception?.Message);
        }

        private void GridFormations_UserDeletedRow(object sender, DataGridViewRowEventArgs e)
        {
            if (_loading || _zone == null) return;

            SyncFormationsFromGrid();
            ZoneModified?.Invoke();
        }

        // -------------------- Neighbors --------------------

        // Seuil d'affichage = seuil de détection auto + 20 % de marge. Une zone
        // déjà cochée manuellement reste toujours visible même au-delà, sinon
        // impossible de la décocher (cas d'une île reliée par la mer, par ex.).
        private const double NeighborListMarginFactor = 1.2;

        private void LoadNeighborsList()
        {
            _listNeighbors.Items.Clear();

            double maxDistance = WargameNeighborDetector.DefaultMaxGapMeters * NeighborListMarginFactor;

            foreach (WargameZoneData candidate in _allZones)
            {
                bool isNeighbor = _zone.Neighbors != null &&
                    _zone.Neighbors.Contains(candidate.Id, StringComparer.OrdinalIgnoreCase);

                if (!_showAllNeighbors && !isNeighbor)
                {
                    double distance = WargameNeighborDetector.GetMinDistance(_zone.DcsPoints, candidate.DcsPoints);
                    if (distance > maxDistance)
                        continue;
                }

                _listNeighbors.Items.Add(candidate.Id, isNeighbor);
            }

            _buttonShowAllNeighbors.Text = _showAllNeighbors
                ? "Show only nearby zones"
                : "Show all zones...";
        }

        private void ButtonShowAllNeighbors_Click(object sender, EventArgs e)
        {
            _showAllNeighbors = !_showAllNeighbors;
            LoadNeighborsList();
        }

        private void ListNeighbors_ItemCheck(object sender, ItemCheckEventArgs e)
        {
            if (_loading || _zone == null) return;

            // ItemCheck se déclenche AVANT que l'état ne soit appliqué à l'item,
            // donc on se fie à e.NewValue plutôt qu'à l'état actuel de l'item.
            string id = _listNeighbors.Items[e.Index].ToString();
            bool willBeChecked = e.NewValue == CheckState.Checked;

            if (_zone.Neighbors == null)
                _zone.Neighbors = new List<string>();

            if (willBeChecked && !_zone.Neighbors.Contains(id, StringComparer.OrdinalIgnoreCase))
            {
                _zone.Neighbors.Add(id);
            }
            else if (!willBeChecked)
            {
                _zone.Neighbors.RemoveAll(n => n.Equals(id, StringComparison.OrdinalIgnoreCase));
            }

            ZoneModified?.Invoke();
        }

        // -------------------- Autres champs --------------------

        private void ComboControl_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (_loading || _zone == null) return;

            _zone.Control = ControlFromComboIndex(_comboControl.SelectedIndex);

            // Le camp a changé -> les templates proposables changent aussi
            RefreshTemplateChoices();

            ZoneModified?.Invoke();
        }

        private void CheckIrregularActive_CheckedChanged(object sender, EventArgs e)
        {
            if (_loading || _zone == null) return;

            _numResidualStock.Enabled = _checkIrregularActive.Checked;

            if (_checkIrregularActive.Checked)
            {
                if (_zone.Irregular == null)
                    _zone.Irregular = new WargameIrregularMarker();

                _zone.Irregular.Active = true;
            }
            else if (_zone.Irregular != null)
            {
                _zone.Irregular.Active = false;
            }

            ZoneModified?.Invoke();
        }

        private void NumResidualStock_ValueChanged(object sender, EventArgs e)
        {
            if (_loading || _zone == null || _zone.Irregular == null) return;

            _zone.Irregular.ResidualStock = (int)_numResidualStock.Value;
            ZoneModified?.Invoke();
        }

        private void NumResupplyModifier_ValueChanged(object sender, EventArgs e)
        {
            if (_loading || _zone == null) return;

            _zone.ResupplyModifier = (double)_numResupplyModifier.Value;
            ZoneModified?.Invoke();
        }

        private void NumSupplySource_ValueChanged(object sender, EventArgs e)
        {
            if (_loading || _zone == null) return;

            _zone.SupplySource = (double)_numSupplySource.Value;
            ZoneModified?.Invoke();
        }

        private void ComboSupplyRoute_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (_loading || _zone == null) return;

            _zone.SupplyRoute = _comboSupplyRoute.SelectedItem?.ToString() ?? WargameSupplyRoute.Road;
            ZoneModified?.Invoke();
        }

        private void ComboTerrain_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (_loading || _zone == null) return;

            _zone.Terrain = _comboTerrain.SelectedItem?.ToString() ?? WargameTerrain.Plain;
            ZoneModified?.Invoke();
        }

        // Un seul clic sur la colonne Template ouvre directement la liste déroulante,
        // au lieu d'exiger un premier clic pour entrer dans la cellule puis un second
        // pour l'ouvrir.
        private void GridFormations_CellClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0 || e.ColumnIndex < 0) return;
            if (_gridFormations.Columns[e.ColumnIndex].Name != "colTemplate") return;

            _gridFormations.BeginEdit(true);
        }

        private void GridFormations_EditingControlShowing(object sender, DataGridViewEditingControlShowingEventArgs e)
        {
            if (_gridFormations.CurrentCell?.OwningColumn?.Name == "colTemplate" && e.Control is ComboBox combo)
                combo.DroppedDown = true;
        }

        // Retire la formation actuellement sélectionnée dans la grille.
        private void ButtonRemoveFormation_Click(object sender, EventArgs e)
        {
            if (_gridFormations.CurrentRow == null || _gridFormations.CurrentRow.IsNewRow)
                return;

            _gridFormations.Rows.Remove(_gridFormations.CurrentRow);
            SyncFormationsFromGrid();
            ZoneModified?.Invoke();
        }

        // -------------------- Construction de l'UI --------------------

        // FlowLayoutPanel TopDown avec des groupes en Width/Height fixes (pas
        // d'Anchor Top|Bottom ici - piège connu qui collapse la hauteur à zéro
        // dans un FlowLayoutPanel TopDown).
        private void BuildUi()
        {
            var flow = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoScroll = true,
            };
            _flow = flow;
            Controls.Add(flow);

            _labelZoneId = new Label
            {
                Text = "Zone: -",
                Font = new Font(Font, FontStyle.Bold),
                AutoSize = true,
                Margin = new Padding(4, 4, 4, 10),
            };
            flow.Controls.Add(_labelZoneId);

            // ---- Control (camp) ----
            var groupControl = new GroupBox { Text = "Control", Width = 300, Height = 60 };
            _groupControl = groupControl;
            _comboControl = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList, // liste fermée : 3 valeurs possibles
                Location = new Point(10, 22),
                Width = 260,
            };
            _comboControl.SelectedIndexChanged += ComboControl_SelectedIndexChanged;
            groupControl.Controls.Add(_comboControl);
            flow.Controls.Add(groupControl);

            // ---- Units ----
            var groupFormations = new GroupBox { Text = "Units", Width = 300, Height = 280 };
            _groupUnits = groupFormations;

            _gridFormations = new DataGridView
            {
                Location = new Point(10, 20),
                Width = 280,
                Height = 220,
                AllowUserToAddRows = true,
                AllowUserToDeleteRows = true,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                RowHeadersVisible = false,
                ScrollBars = ScrollBars.Both,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None,
            };

            _colTemplate = new DataGridViewComboBoxColumn
            {
                Name = "colTemplate",
                HeaderText = "Template",
                FillWeight = 40,
            };
            _gridFormations.Columns.Add(_colTemplate);
            _gridFormations.Columns.Add("colName", "Name");
            _gridFormations.Columns.Add("colMultiplier", "xN");
            _gridFormations.Columns.Add("colForcePower", "ForcePower");
            _gridFormations.Columns.Add("colPriority", "tgt_Priority");
            _gridFormations.Columns.Add("colAttributes", "tgt_Attributes");
            _gridFormations.Columns.Add("colFirepowerMin", "tgt_FP_min");
            _gridFormations.Columns.Add("colFirepowerMax", "tgt_FP_max");

            _gridFormations.Columns["colTemplate"].Width = 150;
            _gridFormations.Columns["colName"].Width = 110;
            _gridFormations.Columns["colMultiplier"].Width = 45;
            _gridFormations.Columns["colForcePower"].Width = 75;
            _gridFormations.Columns["colPriority"].Width = 65;
            _gridFormations.Columns["colAttributes"].Width = 120;
            _gridFormations.Columns["colFirepowerMin"].Width = 65;
            _gridFormations.Columns["colFirepowerMax"].Width = 65;

            // CurrentCellDirtyStateChanged + CommitEdit : sans ça une ComboBox ne valide
            // sa valeur qu'en quittant la cellule, donc les valeurs par défaut ne se
            // remplissaient qu'après un clic ailleurs dans la grille.
            _gridFormations.CurrentCellDirtyStateChanged += GridFormations_CurrentCellDirtyStateChanged;
            _gridFormations.EditMode = DataGridViewEditMode.EditOnEnter;
            _gridFormations.CellClick += GridFormations_CellClick;
            _gridFormations.EditingControlShowing += GridFormations_EditingControlShowing;

            _gridFormations.CellValueChanged += GridFormations_CellValueChanged;
            _gridFormations.UserDeletedRow += GridFormations_UserDeletedRow;
            _gridFormations.DataError += GridFormations_DataError;
            groupFormations.Controls.Add(_gridFormations);

            var buttonRemoveFormation = new Button
            {
                Text = "Remove selected formation",
                Location = new Point(10, _gridFormations.Bottom + 6),
                Width = _gridFormations.Width,
                Height = 26,
            };
            buttonRemoveFormation.Click += ButtonRemoveFormation_Click;
            groupFormations.Controls.Add(buttonRemoveFormation);

            _labelTotalStock = new Label
            {
                Text = "Zone total ForcePower: -",
                Location = new Point(10, 172),
                AutoSize = true,
            };
            groupFormations.Controls.Add(_labelTotalStock);

            flow.Controls.Add(groupFormations);

            // ---- Irregular (asymmetric mode) ----
            var groupIrregular = new GroupBox { Text = "Irregular (asymmetric mode)", Width = 300, Height = 110 };
            _groupIrregular = groupIrregular;

            _checkIrregularActive = new CheckBox
            {
                Text = "Active",
                Location = new Point(10, 22),
                AutoSize = true,
            };
            _checkIrregularActive.CheckedChanged += CheckIrregularActive_CheckedChanged;

            var labelResidual = new Label
            {
                Text = "Residual stock",
                Location = new Point(10, 52),
                AutoSize = true,
            };

            _numResidualStock = new NumericUpDown
            {
                Location = new Point(120, 48),
                Width = 80,
                Minimum = 0,
                Maximum = 9999,
            };
            _numResidualStock.ValueChanged += NumResidualStock_ValueChanged;

            groupIrregular.Controls.Add(_checkIrregularActive);
            groupIrregular.Controls.Add(labelResidual);
            groupIrregular.Controls.Add(_numResidualStock);
            flow.Controls.Add(groupIrregular);

            // ---- Supply ----
            var groupResupply = new GroupBox { Text = "Supply", Width = 300, Height = 120 };
            _groupResupply = groupResupply;

            var labelModifier = new Label { Text = "Modifier", Location = new Point(10, 24), AutoSize = true };
            _numResupplyModifier = new NumericUpDown
            {
                Location = new Point(105, 21),
                Width = 90,
                DecimalPlaces = 2,
                Minimum = -10,
                Maximum = 10,
                Increment = 0.1m,
            };
            _numResupplyModifier.ValueChanged += NumResupplyModifier_ValueChanged;

            // 0 = zone ordinaire. Une valeur > 0 fait de la zone un point d'injection
            // de ravitaillement dans le réseau (port, base arrière, frontière amie).
            var labelSource = new Label { Text = "Source", Location = new Point(10, 54), AutoSize = true };
            _numSupplySource = new NumericUpDown
            {
                Location = new Point(105, 51),
                Width = 90,
                DecimalPlaces = 1,
                Minimum = 0,
                Maximum = 9999,
                Increment = 1m,
            };
            _numSupplySource.ValueChanged += NumSupplySource_ValueChanged;

            var labelRoute = new Label { Text = "Route", Location = new Point(10, 84), AutoSize = true };
            _comboSupplyRoute = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Location = new Point(105, 81),
                Width = 150,
            };
            foreach (string route in WargameSupplyRoute.All)
                _comboSupplyRoute.Items.Add(route);
            _comboSupplyRoute.SelectedIndexChanged += ComboSupplyRoute_SelectedIndexChanged;

            groupResupply.Controls.Add(labelModifier);
            groupResupply.Controls.Add(_numResupplyModifier);
            groupResupply.Controls.Add(labelSource);
            groupResupply.Controls.Add(_numSupplySource);
            groupResupply.Controls.Add(labelRoute);
            groupResupply.Controls.Add(_comboSupplyRoute);
            flow.Controls.Add(groupResupply);

            // ---- Terrain ----
            var groupTerrain = new GroupBox { Text = "Terrain", Width = 300, Height = 60 };
            _groupTerrain = groupTerrain;
            _comboTerrain = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Location = new Point(10, 22),
                Width = 260,
            };
            foreach (string terrain in WargameTerrain.All)
                _comboTerrain.Items.Add(terrain);
            _comboTerrain.SelectedIndexChanged += ComboTerrain_SelectedIndexChanged;
            groupTerrain.Controls.Add(_comboTerrain);
            flow.Controls.Add(groupTerrain);

            // ---- Neighbors ----
            var groupNeighbors = new GroupBox { Text = "Neighbors", Width = 300, Height = 195 };
            _groupNeighbors = groupNeighbors;
            _listNeighbors = new CheckedListBox
            {
                Location = new Point(10, 20),
                Width = 280,
                Height = 130,
                CheckOnClick = true,
            };
            _listNeighbors.ItemCheck += ListNeighbors_ItemCheck;
            groupNeighbors.Controls.Add(_listNeighbors);

            _buttonShowAllNeighbors = new Button
            {
                Text = "Show all zones...",
                Location = new Point(10, 156),
                Width = 280,
                Height = 26,
            };
            _buttonShowAllNeighbors.Click += ButtonShowAllNeighbors_Click;
            groupNeighbors.Controls.Add(_buttonShowAllNeighbors);

            flow.Controls.Add(groupNeighbors);
        }
    }
}
