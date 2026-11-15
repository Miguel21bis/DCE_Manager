using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using DCE_Manager.Parameters;
using DCE_Manager.Utils;
using NLua;

namespace DCE_Manager
{
    // Un événement du debrief = une entrée de "DebriefEvents <n>.lua" (écrit par DEBRIEF_ExportMap.lua)
    internal class Map_Event
    {
        public int Id;
        public double T;                    // secondes depuis le début de la mission
        public string Kind = "";            // air_lost, ground_kill, ship_kill, static_kill, scenery_kill, eject, pilot_dead, pilot_sar, bomb
        public string Name = "", Flight = "", Side = "", Category = "", Type = "", Pilot = "";
        public bool HasPos;
        public double X, Y, Z;              // x = vers le NORD, y = vers l'EST (comme la carte de mission)
        public string PosSrc = "";          // eject, sar_approx, oob_start, scen, sar, impact : d'où vient la position
        public string Killer = "", KillerFlight = "", KillerSide = "", KillerType = "", KillerCategory = "";
        public bool Friendly;
        public string Target = "", Element = "", Status = "", Weapon = "", Cause = "", Info = "", Unit = "";
        public double Mass;
        public int MissionNo;
        public bool Current = true;         // faux = pilote resté d'une mission précédente
        public string Group = "";           // couche d'affichage (voir Map_Events_Form.GroupOf)
    }

    // Carte des événements du debrief : même fond et même projection que la carte de mission,
    // avec une icône par type d'événement et les plans de vol en filigrane.
    // Filtres : camp -> vol -> avion, plus un bouton par famille d'icônes.
    internal class Map_Events_Form : Form
    {
        private readonly string _campaign;
        private readonly string _file;
        private readonly bool _justGenerated;

        private Map_UI_View _view;
        private Map_Background _background;

        // Données
        private readonly List<Map_Event> _all = new List<Map_Event>();      // tout le fichier, trié par heure
        private List<Map_Event> _shown = new List<Map_Event>();             // ceux qui ont une position et passent les filtres, triés par couche (dessin)
        private Map_Event _selected;
        private string _title = "";
        private int _unmatched;
        private string _loadError = "";

        // Plans de vol (filigrane) : tronçons par vol, et camp de chaque vol
        private readonly Dictionary<string, List<Map_Drawing>> _planLegs = new Dictionary<string, List<Map_Drawing>>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> _flightSide = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private string _planInfo = "";

        // Joueurs (clients) de chaque vol, lus dans la table "players" du fichier (écrite par DEBRIEF_ExportMap.lua)
        private readonly Dictionary<string, List<string>> _flightPlayers = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> _unitPlayer = new Dictionary<string, string>(StringComparer.Ordinal);                  // avion -> joueur (fichier debrief)
        private readonly Dictionary<string, List<string>> _flightUnits = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);  // vol -> ses avions (fichier mission, même sans événement)
        private bool _flightsInFile;       // le fichier debrief contient les vols de SA mission (table "flights") : plans et avions viennent de là
        private readonly HashSet<string> _clientUnits = new HashSet<string>(StringComparer.Ordinal);                                      // avions en slot Client/Player dans le fichier mission

        // Filtres
        private string _selSide = "", _selFlight = "", _selUnit = "";
        private bool _syncFilter, _syncSel;
        private readonly Dictionary<string, bool> _groupOn = new Dictionary<string, bool>();

        // Contrôles
        private ComboBox _cmbSide, _cmbFlight, _cmbUnit;
        private CheckBox _chkKills, _chkPlans, _chkBg, _chkRuler;
        private ListView _list, _listNoPos;
        private TextBox _details;
        private Label _lblStatus, _lblNoPos, _lblList;
        private ToolTip _tip, _hoverTip;

        // Dessin
        private const float CellPx = 40f;          // les bombes et les décors se regroupent par case de 40 pixels (icônes deux fois plus grandes)
        private const float IconScale = 2f;        // taille des icônes : 2 = deux fois la taille d'origine
        private static readonly string[] Layers = { "bomb", "scenery", "static", "ground", "ship", "oldsar", "crew", "air" };    // du dessous au dessus
        private readonly Font _fontBold = new Font("Segoe UI", 9f, FontStyle.Bold);
        private readonly StringFormat _sfCenter = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        private readonly List<Hit> _hits = new List<Hit>();
        private string _hoverKey = "";
        private Point _downPt;

        private class Hit
        {
            public float X, Y, R;
            public Map_Event Ev;                // un seul événement ...
            public List<Map_Event> Items;       // ... ou un tas (bombes, décors)
        }

        private class Cluster
        {
            public readonly List<Map_Event> Items = new List<Map_Event>();
            public float SX, SY;
        }

        // Contour d'un avion vu de dessus, nez en haut (de -1 à +1), le même que sur la carte des bases
        private static readonly PointF[] JetShape =
        {
            new PointF(0f, -1f), new PointF(0.12f, -0.55f), new PointF(0.12f, -0.15f), new PointF(0.95f, 0.35f),
            new PointF(0.95f, 0.5f), new PointF(0.12f, 0.3f), new PointF(0.1f, 0.7f), new PointF(0.4f, 0.9f),
            new PointF(0.4f, 1f), new PointF(0f, 0.92f), new PointF(-0.4f, 1f), new PointF(-0.4f, 0.9f),
            new PointF(-0.1f, 0.7f), new PointF(-0.12f, 0.3f), new PointF(-0.95f, 0.5f), new PointF(-0.95f, 0.35f),
            new PointF(-0.12f, -0.15f), new PointF(-0.12f, -0.55f)
        };

        // eventsFile : chemin complet de "DebriefEvents <n>.lua" ; justGenerated : même sens que pour la carte de mission (lecture du .miz pour les plans de vol)
        public Map_Events_Form(string campaignName, string eventsFile, bool justGenerated = false)
        {
            _campaign = campaignName ?? "";
            _file = eventsFile ?? "";
            _justGenerated = justGenerated;
            Map_Web.UseCampaign(_campaign);         // le style de tuiles est propre à chaque campagne

            foreach (string g in Layers) _groupOn[g] = true;

            BuildUi();
            LoadEvents();
            FillFlights();

            Map_Background cached = null;
            try { cached = Map_Web.TryLoadCached(_campaign); }
            catch (Exception ex) { FormUtils.LogRegister("Map_Events_Form | fond web : " + ex.Message); }
            if (cached != null) UseBackground(cached);
            LoadBases();

            ApplyFilters();

            Shown += (s, e) =>
            {
                LoadPlans();
                FitAll();
            };
        }

        // ------------------------------------------------------------------
        //  Interface
        // ------------------------------------------------------------------

        private void BuildUi()
        {
            StartPosition = FormStartPosition.CenterParent;
            Rectangle area = Screen.PrimaryScreen.WorkingArea;
            Size = new Size((int)(area.Width * 0.9), (int)(area.Height * 0.9));
            MinimumSize = new Size(1000, 600);
            Text = "Events map - " + _campaign;

            _tip = new ToolTip { AutoPopDelay = 25000, InitialDelay = 250, ReshowDelay = 100, ShowAlways = true };
            _hoverTip = new ToolTip { InitialDelay = 0, ReshowDelay = 0, ShowAlways = true };
            Action<Control, string, string> hint = (c, title, text) => _tip.SetToolTip(c, title.ToUpper() + "\n" + text);

            // --- Carte ---
            _view = new Map_UI_View { Dock = DockStyle.Fill, ShowSilhouettes = false };
            _view.OverlayPaint += PaintEvents;
            _view.MouseDown += (s, e) => { _downPt = e.Location; };
            _view.MouseUp += (s, e) =>
            {
                if (e.Button == MouseButtons.Left && !_view.RulerEnabled && Math.Abs(e.X - _downPt.X) <= 4 && Math.Abs(e.Y - _downPt.Y) <= 4)
                    MapClicked(e.Location);
            };
            _view.MouseMove += (s, e) => { if (e.Button == MouseButtons.None) HoverAt(e.Location); };
            _view.MouseLeave += (s, e) => { _hoverKey = ""; _hoverTip.Hide(_view); };

            // --- Panneau de droite : liste des événements, puis ceux sans position, puis la fiche ---
            var right = new Panel { Dock = DockStyle.Right, Width = 420, Padding = new Padding(6) };

            _lblList = new Label { Dock = DockStyle.Top, Height = 22, Font = new Font("Segoe UI", 9f, FontStyle.Bold), TextAlign = ContentAlignment.BottomLeft, Text = "Events" };
            _list = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, GridLines = true, HideSelection = false, MultiSelect = false };
            _list.Columns.Add("Time", 60);
            _list.Columns.Add("Event", 130);
            _list.Columns.Add("Name", 200);
            _list.SelectedIndexChanged += (s, e) =>
            {
                if (_syncSel || _list.SelectedItems.Count == 0) return;
                SelectEvent(_list.SelectedItems[0].Tag as Map_Event, true);
            };
            hint(_list, "Events", "The events that pass the filters and have a position on the map. Click one: the map centers on it and the details are shown below.");

            var noPosPanel = new Panel { Dock = DockStyle.Bottom, Height = 150 };
            _lblNoPos = new Label { Dock = DockStyle.Top, Height = 22, Font = new Font("Segoe UI", 9f, FontStyle.Bold), TextAlign = ContentAlignment.BottomLeft, Text = "Position unknown" };
            _listNoPos = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, GridLines = true, HideSelection = false, MultiSelect = false };
            _listNoPos.Columns.Add("Time", 60);
            _listNoPos.Columns.Add("Aircraft", 190);
            _listNoPos.Columns.Add("Cause", 130);
            _listNoPos.SelectedIndexChanged += (s, e) =>
            {
                if (_syncSel || _listNoPos.SelectedItems.Count == 0) return;
                SelectEvent(_listNoPos.SelectedItems[0].Tag as Map_Event, false);
            };
            hint(_listNoPos, "Position unknown", "Aircraft lost with no position in the mission log: no ejection, no parachute seen. They can not be put on the map (nothing is invented). When the in-game tracker records the position of a kill, they will move to the map.");
            noPosPanel.Controls.Add(_listNoPos);
            noPosPanel.Controls.Add(_lblNoPos);

            _details = new TextBox { Dock = DockStyle.Bottom, Height = 190, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Font = new Font("Consolas", 9f), BackColor = Color.FromArgb(250, 251, 253) };
            hint(_details, "Details", "All the fields of the selected event (copyable).");

            // Ordre d'ajout : Fill, puis Bottom, puis Top (le dernier ajouté prend sa place en premier)
            right.Controls.Add(_list);
            right.Controls.Add(noPosPanel);
            right.Controls.Add(_details);
            right.Controls.Add(_lblList);

            // --- Bande de gauche : un bouton par famille d'icônes, puis plans de vol, fond, règle, cadrage ---
            var strip = new Panel { Dock = DockStyle.Left, Width = 38, BackColor = Color.FromArgb(243, 243, 243) };
            int sy = 6;
            Func<int> nextY = () => { int r = sy; sy += 38; return r; };

            AddKindToggle(strip, hint, nextY(), "air", "✈", "Aircraft lost", "Planes and helicopters lost: silhouette with a cross, blue or red = side of the lost aircraft. A PALE icon is an approximate position (where the crew landed, or the start position of the aircraft). Aircraft with no position at all are in the list 'Position unknown'.");
            AddKindToggle(strip, hint, nextY(), "ground", "■", "Vehicles destroyed", "Ground vehicles destroyed: tank, truck, infantry, SAM, gun or radar icon (chosen from the unit type), with a cross.");
            AddKindToggle(strip, hint, nextY(), "ship", "⚓", "Ships destroyed", "Ships destroyed: ship icon with a cross.");
            AddKindToggle(strip, hint, nextY(), "static", "◆", "Static objects destroyed", "Static objects destroyed (parked aircraft, crates, barrels, sandbags, buildings, trucks...): the icon follows the object type, with a cross.");
            AddKindToggle(strip, hint, nextY(), "scenery", "⌂", "Scenery destroyed", "Scenery objects destroyed: building, bridge, railway station, ship, tree... chosen from the object name. A GOLD icon is an object that belongs to a target of the target list (the debriefing links them). Many objects close together are grouped in a circle with their number: click it to zoom in.");
            AddKindToggle(strip, hint, nextY(), "crew", "☂", "Crew", "Parachute = ejection point. Person = pilot of this mission waiting for rescue (SAR, green). Black disc with a cross = pilot killed.");
            AddKindToggle(strip, hint, nextY(), "oldsar", "⟲", "Pilots from earlier missions", "Pilots still waiting for rescue from PREVIOUS missions (grey person). Their name tells the mission number.");
            AddKindToggle(strip, hint, nextY(), "bomb", "●", "Bomb impacts", "Bomb impacts (orange dot). Many impacts close together are grouped in a circle with their number: click it to zoom in.");
            sy += 16;

            _chkPlans = MakeToggle("➤", nextY());
            _chkPlans.Checked = true;
            _chkPlans.CheckedChanged += (s, e) => _view.SetDrawingsVisible("ships", _chkPlans.Checked);
            hint(_chkPlans, "Flight plans", "Show or hide the flight plans in the background (faint). The flights of the filter (side, flight) are drawn stronger. Read from the mission file of this debriefing.");
            strip.Controls.Add(_chkPlans);

            _chkBg = MakeToggle("▤", nextY());
            _chkBg.CheckedChanged += (s, e) => _view.ShowBackground = _chkBg.Checked;
            hint(_chkBg, "Web map", "Show or hide the web map (the one of the mission map). If it is greyed out, open the Map window of this campaign once to build it.");
            strip.Controls.Add(_chkBg);

            _chkRuler = MakeToggle("⟷", nextY());
            _chkRuler.CheckedChanged += (s, e) => _view.RulerEnabled = _chkRuler.Checked;
            hint(_chkRuler, "Ruler", "Distance between two points: click A, move, click B. Right-click or Esc = clear. Untick to go back to normal clicks.");
            strip.Controls.Add(_chkRuler);

            var btnFit = MakeToolButton("⛶", nextY());
            btnFit.Click += (s, e) => FitShown();
            hint(btnFit, "Fit", "Zoom on the events that are shown now (all of them if none is shown).");
            strip.Controls.Add(btnFit);

            // --- Barre du haut : filtres ---
            var top = new Panel { Dock = DockStyle.Top, Height = 40 };
            top.Controls.Add(new Label { Text = "Side:", Location = new Point(10, 12), AutoSize = true });
            _cmbSide = new ComboBox { Location = new Point(52, 8), Width = 90, DropDownStyle = ComboBoxStyle.DropDownList };
            _cmbSide.Items.AddRange(new object[] { "All", "Blue", "Red" });
            _cmbSide.SelectedIndex = 0;
            _cmbSide.SelectionChangeCommitted += (s, e) =>
            {
                _selSide = _cmbSide.SelectedIndex == 1 ? "blue" : (_cmbSide.SelectedIndex == 2 ? "red" : "");
                FillFlights();
                ApplyFilters();
            };
            hint(_cmbSide, "Side", "Show only the events of one side: what this side lost (and, with a flight chosen, what it destroyed).");
            top.Controls.Add(_cmbSide);

            top.Controls.Add(new Label { Text = "Flight:", Location = new Point(156, 12), AutoSize = true });
            _cmbFlight = new ComboBox { Location = new Point(204, 8), Width = 330, DropDownWidth = 560, DropDownStyle = ComboBoxStyle.DropDownList };
            _cmbFlight.SelectionChangeCommitted += (s, e) =>
            {
                FlightItem chosen = _cmbFlight.SelectedItem as FlightItem;
                _selFlight = (_cmbFlight.SelectedIndex <= 0 || chosen == null) ? "" : chosen.Key;
                FillUnits();
                ApplyFilters();
                if (_selected == null) _details.Text = FlightSummary();
            };
            hint(_cmbFlight, "Flight", "Show only the events of one whole flight (all its aircraft). Its flight plan is drawn stronger on the map.");
            top.Controls.Add(_cmbFlight);

            top.Controls.Add(new Label { Text = "Aircraft:", Location = new Point(548, 12), AutoSize = true });
            _cmbUnit = new ComboBox { Location = new Point(608, 8), Width = 240, DropDownStyle = ComboBoxStyle.DropDownList, Enabled = false };
            _cmbUnit.SelectionChangeCommitted += (s, e) =>
            {
                FlightItem chosen = _cmbUnit.SelectedItem as FlightItem;
                _selUnit = (_cmbUnit.SelectedIndex <= 0 || chosen == null) ? "" : chosen.Key;
                ApplyFilters();
                if (_selected == null) _details.Text = FlightSummary();
            };
            hint(_cmbUnit, "Aircraft", "Show only the events of one aircraft of the chosen flight (its loss, its ejection, its pilot).");
            top.Controls.Add(_cmbUnit);

            _chkKills = new CheckBox { Text = "+ what it destroyed", Location = new Point(864, 10), AutoSize = true, Checked = true };
            _chkKills.CheckedChanged += (s, e) => { if (_selFlight != "") ApplyFilters(); };
            hint(_chkKills, "What it destroyed", "With a flight or an aircraft chosen: also show the kills made by it (aircraft or ground units it shot down). Unticked: only what happened TO it.");
            top.Controls.Add(_chkKills);

            // --- Ligne d'état ---
            _lblStatus = new Label { Dock = DockStyle.Bottom, Height = 26, Padding = new Padding(8, 5, 8, 0) };

            Controls.Add(_view);
            Controls.Add(new Splitter { Dock = DockStyle.Right, Width = 6, MinSize = 250, MinExtra = 300 });
            Controls.Add(right);
            Controls.Add(strip);
            Controls.Add(top);
            Controls.Add(_lblStatus);

            KeyPreview = true;
            KeyDown += (s, e) => { if (e.KeyCode == Keys.Escape) _view.ClearRuler(); };
        }

        private void AddKindToggle(Panel strip, Action<Control, string, string> hint, int y, string group, string glyph, string title, string text)
        {
            CheckBox chk = MakeToggle(glyph, y);
            chk.Checked = true;
            chk.CheckedChanged += (s, e) => { _groupOn[group] = chk.Checked; ApplyFilters(); };
            hint(chk, title, text);
            strip.Controls.Add(chk);
        }

        // Les symboles de la police se posent un peu bas dans le bouton : on les dessine nous-mêmes, remontés de 2 pixels
        private static void DrawGlyph(Control c, PaintEventArgs e, string symbol)
        {
            var rect = new Rectangle(0, -2, c.Width, c.Height);
            Color color = c.Enabled ? c.ForeColor : SystemColors.GrayText;
            TextRenderer.DrawText(e.Graphics, symbol, c.Font, rect, color,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.NoClipping | TextFormatFlags.SingleLine);
        }

        private static CheckBox MakeToggle(string symbol, int y)
        {
            var chk = new CheckBox
            {
                Text = "",
                Appearance = Appearance.Button,
                Font = new Font("Segoe UI Symbol", 15f),
                Location = new Point(3, y),
                Size = new Size(32, 32),
                FlatStyle = FlatStyle.Flat,
                TabStop = false
            };
            chk.Paint += (s, e) => DrawGlyph(chk, e, symbol);
            chk.FlatAppearance.BorderColor = Color.Silver;
            chk.FlatAppearance.CheckedBackColor = Color.Gold;
            return chk;
        }

        private static Button MakeToolButton(string symbol, int y)
        {
            var b = new Button
            {
                Text = "",
                Font = new Font("Segoe UI Symbol", 15f),
                Location = new Point(3, y),
                Size = new Size(32, 32),
                FlatStyle = FlatStyle.Flat,
                TabStop = false
            };
            b.Paint += (s, e) => DrawGlyph(b, e, symbol);
            b.FlatAppearance.BorderColor = Color.Silver;
            return b;
        }

        // ------------------------------------------------------------------
        //  Lecture des données
        // ------------------------------------------------------------------

        private static string Str(LuaTable t, string key)
        {
            object v = t[key];
            return v == null ? "" : Convert.ToString(v, CultureInfo.InvariantCulture);
        }

        private static bool Num(LuaTable t, string key, out double d)
        {
            d = 0;
            object v = t[key];
            if (v == null || v is bool || v is string) return false;
            try { d = Convert.ToDouble(v, CultureInfo.InvariantCulture); return true; }
            catch { return false; }
        }

        private static string GroupOf(Map_Event e)
        {
            switch (e.Kind)
            {
                case "air_lost": return "air";
                case "ground_kill": return "ground";
                case "ship_kill": return "ship";
                case "static_kill": return "static";
                case "scenery_kill": return "scenery";
                case "eject":
                case "pilot_dead": return "crew";
                case "pilot_sar": return e.Current ? "crew" : "oldsar";
                case "bomb": return "bomb";
                default: return "";
            }
        }

        private void LoadEvents()
        {
            try
            {
                if (!File.Exists(_file)) { _loadError = "File not found: " + _file; return; }

                using (Lua lua = new Lua())
                {
                    lua.DoFile(_file);
                    LuaTable root = lua["debriefEvents"] as LuaTable;
                    LuaTable inc = root == null ? null : root["incidents"] as LuaTable;
                    if (inc == null) { _loadError = "No 'incidents' table in " + Path.GetFileName(_file); return; }

                    string mission = Str(root, "mission");
                    _title = "Mission " + mission + (Str(root, "campaign") != "" ? " - " + Str(root, "campaign") : "");
                    double un;
                    if (Num(root, "unmatched", out un)) _unmatched = (int)un;

                    // Joueurs : "unitPlayers" (avion -> joueur) et "players" (vol -> joueurs). Absents des anciens fichiers : la liste s'affiche alors sans noms.
                    LuaTable up = root["unitPlayers"] as LuaTable;
                    if (up != null)
                    {
                        foreach (object uk in up.Keys)
                        {
                            string unit = Convert.ToString(uk, CultureInfo.InvariantCulture);
                            string who = Convert.ToString(up[uk], CultureInfo.InvariantCulture);
                            if (string.IsNullOrEmpty(unit) || string.IsNullOrEmpty(who)) continue;
                            _unitPlayer[unit] = who;
                            AddPlayer(FlightOfUnit(unit), who);
                        }
                    }
                    LuaTable pl = root["players"] as LuaTable;
                    if (pl != null)
                    {
                        foreach (object fk in pl.Keys)
                        {
                            LuaTable names = pl[fk] as LuaTable;
                            if (names == null) continue;
                            foreach (object nk in names.Keys)
                                AddPlayer(Convert.ToString(fk, CultureInfo.InvariantCulture), Convert.ToString(names[nk], CultureInfo.InvariantCulture));
                        }
                    }

                    // Vols de la mission jouée : camp, plan (waypoints), avions et slots clients. Absent des anciens fichiers.
                    LuaTable fl = root["flights"] as LuaTable;
                    if (fl != null)
                    {
                        foreach (object fk in fl.Keys)
                        {
                            LuaTable f = fl[fk] as LuaTable;
                            string fname = Convert.ToString(fk, CultureInfo.InvariantCulture);
                            if (f == null || fname == "") continue;
                            _flightsInFile = true;
                            _flightSide[fname] = Str(f, "side");

                            LuaTable pts = f["points"] as LuaTable;
                            if (pts != null)
                            {
                                var leg = new Map_Drawing { Name = fname, Kind = "Line" };
                                var order = new List<KeyValuePair<double, object>>();
                                foreach (object pk in pts.Keys)
                                {
                                    try { order.Add(new KeyValuePair<double, object>(Convert.ToDouble(pk, CultureInfo.InvariantCulture), pk)); } catch { }
                                }
                                order.Sort((x, y) => x.Key.CompareTo(y.Key));
                                foreach (KeyValuePair<double, object> kv in order)
                                {
                                    LuaTable pt = pts[kv.Value] as LuaTable;
                                    double px, py;
                                    if (pt != null && Num(pt, "x", out px) && Num(pt, "y", out py)) leg.Points.Add(new double[] { px, py });
                                }
                                if (leg.Points.Count >= 2) _planLegs[fname] = new List<Map_Drawing> { leg };
                            }

                            LuaTable us = f["units"] as LuaTable;
                            if (us != null)
                            {
                                var names = new List<string>();
                                foreach (object uk in us.Keys)
                                {
                                    LuaTable u = us[uk] as LuaTable;
                                    string uname = u == null ? "" : Str(u, "name");
                                    if (uname == "") continue;
                                    names.Add(uname);
                                    if (Str(u, "client") == "True" || Str(u, "client") == "true") _clientUnits.Add(uname);
                                }
                                if (names.Count > 0) _flightUnits[fname] = names;
                            }
                        }
                        if (_flightsInFile) _planInfo = "Flight plans: from this debriefing.";
                    }

                    foreach (object key in inc.Keys)
                    {
                        LuaTable t = inc[key] as LuaTable;
                        if (t == null) continue;

                        var e = new Map_Event();
                        double d;
                        if (Num(t, "id", out d)) e.Id = (int)d;
                        if (Num(t, "t", out d)) e.T = d;
                        e.Kind = Str(t, "kind");
                        e.Name = Str(t, "name"); e.Flight = Str(t, "flight"); e.Side = Str(t, "side");
                        e.Category = Str(t, "category"); e.Type = Str(t, "type"); e.Pilot = Str(t, "pilot");
                        double x, y;
                        if (Num(t, "x", out x) && Num(t, "y", out y)) { e.HasPos = true; e.X = x; e.Y = y; }
                        if (Num(t, "z", out d)) e.Z = d;
                        e.PosSrc = Str(t, "posSrc");
                        e.Killer = Str(t, "killer"); e.KillerFlight = Str(t, "killerFlight"); e.KillerSide = Str(t, "killerSide");
                        e.KillerType = Str(t, "killerType"); e.KillerCategory = Str(t, "killerCategory");
                        e.Friendly = t["friendly"] is bool && (bool)t["friendly"];
                        e.Target = Str(t, "target"); e.Element = Str(t, "element"); e.Status = Str(t, "status");
                        e.Weapon = Str(t, "weapon"); e.Cause = Str(t, "cause"); e.Info = Str(t, "info"); e.Unit = Str(t, "unit");
                        if (Num(t, "mass", out d)) e.Mass = d;
                        if (Num(t, "missionNo", out d)) e.MissionNo = (int)d;
                        object cur = t["current"];
                        e.Current = !(cur is bool) || (bool)cur;
                        e.Group = GroupOf(e);
                        _all.Add(e);
                    }
                }

                _all.Sort((a, b) => { int c = a.T.CompareTo(b.T); return c != 0 ? c : a.Id.CompareTo(b.Id); });

                // Camp de chaque vol : lu dans les événements (victime ou tueur)
                foreach (Map_Event e in _all)
                {
                    if (e.Flight != "" && e.Side != "" && !_flightSide.ContainsKey(e.Flight)) _flightSide[e.Flight] = e.Side;
                    if (e.KillerFlight != "" && e.KillerSide != "" && !_flightSide.ContainsKey(e.KillerFlight)) _flightSide[e.KillerFlight] = e.KillerSide;
                }
            }
            catch (Exception ex)
            {
                _loadError = "The events file could not be read: " + ex.Message;
                FormUtils.LogRegister("Map_Events_Form | lecture : " + ex);
            }
        }

        // Bases de la campagne (repères) : mêmes données que la carte de mission
        private void LoadBases()
        {
            var markers = new List<Map_Base>();
            try
            {
                var loaded = new Parser_db_airbases().Load_db_airbases(_campaign);
                foreach (KeyValuePair<string, AirbaseInfo> kv in loaded)
                {
                    AirbaseInfo a = kv.Value;
                    bool placed = !(a.X == 0 && a.Y == 0) && Math.Abs(a.X) < 1e7 && Math.Abs(a.Y) < 1e7;
                    if (!placed) continue;

                    markers.Add(new Map_Base
                    {
                        Name = kv.Key,
                        X = a.X,
                        Y = a.Y,
                        Side = a.Side,
                        Inactive = a.Inactive,
                        IsFarp = kv.Key.IndexOf("FARP", StringComparison.OrdinalIgnoreCase) >= 0,
                        Runways = a.Runways == null ? new List<Map_Runway>()
                            : a.Runways.Select(rw => new Map_Runway { Hdg = rw.Hdg, Length = rw.Length, X = rw.X, Y = rw.Y }).ToList()
                    });
                }
                Map_RunwayData.Fill(_campaign, markers, _background != null ? _background.Proj : null);
            }
            catch (Exception ex)
            {
                FormUtils.LogRegister("Map_Events_Form | bases : " + ex.Message);
            }
            _view.SetBases(markers);
        }

        private void UseBackground(Map_Background bg)
        {
            _background = bg;
            _view.SetBackground(bg);
            try { Map_RunwayData.SetMeasured(Map_RunwayData.TheatreOf(_campaign), bg.AnchorRule); }
            catch (Exception ex) { FormUtils.LogRegister("Map_Events_Form | règle des pistes : " + ex.Message); }
            _view.ShowBackground = true;
            _chkBg.Checked = true;
        }

        // Plans de vol de la mission en filigrane (lus dans le .miz, comme la carte de mission)
        private void LoadPlans()
        {
            // Fichier récent : les vols de la mission jouée sont déjà dans le fichier debrief (le .miz actuel est celui d'une AUTRE mission)
            if (_flightsInFile) { RebuildPlans(); UpdateStatus(); return; }

            Cursor = Cursors.WaitCursor;
            try
            {
                Map_MissionData data = Map_Drawings.LoadMission(_campaign, _justGenerated);
                if (data.Error != "") _planInfo = "Flight plans: " + data.Error;

                foreach (Map_Flight f in data.Flights)
                    if (!string.IsNullOrEmpty(f.Name) && !_flightSide.ContainsKey(f.Name)) _flightSide[f.Name] = f.Side ?? "";

                _planLegs.Clear();
                foreach (Map_Drawing d in data.Plans)
                {
                    if (d.Kind != "Line" || d.Points.Count < 2) continue;
                    List<Map_Drawing> legs;
                    if (!_planLegs.TryGetValue(d.Name, out legs)) { legs = new List<Map_Drawing>(); _planLegs[d.Name] = legs; }
                    legs.Add(d);
                }
                // Vols sans plan dessiné (avions IA) : le plan vient des waypoints du vol. On relève aussi les avions de chaque vol (même sans événement).
                foreach (Map_Flight f in data.Flights)
                {
                    if (string.IsNullOrEmpty(f.Name)) continue;
                    if (!_planLegs.ContainsKey(f.Name) && f.Waypoints.Count >= 2)
                    {
                        var leg = new Map_Drawing { Name = f.Name, Kind = "Line" };
                        foreach (Map_Waypoint w in f.Waypoints) leg.Points.Add(new double[] { w.X, w.Y });
                        _planLegs[f.Name] = new List<Map_Drawing> { leg };
                    }
                    ReadFlightUnits(f);
                }
                if (_planLegs.Count == 0 && _planInfo == "") _planInfo = "Flight plans: none found in the mission file.";
            }
            catch (Exception ex)
            {
                _planInfo = "Flight plans not available: " + ex.Message;
                FormUtils.LogRegister("Map_Events_Form | plans de vol : " + ex);
            }
            finally { Cursor = Cursors.Default; }

            FillFlights();      // la liste des vols s'enrichit de ceux du fichier mission
            RebuildPlans();
            UpdateStatus();
        }

        // ------------------------------------------------------------------
        //  Filtres
        // ------------------------------------------------------------------

        // Clé de tri naturelle : "Pack 2" avant "Pack 10"
        private static string NatKey(string s)
        {
            return Regex.Replace(s ?? "", @"\d+", m => m.Value.PadLeft(6, '0'));
        }

        // Une ligne de la liste des vols : Key = nom du vol (sert au filtre), Text = ce qui s'affiche (avec les joueurs)
        private class FlightItem
        {
            public readonly string Key, Text;
            public FlightItem(string key, string text) { Key = key; Text = text; }
            public override string ToString() { return Text; }
        }

        private void FillFlights()
        {
            _syncFilter = true;
            _cmbFlight.Items.Clear();
            _cmbFlight.Items.Add(new FlightItem("", "All flights"));
            foreach (string f in _flightSide.Where(kv => _selSide == "" || kv.Value == _selSide).Select(kv => kv.Key).OrderBy(NatKey, StringComparer.Ordinal))
            {
                List<string> players;
                string text = _flightPlayers.TryGetValue(f, out players) ? f + "   [" + string.Join(", ", players) + "]" : f;
                _cmbFlight.Items.Add(new FlightItem(f, text));
            }
            _cmbFlight.SelectedIndex = 0;
            _selFlight = "";
            FillUnits();
            _syncFilter = false;
        }

        private static string UnitNameOf(Map_Event e)
        {
            return e.Kind == "pilot_sar" ? e.Unit : e.Name;
        }

        private static string FlightOfUnit(string unit)
        {
            return Regex.Replace(unit ?? "", @"-\d+$", "");
        }

        private void AddPlayer(string flight, string who)
        {
            if (string.IsNullOrEmpty(flight) || string.IsNullOrEmpty(who) || who == "nil") return;
            List<string> list;
            if (!_flightPlayers.TryGetValue(flight, out list)) { list = new List<string>(); _flightPlayers[flight] = list; }
            if (!list.Contains(who)) list.Add(who);
        }

        private List<string> PlayersOf(string flight)
        {
            List<string> list;
            return _flightPlayers.TryGetValue(flight ?? "", out list) ? list : new List<string>();
        }

        // Avions et slots clients d'un vol, lus dans le fichier mission
        private void ReadFlightUnits(Map_Flight f)
        {
            object o;
            var units = (f.Source != null && f.Source.TryGetValue("units", out o)) ? o as Dictionary<string, object> : null;
            if (units == null) return;

            var names = new List<string>();
            foreach (object uo in units.Values)
            {
                var u = uo as Dictionary<string, object>;
                if (u == null) continue;
                object n, sk;
                string name = u.TryGetValue("name", out n) ? Convert.ToString(n, CultureInfo.InvariantCulture) : "";
                if (name == "") continue;
                names.Add(name);
                string skill = u.TryGetValue("skill", out sk) ? Convert.ToString(sk, CultureInfo.InvariantCulture) : "";
                if (skill.Equals("Client", StringComparison.OrdinalIgnoreCase) || skill.Equals("Player", StringComparison.OrdinalIgnoreCase)) _clientUnits.Add(name);
            }
            if (names.Count > 0) _flightUnits[f.Name] = names;
        }

        // Tous les avions du vol : ceux du fichier mission + ceux qui ont un événement (même sans événement, un avion est listé)
        private List<string> UnitsOfFlight(string flight)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            List<string> fromMission;
            if (_flightUnits.TryGetValue(flight, out fromMission)) foreach (string n in fromMission) names.Add(n);
            foreach (Map_Event e in _all)
            {
                if (e.Flight == flight && UnitNameOf(e) != "") names.Add(UnitNameOf(e));
                if (e.KillerFlight == flight && e.Killer != "") names.Add(e.Killer);
            }
            return names.OrderBy(NatKey, StringComparer.Ordinal).ToList();
        }

        // "Pack 1 - 469th TFS - Escort 1-1   [Rayak_89 - Bobois]" ; "[client]" = slot joueur dont le nom n'est pas connu
        private string UnitLabel(string unit)
        {
            string who;
            if (_unitPlayer.TryGetValue(unit, out who)) return unit + "   [" + who + "]";
            return _clientUnits.Contains(unit) ? unit + "   [client]" : unit;
        }

        // Fiche du vol choisi (affichée dans les détails quand aucun événement n'est sélectionné)
        private string FlightSummary()
        {
            if (_selFlight == "") return "";
            var sb = new StringBuilder();
            sb.AppendLine(_selFlight);
            string side;
            if (_flightSide.TryGetValue(_selFlight, out side) && side != "") sb.AppendLine("Side:     " + side);
            List<string> pl = PlayersOf(_selFlight);
            if (pl.Count > 0) sb.AppendLine("Players:  " + string.Join(", ", pl));
            sb.AppendLine("Aircraft:");
            foreach (string n in UnitsOfFlight(_selFlight)) sb.AppendLine("  " + UnitLabel(n));
            return sb.ToString().TrimEnd();
        }

        private void FillUnits()
        {
            _cmbUnit.Items.Clear();
            _cmbUnit.Items.Add(new FlightItem("", "All aircraft"));
            _selUnit = "";
            if (_selFlight != "")
                foreach (string n in UnitsOfFlight(_selFlight)) _cmbUnit.Items.Add(new FlightItem(n, UnitLabel(n)));
            _cmbUnit.SelectedIndex = 0;
            _cmbUnit.Enabled = _selFlight != "";
        }

        private bool Match(Map_Event e)
        {
            if (e.Group != "" && !_groupOn[e.Group]) return false;

            bool hasSide = _selSide != "", hasFlight = _selFlight != "", hasUnit = _selUnit != "";
            if (!hasSide && !hasFlight && !hasUnit) return true;

            // ce qui est arrivé au camp / vol / avion choisi
            if ((!hasSide || e.Side == _selSide) && (!hasFlight || e.Flight == _selFlight) && (!hasUnit || UnitNameOf(e) == _selUnit)) return true;

            // ce qu'il a détruit (seulement quand un vol ou un avion est choisi)
            if (_chkKills.Checked && (hasFlight || hasUnit))
                return (!hasSide || e.KillerSide == _selSide) && (!hasFlight || e.KillerFlight == _selFlight) && (!hasUnit || e.Killer == _selUnit);

            return false;
        }

        private static int LayerOf(Map_Event e)
        {
            int i = Array.IndexOf(Layers, e.Group);
            return i < 0 ? 0 : i;
        }

        private void ApplyFilters()
        {
            if (_syncFilter) return;

            List<Map_Event> match = _all.Where(Match).ToList();                 // dans l'ordre du temps
            _shown = match.Where(e => e.HasPos).OrderBy(LayerOf).ToList();      // dans l'ordre de dessin
            List<Map_Event> noPos = match.Where(e => !e.HasPos).ToList();

            // Liste des événements placés
            _list.BeginUpdate();
            _list.Items.Clear();
            foreach (Map_Event e in match.Where(x => x.HasPos))
            {
                var it = new ListViewItem(FmtT(e.T)) { Tag = e };
                it.SubItems.Add(KindLabel(e));
                it.SubItems.Add(DisplayName(e));
                it.ForeColor = e.Side == "blue" ? Color.RoyalBlue : (e.Side == "red" ? Color.Firebrick : Color.Black);
                _list.Items.Add(it);
            }
            _list.EndUpdate();

            // Liste des pertes sans position
            _listNoPos.BeginUpdate();
            _listNoPos.Items.Clear();
            foreach (Map_Event e in noPos)
            {
                var it = new ListViewItem(FmtT(e.T)) { Tag = e };
                it.SubItems.Add(DisplayName(e));
                it.SubItems.Add(e.Killer != "" ? "shot down by " + (e.KillerType != "" ? e.KillerType : e.Killer) : (e.Cause != "" ? e.Cause : "no killer in the log"));
                it.ForeColor = e.Side == "blue" ? Color.RoyalBlue : (e.Side == "red" ? Color.Firebrick : Color.Black);
                _listNoPos.Items.Add(it);
            }
            _listNoPos.EndUpdate();
            _lblNoPos.Text = "Position unknown (" + noPos.Count + ")";
            _lblList.Text = "Events on the map (" + _list.Items.Count + ")";

            if (_selected != null && !match.Contains(_selected)) { _selected = null; _details.Text = ""; }

            RebuildPlans();
            UpdateStatus();
            _view.Invalidate();
        }

        private void UpdateStatus()
        {
            if (_loadError != "") { _lblStatus.Text = _loadError; return; }

            int placed = _all.Count(e => e.HasPos);
            string s = _title + "   |   " + _shown.Count + " shown of " + placed + " placed events (" + (_all.Count - placed) + " with no position)";
            if (_unmatched > 0) s += "   |   " + _unmatched + " log lines not matched";
            if (_planInfo != "") s += "   |   " + _planInfo;
            if (_background == null) s += "   |   No web map in the cache: open the Map window of this campaign once.";
            _lblStatus.Text = s;
        }

        // Plans de vol : tous en filigrane ; ceux du filtre (camp, vol) plus marqués, dessinés en dernier (donc dessus)
        private void RebuildPlans()
        {
            // Vol dont le plan est mis en avant : le vol choisi, sinon celui de l'événement sélectionné
            string focus = _selFlight;
            if (focus == "" && _selected != null) focus = _selected.Flight != "" ? _selected.Flight : _selected.KillerFlight;

            bool anySel = _selSide != "" || focus != "";
            var faint = new List<Map_Drawing>();
            var strong = new List<Map_Drawing>();

            foreach (KeyValuePair<string, List<Map_Drawing>> kv in _planLegs)
            {
                string side;
                if (!_flightSide.TryGetValue(kv.Key, out side)) side = "";

                bool isStrong = focus != "" ? string.Equals(kv.Key, focus, StringComparison.OrdinalIgnoreCase) : (_selSide != "" && side == _selSide);
                Color col = isStrong ? SideColor(side) : (anySel ? Color.FromArgb(35, 190, 190, 190) : Color.FromArgb(90, SideColor(side)));
                float width = isStrong ? 4.5f : (anySel ? 1f : 1.3f);

                foreach (Map_Drawing leg in kv.Value)
                {
                    // Le groupe "ships" de la vue sert ici : il n'a aucun effet de sélection (contrairement à "plans")
                    if (isStrong)
                    {
                        // Plan mis en avant : un liseré sombre dessous, le trait plein et épais dessus, et le numéro de chaque waypoint
                        var halo = new Map_Drawing { Name = kv.Key, Group = "ships", Kind = "Line", Thickness = width + 4f, Dashed = false, Line = Color.FromArgb(170, 0, 0, 0) };
                        halo.Points = leg.Points;
                        strong.Add(halo);
                        var line = new Map_Drawing { Name = kv.Key, Group = "ships", Kind = "Line", Thickness = width, Dashed = false, Line = Color.FromArgb(255, col) };
                        line.Points = leg.Points;
                        strong.Add(line);
                        for (int i = 0; i < leg.Points.Count; i++)
                        {
                            var num = new Map_Drawing { Name = kv.Key, Group = "ships", Kind = "Text", Line = Color.White, Text = "\u25CF" + i };
                            num.Points.Add(leg.Points[i]);
                            strong.Add(num);
                        }
                    }
                    else
                    {
                        var d = new Map_Drawing { Name = kv.Key, Group = "ships", Kind = "Line", Thickness = width, Dashed = true, Line = col };
                        d.Points = leg.Points;
                        faint.Add(d);
                    }
                }
            }
            faint.AddRange(strong);
            _view.SetDrawings("ships", faint);
            _view.SetDrawingsVisible("ships", _chkPlans.Checked);
        }

        // ------------------------------------------------------------------
        //  Cadrage
        // ------------------------------------------------------------------

        private void FitAll()
        {
            FitList(_all.Where(e => e.HasPos).ToList());
        }

        private void FitShown()
        {
            if (_shown.Count > 0) FitList(_shown);
            else FitAll();
        }

        private void FitList(List<Map_Event> list)
        {
            if (list.Count == 0) return;
            _view.ZoomToBox(list.Min(e => e.X), list.Max(e => e.X), list.Min(e => e.Y), list.Max(e => e.Y));
        }

        // ------------------------------------------------------------------
        //  Sélection, clic, survol
        // ------------------------------------------------------------------

        private void SelectEvent(Map_Event e, bool center)
        {
            _selected = e;
            _details.Text = e == null ? FlightSummary() : Describe(e);

            _syncSel = true;
            try
            {
                if (e != null)
                {
                    foreach (ListViewItem it in _list.Items)
                        if (ReferenceEquals(it.Tag, e)) { it.Selected = true; it.EnsureVisible(); break; }
                    if (e.HasPos) _listNoPos.SelectedItems.Clear();
                }
            }
            finally { _syncSel = false; }

            if (e != null && e.HasPos && center) _view.CenterOn(e.X, e.Y);
            RebuildPlans();
            _view.Invalidate();
        }

        private Hit HitAt(Point p)
        {
            for (int i = _hits.Count - 1; i >= 0; i--)
            {
                Hit h = _hits[i];
                float dx = h.X - p.X, dy = h.Y - p.Y;
                if (dx * dx + dy * dy <= (h.R + 2f) * (h.R + 2f)) return h;
            }
            return null;
        }

        private void MapClicked(Point p)
        {
            Hit h = HitAt(p);
            if (h == null)
            {
                _list.SelectedItems.Clear();
                SelectEvent(null, false);
                return;
            }
            if (h.Ev != null) { SelectEvent(h.Ev, false); return; }

            // Un tas : on zoome dessus
            _view.ZoomToBox(h.Items.Min(e => e.X), h.Items.Max(e => e.X), h.Items.Min(e => e.Y), h.Items.Max(e => e.Y));
        }

        private void HoverAt(Point p)
        {
            Hit h = HitAt(p);
            if (h == null) { if (_hoverKey != "") { _hoverKey = ""; _hoverTip.Hide(_view); } return; }

            string key = h.Ev != null ? "e" + h.Ev.Id + h.Ev.Kind : "c" + h.Items.Count + "_" + h.Items[0].Id;
            if (key == _hoverKey) return;
            _hoverKey = key;

            string text;
            if (h.Ev != null) text = Describe(h.Ev);
            else
            {
                bool bomb = h.Items[0].Group == "bomb";
                var sb = new StringBuilder();
                sb.AppendLine(h.Items.Count + (bomb ? " bomb impacts" : " scenery objects destroyed"));
                foreach (Map_Event e in h.Items.Take(12))
                    sb.AppendLine("  " + FmtT(e.T) + "  " + (bomb ? (e.Weapon != "" ? e.Weapon : "bomb") : DisplayName(e)));
                if (h.Items.Count > 12) sb.AppendLine("  ... and " + (h.Items.Count - 12) + " more");
                sb.Append("Click to zoom in.");
                text = sb.ToString();
            }
            _hoverTip.Show(text, _view, p.X + 16, p.Y + 16);
        }

        // ------------------------------------------------------------------
        //  Textes
        // ------------------------------------------------------------------

        private static string FmtT(double t)
        {
            int s = (int)Math.Round(t);
            return "T+" + (s / 60) + ":" + (s % 60).ToString("00");
        }

        private static string KindLabel(Map_Event e)
        {
            switch (e.Kind)
            {
                case "air_lost": return e.Category == "helicopter" ? "Helicopter lost" : "Aircraft lost";
                case "ground_kill": return "Vehicle destroyed";
                case "ship_kill": return "Ship destroyed";
                case "static_kill": return "Static destroyed";
                case "scenery_kill": return e.Target != "" ? "Target object destroyed" : "Scenery destroyed";
                case "eject": return "Pilot ejected";
                case "pilot_dead": return "Pilot killed";
                case "pilot_sar": return e.Current ? "Pilot waiting (SAR)" : "Pilot waiting (old)";
                case "bomb": return "Bomb impact";
                default: return e.Kind;
            }
        }

        private static string DisplayName(Map_Event e)
        {
            if (e.Kind == "scenery_kill") return e.Element != "" ? e.Element : (e.Type != "" ? e.Type : "scenery object");
            if (e.Kind == "bomb") return e.Weapon != "" ? e.Weapon : "bomb";
            return e.Name != "" ? e.Name : e.Type;
        }

        private static string PosText(Map_Event e)
        {
            if (!e.HasPos) return "unknown (not in the mission log)";
            switch (e.PosSrc)
            {
                case "eject": return "exact (ejection point)";
                case "sar_approx": return "approximate (where the pilot landed)";
                case "oob_start": return "approximate (start position of the aircraft)";
                case "scen": return "position of the scenery object";
                case "sar": return "position of the SAR zone";
                case "impact": return "impact point";
                default: return e.PosSrc;
            }
        }

        private string Describe(Map_Event e)
        {
            var sb = new StringBuilder();
            sb.AppendLine(FmtT(e.T) + "   " + KindLabel(e));
            string name = DisplayName(e);
            if (name != "") sb.AppendLine(name);

            if (e.Side != "") sb.AppendLine("Side:     " + e.Side);
            if (e.Type != "" && e.Type != name) sb.AppendLine("Type:     " + e.Type + (e.Category != "" ? " (" + e.Category + ")" : ""));
            if (e.Flight != "") sb.AppendLine("Flight:   " + e.Flight);
            if (e.Flight != "" && PlayersOf(e.Flight).Count > 0) sb.AppendLine("Players:  " + string.Join(", ", PlayersOf(e.Flight)));
            if (e.Unit != "" && e.Kind == "pilot_sar") sb.AppendLine("Aircraft: " + e.Unit);
            if (e.Pilot != "") sb.AppendLine("Pilot:    " + e.Pilot);
            string victimWho;
            if (_unitPlayer.TryGetValue(UnitNameOf(e), out victimWho) && victimWho != e.Pilot) sb.AppendLine("Player:   " + victimWho);
            if (e.Kind == "pilot_sar")
            {
                if (e.MissionNo > 0) sb.AppendLine("Mission:  " + e.MissionNo + (e.Current ? " (this one)" : " (earlier)"));
                if (e.Status != "") sb.AppendLine("Status:   " + e.Status);
            }
            if (e.Cause != "") sb.AppendLine("Cause:    " + e.Cause);

            if (e.Killer != "")
            {
                string by = e.Killer + (e.KillerType != "" ? " [" + e.KillerType + "]" : "");
                if (e.KillerSide != "") by += "  (" + e.KillerSide + ")";
                sb.AppendLine("Killed by: " + by);
                if (e.KillerFlight != "") sb.AppendLine("  flight:  " + e.KillerFlight);
                string killerWho;
                if (_unitPlayer.TryGetValue(e.Killer, out killerWho)) sb.AppendLine("  player:  " + killerWho);
            }
            if (e.Friendly) sb.AppendLine("FRIENDLY FIRE");
            if (e.Weapon != "") sb.AppendLine("Weapon:   " + e.Weapon + (e.Mass > 0 ? "  (" + e.Mass.ToString("0", CultureInfo.InvariantCulture) + " kg)" : ""));

            if (e.Target != "") sb.AppendLine("Target:   " + e.Target);
            if (e.Element != "" && e.Kind == "scenery_kill") sb.AppendLine("Element:  " + e.Element);
            if (e.Info != "" && e.Kind != "pilot_sar") sb.AppendLine("Info:     " + e.Info);
            else if (e.Info != "") sb.AppendLine("Zone:     " + e.Info);

            sb.AppendLine("Position: " + PosText(e));
            if (e.HasPos)
            {
                sb.AppendLine("  x (north) " + e.X.ToString("0", CultureInfo.InvariantCulture) + "   y (east) " + e.Y.ToString("0", CultureInfo.InvariantCulture)
                    + (e.Z != 0 ? "   alt " + Map_Units.Alt(e.Z) : ""));
            }
            return sb.ToString().TrimEnd();
        }

        // ------------------------------------------------------------------
        //  Dessin des icônes (appelé par la carte, après les bases et les plans)
        // ------------------------------------------------------------------

        private static Color SideColor(string side)
        {
            if (side == "blue") return Color.FromArgb(70, 140, 255);
            if (side == "red") return Color.FromArgb(235, 70, 70);
            return Color.FromArgb(180, 180, 180);
        }

        private void PaintEvents(Graphics g)
        {
            _hits.Clear();
            if (_shown.Count == 0) return;

            int w = _view.Width, h = _view.Height;
            var bombs = new Dictionary<long, Cluster>();
            var scenery = new Dictionary<long, Cluster>();

            // 1) Bombes et décors : regroupés par case de 40 pixels (sinon 600 icônes l'une sur l'autre)
            foreach (Map_Event e in _shown)
            {
                if (e.Group != "bomb" && e.Group != "scenery") continue;
                PointF p = _view.ToScreen(e.X, e.Y);
                if (p.X < -40 || p.Y < -40 || p.X > w + 40 || p.Y > h + 40) continue;

                var dict = e.Group == "bomb" ? bombs : scenery;
                long key = (long)(Math.Floor(p.X / CellPx) + 1000) * 100000L + (long)(Math.Floor(p.Y / CellPx) + 1000);
                Cluster c;
                if (!dict.TryGetValue(key, out c)) { c = new Cluster(); dict[key] = c; }
                c.Items.Add(e);
                c.SX += p.X;
                c.SY += p.Y;
            }
            DrawClusters(g, bombs, Color.FromArgb(255, 150, 40));
            DrawClusters(g, scenery, Color.FromArgb(200, 170, 110));

            // 2) Les autres événements, couche par couche
            foreach (Map_Event e in _shown)
            {
                if (e.Group == "bomb" || e.Group == "scenery") continue;
                PointF p = _view.ToScreen(e.X, e.Y);
                if (p.X < -40 || p.Y < -40 || p.X > w + 40 || p.Y > h + 40) continue;
                DrawEvent(g, e, p.X, p.Y);
                _hits.Add(new Hit { X = p.X, Y = p.Y, R = 20f, Ev = e });
            }

            // 3) L'événement choisi : anneau doré
            if (_selected != null && _selected.HasPos)
            {
                PointF p = _view.ToScreen(_selected.X, _selected.Y);
                using (var pen = new Pen(Color.Gold, 2.5f))
                    g.DrawEllipse(pen, p.X - 26, p.Y - 26, 52, 52);
            }
        }

        private void DrawClusters(Graphics g, Dictionary<long, Cluster> dict, Color color)
        {
            foreach (Cluster c in dict.Values)
            {
                float cx = c.SX / c.Items.Count, cy = c.SY / c.Items.Count;
                if (c.Items.Count == 1)
                {
                    DrawEvent(g, c.Items[0], cx, cy);
                    _hits.Add(new Hit { X = cx, Y = cy, R = 16f, Ev = c.Items[0] });
                    continue;
                }

                float r = 14f + (float)Math.Min(12.0, Math.Sqrt(c.Items.Count));
                using (var br = new SolidBrush(Color.FromArgb(205, color)))
                    g.FillEllipse(br, cx - r, cy - r, 2 * r, 2 * r);
                using (var pen = new Pen(Color.FromArgb(230, 30, 30, 30), 1.4f))
                    g.DrawEllipse(pen, cx - r, cy - r, 2 * r, 2 * r);
                g.DrawString(c.Items.Count.ToString(CultureInfo.InvariantCulture), _fontBold, Brushes.Black, new RectangleF(cx - r, cy - r, 2 * r, 2 * r), _sfCenter);
                _hits.Add(new Hit { X = cx, Y = cy, R = r, Items = c.Items });
            }
        }

        // Une croix blanche cernée de noir : "détruit"
        private static void DrawCross(Graphics g, float x, float y, float r)
        {
            using (var black = new Pen(Color.Black, 2.4f))
            {
                g.DrawLine(black, x - r, y - r, x + r, y + r);
                g.DrawLine(black, x - r, y + r, x + r, y - r);
            }
            using (var white = new Pen(Color.White, 1.2f))
            {
                g.DrawLine(white, x - r, y - r, x + r, y + r);
                g.DrawLine(white, x - r, y + r, x + r, y - r);
            }
        }

        // ------------------------------------------------------------------
        //  Icônes : la famille (char, camion, bâtiment, pont, gare...) est déduite du type et du nom de l'objet
        // ------------------------------------------------------------------

        private class IconRule
        {
            public Regex Rx;
            public string Icon;
            public IconRule(string pattern, string icon) { Rx = new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant); Icon = icon; }
        }

        // Véhicules (du plus précis au plus général)
        private static readonly IconRule[] VehicleRules =
        {
            new IconRule(@"\bsa-?\d|\bs-?(75|125|200|300|400)\b|patriot|\bhawk\b|roland|rapier|\btor\b|\bosa\b|strela|tunguska|2s6|\bsam\b|nasams|avenger|chaparral|igla|stinger|manpad|\bbuk\b|\bkub\b|9k\d|sa-?\d+", "sam"),
            new IconRule(@"zu-?23|zsu|flak|vulcan|gepard|\baaa\b|ks-?19|\d+ ?mm|bofors|m163|\bm42\b|s-?60\b|zpu|shilka|\bzu\b", "aaa"),
            new IconRule(@"radar|\bewr\b|\bsnr\b|p-?19|1l13|55g6|early warning|dog ear|flat face|spoon rest|squat eye|tin shield|stonehenge|search", "radar"),
            new IconRule(@"howitzer|m-?109|2s[13]\b|grad|bm-?21|mlrs|smerch|uragan|mortar|artillery|\bgun\b|cannon|d-?30|m-?198|m-?777|paladin|msta|akatsiya", "art"),
            new IconRule(@"\bt-?(55|62|64|72|80|90)|\bm-?1a?\d?\b|abrams|leopard|challenger|merkava|bmp|btr|bmd|m-?113|m-?2a|bradley|marder|\btank\b|armor|armour|\bapc\b|\bifv\b|\bmbt\b|pt-?76|scimitar|warrior|lav-?25|amx|\bm60\b|\bm48\b|type ?59", "tank"),
            new IconRule(@"infantry|soldier|rifle|\bak\b|rpg|sniper|trooper|militia|\binf\b|\bins\b|paratroop|mortar team|\bmg\b", "inf"),
            new IconRule(@"truck|ural|(?<![a-z])(?:zil|zis|gaz)|kamaz|kraz|hmmwv|humvee|hummer|jeep|land ?rover|tigr|m ?818|m ?939|hemtt|supply|fuel|\batz\b|ambulance|\bbus\b|\bcar\b|vehicle|trailer|lorry|\bm35\b|\bm54\b", "truck"),
        };

        // Bâtiments et décors
        private static readonly IconRule[] StructureRules =
        {
            new IconRule(@"bridge|\bpont\b|viaduct", "bridge"),
            new IconRule(@"rail|train|station|\bgare\b|locomotive|wagon|tram\b", "station"),
            new IconRule(@"\bship\b|boat|vessel|ferry|barge", "ship"),
            new IconRule(@"ammo_box|barrel|crate|pallet|container|sandbag|camouflage|fuel_tank|\bbox\b|drum", "crate"),
            new IconRule(@"blackgum|aspen|\bfir\b|\btree|pine|\boak|palm|bush|forest|spruce|birch|cedar|cypress|\belm\b|maple|umoe", "tree"),
            new IconRule(@"crane|warehouse|shelter|hangar|tower|house|\bhut\b|barrack|building|bunker|depot|storage|factory|plant|refiner|terminal|office|shed|church|school|hospital|mosque|silo|tank farm|ammo supply|command|garage|complex|villa|apartment|market|tent|aiplane|airplane", "building"),
        };

        private static readonly Regex RxStaticPlane = new Regex(@"^(F-?\d|A-?\d|AV-|MiG|Su-|Tu-|Il-|An-|B-\d|C-\d|E-\d|KC-|P-3|Yak|JF-|Mirage|F/A|vwv_|L-39)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static readonly Regex RxStaticHeli = new Regex(@"^(UH-|AH-|CH-|Mi-|Ka-|SA342|OH-|MH-|HH-)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private static string FirstMatch(IconRule[] rules, string text)
        {
            if (text == "") return null;
            foreach (IconRule r in rules)
                if (r.Rx.IsMatch(text)) return r.Icon;
            return null;
        }

        // Famille d'icône d'un événement : le type d'abord, puis le nom
        private static string IconOf(Map_Event e)
        {
            string typ = ((e.Type ?? "") + " " + (e.Element ?? "")).Trim();
            string nam = e.Name ?? "";

            switch (e.Kind)
            {
                case "air_lost": return e.Category == "helicopter" ? "heli" : "plane";
                case "ship_kill": return "ship";
                case "ground_kill":
                    return FirstMatch(VehicleRules, typ) ?? FirstMatch(VehicleRules, nam) ?? "truck";

                case "static_kill":
                    if (RxStaticHeli.IsMatch(typ)) return "heli";
                    if (RxStaticPlane.IsMatch(typ)) return "plane";
                    return FirstMatch(VehicleRules, typ) ?? FirstMatch(StructureRules, typ)
                        ?? FirstMatch(StructureRules, nam) ?? "crate";

                case "scenery_kill":
                    return FirstMatch(StructureRules, typ) ?? FirstMatch(StructureRules, nam) ?? "building";
            }
            return "";
        }

        private void DrawEvent(Graphics g, Map_Event e, float x0, float y0)
        {
            // Position approximative : icône plus pâle
            int alpha = (e.PosSrc == "sar_approx" || e.PosSrc == "oob_start") ? 130 : 255;
            Color c = Color.FromArgb(alpha, SideColor(e.Side));

            // Décors : beige ; objet d'une cible de la target list : or
            if (e.Kind == "scenery_kill") c = e.Target != "" ? Color.FromArgb(255, 215, 0) : Color.FromArgb(220, 190, 150, 90);

            // Les icônes sont dessinées autour de (0,0) à la taille d'origine, puis agrandies par IconScale
            GraphicsState st = g.Save();
            g.TranslateTransform(x0, y0);
            g.ScaleTransform(IconScale, IconScale);
            try { DrawEventIcon(g, e, c, alpha); }
            finally { g.Restore(st); }
        }

        private void DrawEventIcon(Graphics g, Map_Event e, Color c, int alpha)
        {
            const float x = 0f, y = 0f;

            using (var fill = new SolidBrush(c))
            using (var edge = new Pen(Color.FromArgb(alpha, 0, 0, 0), 0.9f) { LineJoin = LineJoin.Round })
            {
                switch (e.Kind)
                {
                    case "air_lost":
                    case "ground_kill":
                    case "ship_kill":
                    case "static_kill":
                    case "scenery_kill":
                        DrawFamily(g, IconOf(e), fill, edge, c);
                        // Pas de croix sur un décor ordinaire (trop nombreux) ; croix sur tout le reste
                        if (e.Kind != "scenery_kill" || e.Target != "") DrawCross(g, x, y, e.Kind == "air_lost" ? 6f : 5f);
                        break;

                    case "eject":
                        {
                            // Parachute : voile (demi-disque), cordes, pilote
                            g.FillPie(fill, x - 8, y - 10, 16, 12, 180, 180);
                            g.DrawArc(edge, x - 8, y - 10, 16, 12, 180, 180);
                            g.DrawLine(edge, x - 8, y - 4, x, y + 6);
                            g.DrawLine(edge, x + 8, y - 4, x, y + 6);
                            g.FillEllipse(Brushes.White, x - 2, y + 4, 4, 4);
                            g.DrawEllipse(edge, x - 2, y + 4, 4, 4);
                            break;
                        }

                    case "pilot_dead":
                        g.FillEllipse(Brushes.Black, x - 7, y - 7, 14, 14);
                        using (var white = new Pen(Color.White, 1.2f))
                        {
                            g.DrawLine(white, x - 3, y, x + 3, y);
                            g.DrawLine(white, x, y - 4, x, y + 4);
                        }
                        break;

                    case "pilot_sar":
                        {
                            // Pilote au sol : vert s'il est de cette mission, gris s'il date d'une mission précédente
                            Color pc = e.Current ? Color.FromArgb(90, 220, 90) : Color.FromArgb(160, 160, 160);
                            using (var pbr = new SolidBrush(pc))
                            {
                                g.FillEllipse(pbr, x - 3, y - 9, 6, 6);
                                g.FillPolygon(pbr, new[] { new PointF(x - 6, y + 6), new PointF(x + 6, y + 6), new PointF(x, y - 3) });
                            }
                            g.DrawEllipse(edge, x - 3, y - 9, 6, 6);
                            g.DrawPolygon(edge, new[] { new PointF(x - 6, y + 6), new PointF(x + 6, y + 6), new PointF(x, y - 3) });
                            break;
                        }

                    case "bomb":
                        using (var br = new SolidBrush(Color.FromArgb(255, 150, 40))) g.FillEllipse(br, x - 3.5f, y - 3.5f, 7, 7);
                        g.DrawEllipse(edge, x - 3.5f, y - 3.5f, 7, 7);
                        break;

                    default:
                        g.FillEllipse(Brushes.Gainsboro, x - 3, y - 3, 6, 6);
                        break;
                }
            }
        }

        // Une famille d'icône, centrée en (0,0), environ 18 x 18 (avant agrandissement)
        private static void DrawFamily(Graphics g, string icon, SolidBrush fill, Pen edge, Color c)
        {
            switch (icon)
            {
                case "plane":
                    {
                        var pts = new PointF[JetShape.Length];
                        for (int i = 0; i < pts.Length; i++) pts[i] = new PointF(JetShape[i].X * 9f, JetShape[i].Y * 9f);
                        g.FillPolygon(fill, pts);
                        g.DrawPolygon(edge, pts);
                        break;
                    }

                case "heli":
                    g.FillEllipse(fill, -4, -5, 8, 10);                    // cabine
                    g.DrawEllipse(edge, -4, -5, 8, 10);
                    g.DrawLine(edge, 0, 5, 0, 10);                        // poutre de queue
                    g.DrawLine(edge, -9, -2, 9, 2);                       // rotor
                    g.DrawLine(edge, -9, 2, 9, -2);
                    break;

                case "tank":
                    g.FillRectangle(fill, -8, -2, 16, 8);                 // caisse
                    g.DrawRectangle(edge, -8, -2, 16, 8);
                    g.FillEllipse(fill, -4, -6, 9, 9);                    // tourelle
                    g.DrawEllipse(edge, -4, -6, 9, 9);
                    g.DrawLine(edge, 4, -2, 11, -2);                      // canon
                    break;

                case "truck":
                    g.FillRectangle(fill, -9, -5, 11, 9);                 // benne
                    g.DrawRectangle(edge, -9, -5, 11, 9);
                    g.FillRectangle(fill, 2, -3, 7, 7);                   // cabine
                    g.DrawRectangle(edge, 2, -3, 7, 7);
                    g.FillEllipse(Brushes.Black, -7, 3, 4, 4);            // roues
                    g.FillEllipse(Brushes.Black, 3, 3, 4, 4);
                    break;

                case "inf":
                    g.FillEllipse(fill, -3, -9, 6, 6);                    // tête
                    g.DrawEllipse(edge, -3, -9, 6, 6);
                    g.FillPolygon(fill, new[] { new PointF(-5, 7), new PointF(5, 7), new PointF(0, -3) });
                    g.DrawPolygon(edge, new[] { new PointF(-5, 7), new PointF(5, 7), new PointF(0, -3) });
                    g.DrawLine(edge, 3, 4, 9, -4);                        // fusil
                    break;

                case "sam":
                    g.FillPolygon(fill, new[] { new PointF(0, -9), new PointF(8, 7), new PointF(-8, 7) });
                    g.DrawPolygon(edge, new[] { new PointF(0, -9), new PointF(8, 7), new PointF(-8, 7) });
                    g.DrawLine(edge, -2.5f, 3, -2.5f, -3);                // deux missiles
                    g.DrawLine(edge, 2.5f, 3, 2.5f, -3);
                    break;

                case "aaa":
                    g.FillEllipse(fill, -5, -3, 10, 10);                  // affût
                    g.DrawEllipse(edge, -5, -3, 10, 10);
                    g.DrawLine(edge, -2, -2, -4, -10);                    // deux canons
                    g.DrawLine(edge, 2, -2, 4, -10);
                    break;

                case "radar":
                    g.FillPie(fill, -8, -8, 16, 16, 0, 180);              // antenne parabolique (bol)
                    g.DrawPie(edge, -8, -8, 16, 16, 0, 180);
                    g.DrawLine(edge, 0, 0, 4, -8);                        // pied du capteur
                    g.FillEllipse(Brushes.White, 3, -10, 3.5f, 3.5f);
                    g.DrawLine(edge, 0, 8, 0, 11);                        // mât
                    break;

                case "art":
                    g.FillEllipse(fill, -6, -2, 10, 10);                  // affût
                    g.DrawEllipse(edge, -6, -2, 10, 10);
                    using (var barrel = new Pen(Color.FromArgb(edge.Color.A, 0, 0, 0), 2.4f))
                        g.DrawLine(barrel, -1, 3, 9, -8);                 // long tube
                    break;

                case "ship":
                    {
                        var hull = new[] { new PointF(-10, -2), new PointF(5, -2), new PointF(11, 2), new PointF(5, 6), new PointF(-10, 6) };
                        g.FillPolygon(fill, hull);
                        g.DrawPolygon(edge, hull);
                        g.FillRectangle(fill, -3, -7, 6, 5);              // superstructure
                        g.DrawRectangle(edge, -3, -7, 6, 5);
                        break;
                    }

                case "building":
                    {
                        g.FillRectangle(fill, -6, -1, 12, 9);             // murs
                        g.DrawRectangle(edge, -6, -1, 12, 9);
                        var roof = new[] { new PointF(-9, -1), new PointF(0, -9), new PointF(9, -1) };
                        g.FillPolygon(fill, roof);
                        g.DrawPolygon(edge, roof);
                        break;
                    }

                case "bridge":
                    g.FillRectangle(fill, -10, -1, 20, 3);                // tablier
                    g.DrawRectangle(edge, -10, -1, 20, 3);
                    using (var arch = new Pen(Color.FromArgb(edge.Color.A, 0, 0, 0), 3f))
                        g.DrawArc(arch, -9, -9, 18, 16, 180, 180);
                    using (var arch2 = new Pen(c, 1.6f))
                        g.DrawArc(arch2, -9, -9, 18, 16, 180, 180);
                    g.DrawLine(edge, -9, 2, -9, 8);                       // piles
                    g.DrawLine(edge, 9, 2, 9, 8);
                    break;

                case "station":
                    {
                        g.FillRectangle(fill, -6, -9, 12, 13);            // face d'un train
                        g.DrawRectangle(edge, -6, -9, 12, 13);
                        g.FillRectangle(Brushes.White, -4, -7, 8, 5);     // pare-brise
                        g.DrawRectangle(edge, -4, -7, 8, 5);
                        g.FillEllipse(Brushes.Black, -4.5f, 0, 3, 3);     // feux
                        g.FillEllipse(Brushes.Black, 1.5f, 0, 3, 3);
                        g.DrawLine(edge, -9, 7, 9, 7);                    // rails
                        g.DrawLine(edge, -9, 10, 9, 10);
                        break;
                    }

                case "tree":
                    {
                        using (var green = new SolidBrush(Color.FromArgb(c.A, 70, 140, 70)))
                        {
                            var tri = new[] { new PointF(0, -8), new PointF(6, 3), new PointF(-6, 3) };
                            g.FillPolygon(green, tri);
                            g.DrawPolygon(edge, tri);
                        }
                        g.DrawLine(edge, 0, 3, 0, 8);
                        break;
                    }

                case "crate":
                    g.FillRectangle(fill, -5, -5, 10, 10);
                    g.DrawRectangle(edge, -5, -5, 10, 10);
                    g.DrawLine(edge, -5, -5, 5, 5);
                    g.DrawLine(edge, -5, 5, 5, -5);
                    break;

                default:
                    {
                        var dia = new[] { new PointF(0, -8), new PointF(8, 0), new PointF(0, 8), new PointF(-8, 0) };
                        g.FillPolygon(fill, dia);
                        g.DrawPolygon(edge, dia);
                        break;
                    }
            }
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            _view.OverlayPaint -= PaintEvents;
            _hoverTip.Dispose();
            _tip.Dispose();
            _fontBold.Dispose();
            _sfCenter.Dispose();
            if (_background != null) _background.Dispose();
            base.OnFormClosed(e);
        }
    }
}
