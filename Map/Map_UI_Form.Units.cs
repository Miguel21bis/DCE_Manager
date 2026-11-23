using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;
using DCE_Manager.Parameters;
using DCE_Manager.Utils;

namespace DCE_Manager
{
    // Partie de Map_UI_Form (voir Map_UI_Form.cs) : avions du vol : lignes, indicatifs, fréquences, suppression
    internal partial class Map_UI_Form : Form
    {
        private static string TextOf(object o)
        {
            return o == null ? "" : Convert.ToString(o, CultureInfo.InvariantCulture);
        }

        // Les avions du vol tels qu'ils sont dans le fichier (la table Lua du vol est déjà en mémoire : pas de relecture)
        private List<UnitRow> OriginalRows(Map_Flight f)
        {
            var rows = new List<UnitRow>();
            object uo;
            var us = f.Source != null && f.Source.TryGetValue("units", out uo) ? uo as Dictionary<string, object> : null;
            if (us == null) return rows;

            for (int i = 1; ; i++)
            {
                object o;
                var u = us.TryGetValue(i.ToString(CultureInfo.InvariantCulture), out o) ? o as Dictionary<string, object> : null;
                if (u == null) break;

                object co, v;
                var cs = u.TryGetValue("callsign", out co) ? co as Dictionary<string, object> : null;
                string skill = u.TryGetValue("skill", out v) ? TextOf(v) : "";
                rows.Add(new UnitRow
                {
                    Src = i,
                    Name = u.TryGetValue("name", out v) ? TextOf(v) : "",
                    Onboard = u.TryGetValue("onboard_num", out v) ? TextOf(v) : "",
                    Callsign = cs != null && cs.TryGetValue("name", out v) ? TextOf(v) : "",
                    Player = skill.Equals("Client", StringComparison.OrdinalIgnoreCase) || skill.Equals("Player", StringComparison.OrdinalIgnoreCase)
                });
            }
            return rows;
        }

        private List<UnitRow> RowsOf(Map_Flight f)
        {
            List<UnitRow> rows;
            return _unitRows.TryGetValue(f.Name, out rows) ? rows : OriginalRows(f);
        }

        private bool RowsDirty(Map_Flight f, List<UnitRow> rows)
        {
            List<UnitRow> o = OriginalRows(f);
            if (o.Count != rows.Count) return true;
            for (int i = 0; i < o.Count; i++)
                if (rows[i].Src != o[i].Src || rows[i].Name != o[i].Name || rows[i].Callsign != o[i].Callsign || rows[i].Onboard != o[i].Onboard) return true;
            return false;
        }

        // Garde la liste en attente (ou l'oublie si elle est redevenue identique au fichier)
        private void TouchRows(Map_Flight f, List<UnitRow> rows)
        {
            if (RowsDirty(f, rows)) _unitRows[f.Name] = rows;
            else _unitRows.Remove(f.Name);
            UpdateSaveWpt();
        }

        // Même nom et même 1er chiffre pour tout le vol (pris sur le premier avion), 2e chiffre = place dans le vol : 1, 2, 3... sans trou
        private static void RenumberCallsigns(List<UnitRow> rows)
        {
            if (rows.Count == 0) return;
            Match m = Regex.Match(rows[0].Callsign ?? "", @"^(.*?)(\d)(\d)$");
            if (!m.Success) return;
            for (int i = 0; i < rows.Count; i++) rows[i].Callsign = m.Groups[1].Value + m.Groups[2].Value + (i + 1);
        }

        // Nouvel avion : nom libre "<vol>-k", indicatif et numéro de queue qui suivent ceux du dernier
        private UnitRow NewRow(Map_Flight f, List<UnitRow> rows)
        {
            UnitRow last = rows.Count > 0 ? rows[rows.Count - 1] : new UnitRow();
            int k = 1;
            while (rows.Any(r => r.Name == f.Name + "-" + k)) k++;

            string cs = last.Callsign;
            if (cs == "")
            {
                List<string> choices = CallsignChoices(f);
                if (choices.Count > 0) cs = choices[0] + "1" + (rows.Count + 1);       // indicatif du squad : "Uzi" -> "Uzi12"
            }
            Match cm = Regex.Match(cs, @"^(.*)\d$");
            if (cm.Success) cs = cm.Groups[1].Value + (rows.Count + 1);

            string ob = SuggestTail(f, rows, null);         // plage "sidenumber" du squad, null s'il n'en a pas
            if (ob == null)
            {
                ob = last.Onboard;
                int obn;
                if (ob.Length > 0 && ob.All(char.IsDigit) && int.TryParse(ob, out obn)) ob = (obn + 1).ToString(CultureInfo.InvariantCulture).PadLeft(ob.Length, '0');
            }

            return new UnitRow { Src = 0, Name = f.Name + "-" + k, Callsign = cs, Onboard = ob };
        }

        // Le nombre d'avions (ou la liste) a changé : silhouettes prévues sur la carte, état du Save, cadre refait
        private void RowsCountChanged(Map_Flight f, List<UnitRow> rows)
        {
            RenumberCallsigns(rows);
            TouchRows(f, rows);
            f.PendingUnitPos = rows.Count == f.Units ? null : Map_Drawings.PreviewUnits(_missionFile, f.Source, rows.Count, SarSpotsNear);     // sans relire le fichier
            if (_missionData != null) _view.ReplaceFlightDrawings(f.Name, Map_Flights.BuildPlans(new[] { f }));            // seulement ce vol, pas tous les tracés

            _syncNum = true;
            _numAircraft.Value = Math.Max(1, Math.Min(4, rows.Count));
            _syncNum = false;

            _lblStatus.Text = rows.Count == f.Units && !_unitRows.ContainsKey(f.Name) ? f.Title + ": no change." : f.Title + ": " + f.Units + " -> " + rows.Count + " aircraft. Press Save modification to write it in the mission.";
            RebuildUnitRows(f, rows);
        }

        private void RebuildUnitsPanel()
        {
            if (_unitsPanel == null) return;

            Map_Flight f = _shownFlight;
            _unitsPanel.SuspendLayout();
            foreach (Control c in _unitsPanel.Controls.Cast<Control>().ToList()) { _unitsPanel.Controls.Remove(c); c.Dispose(); }
            _unitsPanel.RowStyles.Clear();
            _unitsPanel.RowCount = 0;

            if (f == null)
            {
                _unitsPanel.Visible = false;
                _unitsPanel.ResumeLayout();
                ResizeFlightHost();
                return;
            }

            _unitsPanel.Visible = true;
            bool pencil = _chkMove != null && _chkMove.Checked;
            List<UnitRow> rows = RowsOf(f);
            List<string> choices = pencil ? CallsignChoices(f) : new List<string>();
            _csRefresh.Clear();
            _rowsShown = rows;
            int line = 0;

            // Titre du cadre + croix du groupe (même action que "Delete...")
            var title = new Label { Text = "Aircraft of this flight", Font = FTitle9, Dock = DockStyle.Fill, TextAlign = ContentAlignment.BottomLeft, Height = 22 };
            _unitsPanel.Controls.Add(title, 0, line);
            _unitsPanel.SetColumnSpan(title, 4);
            var xGroup = MakeCross("Delete / disable the whole flight (same as the Delete... button)", pencil && !f.IsPlayer);
            xGroup.Click += (s, e) => RemoveShownFlight();
            _unitsPanel.Controls.Add(xGroup, 4, line);
            line++;

            // Indicatif du vol : un seul nom et un seul 1er chiffre pour tous les avions ; le 2e chiffre est la place de l'avion (1, 2, 3... sans trou)
            Match fm = Regex.Match(rows.Count > 0 ? rows[0].Callsign ?? "" : "", @"^(.*?)(\d)(\d)$");
            string fName = fm.Success ? fm.Groups[1].Value : (rows.Count > 0 ? rows[0].Callsign ?? "" : ""), fDigit = fm.Success ? fm.Groups[2].Value : "";

            var lblCs = new Label { Text = "Callsign (flight)", Dock = DockStyle.Fill, Height = 22, TextAlign = ContentAlignment.MiddleLeft, Margin = new Padding(0, 6, 0, 0) };
            Control fcName;
            if (pencil && choices.Count > 0)
            {
                var cb = new ComboBox { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 2, 0), Font = FCell, DropDownStyle = ComboBoxStyle.DropDown, DropDownWidth = 170 };
                cb.Items.AddRange(choices.Cast<object>().ToArray());
                cb.Text = fName;
                fcName = cb;
            }
            else { TextBox tb = MakeCell(fName, pencil); tb.Margin = new Padding(0, 0, 2, 0); fcName = tb; }
            TextBox fcDigit = MakeCell(fDigit, pencil);
            fcDigit.MaxLength = 1;
            fcDigit.TextAlign = HorizontalAlignment.Center;
            fcDigit.Margin = new Padding(0, 0, 2, 0);
            fcDigit.KeyPress += (s, e) => { if (!char.IsControl(e.KeyChar) && !char.IsDigit(e.KeyChar)) e.Handled = true; };
            if (_toolsTip != null)
            {
                _toolsTip.SetToolTip(fcName, "Callsign name of the whole flight: pick one or type your own.");
                _toolsTip.SetToolTip(fcDigit, "Flight number (first digit), the same for all aircraft. The last digit is the place of the aircraft: 1, 2, 3...");
            }
            var fcCell = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = new Padding(0, 6, 3, 1), Height = 24 };
            fcCell.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            fcCell.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 22));
            fcCell.Controls.Add(fcName, 0, 0);
            fcCell.Controls.Add(fcDigit, 1, 0);
            EventHandler fcChanged = (s, e) =>
            {
                string nm = fcName.Text.Trim(), d = fcDigit.Text;
                List<UnitRow> rs = _rowsShown;
                for (int k = 0; k < rs.Count; k++) rs[k].Callsign = d == "" ? nm : nm + d + (k + 1);
                foreach (Action a in _csRefresh) a();
                TouchRows(f, rs);
            };
            fcName.TextChanged += fcChanged;
            fcDigit.TextChanged += fcChanged;
            _unitsPanel.Controls.Add(lblCs, 0, line);
            _unitsPanel.Controls.Add(fcCell, 1, line);
            line++;

            // Légende des colonnes
            string[] heads = { "Name", "Callsign", "Tail no." };
            for (int i = 0; i < 3; i++)
                _unitsPanel.Controls.Add(new Label { Text = heads[i], ForeColor = Color.DimGray, Font = FSmall75, Dock = DockStyle.Fill, Height = 16, TextAlign = ContentAlignment.BottomLeft }, i, line);
            line++;

            _unitRows_tbl = new Map_UI_FastTable { Dock = DockStyle.Fill, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 5, Margin = new Padding(0) };
            _unitRows_tbl.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40));
            _unitRows_tbl.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40));
            _unitRows_tbl.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20));
            _unitRows_tbl.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 28));
            _unitRows_tbl.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 30));
            FillUnitRows(f, rows, pencil);
            _unitsPanel.Controls.Add(_unitRows_tbl, 0, line);
            _unitsPanel.SetColumnSpan(_unitRows_tbl, 5);
            line++;

            // Fréquence "flight" (radio entre les avions du vol, DCE_FreqFlight) : contrôlée sur les plages radio du type d'avion
            List<double[]> ranges = RadioRanges(f);
            double shown = ShownFreq(f), orig = FlightFreq(f);
            var lblFreq = new Label { Text = "Flight radio (MHz)", Dock = DockStyle.Fill, Height = 22, TextAlign = ContentAlignment.MiddleLeft, Margin = new Padding(0, 6, 0, 0) };
            TextBox tf = MakeCell(shown > 0 ? shown.ToString("0.000", CultureInfo.InvariantCulture) : "", pencil);
            tf.Margin = new Padding(0, 6, 3, 1);
            if (_toolsTip != null)
                _toolsTip.SetToolTip(tf, ranges != null && ranges.Count > 0 ? (RadioKnown(f) ? "Radio range of " + f.Type + ": " : "No radio data for " + f.Type + ", scriptsMod default range: ") + RangesText(ranges) + " MHz" : "No radio range known for this aircraft.");

            Color okC = SystemColors.Window, editedC = Color.FromArgb(255, 248, 200), warnC = Color.FromArgb(255, 220, 170), badC = Color.FromArgb(255, 200, 200);
            tf.TextChanged += (s, e) =>
            {
                string txt = tf.Text.Trim().Replace(',', '.');
                double v;
                if (!double.TryParse(txt, NumberStyles.Float, CultureInfo.InvariantCulture, out v))
                {
                    _pendingFreq.Remove(f.Name);
                    tf.BackColor = txt == "" ? okC : badC;
                    UpdateSaveWpt();
                    return;
                }
                if (Math.Abs(v - orig) < 0.0005)
                {
                    _pendingFreq.Remove(f.Name);
                    tf.BackColor = okC;
                    UpdateSaveWpt();
                    return;
                }
                string msg;
                int st = CheckFreqLogged(f, v, out msg);
                if (st == 2)
                {
                    _pendingFreq.Remove(f.Name);
                    tf.BackColor = badC;
                    _lblStatus.Text = f.Title + ": " + msg;
                }
                else
                {
                    _pendingFreq[f.Name] = v;
                    tf.BackColor = st == 1 ? warnC : editedC;
                    _lblStatus.Text = f.Title + ": flight radio " + v.ToString("0.000", CultureInfo.InvariantCulture) + " MHz. " + (st == 1 ? msg + " " : "") + "Press Save modification to write it in the mission.";
                }
                UpdateSaveWpt();
            };
            tf.BackColor = _pendingFreq.ContainsKey(f.Name) ? editedC : okC;
            tf.Leave += (s, e) =>       // toujours 3 décimales : 266.2 -> 266.200
            {
                double lv;
                if (double.TryParse(tf.Text.Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out lv) && lv > 0) tf.Text = lv.ToString("0.000", CultureInfo.InvariantCulture);
            };

            var diceF = MakeDice("New free frequency in the radio range of this aircraft", pencil && ranges != null && ranges.Count > 0);
            diceF.Click += (s, e) =>
            {
                double v = RandomFreq(f);
                if (v > 0) tf.Text = v.ToString("0.000", CultureInfo.InvariantCulture);
                else _lblStatus.Text = f.Title + ": no free frequency found in the radio range.";
            };

            _unitsPanel.Controls.Add(lblFreq, 0, line);
            _unitsPanel.Controls.Add(tf, 1, line);
            _unitsPanel.Controls.Add(diceF, 3, line);
            line++;

            var lblRange = new Label
            {
                Text = ranges != null && ranges.Count > 0 ? (RadioKnown(f) ? "Radio range of " + f.Type + ": " : "No radio data for " + f.Type + ", scriptsMod default range: ") + RangesText(ranges) + " MHz" : "Radio range of " + f.Type + " unknown.",
                ForeColor = Color.DimGray, Font = FSmall75, Dock = DockStyle.Fill, AutoSize = true, MaximumSize = new Size(360, 0)
            };
            _unitsPanel.Controls.Add(lblRange, 0, line);
            _unitsPanel.SetColumnSpan(lblRange, 4);

            _unitsPanel.ResumeLayout(true);
            ResizeFlightHost();
        }

        // Remplit le sous-tableau : une ligne par avion (nom, indicatif, numéro de queue, dé, croix)
        private void FillUnitRows(Map_Flight f, List<UnitRow> rows, bool pencil)
        {
            List<UnitRow> orig0 = OriginalRows(f);
            bool hasSquad = SquadOfFlight(f) != null;
            int line = 0;

            for (int i = 0; i < rows.Count; i++)
            {
                UnitRow row = rows[i];
                int ui = i;
                TextBox tn = MakeCell(row.Name, pencil), to = MakeCell(row.Onboard, pencil);

                // Indicatif de l'avion : calculé (nom et 1er chiffre communs à tout le vol, 2e chiffre = place dans le vol : 1, 2, 3...)
                // Il se règle avec la ligne "Callsign" sous la liste, pas avion par avion.
                var tc = new Label { Text = row.Callsign, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Margin = new Padding(0, 1, 3, 1), Height = 22, Font = FCell, ForeColor = Color.DimGray };

                Action color = () =>
                {
                    List<UnitRow> o = orig0;
                    UnitRow src = row.Src > 0 && row.Src <= o.Count ? o[row.Src - 1] : null;
                    Color ok = SystemColors.Window, edited = Color.FromArgb(255, 248, 200), added = Color.FromArgb(215, 245, 215);
                    tn.BackColor = src == null ? added : (tn.Text != src.Name ? edited : ok);
                    tc.BackColor = src == null ? added : (tc.Text != src.Callsign ? edited : SystemColors.Control);
                    to.BackColor = src == null ? added : (to.Text != src.Onboard ? edited : ok);
                };
                _csRefresh.Add(() => { tc.Text = row.Callsign; color(); });
                tn.TextChanged += (s, e) => { row.Name = tn.Text; color(); TouchRows(f, rows); };
                to.TextChanged += (s, e) => { row.Onboard = to.Text; color(); TouchRows(f, rows); };
                color();

                var dice = MakeDice("New tail number from the squadron range (sidenumber), a free one if possible", pencil && hasSquad);
                dice.Click += (s, e) =>
                {
                    string ob = SuggestTail(f, rows, row);
                    if (ob == null) _lblStatus.Text = f.Title + ": this squadron has no tail number range (sidenumber).";
                    else to.Text = ob;
                };

                var xUnit = MakeCross("Remove this aircraft from the flight", pencil && !row.Player && rows.Count > 1);
                xUnit.Click += (s, e) => { rows.Remove(row); RowsCountChanged(f, rows); };

                _unitRows_tbl.Controls.Add(tn, 0, line);
                _unitRows_tbl.Controls.Add(tc, 1, line);
                _unitRows_tbl.Controls.Add(to, 2, line);
                _unitRows_tbl.Controls.Add(dice, 3, line);
                _unitRows_tbl.Controls.Add(xUnit, 4, line);
                line++;
            }

        }

        // Ajout / retrait d'un avion : seules les lignes d'avions sont refaites, le reste du cadre ne bouge pas (pas de scintillement)
        private void RebuildUnitRows(Map_Flight f, List<UnitRow> rows)
        {
            if (_unitRows_tbl == null || _unitsPanel == null || !_unitsPanel.Visible || _shownFlight != f) { RebuildUnitsPanel(); return; }

            _rowsShown = rows;
            _csRefresh.Clear();
            bool pencil = _chkMove != null && _chkMove.Checked;
            _unitRows_tbl.SuspendLayout();
            foreach (Control c in _unitRows_tbl.Controls.Cast<Control>().ToList()) { _unitRows_tbl.Controls.Remove(c); c.Dispose(); }
            _unitRows_tbl.RowStyles.Clear();
            _unitRows_tbl.RowCount = 0;
            FillUnitRows(f, rows, pencil);
            _unitRows_tbl.ResumeLayout(true);
            ResizeFlightHost();
        }

        // ---- Squadron du vol, numéro de queue, indicatif, fréquence ----

        private static readonly Regex FlightSquadRx = new Regex(@"^(?:Recovery )?Pack \d+ - (.+) - \D+? ?\d+$");
        private CampaignLuaData _luaData;
        private bool _luaTried;
        private readonly HashSet<string> _radioLogged = new HashSet<string>();

        // Données de scriptsMod (indicatifs, radios par avion) : lues une fois, le loader garde déjà un cache par campagne
        private CampaignLuaData LuaOf()
        {
            if (_luaTried) return _luaData;
            _luaTried = true;
            try { _luaData = _ctx != null ? _ctx.LuaData : new CampaignLuaLoader().Load(_campaign); }
            catch (Exception ex) { FormUtils.LogRegister("Map_UI_Form | scriptsMod data : " + ex.Message); _luaData = null; }
            FormUtils.LogRegister("Map radio | scriptsMod data " + (_luaData == null ? "NOT loaded" : "loaded") + " | modules with radio ranges : "
                + (_luaData == null || _luaData.RadioByModule == null ? "RadioByModule is null (loader does not fill it?)" : _luaData.RadioByModule.Count.ToString(CultureInfo.InvariantCulture)));
            return _luaData;
        }

        // "Pack 1 - 160th SOAR Det 1 - SAR 1" -> le squadron "160th SOAR Det 1" (Active s'il existe, sinon Init). null pour un vol hors "Pack".
        private Squad SquadOfFlight(Map_Flight f)
        {
            Match m = FlightSquadRx.Match(f.Name ?? "");
            if (!m.Success) return null;
            Squad found = null;
            foreach (CampaignSquad cs in _allSquads)
            {
                Squad s = cs.Active ?? cs.Init;
                if (s == null || !string.Equals(s.Name, m.Groups[1].Value, StringComparison.OrdinalIgnoreCase)) continue;
                if (string.Equals(cs.SideString, f.Side, StringComparison.OrdinalIgnoreCase)) return s;
                if (found == null) found = s;
            }
            return found;
        }

        // Indicatifs proposés : celui du squad, puis les indicatifs spéciaux du type d'avion (pays du squad), puis les standard.
        // Seuls les squads occidentaux en ont (scriptsMod n'en donne pas aux autres) : pour les autres la liste est vide, saisie libre.
        private List<string> CallsignChoices(Map_Flight f)
        {
            var list = new List<string>();
            Squad sq = SquadOfFlight(f);
            CampaignLuaData lua = LuaOf();
            if (sq == null || lua == null) return list;

            if (!string.IsNullOrWhiteSpace(sq.Callsign)) list.Add(sq.Callsign.Trim());

            bool specific = false;
            Dictionary<string, Dictionary<string, string>> byCountry;
            if (lua.SpecificCallnames != null && sq.Type != null && lua.SpecificCallnames.TryGetValue(sq.Type, out byCountry))
            {
                foreach (KeyValuePair<string, Dictionary<string, string>> kv in byCountry)
                {
                    if (!string.Equals(kv.Key, sq.Country, StringComparison.OrdinalIgnoreCase)) continue;
                    foreach (KeyValuePair<string, string> e in kv.Value.Where(x => { int id; return int.TryParse(x.Key, out id) && id >= 9; }).OrderBy(x => int.Parse(x.Key)))
                    {
                        list.Add(e.Value);
                        specific = true;
                    }
                }
            }

            if (list.Count > 0 || specific)
            {
                bool awacs = sq.Tasks != null && sq.Tasks.ContainsKey("AWACS") && sq.Tasks["AWACS"];
                bool tanker = sq.Tasks != null && sq.Tasks.ContainsKey("Refueling") && sq.Tasks["Refueling"];
                string cat = awacs ? "AWACS" : tanker ? "tanker" : "generic";
                List<string> std;
                if (lua.CallsignWest != null && lua.CallsignWest.TryGetValue(cat, out std)) list.AddRange(std);
            }

            return list.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        // Numéro de queue libre pris au hasard dans la plage "sidenumber" du squad (min..max). Tous pris : un doublon est toléré.
        // null = le squad n'a pas de plage.
        private string SuggestTail(Map_Flight f, List<UnitRow> rows, UnitRow self)
        {
            Squad sq = SquadOfFlight(f);
            if (sq == null || sq.SideNumberMax <= 0 || sq.SideNumberMax < sq.SideNumberMin) return null;

            var used = new HashSet<string>();
            if (_missionData != null)
            {
                foreach (Map_Flight q in _missionData.Flights)
                {
                    if (q == f) continue;
                    object uo;
                    var us = q.Source != null && q.Source.TryGetValue("units", out uo) ? uo as Dictionary<string, object> : null;
                    if (us == null) continue;
                    foreach (object u in us.Values)
                    {
                        var d = u as Dictionary<string, object>;
                        object v;
                        if (d != null && d.TryGetValue("onboard_num", out v)) used.Add(TextOf(v));
                    }
                }
            }
            foreach (KeyValuePair<string, List<UnitRow>> kv in _unitRows)
                if (kv.Key != f.Name) foreach (UnitRow r in kv.Value) used.Add(r.Onboard);
            foreach (UnitRow r in rows)
                if (r != self && r.Onboard != "") used.Add(r.Onboard);

            // 1) libre dans toute la mission ; 2) sinon libre dans CE vol (au moins pas de doublon entre ses avions) ; 3) sinon n'importe lequel
            var inFlight = new HashSet<string>(rows.Where(r => r != self && r.Onboard != "").Select(r => r.Onboard));
            var free = new List<int>();
            var freeHere = new List<int>();
            for (int n = sq.SideNumberMin; n <= sq.SideNumberMax; n++)
            {
                string s = n.ToString(CultureInfo.InvariantCulture);
                if (!used.Contains(s)) free.Add(n);
                if (!inFlight.Contains(s)) freeHere.Add(n);
            }

            int pick;
            if (free.Count > 0) pick = free[_rng.Next(free.Count)];
            else if (freeHere.Count > 0) pick = freeHere[_rng.Next(freeHere.Count)];
            else pick = _rng.Next(sq.SideNumberMin, sq.SideNumberMax + 1);

            if (free.Count == 0 && _lblStatus != null)
                _lblStatus.Text = f.Title + ": the range " + sq.SideNumberMin + "-" + sq.SideNumberMax + " of " + sq.Name + " has no free number left in the mission, " + (freeHere.Count > 0 ? "a number used by another flight was taken." : "a duplicate was taken.");
            return pick.ToString(CultureInfo.InvariantCulture);
        }

        // Fréquence "flight" écrite dans le fichier (0 si absente)
        private double FlightFreq(Map_Flight f)
        {
            object v;
            double d;
            if (f.Source != null && f.Source.TryGetValue("DCE_FreqFlight", out v) && double.TryParse(TextOf(v), NumberStyles.Float, CultureInfo.InvariantCulture, out d)) return d;
            return 0;
        }

        private double ShownFreq(Map_Flight f)
        {
            double d;
            return _pendingFreq.TryGetValue(f.Name, out d) ? d : FlightFreq(f);
        }

        // Plages radio de l'avion (RadioByModule de DCEM_Function.lua) : { min, max } en MHz, null si inconnues
        private List<double[]> RadioRanges(Map_Flight f)
        {
            CampaignLuaData lua = LuaOf();
            List<double[]> r;
            if (lua != null && lua.RadioByModule != null && f.Type != null && lua.RadioByModule.TryGetValue(f.Type, out r)) return r;

            // une ligne de journal par type d'avion : pourquoi la plage radio est inconnue
            if (_radioLogged.Add(f.Type ?? ""))
            {
                string why = lua == null ? "scriptsMod data not loaded"
                    : lua.RadioByModule == null ? "RadioByModule is null (the loader does not fill it)"
                    : lua.RadioByModule.Count == 0 ? "RadioByModule is empty (RadioRanges missing in DCEM_Function.lua result, or RadioRangesInfo.error)"
                    : "type not in RadioByModule (no radio data for it, or only the default 100-156 range : those are skipped)";
                string near = lua == null || lua.RadioByModule == null ? "" : string.Join(", ", lua.RadioByModule.Keys.Where(k => (f.Type ?? "").Length >= 3 && k.StartsWith((f.Type ?? "").Substring(0, 3), StringComparison.OrdinalIgnoreCase)).Take(8));
                FormUtils.LogRegister("Map radio | flight radio range unknown for type '" + f.Type + "' | " + why + (near != "" ? " | similar types : " + near : "") + " | scriptsMod default range 100-156 used instead");
            }
            return StandardBands;
        }

        // Pas de donnée radio pour ce type d'avion : scriptsMod lui donne alors la radio par défaut 100-156 MHz (Make_Db_Frequency), on fait pareil
        private static readonly List<double[]> StandardBands = new List<double[]> { new[] { 100.0, 156.0 } };

        // Vrai si les plages viennent des données de scriptsMod (sinon c'est la plage par défaut 100-156)
        private bool RadioKnown(Map_Flight f)
        {
            CampaignLuaData lua = LuaOf();
            List<double[]> r;
            return lua != null && lua.RadioByModule != null && f.Type != null && lua.RadioByModule.TryGetValue(f.Type, out r);
        }

        private static string RangesText(List<double[]> ranges)
        {
            return string.Join(" / ", ranges.Select(x => x[0].ToString("0.###", CultureInfo.InvariantCulture) + "-" + x[1].ToString("0.###", CultureInfo.InvariantCulture)));
        }

        // Fréquences déjà prises par les AUTRES vols de la mission (radio du package et radio du vol, y compris les changements en attente)
        private Dictionary<double, string> UsedFreqs(Map_Flight f)
        {
            var used = new Dictionary<double, string>();
            if (_missionData == null) return used;
            foreach (Map_Flight q in _missionData.Flights)
            {
                if (q == f) continue;
                double a = ShownFreq(q), b = q.Freq;
                if (a > 0) used[Math.Round(a, 3)] = q.Name;
                if (b > 0) used[Math.Round(b, 3)] = q.Name;
            }
            return used;
        }

        // 0 = bon, 1 = déjà utilisée par un autre vol (accepté, signalé), 2 = refusée (message dit pourquoi)
        private int CheckFreq(Map_Flight f, double v, out string msg)
        {
            msg = "";
            if (v < 1 || v > 500) { msg = "Not a valid frequency."; return 2; }
            if (Math.Abs(v - 121.5) < 0.001 || Math.Abs(v - 243.0) < 0.001) { msg = "121.5 and 243.0 are the emergency frequencies."; return 2; }
            List<double[]> ranges = RadioRanges(f);
            if (ranges != null && ranges.Count > 0 && !ranges.Any(r => v >= r[0] - 0.0001 && v <= r[1] + 0.0001))
            { msg = "Outside the radio range of " + f.Type + " (" + RangesText(ranges) + " MHz)."; return 2; }
            string by;
            if (UsedFreqs(f).TryGetValue(Math.Round(v, 3), out by)) { msg = "Already used by " + by + "."; return 1; }
            return 0;
        }

        // Même contrôle, avec une ligne de journal quand la valeur est refusée ou signalée
        private int CheckFreqLogged(Map_Flight f, double v, out string msg)
        {
            int st = CheckFreq(f, v, out msg);
            if (st != 0) FormUtils.LogRegister("Map radio | flight radio check | " + f.Name + " | " + v.ToString(CultureInfo.InvariantCulture) + " MHz | " + (st == 2 ? "REFUSED" : "warning") + " | " + msg);
            return st;
        }

        // Fréquence libre au hasard (pas de 0.05) dans la plage radio qui contient la fréquence actuelle (sinon la plus large). 0 = rien trouvé.
        private double RandomFreq(Map_Flight f)
        {
            List<double[]> ranges = RadioRanges(f);
            if (ranges == null || ranges.Count == 0) { FormUtils.LogRegister("Map radio | random frequency | " + f.Name + " | no radio range for '" + f.Type + "'"); return 0; }

            double cur = ShownFreq(f);
            double[] r = ranges.FirstOrDefault(x => cur >= x[0] && cur <= x[1]) ?? ranges.OrderByDescending(x => x[1] - x[0]).First();
            const double step = 0.05;
            double min = Math.Ceiling(r[0] / step) * step, max = Math.Floor(r[1] / step) * step;
            int count = (int)Math.Floor((max - min) / step);
            if (count <= 0) return 0;

            Dictionary<double, string> used = UsedFreqs(f);
            for (int i = 0; i < 300; i++)
            {
                double v = Math.Round(min + _rng.Next(0, count + 1) * step, 2);
                if (Math.Abs(v - 121.5) < 0.001 || Math.Abs(v - 243.0) < 0.001) continue;
                if (used.ContainsKey(Math.Round(v, 3))) continue;
                FormUtils.LogRegister("Map radio | random frequency | " + f.Name + " | " + v.ToString(CultureInfo.InvariantCulture) + " MHz in range " + r[0].ToString(CultureInfo.InvariantCulture) + "-" + r[1].ToString(CultureInfo.InvariantCulture));
                return v;
            }
            FormUtils.LogRegister("Map radio | random frequency | " + f.Name + " | no free frequency found in " + r[0].ToString(CultureInfo.InvariantCulture) + "-" + r[1].ToString(CultureInfo.InvariantCulture));
            return 0;
        }

        private Button MakeDice(string tip, bool enabled)
        {
            var b = new Button { Text = "\U0001F3B2", Size = new Size(24, 22), Margin = new Padding(2, 1, 0, 1), Enabled = enabled, FlatStyle = FlatStyle.Flat, Font = FSym };
            b.FlatAppearance.BorderColor = Color.Silver;
            if (_toolsTip != null) _toolsTip.SetToolTip(b, tip);
            return b;
        }

        private TextBox MakeCell(string text, bool editable)
        {
            return new TextBox { Text = text, ReadOnly = !editable, Dock = DockStyle.Fill, Margin = new Padding(0, 1, 3, 1), Font = FCell };
        }

        private Button MakeCross(string tip, bool enabled)
        {
            var b = new Button { Text = "✕", Size = new Size(24, 22), Margin = new Padding(2, 1, 0, 1), Enabled = enabled, FlatStyle = FlatStyle.Flat, Font = FCross, ForeColor = Color.Firebrick };
            b.FlatAppearance.BorderColor = Color.Silver;
            if (_toolsTip != null) _toolsTip.SetToolTip(b, tip);
            return b;
        }

        // Le nombre d'avions change (flèches du compteur) : lignes ajoutées / retirées à la fin, écrites au clic sur Save modification
        private void AircraftCountChanged()
        {
            if (_syncNum || _shownFlight == null) return;

            Map_Flight f = _shownFlight;
            int n = (int)_numAircraft.Value;
            List<UnitRow> rows = RowsOf(f);

            while (rows.Count > n)
            {
                int idx = rows.FindLastIndex(r => !r.Player);
                if (idx < 0) break;
                rows.RemoveAt(idx);
            }
            while (rows.Count < n) rows.Add(NewRow(f, rows));

            RowsCountChanged(f, rows);
        }

        // Bouton "Delete..." : choix (ce vol / tout le package / désactiver), puis mise en attente jusqu'à Save modification.
        // Si le vol est déjà en attente, le bouton ("Undo") annule le lot entier.
        private void RemoveShownFlight()
        {
            Map_Flight f = _shownFlight;
            if (f == null || _missionFile == "") return;

            string tag;
            if (_pendingRemove.TryGetValue(f.Name, out tag))
            {
                foreach (string k in _pendingRemove.Where(kv => kv.Value == tag).Select(kv => kv.Key).ToList()) _pendingRemove.Remove(k);
                _lblStatus.Text = "Removal cancelled for " + f.Title + ".";
                UpdateFlightTools();
                UpdateSaveWpt();
                return;
            }

            Cursor = Cursors.WaitCursor;
            List<string> pack;
            try { pack = Map_Drawings.FlightsOfPackage(_missionFile, f.Name); }
            finally { Cursor = Cursors.Default; }

            string mode;
            List<string> names;
            if (!AskRemove(f, pack, out mode, out names)) return;

            _removeBatch++;
            string key = mode + "#" + _removeBatch;
            foreach (string n in names) _pendingRemove[n] = key;
            _lblStatus.Text = (mode == "disable" ? "Disable: " : "Delete: ") + names.Count + " flight" + (names.Count > 1 ? "s" : "") + " waiting. Press Save modification to write it in the mission (the back arrow cancels).";
            UpdateFlightTools();
            UpdateSaveWpt();
        }

        private bool AskRemove(Map_Flight f, List<string> pack, out string mode, out List<string> names)
        {
            mode = "";
            names = null;
            bool hasPack = pack.Count > 1;
            string choiceMode = "delete";
            List<string> choiceNames = new List<string> { f.Name };

            using (var dlg = new Form())
            {
                dlg.Text = "Delete flight";
                dlg.StartPosition = FormStartPosition.CenterParent;
                dlg.FormBorderStyle = FormBorderStyle.FixedDialog;
                dlg.MinimizeBox = false;
                dlg.MaximizeBox = false;
                dlg.ClientSize = new Size(560, 300);

                var r1 = new RadioButton { Text = "Delete this flight only", Location = new Point(14, 14), Size = new Size(520, 24), Checked = true };
                var r2 = new RadioButton { Text = "Delete the whole package (" + pack.Count + " flights, recovery flights included)", Location = new Point(14, 42), Size = new Size(520, 24), Enabled = hasPack };
                var r3 = new RadioButton { Text = "Disable this flight only (kept in the file, never activated: ids and names stay)", Location = new Point(14, 70), Size = new Size(520, 24) };
                var info = new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Location = new Point(14, 104), Size = new Size(532, 140), Font = new Font("Segoe UI", 9f) };
                var ok = new Button { Text = "OK", Location = new Point(376, 258), Size = new Size(80, 30), DialogResult = DialogResult.OK };
                var cancel = new Button { Text = "Cancel", Location = new Point(466, 258), Size = new Size(80, 30), DialogResult = DialogResult.Cancel };
                var note = new Label { Text = "Nothing is written until you press Save modification.", Location = new Point(14, 264), Size = new Size(350, 20), ForeColor = Color.DimGray };

                Action refresh = () =>
                {
                    choiceMode = r3.Checked ? "disable" : "delete";
                    choiceNames = r2.Checked ? pack : new List<string> { f.Name };
                    string msg;
                    bool good = Map_Drawings.RemoveFlights(_missionFile, choiceNames, r3.Checked, true, out msg);
                    info.Text = (good ? "" : "NOT POSSIBLE: ") + msg + "\r\n\r\nFlights: " + string.Join(", ", choiceNames);
                    ok.Enabled = good;
                };
                r1.CheckedChanged += (s, e) => refresh();
                r2.CheckedChanged += (s, e) => refresh();
                r3.CheckedChanged += (s, e) => refresh();

                dlg.Controls.AddRange(new Control[] { r1, r2, r3, info, note, ok, cancel });
                dlg.AcceptButton = ok;
                dlg.CancelButton = cancel;
                Cursor = Cursors.WaitCursor;
                try { refresh(); }
                finally { Cursor = Cursors.Default; }

                if (dlg.ShowDialog(this) != DialogResult.OK) return false;
            }
            mode = choiceMode;
            names = choiceNames;
            return true;
        }
    }
}
