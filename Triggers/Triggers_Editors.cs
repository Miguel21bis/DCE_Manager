using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using DCE_Manager.Utils;

namespace DCE_Manager
{
    // Deux éditeurs sur mesure pour les actions dont le paramètre est trop compliqué pour une simple zone de texte :
    //   - Action.SetWeather("weather = { trend = 40, refTemp = 25 }")      -> des cases à cocher + des nombres
    //   - Action.ShipMission(groupe, {{"P1", "P2"}, {"P3", "P4"}}, ...)    -> une route par ligne
    // Si le texte du fichier n'a pas la forme attendue, on retombe sur la zone de texte Lua habituelle : rien n'est perdu.
    public partial class Triggers_Form
    {
        private int _detailsNeed;           // largeur utile du panneau de droite (remise à 0 à chaque affichage)

        // ------------------------------------------------------------------------------------------
        //  Météo (Action.SetWeather)
        // ------------------------------------------------------------------------------------------

        private class WeatherField
        {
            public string Key, Label, Tip;
            public decimal Min, Max, Default;
            public int Decimals;
            public bool Main;           // affiché tout de suite (sinon sous "More settings")
            public bool NotRead;        // le SetWeather actuel ne lit pas cette valeur
        }

        // Les réglages d'un SetWeather. Les 4 premiers sont ceux des exemples de camp_triggers ; les autres sont rangés dans "More settings".
        // Lus par DC_CheckTriggers.lua : pHigh (= trend), refTemp, trend, variance, instability, windActivity, winDirection.
        // pLow et weatherChangeRate sont acceptés dans le texte mais ignorés par le moteur actuel.
        private static readonly WeatherField[] WeatherFields =
        {
            new WeatherField { Key = "pHigh", Label = "High pressure (pHigh)", Min = 0, Max = 100, Default = 50, Main = true,
                Tip = "Weather trend, 0 to 100.\r\n0 = strong low pressure (storms, fronts, heavy clouds).\r\n100 = strong high pressure (clear skies, stable weather)." },
            new WeatherField { Key = "pLow", Label = "Low pressure (pLow)", Min = 0, Max = 100, Default = 22, Main = true, NotRead = true,
                Tip = "Written in the text, but the current SetWeather does not read it." },
            new WeatherField { Key = "refTemp", Label = "Reference temperature (°C)", Min = -30, Max = 45, Default = 20, Main = true,
                Tip = "Reference daytime temperature, in degrees Celsius." },
            new WeatherField { Key = "weatherChangeRate", Label = "Change rate (weatherChangeRate)", Min = 0, Max = 1, Default = 0.12m, Decimals = 2, Main = true, NotRead = true,
                Tip = "Written in the text, but the current SetWeather does not read it." },
            new WeatherField { Key = "trend", Label = "Trend", Min = 0, Max = 100, Default = 50,
                Tip = "Same as pHigh. 0 = strong low pressure, 100 = strong high pressure.\r\nIf both are written, pHigh is read first, then trend replaces it." },
            new WeatherField { Key = "variance", Label = "Variance", Min = 0, Max = 100, Default = 30,
                Tip = "How far the weather may deviate from the trend.\r\nLow = stable and predictable. High = wide variations." },
            new WeatherField { Key = "instability", Label = "Instability (hours)", Min = 0, Max = 100, Default = 60,
                Tip = "How fast the weather changes over time, in hours." },
            new WeatherField { Key = "windActivity", Label = "Wind activity (m/s)", Min = 0, Max = 10, Default = 2.5m, Decimals = 1,
                Tip = "Average wind at ground level, in m/s. Higher = stronger and more turbulent." },
            new WeatherField { Key = "winDirection", Label = "Wind direction (°)", Min = 0, Max = 359, Default = 158,
                Tip = "Dominant wind direction, in degrees." }
        };

        private static readonly Regex WeatherRx = new Regex(@"^\s*weather\s*=\s*\{(.*)\}\s*$", RegexOptions.Singleline);
        private static readonly Regex WeatherPairRx = new Regex(@"^\s*([A-Za-z_]\w*)\s*=\s*(.+?)\s*$", RegexOptions.Singleline);

        // Coupe "a = 1, b = { 2, 3 }, c = 4" aux virgules du premier niveau.
        private static List<string> SplitTopLevel(string s)
        {
            var parts = new List<string>();
            var sb = new StringBuilder();
            int depth = 0;
            bool inString = false;
            char quote = '"';

            foreach (char c in s)
            {
                if (inString)
                {
                    if (c == quote) inString = false;
                }
                else if (c == '"' || c == '\'')
                {
                    inString = true;
                    quote = c;
                }
                else if (c == '{' || c == '(') depth++;
                else if (c == '}' || c == ')') depth--;
                else if (c == ',' && depth == 0)
                {
                    parts.Add(sb.ToString());
                    sb.Clear();
                    continue;
                }

                sb.Append(c);
            }

            if (sb.ToString().Trim().Length > 0)
                parts.Add(sb.ToString());

            return parts;
        }

        // Lit "weather = { ... }" : les réglages connus (nombres) et le reste, gardé tel quel. null = forme inattendue.
        private static bool ParseWeather(string text, Dictionary<string, decimal> values, List<string> others)
        {
            Match m = WeatherRx.Match(text);

            if (!m.Success)
                return false;

            foreach (string part in SplitTopLevel(m.Groups[1].Value))
            {
                string trimmed = part.Trim();

                if (trimmed.Length == 0)
                    continue;

                Match pair = WeatherPairRx.Match(trimmed);
                decimal number;

                if (pair.Success && decimal.TryParse(pair.Groups[2].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out number))
                {
                    string key = pair.Groups[1].Value;

                    if (WeatherFields.Any(f => f.Key == key))
                    {
                        values[key] = number;
                        continue;
                    }
                }

                others.Add(trimmed);                    // clé inconnue ou valeur non numérique : gardée telle quelle
            }

            return true;
        }

        private static string BuildWeather(Dictionary<string, decimal> values, List<string> others)
        {
            var parts = new List<string>();

            foreach (WeatherField f in WeatherFields)
            {
                decimal v;

                if (values.TryGetValue(f.Key, out v))
                    parts.Add(f.Key + " = " + v.ToString("0.##", CultureInfo.InvariantCulture));
            }

            parts.AddRange(others);

            return "weather = { " + string.Join(", ", parts.ToArray()) + (parts.Count > 0 ? " }" : "}");
        }

        private Control MakeWeatherEditor(CampTrigger t, ActionRow state, int k, ParamDef p, LuaNode arg, out int height)
        {
            height = 22;

            var values = new Dictionary<string, decimal>();
            var others = new List<string>();

            if (!ParseWeather((arg == null || arg.Name.Trim().Length == 0) ? "weather = { }" : arg.Name, values, others))
                return null;                            // forme inattendue : zone de texte Lua

            var panel = new Panel();
            _detailsNeed = Math.Max(_detailsNeed, 124 + 18 + 380);
            bool loading = true;
            bool expanded = WeatherFields.Any(f => !f.Main && values.ContainsKey(f.Key));

            Action write = () =>
            {
                if (!loading)
                    SetArg(t, state, k, LuaText.Quote(BuildWeather(values, others)));
            };

            var rows = new List<Control[]>();               // pour chaque réglage : la case et le nombre
            var link = new LinkLabel { AutoSize = true, Left = 0 };
            var footnote = new Label { Left = 0, AutoSize = true, ForeColor = SystemColors.GrayText, Text = Lang.T("* Written in the text, but not read by the current SetWeather.") };
            var note = new Label { Left = 0, Width = 440, Height = 20, AutoEllipsis = true, ForeColor = SystemColors.GrayText };

            foreach (WeatherField f in WeatherFields)
            {
                WeatherField field = f;
                decimal start;
                bool on = values.TryGetValue(field.Key, out start);

                var check = new CheckBox { Left = 0, Width = 270, Height = 22, Text = Lang.T(field.Label) + (field.NotRead ? " *" : ""), Checked = on };
                var number = new NumericUpDown
                {
                    Left = 276,
                    Width = 80,
                    Minimum = field.Min,
                    Maximum = field.Max,
                    DecimalPlaces = field.Decimals,
                    Increment = field.Decimals == 2 ? 0.01m : field.Decimals > 0 ? 0.1m : 1m,
                    Enabled = on,
                    Value = Math.Max(field.Min, Math.Min(field.Max, on ? start : field.Default))
                };

                check.CheckedChanged += (s, e) =>
                {
                    number.Enabled = check.Checked;

                    if (check.Checked) values[field.Key] = number.Value;
                    else values.Remove(field.Key);

                    write();
                };

                number.ValueChanged += (s, e) =>
                {
                    if (check.Checked)
                    {
                        values[field.Key] = number.Value;
                        write();
                    }
                };

                _toolTip.SetToolTip(check, Lang.T(field.Tip));
                _toolTip.SetToolTip(number, Lang.T(field.Tip));

                panel.Controls.Add(check);
                panel.Controls.Add(number);
                rows.Add(new Control[] { check, number });
            }

            panel.Controls.Add(link);
            panel.Controls.Add(footnote);
            panel.Controls.Add(note);

            // Place tout ; les réglages secondaires ne sont visibles que dépliés. Renvoie la hauteur utile.
            Func<int> layout = () =>
            {
                int y = 0;

                for (int i = 0; i < WeatherFields.Length; i++)
                {
                    rows[i][0].Visible = rows[i][1].Visible = WeatherFields[i].Main || expanded;

                    if (!rows[i][0].Visible)
                        continue;

                    rows[i][0].Top = y;
                    rows[i][1].Top = y;
                    y += 26;

                    if (i == 3)                         // fin du bloc principal
                    {
                        footnote.Top = y;
                        y += 20;
                        link.Top = y;
                        y += 24;
                    }
                }

                link.Text = expanded ? Lang.T("▾ Hide other settings") : Lang.T("▸ More settings (trend, variance, wind...)");

                note.Visible = others.Count > 0;

                if (others.Count > 0)
                {
                    note.Top = y;
                    note.Text = Lang.T("Also in the text (ignored by the engine): ") + string.Join(", ", others.ToArray());
                    y += 22;
                }

                return y;
            };

            link.LinkClicked += (s, e) =>
            {
                expanded = !expanded;
                int h = layout();

                if (panel.Parent != null)
                    panel.Parent.Height = h + 4 + 4;    // la ligne de paramètre se redimensionne, les suivantes suivent
            };

            height = layout() + 4;
            loading = false;
            return panel;
        }

        // Pour les résumés : "trend 40, temperature 25 °C..."
        public static string DescribeWeather(string text)
        {
            var values = new Dictionary<string, decimal>();
            var others = new List<string>();

            if (!ParseWeather(text, values, others))
                return "\"" + text + "\"";

            var parts = new List<string>();

            foreach (WeatherField f in WeatherFields)
            {
                decimal v;

                if (!values.TryGetValue(f.Key, out v))
                    continue;

                string word = f.Key == "refTemp" ? "temperature" : f.Key == "windActivity" ? "wind" : f.Key == "winDirection" ? "wind direction" : f.Key == "pHigh" ? "high pressure" : f.Key == "pLow" ? "low pressure" : f.Key == "weatherChangeRate" ? "change rate" : f.Key;
                parts.Add(Lang.T(word) + " " + v.ToString("0.##", CultureInfo.InvariantCulture));
            }

            return parts.Count > 0 ? string.Join(", ", parts.ToArray()) : Lang.T("no change");
        }

        // ------------------------------------------------------------------------------------------
        //  Route d'un groupe de navires (Action.ShipMission)
        // ------------------------------------------------------------------------------------------

        // Lit {{"A","B"},{"C","D"}} ou {"A","B"} (une seule route). false = forme inattendue.
        private static bool ParseRoutes(LuaNode table, List<List<string>> routes, out bool startHere)
        {
            startHere = false;
            List<LuaNode> items = table.Items.ToList();

            if (items.Count > 0 && items[0].Kind == NodeKind.Text && items[0].Name.Length == 0)
            {
                startHere = true;                       // le "" du début, gardé tel quel
                items.RemoveAt(0);
            }

            if (items.Count == 0)
                return true;

            if (items.All(x => x.Kind == NodeKind.Text))
            {
                routes.Add(items.Select(x => x.Name).ToList());
                return true;
            }

            if (items.All(x => x.Kind == NodeKind.Table && x.Items.All(y => y.Kind == NodeKind.Text)))
            {
                foreach (LuaNode route in items)
                    routes.Add(route.Items.Select(x => x.Name).ToList());

                return true;
            }

            return false;
        }

        private static string BuildRoutes(List<List<string>> routes, bool startHere)
        {
            string inner = string.Join(", ", routes.Select(r => "{" + string.Join(", ", r.Select(LuaText.Quote).ToArray()) + "}").ToArray());
            return "{" + (startHere ? "\"\"" + (inner.Length > 0 ? ", " : "") : "") + inner + "}";
        }

        // Éditeur de zones de navires : à gauche les variantes (une est tirée au hasard à chaque mission),
        // à droite la liste des points de la variante choisie (autant qu'on veut), avec un champ pour en ajouter.
        // flat = true : une seule liste de points, sans variantes (Return.ShipGroupInPoly).
        private Control MakeRouteEditor(CampTrigger t, ActionRow state, int k, ParamDef p, LuaNode arg, bool flat, List<string> zoneNames, out int height)
        {
            height = 22;

            var routes = new List<List<string>>();
            bool startHere = false;
            bool emptyText = arg != null && arg.Kind == NodeKind.Text && arg.Name.Trim().Length == 0;   // ancienne valeur par défaut ""

            if (arg != null && !emptyText && (arg.Kind != NodeKind.Table || !ParseRoutes(arg, routes, out startHere)))
                return null;                            // forme inattendue : zone de texte Lua

            if (routes.Count == 0)
                routes.Add(new List<string>());

            if (flat && routes.Count > 1)
                return null;                            // plusieurs variantes dans un test : on ne touche pas

            var panel = new Panel();
            bool busy = true;
            int left = flat ? 0 : 150;
            _detailsNeed = Math.Max(_detailsNeed, 124 + 18 + left + 300);     // largeur mini du panneau de droite (sinon : une barre de défilement horizontale, une seule)

            var titleLeft = new Label { Left = 0, Top = 0, Width = 144, Height = 18, ForeColor = SystemColors.GrayText, Text = Lang.T("Alternatives"), Visible = !flat };
            var titleRight = new Label { Left = left, Top = 0, Width = 294, Height = 18, ForeColor = SystemColors.GrayText, Text = flat ? Lang.T("Points of the area") : Lang.T("Points of the selected alternative") };

            var lst = new ListBox { Left = 0, Top = 20, Width = 144, Height = 104, IntegralHeight = false, Visible = !flat };
            var add = new Button { Left = 0, Top = 128, Width = 70, Height = 26, Text = Lang.T("+ Add"), Visible = !flat };
            var del = new Button { Left = 74, Top = 128, Width = 70, Height = 26, Text = Lang.T("Remove"), Visible = !flat };

            var ptsList = new ListBox { Left = left, Top = 20, Width = 294, Height = 104, IntegralHeight = false, HorizontalScrollbar = true };

            var combo = new ComboBox
            {
                Left = left, Top = 129, Width = 170, DropDownWidth = 360,
                DropDownStyle = ComboBoxStyle.DropDown
            };

            if (zoneNames != null)
            {
                combo.AutoCompleteMode = AutoCompleteMode.SuggestAppend;
                combo.AutoCompleteSource = AutoCompleteSource.ListItems;
                combo.Items.AddRange(zoneNames.ToArray());
            }

            var addPt = new Button { Left = left + 176, Top = 127, Width = 84, Height = 26, Text = Lang.T("+ Add point") };
            var delPt = new Button { Left = left + 264, Top = 127, Width = 30, Height = 26, Text = "✕" };
            _toolTip.SetToolTip(delPt, Lang.T("Remove point"));

            var status = new Label
            {
                Left = left, Top = 160, Width = 294, Height = 48,
                ForeColor = SystemColors.GrayText
            };

            Func<int, string> itemText = i => flat ? "" : Lang.T("Alternative ") + (i + 1) + " (" + routes[i].Count + ")";
            Func<int> current = () => Math.Max(0, lst.SelectedIndex);

            Action showStatus = () =>
            {
                List<string> list = routes[current()];
                int n = list.Count;

                if (n == 0) status.Text = Lang.T("Choose or type a reference point, then click \"+ Add point\". You can add as many as you need.");
                else if (n == 1) status.Text = flat ? Lang.T("1 point: the group must be exactly on it.") : Lang.T("1 point: the group goes to this point.");
                else if (n == 2) status.Text = flat ? Lang.T("2 points: the group must be on the line between them.") : Lang.T("2 points: the group sails between the two points.");
                else status.Text = flat ? Lang.T("Several points: they make an area. The group must be inside it.") : Lang.T("Several points: they make an area. The group sails to random spots inside it.");

                if (!flat)
                    status.Text += "\r\n" + Lang.T("One alternative is picked at random at each mission.");

                status.ForeColor = SystemColors.GrayText;

                if (zoneNames != null)
                {
                    string[] unknown = list.Where(x => !zoneNames.Contains(x)).ToArray();

                    if (unknown.Length > 0)
                    {
                        status.Text = Lang.T("Not found in base_mission.miz: ") + string.Join(", ", unknown);
                        status.ForeColor = SystemColors.ControlText;
                    }
                }
            };

            Action fillPoints = () =>
            {
                bool before = busy;
                busy = true;
                ptsList.Items.Clear();

                foreach (string name in routes[current()])
                    ptsList.Items.Add(zoneNames != null && !zoneNames.Contains(name) ? "⚠ " + name : name);

                busy = before;
                showStatus();
            };

            Action refreshAlternative = () =>
            {
                if (flat)
                    return;

                int i = current();
                busy = true;
                lst.Items[i] = itemText(i);
                lst.SelectedIndex = i;
                busy = false;
            };

            Action write = () =>
            {
                if (busy)
                    return;

                List<List<string>> good = routes.Where(r => r.Count > 0).ToList();

                if (good.Count == 0)
                {
                    MessageBox.Show(Lang.T("At least one reference point is needed."), "Triggers", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                if (flat)
                    SetArg(t, state, k, "{" + string.Join(", ", good[0].Select(LuaText.Quote).ToArray()) + "}");
                else
                    SetArg(t, state, k, BuildRoutes(good, startHere));
            };

            Action doAddPoint = () =>
            {
                string name = combo.Text.Trim();

                if (name.Length == 0)
                    return;

                routes[current()].Add(name);
                combo.Text = "";
                fillPoints();
                refreshAlternative();
                write();
                combo.Focus();
            };

            Action doRemovePoint = () =>
            {
                int i = ptsList.SelectedIndex;

                if (i < 0)
                    return;

                routes[current()].RemoveAt(i);
                fillPoints();
                refreshAlternative();
                write();
            };

            for (int i = 0; i < routes.Count; i++)
                lst.Items.Add(itemText(i));

            lst.SelectedIndex = 0;
            fillPoints();

            lst.SelectedIndexChanged += (s, e) =>
            {
                if (!busy && lst.SelectedIndex >= 0)
                    fillPoints();
            };

            addPt.Click += (s, e) => doAddPoint();
            delPt.Click += (s, e) => doRemovePoint();

            combo.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter)
                {
                    e.SuppressKeyPress = true;
                    doAddPoint();
                }
            };

            combo.SelectionChangeCommitted += (s, e) => BeginInvoke(new Action(doAddPoint));

            ptsList.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Delete)
                    doRemovePoint();
            };

            add.Click += (s, e) =>
            {
                routes.Add(new List<string>());
                busy = true;
                lst.Items.Add(itemText(routes.Count - 1));
                lst.SelectedIndex = routes.Count - 1;
                busy = false;
                fillPoints();
                combo.Focus();
            };

            del.Click += (s, e) =>
            {
                int i = lst.SelectedIndex;

                if (i < 0)
                    return;

                if (routes.Count == 1)
                {
                    routes[0].Clear();                  // la dernière variante reste, vide
                    refreshAlternative();
                }
                else
                {
                    routes.RemoveAt(i);
                    busy = true;
                    lst.Items.Clear();

                    for (int n = 0; n < routes.Count; n++)
                        lst.Items.Add(itemText(n));

                    lst.SelectedIndex = Math.Min(i, routes.Count - 1);
                    busy = false;
                }

                fillPoints();
                write();
            };

            // La partie droite suit la largeur disponible (barre de défilement horizontale en dessous de 520 px).
            panel.Resize += (s, e) =>
            {
                int width = Math.Max(294, panel.ClientSize.Width - left - 8);
                ptsList.Width = width;
                status.Width = width;
                titleRight.Width = width;
            };

            _toolTip.SetToolTip(ptsList, Lang.T("The reference points (Refpoint) of this alternative. Select one and press Delete to remove it."));
            _toolTip.SetToolTip(combo, Lang.T("Pick a zone of base_mission.miz or type a name, then press Enter."));
            _toolTip.SetToolTip(lst, Lang.T("Alternatives: one of them is picked at random at each mission."));

            panel.Controls.AddRange(new Control[] { titleLeft, titleRight, lst, add, del, ptsList, combo, addPt, delPt, status });

            busy = false;
            height = 212;
            return panel;
        }

        // Une heure HH:MM depuis le début de la campagne, facultative. Le Lua reçoit des secondes.
        private Control MakeTimeEditor(CampTrigger t, ActionRow state, int k, ParamDef p, LuaNode arg, out int height)
        {
            height = 26;

            decimal seconds = 0;
            bool on = false;

            if (arg != null && arg.Kind == NodeKind.Number)
            {
                if (!decimal.TryParse(arg.Source.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out seconds) || seconds < 0 || seconds % 60 != 0 || seconds > 999 * 3600 + 3540)
                    return null;                        // pas un nombre de minutes entières : zone Lua

                on = true;
            }
            else if (arg != null && arg.Kind != NodeKind.Nil)
            {
                return null;
            }

            var panel = new Panel();

            var check = new CheckBox { Left = 0, Top = 3, Width = 70, Text = Lang.T("set"), Checked = on };
            var hours = new NumericUpDown { Left = 74, Top = 1, Width = 60, Minimum = 0, Maximum = 999, Enabled = on, Value = (int)(seconds / 3600) };
            var colon = new Label { Left = 136, Top = 4, Width = 10, Text = ":" };
            var minutes = new NumericUpDown { Left = 148, Top = 1, Width = 50, Minimum = 0, Maximum = 59, Enabled = on, Value = (int)((seconds % 3600) / 60) };
            var info = new Label { Left = 208, Top = 4, Width = 300, ForeColor = SystemColors.GrayText };

            Action refresh = () =>
            {
                info.Text = check.Checked ? Lang.T("hours : minutes since the start of the campaign") : Lang.T("(not set: ") + p.Default + ")";
            };

            Action write = () =>
            {
                refresh();
                SetArg(t, state, k, check.Checked ? ((int)hours.Value * 3600 + (int)minutes.Value * 60).ToString(CultureInfo.InvariantCulture) : null);
            };

            check.CheckedChanged += (s, e) => { hours.Enabled = minutes.Enabled = check.Checked; write(); };
            hours.ValueChanged += (s, e) => { if (check.Checked) write(); };
            minutes.ValueChanged += (s, e) => { if (check.Checked) write(); };

            _toolTip.SetToolTip(hours, Lang.T("Hours since the start of the campaign."));
            _toolTip.SetToolTip(minutes, Lang.T("Minutes (0 to 59)."));

            refresh();
            panel.Controls.AddRange(new Control[] { check, hours, colon, minutes, info });
            return panel;
        }

        // Vitesse en m/s, facultative : une case "définie" + un nombre, avec l'équivalent en nœuds.
        private Control MakeSpeedEditor(CampTrigger t, ActionRow state, int k, ParamDef p, LuaNode arg, decimal startValue, out int height)
        {
            height = 24;

            decimal value = startValue;
            bool on = false;

            if (arg != null && arg.Kind == NodeKind.Number)
            {
                decimal parsed;

                if (!decimal.TryParse(arg.Source.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out parsed))
                    return null;

                value = parsed;
                on = true;
            }
            else if (arg != null && arg.Kind != NodeKind.Nil)
            {
                return null;                            // une variable ou un calcul : zone Lua
            }

            var panel = new Panel();
            _detailsNeed = Math.Max(_detailsNeed, 124 + 18 + 360);
            var check = new CheckBox { Left = 0, Top = 2, Width = 70, Text = Lang.T("set"), Checked = on };

            var number = new NumericUpDown
            {
                Left = 74, Top = 0, Width = 70, Minimum = 0, Maximum = 40, DecimalPlaces = 1, Increment = 0.5m,
                Enabled = on, Value = Math.Max(0, Math.Min(40, value))
            };

            var knots = new Label { Left = 150, Top = 3, Width = 360, ForeColor = SystemColors.GrayText };

            Action refresh = () =>
            {
                knots.Text = check.Checked
                    ? "= " + (number.Value * 1.94384m).ToString("0.#", CultureInfo.InvariantCulture) + " kn"
                    : Lang.T("(not set: ") + p.Default + ")";
            };

            Action write = () =>
            {
                refresh();
                SetArg(t, state, k, check.Checked ? number.Value.ToString("0.##", CultureInfo.InvariantCulture) : null);
            };

            check.CheckedChanged += (s, e) => { number.Enabled = check.Checked; write(); };
            number.ValueChanged += (s, e) => { if (check.Checked) write(); };

            _toolTip.SetToolTip(number, Lang.T("Speed in metres per second (1 m/s = 1.94 knots)."));
            refresh();

            panel.Controls.Add(check);
            panel.Controls.Add(number);
            panel.Controls.Add(knots);
            return panel;
        }

        // Nombre avec bornes et unité (--@param ... | type=percent) : une case "défini" si facultatif, le nombre, puis l'unité.
        private Control MakeRangeEditor(CampTrigger t, ActionRow state, int k, ParamDef p, LuaNode arg, out int height)
        {
            height = 24;

            decimal min = p.Min.HasValue ? (decimal)p.Min.Value : 0m;
            decimal max = p.Max.HasValue ? (decimal)p.Max.Value : 1000000m;
            int decimals = max <= 1m ? 2 : 0;
            decimal value = min;
            bool on = false;

            if (arg != null && arg.Kind == NodeKind.Number)
            {
                decimal parsed;

                if (!decimal.TryParse(arg.Source.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out parsed))
                    return null;

                value = parsed;
                on = true;

                if (decimals == 0 && parsed != decimal.Truncate(parsed))
                    decimals = 2;
            }
            else if (arg != null && arg.Kind != NodeKind.Nil)
            {
                return null;                            // une variable ou un calcul : zone Lua
            }

            bool optional = p.Optional;
            int x = optional ? 64 : 0;
            var panel = new Panel { Width = x + 74 + 150, Tag = "compact" };
            _detailsNeed = Math.Max(_detailsNeed, 124 + 18 + panel.Width);

            CheckBox check = optional ? new CheckBox { Left = 0, Top = 2, Width = 60, Text = Lang.T("set"), Checked = on } : null;

            var number = new NumericUpDown
            {
                Left = x, Top = 0, Width = 70, Minimum = min, Maximum = max, DecimalPlaces = decimals,
                Increment = decimals > 0 ? 0.05m : 1m,
                Enabled = !optional || on,
                Value = Math.Max(min, Math.Min(max, value))
            };

            var unit = new Label { Left = x + 74, Top = 3, Width = 150, ForeColor = SystemColors.GrayText };

            Action refresh = () =>
            {
                bool set = check == null || check.Checked;
                unit.Text = set ? p.Unit : (p.Default.Length > 0 ? Lang.T("(not set: ") + p.Default + ")" : Lang.T("(not set)"));
            };

            Action write = () =>
            {
                refresh();
                bool set = check == null || check.Checked;
                SetArg(t, state, k, set ? number.Value.ToString(decimals > 0 ? "0.##" : "0", CultureInfo.InvariantCulture) : null);
            };

            if (check != null)
                check.CheckedChanged += (s, e) => { number.Enabled = check.Checked; write(); };

            number.ValueChanged += (s, e) => { if (check == null || check.Checked) write(); };

            refresh();

            if (check != null)
                panel.Controls.Add(check);

            panel.Controls.Add(number);
            panel.Controls.Add(unit);
            return panel;
        }

        // Pour les résumés : "A → B → C" (ou plusieurs routes : "(A → B) or (C → D)")
        public static string DescribeRoute(LuaNode table)
        {
            var routes = new List<List<string>>();
            bool startHere;

            if (!ParseRoutes(table, routes, out startHere) || routes.Count == 0)
                return TriggerText.Describe(table);

            string[] texts = routes.Select(r => string.Join(" → ", r.ToArray())).ToArray();

            return texts.Length == 1 ? texts[0] : string.Join(Lang.T(" or "), texts.Select(x => "(" + x + ")").ToArray());
        }
    }
}
