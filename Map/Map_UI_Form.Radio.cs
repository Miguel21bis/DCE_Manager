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
    // Partie de Map_UI_Form (voir Map_UI_Form.cs) : presets radio du vol
    internal partial class Map_UI_Form : Form
    {
        private static string BandName(double mhz)
        {
            return mhz < 100 ? "LVHF" : (mhz < 225 ? "VHF" : "UHF");
        }

        // Presets lus dans le fichier (mis en cache) puis, s'il y en a, ceux qui attendent l'enregistrement
        private List<List<double>> PresetsOf(Map_Flight f, bool original)
        {
            List<List<double>> orig;
            if (!_presetOrig.TryGetValue(f.Name, out orig))
            {
                int n;
                List<int> first;
                orig = Map_Drawings.ReadPresetsOf(f.Source, out n, out first);
                _presetOrig[f.Name] = orig;
                _presetUnits[f.Name] = n;
                _presetFirst[f.Name] = first;
            }
            List<List<double>> pend;
            return !original && _pendingPresets.TryGetValue(f.Name, out pend) ? pend : orig;
        }

        // Montre ou cache la section selon le vol affiché (joueur / client, avec presets dans le fichier, niveau CampaignMaker)
        private void UpdateRadioSection(Map_Flight f)
        {
            if (_secRadio == null) return;

            List<List<double>> presets = null;
            bool show = f != null && f.IsPlayer && _missionFile != "" && ParamConf.UserLevel == UserLevel.CampaignMaker;
            if (show)
            {
                presets = PresetsOf(f, false);
                show = presets.Count > 0;
            }

            if (f != null && f.IsPlayer && !show)
                FormUtils.LogRegister("Map radio | presets section hidden for '" + f.Name + "' | " + (_missionFile == "" ? "no mission file" : (ParamConf.UserLevel != UserLevel.CampaignMaker ? "user level is not CampaignMaker" : "no Radio table found (see the 'presets read' line)")));
            _radioHas = show;
            if (show)
            {
                _secRadio.Summary = RadioSummary(f, presets);
                if (_radioTab >= presets.Count) _radioTab = 0;
                if (_secRadio.Expanded) RebuildRadioPanel(f);
            }
            PlaceOverlays();
        }

        // Plages permises pour une radio : celles de scriptsMod qui contiennent ses canaux actuels (sinon toutes)
        private List<double[]> PresetRanges(Map_Flight f, List<double> channels)
        {
            // Données radio par radio de scriptsMod (DCEM_Function.lua) : on prend la radio dont les plages contiennent le plus de canaux actuels
            CampaignLuaData lua = LuaOf();
            List<CampaignLuaData.ModuleRadio> radios;
            if (lua != null && lua.RadioDetail != null && f.Type != null && lua.RadioDetail.TryGetValue(f.Type, out radios) && radios.Count > 0 && !radios[0].IsDefault)
            {
                CampaignLuaData.ModuleRadio best = null;
                int bestN = 0;
                foreach (CampaignLuaData.ModuleRadio r in radios)
                {
                    int n = channels.Count(c => r.Ranges.Any(g => c >= g[0] - 0.0005 && c <= g[1] + 0.0005));
                    if (n > bestN) { best = r; bestN = n; }
                }
                if (best != null) return best.Ranges;
            }

            List<double[]> all = RadioRanges(f);
            if (all == null || all.Count == 0) return new List<double[]>();
            List<double[]> mine = all.Where(r => channels.Any(c => c >= r[0] && c <= r[1])).ToList();
            return mine.Count > 0 ? mine : all;
        }

        private string RadioSummary(Map_Flight f, List<List<double>> orig)
        {
            return string.Join("  -  ", orig.Select((r, i) => (r.Count > 0 ? BandName(r[0]) : "radio " + (i + 1)) + " " + r.Count + " ch")) + (_pendingPresets.ContainsKey(f.Name) ? "  (edited)" : "");
        }

        // Ligne de la liste (créée à la demande, puis réutilisée)
        private RadioRow GetRadioRow(int k)
        {
            while (_radioRows.Count <= k)
            {
                int i = _radioRows.Count, y = 4 + i * 24;
                var r = new RadioRow();
                r.Num = new Label { AutoSize = false, Location = new Point(2, y + 4), Size = new Size(22, 18), TextAlign = ContentAlignment.MiddleRight, ForeColor = Color.DimGray, Font = FRadioSmall, Text = i.ToString() };
                r.Box = new TextBox { Location = new Point(28, y + 1), Size = new Size(76, 22), Font = FRadio, Tag = i };
                r.Box.TextChanged += RadioBoxChanged;
                r.Box.Leave += RadioBoxLeave;
                r.Use = new Label { AutoSize = false, AutoEllipsis = true, Location = new Point(110, y + 4), Size = new Size(270, 18), Font = FRadioSmall, Tag = i };
                r.Use.Click += RadioUseClicked;
                _radioList.Controls.Add(r.Num);
                _radioList.Controls.Add(r.Box);
                _radioList.Controls.Add(r.Use);
                _radioRows.Add(r);
            }
            return _radioRows[k];
        }

        // Texte et couleur de l'usage : "▾ Tanker Arco131" quand on peut choisir dans la liste (crayon actif)
        private void SetRadioUse(RadioRow r, string use)
        {
            if (_radioPick)
            {
                r.Use.Text = "▾ " + (use == "" ? "choose..." : use);
                r.Use.ForeColor = use == "" ? Color.Gray : Color.FromArgb(40, 70, 120);
                r.Use.Cursor = Cursors.Hand;
            }
            else
            {
                r.Use.Text = use;
                r.Use.ForeColor = Color.FromArgb(40, 70, 120);
                r.Use.Cursor = Cursors.Default;
            }
            if (_toolsTip != null) _toolsTip.SetToolTip(r.Use, use == "" ? (_radioPick ? "Pick what this frequency is for: it sets the frequency." : "") : use);
        }

        private bool InRadioRange(double mhz)
        {
            return _radioRanges.Count == 0 || _radioRanges.Any(g => mhz >= g[0] - 0.0005 && mhz <= g[1] + 0.0005);
        }

        // Remplit le panneau : un bouton par radio, puis ses canaux avec, en face, à quoi sert la fréquence dans la mission (lu dans le briefing du vol)
        private void RebuildRadioPanel(Map_Flight f)
        {
            if (_radioPanel == null || _radioList == null) return;
            if (f == null || !f.IsPlayer || _missionFile == "" || !_radioHas) return;
            List<List<double>> orig = PresetsOf(f, true);
            List<List<double>> cur = PresetsOf(f, false);
            if (orig.Count == 0) return;
            if (_radioTab >= orig.Count) _radioTab = 0;

            _radioFill = true;
            _radioFlight = f;
            _radioOrig = orig;
            bool pencil = _chkMove != null && _chkMove.Checked;
            Color okC = SystemColors.Window, editedC = Color.FromArgb(255, 248, 200);
            List<int> first;
            int firstKey = _presetFirst.TryGetValue(f.Name, out first) && _radioTab < first.Count ? first[_radioTab] : 1;       // 1er canal : 1, ou 0 (MiG-21...)

            // boutons des radios
            while (_radioButtons.Count < orig.Count)
            {
                var b = new Button { FlatStyle = FlatStyle.Flat, Size = new Size(110, 24), Tag = _radioButtons.Count };
                b.Click += (s, e) => { _radioTab = (int)((Button)s).Tag; RebuildRadioPanel(_radioFlight); };
                _radioTabs.Controls.Add(b);
                _radioButtons.Add(b);
            }
            for (int i = 0; i < _radioButtons.Count; i++)
            {
                Button b = _radioButtons[i];
                b.Visible = i < orig.Count;
                if (i >= orig.Count) continue;
                string band = orig[i].Count > 0 ? BandName(orig[i][0]) : "";
                b.Text = "Radio " + (i + 1) + (band != "" ? " (" + band + ")" : "");
                b.Location = new Point(6 + i * 114, 5);
                b.Font = i == _radioTab ? FRadioBold : FRadio;
                b.BackColor = i == _radioTab ? Color.FromArgb(220, 230, 250) : SystemColors.Control;
            }

            // canaux de la radio choisie
            List<double> o = orig[_radioTab], c2 = cur[_radioTab];
            _radioRanges = PresetRanges(f, o);
            RadioUseTable uses = RadioUses();
            _radioPick = pencil && uses.Items.Any(it => InRadioRange(it.Key / 1000.0));
            for (int k = 0; k < o.Count; k++)
            {
                RadioRow r = GetRadioRow(k);
                r.Num.Text = (firstKey + k).ToString(CultureInfo.InvariantCulture);
                r.Box.ReadOnly = !pencil;
                r.Box.Text = c2[k].ToString("0.000", CultureInfo.InvariantCulture);
                r.Box.BackColor = Math.Abs(c2[k] - o[k]) > 0.0004 ? editedC : okC;
                SetRadioUse(r, UseOfFreq(uses, c2[k]));
                r.Num.Visible = r.Box.Visible = r.Use.Visible = true;
            }
            for (int k = o.Count; k < _radioRows.Count; k++)
                _radioRows[k].Num.Visible = _radioRows[k].Box.Visible = _radioRows[k].Use.Visible = false;

            int nUnits;
            _presetUnits.TryGetValue(f.Name, out nUnits);
            string info = "Applies to the " + nUnits + " aircraft of the flight that have radios. "
                + (_radioRanges.Count > 0 ? "Allowed: " + RangesText(_radioRanges) + " MHz." : "No range known for this radio.")
                + (pencil ? "" : " Click the pencil to edit.") + " The briefing text is not changed.";
            _radioInfo.Text = info;
            _radioInfo.Height = TextRenderer.MeasureText(info, _radioInfo.Font, new Size(Math.Max(200, _radioPanel.Width - 20), 0), TextFormatFlags.WordBreak).Height + 8;
            _radioWantH = _radioTabs.Height + o.Count * 24 + 10 + _radioInfo.Height + 2;

            _radioFill = false;
            PlaceOverlays();
        }

        // Une fréquence a été tapée : contrôle, mémorise dans _pendingPresets (écrit au Save), met à jour la couleur et l'usage affiché
        private void RadioBoxChanged(object sender, EventArgs e)
        {
            if (_radioFill || _radioFlight == null || _radioOrig == null) return;

            var tb = (TextBox)sender;
            int k = (int)tb.Tag;
            Map_Flight f = _radioFlight;
            List<double> o = _radioOrig[_radioTab];
            if (k >= o.Count) return;
            Color okC = SystemColors.Window, editedC = Color.FromArgb(255, 248, 200), badC = Color.FromArgb(255, 200, 200);

            double v;
            string txt = tb.Text.Trim().Replace(',', '.');
            if (!double.TryParse(txt, NumberStyles.Float, CultureInfo.InvariantCulture, out v) || v < 1 || v > 500
                || (Math.Abs(v - o[k]) > 0.0004 && !InRadioRange(v)))
            {
                tb.BackColor = badC;            // refusé : la valeur n'est pas retenue
                return;
            }

            List<List<double>> pend;
            if (!_pendingPresets.TryGetValue(f.Name, out pend))
            {
                pend = _radioOrig.Select(r => r.ToList()).ToList();
                _pendingPresets[f.Name] = pend;
            }
            pend[_radioTab][k] = v;
            if (pend.Select((r, i) => r.Select((x, j) => Math.Abs(x - _radioOrig[i][j]) < 0.0004).All(z => z)).All(z => z)) _pendingPresets.Remove(f.Name);   // revenu aux valeurs d'origine

            tb.BackColor = Math.Abs(v - o[k]) > 0.0004 ? editedC : okC;
            SetRadioUse(_radioRows[k], UseOfFreq(RadioUses(), v));
            _secRadio.Summary = RadioSummary(f, _radioOrig);
            UpdateSaveWpt();
        }

        // On quitte la case : la fréquence est toujours écrite avec 3 décimales (266.2 -> 266.200)
        private void RadioBoxLeave(object sender, EventArgs e)
        {
            var tb = (TextBox)sender;
            double v;
            if (!double.TryParse(tb.Text.Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out v) || v <= 0) return;
            string s = v.ToString("0.000", CultureInfo.InvariantCulture);
            if (tb.Text == s) return;
            _radioFill = true;          // même valeur : rien à recalculer
            tb.Text = s;
            _radioFill = false;
        }

        // Clic sur l'usage : liste des emplois du briefing qui entrent dans la plage de cette radio ; le choix remplit la fréquence
        private void RadioUseClicked(object sender, EventArgs e)
        {
            if (!_radioPick || _radioFlight == null) return;
            var lb = (Label)sender;
            int k = (int)lb.Tag;
            double now;
            double.TryParse(_radioRows[k].Box.Text.Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out now);

            var menu = new ContextMenuStrip { ShowImageMargin = false };
            foreach (KeyValuePair<long, string> it in RadioUses().Items)
            {
                double mhz = it.Key / 1000.0;
                if (!InRadioRange(mhz)) continue;
                var mi = new ToolStripMenuItem(it.Value + "    " + mhz.ToString("0.000", CultureInfo.InvariantCulture)) { Checked = Math.Abs(mhz - now) < 0.0005 };
                string fill = mhz.ToString("0.000", CultureInfo.InvariantCulture);
                mi.Click += (s2, e2) => _radioRows[k].Box.Text = fill;         // TextChanged fait le contrôle et l'enregistrement
                menu.Items.Add(mi);
            }
            if (menu.Items.Count == 0) { menu.Dispose(); return; }
            menu.Closed += (s2, e2) => BeginInvoke(new Action(menu.Dispose));
            menu.Show(lb, new Point(0, lb.Height));
        }

        private static string UseOfFreq(RadioUseTable uses, double mhz)
        {
            string s;
            return uses.ByFreq.TryGetValue((long)Math.Round(mhz * 1000), out s) ? s : "";
        }

        // Fréquences du briefing du vol, mises en cache tant que le texte ne change pas
        private RadioUseTable RadioUses()
        {
            if (_radioUsesSrc != _briefPart)
            {
                _radioUsesTable = ParseRadioUses(_briefPart);
                _radioUsesSrc = _briefPart;
            }
            return _radioUsesTable;
        }

        // Section "Communication:" du briefing : "ATC:   Da Nang   250.750 MHz   COMM Channel 2"  ->  250750 = "ATC Da Nang"
        // ByFreq : un usage par fréquence (deux emplois de la même fréquence, ex. deux bases : noms mis bout à bout).
        // Items : un par ligne du briefing, dans l'ordre, sans doublon (la liste de choix).
        private static RadioUseTable ParseRadioUses(string part)
        {
            var table = new RadioUseTable();
            string body = Map_Briefing.Section(part, "Communication:");
            if (body == "") return table;

            foreach (string raw in body.Split('\n'))
            {
                string l = raw.TrimEnd('\r').Trim();
                if (l == "" || l.StartsWith("Radio:", StringComparison.OrdinalIgnoreCase)) continue;

                string[] cells = Regex.Split(l, @"\s{2,}").Where(s => s != "").ToArray();
                if (cells.Length < 2) continue;

                double freq = 0;
                var words = new List<string> { cells[0].Replace(":", "") };         // "AWACS: EC-121" -> "AWACS EC-121"
                for (int i = 1; i < cells.Length; i++)
                {
                    Match mf = Regex.Match(cells[i], @"^(\d+(?:\.\d+)?)\s*MHz$", RegexOptions.IgnoreCase);
                    if (mf.Success) { freq = double.Parse(mf.Groups[1].Value, CultureInfo.InvariantCulture); continue; }
                    if (Regex.IsMatch(cells[i], @"Channel\s+\S+$", RegexOptions.IgnoreCase)) continue;
                    words.Add(cells[i]);
                }
                if (freq <= 0) continue;

                string label = Regex.Replace(string.Join(" ", words), @"\s+", " ").Trim();
                long key = (long)Math.Round(freq * 1000);
                string old;
                if (!table.ByFreq.TryGetValue(key, out old)) table.ByFreq[key] = label;
                else if (old.IndexOf(label, StringComparison.Ordinal) < 0) table.ByFreq[key] = old + " / " + label;
                if (!table.Items.Any(it => it.Key == key && it.Value == label)) table.Items.Add(new KeyValuePair<long, string>(key, label));
            }
            return table;
        }
    }
}
