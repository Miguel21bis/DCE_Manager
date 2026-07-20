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
    public class ConfModForm : Form
    {
        private readonly string _campaignName;
        private readonly ConfModLoader _loader = new ConfModLoader();
        private readonly ConfModWriter _writer = new ConfModWriter();
        private ConfModDynamicData _data;
        private readonly List<UiFieldControl> _controls = new List<UiFieldControl>();

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
            var layoutsByGroup = new Dictionary<string, TableLayoutPanel>();
            var rowByGroup = new Dictionary<string, int>();

            // Preserve the file's own order: a group's tab appears where its first
            // field appears, and fields within a group keep the file's own order.
            foreach (ConfUiFieldSchema field in _data.Schema.OrderBy(f => f.LineIndex))
            {
                TableLayoutPanel groupLayout;

                if (!layoutsByGroup.TryGetValue(field.Group, out groupLayout))
                {
                    var tab = new TabPage(field.Group);
                    groupLayout = NewLayout();

                    tab.Controls.Add(groupLayout);
                    tabs.TabPages.Add(tab);

                    layoutsByGroup[field.Group] = groupLayout;
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
            foreach (TableLayoutPanel groupLayout in layoutsByGroup.Values)
            {
                groupLayout.RowCount += 1;
                groupLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
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
        }

        private void ButtonSave_Click(object sender, EventArgs e)
        {
            foreach (UiFieldControl fc in _controls)
                _data.Values[fc.Schema.Path] = fc.GetValue();

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
        // Generic control factory: one entry point that turns a field's schema
        // (type + bounds + options) into the right WinForms control, plus a
        // uniform get/set pair so the rest of the Form never needs to know
        // which control kind backs a given field.
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
    }
}