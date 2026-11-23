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
    // Partie de Map_UI_Form (voir Map_UI_Form.cs) : fiche du vol choisi : waypoints, briefing, statistiques, menus
    internal partial class Map_UI_Form : Form
    {
        // Redessine les plans de vol (après un changement qui ne touche que l'affichage)
        private void RefreshPlanDrawings()
        {
            if (_missionData == null) return;
            _view.SetDrawings("plans", Map_Flights.BuildPlans(VisibleFlights()));
            _view.SetDrawingsVisible("plans", _chkPlans.Checked);
        }

        // Points SAR (x, y) de db_airbases pour le FARP "padName" : la base du même nom, sinon la plus proche à moins de 300 m
        private List<double[]> SarSpotsNear(string padName, double x, double y)
        {
            AirbaseInfo a;
            if (!_airbases.TryGetValue(padName ?? "", out a) || a.ParkAlertSAR == null || a.ParkAlertSAR.Count == 0)
            {
                a = null;
                double best = 300;
                foreach (AirbaseInfo b in _airbases.Values)
                {
                    if (b.ParkAlertSAR == null || b.ParkAlertSAR.Count == 0) continue;
                    double d = Math.Sqrt((b.X - x) * (b.X - x) + (b.Y - y) * (b.Y - y));
                    if (d <= best) { best = d; a = b; }
                }
            }
            return a == null ? null : a.ParkAlertSAR.Select(s => new[] { s.X, s.Y }).ToList();
        }

        private void AuditShownFlight()
        {
            Map_Flight f = _shownFlight;
            if (f == null || _missionFile == "") return;

            Cursor = Cursors.WaitCursor;
            string report;
            try { report = Map_Drawings.AuditFlight(_missionFile, f.Name); }
            finally { Cursor = Cursors.Default; }
            ShowReport("Flight audit - " + f.Title, report);
        }

        // Petite fenêtre de texte (lecture seule) avec un bouton pour copier
        private void ShowReport(string title, string text)
        {
            using (var dlg = new Form())
            {
                dlg.Text = title;
                dlg.StartPosition = FormStartPosition.CenterParent;
                dlg.Size = new Size(980, 680);
                dlg.MinimizeBox = false;

                var box = new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, WordWrap = false, Dock = DockStyle.Fill, Font = new Font("Consolas", 9.5f), Text = (text ?? "").Replace("\r\n", "\n").Replace("\n", "\r\n") };
                var bar = new Panel { Dock = DockStyle.Bottom, Height = 40 };
                var copy = new Button { Text = "Copy", Location = new Point(8, 6), Size = new Size(90, 28) };
                copy.Click += (s, e) => { try { Clipboard.SetText(box.Text); } catch { } };
                var close = new Button { Text = "Close", Location = new Point(104, 6), Size = new Size(90, 28), DialogResult = DialogResult.OK };
                bar.Controls.Add(copy);
                bar.Controls.Add(close);
                dlg.Controls.Add(box);
                dlg.Controls.Add(bar);
                dlg.AcceptButton = close;
                box.Select(0, 0);
                dlg.ShowDialog(this);
            }
        }

        // Bouton "retour" : tous les waypoints déplacés reprennent leur place
        private void ResetMovedWaypoints()
        {
            if (_missionData == null) return;

            foreach (Map_Flight f in _missionData.Flights.Where(q => q.Edited).ToList())
                f.ResetEdit();
            _unitRows.Clear();
            _pendingFreq.Clear();
            _pendingPresets.Clear();
            _presetOrig.Clear();
            _presetUnits.Clear();
            _presetFirst.Clear();
            _pendingRemove.Clear();
            if (_pendingCamp.Count > 0) { _pendingCamp.Clear(); LoadMissionDebugBox(_missionFile); }
            foreach (Map_Flight pf in _missionData.Flights) pf.PendingUnitPos = null;
            UpdateFlightTools();

            _view.SetDrawings("plans", Map_Flights.BuildPlans(VisibleFlights()));
            _view.SetDrawingsVisible("plans", _chkPlans.Checked);
            RefreshRadars();
            if (_shownFlight != null) ShowFlight(_shownFlight);
            UpdateSaveWpt();
        }

        // Clic sur un waypoint de la carte : on choisit son vol, puis le waypoint dans la liste
        private void OnWaypointClicked(string flightName, int index)
        {
            if (!_missionMode || _missionData == null) return;
            Map_Flight f = _missionData.Flights.FirstOrDefault(x => x.Name == flightName);
            if (f == null) return;

            SelectInList(new List<Map_Flight> { f });

            _tabs.SelectedTab = _tabFlights;
            if (_shownFlight != f) { ShowFlight(f); AfterFlightPicked(f, 0); }       // 0 : le waypoint choisi est celui du clic, pas celui par défaut

            _syncWp = true;
            _wpList.SelectedIndex = index < _wpList.Items.Count ? index : -1;
            _syncWp = false;
            ShowWaypoint(f, index);
        }

        private void ShowWaypoint(Map_Flight f, int index)
        {
            if (_wpCard == null) return;
            UpdateWpTools(f, index);
            if (f == null || index < 0 || index >= f.Waypoints.Count)
            {
                _wpCard.SetContent("Waypoint", "Click a waypoint on the map, or pick one in the list.", Color.Gray, null);
            }
            else
            {
                Map_Waypoint w = f.Waypoints[index];
                double start = _missionData == null ? 0 : _missionData.StartTime;
                _wpCard.SetContent("Waypoint " + index + (w.ShortName != "" ? "  -  " + w.ShortName : ""), f.Title, f.Color, Map_Flights.DescribeWaypoint(f, index, start));
            }
            FillWpEdit(f, index);
            ResizeFlightHost();
        }

        // ---- Édition en place dans la fiche du waypoint (voir Map_UI_Card) ----

        private void FillWpEdit(Map_Flight f, int index)
        {
            bool show = ParamConf.UserLevel == UserLevel.CampaignMaker && f != null && index >= 0 && index < f.Waypoints.Count && index < f.Times.Length;
            _wpCard.Editable = show;
            if (_cmbStart != null && show && index == 0)
            {
                _cmbStart.SelectedIndex = f.StartKind();         // -1 : type inconnu, la liste reste vide
                _cmbStart.Enabled = _chkMove != null && _chkMove.Checked;
            }
            if (!show) return;

            Map_Waypoint w = f.Waypoints[index];
            double start = _missionData == null ? 0 : _missionData.StartTime;
            _wpCard.EditTime = Map_Flights.Clock(start, f.Times[index]);
            _wpCard.EditAlt = Math.Round(Map_Units.AltVal(w.Alt)).ToString(CultureInfo.InvariantCulture);          // dans l'unité choisie (m ou ft)
            _wpCard.EditSpeed = Math.Round(Map_Units.SpeedVal(w.Speed)).ToString(CultureInfo.InvariantCulture);     // km/h ou kt
            _wpCard.LockSpeed = w.SpeedLocked;
            _wpCard.LockEta = w.EtaLocked;
            _wpCard.LockTot = f.IsFixed(index);
            _wpCard.LockEnabled = _chkMove != null && _chkMove.Checked;
            _wpCard.Invalidate();
        }

        // Type de départ choisi dans la liste
        private void StartKindPicked()
        {
            Map_Flight f = _shownFlight;
            if (f == null || _wpList.SelectedIndex != 0) return;

            string why;
            if (!f.SetStartType(_cmbStart.SelectedIndex, out why))
            {
                if (why != "") MessageBox.Show(why, "Start type");
                ShowWaypoint(f, 0);         // remet la liste sur le type réel
                return;
            }
            AfterWpEdit(f, 0);
        }

        // Heure du jour "HH:MM:SS" (ou "HH:MM") -> secondes depuis le début de la mission
        private bool TryParseEta(string text, out double rel)
        {
            rel = 0;
            string s = (text ?? "").Trim();
            if (s.Split(':').Length == 2) s += ":00";
            TimeSpan ts;
            if (!TimeSpan.TryParse(s, CultureInfo.InvariantCulture, out ts)) return false;

            double start = _missionData == null ? 0 : _missionData.StartTime;
            rel = ts.TotalSeconds - start;
            if (rel < 0) rel += 86400;
            return true;
        }

        // Valeur modifiée dans la fiche (champ : time / alt / speed)
        private void WpValueEdited(string field, string text)
        {
            if (_shownFlight == null) return;
            int i = _wpList.SelectedIndex;
            if (i < 0 || i >= _shownFlight.Waypoints.Count) return;
            Map_Flight f = _shownFlight;
            double v;

            switch (field)
            {
                case "speed":
                    if (!double.TryParse(text.Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out v) || v <= 0) { ShowWaypoint(f, i); return; }
                    f.EditWaypoint(i, Map_Units.SpeedToMs(v), double.NaN, double.NaN);
                    break;
                case "alt":
                    if (!double.TryParse(text.Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out v) || v < 0) { ShowWaypoint(f, i); return; }
                    f.EditWaypoint(i, double.NaN, double.NaN, Map_Units.AltToM(v));
                    break;
                case "time":
                    {
                        double rel;
                        if (!TryParseEta(text, out rel)) { ShowWaypoint(f, i); return; }
                        f.EditWaypoint(i, double.NaN, rel, double.NaN);
                        break;
                    }
            }
            AfterWpEdit(f, i);
        }

        // Cadenas / cases "locked" cliquées dans la fiche
        private void WpLockToggled(string which, bool on)
        {
            if (_shownFlight == null) return;
            int i = _wpList.SelectedIndex;
            if (i < 0 || i >= _shownFlight.Waypoints.Count) return;
            Map_Flight f = _shownFlight;

            if (which == "tot") f.SetAnchor(i, on);
            else if (which == "eta") f.SetEtaLocked(i, on);
            else if (which == "speed") f.SetSpeedLocked(i, on);
            AfterWpEdit(f, i);
        }

        private void AfterWpEdit(Map_Flight f, int i)
        {
            _view.ReplaceFlightDrawings(f.Name, Map_Flights.BuildPlans(new[] { f }));
            OnWaypointDropped(f.Name, i);       // fiche du vol, liste, fiche du waypoint, radars, bouton Save
        }

        // Onglet Briefing : la partie du texte de la mission qui concerne ce vol
        private void UpdateBriefing(Map_Flight f)
        {
            if (_briefBox == null) return;

            _briefPart = "";
            _btnDatacard.Enabled = f != null;

            Map_MissionData d = _missionData;
            string general = d == null ? "" : d.BriefingGeneral;
            string sideText = d == null || f == null ? "" : (f.Side == "red" ? d.BriefingRed : d.BriefingBlue);
            string sideName = f != null && f.Side == "red" ? "red" : "blue";
            int mode = _briefMode == null ? 0 : _briefMode.SelectedIndex;

            if (mode == 2)
            {
                _briefBox.Text = general != "" ? general : "This mission has no description text.";
                if (f != null) _briefPart = Map_Briefing.PartFor(f, sideText + "\n" + general);
                return;
            }

            if (f == null)
            {
                _briefBox.Text = "Select a flight to see its briefing.";
                return;
            }

            // La partie du vol peut être dans le texte du camp ou dans la description générale
            _briefPart = Map_Briefing.PartFor(f, sideText + "\n" + general);

            if (mode == 1)
            {
                _briefBox.Text = sideText != "" ? sideText : "This mission has no " + sideName + " briefing text.";
                return;
            }

            if (_briefPart != "")
            {
                _briefBox.Text = _briefPart;
                return;
            }

            // Pas de partie pour ce vol (vol IA) : sa ligne dans l'Air Tasking Order, si elle existe
            string ato = Map_Briefing.AtoLine(f, sideText + "\n" + general);
            _briefBox.Text = "No individual briefing for this flight (only the player flights have one)."
                + (ato != "" ? "\n\nAir tasking order:\n" + ato : "")
                + "\n\nUse the list above to see the side briefing or the mission description.";
        }

        private void OpenDatacard()
        {
            if (_shownFlight == null) return;
            try
            {
                double start = _missionData == null ? 0 : _missionData.StartTime;
                using (var dlg = new Map_UI_DatacardForm(Map_Datacard.Render(_shownFlight, start, _briefPart, _background != null ? _background.Proj : null), _shownFlight.Title))
                    dlg.ShowDialog(this);
            }
            catch (Exception ex)
            {
                FormUtils.LogRegister("Map_UI_Form | datacard : " + ex);
                MessageBox.Show("The datacard could not be made: " + ex.Message, "Datacard");
            }
        }

        private void ResizeFlightHost()
        {
            if (_flightHost == null) return;
            _flightHost.AutoScrollMinSize = new Size(0, (_flightInfo != null && _flightInfo.Visible ? _flightInfo.Height : 0) + (_toolsPanel != null ? _toolsPanel.Height : 0) + (_unitsPanel != null && _unitsPanel.Visible ? _unitsPanel.PreferredSize.Height : 0) + _wpTitle.Height + _wpList.Height + 8 + _wpCard.Height + 16);
        }

        // Clic sur la carte -> même chose dans l'autre sens (liste vide = clic dans le vide : plus de sélection)
        private void OnFlightsClicked(List<string> names)
        {
            if (!_missionMode || _missionData == null) return;

            List<Map_Flight> fl = names.Select(n => _missionData.Flights.FirstOrDefault(x => x.Name == n)).Where(x => x != null).ToList();
            SelectInList(fl);

            if (fl.Count > 0) _tabs.SelectedTab = _tabFlights;
            ShowFlight(fl.Count == 0 ? null : fl[fl.Count - 1], fl.Count);
            AfterFlightPicked(fl.Count == 0 ? null : fl[fl.Count - 1], fl.Count);
        }

        // Un vol vient d'être choisi (liste ou carte) : météo repliée sur une ligne, presets radio ouverts en grand si c'est un vol joueur / client,
        // et le waypoint utile déjà sélectionné. Pas dans ShowFlight : celui-ci est aussi rappelé après un Save, on ne veut pas replier / rouvrir à ce moment.
        private void AfterFlightPicked(Map_Flight f, int selectedCount)
        {
            if (f == null) return;

            // Météo et presets radio restent comme l'utilisateur les a laissés (repliés au départ) : c'est à lui de les agrandir

            if (selectedCount == 1) SelectDefaultWaypoint(f);
        }

        // Le waypoint qu'on veut voir en premier : l'IP, sinon la station (CAP, orbite...), sinon le seul waypoint hors terrain
        // (intercepteur, SAR...), sinon la cible. -1 = aucun.
        private static int DefaultWaypoint(Map_Flight f)
        {
            List<Map_Waypoint> wps = f.Waypoints;
            int i = wps.FindIndex(w => w.Role == "station" && Map_Flights.RegexIp.IsMatch((w.Name ?? "").Trim()));
            if (i < 0) i = wps.FindIndex(w => w.Role == "station");
            if (i < 0)
            {
                List<int> open = Enumerable.Range(0, wps.Count).Where(k => wps[k].Role != "airfield").ToList();
                if (open.Count == 1) i = open[0];
            }
            if (i < 0) i = wps.FindIndex(w => w.Role == "target");
            return i;
        }

        private void SelectDefaultWaypoint(Map_Flight f)
        {
            int i = DefaultWaypoint(f);
            if (i < 0 || i >= _wpList.Items.Count) return;

            _syncWp = true;
            _wpList.SelectedIndex = i;
            _syncWp = false;
            _view.SetSelectedWaypoint(f.Name, i);
            ShowWaypoint(f, i);
        }

        private void ShowFlight(Map_Flight f, int selectedCount = 1)
        {
            if (_flightInfo == null) return;

            _shownFlight = f;
            UpdateFlightTools();
            double startTime = _missionData == null ? 0 : _missionData.StartTime;
            _syncWp = true;
            _wpList.Fill(f, startTime);
            _wpList.Visible = f != null;
            _wpTitle.Visible = f != null;
            UpdateBriefing(f);
            _syncWp = false;
            ShowWaypoint(f, -1);

            UpdateRadioSection(f);

            if (f == null)
            {
                _flightInfo.Visible = false;
                ResizeFlightHost();
                return;
            }

            // Ce qui était dans la fiche du vol posée sur la carte : camp, avions, durée, distance, contrôle du plan
            string sub = (f.Side ?? "").ToUpperInvariant() + "  -  " + f.Units + " aircraft" + (f.IsPlayer ? "  -  PLAYER" : (f.IsPackage ? "  -  PACKAGE" : "")) + (selectedCount > 1 ? "  -  +" + (selectedCount - 1) + " selected" : "");
            _flightSub = sub;
            _flightInfo.Visible = true;
            RefreshFlightInfoText();
        }

        private string _flightSub = "";

        // Texte de la fiche du vol (camp, avions, durée, distance, contrôle du plan) : refait aussi quand on change d'unités
        private void RefreshFlightInfoText()
        {
            Map_Flight f = _shownFlight;
            if (f == null || _flightInfo == null || !_flightInfo.Visible) return;

            double startTime = _missionData == null ? 0 : _missionData.StartTime;
            string stats = string.Join("    ", Map_Flights.Describe(f, startTime)
                .Where(r => r.Key == "Duration" || r.Key == "Distance" || r.Key == "Plan check" || r.Key == "Note")
                .Select(r => r.Key + ": " + (r.Value ?? "").Replace("\n", " ")));
            string text = _flightSub + (stats != "" ? "\n" + stats : "");

            _flightInfo.Text = text;
            _flightInfo.Height = TextRenderer.MeasureText(text, _flightInfo.Font, new Size(Math.Max(200, _flightHost.ClientSize.Width - 6), 0), TextFormatFlags.WordBreak).Height + 8;
            ResizeFlightHost();
        }

        // Bascule m / km / km/h <-> ft / Nm / kt : tout ce qui montre une valeur se redessine tout de suite
        private void UnitsChanged(object sender, EventArgs e)
        {
            UpdateUnitsButton();
            _view.Invalidate();                                           // règle, longueur des pistes
            if (_wpList != null) _wpList.Invalidate();                    // altitude / vitesse de chaque waypoint
            RefreshPlanDrawings();                                        // étiquettes des circuits (tanker)
            RefreshFlightInfoText();
            if (_shownFlight != null && _wpList != null) ShowWaypoint(_shownFlight, _wpList.SelectedIndex);
            if (_missionMode && _cardSquads != null && _selectedBase != "") UpdateBaseCard(_cardSquads, _cardReady, _cardReserve);
        }

        private Button _btnUnits;

        private void UpdateUnitsButton()
        {
            if (_btnUnits != null) _btnUnits.Text = Map_Units.Metric ? "Metric" : "Imperial";
        }

        private bool _statsHas;

        // Encart "Mission content" : chiffres comptés dans le .miz + éléments supprimés par DC_Prune (écrits par la fenêtre ScriptsMod) + modules requis
        private void ShowMissionStats(string miz)
        {
            if (_secStats == null) return;
            Map_StatsData d = Map_MissionStats.Load(miz);
            _statsHas = d.TotalGroups > 0 || d.Modules.Count > 0;
            if (!_statsHas) { PlaceOverlays(); return; }

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("Type            Groups   Units");
            Action<string, int[]> row = (n, v) => sb.AppendLine(n.PadRight(14) + v[0].ToString().PadLeft(7) + v[1].ToString().PadLeft(8));
            row("Planes", d.Plane);
            row("Helicopters", d.Heli);
            row("Ships", d.Ship);
            row("Vehicles", d.Vehicle);
            row("Statics", d.Static);
            sb.AppendLine("Total".PadRight(14) + d.TotalGroups.ToString().PadLeft(7) + d.TotalUnits.ToString().PadLeft(8));
            sb.AppendLine();
            if (d.Pruned >= 0)
            {
                sb.AppendLine("DC_Prune: " + d.Pruned + " removed, " + d.Kept + " kept");
                if (d.StatPlaneBefore >= 0 && d.StatPlaneAfter >= 0)
                    sb.AppendLine("Static planes " + d.StatPlaneBefore + " > " + d.StatPlaneAfter + ", static helicopters " + d.StatHeliBefore + " > " + d.StatHeliAfter);
            }
            else sb.AppendLine("DC_Prune: not recorded for this mission");
            if (d.Modules.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("Required modules (" + d.Modules.Count + "):");
                foreach (string mod in d.Modules) sb.AppendLine("  - " + mod);
            }

            _statsBox.Text = sb.ToString().TrimEnd();
            int lines = _statsBox.Text.Split('\n').Length;
            _statsWantH = lines * (_statsBox.Font.Height + 1) + 16;
            _secStats.Summary = d.Plane[0] + d.Heli[0] + " flights, " + (d.Vehicle[0] + d.Ship[0]) + " ground/sea groups";
            PlaceOverlays();
        }

        // Colonne de gauche posée sur la carte (bouton de gauche) : des sections empilées, chacune avec son bandeau.
        // Un clic sur le bandeau replie la section sur cette seule ligne. Les prochaines sections (presets radio, datalink) se placent ici, à la suite.
        private void PlaceOverlays()
        {
            if (_secSky == null || _sky == null || _tglLeft == null) return;

            bool on = _tglLeft.Checked;
            _secSky.Visible = on;
            if (!on) { _sky.Visible = false; if (_secStats != null) { _secStats.Visible = false; _statsPanel.Visible = false; } if (_secOpts != null) { _secOpts.Visible = false; _optsPanel.Visible = false; } if (_secRadio != null) _secRadio.Visible = false; if (_radioPanel != null) _radioPanel.Visible = false; return; }

            int y = 10;
            _secSky.Location = new Point(10, y);
            y += _secSky.Height + 2;

            _sky.Visible = _secSky.Expanded;
            if (_secSky.Expanded)
            {
                _sky.Location = new Point(10, y);
                y += _sky.Height + 6;
            }

            // Contenu de la mission : seulement quand le fichier a donné des chiffres
            if (_secStats != null)
            {
                bool showStats = _statsHas && _secStats.Expanded;        // pas _statsPanel.Visible : tant que la fiche n'est pas affichée, il reste faux
                _secStats.Visible = _statsHas;
                _statsPanel.Visible = showStats;
                if (_statsHas)
                {
                    _secStats.Location = new Point(10, y);
                    y += _secStats.Height + 2;
                    if (showStats)
                    {
                        _statsPanel.Location = new Point(10, y);
                        _statsPanel.Height = Math.Max(80, Math.Min(_statsWantH, _view.Height - y - 12));      // jamais plus haut que la carte : sinon la liste défile
                        y += _statsPanel.Height + 6;
                    }
                }
            }

            // Debug de la mission + plans de vol joueurs / IA
            if (_secOpts != null)
            {
                bool showOpts = _secOpts.Expanded;
                _secOpts.Visible = true;
                _optsPanel.Visible = showOpts;
                _secOpts.Location = new Point(10, y);
                y += _secOpts.Height + 2;
                if (showOpts)
                {
                    _optsPanel.Location = new Point(10, y);
                    y += _optsPanel.Height + 6;
                }
            }

            // Presets radio : seulement si un vol joueur / client est affiché (UpdateRadioSection décide de Visible)
            if (_secRadio != null && _radioPanel != null)
            {
                _secRadio.Visible = _radioHas;
                bool showPanel = _radioHas && _secRadio.Expanded;
                if (_radioHas)
                {
                    _secRadio.Location = new Point(10, y);
                    y += _secRadio.Height + 2;
                }
                _radioPanel.Visible = showPanel;
                if (showPanel)
                {
                    _radioPanel.Location = new Point(10, y);
                    _radioPanel.Height = Math.Max(120, Math.Min(_radioWantH, _view.Height - y - 12));      // jamais plus haut que la carte : sinon la liste défile
                    y += _radioPanel.Height + 6;
                }
            }
        }

        // ------------------------------------------------------------------
        //  Section "Radio presets" (colonne de gauche) : les canaux préréglés des radios de l'avion joueur / client.
        //  Le réglage vaut pour tout le groupe : écrit dans chaque avion du vol qui a une table Radio, au clic sur Save modification.
        // ------------------------------------------------------------------

        // Ligne du menu de choix de la carte (vols superposés sous le clic)
        private string FlightMenuLabel(string name)
        {
            Map_Flight f = _missionData == null ? null : _missionData.Flights.FirstOrDefault(x => x.Name == name);
            if (f == null) return name;
            return f.Title + (f.IsPlayer ? "   [PLAYER]" : (f.IsPackage ? "   [package]" : ""));
        }

        // Nom du waypoint pour le survol des éléments éclatés : "WP 3 - Assemble" (sinon son type)
        private string WaypointMenuLabel(string flight, int index)
        {
            Map_Flight f = _missionData == null ? null : _missionData.Flights.FirstOrDefault(x => x.Name == flight);
            if (f == null || index < 0 || index >= f.Waypoints.Count) return "";
            Map_Waypoint w = f.Waypoints[index];
            string n = w.ShortName;
            if (n == "") n = w.Type;
            return "WP " + index + (n != "" ? " - " + n : "");
        }
    }
}
