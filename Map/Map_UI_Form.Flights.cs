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
    // Partie de Map_UI_Form (voir Map_UI_Form.cs) : colonne de droite de la carte mission : liste des vols, filtres, waypoints
    internal partial class Map_UI_Form : Form
    {
        // Carte de mission : le panneau de droite a deux onglets, en lecture seule.
        //  - Flights : liste des vols (couleur = celle de la carte) + fiche du vol choisi
        //  - Bases   : fiche de la base choisie (altitude, pistes, fréquences, avions) + ses escadrons
        private void BuildMissionRight(Panel right, Panel noPosPanel)
        {
            _tabs = new TabControl { Dock = DockStyle.Fill };
            _tabFlights = new TabPage("Flights") { Padding = new Padding(6) };
            _tabBases = new TabPage("Bases") { Padding = new Padding(6) };

            // --- Flights ---
            _flightList = new Map_UI_FlightList { Dock = DockStyle.Fill };
            _flightList.SelectedIndexChanged += FlightListChanged;
            BuildFlightPop();

            // Fiche du vol, puis liste de ses waypoints, puis fiche du waypoint choisi : le tout défile si la fenêtre est petite
            var cardHost = new Map_UI_FastPanel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(0, 8, 0, 0) };
            _flightHost = cardHost;
            _wpTitle = new Label { Dock = DockStyle.Top, Height = 22, Text = "Waypoints", Font = new Font("Segoe UI", 9f, FontStyle.Bold), TextAlign = ContentAlignment.BottomLeft };
            _wpList = new Map_UI_WaypointList { Dock = DockStyle.Top, Height = 60 };
            _wpList.SelectedIndexChanged += WaypointListChanged;
            _wpCard = new Map_UI_Card { Dock = DockStyle.Top };
            _wpCard.ValueEdited += WpValueEdited;
            _wpCard.LockToggled += WpLockToggled;
            cardHost.Controls.Add(_wpCard);

            // CampaignMaker : type de départ du 1er waypoint (écrit au clic sur Save modification : seulement type et action du point)
            if (ParamConf.UserLevel == UserLevel.CampaignMaker)
            {
                // La liste est posée DANS la fiche, à la place de la valeur de la ligne "Start type" (c'est la fiche qui la place)
                _cmbStart = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Size = new Size(210, 24), Enabled = false, Visible = false };
                _cmbStart.Items.AddRange(Map_Flight.StartNames);
                _cmbStart.SelectionChangeCommitted += (s, e) => StartKindPicked();
                var startTip = new ToolTip { AutoPopDelay = 25000, InitialDelay = 250, ReshowDelay = 100, ShowAlways = true };
                startTip.SetToolTip(_cmbStart, "START TYPE of the flight (first waypoint)\nChanges only [\"type\"] and [\"action\"] of the first waypoint in the mission file. The parking / parking_id fields of the aircraft are NOT changed.\nParking / Runway need a flight that starts from an airfield.\nGround: altitude 0, no airfield attached.\nAir start: the flight is active at mission start, at 1500 m (if the first waypoint was lower) and at least the cruise speed of the flight; if it is early for its locked time, the extra time goes into a hold orbit (DCE OrbitPosition) on the first waypoint that has one, or on waypoint 1 (never waypoint 0); type and action are both \"Turning Point\".\nActive in pencil mode only.");
                _wpCard.Controls.Add(_cmbStart);
                _wpCard.Embedded = _cmbStart;
                _wpCard.EmbeddedKey = "Start type";
            }
            cardHost.Controls.Add(new Panel { Dock = DockStyle.Top, Height = 8 });
            cardHost.Controls.Add(_wpList);
            cardHost.Controls.Add(_wpTitle);

            // CampaignMaker, mode crayon : ajouter / supprimer un waypoint (aussi par clic droit sur la carte)
            if (ParamConf.UserLevel == UserLevel.CampaignMaker)
            {
                var wpTip = new ToolTip { AutoPopDelay = 20000, InitialDelay = 250, ReshowDelay = 100, ShowAlways = true };
                _wpTitle.Height = 30;
                _btnAddWp = new Button { Text = "Add WP", Size = new Size(76, 24), Top = 4, Enabled = false };
                _btnDelWp = new Button { Text = "Delete WP", Size = new Size(84, 24), Top = 4, Enabled = false };
                _btnAddWp.Click += (s, e) => AddWaypointClicked();
                _btnDelWp.Click += (s, e) => DeleteWaypointClicked();
                wpTip.SetToolTip(_btnAddWp, "ADD A WAYPOINT\nAdds a waypoint in the middle of the leg that follows the chosen waypoint (the leg before it if it is the last one). Its altitude is the average of its two neighbours. Then drag it where you want. Times and speeds of the flight are calculated again. You can also right-click on a leg of the chosen flight on the map: the waypoint is added exactly there. Active in pencil mode only.");
                wpTip.SetToolTip(_btnDelWp, "DELETE THE CHOSEN WAYPOINT\nTimes and speeds of the other waypoints are calculated again. The first waypoint (start), the last one (landing), airfield waypoints and the end of a race-track can not be deleted. You can also right-click on the waypoint on the map. Active in pencil mode only.");
                _wpTitle.Controls.Add(_btnAddWp);
                _wpTitle.Controls.Add(_btnDelWp);
                _wpTitle.Resize += (s, e) =>
                {
                    _btnDelWp.Left = _wpTitle.Width - _btnDelWp.Width - 6;
                    _btnAddWp.Left = _btnDelWp.Left - _btnAddWp.Width - 4;
                };
            }

            // CampaignMaker, mode crayon : nombre d'avions du vol choisi (écrit au clic sur Save modification) et audit du vol
            if (ParamConf.UserLevel == UserLevel.CampaignMaker)
            {
                _toolsTip = new ToolTip { AutoPopDelay = 20000, InitialDelay = 250, ReshowDelay = 100, ShowAlways = true };
                _toolsPanel = new Panel { Dock = DockStyle.Top, Height = 40 };
                var lblAc = new Label { Text = "Aircraft", Location = new Point(4, 12), Size = new Size(54, 20) };
                _numAircraft = new NumericUpDown { Minimum = 1, Maximum = 4, Value = 1, Location = new Point(62, 7), Size = new Size(52, 26), Enabled = false };
                _numAircraft.ValueChanged += (s, e) => AircraftCountChanged();
                _toolsTip.SetToolTip(_numAircraft, "NUMBER OF AIRCRAFT\nNumber of aircraft (1 to 4) of the flight chosen in the list. Added aircraft are copies of the last one (new unit id, name, onboard number, callsign number, formation spot); removed ones are the last ones. Nothing is written until you press Save modification (copy kept in <name>.wpt_backup). Not changed: the briefing text and the squadron roster of DCE. Active in pencil mode only.");
                _btnAudit = new Button { Text = "Audit", Location = new Point(122, 4), Size = new Size(80, 30), Enabled = false };
                _btnAudit.Click += (s, e) => AuditShownFlight();
                _toolsTip.SetToolTip(_btnAudit, "FLIGHT AUDIT\nRead only. Lists everything in the mission file that touches the chosen flight: ids, names, aircraft, triggers, other flights that point at it, scripts, and whether the ids are unique. Active in pencil mode only.");
                _btnRemove = new Button { Text = "Delete...", Location = new Point(208, 4), Size = new Size(84, 30), Enabled = false };
                _btnRemove.Click += (s, e) => RemoveShownFlight();
                _toolsTip.SetToolTip(_btnRemove, "DELETE / DISABLE FLIGHT\nDelete the chosen flight, the whole package, or only disable the flight (it stays in the file but is never activated). Triggers that use it are cleaned; flights that follow it are moved to another flight of the package. Nothing is written until you press Save modification. Player flights can not be removed. Active in pencil mode only.");
                _toolsPanel.Controls.Add(lblAc);
                _toolsPanel.Controls.Add(_numAircraft);
                _toolsPanel.Controls.Add(_btnAudit);
                _toolsPanel.Controls.Add(_btnRemove);

                // Cadre des avions du vol : entre les outils et les waypoints (Top : ajouté avant les outils pour rester dessous)
                _unitsPanel = new Map_UI_FastTable { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 5, Visible = false, Padding = new Padding(0, 2, 0, 6) };
                _unitsPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40));
                _unitsPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40));
                _unitsPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20));
                _unitsPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 28));
                _unitsPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 30));
                cardHost.Controls.Add(_unitsPanel);
                cardHost.Controls.Add(_toolsPanel);        // Top : le dernier ajouté est le plus haut
            }

            _flightInfo = new Label { Dock = DockStyle.Top, Height = 34, ForeColor = Color.DimGray, Font = new Font("Segoe UI", 8.5f), Visible = false };
            cardHost.Controls.Add(_flightInfo);         // Top : le dernier ajouté est le plus haut

            _tabFlights.Controls.Add(cardHost);
            _tabFlights.Controls.Add(_flightSlot);      // la liste elle-même (_flightPop) est posée par-dessus, voir PlaceFlightPop

            // CampaignMaker : les deux camps sont chargés, ces cases en masquent un ou l'autre
            if (ParamConf.UserLevel == UserLevel.CampaignMaker)
            {
                var camps = new Panel { Dock = DockStyle.Top, Height = 26 };
                _chkBlueFlights = new CheckBox { Text = "Blue flights", Checked = true, AutoSize = true, Location = new Point(4, 4), ForeColor = Color.FromArgb(30, 90, 200) };
                _chkRedFlights = new CheckBox { Text = "Red flights", Checked = true, AutoSize = true, Location = new Point(120, 4), ForeColor = Color.FromArgb(200, 40, 40) };
                _chkBlueFlights.CheckedChanged += (s, e) => ApplyCampFilter();
                _chkRedFlights.CheckedChanged += (s, e) => ApplyCampFilter();
                camps.Controls.Add(_chkBlueFlights);
                camps.Controls.Add(_chkRedFlights);
                _tabFlights.Controls.Add(camps);        // Top : le dernier ajouté est le plus haut
            }

            _tabFlights.Controls.Add(_flightPop);
            _flightPop.BringToFront();
            _tabFlights.Resize += (s, e) => PlaceFlightPop();
            PlaceFlightPop();

            // --- Bases ---
            // Fiche, puis escadrons (hauteur fixe), puis bases sans position : le tout défile si la fenêtre est petite
            _baseCard = new Map_UI_Card { Dock = DockStyle.Top };
            _baseHost = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
            _listSquads.Dock = DockStyle.Top;
            _listSquads.Height = SquadListHeight;
            noPosPanel.Dock = DockStyle.Top;
            noPosPanel.Height = 120;
            _baseHost.Controls.Add(noPosPanel);     // Top : le dernier ajouté est le plus haut
            _baseHost.Controls.Add(_listSquads);
            _baseHost.Controls.Add(_baseCard);
            if (_parkBar != null) _baseHost.Controls.Add(_parkBar);        // CampaignMaker : barre des places d'hélicoptère, tout en haut de l'onglet
            _tabBases.Controls.Add(_baseHost);

            // --- Briefing : la partie du briefing de la mission qui concerne le vol choisi ---
            _tabBrief = new TabPage("Briefing") { Padding = new Padding(6) };
            _briefBox = new RichTextBox { Dock = DockStyle.Fill, ReadOnly = true, WordWrap = false, ScrollBars = RichTextBoxScrollBars.Both, Font = new Font("Consolas", 8.5f), BackColor = Color.FromArgb(250, 251, 253), BorderStyle = BorderStyle.FixedSingle };
            _btnDatacard = new Button { Text = "Datacard (image)...", Dock = DockStyle.Top, Height = 28, Enabled = false };
            _btnDatacard.Click += (s, e) => OpenDatacard();
            _briefMode = new ComboBox { Dock = DockStyle.Top, DropDownStyle = ComboBoxStyle.DropDownList };
            _briefMode.Items.AddRange(new object[] { "This flight", "Side briefing (blue / red task)", "Mission description" });
            _briefMode.SelectedIndex = 0;
            _briefMode.SelectedIndexChanged += (s, e) => UpdateBriefing(_shownFlight);
            _tabBrief.Controls.Add(_briefBox);
            _tabBrief.Controls.Add(_briefMode);
            _tabBrief.Controls.Add(_btnDatacard);

            _tabs.TabPages.Add(_tabFlights);
            _tabs.TabPages.Add(_tabBrief);
            _tabs.TabPages.Add(_tabBases);
            right.Controls.Add(_tabs);

            ShowFlight(null);
            UpdateBaseCard(new List<Squad>(), 0, 0);
        }

        // Liste des vols (joueurs d'abord), et fiche vide
        private void FillFlightList(Map_MissionData data)
        {
            _missionData = data;
            if (_flightList == null) return;

            _syncFlightList = true;
            _flightList.BeginUpdate();
            _flightList.Items.Clear();
            RebuildFilterChoices();
            foreach (Map_Flight f in FilteredFlights())
                _flightList.Items.Add(f);
            _flightList.EndUpdate();
            _syncFlightList = false;

            UpdateFlightSummary();
            ShowFlight(null);
        }

        // ------------------------------------------------------------------
        //  Liste des vols : bandeau repliable, filtres, agrandissement par-dessus les autres blocs
        // ------------------------------------------------------------------

        private void BuildFlightPop()
        {
            // Place réservée dans l'onglet : le bandeau + la liste repliée. La liste (_flightPop) flotte par-dessus.
            _flightSlot = new Panel { Dock = DockStyle.Top, Height = 24 + FlightListH };
            _secFlights = new Map_UI_Section { Dock = DockStyle.Top, Height = 24, Title = "Flights", Expanded = true };
            _secFlights.Toggled += (s, e) => { _flightSlot.Height = _secFlights.Expanded ? 24 + FlightListH : 24; PlaceFlightPop(); };
            _flightSlot.Controls.Add(_secFlights);
            _flightSlot.LocationChanged += (s, e) => PlaceFlightPop();
            _flightSlot.SizeChanged += (s, e) => PlaceFlightPop();

            // Barre de filtres (en bas de la liste agrandie : la liste ne bouge pas sous la souris quand elle s'agrandit)
            _cmbFlPack = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill, Margin = new Padding(0, 0, 4, 2) };
            _cmbFlType = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill, Margin = new Padding(0, 0, 4, 2) };
            _cmbFlKind = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 2) };
            _cmbFlKind.Items.AddRange(new object[] { "Planes + helicopters", "Planes only", "Helicopters only" });
            _cmbFlKind.SelectedIndex = 0;
            _txtFlName = new TextBox { Dock = DockStyle.Fill, Margin = new Padding(0, 2, 4, 0) };
            var btnAll = new Button { Text = "All shown", Dock = DockStyle.Fill, Margin = new Padding(0, 1, 4, 0) };
            var btnNone = new Button { Text = "None", Dock = DockStyle.Fill, Margin = new Padding(0, 1, 0, 0) };

            _cmbFlPack.SelectedIndexChanged += (s, e) => FilterChanged();
            _cmbFlType.SelectedIndexChanged += (s, e) => FilterChanged();
            _cmbFlKind.SelectedIndexChanged += (s, e) => FilterChanged();
            _txtFlName.TextChanged += (s, e) => FilterChanged();
            btnAll.Click += (s, e) => { _syncFlightList = true; for (int i = 0; i < _flightList.Items.Count; i++) _flightList.SetSelected(i, true); _syncFlightList = false; FlightListChanged(null, EventArgs.Empty); };
            btnNone.Click += (s, e) => { _syncFlightList = true; _flightList.ClearSelected(); _syncFlightList = false; FlightListChanged(null, EventArgs.Empty); };

            var tip = new ToolTip { AutoPopDelay = 20000, InitialDelay = 300, ReshowDelay = 100, ShowAlways = true };
            tip.SetToolTip(_cmbFlPack, "Show only the flights of one package (Pack N).");
            tip.SetToolTip(_cmbFlType, "Show only one aircraft type.");
            tip.SetToolTip(_cmbFlKind, "Planes, helicopters, or both.");
            tip.SetToolTip(_txtFlName, "Show only the flights whose name contains this text.");
            tip.SetToolTip(btnAll, "Choose all the flights that are shown in the list (they are shown on the map).");
            tip.SetToolTip(btnNone, "Choose no flight: all the flights are shown on the map as usual.");

            var bar = new TableLayoutPanel { Dock = DockStyle.Bottom, Height = FlightFilterH, ColumnCount = 3, RowCount = 2, Visible = false, Padding = new Padding(0, 4, 0, 0) };
            bar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 34));
            bar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 34));
            bar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 32));
            bar.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
            bar.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
            bar.Controls.Add(_cmbFlPack, 0, 0);
            bar.Controls.Add(_cmbFlType, 1, 0);
            bar.Controls.Add(_cmbFlKind, 2, 0);
            bar.Controls.Add(_txtFlName, 0, 1);
            bar.Controls.Add(btnAll, 1, 1);
            bar.Controls.Add(btnNone, 2, 1);
            _flightFilterBar = bar;

            _flightPop = new Panel { Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right, Size = new Size(300, FlightListH) };
            _flightPop.Controls.Add(_flightList);       // Fill d'abord, puis Bottom : le dernier ajouté prend sa place en premier
            _flightPop.Controls.Add(_flightFilterBar);

            // Agrandie quand la souris s'y arrête, repliée quand elle est partie un moment (la liste déroulante d'un filtre ou la saisie du nom la gardent ouverte)
            _popTimer = new System.Windows.Forms.Timer { Interval = 150 };
            _popTimer.Tick += (s, e) => FlightPopTick();
            _popTimer.Start();
            Disposed += (s, e) => { if (_popTimer != null) _popTimer.Dispose(); };
        }

        private void FlightPopTick()
        {
            if (_flightPop == null || !_flightPop.Visible || _tabs == null || _tabs.SelectedTab != _tabFlights || Form.ActiveForm != this) return;
            bool inside = _flightPop.RectangleToScreen(_flightPop.ClientRectangle).Contains(Cursor.Position);
            if (inside)
            {
                _popOutTicks = 0;
                if (!_flightPopOpen && ++_popInTicks >= 2) SetFlightPopOpen(true);
                return;
            }
            _popInTicks = 0;
            if (!_flightPopOpen) return;
            if (_cmbFlPack.DroppedDown || _cmbFlType.DroppedDown || _cmbFlKind.DroppedDown || _txtFlName.Focused) { _popOutTicks = 0; return; }
            if (++_popOutTicks >= 3) SetFlightPopOpen(false);
        }

        private void SetFlightPopOpen(bool open)
        {
            if (_flightPopOpen == open) return;
            _flightPopOpen = open;
            _flightFilterBar.Visible = open;
            PlaceFlightPop();
            if (!open && _flightList.SelectedIndex >= 0) _flightList.TopIndex = Math.Max(0, _flightList.SelectedIndices[0]);       // replié : le premier vol choisi reste visible
        }

        // La liste est posée sur la place réservée ; agrandie, elle déborde sur les blocs du dessous
        private void PlaceFlightPop()
        {
            if (_flightPop == null || _flightSlot == null || _tabFlights == null) return;

            bool show = _secFlights.Expanded;
            _flightPop.Visible = show;
            if (!show) return;

            int top = _flightSlot.Top + 24;
            int h = FlightListH;
            if (_flightPopOpen)
            {
                int want = Math.Max(FlightListH, _flightList.Items.Count * _flightList.ItemHeight + 4) + FlightFilterH;
                int max = _tabFlights.ClientSize.Height - top - 6;
                h = Math.Min(want, Math.Max(max, FlightListH + FlightFilterH));
            }
            _flightPop.SetBounds(_flightSlot.Left, top, _flightSlot.Width, h);
            _flightPop.BringToFront();
        }

        // Choix des filtres : les types et les packages viennent des vols de la mission
        private void RebuildFilterChoices()
        {
            if (_cmbFlPack == null) return;
            _filterSync = true;
            try
            {
                List<Map_Flight> all = VisibleFlights();
                string pack = _cmbFlPack.SelectedItem as string, type = _cmbFlType.SelectedItem as string;

                _cmbFlPack.Items.Clear();
                _cmbFlPack.Items.Add("All packages");
                foreach (int n in all.Where(f => f.Package >= 0).Select(f => f.Package).Distinct().OrderBy(n => n))
                    _cmbFlPack.Items.Add("Pack " + n);
                int i = pack == null ? -1 : _cmbFlPack.Items.IndexOf(pack);
                _cmbFlPack.SelectedIndex = i < 0 ? 0 : i;

                _cmbFlType.Items.Clear();
                _cmbFlType.Items.Add("All types");
                foreach (string ty in all.Where(f => f.Type != "").Select(f => Map_Flights.TypeName(f.Type)).Distinct().OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
                    _cmbFlType.Items.Add(ty);
                i = type == null ? -1 : _cmbFlType.Items.IndexOf(type);
                _cmbFlType.SelectedIndex = i < 0 ? 0 : i;
            }
            finally { _filterSync = false; }
        }

        private IEnumerable<Map_Flight> FilteredFlights()
        {
            IEnumerable<Map_Flight> q = VisibleFlights();
            if (_cmbFlPack != null)
            {
                string pack = _cmbFlPack.SelectedItem as string;
                if (_cmbFlPack.SelectedIndex > 0 && pack != null)
                {
                    int n;
                    if (int.TryParse(pack.Substring(5), out n)) q = q.Where(f => f.Package == n);
                }
                string type = _cmbFlType.SelectedItem as string;
                if (_cmbFlType.SelectedIndex > 0 && type != null) q = q.Where(f => Map_Flights.TypeName(f.Type) == type);
                if (_cmbFlKind.SelectedIndex == 1) q = q.Where(f => !f.IsHeli);
                else if (_cmbFlKind.SelectedIndex == 2) q = q.Where(f => f.IsHeli);
                string txt = _txtFlName.Text.Trim();
                if (txt != "") q = q.Where(f => f.Name.IndexOf(txt, StringComparison.OrdinalIgnoreCase) >= 0 || f.Title.IndexOf(txt, StringComparison.OrdinalIgnoreCase) >= 0);
            }
            return q.OrderBy(x => x.IsPlayer ? 0 : (x.IsPackage ? 1 : 2)).ThenBy(x => x.Title, StringComparer.OrdinalIgnoreCase);
        }

        // Un filtre a changé : la liste est refaite, les vols choisis qui y sont encore le restent (les autres restent choisis sur la carte)
        private void FilterChanged()
        {
            if (_filterSync || _flightList == null || _missionData == null) return;
            RefillFlightItems();
        }

        private void RefillFlightItems()
        {
            var keep = new HashSet<Map_Flight>(_flightList.SelectedItems.Cast<Map_Flight>());
            _syncFlightList = true;
            _flightList.BeginUpdate();
            _flightList.Items.Clear();
            foreach (Map_Flight f in FilteredFlights()) _flightList.Items.Add(f);
            for (int i = 0; i < _flightList.Items.Count; i++)
                if (keep.Contains((Map_Flight)_flightList.Items[i])) _flightList.SetSelected(i, true);
            _flightList.EndUpdate();
            _syncFlightList = false;
            UpdateFlightSummary();
            PlaceFlightPop();
        }

        private void ResetFlightFilters()
        {
            if (_cmbFlPack == null) return;
            _filterSync = true;
            _cmbFlPack.SelectedIndex = 0;
            _cmbFlType.SelectedIndex = 0;
            _cmbFlKind.SelectedIndex = 0;
            _txtFlName.Text = "";
            _filterSync = false;
            RefillFlightItems();
        }

        // Résumé sur le bandeau (visible aussi quand la section est repliée)
        private void UpdateFlightSummary()
        {
            if (_secFlights == null || _flightList == null) return;
            int total = VisibleFlights().Count, shown = _flightList.Items.Count, sel = _flightList.SelectedItems.Count;
            _secFlights.Summary = (shown == total ? total + " flights" : shown + " of " + total + " flights") + (sel > 0 ? ", " + sel + " chosen" : "");
        }

        // Vols du joueur (Client) et de son package, dans le camp du joueur
        private List<Map_Flight> MyFlights()
        {
            var mySides = new HashSet<string>(VisibleFlights().Where(f => f.IsPlayer).Select(f => f.Side));
            return VisibleFlights().Where(f => f.IsPlayer || (f.IsPackage && mySides.Contains(f.Side)))
                .OrderBy(f => f.IsPlayer ? 0 : 1).ThenBy(f => f.Title, StringComparer.OrdinalIgnoreCase).ToList();
        }

        // A l'ouverture : le vol du joueur et ceux de son package sont choisis (liste + carte + fiche), puis la carte se cadre juste autour d'eux
        private void SelectMyFlights()
        {
            if (_flightList == null || _view == null) return;

            List<Map_Flight> mine = MyFlights();
            if (mine.Count == 0) return;

            SelectInList(mine);
            _view.SetSelectedFlights(mine.Select(x => x.Name));
            _view.SetSelectedWaypoint("", -1);
            ShowFlight(mine[0], mine.Count);        // la fiche montre le vol du joueur (le premier de la liste)
            _view.ZoomToFlights(mine.Select(x => x.Name));
        }

        // CampaignMaker : on a le droit de voir les deux camps, mais le camp adverse au joueur est décoché au départ (à l'utilisateur de le cocher)
        private void SetDefaultCampBoxes(Map_MissionData data)
        {
            if (_chkBlueFlights == null || _chkRedFlights == null) return;

            bool playerBlue = data.Flights.Any(x => x.IsPlayer && x.Side != "red");
            bool playerRed = data.Flights.Any(x => x.IsPlayer && x.Side == "red");
            if (!playerBlue && !playerRed) return;      // pas de vol joueur : on laisse les deux cochés

            _missionData = data;
            _syncCamps = true;                          // pas de recalcul à chaque case : on le fait une seule fois en dessous
            _chkBlueFlights.Checked = playerBlue;
            _chkRedFlights.Checked = playerRed;
            _syncCamps = false;

            _view.SetDrawings("plans", Map_Flights.BuildPlans(VisibleFlights()));
            _view.SetDrawingsVisible("plans", _chkPlans.Checked);
            RefreshRadars();
        }

        private bool _syncCamps;

        // Zones radar : EWR au sol (toujours) + AWACS des camps affichés. Le bouton est grisé s'il n'y a rien à montrer.
        private void RefreshRadars()
        {
            if (_chkRadars == null || _view == null) return;

            var list = new List<Map_Drawing>(_ewrCircles);
            list.AddRange(Map_Flights.BuildRadars(VisibleFlights()));

            _view.SetDrawings("radars", list);
            _chkRadars.Enabled = list.Count > 0;
            _view.SetDrawingsVisible("radars", list.Count > 0 && _chkRadars.Checked);
        }

        // Vols des camps cochés (sans les cases, tous)
        private List<Map_Flight> VisibleFlights()
        {
            if (_missionData == null) return new List<Map_Flight>();
            bool blue = _chkBlueFlights == null || _chkBlueFlights.Checked;
            bool red = _chkRedFlights == null || _chkRedFlights.Checked;
            return _missionData.Flights.Where(f => f.Side == "red" ? red : blue).ToList();
        }

        // Case Blue / Red cochée ou décochée : on reconstruit les tracés avec les vols restants
        private void ApplyCampFilter()
        {
            if (_syncCamps || _missionData == null || _view == null) return;

            _view.SelectedFlight = "";
            _view.SetSelectedWaypoint("", -1);
            _view.SetDrawings("plans", Map_Flights.BuildPlans(VisibleFlights()));
            _view.SetDrawingsVisible("plans", _chkPlans.Checked);
            RefreshRadars();
            FillFlightList(_missionData);
        }

        // Choix dans la liste (CTRL / MAJ = plusieurs vols) -> surbrillance sur la carte + fiche du dernier choisi
        private void FlightListChanged(object sender, EventArgs e)
        {
            if (_syncFlightList) return;
            UpdateFlightSummary();
            List<Map_Flight> sel = _flightList.SelectedItems.Cast<Map_Flight>().ToList();
            _view.SetSelectedFlights(sel.Select(x => x.Name));
            _view.SetSelectedWaypoint("", -1);
            ShowFlight(sel.Count == 0 ? null : sel[sel.Count - 1], sel.Count);
            AfterFlightPicked(sel.Count == 0 ? null : sel[sel.Count - 1], sel.Count);
        }

        // Sélectionne ces vols dans la liste sans déclencher FlightListChanged
        private void SelectInList(List<Map_Flight> flights)
        {
            // Un vol choisi sur la carte est caché par un filtre : les filtres sont remis à zéro pour qu'on le voie dans la liste
            if (flights.Any(f => !_flightList.Items.Contains(f))) ResetFlightFilters();
            _syncFlightList = true;
            _flightList.ClearSelected();
            foreach (Map_Flight f in flights)
            {
                int i = _flightList.Items.IndexOf(f);
                if (i >= 0) _flightList.SetSelected(i, true);
            }
            _syncFlightList = false;
            UpdateFlightSummary();
        }

        // Choix d'un waypoint dans la liste -> anneau sur la carte + fiche du waypoint
        private void WaypointListChanged(object sender, EventArgs e)
        {
            if (_syncWp || _shownFlight == null) return;
            int i = _wpList.SelectedIndex;
            _view.SetSelectedWaypoint(i < 0 ? "" : _shownFlight.Name, i);
            if (i >= 0) _view.CenterOnSelectedWaypoint();        // la carte se centre sur le waypoint choisi dans la liste
            ShowWaypoint(_shownFlight, i);
        }

        // Waypoint tiré à la souris (simulation) : on garde les heures, les vitesses s'adaptent, le tracé et la fiche suivent en direct
        private void OnWaypointMoving(string flightName, int index, double x, double y)
        {
            if (_missionData == null) return;
            Map_Flight f = _missionData.Flights.FirstOrDefault(q => q.Name == flightName);
            if (f == null) return;

            f.MoveWaypoint(index, x, y);
            double start = _missionData.StartTime;

            _view.ReplaceFlightDrawings(f.Name, Map_Flights.BuildPlans(new[] { f }));
            _view.SetSelectedWaypoint(f.Name, index);           // l'anneau doré suit
            _view.SetDragInfo(Map_Flights.DragText(f, index, start));

            // La fiche de droite (plus lourde à refaire) : au plus 6 fois par seconde, et en entier au lâcher
            if (Environment.TickCount - _lastWpCard > 160)
            {
                _lastWpCard = Environment.TickCount;
                ShowWaypoint(f, index);
            }
        }

        private int _lastWpCard;

        // Waypoint lâché : fiche du vol (distance, contrôle du plan) et zones radar mises à jour
        private void OnWaypointDropped(string flightName, int index)
        {
            if (_missionData == null) return;
            Map_Flight f = _missionData.Flights.FirstOrDefault(q => q.Name == flightName);
            if (f == null) return;

            ShowFlight(f);
            _syncWp = true;
            _wpList.SelectedIndex = index < _wpList.Items.Count ? index : -1;
            _syncWp = false;
            ShowWaypoint(f, index);
            RefreshRadars();
            UpdateSaveWpt();
            f.WriteDebug(_missionData.StartTime, "after drop / edit of WP " + index);
        }

        // Le bouton disquette : CampaignMaker, et au moins un waypoint déplacé
        private void UpdateSaveWpt()
        {
            RefreshIssues();
            if (_btnSaveWpt == null) return;
            _btnSaveWpt.Enabled = ParamConf.UserLevel == UserLevel.CampaignMaker && _missionData != null && _missionFile != ""
                && (_missionData.Flights.Any(q => q.Edited) || _unitRows.Count > 0 || _pendingFreq.Count > 0 || _pendingPresets.Count > 0 || _pendingRemove.Count > 0 || _pendingCamp.Count > 0 || Map_RunwayData.HasPending || _parkDirty.Count > 0);
        }

        // Écrit les waypoints déplacés dans le fichier mission (avec copie de sécurité), puis relit la mission
        private void SaveMovedWaypoints()
        {
            if (_missionData == null || _missionFile == "") return;
            List<Map_Flight> edited = _missionData.Flights.Where(q => q.Edited).ToList();
            bool parks = _parkDirty.Count > 0;
            if (edited.Count == 0 && _unitRows.Count == 0 && _pendingFreq.Count == 0 && _pendingPresets.Count == 0 && _pendingRemove.Count == 0 && _pendingCamp.Count == 0 && !Map_RunwayData.HasPending && !parks) return;

            // 0) pistes (réglage de la carte, écrit dans dce_data_user.ini : pas dans le fichier mission)
            if (Map_RunwayData.HasPending && !CommitRunways()) return;
            if (edited.Count == 0 && _unitRows.Count == 0 && _pendingFreq.Count == 0 && _pendingPresets.Count == 0 && _pendingRemove.Count == 0 && _pendingCamp.Count == 0 && !parks)
            {
                UpdateTitle();
                UpdateSaveWpt();
                return;
            }

            var messages = new List<string>();
            string message;

            // 1) waypoints déplacés
            if (edited.Count > 0)
            {
                if (!Map_Drawings.SaveWaypoints(_missionFile, edited, out message))
                {
                    MessageBox.Show(this, message, "Save modification", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                messages.Add(message);
            }

            // 2) nombre d'avions demandé (le fichier est relu à chaque vol)
            string keepName = _shownFlight == null ? "" : _shownFlight.Name;
            foreach (KeyValuePair<string, List<UnitRow>> kv in _unitRows.ToList())
            {
                _unitRows.Remove(kv.Key);
                if (_pendingRemove.ContainsKey(kv.Key)) continue;       // le vol part : inutile de changer ses avions
                var edits = kv.Value.Select(r => new Map_Drawings.UnitEdit { Src = r.Src, Name = r.Name.Trim(), Callsign = r.Callsign.Trim(), Onboard = r.Onboard.Trim() }).ToList();
                if (Map_Drawings.ApplyUnits(_missionFile, kv.Key, edits, SarSpotsNear, out message)) messages.Add(message);
                else MessageBox.Show(this, message, "Aircraft of the flight", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }

            // 2b) fréquences "flight" changées
            foreach (KeyValuePair<string, double> kv in _pendingFreq.ToList())
            {
                _pendingFreq.Remove(kv.Key);
                if (_pendingRemove.ContainsKey(kv.Key)) continue;
                if (Map_Drawings.ApplyFlightFreq(_missionFile, kv.Key, kv.Value, out message)) messages.Add(message);
                else MessageBox.Show(this, message, "Flight radio", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }

            // 2c) presets radio changés (avions joueur / client)
            foreach (KeyValuePair<string, List<List<double>>> kv in _pendingPresets.ToList())
            {
                _pendingPresets.Remove(kv.Key);
                if (_pendingRemove.ContainsKey(kv.Key)) continue;
                if (Map_Drawings.ApplyPresets(_missionFile, kv.Key, kv.Value, out message)) messages.Add(message);
                else MessageBox.Show(this, message, "Radio presets", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            _presetOrig.Clear();
            _presetUnits.Clear();
            _presetFirst.Clear();

            // 3) suppressions / désactivations demandées (un lot = un vol, ou tout un package)
            foreach (var batch in _pendingRemove.GroupBy(kv => kv.Value))
            {
                bool disable = batch.Key.StartsWith("disable", StringComparison.Ordinal);
                if (Map_Drawings.RemoveFlights(_missionFile, batch.Select(kv => kv.Key).ToList(), disable, false, out message)) messages.Add(message);
                else MessageBox.Show(this, message, "Delete flight", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            _pendingRemove.Clear();

            // 3b) variables de campL (debug, traces) : camp_status.lua du .miz
            foreach (KeyValuePair<string, bool> kv in _pendingCamp.ToList())
            {
                if (Map_MissionStats.WriteCampBool(_missionFile, kv.Key, kv.Value)) messages.Add(kv.Key + " = " + (kv.Value ? "true" : "false"));
                else MessageBox.Show(this, "The value " + kv.Key + " could not be written in the camp_status.lua of " + Path.GetFileName(_missionFile) + ".\nSee log.txt.", "Mission options", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            _pendingCamp.Clear();

            // 4) places d'hélicoptère : db_airbases (Init ou Active selon la mission) + les hélicos du fichier mission posés sur une place qui a bougé
            if (parks && CommitParks() && _parkMsg != "") messages.Add(_parkMsg);

            LoadDrawings();             // relit la mission : les positions affichées sont celles du fichier
            Map_Flight again = keepName == "" || _missionData == null ? null : _missionData.Flights.FirstOrDefault(x => x.Name == keepName);
            if (again != null)
            {
                SelectInList(new List<Map_Flight> { again });
                _view.SetSelectedFlights(new[] { again.Name });
                _view.SetSelectedWaypoint("", -1);
                ShowFlight(again, 1);
            }
            _lblStatus.Text = string.Join("   ", messages);
            UpdateSaveWpt();
        }

        // ---- Vol choisi : nombre d'avions et audit (CampaignMaker) ----

        private void UpdateFlightTools()
        {
            if (_numAircraft == null) return;
            Map_Flight f = _shownFlight;
            bool on = f != null && _chkMove != null && _chkMove.Checked;
            _numAircraft.Enabled = on;
            _btnAudit.Enabled = on;
            if (_btnRemove != null)
            {
                _btnRemove.Enabled = on && !f.IsPlayer;
                _btnRemove.Text = f != null && _pendingRemove.ContainsKey(f.Name) ? "Undo" : "Delete...";
            }
            RebuildUnitsPanel();
            UpdateWpTools(f, _wpList == null ? -1 : _wpList.SelectedIndex);
            if (f == null) return;

            List<UnitRow> pr;
            int shown = _unitRows.TryGetValue(f.Name, out pr) ? pr.Count : f.Units;
            _syncNum = true;
            _numAircraft.Value = Math.Max(1, Math.Min(4, shown));
            _syncNum = false;
        }

        // ---- Ajout / suppression d'un waypoint (mode crayon) ----

        private void UpdateWpTools(Map_Flight f, int index)
        {
            if (_btnAddWp == null || _btnDelWp == null) return;
            bool pencil = f != null && _chkMove != null && _chkMove.Checked;
            string why;
            _btnAddWp.Enabled = pencil && index >= 0;
            _btnDelWp.Enabled = pencil && index >= 0 && f.CanDelete(index, out why);
        }

        private void AddWaypointClicked()
        {
            Map_Flight f = _shownFlight;
            int i = _wpList.SelectedIndex;
            if (f == null || i < 0 || f.Waypoints.Count < 2) return;

            int idx = i + 1 < f.Waypoints.Count ? i + 1 : i;            // le tronçon qui suit le waypoint choisi (le dernier : celui qui le précède)
            Map_Waypoint a = f.Waypoints[idx - 1], b = f.Waypoints[idx];
            InsertWaypointAt(f, idx, (a.X + b.X) / 2.0, (a.Y + b.Y) / 2.0);
        }

        private void DeleteWaypointClicked()
        {
            if (_shownFlight == null) return;
            DeleteWaypointAt(_shownFlight, _wpList.SelectedIndex);
        }

        private void OnWaypointInsertRequested(string flightName, int index, double x, double y)
        {
            Map_Flight f = _missionData == null ? null : _missionData.Flights.FirstOrDefault(q => q.Name == flightName);
            if (f != null) InsertWaypointAt(f, index, x, y);
        }

        private void OnWaypointDeleteRequested(string flightName, int index)
        {
            Map_Flight f = _missionData == null ? null : _missionData.Flights.FirstOrDefault(q => q.Name == flightName);
            if (f != null) DeleteWaypointAt(f, index);
        }

        private void InsertWaypointAt(Map_Flight f, int index, double x, double y)
        {
            string why;
            if (!f.CanInsert(index, out why)) { MessageBox.Show(this, why, "Add a waypoint", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
            if (!f.InsertWaypoint(index, x, y)) return;
            AfterStructureEdit(f, index);
        }

        private void DeleteWaypointAt(Map_Flight f, int index)
        {
            string why;
            if (!f.CanDelete(index, out why)) { MessageBox.Show(this, why, "Delete a waypoint", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }

            Map_Waypoint w = f.Waypoints[index];
            string tasks = Map_Flights.TaskNames(w);
            if (tasks != "-" || w.OrbitPattern != "")
            {
                string ask = "Waypoint " + index + (w.ShortName != "" ? " (" + w.ShortName + ")" : "") + " has tasks: " + (tasks != "-" ? tasks : w.OrbitPattern) + ".\nThey are deleted with the waypoint. Delete it?";
                if (MessageBox.Show(this, ask, "Delete a waypoint", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            }

            if (!f.DeleteWaypoint(index)) return;
            AfterStructureEdit(f, Math.Min(index, f.Waypoints.Count - 1));
        }

        // Après un ajout / une suppression : tracé, listes, fiches, radars et bouton Save (comme après un déplacement)
        private void AfterStructureEdit(Map_Flight f, int select)
        {
            _view.ReplaceFlightDrawings(f.Name, Map_Flights.BuildPlans(new[] { f }));
            OnWaypointDropped(f.Name, select);
            _view.SetSelectedWaypoint(f.Name, select);
        }

        // ---- Cadre des avions du vol choisi ----
    }
}
