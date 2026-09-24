using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using DCE_Manager.Parameters;
using DCE_Manager.Utils;
using NLua;

namespace DCE_Manager
{
    // Fully schema-driven Form: every visible field, its type, bounds, grouping and
    // order come from the "@ui" tags parsed out of conf_mod.lua (see
    // ConfUiSchemaParser). Adding, removing or reordering a field in conf_mod.lua is
    // enough to change what this Form shows - nothing here needs to change.
    //
    // NOTE: matrix rendering is implemented here, but ConfUiSchemaParser,
    // ConfModLoader and ConfModWriter do not yet parse/read/write the "matrix" tag
    // or the "rows="/"cols=" attributes - that is the next piece of work. Until
    // then, matrix fields simply won't appear in _data.Schema (the parser doesn't
    // produce them), so this code has nothing to render yet - it is ready and
    // waiting for the other three files.
    public class ConfModForm : Form
    {
        private readonly string _campaignName;
        private readonly ConfModLoader _loader = new ConfModLoader();
        private readonly ConfModWriter _writer = new ConfModWriter();
        private ConfModDynamicData _data;
        private readonly List<UiFieldControl> _controls = new List<UiFieldControl>();
        private readonly List<UiMatrixControl> _matrixControls = new List<UiMatrixControl>();
        private readonly Dictionary<string, Panel> _groupPanels = new Dictionary<string, Panel>();
        private readonly Dictionary<string, Button> _groupButtons = new Dictionary<string, Button>();
        private string _activeGroup;

        private Button buttonSave;
        private Button buttonCancel;
        private readonly ToolTip _toolTip = new ToolTip();

        // Préréglages de timing proposés par le campaignMaker (camp.timing_presets
        // dans Init/camp_init.lua). Liste vide = pas de combo affiché.
        private readonly List<TimingPreset> _presets = new List<TimingPreset>();
        // Groupe (onglet) où le combo apparaît : celui du premier champ visé par
        // les préréglages (normalement "Time"). null = pas de combo.
        private string _presetGroup;
        private ComboBox _presetCombo;
        // Évite que le remplissage des champs par un préréglage relance le combo.
        private bool _applyingPreset;

        public ConfModForm(string campaignName)
            : this(campaignName, null, "Config", null)
        {
        }

        // filePath : chemin explicite du fichier .lua à éditer (null = conf_mod.lua de
        // campaignName, comportement historique). titlePrefix : "Config", "Campaign
        // Setup"... warningBanner : texte affiché en bandeau en haut de la Form (null
        // = pas de bandeau).
        public ConfModForm(string campaignName, string filePath, string titlePrefix, string warningBanner)
        {
            _campaignName = campaignName;

            Text = titlePrefix + " - " + campaignName;
            Width = 780;
            Height = 700;
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            Font = new Font("Segoe UI", 9);

            _data = filePath != null ? _loader.Load(campaignName, filePath) : _loader.Load(campaignName);

            if (_data == null || _data.Schema.Count == 0)
            {
                MessageBox.Show(
                    "No @ui field found in " + (filePath ?? "conf_mod.lua") + " for " + campaignName,
                    titlePrefix,
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);

                Load += (s, e) => Close();
                return;
            }

            // Préréglages seulement pour conf_mod.lua (filePath null) : la même Form
            // sert aussi à éditer camp_init.lua ("Campaign Setup"), où ils n'ont
            // rien à faire.
            if (filePath == null)
                LoadTimingPresets();

            BuildForm();

            if (!string.IsNullOrEmpty(warningBanner))
                AddWarningBanner(warningBanner);

            BindValues();
            SyncPresetCombo();
        }

        // Bandeau d'avertissement en haut de la Form (ex: "changes here require
        // restarting the campaign"). Ajouté après BuildForm() pour apparaître
        // au-dessus de la barre d'onglets, pas en dessous.
        private void AddWarningBanner(string text)
        {
            var banner = new Label
            {
                Text = text,
                Dock = DockStyle.Top,
                Height = 32,
                TextAlign = ContentAlignment.MiddleCenter,
                BackColor = Color.FromArgb(255, 244, 200),
                ForeColor = Color.FromArgb(140, 90, 0),
                Font = new Font("Segoe UI", 9, FontStyle.Bold)
            };

            Controls.Add(banner);
        }

        private void BuildForm()
        {
            var tabStrip = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = true,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Padding = new Padding(4)
            };

            var contentPanel = new Panel { Dock = DockStyle.Fill };

            var scalarLayoutByGroup = new Dictionary<string, TableLayoutPanel>();
            var rowByGroup = new Dictionary<string, int>();
            var matrixControlsByGroup = new Dictionary<string, List<Control>>();
            var groupHasRestricted = new Dictionary<string, bool>();
            var groupOrder = new List<string>();

            _presetGroup = FindPresetGroup();

            // Preserve the file's own order: a group's button appears where its first
            // field appears, and fields within a group keep the file's own order.
            // Fields above the current UserLevel are filtered out entirely - a
            // player never even gets a button for a campaignMaker-only group.
            foreach (ConfUiFieldSchema field in _data.Schema
                .Where(f => f.MinLevel <= ParamConf.UserLevel)
                .OrderBy(f => f.LineIndex))
            {
                Panel groupPanel;

                if (!_groupPanels.TryGetValue(field.Group, out groupPanel))
                {
                    groupPanel = new Panel { Dock = DockStyle.Fill, Visible = false };
                    _groupPanels[field.Group] = groupPanel;
                    contentPanel.Controls.Add(groupPanel);
                    groupOrder.Add(field.Group);
                    groupHasRestricted[field.Group] = false;
                }

                if (field.MinLevel > UserLevel.Player)
                    groupHasRestricted[field.Group] = true;

                if (field.Type == UiFieldType.Matrix)
                {
                    List<Control> matrixControls;

                    if (!matrixControlsByGroup.TryGetValue(field.Group, out matrixControls))
                    {
                        matrixControls = new List<Control>();
                        matrixControlsByGroup[field.Group] = matrixControls;
                    }

                    UiMatrixControl mc = CreateMatrixControl(field);
                    _matrixControls.Add(mc);
                    matrixControls.Add(mc.Container);
                    continue;
                }

                TableLayoutPanel groupLayout;

                if (!scalarLayoutByGroup.TryGetValue(field.Group, out groupLayout))
                {
                    groupLayout = NewLayout();
                    groupPanel.Controls.Add(groupLayout);
                    scalarLayoutByGroup[field.Group] = groupLayout;
                    rowByGroup[field.Group] = 0;

                    // Le combo des préréglages prend la première ligne du groupe.
                    if (field.Group == _presetGroup)
                    {
                        AddPresetRow(groupLayout, 0);
                        rowByGroup[field.Group] = 1;
                    }
                }

                int row = rowByGroup[field.Group];
                groupLayout.RowCount = row + 1;
                groupLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

                UiFieldControl fc = CreateFieldControl(groupLayout, row, field);
                _controls.Add(fc);

                rowByGroup[field.Group] = row + 1;
            }

            // Absorbs the leftover vertical space so the AutoSize content rows stay
            // packed at the top instead of the last one stretching.
            foreach (TableLayoutPanel groupLayout in scalarLayoutByGroup.Values)
            {
                groupLayout.RowCount += 1;
                groupLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            }

            // Stack the matrix control(s) of each group into its panel. A single
            // matrix fills the whole panel; several (e.g. main repair table +
            // runway) share it evenly, stacked vertically.
            foreach (KeyValuePair<string, List<Control>> kv in matrixControlsByGroup)
            {
                Panel groupPanel = _groupPanels[kv.Key];
                List<Control> matrixControls = kv.Value;

                if (matrixControls.Count == 1)
                {
                    matrixControls[0].Dock = DockStyle.Fill;
                    groupPanel.Controls.Add(matrixControls[0]);
                    continue;
                }

                var stack = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = matrixControls.Count };
                float percentPerRow = 100f / matrixControls.Count;

                for (int i = 0; i < matrixControls.Count; i++)
                {
                    stack.RowStyles.Add(new RowStyle(SizeType.Percent, percentPerRow));
                    matrixControls[i].Dock = DockStyle.Fill;
                    stack.Controls.Add(matrixControls[i], 0, i);
                }

                groupPanel.Controls.Add(stack);
            }

            // One button per group, added in stable file order - clicking one never
            // reorders the strip (unlike TabControl.Multiline).
            foreach (string group in groupOrder)
            {
                bool restricted = groupHasRestricted[group];

                var button = new Button
                {
                    Text = group,
                    AutoSize = true,
                    AutoSizeMode = AutoSizeMode.GrowAndShrink,
                    FlatStyle = FlatStyle.Flat,
                    Margin = new Padding(2),
                    Padding = new Padding(6, 3, 6, 3),
                    BackColor = restricted ? Color.FromArgb(255, 232, 200) : SystemColors.Control
                };

                button.Click += (s, e) => SelectGroup(group);

                _groupButtons[group] = button;
                tabStrip.Controls.Add(button);
            }

            var buttonPanel = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.RightToLeft,
                Dock = DockStyle.Bottom,
                Height = 45,
                Padding = new Padding(10)
            };

            buttonCancel = new Button { Text = "Cancel", Width = 90 };
            buttonCancel.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };

            buttonSave = new Button { Text = "Save", Width = 100 };
            buttonSave.Click += ButtonSave_Click;

            buttonPanel.Controls.Add(buttonCancel);
            buttonPanel.Controls.Add(buttonSave);

            Controls.Add(contentPanel);
            Controls.Add(tabStrip);
            Controls.Add(buttonPanel);

            if (groupOrder.Count > 0)
                SelectGroup(groupOrder[0]);
        }

        private void SelectGroup(string group)
        {
            if (_activeGroup == group)
                return;

            foreach (KeyValuePair<string, Panel> kv in _groupPanels)
                kv.Value.Visible = kv.Key == group;

            foreach (KeyValuePair<string, Button> kv in _groupButtons)
                kv.Value.Font = new Font(kv.Value.Font, kv.Key == group ? FontStyle.Bold : FontStyle.Regular);

            _activeGroup = group;
        }

        private void BindValues()
        {
            foreach (UiFieldControl fc in _controls)
            {
                object value;

                if (_data.Values.TryGetValue(fc.Schema.Path, out value))
                    fc.SetValue(value);
            }

            foreach (UiMatrixControl mc in _matrixControls)
            {
                object value;

                if (_data.Values.TryGetValue(mc.Schema.Path, out value))
                    mc.SetValue(value as Dictionary<string, double[]>);
            }
        }

        private void ButtonSave_Click(object sender, EventArgs e)
        {
            foreach (UiFieldControl fc in _controls)
                _data.Values[fc.Schema.Path] = fc.GetValue();

            foreach (UiMatrixControl mc in _matrixControls)
                _data.Values[mc.Schema.Path] = mc.GetValue();

            bool ok = _writer.Save(_data);

            if (!ok)
            {
                MessageBox.Show(
                    "Saving conf_mod.lua failed (see the log).",
                    "Config",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);

                return;
            }

            DialogResult = DialogResult.OK;
            Close();
        }

        // ---------------------------------------------------------------
        // Préréglages de timing (camp.timing_presets dans camp_init.lua)
        //
        //   timing_presets = {
        //       [1] = { label = "Mission 2h, one sortie per mission", mission_duration = 7200, idle_time_min = 3600, idle_time_max = 3600 },
        //       ...
        //   },
        //
        // "label" = texte du combo ; toute autre clé = nom d'un champ de conf_mod
        // (Key du schéma). Si plusieurs champs ont le même nom, le premier du
        // fichier gagne (donc mission_ini_check, qui est en tête). Une clé qui ne
        // correspond à aucun champ affiché est ignorée.
        // Le préréglage n'est stocké nulle part : il ne fait que remplir les
        // champs, et c'est Save qui écrit conf_mod.lua comme d'habitude.
        // ---------------------------------------------------------------

        private void LoadTimingPresets()
        {
            string campInitPath = DcemLua.CampaignInitFile(_campaignName, "camp_init.lua");

            if (!File.Exists(campInitPath))
                return;

            try
            {
                using (Lua lua = DcemLua.NewState())
                {
                    lua.DoString(@"
                        os = nil
                        io = nil
                        file = nil
                        debug = nil
                    ");

                    lua.DoFile(campInitPath);

                    LuaTable camp = lua["camp"] as LuaTable;
                    if (camp == null)
                        return;

                    LuaTable presetsTable = camp["timing_presets"] as LuaTable;
                    if (presetsTable == null)
                        return;

                    // On trie sur l'index Lua ([1], [2]...) pour garder l'ordre voulu
                    // par le campaignMaker, l'ordre d'énumération n'étant pas garanti.
                    var sorted = new List<KeyValuePair<double, TimingPreset>>();

                    foreach (object key in presetsTable.Keys)
                    {
                        LuaTable presetTable = presetsTable[key] as LuaTable;
                        if (presetTable == null)
                            continue;

                        TimingPreset preset = ReadPreset(presetTable);
                        if (preset.Values.Count == 0)
                            continue;

                        double order;
                        if (!double.TryParse(Convert.ToString(key, CultureInfo.InvariantCulture), NumberStyles.Float, CultureInfo.InvariantCulture, out order))
                            order = 9999;

                        sorted.Add(new KeyValuePair<double, TimingPreset>(order, preset));
                    }

                    foreach (KeyValuePair<double, TimingPreset> kv in sorted.OrderBy(x => x.Key))
                        _presets.Add(kv.Value);
                }
            }
            catch (Exception ex)
            {
                FormUtils.LogRegister("ConfModForm | lecture de camp.timing_presets impossible dans " + campInitPath + " : " + ex.Message);
                _presets.Clear();
            }
        }

        private static TimingPreset ReadPreset(LuaTable presetTable)
        {
            var preset = new TimingPreset();

            foreach (object key in presetTable.Keys)
            {
                string k = Convert.ToString(key, CultureInfo.InvariantCulture);
                object v = presetTable[key];

                if (k == "label")
                {
                    preset.Label = Convert.ToString(v, CultureInfo.InvariantCulture);
                    continue;
                }

                if (v is double || v is long || v is int)
                    preset.Values[k] = Convert.ToDouble(v, CultureInfo.InvariantCulture);
            }

            if (string.IsNullOrEmpty(preset.Label))
                preset.Label = "Preset";

            return preset;
        }

        // Premier champ visible (niveau utilisateur respecté) visé par un
        // préréglage -> son groupe accueille le combo.
        private string FindPresetGroup()
        {
            if (_presets.Count == 0)
                return null;

            List<ConfUiFieldSchema> visible = _data.Schema
                .Where(f => f.MinLevel <= ParamConf.UserLevel && f.Type != UiFieldType.Matrix)
                .OrderBy(f => f.LineIndex)
                .ToList();

            foreach (TimingPreset preset in _presets)
            {
                foreach (string key in preset.Values.Keys)
                {
                    ConfUiFieldSchema field = visible.FirstOrDefault(f => f.Key == key);
                    if (field != null)
                        return field.Group;
                }
            }

            FormUtils.LogRegister("ConfModForm | camp.timing_presets : aucune clé ne correspond à un champ de conf_mod pour " + _campaignName);
            return null;
        }

        private void AddPresetRow(TableLayoutPanel layout, int row)
        {
            layout.RowCount = row + 1;
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            var nameLabel = new Label
            {
                Text = "Timing preset",
                AutoSize = true,
                Anchor = AnchorStyles.Top | AnchorStyles.Left,
                Margin = new Padding(0, 8, 0, 0),
                Font = new Font("Segoe UI", 9, FontStyle.Bold)
            };
            layout.Controls.Add(nameLabel, 0, row);

            _toolTip.SetToolTip(nameLabel,
                "Timing presets suggested by the campaign maker for this campaign. " +
                "Picking one fills the fields below; you can still adjust them before saving.");

            _presetCombo = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Width = 320,
                Anchor = AnchorStyles.Left,
                Margin = new Padding(0, 5, 0, 8)
            };

            _presetCombo.Items.Add("Custom");
            foreach (TimingPreset preset in _presets)
                _presetCombo.Items.Add(preset);

            _presetCombo.SelectedIndexChanged += PresetCombo_SelectedIndexChanged;

            layout.Controls.Add(_presetCombo, 1, row);
            layout.SetColumnSpan(_presetCombo, 2);
        }

        private void PresetCombo_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (_applyingPreset)
                return;

            TimingPreset preset = _presetCombo.SelectedItem as TimingPreset;
            if (preset == null)
                return; // "Custom" : on ne touche à rien

            _applyingPreset = true;

            foreach (KeyValuePair<string, double> kv in preset.Values)
            {
                UiFieldControl fc = _controls.FirstOrDefault(c => c.Schema.Key == kv.Key);
                if (fc != null)
                    fc.SetValue(kv.Value);
            }

            _applyingPreset = false;
        }

        // À l'ouverture : sélectionne le préréglage qui correspond aux valeurs
        // actuelles de conf_mod, sinon "Custom".
        private void SyncPresetCombo()
        {
            if (_presetCombo == null)
                return;

            _applyingPreset = true;
            _presetCombo.SelectedIndex = 0;

            foreach (TimingPreset preset in _presets)
            {
                if (PresetMatchesCurrentValues(preset))
                {
                    _presetCombo.SelectedItem = preset;
                    break;
                }
            }

            _applyingPreset = false;
        }

        private bool PresetMatchesCurrentValues(TimingPreset preset)
        {
            bool atLeastOne = false;

            foreach (KeyValuePair<string, double> kv in preset.Values)
            {
                UiFieldControl fc = _controls.FirstOrDefault(c => c.Schema.Key == kv.Key);
                if (fc == null)
                    continue;

                double current;
                try
                {
                    current = Convert.ToDouble(fc.GetValue(), CultureInfo.InvariantCulture);
                }
                catch
                {
                    return false;
                }

                if (Math.Abs(current - kv.Value) > 0.001)
                    return false;

                atLeastOne = true;
            }

            return atLeastOne;
        }

        // ---------------------------------------------------------------
        // Generic control factory (scalar fields): one entry point that turns a
        // field's schema (type + bounds + options) into the right WinForms
        // control, plus a uniform get/set pair so the rest of the Form never
        // needs to know which control kind backs a given field.
        // ---------------------------------------------------------------

        private UiFieldControl CreateFieldControl(TableLayoutPanel layout, int row, ConfUiFieldSchema schema)
        {
            var fc = new UiFieldControl { Schema = schema };

            var nameLabel = new Label
            {
                Text = schema.Label,
                AutoSize = true,
                Anchor = AnchorStyles.Top | AnchorStyles.Left,
                Margin = new Padding(0, 8, 0, 0)
            };
            layout.Controls.Add(nameLabel, 0, row);

            if (!string.IsNullOrEmpty(schema.Help))
                _toolTip.SetToolTip(nameLabel, schema.Help);

            switch (schema.Type)
            {
                case UiFieldType.Checkbox:
                    BuildCheckbox(layout, row, fc);
                    break;
                case UiFieldType.Slider:
                    BuildSlider(layout, row, schema, fc);
                    break;
                case UiFieldType.Numeric:
                    BuildNumeric(layout, row, schema, fc);
                    break;
                case UiFieldType.Combo:
                    BuildCombo(layout, row, schema, fc);
                    break;
                case UiFieldType.Text:
                    BuildText(layout, row, fc);
                    break;
                case UiFieldType.List:
                    BuildList(layout, row, fc);
                    break;
            }

            return fc;
        }

        private static void BuildCheckbox(TableLayoutPanel layout, int row, UiFieldControl fc)
        {
            var check = new CheckBox { AutoSize = true, Margin = new Padding(0, 5, 0, 0) };
            layout.Controls.Add(check, 1, row);

            fc.GetValue = () => check.Checked;
            fc.SetValue = v => check.Checked = Convert.ToBoolean(v);
        }

        private static void BuildSlider(TableLayoutPanel layout, int row, ConfUiFieldSchema schema, UiFieldControl fc)
        {
            int min = (int)(schema.Min ?? 0);
            int max = (int)(schema.Max ?? 100);
            int scale = schema.Step < 1 ? (int)Math.Round(1 / schema.Step) : 1;
            string format = scale > 1 ? "0.0" : "0";

            var track = new TrackBar
            {
                Minimum = min * scale,
                Maximum = max * scale,
                TickStyle = TickStyle.None,
                Dock = DockStyle.Fill
            };

            var valueLabel = new Label { AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(5, 8, 0, 0) };

            Action updateLabel = () => valueLabel.Text = (track.Value / (double)scale).ToString(format, CultureInfo.InvariantCulture);
            track.Scroll += (s, e) => updateLabel();

            layout.Controls.Add(track, 1, row);
            layout.Controls.Add(valueLabel, 2, row);

            fc.GetValue = () => track.Value / (double)scale;
            fc.SetValue = v =>
            {
                double d = Convert.ToDouble(v, CultureInfo.InvariantCulture);
                track.Value = Clamp((int)Math.Round(d * scale), track.Minimum, track.Maximum);
                updateLabel();
            };
        }

        private static void BuildNumeric(TableLayoutPanel layout, int row, ConfUiFieldSchema schema, UiFieldControl fc)
        {
            var num = new NumericUpDown
            {
                Minimum = (decimal)(schema.Min ?? 0),
                Maximum = (decimal)(schema.Max ?? 100),
                DecimalPlaces = schema.Step < 1 ? 1 : 0,
                Increment = (decimal)schema.Step,
                Width = 90,
                Anchor = AnchorStyles.Left,
                Margin = new Padding(0, 5, 0, 0)
            };
            layout.Controls.Add(num, 1, row);

            fc.GetValue = () => (double)num.Value;
            fc.SetValue = v =>
            {
                decimal d = (decimal)Convert.ToDouble(v, CultureInfo.InvariantCulture);
                num.Value = ClampDecimal(num, d);
            };
        }

        private static void BuildCombo(TableLayoutPanel layout, int row, ConfUiFieldSchema schema, UiFieldControl fc)
        {
            var combo = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Width = 220,
                Anchor = AnchorStyles.Left,
                Margin = new Padding(0, 5, 0, 0)
            };
            combo.Items.AddRange(schema.Options.ToArray());
            layout.Controls.Add(combo, 1, row);

            fc.GetValue = () =>
            {
                UiOption selected = combo.SelectedItem as UiOption;
                return selected != null ? selected.Value : "";
            };
            fc.SetValue = v =>
            {
                string token = v != null ? v.ToString() : "";
                UiOption match = schema.Options.FirstOrDefault(o => o.Value == token);
                combo.SelectedItem = match ?? schema.Options.FirstOrDefault();
            };
        }

        private static void BuildText(TableLayoutPanel layout, int row, UiFieldControl fc)
        {
            var text = new TextBox { Width = 220, Anchor = AnchorStyles.Left, Margin = new Padding(0, 5, 0, 0) };
            layout.Controls.Add(text, 1, row);

            fc.GetValue = () => text.Text;
            fc.SetValue = v => text.Text = v != null ? v.ToString() : "";
        }

        // Une entrée par ligne (ex: pictureBrief.blue - un nom de fichier par ligne).
        private static void BuildList(TableLayoutPanel layout, int row, UiFieldControl fc)
        {
            var text = new TextBox
            {
                Width = 220,
                Height = 60,
                Multiline = true,
                ScrollBars = ScrollBars.Vertical,
                Anchor = AnchorStyles.Left | AnchorStyles.Top,
                Margin = new Padding(0, 5, 0, 0)
            };
            layout.Controls.Add(text, 1, row);

            fc.GetValue = () => text.Text
                .Replace("\r\n", "\n")
                .Split('\n')
                .Select(s => s.Trim())
                .Where(s => s.Length > 0)
                .ToList();

            fc.SetValue = v =>
            {
                List<string> list = v as List<string>;
                text.Text = list != null ? string.Join(Environment.NewLine, list) : "";
            };
        }

        // ---------------------------------------------------------------
        // Matrix rendering: rows = named Lua keys (e.g. airUnit, airbase...),
        // columns = 1-based positions inside each row's positional Lua array
        // (e.g. col "2" = deathPoint). Only positions declared in schema.ColSpecs
        // are editable; any other position in the underlying array is preserved
        // untouched on save (see the closures below).
        // ---------------------------------------------------------------

        private UiMatrixControl CreateMatrixControl(ConfUiFieldSchema schema)
        {
            var container = new Panel { Dock = DockStyle.Fill };

            var title = new Label
            {
                Text = schema.Label,
                Font = new Font("Segoe UI", 10, FontStyle.Bold),
                Dock = DockStyle.Top,
                Height = 26,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(4, 0, 0, 0)
            };

            var grid = new DataGridView
            {
                Dock = DockStyle.Fill,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                AllowUserToResizeRows = false,
                RowHeadersVisible = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                SelectionMode = DataGridViewSelectionMode.CellSelect
            };

            grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "__row",
                HeaderText = "",
                ReadOnly = true,
                FillWeight = 70
            });

            foreach (UiOption col in schema.ColSpecs)
            {
                grid.Columns.Add(new DataGridViewTextBoxColumn
                {
                    Name = "col_" + col.Value,
                    HeaderText = col.Label,
                    FillWeight = 100
                });
            }

            foreach (UiOption rowSpec in schema.RowSpecs)
            {
                int idx = grid.Rows.Add();
                grid.Rows[idx].Cells["__row"].Value = rowSpec.Label;
                grid.Rows[idx].Tag = rowSpec.Value;
            }

            container.Controls.Add(grid);
            container.Controls.Add(title);

            if (!string.IsNullOrEmpty(schema.Help))
                _toolTip.SetToolTip(title, schema.Help);

            var mc = new UiMatrixControl { Schema = schema, Container = container };

            // Full per-row arrays as loaded (including columns not exposed in the
            // grid), so anything not shown here still round-trips unchanged.
            var baseline = new Dictionary<string, double[]>();

            mc.SetValue = data =>
            {
                baseline = data ?? new Dictionary<string, double[]>();

                foreach (DataGridViewRow gridRow in grid.Rows)
                {
                    string rowKey = (string)gridRow.Tag;
                    double[] values;

                    if (!baseline.TryGetValue(rowKey, out values))
                        continue;

                    foreach (UiOption col in schema.ColSpecs)
                    {
                        int colIndex = ParseColumnIndex(col.Value);

                        if (colIndex >= 0 && colIndex < values.Length)
                            gridRow.Cells["col_" + col.Value].Value = values[colIndex].ToString(CultureInfo.InvariantCulture);
                    }
                }
            };

            mc.GetValue = () =>
            {
                var result = new Dictionary<string, double[]>();

                foreach (DataGridViewRow gridRow in grid.Rows)
                {
                    string rowKey = (string)gridRow.Tag;
                    double[] values;

                    if (!baseline.TryGetValue(rowKey, out values))
                        continue;

                    double[] updated = (double[])values.Clone();

                    foreach (UiOption col in schema.ColSpecs)
                    {
                        int colIndex = ParseColumnIndex(col.Value);

                        if (colIndex < 0 || colIndex >= updated.Length)
                            continue;

                        object cellValue = gridRow.Cells["col_" + col.Value].Value;
                        double d;

                        if (cellValue != null && double.TryParse(cellValue.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out d))
                            updated[colIndex] = d;
                    }

                    result[rowKey] = updated;
                }

                return result;
            };

            return mc;
        }

        // schema.ColSpecs values are 1-based Lua positions (e.g. "2" for the 2nd
        // element of the row array); converts to a 0-based C# array index.
        private static int ParseColumnIndex(string oneBasedToken)
        {
            int oneBased;
            return int.TryParse(oneBasedToken, NumberStyles.Integer, CultureInfo.InvariantCulture, out oneBased)
                ? oneBased - 1
                : -1;
        }

        private static TableLayoutPanel NewLayout()
        {
            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(15),
                ColumnCount = 3,
                RowCount = 1,
                AutoSize = true
            };

            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 200));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 70));

            return layout;
        }

        private static int Clamp(int value, int min, int max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }

        private static decimal ClampDecimal(NumericUpDown num, decimal value)
        {
            if (value < num.Minimum) return num.Minimum;
            if (value > num.Maximum) return num.Maximum;
            return value;
        }

        private class UiFieldControl
        {
            public ConfUiFieldSchema Schema;
            public Func<object> GetValue;
            public Action<object> SetValue;
        }

        private class TimingPreset
        {
            public string Label;
            public Dictionary<string, double> Values = new Dictionary<string, double>();

            public override string ToString()
            {
                return Label;
            }
        }

        private class UiMatrixControl
        {
            public ConfUiFieldSchema Schema;
            public Control Container;
            public Func<Dictionary<string, double[]>> GetValue;
            public Action<Dictionary<string, double[]>> SetValue;
        }
    }
}
