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
        // dans Init/camp_init.lua). Liste vide = pas de boutons affichés.
        private readonly List<TimingPreset> _presets = new List<TimingPreset>();
        // Groupe (onglet) où les boutons apparaissent : celui du premier champ visé
        // par les préréglages (normalement "Time"). null = pas de boutons.
        private string _presetGroup;
        // Un bouton par préréglage, Tag = le TimingPreset correspondant.
        private readonly List<Button> _presetButtons = new List<Button>();

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
            SyncPresetButtons();
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

                // Tableau des préréglages (camp_init, onglet "Timing presets") : il
                // occupe tout son panneau comme une matrice, mais sa valeur passe par
                // _controls comme un champ normal (BindValues / Save inchangés).
                if (field.Type == UiFieldType.Presets)
                {
                    List<Control> presetsControls;

                    if (!matrixControlsByGroup.TryGetValue(field.Group, out presetsControls))
                    {
                        presetsControls = new List<Control>();
                        matrixControlsByGroup[field.Group] = presetsControls;
                    }

                    var presetsFc = new UiFieldControl { Schema = field };
                    presetsControls.Add(CreatePresetsControl(field, presetsFc));
                    _controls.Add(presetsFc);
                    continue;
                }

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
                }

                int row = rowByGroup[field.Group];
                groupLayout.RowCount = row + 1;
                groupLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

                UiFieldControl fc = CreateFieldControl(groupLayout, row, field);
                _controls.Add(fc);

                rowByGroup[field.Group] = row + 1;
            }

            // Boutons des préréglages : sous les champs de leur groupe.
            TableLayoutPanel presetLayout;
            if (_presetGroup != null && scalarLayoutByGroup.TryGetValue(_presetGroup, out presetLayout))
                AddPresetRows(presetLayout, rowByGroup[_presetGroup]);

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
        // "label" = texte affiché à côté du bouton ; toute autre clé = nom d'un champ de conf_mod
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

        // Une ligne par préréglage sous les champs : libellé à gauche, bouton
        // "Apply" à droite. La première ligne a une marge au-dessus pour la
        // séparer des champs.
        private void AddPresetRows(TableLayoutPanel layout, int firstRow)
        {
            int row = firstRow;

            foreach (TimingPreset preset in _presets)
            {
                layout.RowCount = row + 1;
                layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

                int topMargin = row == firstRow ? 30 : 6;

                var presetLabel = new Label
                {
                    Text = preset.Label,
                    AutoSize = true,
                    MaximumSize = new Size(190, 0), // retour à la ligne si le libellé est long
                    Anchor = AnchorStyles.Left,
                    Margin = new Padding(0, topMargin, 0, 0)
                };
                layout.Controls.Add(presetLabel, 0, row);

                var button = new Button
                {
                    Text = "Apply",
                    Width = 110,
                    Height = 28,
                    Anchor = AnchorStyles.Left,
                    Margin = new Padding(0, topMargin, 0, 0),
                    Tag = preset
                };
                button.Click += PresetButton_Click;
                layout.Controls.Add(button, 1, row);

                string tip = "Preset suggested by the campaign maker: " + preset.Label + ". " +
                             "Fills the timing fields above; you can still adjust them before saving.";
                _toolTip.SetToolTip(presetLabel, tip);
                _toolTip.SetToolTip(button, tip);

                _presetButtons.Add(button);
                row++;
            }

            // Si le joueur retouche un champ numérique de ce groupe à la main, le
            // bouton "Active" suit tout de suite (plus besoin de rouvrir la form).
            // Les champs HH:MM s'en chargent eux-mêmes (voir BuildTime).
            foreach (Control c in layout.Controls)
            {
                NumericUpDown num = c as NumericUpDown;
                if (num != null)
                    num.ValueChanged += (s, e) => SyncPresetButtons();
            }
        }

        private void PresetButton_Click(object sender, EventArgs e)
        {
            TimingPreset preset = ((Button)sender).Tag as TimingPreset;
            if (preset == null)
                return;

            foreach (KeyValuePair<string, double> kv in preset.Values)
            {
                UiFieldControl fc = _controls.FirstOrDefault(c => c.Schema.Key == kv.Key);
                if (fc != null)
                    fc.SetValue(kv.Value);
            }

            SyncPresetButtons();
        }

        // Le bouton du préréglage qui correspond aux valeurs actuelles passe en
        // vert avec "Active" ; les autres restent en "Apply".
        private void SyncPresetButtons()
        {
            foreach (Button button in _presetButtons)
            {
                TimingPreset preset = (TimingPreset)button.Tag;

                if (PresetMatchesCurrentValues(preset))
                {
                    button.Text = "✓ Active";
                    button.BackColor = Color.FromArgb(200, 235, 200);
                    button.Font = new Font(Font, FontStyle.Bold);
                }
                else
                {
                    button.Text = "Apply";
                    button.BackColor = SystemColors.Control;
                    button.UseVisualStyleBackColor = true;
                    button.Font = Font;
                }
            }
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
                    // format=hhmm : valeur en secondes dans le .lua, affichée en HH:MM
                    if (schema.Format == "hhmm")
                        BuildTime(layout, row, schema, fc);
                    else
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

        // Champ en secondes (tag "format=hhmm") affiché en HH:MM dans une seule case
        // avec ses flèches, comme les autres champs numériques (voir HhMmUpDown en
        // bas du fichier). La valeur écrite dans le .lua reste en secondes, seul
        // l'affichage change.
        // Tant que le joueur ne touche pas au champ, la valeur d'origine est rendue
        // telle quelle, même si elle n'est pas un multiple de 60.
        private void BuildTime(TableLayoutPanel layout, int row, ConfUiFieldSchema schema, UiFieldControl fc)
        {
            double min = schema.Min ?? 0;
            double max = schema.Max ?? 86400;
            double current = min; // valeur en secondes
            bool busy = false;    // évite que SetValue soit pris pour une modif du joueur

            var box = new HhMmUpDown
            {
                Minimum = (decimal)Math.Ceiling(min / 60),
                Maximum = (decimal)Math.Floor(max / 60),
                Width = 90,
                Anchor = AnchorStyles.Left,
                Margin = new Padding(0, 5, 0, 0)
            };
            layout.Controls.Add(box, 1, row);

            _toolTip.SetToolTip(box, "Hours:minutes. Click the hours or the minutes, then use the arrows or the mouse wheel.");

            box.ValueChanged += (s, e) =>
            {
                if (busy)
                    return;
                current = (double)box.Value * 60;
                SyncPresetButtons();
            };

            fc.GetValue = () =>
            {
                // Lire Value valide une saisie clavier pas encore prise en compte
                // (ex: Entrée sur Save sans quitter la case) -> ValueChanged -> current
                decimal force = box.Value;
                return current;
            };

            fc.SetValue = v =>
            {
                busy = true;
                current = Math.Max(min, Math.Min(max, Convert.ToDouble(v, CultureInfo.InvariantCulture)));
                decimal minutes = (decimal)Math.Round(current / 60);
                box.Value = Math.Max(box.Minimum, Math.Min(box.Maximum, minutes));
                busy = false;
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

        // ---------------------------------------------------------------
        // Tableau éditable des préréglages (tag "@ui presets" sur la ligne
        // "timing_presets = {" de camp_init.lua), pour le campaignMaker.
        //
        //   timing_presets = { -- @ui presets format=hhmm cols="mission_duration:Mission,..." ...
        //
        // - 1re colonne : le libellé du bouton côté joueur (modifiable)
        // - une colonne par entrée de cols= (clé du champ conf_mod : titre)
        // - format=hhmm : les cellules sont saisies/affichées en HH:MM, stockées en
        //   secondes ; sinon ce sont des nombres simples
        // - cellule vide = ce préréglage ne touche pas à ce champ
        // - une clé présente dans le fichier mais absente de cols= n'est pas
        //   affichée, mais elle est conservée (Tag de la ligne)
        // ---------------------------------------------------------------

        private Control CreatePresetsControl(ConfUiFieldSchema schema, UiFieldControl fc)
        {
            bool hhmm = schema.Format == "hhmm";

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

            var helpLabel = new Label
            {
                Text = (string.IsNullOrEmpty(schema.Help) ? "" : schema.Help + "\r\n") +
                       (hhmm ? "Times are entered as HH:MM (e.g. 2:30, 4h, 3). " : "") +
                       "Leave a cell empty to leave that setting unchanged.",
                ForeColor = Color.DimGray,
                Dock = DockStyle.Top,
                Height = 44,
                Padding = new Padding(4, 0, 0, 4)
            };

            var grid = new DataGridView
            {
                Dock = DockStyle.Fill,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                AllowUserToResizeRows = false,
                RowHeadersVisible = false,
                MultiSelect = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                SelectionMode = DataGridViewSelectionMode.CellSelect,
                BackgroundColor = SystemColors.Window
            };

            grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "__label",
                HeaderText = "Label (button text)",
                FillWeight = 180
            });

            foreach (UiOption col in schema.ColSpecs)
            {
                var column = new DataGridViewTextBoxColumn
                {
                    Name = "col_" + col.Value,
                    HeaderText = col.Label,
                    FillWeight = 60
                };
                column.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
                grid.Columns.Add(column);
            }

            // Saisie d'une cellule de valeur : refusée si illisible (le joueur peut
            // corriger ou faire Échap), sinon remise au propre en quittant la cellule.
            grid.CellValidating += (s, e) =>
            {
                if (e.ColumnIndex == 0)
                    return;

                string text = Convert.ToString(e.FormattedValue).Trim();
                if (text == "" || PresetCellToNumber(text, hhmm) != null)
                {
                    grid.Rows[e.RowIndex].ErrorText = "";
                    return;
                }

                grid.Rows[e.RowIndex].ErrorText = hhmm ? "Use HH:MM, e.g. 2:30" : "Use a number";
                System.Media.SystemSounds.Beep.Play();
                e.Cancel = true;
            };

            grid.CellEndEdit += (s, e) =>
            {
                grid.Rows[e.RowIndex].ErrorText = "";

                if (e.ColumnIndex == 0)
                    return;

                DataGridViewCell cell = grid.Rows[e.RowIndex].Cells[e.ColumnIndex];
                double? v = PresetCellToNumber(Convert.ToString(cell.Value), hhmm);
                cell.Value = v == null ? "" : NumberToPresetCell(v.Value, hhmm);
            };

            // Boutons sous le tableau
            var buttons = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                Height = 38,
                Padding = new Padding(0, 6, 0, 0)
            };

            var addButton = new Button { Text = "+ Add preset", AutoSize = true };
            var removeButton = new Button { Text = "Remove", AutoSize = true };
            var upButton = new Button { Text = "▲ Up", AutoSize = true };
            var downButton = new Button { Text = "▼ Down", AutoSize = true };

            buttons.Controls.Add(addButton);
            buttons.Controls.Add(removeButton);
            buttons.Controls.Add(upButton);
            buttons.Controls.Add(downButton);

            _toolTip.SetToolTip(upButton, "Move the selected preset up (order of the buttons in the player's Config window).");
            _toolTip.SetToolTip(downButton, "Move the selected preset down.");

            addButton.Click += (s, e) =>
            {
                if (!grid.EndEdit())
                    return;

                int index = grid.Rows.Add();
                grid.Rows[index].Cells[0].Value = "New preset";
                grid.CurrentCell = grid.Rows[index].Cells[0];
                grid.BeginEdit(true);
            };

            removeButton.Click += (s, e) =>
            {
                if (grid.CurrentRow == null)
                    return;

                grid.CancelEdit();
                grid.Rows.Remove(grid.CurrentRow);
            };

            upButton.Click += (s, e) => MovePresetRow(grid, -1);
            downButton.Click += (s, e) => MovePresetRow(grid, +1);

            // Ordre d'ajout important avec Dock : Fill d'abord, puis Top/Bottom
            container.Controls.Add(grid);
            container.Controls.Add(buttons);
            container.Controls.Add(helpLabel);
            container.Controls.Add(title);

            fc.SetValue = v =>
            {
                grid.Rows.Clear();

                List<UiPresetRow> rows = v as List<UiPresetRow>;
                if (rows == null)
                    return;

                foreach (UiPresetRow presetRow in rows)
                {
                    int index = grid.Rows.Add();
                    DataGridViewRow gridRow = grid.Rows[index];
                    gridRow.Tag = presetRow; // garde les clés hors colonnes
                    gridRow.Cells[0].Value = presetRow.Label;

                    for (int c = 0; c < schema.ColSpecs.Count; c++)
                    {
                        double value;
                        if (presetRow.Values.TryGetValue(schema.ColSpecs[c].Value, out value))
                            gridRow.Cells[c + 1].Value = NumberToPresetCell(value, hhmm);
                    }
                }
            };

            fc.GetValue = () =>
            {
                grid.EndEdit();

                var result = new List<UiPresetRow>();

                foreach (DataGridViewRow gridRow in grid.Rows)
                {
                    var presetRow = new UiPresetRow();
                    presetRow.Label = Convert.ToString(gridRow.Cells[0].Value).Trim();

                    // Repart des valeurs d'origine pour ne pas perdre les clés qui
                    // n'ont pas de colonne, puis applique celles du tableau.
                    UiPresetRow original = gridRow.Tag as UiPresetRow;
                    if (original != null)
                    {
                        foreach (KeyValuePair<string, double> kv in original.Values)
                            presetRow.Values[kv.Key] = kv.Value;
                    }

                    for (int c = 0; c < schema.ColSpecs.Count; c++)
                    {
                        string key = schema.ColSpecs[c].Value;
                        double? v = PresetCellToNumber(Convert.ToString(gridRow.Cells[c + 1].Value), hhmm);

                        if (v == null)
                            presetRow.Values.Remove(key);
                        else
                            presetRow.Values[key] = v.Value;
                    }

                    // Ligne complètement vide : ignorée
                    if (presetRow.Label == "" && presetRow.Values.Count == 0)
                        continue;

                    if (presetRow.Label == "")
                        presetRow.Label = "Preset " + (result.Count + 1);

                    result.Add(presetRow);
                }

                return result;
            };

            return container;
        }

        private static void MovePresetRow(DataGridView grid, int direction)
        {
            if (grid.CurrentRow == null || !grid.EndEdit())
                return;

            int from = grid.CurrentRow.Index;
            int to = from + direction;
            if (to < 0 || to >= grid.Rows.Count)
                return;

            int column = grid.CurrentCell.ColumnIndex;
            DataGridViewRow row = grid.Rows[from];
            grid.Rows.RemoveAt(from);
            grid.Rows.Insert(to, row);
            grid.CurrentCell = grid.Rows[to].Cells[column];
        }

        // Texte d'une cellule -> nombre (secondes si HH:MM). null si vide ou illisible.
        private static double? PresetCellToNumber(string text, bool hhmm)
        {
            if (string.IsNullOrWhiteSpace(text))
                return null;

            if (hhmm)
            {
                int? minutes = HhMmUpDown.ParseMinutes(text);
                return minutes == null ? (double?)null : minutes.Value * 60.0;
            }

            double d;
            if (double.TryParse(text.Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out d))
                return d;
            return null;
        }

        private static string NumberToPresetCell(double value, bool hhmm)
        {
            if (!hhmm)
                return value.ToString(CultureInfo.InvariantCulture);

            int totalMinutes = (int)Math.Round(value / 60.0);
            return (totalMinutes / 60).ToString("00") + ":" + (totalMinutes % 60).ToString("00");
        }

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

        // Case "HH:MM" avec flèches, pour les champs format=hhmm.
        // Value = nombre total de minutes ; l'affichage est converti en HH:MM.
        // Comme un DateTimePicker : on clique sur les heures ou sur les minutes, et
        // les flèches (boutons, clavier ou molette) changent la partie sélectionnée.
        // Les minutes débordent toutes seules sur les heures (00:59 +1 = 01:00).
        // Tab passe des heures aux minutes, puis au champ suivant (Maj+Tab : l'inverse).
        // Gauche/droite passent aussi d'une partie à l'autre.
        // Contrairement au DateTimePicker, les heures peuvent dépasser 24.
        // Saisie directe acceptée : "4:55", "04:55", "4h55" ou "4" (= 4 heures).
        private class HhMmUpDown : NumericUpDown
        {
            private TextBox _edit;        // la zone de texte interne du NumericUpDown
            private bool _onHours = true; // partie sélectionnée : heures ou minutes

            public HhMmUpDown()
            {
                TextAlign = HorizontalAlignment.Center;
                _edit = Controls.OfType<TextBox>().FirstOrDefault();

                if (_edit != null)
                {
                    // Gauche/droite passent d'une partie à l'autre
                    _edit.KeyDown += (s, e) =>
                    {
                        if (e.KeyCode == Keys.Left || e.KeyCode == Keys.Right)
                        {
                            _onHours = e.KeyCode == Keys.Left;
                            HighlightPart();
                            e.Handled = true;
                            e.SuppressKeyPress = true;
                        }
                    };
                }
            }

            // Un clic choisit la partie (heures ou minutes) et la surligne.
            // Piège WinForms : la zone de texte interne du NumericUpDown ne déclenche
            // PAS son propre événement MouseUp, elle le fait remonter au
            // NumericUpDown (coordonnées converties). D'où cet override ici plutôt
            // qu'un _edit.MouseUp += ... qui n'était jamais appelé.
            protected override void OnMouseUp(MouseEventArgs e)
            {
                base.OnMouseUp(e);

                // Les flèches remontent aussi ici : on ne réagit qu'au clic dans le texte
                if (_edit == null || !_edit.Bounds.Contains(e.Location))
                    return;

                int colon = _edit.Text.IndexOf(':');
                _onHours = colon < 0 || _edit.SelectionStart <= colon;
                HighlightPart();
            }

            // Tab : des heures on passe aux minutes, des minutes on sort de la case.
            // Maj+Tab : l'inverse.
            protected override bool ProcessDialogKey(Keys keyData)
            {
                if (_edit != null && _edit.Focused)
                {
                    if (keyData == Keys.Tab && _onHours)
                    {
                        if (UserEdit)
                            ValidateEditText();
                        _onHours = false;
                        HighlightPart();
                        return true;
                    }

                    if (keyData == (Keys.Tab | Keys.Shift) && !_onHours)
                    {
                        if (UserEdit)
                            ValidateEditText();
                        _onHours = true;
                        HighlightPart();
                        return true;
                    }
                }

                return base.ProcessDialogKey(keyData);
            }

            // En arrivant dans la case au clavier (Tab), on part sur les heures.
            // Si c'est un clic, on ne fait rien ici : OnMouseUp choisit la partie
            // cliquée (sinon on écraserait le clic sur les minutes).
            // BeginInvoke : Windows sélectionne tout le texte juste après l'entrée,
            // on repasse derrière pour ne surligner que les heures.
            protected override void OnEnter(EventArgs e)
            {
                base.OnEnter(e);

                if (Control.MouseButtons != MouseButtons.None)
                    return;

                _onHours = true;
                if (IsHandleCreated)
                    BeginInvoke((Action)HighlightPart);
            }

            protected override void UpdateEditText()
            {
                int total = (int)Value;
                Text = (total / 60).ToString("00") + ":" + (total % 60).ToString("00");
            }

            // Le NumericUpDown de base refuse tout sauf les chiffres : on autorise
            // aussi ":" et "h" pour pouvoir taper "4:55" ou "4h55".
            protected override void OnTextBoxKeyPress(object source, KeyPressEventArgs e)
            {
                char c = e.KeyChar;
                bool allowed = char.IsDigit(c) || c == ':' || c == 'h' || c == 'H' || char.IsControl(c);
                if (!allowed)
                    e.Handled = true;
            }

            // Appelé quand le joueur a tapé quelque chose au clavier
            protected override void ValidateEditText()
            {
                int? minutes = ParseMinutes(Text);
                UserEdit = false;

                if (minutes != null)
                    Value = Math.Max(Minimum, Math.Min(Maximum, minutes.Value));

                UpdateEditText();
            }

            public override void UpButton()
            {
                StepBy(+1);
            }

            public override void DownButton()
            {
                StepBy(-1);
            }

            private void StepBy(int direction)
            {
                if (UserEdit)
                    ValidateEditText();

                decimal step = _onHours ? 60 : 1;
                Value = Math.Max(Minimum, Math.Min(Maximum, Value + direction * step));
                HighlightPart();
            }

            // Surligne la partie active, seulement si la case a le focus (sinon
            // toutes les cases de l'onglet auraient un bout surligné)
            private void HighlightPart()
            {
                if (_edit == null || !_edit.Focused)
                    return;

                int colon = _edit.Text.IndexOf(':');
                if (colon < 0)
                    return;

                if (_onHours)
                    _edit.Select(0, colon);
                else
                    _edit.Select(colon + 1, _edit.Text.Length - colon - 1);
            }

            // "4:55", "04:55", "4h55", "4h", "4" -> minutes totales. null si illisible.
            // internal : resservi par le tableau des préréglages (PresetCellToNumber).
            internal static int? ParseMinutes(string text)
            {
                if (text == null)
                    return null;

                string[] parts = text.Trim().Replace('h', ':').Replace('H', ':').Split(':');
                if (parts.Length > 2)
                    return null;

                int hours;
                if (!int.TryParse(parts[0].Trim(), out hours) || hours < 0)
                    return null;

                int minutes = 0;
                if (parts.Length == 2 && parts[1].Trim() != "")
                {
                    if (!int.TryParse(parts[1].Trim(), out minutes) || minutes < 0 || minutes > 59)
                        return null;
                }

                return hours * 60 + minutes;
            }
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
