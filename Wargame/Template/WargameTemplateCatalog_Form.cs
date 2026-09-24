using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;

namespace DCE_Manager
{
    // Edition du catalogue de templates d'une campagne : une ligne par .stm trouvé
    // dans Templates/wargame_blue et wargame_red, avec ses caractéristiques de combat.
    //
    // La liste des lignes n'est pas modifiable ici (pas d'ajout/suppression) :
    // elle est le reflet des fichiers réellement présents sur disque, synchronisée
    // au chargement. Seules les valeurs sont éditables.
    internal class WargameTemplateCatalog_Form : Form
    {
        private readonly WargameTemplateCatalog _catalog;
        private readonly WargameCampaignInfo _campaignInfo;
        private readonly string _catalogPath;

        private DataGridView _grid;

        // Passe à true dès qu'une valeur est modifiée, repasse à false au Save
        private bool _dirty;

        public WargameTemplateCatalog_Form(string campaignName, WargameCampaignInfo campaignInfo)
        {
            _catalogPath = WargameZoneRepository.GetTemplateCatalogPath(campaignName);
            _campaignInfo = campaignInfo;
            _catalog = WargameTemplateCatalog.LoadAndSync(campaignName, campaignInfo);

            Text = "Wargame templates - " + campaignName;
            Width = 1150;
            Height = 600;
            StartPosition = FormStartPosition.CenterParent;

            BuildUi();
            LoadGrid();
        }

        private void BuildUi()
        {
            _grid = new DataGridView
            {
                Dock = DockStyle.Fill,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                RowHeadersVisible = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None,
            };

            var colTemplate = new DataGridViewTextBoxColumn { Name = "colTemplate", HeaderText = "Template", ReadOnly = true, Width = 160 };
            var colSide = new DataGridViewTextBoxColumn { Name = "colSide", HeaderText = "Side", ReadOnly = true, Width = 50 };
            var colType = new DataGridViewComboBoxColumn { Name = "colType", HeaderText = "Type", Width = 90 };
            foreach (string type in WargameUnitType.All)
                colType.Items.Add(type);

            _grid.Columns.Add(colTemplate);
            _grid.Columns.Add(colSide);
            _grid.Columns.Add(colType);
            _grid.Columns.Add("colPower", "Power");
            _grid.Columns.Add("colAttack", "Attack");
            _grid.Columns.Add("colDefense", "Defense");
            _grid.Columns.Add("colSupplyCost", "Supply cost");
            _grid.Columns.Add("colMultiplier", "Default xN");
            _grid.Columns.Add("colVehicles", "Units");
            _grid.Columns.Add("colPriority", "tgt_Priority");
            _grid.Columns.Add("colAttributes", "tgt_Attributes");
            _grid.Columns.Add("colFirepowerMin", "tgt_FP_min");
            _grid.Columns.Add("colFirepowerMax", "tgt_FP_max");

            _grid.Columns["colPower"].Width = 60;
            _grid.Columns["colAttack"].Width = 60;
            _grid.Columns["colDefense"].Width = 60;
            _grid.Columns["colSupplyCost"].Width = 80;
            _grid.Columns["colMultiplier"].Width = 70;
            _grid.Columns["colVehicles"].Width = 60;
            _grid.Columns["colPriority"].Width = 70;
            _grid.Columns["colAttributes"].Width = 150;
            _grid.Columns["colFirepowerMin"].Width = 80;
            _grid.Columns["colFirepowerMax"].Width = 80;

            _grid.CellEndEdit += Grid_CellEndEdit;
            _grid.DataError += Grid_DataError;

            var bottom = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                FlowDirection = FlowDirection.LeftToRight,
                Height = 40,
                WrapContents = false,
            };

            var buttonApplyDefaults = new Button { Text = "Apply type defaults", Width = 150, Height = 30 };
            buttonApplyDefaults.Click += (s, e) => ApplyTypeDefaults();

            var buttonRecount = new Button { Text = "Recount units", Width = 130, Height = 30 };
            buttonRecount.Click += (s, e) => RecountAll();

            var buttonSave = new Button { Text = "Save", Width = 100, Height = 30 };
            buttonSave.Click += (s, e) => SaveCatalog(true);

            var buttonHelp = new Button { Text = "?", Width = 40, Height = 30 };
            buttonHelp.Click += (s, e) => WargameHelp_Form.ShowHelp(this);

            bottom.Controls.Add(buttonHelp);
            bottom.Controls.Add(buttonApplyDefaults);
            bottom.Controls.Add(buttonRecount);
            bottom.Controls.Add(buttonSave);

            Controls.Add(_grid);
            Controls.Add(bottom);
        }

        private void LoadGrid()
        {
            _grid.Rows.Clear();

            foreach (WargameTemplateEntry e in _catalog.Entries)
            {
                _grid.Rows.Add(e.Template, e.Side, e.Type, e.Power, e.Attack, e.Defense, e.SupplyCost,
                    e.DefaultMultiplier, e.VehicleCount,
                    e.Priority, string.Join(", ", e.Attributes), e.FirepowerMin, e.FirepowerMax,
                    e.DetectedDynamic + " / " + e.DetectedStatic);
            }
        }

        // Recale les valeurs de combat de la ligne courante sur celles proposées pour
        // son type. Pratique après avoir changé le type d'un lot de templates.
        private void ApplyTypeDefaults()
        {
            if (_grid.CurrentRow == null)
                return;

            DataGridViewRow row = _grid.CurrentRow;
            string type = row.Cells["colType"].Value?.ToString() ?? WargameUnitType.Infantry;

            double power, attack, defense, supplyCost, firepowerMin, firepowerMax;
            int priority;
            List<string> attributes;
            WargameUnitType.GetDefaults(type, out power, out attack, out defense, out supplyCost,
                out priority, out attributes, out firepowerMin, out firepowerMax);

            row.Cells["colPower"].Value = power;
            row.Cells["colAttack"].Value = attack;
            row.Cells["colDefense"].Value = defense;
            row.Cells["colSupplyCost"].Value = supplyCost;
            row.Cells["colPriority"].Value = priority;
            row.Cells["colAttributes"].Value = string.Join(", ", attributes);
            row.Cells["colFirepowerMin"].Value = firepowerMin;
            row.Cells["colFirepowerMax"].Value = firepowerMax;

            SyncFromGrid();
        }

        // Relit tous les .stm et écrase les comptages, y compris les valeurs
        // corrigées à la main : c'est une action explicite du campaignMaker.
        private void RecountAll()
        {
            if (MessageBox.Show(
                    "Recount units from the .stm files? Manual corrections will be overwritten.",
                    "Wargame", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            {
                return;
            }

            SyncFromGrid();

            foreach (WargameTemplateEntry entry in _catalog.Entries)
                _catalog.RecountTemplate(entry, _campaignInfo, true);

            _dirty = true;
            LoadGrid();
        }

        private void Grid_CellEndEdit(object sender, DataGridViewCellEventArgs e)
        {
            _dirty = true;
            SyncFromGrid();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (_dirty)
            {
                DialogResult answer = MessageBox.Show(
                    "You have unsaved template changes. Save before closing?",
                    "Wargame",
                    MessageBoxButtons.YesNoCancel,
                    MessageBoxIcon.Warning);

                if (answer == DialogResult.Cancel)
                {
                    e.Cancel = true;
                    return;
                }

                if (answer == DialogResult.Yes)
                    SaveCatalog(false);
            }

            base.OnFormClosing(e);
        }

        private void Grid_DataError(object sender, DataGridViewDataErrorEventArgs e)
        {
            // Evite la boîte de dialogue par défaut du DataGridView
            e.ThrowException = false;
        }

        private void SyncFromGrid()
        {
            foreach (DataGridViewRow row in _grid.Rows)
            {
                if (row.IsNewRow) continue;

                string template = row.Cells["colTemplate"].Value?.ToString();
                WargameTemplateEntry entry = _catalog.Find(template);
                if (entry == null) continue;

                entry.Type = row.Cells["colType"].Value?.ToString() ?? entry.Type;
                entry.Power = ParseDouble(row.Cells["colPower"], entry.Power);
                entry.Attack = ParseDouble(row.Cells["colAttack"], entry.Attack);
                entry.Defense = ParseDouble(row.Cells["colDefense"], entry.Defense);
                entry.SupplyCost = ParseDouble(row.Cells["colSupplyCost"], entry.SupplyCost);

                int multiplier = (int)ParseDouble(row.Cells["colMultiplier"], entry.DefaultMultiplier);
                entry.DefaultMultiplier = multiplier > 0 ? multiplier : 1;

                entry.VehicleCount = (int)ParseDouble(row.Cells["colVehicles"], entry.VehicleCount);

                entry.Priority = (int)ParseDouble(row.Cells["colPriority"], entry.Priority);

                string attributesText = row.Cells["colAttributes"].Value?.ToString() ?? "";
                entry.Attributes = attributesText
                    .Split(',')
                    .Select(a => a.Trim())
                    .Where(a => a.Length > 0)
                    .ToList();
                if (entry.Attributes.Count == 0)
                    entry.Attributes.Add("Vehicles");

                entry.FirepowerMin = ParseDouble(row.Cells["colFirepowerMin"], entry.FirepowerMin);
                entry.FirepowerMax = ParseDouble(row.Cells["colFirepowerMax"], entry.FirepowerMax);
            }
        }

        private static double ParseDouble(DataGridViewCell cell, double fallback)
        {
            double value;
            if (double.TryParse(cell.Value?.ToString(), out value))
                return value;

            return fallback;
        }

        private void SaveCatalog(bool showConfirmation)
        {
            SyncFromGrid();
            _catalog.Save(_catalogPath);
            _dirty = false;

            if (showConfirmation)
                MessageBox.Show("Templates saved.", "Wargame", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }
}
