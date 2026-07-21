using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;
using DCE_Manager.Parameters;

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

        private Button buttonSave;
        private Button buttonCancel;

        public ConfModForm(string campaignName)
        {
            _campaignName = campaignName;

            Text = "Config - " + campaignName;
            Width = 560;
            Height = 600;
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            Font = new Font("Segoe UI", 9);

            _data = _loader.Load(campaignName);

            if (_data == null || _data.Schema.Count == 0)
            {
                MessageBox.Show(
                    "No @ui field found in conf_mod.lua for " + campaignName,
                    "Config",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);

                Load += (s, e) => Close();
                return;
            }

            BuildForm();
            BindValues();
        }

        private void BuildForm()
        {
            var tabs = new TabControl { Dock = DockStyle.Fill };
            var tabByGroup = new Dictionary<string, TabPage>();
            var scalarLayoutByGroup = new Dictionary<string, TableLayoutPanel>();
            var rowByGroup = new Dictionary<string, int>();
            var matrixControlsByGroup = new Dictionary<string, List<Control>>();

            // Preserve the file's own order: a group's tab appears where its first
            // field appears, and fields within a group keep the file's own order.
            foreach (ConfUiFieldSchema field in _data.Schema.OrderBy(f => f.LineIndex))
            {
                TabPage tab;

                if (!tabByGroup.TryGetValue(field.Group, out tab))
                {
                    tab = new TabPage(field.Group);
                    tabs.TabPages.Add(tab);
                    tabByGroup[field.Group] = tab;
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
                    tab.Controls.Add(groupLayout);
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

            // Absorbs the leftover vertical space left by Dock=Fill so the AutoSize
            // content rows stay packed at the top instead of the last one stretching.
            foreach (TableLayoutPanel groupLayout in scalarLayoutByGroup.Values)
            {
                groupLayout.RowCount += 1;
                groupLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            }

            // Stack the matrix control(s) of each group into their tab. A single
            // matrix fills the whole tab; several (e.g. main repair table + runway)
            // share the tab evenly, stacked vertically.
            foreach (KeyValuePair<string, List<Control>> kv in matrixControlsByGroup)
            {
                TabPage tab = tabByGroup[kv.Key];
                List<Control> matrixControls = kv.Value;

                if (matrixControls.Count == 1)
                {
                    matrixControls[0].Dock = DockStyle.Fill;
                    tab.Controls.Add(matrixControls[0]);
                    continue;
                }

                var stack = new TableLayoutPanel
                {
                    Dock = DockStyle.Fill,
                    ColumnCount = 1,
                    RowCount = matrixControls.Count
                };

                float percentPerRow = 100f / matrixControls.Count;

                for (int i = 0; i < matrixControls.Count; i++)
                {
                    stack.RowStyles.Add(new RowStyle(SizeType.Percent, percentPerRow));
                    matrixControls[i].Dock = DockStyle.Fill;
                    stack.Controls.Add(matrixControls[i], 0, i);
                }

                tab.Controls.Add(stack);
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

            Controls.Add(tabs);
            Controls.Add(buttonPanel);
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
        // Generic control factory (scalar fields): one entry point that turns a
        // field's schema (type + bounds + options) into the right WinForms
        // control, plus a uniform get/set pair so the rest of the Form never
        // needs to know which control kind backs a given field.
        // ---------------------------------------------------------------

        private static UiFieldControl CreateFieldControl(TableLayoutPanel layout, int row, ConfUiFieldSchema schema)
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
            {
                var tip = new ToolTip();
                tip.SetToolTip(nameLabel, schema.Help);
            }

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
            {
                var tip = new ToolTip();
                tip.SetToolTip(title, schema.Help);
            }

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

        private class UiMatrixControl
        {
            public ConfUiFieldSchema Schema;
            public Control Container;
            public Func<Dictionary<string, double[]>> GetValue;
            public Action<Dictionary<string, double[]>> SetValue;
        }
    }
}
