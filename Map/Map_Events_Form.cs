using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
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
        public string KillerGroup = "";     // tueur au sol : son groupe (voir _groundGroups) et sa position de départ
        public bool HasKillerPos;
        public bool GroundKiller;           // le tueur est un groupe au sol : KillerFlight contient alors le nom de son groupe (sert de "vol" pour la sélection)
        public double KX, KY;
        public double Health = -1;          // avion endommagé (kind damage) : vie restante en %, -1 = inconnue
        public int MissionNo;
        public bool Current = true;         // faux = pilote resté d'une mission précédente
        public string Group = "";           // couche d'affichage (voir Map_Events_Form.GroupOf)
    }

    // Panneau sans scintillement (pour la grosse icône et la ligne de bilan)
    internal class BufferedPanel : Panel
    {
        public BufferedPanel() { DoubleBuffered = true; ResizeRedraw = true; }
    }

    // Une pastille du bilan : symbole, libellé, nombre
    // Trace de vol d'un joueur (fichier DebriefTracks <n>.txt, écrit en jeu par DCE_InGame)
    internal class Map_Track
    {
        public int Id;
        public string Unit = "", Player = "", Type = "", Side = "", Flight = "";
        public List<double[]> Pts = new List<double[]>();       // { t (s), x (nord), y (est), z (altitude m) }
    }

    internal class AaaZone
    {
        public string Name = "", Owner = "";
        public double X, Y, R;
    }

    internal class StatChip
    {
        public string Glyph = "", Label = "";
        public int Value;
        public Color Color = Color.Gray;
        public Rectangle Box;
        public int GlyphW, LabelW, ValueW;
    }

    internal class StatGroup
    {
        public string Title = "";
        public Point TitlePos;
        public int TitleW;
        public List<StatChip> Chips = new List<StatChip>();
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
        // Noms de remplacement (Init\various_table.lua de la campagne) : TypeAlias (types d'avion) et BaseNameAlias (bases)
        private readonly Dictionary<string, string> _typeAlias = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> _baseAlias = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> _unitType = new Dictionary<string, string>(StringComparer.Ordinal);       // avion -> type DCS exact (fichier mission ou événements)
        private CheckBox _chkRunways;
        private bool _samInFile;           // le fichier debrief contient les sites SAM de SA mission (table "samSites")
        private List<Map_Drawing> _samDraw = new List<Map_Drawing>();     // cercles des menaces SAM
        private class AaaHatch { public Color Color; public List<double[]> Pts = new List<double[]>(); }
        private readonly List<AaaHatch> _aaaHatch = new List<AaaHatch>();     // surfaces hachurées (dessinées par PaintEvents, comme les cartes de la guerre du Vietnam)
        private const bool AaaOval = true;                                    // true : zones jointes = un ovale (enveloppe des cercles) ; false : simple fusion des cercles
        private List<Map_Drawing> _aaaDraw = new List<Map_Drawing>();     // zones AAA à éviter (script AAA_Barrage) : cercles seuls, ou contour commun des cercles qui se chevauchent
        private readonly List<AaaZone> _aaaZones = new List<AaaZone>();   // les zones AAA telles que lues dans le fichier
        private CheckBox _chkBorders, _chkSam, _chkAaa, _chkTracks, _chkTObj;
        private readonly List<Map_Event> _targetObjs = new List<Map_Event>();   // éléments de la target list (bâtiments) proches d'un bombardement, pas détruits : les cibles "ratées" ; hors liste d'événements
        private readonly List<Map_Track> _tracks = new List<Map_Track>();    // traces des joueurs (fichier DebriefTracks)
        private bool _tracksComplete;                                        // la ligne F de fin est là ; sinon DCS a planté ou la mission s'est arrêtée net
        private bool _flightsInFile;       // le fichier debrief contient les vols de SA mission (table "flights") : plans et avions viennent de là
        private readonly HashSet<string> _clientUnits = new HashSet<string>(StringComparer.Ordinal);                                      // avions en slot Client/Player dans le fichier mission

        // Filtres
        private string _selSide = "", _selFlight = "", _selUnit = "";
        private bool _syncFilter, _syncSel;
        private readonly Dictionary<string, bool> _groupOn = new Dictionary<string, bool>();

        // Contrôles
        private ComboBox _cmbSide, _cmbFlight, _cmbUnit;
        // 3 lignes de choix : 0 = avions, 1 = hélicoptères, 2 = tireurs au sol. Une seule est active à la fois.
        private readonly ComboBox[] _fCombo = new ComboBox[3], _uCombo = new ComboBox[3];
        private static readonly string[] RowTitle = { "Aircraft:", "Helicopters:", "Ground:" };
        private static readonly string[] RowAllFlights = { "All flights", "All helicopter flights", "All ground shooters" };
        private static readonly string[] RowAllUnits = { "All aircraft", "All choppers", "All units" };
        private readonly List<Control> _rowCtrls = new List<Control>();         // lignes 2 et 3 (cachées par le bouton)
        private readonly List<Control> _mapRowCtrls = new List<Control>();      // ligne "Map style" : remonte quand les lignes 2 et 3 sont cachées
        private Button _btnSelRows;
        private bool _selRowsOpen = true;
        private Panel _topBarOuter;
        private readonly HashSet<string> _heliFlights = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> _gndSide = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);    // groupe au sol qui a détruit quelque chose -> son camp
        private bool _selFromList;             // le dernier événement choisi l'a été dans la liste de droite : il ne change ni la sélection du haut ni la liste
        private CheckBox _chkKills, _chkPlans, _chkBg, _chkRuler;
        private CheckBox _chkTime;                  // curseur de temps : les traces ne montrent que la période [T - longueur, T]
        private TrackBar _barTime;
        private TrackBar _barTrail;                // longueur de la traîne : de 30 s à 20 min, puis "All"
        private Label _lblTime, _lblTrail, _lblSpeed;
        private Button _btnPlay, _btnSlow, _btnFast;
        private CheckBox _chkFollow;               // la carte suit l'avion choisi
        private Panel _timePanel;
        private Timer _playTimer;
        private double _tNow;                       // heure du curseur (secondes), avec les décimales de la lecture
        private bool _syncBar;                      // vrai quand c'est le programme qui bouge la barre (pas l'utilisateur)
        private int _lastTick;
        private static readonly double[] PlaySpeeds = { 1, 2, 5, 10, 20, 50, 100, 200, 500 };
        private int _speedIdx = 1;                  // x2 au départ
        private ListView _list, _listNoPos;
        private TextBox _details;
        private BufferedPanel _iconBox;        // grosse icône de l'événement choisi (panneau de droite)
        private BufferedPanel _stats;          // ligne de bilan sous la barre des filtres
        private string _statsTitle = "";
        private readonly List<StatGroup> _statGroups = new List<StatGroup>();
        private int _statsLayoutW = -1;
        private readonly Font _statFont = new Font("Segoe UI", 9f);
        private readonly Font _statBold = new Font("Segoe UI", 9.5f, FontStyle.Bold);
        private readonly Font _statCap = new Font("Segoe UI", 7.5f, FontStyle.Bold);
        private readonly Font _statGlyph = new Font("Segoe UI Symbol", 10f);
        private Label _lblStatus, _lblNoPos, _lblList;
        private ToolTip _tip, _hoverTip;

        // Dessin
        private const float CellPx = 40f;          // les bombes et les décors se regroupent par case de 40 pixels (icônes deux fois plus grandes)
        private float _pxPerM = 0f;                // pixels par mètre au zoom actuel (sert à la taille réelle des camions et des cratères)
        private const float FamilyMinPx = 14f;                      // longueur mini à l'écran d'une icône (camion, char, bâtiment, avion au sol...)
        private const float CraterMinPx = 8f, CraterDefaultM = 6f;  // cratère : diamètre mini à l'écran, diamètre réel d'une bombe de 87 kg
        private float _iconScale = 2f;             // taille des icônes sur la carte : 2 = deux fois la taille d'origine, jusqu'à 4,5 quand on zoome fort
        private const float IconScaleMin = 2f, IconScaleMax = 3f;
        private float _iconMul = 1f;                // réglage des boutons "icônes plus petites / plus grandes" : multiplie la taille automatique (0,4 à 3)
        private Splitter _splitRight;
        private ComboBox _cmbStyle;
        private CheckBox _chkRight;                 // montre / cache la partie droite (listes et détails)
        private CheckBox _chkLeft;                  // montre / cache la colonne d'outils de gauche
        private CheckBox _chkLabels, _chkSpread;       // _chkSpread : icônes superposées écartées les unes des autres (trait pointillé vers le vrai lieu)
        private const float ZoomStartPxKm = 20f, ZoomFullPxKm = 2000f;   // en dessous de 20 pixels par km : taille normale ; à 2000 pixels par km (zoom maximum de la carte) : taille maximale
        private static readonly string[] Layers = { "bomb", "scenery", "static", "ground", "ship", "landing", "oldsar", "crew", "damage", "rescue", "air" };    // du dessous au dessus
        private readonly Font _fontBold = new Font("Segoe UI", 9f, FontStyle.Bold);
        private readonly StringFormat _sfCenter = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        private readonly List<Hit> _hits = new List<Hit>();
        private string _hoverKey = "";
        private class GroundMember { public string Name = "", Type = ""; public double X, Y; }
        private class GroundGroup { public string Side = ""; public List<GroundMember> Units = new List<GroundMember>(); }
        private readonly Dictionary<string, GroundGroup> _groundGroups = new Dictionary<string, GroundGroup>(StringComparer.OrdinalIgnoreCase);

        // Éléments liés au vol mis en avant, en second plan : l'avion qui l'a abattu, ceux qu'il a abattus (vol -> avions ; "" = tout le vol)
        private readonly Dictionary<string, HashSet<string>> _related = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        private readonly List<Map_Event> _gndKillers = new List<Map_Event>();      // événements du vol mis en avant causés par un tueur au sol
        private string _relKey = "\u0001";
        private CheckBox _chkHide;                 // coché : les événements hors du vol / avion choisi sont cachés ; sinon ils restent en filigrane
        private bool _comboFromClick;               // les listes du haut ont été réglées par un clic sur la carte (un clic dans le vide les remet sur "All")
        private string _focus = "";                 // vol mis en avant par un clic (icône, tracé, plan de vol) ; "" = aucun
        private string _focusNow = "";              // idem, figé au début de chaque dessin (le filtre "Flight" passe avant)
        // Vols AJOUTÉS à la mise en avant par CTRL + clic (le vol principal reste dans _selFlight / _focus). Vide = un seul vol, comme avant.
        private readonly HashSet<string> _extraFlights = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private string ExtraKey { get { return string.Join(",", _extraFlights.OrderBy(x => x, StringComparer.Ordinal).ToArray()); } }
        private bool InFocus(string flight) { return !string.IsNullOrEmpty(flight) && (string.Equals(flight, _focusNow, StringComparison.OrdinalIgnoreCase) || _extraFlights.Contains(flight)); }
        private bool InSelFlight(string flight) { return flight == _selFlight || (!string.IsNullOrEmpty(flight) && _extraFlights.Contains(flight)); }
        private Bitmap _ghost;                      // icône d'un événement hors du vol mis en avant : dessinée ici puis recopiée très pâle
        private ImageAttributes _ghostAttr;
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
            RemoveDestroyedTargetObjects();
            SetTargetObjectTimes();
            LoadTracks();
            SetupTimeBar();
            LoadAliases();
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
                ApplyOverlays();
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
                bool still = Math.Abs(e.X - _downPt.X) <= 4 && Math.Abs(e.Y - _downPt.Y) <= 4;
                if (e.Button == MouseButtons.Left && !_view.RulerEnabled && still)
                    MapClicked(e.Location);
                else if (e.Button == MouseButtons.Right && !_view.RulerEnabled && still)
                {
                    string rf = FlightAt(e.Location);          // clic droit sur un tracé / un plan de vol : ce vol sort de la sélection
                    if (rf != "") RemoveFlight(rf);
                }
            };
            _view.MouseMove += (s, e) => { if (e.Button == MouseButtons.None) HoverAt(e.Location); };

            _view.MouseLeave += (s, e) => { _hoverKey = ""; _hoverTip.Hide(_view); };

            // --- Panneau de droite : liste des événements, puis ceux sans position, puis la fiche ---
            var right = new Panel { Dock = DockStyle.Right, Width = 500, Padding = new Padding(6) };

            _lblList = new Label { Dock = DockStyle.Top, Height = 22, Font = new Font("Segoe UI", 9f, FontStyle.Bold), TextAlign = ContentAlignment.BottomLeft, Text = "Events" };
            _list = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, GridLines = true, HideSelection = false, MultiSelect = false };
            _list.Columns.Add("Time", 55);
            _list.Columns.Add("Event", 125);
            _list.Columns.Add("Name", 180);
            _list.Columns.Add("Type", 120);
            _list.SelectedIndexChanged += (s, e) =>
            {
                if (_syncSel || _list.SelectedItems.Count == 0) return;
                SelectEvent(_list.SelectedItems[0].Tag as Map_Event, true, true);       // clic dans la liste : on centre seulement, la sélection du haut ne bouge pas
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
                SelectEvent(_listNoPos.SelectedItems[0].Tag as Map_Event, false, true);
            };
            hint(_listNoPos, "Position unknown", "Aircraft lost with no position in the mission log: no ejection, no parachute seen. They can not be put on the map (nothing is invented). When the in-game tracker records the position of a kill, they will move to the map.");
            noPosPanel.Controls.Add(_listNoPos);
            noPosPanel.Controls.Add(_lblNoPos);

            _details = new TextBox { Dock = DockStyle.Bottom, Height = 190, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Font = new Font("Consolas", 9f), BackColor = Color.FromArgb(250, 251, 253) };
            hint(_details, "Details", "All the fields of the selected event (copyable).");

            // Ordre d'ajout : Fill, puis Bottom, puis Top (le dernier ajouté prend sa place en premier)
            // Bas du panneau : la grosse icône de l'événement choisi (à gauche), puis ses détails
            var detailsHost = new Panel { Dock = DockStyle.Bottom, Height = 190 };
            _details.Dock = DockStyle.Fill;
            _iconBox = new BufferedPanel { Dock = DockStyle.Left, Width = 104, BackColor = Color.FromArgb(246, 247, 249) };
            _iconBox.Paint += PaintSelectedIcon;
            hint(_iconBox, "Selected event", "The icon of the selected event, big, with its type and time.");
            detailsHost.Controls.Add(_details);
            detailsHost.Controls.Add(_iconBox);

            right.Controls.Add(_list);
            right.Controls.Add(noPosPanel);
            right.Controls.Add(detailsHost);
            right.Controls.Add(_lblList);

            // --- Bande de gauche : un bouton par famille d'icônes, puis plans de vol, fond, règle, cadrage ---
            var strip = new Panel { Dock = DockStyle.Left, Width = 38 + SystemInformation.VerticalScrollBarWidth, BackColor = Color.FromArgb(243, 243, 243), AutoScroll = true };      // ascenseur vertical : trop de boutons pour un petit écran
            int sy = 6;
            Func<int> nextY = () => { int r = sy; sy += 38; return r; };
            // Parties "carte" en haut (règle, cadrage, calques, noms, taille des icônes), puis les familles d'événements : même logique que les autres cartes
            _chkRuler = MakeToggle("⟷", nextY());
            _chkRuler.CheckedChanged += (s, e) => _view.RulerEnabled = _chkRuler.Checked;
            hint(_chkRuler, "Ruler", "Distance between two points: click A, move, click B. Right-click or Esc = clear. Untick to go back to normal clicks.");
            strip.Controls.Add(_chkRuler);

            var btnFit = MakeToolButton("⛶", nextY());
            btnFit.Click += (s, e) => FitShown();
            hint(btnFit, "Fit", "Zoom on the events that are shown now (all of them if none is shown).");
            strip.Controls.Add(btnFit);

            sy += 16;

            _chkBorders = MakeToggle("⬚", nextY());
            _chkBorders.Checked = true;
            _chkBorders.CheckedChanged += (s, e) => _view.SetDrawingsVisible("mission", _chkBorders.Checked);
            hint(_chkBorders, "Borders and drawings", "Lines and zones drawn in the mission editor (borders, front line...). Read from Init\\base_mission.miz of the campaign.");
            strip.Controls.Add(_chkBorders);

            _chkSam = MakeToggle("◎", nextY());
            _chkSam.Checked = true;
            _chkSam.CheckedChanged += (s, e) => _view.SetDrawingsVisible("threats", _chkSam.Checked);
            hint(_chkSam, "SAM threats", "Range circle of each surface-to-air site of the mission (blue or red = side of the site). One circle per group, around the launcher with the longest range. Ranges are the same as the mission map.");
            strip.Controls.Add(_chkSam);

            _chkAaa = MakeToggle("✹", nextY());
            _chkAaa.Checked = true;
            _chkAaa.CheckedChanged += (s, e) => _view.SetDrawingsVisible("aaa", _chkAaa.Checked);
            hint(_chkAaa, "AAA zones", "Zones AAA_ZONE_BLUE_* / AAA_ZONE_RED_* of the in-game script AAA_Barrage. Zones that overlap are drawn as ONE area to avoid, with a common outline. The colour is the side that DEFENDS the zone: it fires at the opposite side. A zone does nothing if the campaign has no AAA preset for its side.");
            strip.Controls.Add(_chkAaa);

            _chkRunways = MakeToggle("═", nextY());
            _chkRunways.Checked = true;
            _chkRunways.CheckedChanged += (s, e) => _view.ShowRunways = _chkRunways.Checked;
            hint(_chkRunways, "Runways", "Show or hide the runways of the airbases. Hidden: each base is a small dot with its name (the alias of Init\\various_table.lua if there is one).");
            strip.Controls.Add(_chkRunways);

            _chkPlans = MakeToggle("➤", nextY());
            _chkPlans.Checked = true;
            _chkPlans.CheckedChanged += (s, e) => _view.SetDrawingsVisible("ships", _chkPlans.Checked);
            hint(_chkPlans, "Flight plans", "Show or hide the flight plans in the background (faint). The flights of the filter (side, flight) are drawn stronger. Read from the mission file of this debriefing.");
            strip.Controls.Add(_chkPlans);

            _chkTracks = MakeToggle("∿", nextY());
            _chkTracks.Checked = true;
            _chkTracks.Enabled = _tracks.Count > 0;
            _chkTracks.CheckedChanged += (s, e) => _view.Invalidate();
            hint(_chkTracks, "Player tracks", "The real track flown by each player (a point every few seconds, from the file DebriefTracks <n>.txt written in game). Light blue = blue side, pink = red side. With a flight or an aircraft chosen, its track is drawn stronger with a mark every 5 minutes; the others are faint. Hover the line for time, altitude and speed. Greyed out = no track file for this mission.");
            strip.Controls.Add(_chkTracks);

            sy += 16;

            _chkLabels = MakeToggle("Tt", nextY());
            _chkLabels.Font = new Font("Segoe UI", 11f, FontStyle.Bold);
            _chkLabels.Checked = true;
            _chkLabels.CheckedChanged += (s, e) => _view.ShowLabels = _chkLabels.Checked;
            hint(_chkLabels, "Labels", "Show or hide ALL the names written on the map: bases, victory and track labels, ground killers, drawings. Untick for a clean map (the list on the right keeps the names).");
            strip.Controls.Add(_chkLabels);

            _chkSpread = MakeToggle("\u2735", nextY());
            _chkSpread.Checked = true;
            _chkSpread.CheckedChanged += (s, e) => _view.Invalidate();
            hint(_chkSpread, "Spread icons", "When icons are on top of each other, move them apart so none touches another. A dotted line goes from the real place of the event (small dot) to its icon. Zoomed out, only the pile of the event you clicked is spread; zoomed in, all piles are spread. Untick to see every icon exactly where it happened.");
            strip.Controls.Add(_chkSpread);

            var btnIconSmall = MakeToolButton("⇙", nextY());
            btnIconSmall.Click += (s, e) => ChangeIconSize(0.8f);
            hint(btnIconSmall, "Smaller icons", "Make all the icons of the events smaller (each click = 20 % less). They still grow a little when you zoom in.");
            strip.Controls.Add(btnIconSmall);

            var btnIconBig = MakeToolButton("⇗", nextY());
            btnIconBig.Click += (s, e) => ChangeIconSize(1.25f);
            hint(btnIconBig, "Bigger icons", "Make all the icons of the events bigger (each click = 25 % more).");
            strip.Controls.Add(btnIconBig);

            sy += 16;

            AddKindToggle(strip, hint, nextY(), "air", "✈", "Aircraft lost", "Planes and helicopters lost: silhouette with a cross, blue or red = side of the lost aircraft. A PALE icon is an approximate position (where the crew landed, or the start position of the aircraft). Aircraft with no position at all are in the list 'Position unknown'.");
            AddKindToggle(strip, hint, nextY(), "ground", "■", "Vehicles destroyed", "Ground vehicles destroyed: tank, truck, infantry, SAM, gun or radar icon (chosen from the unit type), with a cross.");
            AddKindToggle(strip, hint, nextY(), "ship", "⚓", "Ships destroyed", "Ships destroyed: ship icon with a cross.");
            AddKindToggle(strip, hint, nextY(), "static", "◆", "Static objects destroyed", "Static objects destroyed (parked aircraft, crates, barrels, sandbags, buildings, trucks...): the icon follows the object type, with a cross.");
            AddKindToggle(strip, hint, nextY(), "scenery", "⌂", "Scenery destroyed", "Scenery objects destroyed: building, bridge, railway station, ship, tree... chosen from the object name. Many objects close together are grouped in a circle with their number: click it to zoom in.");
            AddKindToggle(strip, hint, nextY(), "crew", "☂", "Crew", "Parachute = ejection point. Person = pilot of this mission waiting for rescue (SAR, green). Black disc with a cross = pilot killed.");
            AddKindToggle(strip, hint, nextY(), "rescue", "✚", "Rescues (SAR / CSAR)", "Green cross = a helicopter picked up a pilot. The icon is where the pilot was waiting. Click it to see the helicopter and the rescued pilot.");
            AddKindToggle(strip, hint, nextY(), "landing", "H", "Helicopter and transport landings", "A helicopter (or a Transport / airlift flight) landed somewhere that is NOT its own base: H in a square. One point per flight and per place. The details tell the place.");
            AddKindToggle(strip, hint, nextY(), "damage", "⚠", "Aircraft damaged", "Aircraft that were hit but NOT lost: aircraft icon with an orange warning sign, at the point where the worst hit happened. The details tell the life left and who hit it.");
            AddKindToggle(strip, hint, nextY(), "oldsar", "⟲", "Pilots from earlier missions", "Pilots still waiting for rescue from PREVIOUS missions (grey person). Their name tells the mission number.");
            AddKindToggle(strip, hint, nextY(), "bomb", "●", "Bomb impacts", "Bomb impacts (orange dot). Many impacts close together are grouped in a circle with their number: click it to zoom in.");

            sy += 16;


            _chkTObj = MakeToggle("▣", nextY());
            _chkTObj.Checked = true;
            _chkTObj.Enabled = false;          // activé quand le fichier contient des bâtiments de la target list près des bombes
            _chkTObj.CheckedChanged += (s, e) => _view.Invalidate();
            hint(_chkTObj, "Intact target objects", "Show or hide ALL the intact target objects (buildings of the target list near the bomb impacts that are still standing). Empty icon with a green check = intact (a filled icon with a cross = destroyed). Small square when zoomed out. Grey = already destroyed before this mission.");
            strip.Controls.Add(_chkTObj);

            // La molette fait défiler la bande aussi quand la souris est sur un bouton
            MouseEventHandler wheel = (s, e) => strip.AutoScrollPosition = new Point(0, -strip.AutoScrollPosition.Y - e.Delta / 2);
            strip.MouseWheel += wheel;
            foreach (Control c in strip.Controls) c.MouseWheel += wheel;

            // --- Barre du haut : filtres ---
            Panel top;
            Map_UI_SideToggle tglLeft, tglRight;
            Panel topBar = Map_UI_SideToggle.Wrap(80, out top, out tglLeft, out tglRight);       // les 2 boutons de colonne, comme sur les autres cartes
            _chkLeft = tglLeft;
            _chkRight = tglRight;
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

            for (int r = 0; r < 3; r++)
            {
                int row = r, ry = 8 + 41 * r;
                var lbl = new Label { Text = RowTitle[r], Location = new Point(156, ry + 4), AutoSize = true };
                var cf = new ComboBox { Location = new Point(240, ry), Width = 430, DropDownWidth = 640, DropDownStyle = ComboBoxStyle.DropDownList };
                var cu = new ComboBox { Location = new Point(680, ry), Width = 398, DropDownWidth = 640, DropDownStyle = ComboBoxStyle.DropDownList, Enabled = false };
                _fCombo[r] = cf; _uCombo[r] = cu;

                cf.SelectionChangeCommitted += (s, e) =>
                {
                    FlightItem chosen = cf.SelectedItem as FlightItem;
                    _comboFromClick = false;
                    _extraFlights.Clear();
                    _selFlight = (cf.SelectedIndex <= 0 || chosen == null) ? "" : chosen.Key;
                    _focus = "";
                    _selFromList = false;
                    ResetOtherRows(row);                    // une seule ligne active à la fois
                    FillUnits();
                    ApplyFilters();
                    if (_selected == null) _details.Text = FlightSummary();
                };
                cu.SelectionChangeCommitted += (s, e) =>
                {
                    FlightItem chosen = cu.SelectedItem as FlightItem;
                    _comboFromClick = false;
                    _selUnit = (cu.SelectedIndex <= 0 || chosen == null) ? "" : chosen.Key;
                    ApplyFilters();
                    if (_selected == null) _details.Text = FlightSummary();
                };

                if (r == 0)
                {
                    hint(cf, "Aircraft flight", "Put one whole flight of airplanes (all its aircraft) in focus: its events, flight plan and track are highlighted, the rest is faded (or hidden, see \"Hide the rest\"). Clicking an icon, a track or a flight plan on the map fills this list for you. CTRL + click on a track or a flight plan adds a flight to the focus; right-click takes one out (at the start all flights count). Only one of the three lines (aircraft, helicopters, ground) is active at a time.");
                    hint(cu, "Aircraft", "Put one aircraft of the chosen flight in focus (its loss, its ejection, its pilot, its track). Clicking an icon or a track on the map fills this list for you.");
                }
                else if (r == 1)
                {
                    hint(cf, "Helicopter flight", "Put one whole flight of helicopters in focus (its rescues, landings, losses, kills). Choosing here puts the other two lines back on \"All\".");
                    hint(cu, "Chopper", "Put one helicopter of the chosen flight in focus.");
                }
                else
                {
                    hint(cf, "Ground shooters", "Ground groups that destroyed something in this mission: SAM, AAA, guns, artillery, ships... Put one in focus: what it destroyed is listed and its position is drawn. Only groups with at least one kill are in this list. Choosing here puts the other two lines back on \"All\".");
                    hint(cu, "Ground unit", "Put one unit of the chosen ground group in focus (only the units that destroyed something).");
                }

                top.Controls.Add(lbl); top.Controls.Add(cf); top.Controls.Add(cu);
                if (r > 0) { _rowCtrls.Add(lbl); _rowCtrls.Add(cf); _rowCtrls.Add(cu); }
            }
            _cmbFlight = _fCombo[0];
            _cmbUnit = _uCombo[0];

            _chkKills = new CheckBox { Text = "+ what it destroyed", Location = new Point(1094, 10), AutoSize = true, Checked = true };
            _chkKills.CheckedChanged += (s, e) => { if (_selFlight != "") ApplyFilters(); };
            hint(_chkKills, "What it destroyed", "With a flight or an aircraft chosen: also show the kills made by it (aircraft or ground units it shot down). Unticked: only what happened TO it.");
            top.Controls.Add(_chkKills);

            _chkHide = new CheckBox { Text = "Hide the rest", Location = new Point(1246, 10), AutoSize = true };
            _chkHide.CheckedChanged += (s, e) => ApplyFilters();
            hint(_chkHide, "Hide the rest", "Unticked (default): the events that are not about the chosen flight / aircraft stay on the map, faded. Ticked: they are removed from the map and from the list.");
            top.Controls.Add(_chkHide);

            // 2e ligne : style de la carte web et ses outils (comme les autres cartes), puis le bouton qui montre / cache la partie droite
            var lblStyle = new Label { Text = "Map style:", Location = new Point(10, 53), AutoSize = true };
            top.Controls.Add(lblStyle);
            _cmbStyle = new ComboBox { Location = new Point(80, 49), Width = 290, DropDownStyle = ComboBoxStyle.DropDownList };
            foreach (Map_Style st in Map_Web.Styles) _cmbStyle.Items.Add(st.Label);
            _cmbStyle.SelectedIndex = Math.Max(0, Map_Web.Styles.FindIndex(x => x.Id == Map_Web.CurrentStyle.Id));
            _cmbStyle.SelectionChangeCommitted += (s, e) => StyleChanged();
            hint(_cmbStyle, "Map style", "Look of the web map. The style is remembered for each campaign (same choice as the other maps). Satellite and shaded relief have no names at all.");
            top.Controls.Add(_cmbStyle);

            _chkBg = MakeToggle("▤", 0);
            _chkBg.Location = new Point(378, 46);
            _chkBg.CheckedChanged += (s, e) => _view.ShowBackground = _chkBg.Checked;
            hint(_chkBg, "Web map", "Show or hide the web map (the one of the mission map). If there is none, open the Map window of this campaign once to build it.");
            top.Controls.Add(_chkBg);

            var btnArea = MakeToolButton("\u2316", 0);
            btnArea.Location = new Point(416, 46);
            btnArea.Click += (s, e) => _view.LoadVisibleArea();
            hint(btnArea, "Load visible area", "Download the map tiles of what you see now, even far from the conflict zone. Then zoom in on this area and the detailed tiles come by themselves.");
            top.Controls.Add(btnArea);

            var btnDetail = MakeToolButton("\u229E", 0);
            btnDetail.Location = new Point(454, 46);
            btnDetail.Click += (s, e) => _view.ForceDetailHere();
            hint(btnDetail, "Detail now", "Load the detailed tiles of the zoom level you are at, right now (click here when the map looks blurry).");
            top.Controls.Add(btnDetail);

            // Bouton qui cache / montre les lignes Helicopters et Ground
            _btnSelRows = new Button { Size = new Size(200, 28), Location = new Point(500, 46), FlatStyle = FlatStyle.System };
            _btnSelRows.Click += (s, e) => SetSelRows(!_selRowsOpen);
            hint(_btnSelRows, "Helicopters / Ground lines", "Show or hide the two extra selection lines (helicopters, ground shooters). Hidden = more room for the map; a choice made on those lines is put back on \"All\".");
            top.Controls.Add(_btnSelRows);

            foreach (Control c in new Control[] { lblStyle, _cmbStyle, _chkBg, btnArea, btnDetail, _btnSelRows }) { c.Tag = c.Top; _mapRowCtrls.Add(c); }
            _topBarOuter = topBar;
            LayoutSelRows();

            _chkRight.CheckedChanged += (s, e) => { right.Visible = _chkRight.Checked; _splitRight.Visible = _chkRight.Checked; };
            hint(_chkRight, "Events panel", "Show or hide the right part: list of events, aircraft with no position, details of the selected event. Hidden = a bigger map.");
            _chkLeft.CheckedChanged += (s, e) => strip.Visible = _chkLeft.Checked;
            hint(_chkLeft, "Tools column", "Show or hide the column of buttons on the left of the map (event kinds, ruler, zoom...). Hidden = a bigger map.");

            // --- Ligne d'état ---
            _lblStatus = new Label { Dock = DockStyle.Bottom, Height = 26, Padding = new Padding(8, 5, 8, 0) };

            // --- Barre de temps : lecteur, curseur sur la durée des traces, longueur de la traîne ---
            _timePanel = new Panel { Dock = DockStyle.Bottom, Height = 38, BackColor = Color.FromArgb(243, 243, 243) };
            _chkTime = new CheckBox { Text = "Time", Location = new Point(10, 10), AutoSize = true, Enabled = false };
            _chkTime.CheckedChanged += (s, e) =>
            {
                _barTime.Enabled = _chkTime.Checked; _barTrail.Enabled = _chkTime.Checked;
                if (!_chkTime.Checked) StopPlay();
                _view.Invalidate();
            };
            hint(_chkTime, "Time", "Off = the whole tracks and all the events. On = the map shows the situation at the time of the cursor: each track shows only its last part, and each icon appears at the time of its event.");

            _btnSlow = new Button { Text = "\u25C0\u25C0", Location = new Point(70, 6), Size = new Size(34, 26), FlatStyle = FlatStyle.System };
            _btnPlay = new Button { Text = "\u25B6", Location = new Point(106, 6), Size = new Size(38, 26), FlatStyle = FlatStyle.System, Enabled = false };
            _btnFast = new Button { Text = "\u25B6\u25B6", Location = new Point(146, 6), Size = new Size(34, 26), FlatStyle = FlatStyle.System };
            _lblSpeed = new Label { Location = new Point(184, 11), AutoSize = true, Font = new Font("Segoe UI", 9f, FontStyle.Bold), Text = "x2" };
            _btnSlow.Click += (s, e) => { if (_speedIdx > 0) _speedIdx--; _lblSpeed.Text = "x" + PlaySpeeds[_speedIdx]; };
            _btnFast.Click += (s, e) => { if (_speedIdx < PlaySpeeds.Length - 1) _speedIdx++; _lblSpeed.Text = "x" + PlaySpeeds[_speedIdx]; };
            _btnPlay.Click += (s, e) => { if (_playTimer.Enabled) StopPlay(); else StartPlay(); };
            hint(_btnSlow, "Slower", "Slow the time down (x500, x200 ... x1).");
            hint(_btnFast, "Faster", "Speed the time up (x1 ... x500).");
            hint(_btnPlay, "Play / pause", "Plays the mission time from the cursor. Starts again from the beginning when the cursor is at the end.");
            _playTimer = new Timer { Interval = 20 };
            _playTimer.Tick += (s, e) => PlayTick();

            _barTime = new TrackBar { Location = new Point(232, 3), Width = 360, Height = 32, TickStyle = TickStyle.None, Enabled = false, SmallChange = 10, LargeChange = 60 };
            _barTime.ValueChanged += (s, e) => { if (!_syncBar) SetTime(_barTime.Value); };
            _lblTime = new Label { Location = new Point(600, 11), AutoSize = true, Font = new Font("Segoe UI", 9f, FontStyle.Bold), Text = "T+0:00" };
            var lblTrailCap = new Label { Text = "Trail:", Location = new Point(680, 11), AutoSize = true };
            _barTrail = new TrackBar { Location = new Point(720, 3), Width = 130, Height = 32, TickStyle = TickStyle.None, Enabled = false, Minimum = 1, Maximum = 41, Value = 10, SmallChange = 1, LargeChange = 4 };
            _lblTrail = new Label { Location = new Point(856, 11), AutoSize = true, Text = "5:00" };
            _chkFollow = new CheckBox { Text = "Follow", Location = new Point(905, 10), AutoSize = true };
            _chkFollow.CheckedChanged += (s, e) => { UpdatePlayCache(); if (_chkFollow.Checked) FollowSelected(); };
            hint(_chkFollow, "Follow", "The map follows the chosen aircraft (choose a flight or an aircraft first): the aircraft stays in the middle and the map moves. Uncheck it to move the map yourself.");
            _barTrail.ValueChanged += (s, e) =>
            {
                double sec = TrailSeconds();
                _lblTrail.Text = sec <= 0 ? "All" : FmtT(sec).Replace("T+", "");
                if (_chkTime.Checked) _view.Invalidate();
            };
            hint(_barTrail, "Trail length", "How long the track stays visible behind the aircraft: from 30 seconds to 20 minutes, or All (the whole track before the cursor).");
            _timePanel.Controls.Add(_chkTime);
            _timePanel.Controls.Add(_btnSlow);
            _timePanel.Controls.Add(_btnPlay);
            _timePanel.Controls.Add(_btnFast);
            _timePanel.Controls.Add(_lblSpeed);
            _timePanel.Controls.Add(_barTime);
            _timePanel.Controls.Add(_lblTime);
            _timePanel.Controls.Add(lblTrailCap);
            _timePanel.Controls.Add(_barTrail);
            _timePanel.Controls.Add(_lblTrail);
            _timePanel.Controls.Add(_chkFollow);

            Controls.Add(_view);
            _splitRight = new Splitter { Dock = DockStyle.Right, Width = 6, MinSize = 250, MinExtra = 300 };
            Controls.Add(_splitRight);
            Controls.Add(right);
            Controls.Add(strip);

            // Ligne de bilan, sous la barre des filtres : ce que le vol / l'avion choisi a détruit, perdu, etc.
            _stats = new BufferedPanel { Dock = DockStyle.Top, Height = 56, BackColor = Color.FromArgb(252, 252, 253) };
            _stats.Paint += PaintStats;
            _stats.Resize += (s, e) => { if (_stats.ClientSize.Width != _statsLayoutW) LayoutStats(); };
            hint(_stats, "Summary", "What the chosen side / flight / aircraft did and suffered in this mission. A whole flight = the sum of all its aircraft; one aircraft chosen = that aircraft only. Grey = zero. Destroyed = what it shot down or destroyed (friendly fire is counted apart).");
            Controls.Add(_stats);
            Controls.Add(topBar);
            Controls.Add(_timePanel);
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

        private void ChangeIconSize(float factor)
        {
            _iconMul = Math.Max(0.4f, Math.Min(3f, _iconMul * factor));
            _lblStatus.Text = "Icon size: " + Math.Round(_iconMul * 100) + " %";
            _view.Invalidate();
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
                case "rescue": return "rescue";
                case "landing": return "landing";
                case "damage": return "damage";
                case "bomb": return "bomb";
                case "target_obj": return "tobj";
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

                    // Menaces SAM et zones AAA de la mission jouée (absentes des anciens fichiers : les SAM viennent alors du .miz actuel)
                    LuaTable sams = root["samSites"] as LuaTable;
                    if (sams != null)
                    {
                        _samInFile = true;
                        foreach (object sk in sams.Keys)
                        {
                            LuaTable sm = sams[sk] as LuaTable;
                            double sx, sy, sr;
                            if (sm == null || !Num(sm, "x", out sx) || !Num(sm, "y", out sy) || !Num(sm, "range", out sr) || sr <= 0) continue;
                            _samDraw.Add(MakeCircle(Str(sm, "name"), Str(sm, "label") + (sm["hidden"] is bool && (bool)sm["hidden"] ? " (hidden)" : ""), "threats", sx, sy, sr, Str(sm, "side") == "red", false));
                        }
                    }
                    // Éléments de la target list (bâtiments ajoutés = static, bâtiments de la carte = MapObject) proches d'un bombardement et encore debout
                    LuaTable tobjs = root["targetObjects"] as LuaTable;
                    if (tobjs != null)
                    {
                        int nTo = 0;
                        foreach (object tk in tobjs.Keys)
                        {
                            LuaTable to = tobjs[tk] as LuaTable;
                            double tx, ty, td;
                            if (to == null || !Num(to, "x", out tx) || !Num(to, "y", out ty)) continue;
                            var te = new Map_Event
                            {
                                Id = --nTo, Kind = "target_obj", Name = Str(to, "name"), Target = Str(to, "target"), Side = Str(to, "side"),
                                Category = Str(to, "class"), Type = Str(to, "type"), Status = Str(to, "state"),
                                HasPos = true, X = tx, Y = ty, PosSrc = "scen"
                            };
                            if (Num(to, "dist", out td)) te.Info = td.ToString("0", CultureInfo.InvariantCulture);
                            te.Group = "tobj";
                            _targetObjs.Add(te);
                        }
                    }

                    // Groupes au sol qui ont tué quelque chose : tous leurs éléments (position de départ). Absent des anciens fichiers.
                    LuaTable ggs = root["groundGroups"] as LuaTable;
                    if (ggs != null)
                    {
                        foreach (object gk in ggs.Keys)
                        {
                            LuaTable gt = ggs[gk] as LuaTable;
                            if (gt == null) continue;
                            var gg = new GroundGroup { Side = Str(gt, "side") };
                            LuaTable us = gt["units"] as LuaTable;
                            if (us != null)
                                foreach (object uk in us.Keys)
                                {
                                    LuaTable ut = us[uk] as LuaTable;
                                    double ux, uy;
                                    if (ut == null || !Num(ut, "x", out ux) || !Num(ut, "y", out uy)) continue;
                                    gg.Units.Add(new GroundMember { Name = Str(ut, "name"), Type = Str(ut, "type"), X = ux, Y = uy });
                                }
                            _groundGroups[gk.ToString()] = gg;
                        }
                    }

                    LuaTable aaas = root["aaaZones"] as LuaTable;
                    if (aaas != null)
                    {
                        foreach (object ak in aaas.Keys)
                        {
                            LuaTable az = aaas[ak] as LuaTable;
                            double ax, ay, ar;
                            if (az == null || !Num(az, "x", out ax) || !Num(az, "y", out ay) || !Num(az, "radius", out ar) || ar <= 0) continue;
                            _aaaZones.Add(new AaaZone { Name = Str(az, "name").Replace("AAA_ZONE_", ""), Owner = Str(az, "owner") == "red" ? "red" : "blue", X = ax, Y = ay, R = ar });
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
                        e.KillerGroup = Str(t, "killerGroup");
                        double kx, ky;
                        if (Num(t, "killerX", out kx) && Num(t, "killerY", out ky)) { e.HasKillerPos = true; e.KX = kx; e.KY = ky; }
                        e.Friendly = t["friendly"] is bool && (bool)t["friendly"];
                        e.Target = Str(t, "target"); e.Element = Str(t, "element"); e.Status = Str(t, "status");
                        e.Weapon = Str(t, "weapon"); e.Cause = Str(t, "cause"); e.Info = Str(t, "info"); e.Unit = Str(t, "unit");
                        if (Num(t, "mass", out d)) e.Mass = d;
                        if (Num(t, "health", out d)) e.Health = d;
                        if (Num(t, "missionNo", out d)) e.MissionNo = (int)d;
                        object cur = t["current"];
                        e.Current = !(cur is bool) || (bool)cur;
                        e.Group = GroupOf(e);
                        if (e.Kind == "static_kill" && RxDecorStatic.IsMatch((e.Type ?? "") + " " + (e.Name ?? ""))) continue;        // sacs de sable, filets de camouflage : ni comptés ni affichés (anciens fichiers)
                        _all.Add(e);
                    }
                }

                _all.Sort((a, b) => { int c = a.T.CompareTo(b.T); return c != 0 ? c : a.Id.CompareTo(b.Id); });

                // Camp de chaque vol : lu dans les événements (victime ou tueur)
                foreach (Map_Event e in _all)
                {
                    if (e.Kind == "air_lost" && e.Name != "" && e.Type != "" && !_unitType.ContainsKey(e.Name)) _unitType[e.Name] = e.Type;
                    if (e.Killer != "" && e.KillerFlight != "" && e.KillerType != "" && !_unitType.ContainsKey(e.Killer)) _unitType[e.Killer] = e.KillerType;
                    if (e.Flight != "" && e.Side != "" && !_flightSide.ContainsKey(e.Flight)) _flightSide[e.Flight] = e.Side;
                    if (e.KillerFlight != "" && e.KillerSide != "" && !_flightSide.ContainsKey(e.KillerFlight)) _flightSide[e.KillerFlight] = e.KillerSide;
                }

                // Tireurs au sol qui ont détruit quelque chose : leur groupe sert de "vol" pour la 3e ligne de choix
                foreach (Map_Event e in _all)
                {
                    if (e.Killer == "" || e.KillerFlight != "" || e.KillerGroup == "" || e.Killer == e.Name) continue;     // un statique qui saute seul n'est pas un tireur
                    e.KillerFlight = e.KillerGroup;
                    e.GroundKiller = true;
                    if (!_gndSide.ContainsKey(e.KillerGroup)) _gndSide[e.KillerGroup] = e.KillerSide;
                    if (e.KillerType != "" && !_unitType.ContainsKey(e.Killer)) _unitType[e.Killer] = e.KillerType;
                }
            }
            catch (Exception ex)
            {
                _loadError = "The events file could not be read: " + ex.Message;
                FormUtils.LogRegister("Map_Events_Form | lecture : " + ex);
            }
        }

        // Init\various_table.lua : blocs BaseNameAlias et TypeAlias, lignes ["nom"] = "alias"
        private void LoadAliases()
        {
            _typeAlias.Clear();
            _baseAlias.Clear();
            try
            {
                string file = Path.Combine(ParamConf.PATH_SavedGames_DCS, @"Mods\tech\DCE\Missions\Campaigns", _campaign, "Init", "various_table.lua");
                if (!File.Exists(file)) return;

                string block = "";
                foreach (string raw in File.ReadAllLines(file))
                {
                    string line = raw.Trim();
                    if (line.StartsWith("--")) continue;
                    if (line.StartsWith("BaseNameAlias")) { block = "base"; continue; }
                    if (line.StartsWith("TypeAlias")) { block = "type"; continue; }
                    if (block == "") continue;
                    if (line.StartsWith("}")) { block = ""; continue; }

                    Match m = Regex.Match(line, "^\\[\"(.+?)\"\\]\\s*=\\s*\"(.*?)\"");
                    if (!m.Success) continue;
                    if (block == "base") _baseAlias[m.Groups[1].Value] = m.Groups[2].Value;
                    else _typeAlias[m.Groups[1].Value] = m.Groups[2].Value;
                }
            }
            catch (Exception ex)
            {
                FormUtils.LogRegister("Map_Events_Form | various_table : " + ex.Message);
            }
        }

        // Type d'avion tel qu'on l'affiche : l'alias s'il existe, sinon le type DCS exact
        private string TypeName(string type)
        {
            if (string.IsNullOrEmpty(type)) return "";
            string a;
            return _typeAlias.TryGetValue(type, out a) && a != "" ? a : type;
        }

        private string TypeOfUnit(string unit)
        {
            string t;
            return _unitType.TryGetValue(unit ?? "", out t) ? TypeName(t) : "";
        }

        // Types différents des avions d'un vol : "F-105D" ou "F-4E, F-105D"
        private string TypesOfFlight(string flight)
        {
            var list = new List<string>();
            foreach (string n in UnitsOfFlight(flight))
            {
                string t = TypeOfUnit(n);
                if (t != "" && !list.Contains(t)) list.Add(t);
            }
            return string.Join(", ", list);
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
                        Alias = BaseAlias(kv.Key, a.AliasName),
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

        private string BaseAlias(string name, string fromDb)
        {
            string a;
            if (_baseAlias.TryGetValue(name, out a) && a != "") return a;
            return string.IsNullOrWhiteSpace(fromDb) ? "" : fromDb;
        }

        // Nouveau style de tuiles : même calage que les autres cartes, on recharge le fond de ce style
        private void StyleChanged()
        {
            Map_Web.UseCampaign(_campaign);
            Map_Web.CurrentStyle = Map_Web.Styles[_cmbStyle.SelectedIndex];

            if (_background != null)
            {
                _view.SetBackground(null);
                _background.Dispose();
                _background = null;
            }

            Map_Background cached = null;
            try { cached = Map_Web.TryLoadCached(_campaign); }
            catch (Exception ex) { FormUtils.LogRegister("Map_Events_Form | style : " + ex.Message); }

            if (cached != null) UseBackground(cached);
            else
            {
                _view.ShowBackground = false;
                _lblStatus.Text = "Web map: no map in the cache for this style. Open the Map window of this campaign once to build it.";
            }
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
        // Un cercle (menace SAM ou zone AAA) : même style que la carte de mission, les zones AAA en pointillés
        private static Map_Drawing MakeCircle(string name, string label, string group, double x, double y, double radius, bool red, bool dashed)
        {
            Color c = red ? Color.FromArgb(255, 90, 80) : Color.FromArgb(70, 150, 255);
            var d = new Map_Drawing
            {
                Name = name, Label = label, Group = group, Kind = "Circle", Radius = radius, RangeM = radius,
                Dashed = dashed, Line = Color.FromArgb(dashed ? 230 : 220, c), Fill = Color.FromArgb(dashed ? 40 : 28, c), Thickness = dashed ? 2f : 1.5f
            };
            d.Points.Add(new double[] { x, y });
            return d;
        }

        // ------------------------------------------------------------------
        //  Traces de vol des joueurs (DebriefTracks <n>.txt)
        //    H;1                                   version
        //    U;<id>;<unité>;<joueur>;<type>;<camp>  une fois par avion
        //    T;<t>;<id>;<x>;<y>;<z>                 un point (t en secondes de mission, x nord, y est, z altitude en m)
        //    F;<t>                                  dernière ligne = fichier complet (absente = crash)
        //  Les ids peuvent avoir des trous. Une dernière ligne coupée est ignorée.
        // ------------------------------------------------------------------

        private void LoadTracks()
        {
            try
            {
                string name = Path.GetFileName(_file);
                string trackFile = Path.Combine(Path.GetDirectoryName(_file), Regex.Replace(name, @"^DebriefEvents ", "DebriefTracks ", RegexOptions.IgnoreCase).Replace(".lua", ".txt"));
                if (!File.Exists(trackFile)) return;

                string all;
                using (var fs = new FileStream(trackFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (var sr = new StreamReader(fs, Encoding.UTF8))
                    all = sr.ReadToEnd();

                // Après le dernier saut de ligne, il ne reste qu'une ligne peut-être coupée : on la lit seulement si c'est la ligne F
                string[] lines = all.Split('\n');
                var byId = new Dictionary<int, Map_Track>();
                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i].TrimEnd('\r');
                    bool lastPiece = i == lines.Length - 1;
                    if (line.Length < 2 || (lastPiece && !line.StartsWith("F;", StringComparison.Ordinal))) continue;

                    string[] f = line.Split(';');
                    int id;
                    double t, x, y, z;
                    switch (f[0])
                    {
                        case "U":
                            if (f.Length >= 6 && int.TryParse(f[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out id))
                            {
                                var tr = new Map_Track { Id = id, Unit = f[2], Player = f[3] == "nil" ? "" : f[3], Type = f[4], Side = f[5].Trim().ToLowerInvariant() };
                                tr.Flight = FlightOfUnit(tr.Unit);
                                byId[id] = tr;
                                _tracks.Add(tr);
                            }
                            break;
                        case "T":
                            Map_Track target;
                            if (f.Length >= 6
                                && int.TryParse(f[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out id)
                                && double.TryParse(f[1], NumberStyles.Float, CultureInfo.InvariantCulture, out t)
                                && double.TryParse(f[3], NumberStyles.Float, CultureInfo.InvariantCulture, out x)
                                && double.TryParse(f[4], NumberStyles.Float, CultureInfo.InvariantCulture, out y)
                                && double.TryParse(f[5], NumberStyles.Float, CultureInfo.InvariantCulture, out z)
                                && byId.TryGetValue(id, out target))
                                target.Pts.Add(new double[] { t, x, y, z });
                            break;
                        case "F":
                            _tracksComplete = true;
                            break;
                    }
                }

                // Les joueurs des traces complètent ceux du fichier debrief (utile pour les anciens fichiers)
                foreach (Map_Track tr in _tracks)
                {
                    if (tr.Type != "" && !_unitType.ContainsKey(tr.Unit)) _unitType[tr.Unit] = tr.Type;      // le type exact vient aussi du fichier de traces
                    if (tr.Player == "") continue;
                    if (!_unitPlayer.ContainsKey(tr.Unit)) _unitPlayer[tr.Unit] = tr.Player;
                    AddPlayer(tr.Flight, tr.Player);
                    if (tr.Side != "" && !_flightSide.ContainsKey(tr.Flight)) _flightSide[tr.Flight] = tr.Side;
                }
            }
            catch (Exception ex)
            {
                FormUtils.LogRegister("Map_Events_Form | traces : " + ex.Message);
            }
            if (_chkTracks != null) _chkTracks.Enabled = _tracks.Count > 0;
        }

        private static Color TrackColor(string side)
        {
            if (side == "blue") return Color.FromArgb(0, 200, 255);
            if (side == "red") return Color.FromArgb(255, 80, 200);
            return Color.FromArgb(150, 220, 60);
        }

        // Vol mis en avant : le vol choisi, sinon celui de l'événement sélectionné
        private string TrackFocusFlight()
        {
            return FocusFlight();
        }

        // Vol mis en avant : celui choisi dans la liste, sinon celui sur lequel on a cliqué
        private string FocusFlight()
        {
            return _selFlight != "" ? _selFlight : _focus;
        }

        // L'événement fait partie de ce qui est mis en avant (vol, et avion si un avion est choisi) : ce qui lui est arrivé,
        // et - si "+ what it destroyed" est coché - ce qu'il a fait (bombe, tir...)
        private bool IsFocused(Map_Event e)
        {
            if (_focusNow == "") return true;
            bool unit = _selFlight != "" && _selUnit != "";
            if (InFocus(e.Flight) && (!unit || UnitNameOf(e) == _selUnit)) return true;
            return _chkKills.Checked && InFocus(e.KillerFlight) && (!unit || e.Killer == _selUnit);
        }

        // Vols / avions liés au vol mis en avant : celui qui l'a abattu, et ceux qu'il a abattus. Calculé quand la mise en avant change.
        private void UpdateRelated()
        {
            if (_chkKills == null) return;
            string key = _focusNow + "|" + _selUnit + "|" + (_selFlight != "") + "|" + _chkKills.Checked + "|" + ExtraKey;
            if (key == _relKey) return;
            _relKey = key;
            _related.Clear();
            _gndKillers.Clear();
            if (_focusNow == "") return;

            bool unitSel = _selFlight != "" && _selUnit != "";
            var seenGround = new HashSet<string>();
            foreach (Map_Event e in _all)
            {
                // ce qui lui est arrivé : qui l'a fait ?
                bool subj = InFocus(e.Flight) && (!unitSel || UnitNameOf(e) == _selUnit);
                if (subj && e.Killer != "")
                {
                    if (e.KillerFlight != "" && !e.GroundKiller)
                    {
                        if (!InFocus(e.KillerFlight)) AddRelated(e.KillerFlight, e.Killer);
                    }
                    else if (e.KillerGroup != "" && e.HasKillerPos && seenGround.Add(e.Killer)) _gndKillers.Add(e);
                }

                // le tireur au sol mis en avant : sa position est dessinée
                if (e.GroundKiller && InFocus(e.KillerFlight) && e.HasKillerPos && seenGround.Add(e.Killer)) _gndKillers.Add(e);

                // ce qu'il a fait : qui l'a subi ?
                if (_chkKills.Checked && InFocus(e.KillerFlight) && (!unitSel || e.Killer == _selUnit)
                    && e.Flight != "" && !InFocus(e.Flight))
                    AddRelated(e.Flight, UnitNameOf(e));
            }
        }

        private void AddRelated(string flight, string unit)
        {
            HashSet<string> set;
            if (!_related.TryGetValue(flight, out set)) { set = new HashSet<string>(); _related[flight] = set; }
            set.Add(unit ?? "");
        }

        private bool IsRelatedUnit(string flight, string unit)
        {
            HashSet<string> set;
            return flight != "" && _related.TryGetValue(flight, out set) && (set.Contains("") || set.Contains(unit));
        }

        // 2 = le vol / avion mis en avant, 1 = lié à lui (tueur, victimes), 0 = le reste (en filigrane)
        private int FocusRank(Map_Event e)
        {
            if (_focusNow == "" || IsFocused(e)) return 2;
            if (_related.Count > 0 && ((e.Flight != "" && IsRelatedUnit(e.Flight, UnitNameOf(e))) || (e.KillerFlight != "" && !e.GroundKiller && IsRelatedUnit(e.KillerFlight, e.Killer)))) return 1;
            return 0;
        }

        private bool TrackStrong(Map_Track tr, string focusFlight)
        {
            if (focusFlight == "" && _selUnit == "") return true;
            if (IsRelatedUnit(tr.Flight, tr.Unit)) return true;
            return (focusFlight == "" || string.Equals(tr.Flight, focusFlight, StringComparison.OrdinalIgnoreCase) || _extraFlights.Contains(tr.Flight)) && (_selUnit == "" || tr.Unit == _selUnit);
        }

        // Victoires du vol / avion mis en avant : une étoile sur sa trace à l'heure de la victoire
        private void DrawVictories(Graphics g)
        {
            if (_focusNow == "" || _chkKills == null || !_chkKills.Checked || _tracks.Count == 0) return;

            foreach (Map_Event e in _all)
            {
                if (e.Killer == "" || e.Group == "bomb" || !e.HasPos || !TimeOk(e)) continue;
                if (!InFocus(e.KillerFlight)) continue;
                if (_selFlight != "" && _selUnit != "" && e.Killer != _selUnit) continue;

                Map_Track tr = _tracks.FirstOrDefault(t => t.Unit == e.Killer && t.Pts.Count > 0);
                if (tr == null) continue;                       // pas de trace pour le tueur : pas de marque (la fiche du vol donne la victoire)

                double[] best = tr.Pts[0];
                foreach (double[] pt in tr.Pts)
                    if (Math.Abs(pt[0] - e.T) < Math.Abs(best[0] - e.T)) best = pt;

                PointF k = _view.ToScreen(best[1], best[2]);
                if (float.IsNaN(k.X) || float.IsNaN(k.Y) || Math.Abs(k.X) > 1e5f || Math.Abs(k.Y) > 1e5f) continue;

                var star = new PointF[10];
                for (int i = 0; i < 10; i++)
                {
                    double a = -Math.PI / 2 + i * Math.PI / 5;
                    float r = i % 2 == 0 ? 10f : 4.5f;
                    star[i] = new PointF(k.X + (float)Math.Cos(a) * r, k.Y + (float)Math.Sin(a) * r);
                }
                using (var fill = new SolidBrush(Color.Gold)) g.FillPolygon(fill, star);
                g.DrawPolygon(Pens.Black, star);

                string label = "Victory " + FmtT(e.T) + ": " + (TypeName(e.Type) != "" ? TypeName(e.Type) : e.Name);
                if (_view.ShowLabels)
                {
                    g.DrawString(label, _fontBold, Brushes.Black, k.X + 13, k.Y - 7);
                    g.DrawString(label, _fontBold, Brushes.Gold, k.X + 12, k.Y - 8);
                }
            }
        }

        // Un élément de la target list qui figure aussi parmi les détruits (même nom, ou même place à 3 m près) n'est PAS intact : on ne garde que l'icône "détruit"
        // (le fichier de debriefing peut le classer "encore debout" alors que le journal de mission l'a bien vu mourir)
        private void RemoveDestroyedTargetObjects()
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var dead = new List<Map_Event>();
            foreach (Map_Event e in _all)
            {
                if (e.Kind != "static_kill" && e.Kind != "ground_kill" && e.Kind != "scenery_kill") continue;
                if (e.Name != "") names.Add(e.Name);
                if (e.Element != "") names.Add(e.Element);
                if (e.HasPos) dead.Add(e);
            }
            _targetObjs.RemoveAll(te =>
            {
                if (te.Name != "" && names.Contains(te.Name)) return true;
                foreach (Map_Event d in dead)
                {
                    double dx = d.X - te.X, dy = d.Y - te.Y;
                    if (dx * dx + dy * dy <= 9.0) return true;
                }
                return false;
            });
        }

        // Un élément intact n'apparaît qu'à l'heure de la première bombe tombée près de lui (distance : celle de l'impact le plus proche, au moins 500 m)
        private void SetTargetObjectTimes()
        {
            var bombs = _all.Where(b => b.Group == "bomb" && b.HasPos).ToList();
            foreach (Map_Event te in _targetObjs)
            {
                double d;
                if (!double.TryParse(te.Info, NumberStyles.Float, CultureInfo.InvariantCulture, out d)) d = 0;
                double r = Math.Max(500.0, d), r2 = r * r, first = double.MaxValue;
                foreach (Map_Event b in bombs)
                {
                    double dx = b.X - te.X, dy = b.Y - te.Y;
                    if (dx * dx + dy * dy <= r2 && b.T < first) first = b.T;
                }
                te.T = first == double.MaxValue ? 0 : first;
            }
        }

        // Curseur de temps : de la première à la dernière trace
        private void SetupTimeBar()
        {
            double t0 = double.MaxValue, t1 = double.MinValue;
            foreach (Map_Track tr in _tracks)
                if (tr.Pts.Count > 1) { t0 = Math.Min(t0, tr.Pts[0][0]); t1 = Math.Max(t1, tr.Pts[tr.Pts.Count - 1][0]); }
            if (t1 <= t0) return;           // pas de traces : le curseur reste désactivé
            _syncBar = true;            // la barre change de bornes : pas de réaction pendant ce temps
            try
            {
                _barTime.Maximum = (int)Math.Ceiling(t1);
                _barTime.Minimum = (int)Math.Floor(t0);
            }
            finally { _syncBar = false; }
            SetTime(StartTime());
            _chkTime.Enabled = true;
            _btnPlay.Enabled = true;
        }

        // Change l'heure du curseur (barre, étiquette, carte)
        private void SetTime(double t)
        {
            t = Math.Max(_barTime.Minimum, Math.Min(_barTime.Maximum, t));
            _tNow = t;
            _syncBar = true;
            try { _barTime.Value = (int)Math.Round(t); }
            finally { _syncBar = false; }
            _lblTime.Text = FmtT(t);
            if (_chkTime.Checked)
            {
                FollowSelected();
                _view.Invalidate();
            }
        }

        // Fond mémorisé seulement pendant la lecture, et pas quand la carte suit l'avion (elle bouge à chaque image)
        private void UpdatePlayCache()
        {
            if (_view != null) _view.PlayCache = _playTimer != null && _playTimer.Enabled && !(_chkFollow != null && _chkFollow.Checked);
        }

        // Heure de départ : 20 % de la durée des traces
        private double StartTime()
        {
            return _barTime.Minimum + 0.2 * (_barTime.Maximum - _barTime.Minimum);
        }

        // La carte se centre sur l'avion choisi (position lissée de sa tête) : l'avion reste fixe, c'est la carte qui bouge
        private void FollowSelected()
        {
            if (_chkFollow == null || !_chkFollow.Checked || !TimeOn()) return;
            if (FocusFlight() == "" && _selUnit == "") return;          // aucun avion choisi : rien à suivre

            string focus = TrackFocusFlight();
            Map_Track best = null;
            foreach (Map_Track tr in _tracks)
            {
                if (tr.Pts.Count < 2 || !TrackStrong(tr, focus)) continue;
                if (tr.Pts[0][0] > _tNow || tr.Pts[tr.Pts.Count - 1][0] < _tNow) continue;       // pas de trace à cette heure
                if (best == null || (_selUnit != "" && tr.Unit == _selUnit)) best = tr;
            }
            if (best == null) return;

            double vx, vy;
            List<double[]> w = TrackWindow(best, out vx, out vy);
            if (w.Count == 0) return;
            double[] head = w[w.Count - 1];
            _view.CenterOn(head[1], head[2]);
        }

        private void StartPlay()
        {
            if (!_chkTime.Enabled) return;
            if (!_chkTime.Checked) _chkTime.Checked = true;
            if (_tNow >= _barTime.Maximum - 0.5) SetTime(StartTime());
            _lastTick = Environment.TickCount;
            _playTimer.Start();
            UpdatePlayCache();
            _btnPlay.Text = "\u275A\u275A";
        }

        private void StopPlay()
        {
            if (_playTimer != null) _playTimer.Stop();
            UpdatePlayCache();
            if (_btnPlay != null) _btnPlay.Text = "\u25B6";
        }

        private void PlayTick()
        {
            int now = Environment.TickCount;
            double dt = Math.Min(0.25, Math.Max(0, (now - _lastTick) / 1000.0));
            _lastTick = now;
            SetTime(_tNow + dt * PlaySpeeds[_speedIdx]);
            if (_tNow >= _barTime.Maximum) StopPlay();
        }

        // Curseur de temps actif ? Alors une icône n'existe qu'à partir de l'heure de son événement.
        private bool TimeOn() { return _chkTime != null && _chkTime.Checked; }
        private bool TimeOk(Map_Event e) { return !TimeOn() || e.T <= _tNow; }

        // Longueur de la traîne en secondes (0 = toute la trace avant le curseur) : 30 s par cran, le dernier cran = All
        private double TrailSeconds()
        {
            return _barTrail.Value >= _barTrail.Maximum ? 0 : _barTrail.Value * 30.0;
        }

        // Les points de la trace entre (curseur - traîne) et le curseur.
        // Le dernier point (la tête) est calculé entre deux points enregistrés par une courbe lisse (Hermite : la vitesse est continue d'un segment à l'autre),
        // et sa vitesse (m/s, nord et est) sert à tourner la silhouette. Peu de calcul : un seul segment par trace et par image.
        private List<double[]> TrackWindow(Map_Track tr, out double vx, out double vy)
        {
            vx = 0; vy = 0;
            double t1 = _tNow, t0 = TrailSeconds() > 0 ? t1 - TrailSeconds() : double.MinValue;
            var res = new List<double[]>();
            int iLast = -1, iNext = -1;
            for (int i = 0; i < tr.Pts.Count; i++)
            {
                double[] pt = tr.Pts[i];
                if (pt[0] > t1) { iNext = i; break; }
                iLast = i;
                if (pt[0] >= t0) res.Add(pt);
            }

            if (iLast >= 0 && iNext > iLast && res.Count > 0 && res[res.Count - 1] == tr.Pts[iLast] && tr.Pts[iNext][0] - tr.Pts[iLast][0] < 120)
            {
                double[] p0 = tr.Pts[iLast], p1 = tr.Pts[iNext];
                double[] pm = iLast > 0 ? tr.Pts[iLast - 1] : p0;                           // point avant
                double[] pp = iNext + 1 < tr.Pts.Count ? tr.Pts[iNext + 1] : p1;            // point après
                double h = p1[0] - p0[0];
                double dm = p1[0] - pm[0], dp = pp[0] - p0[0];
                double[] m0 = new double[3], m1 = new double[3];                          // vitesses aux deux bouts (x, y, z)
                for (int c = 0; c < 3; c++)
                {
                    m0[c] = dm > 0 ? (p1[c + 1] - pm[c + 1]) / dm : (p1[c + 1] - p0[c + 1]) / h;
                    m1[c] = dp > 0 ? (pp[c + 1] - p0[c + 1]) / dp : (p1[c + 1] - p0[c + 1]) / h;
                }
                double uEnd = (t1 - p0[0]) / h;
                // quelques points intermédiaires pour que la traîne suive la courbe, puis la tête
                for (int k = 1; k <= 6; k++)
                {
                    double u = uEnd * k / 6.0, u2 = u * u, u3 = u2 * u;
                    double h00 = 2 * u3 - 3 * u2 + 1, h10 = u3 - 2 * u2 + u, h01 = -2 * u3 + 3 * u2, h11 = u3 - u2;
                    var q = new double[4];
                    q[0] = p0[0] + u * h;
                    for (int c = 0; c < 3; c++) q[c + 1] = h00 * p0[c + 1] + h10 * h * m0[c] + h01 * p1[c + 1] + h11 * h * m1[c];
                    res.Add(q);
                }
                double e2 = uEnd * uEnd;
                double d00 = 6 * e2 - 6 * uEnd, d10 = 3 * e2 - 4 * uEnd + 1, d01 = -6 * e2 + 6 * uEnd, d11 = 3 * e2 - 2 * uEnd;
                vx = (d00 * p0[1] + d10 * h * m0[0] + d01 * p1[1] + d11 * h * m1[0]) / h;
                vy = (d00 * p0[2] + d10 * h * m0[1] + d01 * p1[2] + d11 * h * m1[1]) / h;
            }
            else if (res.Count >= 2)
            {
                double[] a = res[res.Count - 2], b = res[res.Count - 1];
                double dt = b[0] - a[0];
                if (dt > 0) { vx = (b[1] - a[1]) / dt; vy = (b[2] - a[2]) / dt; }
            }
            return res;
        }

        // Têtes de trace (silhouettes) : calculées pendant le dessin des traces, dessinées en dernier, au-dessus de toutes les icônes
        private class TrackHead
        {
            public PointF P;
            public float Ang;
            public Color Col;
            public bool Heli;
            public string Label = "";
        }
        private readonly List<TrackHead> _heads = new List<TrackHead>();

        private void DrawTrackHeads(Graphics g)
        {
            if (_heads.Count == 0) return;
            SmoothingMode old = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            foreach (TrackHead hd in _heads)
            {
                GraphicsState hs = g.Save();
                g.TranslateTransform(hd.P.X, hd.P.Y);
                g.RotateTransform(hd.Ang);
                g.ScaleTransform(0.95f, 0.95f);
                using (var hf = new SolidBrush(hd.Col))
                using (var he = new Pen(Color.Black, 1.1f) { LineJoin = LineJoin.Round })
                    DrawFamily(g, hd.Heli ? "heli" : "plane", hf, he, hd.Col);
                g.Restore(hs);
                if (_view.ShowLabels && hd.Label != "")
                {
                    g.DrawString(hd.Label, _fontBold, Brushes.Black, hd.P.X + 11f, hd.P.Y - 7f);
                    using (var tb = new SolidBrush(hd.Col)) g.DrawString(hd.Label, _fontBold, tb, hd.P.X + 10f, hd.P.Y - 8f);
                }
            }
            g.SmoothingMode = old;
        }

        private void DrawTracks(Graphics g)
        {
            _heads.Clear();
            if (_chkTracks == null || !_chkTracks.Checked || _tracks.Count == 0) return;

            string focusFlight = TrackFocusFlight();
            bool focus = focusFlight != "" || _selUnit != "";
            bool moving = _view.IsMoving;       // pendant un zoom / déplacement : pas de lissage, pas de traces pâles (bien plus fluide)
            SmoothingMode oldMode = g.SmoothingMode;
            g.SmoothingMode = moving ? SmoothingMode.HighSpeed : SmoothingMode.AntiAlias;

            foreach (Map_Track tr in _tracks)
            {
                if (tr.Pts.Count < 2 || (_selSide != "" && tr.Side != _selSide)) continue;
                bool strong = TrackStrong(tr, focusFlight);
                if (moving && !strong) continue;
                Color col = TrackColor(tr.Side);
                double hvx = 0, hvy = 0;
                List<double[]> pts = (_chkTime != null && _chkTime.Checked) ? TrackWindow(tr, out hvx, out hvy) : tr.Pts;
                if (pts.Count < 2) continue;

                // La trace en points d'écran (on saute ceux à moins de 2 pixels du précédent) ; la découpe aux bords de l'écran est faite au dessin
                var run = new List<PointF>();
                var pix = new PointF[pts.Count];
                for (int i = 0; i < pts.Count; i++)
                {
                    PointF p = _view.ToScreen(pts[i][1], pts[i][2]);
                    pix[i] = p;
                    if (float.IsNaN(p.X) || float.IsInfinity(p.X) || float.IsNaN(p.Y) || float.IsInfinity(p.Y)) continue;
                    if (run.Count > 0 && i < pts.Count - 1)
                    {
                        PointF q = run[run.Count - 1];
                        if (Math.Abs(p.X - q.X) < 2f && Math.Abs(p.Y - q.Y) < 2f) continue;
                    }
                    run.Add(p);
                }
                if (run.Count < 2) continue;
                PointF[] line = run.ToArray();

                bool timeOn = TimeOn();
                if (strong && timeOn)
                {
                    // Curseur de temps : traîne qui s'efface vers l'arrière (transparente derrière, pleine devant)
                    const int Steps = 12;
                    for (int k = 0; k < Steps; k++)
                    {
                        int i0 = (int)((long)(line.Length - 1) * k / Steps), i1 = (int)((long)(line.Length - 1) * (k + 1) / Steps);
                        if (i1 <= i0) continue;
                        var seg = new PointF[i1 - i0 + 1];
                        Array.Copy(line, i0, seg, 0, seg.Length);
                        using (var pen = new Pen(Color.FromArgb(25 + 230 * (k + 1) / Steps, col), 3f) { LineJoin = LineJoin.Round })
                            _view.DrawClippedLines(g, pen, seg, false);
                    }
                }
                else if (strong)
                {
                    if (!moving)
                        using (var halo = new Pen(Color.FromArgb(140, 0, 0, 0), 5f) { LineJoin = LineJoin.Round })
                            _view.DrawClippedLines(g, halo, line, false);
                    using (var pen = new Pen(col, 2.6f) { LineJoin = LineJoin.Round })
                        _view.DrawClippedLines(g, pen, line, false);
                }
                else
                {
                    using (var pen = new Pen(Color.FromArgb(70, col), 1.2f))
                        _view.DrawClippedLines(g, pen, line, false);
                    continue;
                }

                // Départ : rond blanc ; arrivée : carré ; nom du joueur à côté
                PointF a0 = pix[0], a1 = pix[pix.Length - 1];
                if (!timeOn && Math.Abs(a0.X) < 30000f && Math.Abs(a0.Y) < 30000f)
                {
                    g.FillEllipse(Brushes.White, a0.X - 4.5f, a0.Y - 4.5f, 9f, 9f);
                    using (var edge = new Pen(col, 2f)) g.DrawEllipse(edge, a0.X - 4.5f, a0.Y - 4.5f, 9f, 9f);
                }
                if (Math.Abs(a1.X) < 30000f && Math.Abs(a1.Y) < 30000f)
                {
                    // Avant de la trace : une silhouette (avion ou hélico) tournée dans le sens de la marche ; elle est dessinée plus tard, au-dessus des icônes
                    float ang = 0f;
                    if (TimeOn() && (hvx != 0 || hvy != 0))
                    {
                        // sens de la vitesse lissée de la tête (sur l'écran : le nord n'est pas forcément en haut)
                        PointF ahead = _view.ToScreen(pts[pts.Count - 1][1] + hvx, pts[pts.Count - 1][2] + hvy);
                        ang = (float)(Math.Atan2(ahead.X - a1.X, -(ahead.Y - a1.Y)) * 180.0 / Math.PI);
                    }
                    else
                        for (int j = run.Count - 2; j >= 0; j--)
                        {
                            float ddx = run[run.Count - 1].X - run[j].X, ddy = run[run.Count - 1].Y - run[j].Y;
                            if (ddx * ddx + ddy * ddy >= 64f || j == 0) { ang = (float)(Math.Atan2(ddx, -ddy) * 180.0 / Math.PI); break; }
                        }
                    _heads.Add(new TrackHead { P = a1, Ang = ang, Col = col, Heli = RxStaticHeli.IsMatch(tr.Type ?? ""), Label = tr.Player != "" ? tr.Player : tr.Unit });
                }

                // Une marque toutes les 5 minutes (seulement quand un vol ou un avion est choisi)
                if (focus)
                {
                    for (int i = 1; i < pts.Count; i++)
                    {
                        long k = (long)Math.Floor(pts[i][0] / 300.0);
                        if (k == (long)Math.Floor(pts[i - 1][0] / 300.0)) continue;
                        PointF m = pix[i];
                        if (Math.Abs(m.X) > 30000f || Math.Abs(m.Y) > 30000f) continue;
                        g.FillEllipse(Brushes.White, m.X - 3f, m.Y - 3f, 6f, 6f);
                        g.DrawEllipse(Pens.Black, m.X - 3f, m.Y - 3f, 6f, 6f);
                        string tt = FmtT(k * 300);
                        if (_view.ShowLabels)
                        {
                            g.DrawString(tt, Font, Brushes.Black, m.X + 6f, m.Y - 5f);
                            g.DrawString(tt, Font, Brushes.White, m.X + 5f, m.Y - 6f);
                        }
                    }
                }
            }
            g.SmoothingMode = oldMode;
        }

        // Point de trace le plus proche de la souris (à 7 pixels maximum), parmi les traces affichées
        private bool TrackHitAt(Point p, out Map_Track found, out int index)
        {
            found = null;
            index = -1;
            if (_chkTracks == null || !_chkTracks.Checked) return false;

            // Le tracé est une suite de segments : on mesure la distance au SEGMENT (et pas seulement aux points échantillonnés),
            // puis on garde le point le plus proche du clic sur ce segment pour l'info-bulle
            float best = 7f;
            foreach (Map_Track tr in _tracks)
            {
                if (_selSide != "" && tr.Side != _selSide) continue;
                PointF prev = new PointF(float.NaN, float.NaN);
                for (int i = 0; i < tr.Pts.Count; i++)
                {
                    PointF q = _view.ToScreen(tr.Pts[i][1], tr.Pts[i][2]);
                    bool ok = !float.IsNaN(q.X) && Math.Abs(q.X) < 1e5f && Math.Abs(q.Y) < 1e5f;
                    if (ok && !float.IsNaN(prev.X))
                    {
                        float d = SegDist(p, prev, q);
                        if (d < best)
                        {
                            best = d; found = tr;
                            float dp = (prev.X - p.X) * (prev.X - p.X) + (prev.Y - p.Y) * (prev.Y - p.Y);
                            float dq = (q.X - p.X) * (q.X - p.X) + (q.Y - p.Y) * (q.Y - p.Y);
                            index = dp <= dq ? i - 1 : i;
                        }
                    }
                    else if (ok && tr.Pts.Count == 1)
                    {
                        float d = (float)Math.Sqrt((q.X - p.X) * (q.X - p.X) + (q.Y - p.Y) * (q.Y - p.Y));
                        if (d < best) { best = d; found = tr; index = i; }
                    }
                    prev = ok ? q : new PointF(float.NaN, float.NaN);
                }
            }
            return found != null;
        }

        private static string TrackText(Map_Track tr, int i)
        {
            double[] pt = tr.Pts[i];
            var sb = new StringBuilder();
            sb.AppendLine((tr.Player != "" ? tr.Player : "Player") + (tr.Type != "" ? "   [" + tr.Type + "]" : ""));
            sb.AppendLine(tr.Unit);
            string line = FmtT(pt[0]) + "    alt " + Math.Round(pt[3]).ToString("0", CultureInfo.InvariantCulture) + " m (" + Math.Round(pt[3] * 3.28084).ToString("0", CultureInfo.InvariantCulture) + " ft)";

            // Vitesse sol entre ce point et le précédent
            if (i > 0)
            {
                double[] pr = tr.Pts[i - 1];
                double dt = pt[0] - pr[0];
                if (dt > 0.5)
                {
                    double dx = pt[1] - pr[1], dy = pt[2] - pr[2];
                    double ms = Math.Sqrt(dx * dx + dy * dy) / dt;
                    line += "    speed " + Math.Round(ms * 1.94384).ToString("0", CultureInfo.InvariantCulture) + " kt (" + Math.Round(ms * 3.6).ToString("0", CultureInfo.InvariantCulture) + " km/h)";
                }
            }
            sb.Append(line);
            return sb.ToString();
        }

        // ------------------------------------------------------------------
        //  Zones AAA : les cercles qui se chevauchent (même camp) forment UNE zone à éviter, avec un contour commun
        // ------------------------------------------------------------------

        private List<Map_Drawing> BuildAaaAreas()
        {
            var result = new List<Map_Drawing>();
            _aaaHatch.Clear();
            foreach (string owner in new[] { "blue", "red" })
            {
                List<AaaZone> zs = _aaaZones.Where(z => z.Owner == owner).ToList();
                if (zs.Count == 0) continue;
                Color c = owner == "red" ? Color.FromArgb(255, 80, 70) : Color.FromArgb(60, 140, 255);

                // Groupes de cercles qui se touchent
                int[] grp = new int[zs.Count];
                for (int i = 0; i < grp.Length; i++) grp[i] = -1;
                int nGrp = 0;
                for (int i = 0; i < zs.Count; i++)
                {
                    if (grp[i] >= 0) continue;
                    var stack = new Stack<int>();
                    stack.Push(i);
                    grp[i] = nGrp;
                    while (stack.Count > 0)
                    {
                        int a = stack.Pop();
                        for (int b = 0; b < zs.Count; b++)
                        {
                            if (grp[b] >= 0) continue;
                            double dx = zs[a].X - zs[b].X, dy = zs[a].Y - zs[b].Y;
                            if (Math.Sqrt(dx * dx + dy * dy) < zs[a].R + zs[b].R) { grp[b] = nGrp; stack.Push(b); }
                        }
                    }
                    nGrp++;
                }

                for (int g = 0; g < nGrp; g++)
                {
                    List<AaaZone> members = new List<AaaZone>();
                    for (int i = 0; i < zs.Count; i++) if (grp[i] == g) members.Add(zs[i]);

                    if (members.Count == 1)
                    {
                        // Zone seule : un cercle, même style que les zones jointes
                        AaaZone z = members[0];
                        var circle = new Map_Drawing
                        {
                            Name = z.Name, Group = "aaa", Kind = "Circle", Radius = z.R, RangeM = z.R, Label = "AAA " + owner + " : " + z.Name,
                            Line = Color.FromArgb(235, c), Fill = Color.FromArgb(22, c), Thickness = 2.5f
                        };
                        circle.Points.Add(new double[] { z.X, z.Y });
                        result.Add(circle);
                        _aaaHatch.Add(new AaaHatch { Color = c, Pts = CirclePoints(z.X, z.Y, z.R, 72) });
                        continue;
                    }

                    string names = string.Join(", ", members.Select(m => m.Name));
                    List<List<double[]>> loops;
                    if (AaaOval)
                    {
                        // Un ovale : enveloppe convexe de tous les cercles du groupe
                        var cloud = new List<double[]>();
                        foreach (AaaZone m in members) cloud.AddRange(CirclePoints(m.X, m.Y, m.R, 72));
                        loops = new List<List<double[]>> { ConvexHull(cloud) };
                    }
                    else loops = UnionOutline(members);

                    foreach (List<double[]> loop in loops)
                    {
                        if (loop.Count < 3) continue;
                        bool outer = SignedArea(loop) > 0;            // sens inverse = trou au milieu des cercles : contour seul
                        var poly = new Map_Drawing
                        {
                            Name = names, Group = "aaa", Kind = "Polygon", Closed = true,
                            Label = outer ? "AAA " + owner + " : " + members.Count + " zones" : "",
                            Line = Color.FromArgb(235, c), Fill = Color.FromArgb(outer ? 22 : 0, c), Thickness = 2.5f
                        };
                        poly.Points.AddRange(loop);
                        result.Add(poly);
                        if (outer) _aaaHatch.Add(new AaaHatch { Color = c, Pts = loop });
                    }
                }
            }
            return result;
        }

        private static List<double[]> CirclePoints(double x, double y, double r, int n)
        {
            var l = new List<double[]>();
            for (int i = 0; i < n; i++)
            {
                double a = Math.PI * 2 * i / n;
                l.Add(new double[] { x + r * Math.Cos(a), y + r * Math.Sin(a) });
            }
            return l;
        }

        // Enveloppe convexe (Andrew), résultat dans le sens anti-horaire
        private static List<double[]> ConvexHull(List<double[]> pts)
        {
            List<double[]> p = pts.OrderBy(a => a[0]).ThenBy(a => a[1]).ToList();
            if (p.Count < 3) return p;
            var h = new List<double[]>();
            for (int pass = 0; pass < 2; pass++)
            {
                int start = h.Count;
                for (int i = 0; i < p.Count; i++)
                {
                    while (h.Count >= start + 2 && Cross2(h[h.Count - 2], h[h.Count - 1], p[i]) <= 0) h.RemoveAt(h.Count - 1);
                    h.Add(p[i]);
                }
                h.RemoveAt(h.Count - 1);
                p.Reverse();
            }
            return h;
        }

        private static double Cross2(double[] o, double[] a, double[] b)
        {
            return (a[0] - o[0]) * (b[1] - o[1]) - (a[1] - o[1]) * (b[0] - o[0]);
        }

        private static double SignedArea(List<double[]> p)
        {
            double sum = 0;
            for (int i = 0; i < p.Count; i++)
            {
                double[] a = p[i], b = p[(i + 1) % p.Count];
                sum += a[0] * b[1] - b[0] * a[1];
            }
            return sum / 2.0;
        }

        // Contour de la réunion de plusieurs cercles : pour chaque cercle, on garde les arcs qui ne sont dans aucun autre, puis on les relie bout à bout.
        // Résultat : une ou plusieurs boucles (sens anti-horaire = bord extérieur, sens horaire = trou).
        private static List<List<double[]>> UnionOutline(List<AaaZone> zs)
        {
            const double TwoPi = Math.PI * 2;
            var arcs = new List<List<double[]>>();

            for (int i = 0; i < zs.Count; i++)
            {
                AaaZone a = zs[i];
                var cov = new List<double[]>();         // angles couverts par les autres cercles
                bool inside = false;

                for (int j = 0; j < zs.Count && !inside; j++)
                {
                    if (j == i) continue;
                    AaaZone b = zs[j];
                    double dx = b.X - a.X, dy = b.Y - a.Y, d = Math.Sqrt(dx * dx + dy * dy);
                    if (d >= a.R + b.R) continue;
                    if (d <= Math.Abs(a.R - b.R))
                    {
                        if (b.R > a.R || (b.R == a.R && j < i)) inside = true;      // a est entièrement dans b
                        continue;
                    }
                    double ang = Math.Atan2(dy, dx);
                    double cosH = (a.R * a.R + d * d - b.R * b.R) / (2 * a.R * d);
                    double half = Math.Acos(Math.Max(-1.0, Math.Min(1.0, cosH)));
                    double lo = ((ang - half) % TwoPi + TwoPi) % TwoPi;
                    double hi = lo + 2 * half;
                    if (hi > TwoPi) { cov.Add(new[] { lo, TwoPi }); cov.Add(new[] { 0.0, hi - TwoPi }); }
                    else cov.Add(new[] { lo, hi });
                }
                if (inside) continue;

                // Arcs libres = ce qui reste du cercle une fois les angles couverts retirés
                cov.Sort((p, q) => p[0].CompareTo(q[0]));
                var free = new List<double[]>();
                double pos = 0;
                foreach (double[] iv in cov)
                {
                    if (iv[0] > pos + 1e-9) free.Add(new[] { pos, iv[0] });
                    pos = Math.Max(pos, iv[1]);
                }
                if (pos < TwoPi - 1e-9) free.Add(new[] { pos, TwoPi });
                if (free.Count == 0) continue;
                if (free.Count > 1 && free[0][0] <= 1e-9 && free[free.Count - 1][1] >= TwoPi - 1e-9)
                {
                    // l'arc libre passe par l'angle 0 : on recolle les deux bouts
                    double[] last = free[free.Count - 1], first = free[0];
                    free.RemoveAt(free.Count - 1);
                    free[0] = new[] { last[0], first[1] + TwoPi };
                }

                foreach (double[] f in free)
                {
                    int n = Math.Max(2, (int)Math.Ceiling((f[1] - f[0]) / (Math.PI / 60)));      // un point tous les 3 degrés
                    var pts = new List<double[]>();
                    for (int k = 0; k <= n; k++)
                    {
                        double t = f[0] + (f[1] - f[0]) * k / n;
                        pts.Add(new double[] { a.X + a.R * Math.Cos(t), a.Y + a.R * Math.Sin(t) });
                    }
                    arcs.Add(pts);
                }
            }

            // Relie les arcs : la fin de l'un est le début du suivant (ils se rejoignent aux points d'intersection des cercles)
            var loops = new List<List<double[]>>();
            var used = new bool[arcs.Count];
            for (int s0 = 0; s0 < arcs.Count; s0++)
            {
                if (used[s0]) continue;
                used[s0] = true;
                var loop = new List<double[]>(arcs[s0]);

                for (int guard = 0; guard <= arcs.Count; guard++)
                {
                    double[] end = loop[loop.Count - 1], start = loop[0];
                    if (loop.Count > 2 && PtDist(end, start) < 2.0) break;      // boucle fermée

                    int best = -1;
                    double bestD = 5.0;
                    for (int k = 0; k < arcs.Count; k++)
                    {
                        if (used[k]) continue;
                        double d = PtDist(end, arcs[k][0]);
                        if (d < bestD) { bestD = d; best = k; }
                    }
                    if (best < 0) break;
                    used[best] = true;
                    loop.AddRange(arcs[best].Skip(1));
                }
                loops.Add(loop);
            }
            return loops;
        }

        private static double PtDist(double[] a, double[] b)
        {
            double dx = a[0] - b[0], dy = a[1] - b[1];
            return Math.Sqrt(dx * dx + dy * dy);
        }

        // Frontières (fichier de base de la campagne), menaces SAM et zones AAA : envoyées à la carte
        private void ApplyOverlays()
        {
            try { _view.SetDrawings("mission", Map_Drawings.Load(_campaign)); }
            catch (Exception ex) { FormUtils.LogRegister("Map_Events_Form | frontières : " + ex.Message); }
            _view.SetDrawings("threats", _samDraw);
            _aaaDraw = BuildAaaAreas();
            _view.SetDrawings("aaa", _aaaDraw);
            _view.SetDrawingsVisible("mission", _chkBorders.Checked);
            _view.SetDrawingsVisible("threats", _chkSam.Checked);
            _view.SetDrawingsVisible("aaa", _chkAaa.Checked);
            _chkAaa.Enabled = _aaaDraw.Count > 0;
            _chkTObj.Enabled = _targetObjs.Count > 0;
            _chkSam.Enabled = _samDraw.Count > 0;
        }

        private void LoadPlans()
        {
            // Fichier récent : les vols de la mission jouée sont déjà dans le fichier debrief (le .miz actuel est celui d'une AUTRE mission)
            if (_flightsInFile)
            {
                // Fichier de la version précédente : plans dans le fichier, mais pas les SAM -> SAM du .miz actuel
                if (!_samInFile)
                {
                    try { _samDraw = Map_Drawings.LoadMission(_campaign, _justGenerated).Threats; }
                    catch (Exception ex) { FormUtils.LogRegister("Map_Events_Form | SAM : " + ex.Message); }
                }
                RebuildPlans(); UpdateStatus(); return;
            }

            Cursor = Cursors.WaitCursor;
            try
            {
                Map_MissionData data = Map_Drawings.LoadMission(_campaign, _justGenerated);
                if (data.Error != "") _planInfo = "Flight plans: " + data.Error;
                if (!_samInFile) _samDraw = data.Threats;       // ancien fichier : SAM du .miz actuel (peut être une autre mission)

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

        // Ligne de choix d'un vol : 2 = tireur au sol, 1 = hélicoptère, 0 = avion
        private int KindOf(string flight)
        {
            if (string.IsNullOrEmpty(flight)) return 0;
            if (_gndSide.ContainsKey(flight)) return 2;
            return _heliFlights.Contains(flight) ? 1 : 0;
        }

        // Vols d'hélicoptères : événement "air_lost" d'un hélico, ou type d'avion de la famille des hélicos
        private void ComputeKinds()
        {
            foreach (Map_Event e in _all)
                if (e.Kind == "air_lost" && e.Category == "helicopter" && e.Flight != "") _heliFlights.Add(e.Flight);
            foreach (KeyValuePair<string, string> kv in _unitType)
                if (RxStaticHeli.IsMatch(kv.Value ?? "")) _heliFlights.Add(FlightOfUnit(kv.Key));
        }

        private void FillFlights()
        {
            _syncFilter = true;
            ComputeKinds();
            for (int k = 0; k < 3; k++)
            {
                _fCombo[k].Items.Clear();
                _fCombo[k].Items.Add(new FlightItem("", RowAllFlights[k]));
                IEnumerable<KeyValuePair<string, string>> src = k == 2 ? _gndSide.AsEnumerable() : _flightSide.Where(kv => KindOf(kv.Key) == k);
                int kk = k;
                foreach (string f in src.Where(kv => _selSide == "" || kv.Value == _selSide).Select(kv => kv.Key).OrderBy(NatKey, StringComparer.Ordinal))
                {
                    List<string> players;
                    string types = TypesOfFlight(f);
                    string text = f + (types != "" ? "   (" + types + ")" : "") + (kk != 2 && _flightPlayers.TryGetValue(f, out players) ? "   [" + string.Join(", ", players) + "]" : "");
                    _fCombo[k].Items.Add(new FlightItem(f, text));
                }
                _fCombo[k].SelectedIndex = 0;
            }
            _selFlight = "";
            _extraFlights.Clear();
            FillUnits();
            _syncFilter = false;
        }

        // Une seule ligne active : les listes de vols des 2 autres lignes reviennent sur "All"
        private void ResetOtherRows(int keep)
        {
            for (int k = 0; k < 3; k++)
                if (k != keep && _fCombo[k] != null && _fCombo[k].Items.Count > 0) _fCombo[k].SelectedIndex = 0;
        }

        // Montre / cache les lignes Helicopters et Ground ; la ligne "Map style" remonte
        private void SetSelRows(bool open)
        {
            if (!open && KindOf(_selFlight) != 0)
            {
                _selFlight = ""; _selUnit = ""; _focus = "";
                ResetOtherRows(-1);
                FillUnits();
                ApplyFilters();
            }
            _selRowsOpen = open;
            LayoutSelRows();
        }

        private void LayoutSelRows()
        {
            int off = _selRowsOpen ? 82 : 0;
            foreach (Control c in _rowCtrls) c.Visible = _selRowsOpen;
            foreach (Control c in _mapRowCtrls) c.Top = (int)c.Tag + off;
            if (_topBarOuter != null) _topBarOuter.Height = 80 + off;
            if (_btnSelRows != null) _btnSelRows.Text = _selRowsOpen ? "Helicopters / Ground  \u25B4" : "Helicopters / Ground  \u25BE";
        }

        private static string UnitNameOf(Map_Event e)
        {
            return (e.Kind == "pilot_sar" || e.Kind == "rescue" || (e.Kind == "pilot_dead" && e.Unit != "")) ? e.Unit : e.Name;
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
                object ty;
                if (u.TryGetValue("type", out ty) && Convert.ToString(ty, CultureInfo.InvariantCulture) != "") _unitType[name] = Convert.ToString(ty, CultureInfo.InvariantCulture);
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
            string who, ty = TypeOfUnit(unit);
            string head = ty != "" ? unit + "   (" + ty + ")" : unit;
            if (_unitPlayer.TryGetValue(unit, out who)) return head + "   [" + who + "]";
            return _clientUnits.Contains(unit) ? head + "   [client]" : head;
        }

        // Fiche du vol choisi (affichée dans les détails quand aucun événement n'est sélectionné)
        private string FlightSummary()
        {
            string flight = FocusFlight();
            if (flight == "") return "";
            var sb = new StringBuilder();
            if (_extraFlights.Count > 0)
            {
                var all = new List<string> { flight };
                all.AddRange(_extraFlights.OrderBy(x => x, StringComparer.OrdinalIgnoreCase));
                sb.AppendLine(all.Count + " flights chosen (CTRL + click adds one, right-click takes one out):");
                foreach (string f in all)
                {
                    string ft2 = TypesOfFlight(f);
                    List<string> pl2 = PlayersOf(f);
                    sb.AppendLine("  " + f + (ft2 != "" ? "   (" + ft2 + ")" : "") + (pl2.Count > 0 ? "   [" + string.Join(", ", pl2.ToArray()) + "]" : ""));
                }
                return sb.ToString().TrimEnd();
            }
            sb.AppendLine(flight);
            string side;
            if (_flightSide.TryGetValue(flight, out side) && side != "") sb.AppendLine("Side:     " + side);
            List<string> pl = PlayersOf(flight);
            if (pl.Count > 0) sb.AppendLine("Players:  " + string.Join(", ", pl));
            string ft = TypesOfFlight(flight);
            if (ft != "") sb.AppendLine("Type:     " + ft);
            sb.AppendLine(KindOf(flight) == 2 ? "Units that destroyed something:" : "Aircraft:");
            foreach (string n in UnitsOfFlight(flight)) sb.AppendLine("  " + UnitLabel(n));

            // Victoires : ce que ce vol (ou l'avion choisi) a abattu / détruit
            var wins = new List<string>();
            foreach (Map_Event e in _all)
            {
                if (e.Killer == "" || e.Group == "bomb" || !string.Equals(e.KillerFlight, flight, StringComparison.OrdinalIgnoreCase)) continue;
                if (_selFlight != "" && _selUnit != "" && e.Killer != _selUnit) continue;
                string what = TypeName(e.Type) != "" ? TypeName(e.Type) : e.Name;
                wins.Add("  " + FmtT(e.T) + "  " + what + (e.Name != "" && e.Name != what ? " (" + e.Name + ")" : "") + "   by " + e.Killer);
            }
            if (wins.Count > 0)
            {
                sb.AppendLine("Victories (" + wins.Count + "):");
                foreach (string w in wins) sb.AppendLine(w);
            }
            return sb.ToString().TrimEnd();
        }

        private void FillUnits()
        {
            _selUnit = "";
            int row = KindOf(_selFlight);
            for (int k = 0; k < 3; k++)
            {
                _uCombo[k].Items.Clear();
                _uCombo[k].Items.Add(new FlightItem("", RowAllUnits[k]));
                if (_selFlight != "" && k == row)
                    foreach (string n in UnitsOfFlight(_selFlight)) _uCombo[k].Items.Add(new FlightItem(n, UnitLabel(n)));
                _uCombo[k].SelectedIndex = 0;
                _uCombo[k].Enabled = _selFlight != "" && k == row;
            }
        }

        private bool Match(Map_Event e)
        {
            if (e.Group != "" && !_groupOn[e.Group]) return false;

            bool hide = _chkHide != null && _chkHide.Checked;       // sinon le vol / avion choisi ne filtre pas : il met en avant (voir IsFocused)
            bool hasSide = _selSide != "", hasFlight = hide && _selFlight != "", hasUnit = hide && _selUnit != "";
            if (!hasSide && !hasFlight && !hasUnit) return true;

            // ce qui est arrivé au camp / vol / avion choisi
            if ((!hasSide || e.Side == _selSide) && (!hasFlight || InSelFlight(e.Flight)) && (!hasUnit || UnitNameOf(e) == _selUnit)) return true;

            // ce qu'il a détruit (quand un vol ou un avion est choisi, même si "Hide the rest" est décoché :
            // les victimes sont de l'autre camp, le filtre de camp les aurait sinon toutes masquées)
            bool flightSel = _selFlight != "", unitSel = _selFlight != "" && _selUnit != "";
            if (_chkKills.Checked && (flightSel || unitSel))
                return (!hasSide || e.KillerSide == _selSide) && (!flightSel || InSelFlight(e.KillerFlight)) && (!unitSel || e.Killer == _selUnit);

            return false;
        }

        private static int LayerOf(Map_Event e)
        {
            int i = Array.IndexOf(Layers, e.Group);
            return i < 0 ? 0 : i;
        }

        // ------------------------------------------------------------------
        //  Listes de droite : seulement ce qui touche l'avion / le vol / l'unité sol choisi ; rien de choisi = tout
        // ------------------------------------------------------------------

        private string _listKey = "";

        // Unité sol / navire / statique : l'événement choisi n'a aucun avion derrière lui
        private bool GroundSelected()
        {
            return !_selFromList && _selected != null && _selected.Flight == "" && (_selected.KillerFlight == "" || _selected.GroundKiller)
                && (_selected.Group == "ground" || _selected.Group == "ship" || _selected.Group == "static");
        }

        private bool InListFocus(Map_Event e)
        {
            if (ReferenceEquals(e, _selected)) return true;

            string fl = FocusFlight();
            if (fl != "")
                return IsFocused(e);            // seulement le vol / avion choisi : ce qu'il a subi et (si coché) ce qu'il a détruit

            if (GroundSelected())
            {
                Map_Event s = _selected;
                if (e.Id == s.Id) return true;
                if (s.Name != "" && e.Killer == s.Name) return true;                        // ce que cette unité a détruit
                if (s.Killer != "" && (e.Killer == s.Killer || e.Name == s.Killer)) return true;   // son tueur et ses autres victimes
            }
            return true;
        }

        private bool ListFiltered()
        {
            return FocusFlight() != "" || GroundSelected();
        }

        // Reconstruit les deux listes si la mise en avant a changé (garde le défilement sinon)
        private void RefreshList()
        {
            if (_syncFilter) return;
            _focusNow = FocusFlight();
            UpdateRelated();
            string key = _focusNow + "|" + _selUnit + "|" + (_chkKills != null && _chkKills.Checked) + "|" + (GroundSelected() ? _selected.Id.ToString() : "") + "|" + ExtraKey;
            if (key == _listKey) return;
            BuildLists(_all.Where(Match).ToList());
        }

        private void BuildLists(List<Map_Event> match)
        {
            _focusNow = FocusFlight();
            UpdateRelated();
            _listKey = _focusNow + "|" + _selUnit + "|" + (_chkKills != null && _chkKills.Checked) + "|" + (GroundSelected() ? _selected.Id.ToString() : "") + "|" + ExtraKey;

            if (ListFiltered())
                match = match.Where(e => InListFocus(e)).ToList();
            List<Map_Event> noPos = match.Where(e => !e.HasPos).ToList();

            // Liste des événements placés
            _list.BeginUpdate();
            _list.Items.Clear();
            foreach (Map_Event e in match.Where(x => x.HasPos))
            {
                var it = new ListViewItem(FmtT(e.T)) { Tag = e };
                it.SubItems.Add(KindLabel(e));
                it.SubItems.Add(DisplayName(e));
                it.SubItems.Add(e.Kind == "bomb" ? "" : TypeName(e.Type));
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
                it.SubItems.Add(e.Killer != "" ? "shot down by " + (e.KillerType != "" ? TypeName(e.KillerType) : e.Killer) : (e.Cause != "" ? e.Cause : "no killer in the log"));
                it.ForeColor = e.Side == "blue" ? Color.RoyalBlue : (e.Side == "red" ? Color.Firebrick : Color.Black);
                _listNoPos.Items.Add(it);
            }
            _listNoPos.EndUpdate();
            _lblNoPos.Text = "Position unknown (" + noPos.Count + ")";
            _lblList.Text = "Events on the map (" + _list.Items.Count + ")";
        }

        private void ApplyFilters()
        {
            if (_syncFilter) return;

            List<Map_Event> match = _all.Where(Match).ToList();                 // dans l'ordre du temps
            _shown = match.Where(e => e.HasPos).OrderBy(LayerOf).ToList();      // dans l'ordre de dessin
            List<Map_Event> noPos = match.Where(e => !e.HasPos).ToList();

            _listKey = "";
            BuildLists(match);

            if (_selected != null && _selected.Group != "tobj" && !match.Contains(_selected)) { _selected = null; _details.Text = ""; }

            RebuildPlans();
            UpdateStatus();
            UpdateStats();
            if (_iconBox != null) _iconBox.Invalidate();
            _view.Invalidate();
        }

        // ------------------------------------------------------------------
        //  Grosse icône de l'événement choisi
        // ------------------------------------------------------------------

        private void PaintSelectedIcon(object sender, PaintEventArgs pe)
        {
            Graphics g = pe.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            int w = _iconBox.Width;

            if (_selected == null)
            {
                g.DrawString("Click an event\nto see its icon\nhere", Font, Brushes.Gray, new RectangleF(4, 0, w - 8, _iconBox.Height), _sfCenter);
                return;
            }

            int alpha;
            Color c = EventColor(_selected, out alpha);
            GraphicsState st = g.Save();
            g.TranslateTransform(w / 2f, 62f);
            g.ScaleTransform(3.6f, 3.6f);
            try { DrawEventIcon(g, _selected, c, alpha); }
            finally { g.Restore(st); }

            var cap = new RectangleF(2, 112, w - 4, _iconBox.Height - 114);
            g.DrawString(KindLabel(_selected) + (_selected.Kind == "target_obj" ? "" : "\n" + FmtT(_selected.T)), _fontBold, Brushes.Black, cap, _sfCenter);
        }

        // ------------------------------------------------------------------
        //  Ligne de bilan : sommes pour le camp / vol / avion choisi
        // ------------------------------------------------------------------

        private static readonly Regex RxPilotUnit = new Regex(@"^Mis\d+_Pilot_(.*)_Nb\d+$", RegexOptions.CultureInvariant);

        private void UpdateStats()
        {
            if (_stats == null) return;
            bool hasSide = _selSide != "", hasFlight = _selFlight != "", hasUnit = _selUnit != "";
            int airK = 0, gndK = 0, stK = 0, shipK = 0, scenK = 0, bombs = 0, friendly = 0;
            int shot = 0, crashed = 0, damaged = 0, ejected = 0, dead = 0, mia = 0, rescued = 0, rescues = 0, landings = 0;

            foreach (Map_Event e in _all)
            {
                // Ce que le camp / vol / avion a détruit
                bool doer = e.Killer != "" && (!hasSide || e.KillerSide == _selSide) && (!hasFlight || InSelFlight(e.KillerFlight)) && (!hasUnit || e.Killer == _selUnit);
                if (doer)
                {
                    if (e.Friendly) { if (e.Kind != "bomb") friendly++; }
                    else
                    {
                        switch (e.Kind)
                        {
                            case "air_lost": airK++; break;
                            case "ground_kill": gndK++; break;
                            case "static_kill": stK++; break;
                            case "ship_kill": shipK++; break;
                            case "scenery_kill": scenK++; break;
                            case "bomb": bombs++; break;
                        }
                    }
                }

                // Ce qui lui est arrivé
                bool subj = (!hasSide || e.Side == _selSide) && (!hasFlight || InSelFlight(e.Flight)) && (!hasUnit || UnitNameOf(e) == _selUnit);
                if (subj)
                {
                    switch (e.Kind)
                    {
                        case "air_lost": if (e.Cause == "kill") shot++; else crashed++; break;
                        case "damage": damaged++; break;
                        case "eject": ejected++; break;
                        case "pilot_dead": dead++; break;
                        case "pilot_sar": if (e.Current && e.Status == "MIA") mia++; break;
                        case "rescue": rescues++; break;          // l'hélico sauveteur est le sujet
                        case "landing": landings++; break;
                    }
                }

                // Pilote sauvé : le sujet est l'avion du pilote (nom du pilote dans "info")
                if (e.Kind == "rescue" && e.Info != "")
                {
                    var m = RxPilotUnit.Match(e.Info);
                    string pu = m.Success ? m.Groups[1].Value : "";
                    if (pu != "" && (!hasSide || e.Side == _selSide) && (!hasFlight || InSelFlight(FlightOfUnit(pu))) && (!hasUnit || pu == _selUnit))
                        rescued++;
                }
            }

            // Titre : qui est résumé
            string title;
            string sideTxt = hasSide ? (_selSide == "blue" ? "Blue" : "Red") : "";
            if (hasUnit) title = "Aircraft: " + UnitLabel(_selUnit) + "     (this aircraft only)";
            else if (hasFlight && _extraFlights.Count > 0)
            {
                var names = new List<string> { _selFlight };
                names.AddRange(_extraFlights.OrderBy(x => x, StringComparer.OrdinalIgnoreCase));
                title = "Flights: " + names.Count + " flights chosen     (sum of all their aircraft)     " + string.Join(", ", names.Take(3).ToArray()) + (names.Count > 3 ? ", ..." : "");
            }
            else if (hasFlight)
            {
                int n = UnitsOfFlight(_selFlight).Count;
                List<string> pl = PlayersOf(_selFlight);
                string ftypes = TypesOfFlight(_selFlight);
                bool gnd = KindOf(_selFlight) == 2;
                title = (gnd ? "Ground shooter: " : "Flight: ") + _selFlight + (ftypes != "" ? "  (" + ftypes + ")" : "") + "     (sum of " + (n > 0 ? n + (gnd ? " units" : " aircraft") : (gnd ? "the whole group" : "the whole flight")) + ")" + (pl.Count > 0 ? "     Players: " + string.Join(", ", pl) : "");
            }
            else title = hasSide ? "Side: " + sideTxt + "     (whole side)" : "Whole mission     (all sides)";
            _statsTitle = title;

            Color green = Color.FromArgb(40, 150, 70), red = Color.FromArgb(200, 55, 45), orange = Color.FromArgb(230, 130, 20),
                  blue = Color.FromArgb(50, 110, 200), purple = Color.FromArgb(130, 70, 170), brown = Color.FromArgb(150, 110, 50);

            _statGroups.Clear();
                        var gd = new StatGroup { Title = "DESTROYED" };
            gd.Chips.Add(MakeChip("✈", "Aircraft", airK, green));
            gd.Chips.Add(MakeChip("■", "Vehicles", gndK, green));
            gd.Chips.Add(MakeChip("◆", "Statics", stK, green));
            gd.Chips.Add(MakeChip("⚓", "Ships", shipK, green));
            gd.Chips.Add(MakeChip("⌂", "Structures", scenK, green));
            gd.Chips.Add(MakeChip("●", "Bomb impacts", bombs, brown));
            gd.Chips.Add(MakeChip("✖", "Friendly fire", friendly, red));
            _statGroups.Add(gd);

            var gl = new StatGroup { Title = "LOSSES" };
            gl.Chips.Add(MakeChip("✈", "Shot down", shot, red));
            gl.Chips.Add(MakeChip("✈", "Crashed", crashed, red));
            gl.Chips.Add(MakeChip("⚠", "Damaged", damaged, orange));
            _statGroups.Add(gl);

            var gc = new StatGroup { Title = "CREW" };
            gc.Chips.Add(MakeChip("☂", "Ejected", ejected, blue));
            gc.Chips.Add(MakeChip("✝", "Dead", dead, red));
            gc.Chips.Add(MakeChip("?", "MIA", mia, purple));
            gc.Chips.Add(MakeChip("✚", "Rescued", rescued, green));
            _statGroups.Add(gc);

            var gm = new StatGroup { Title = "HELICOPTERS" };
            gm.Chips.Add(MakeChip("✚", "Rescues made", rescues, green));
            gm.Chips.Add(MakeChip("H", "Landings", landings, blue));
            _statGroups.Add(gm);

            _statsLayoutW = -1;
            LayoutStats();
        }

        private static StatChip MakeChip(string glyph, string label, int value, Color color)
        {
            return new StatChip { Glyph = glyph, Label = label, Value = value, Color = color };
        }

        // Place les pastilles (retour à la ligne si la fenêtre est étroite) et règle la hauteur de la ligne
        private void LayoutStats()
        {
            int w = _stats.ClientSize.Width;
            if (w < 60) return;
            _statsLayoutW = w;

            const TextFormatFlags fl = TextFormatFlags.NoPadding | TextFormatFlags.SingleLine;
            int x = 10, y = 26, rowH = 26, gap = 6;

            foreach (StatGroup grp in _statGroups)
            {
                grp.TitleW = TextRenderer.MeasureText(grp.Title, _statCap, Size.Empty, fl).Width;
                if (x > 10 && x + grp.TitleW + 100 > w - 10) { x = 10; y += rowH; }
                grp.TitlePos = new Point(x, y + 6);
                x += grp.TitleW + 6;

                foreach (StatChip c in grp.Chips)
                {
                    c.GlyphW = TextRenderer.MeasureText(c.Glyph, _statGlyph, Size.Empty, fl).Width;
                    c.LabelW = TextRenderer.MeasureText(c.Label, _statFont, Size.Empty, fl).Width;
                    c.ValueW = TextRenderer.MeasureText(c.Value.ToString(CultureInfo.InvariantCulture), _statBold, Size.Empty, fl).Width;
                    int cw = 8 + c.GlyphW + 4 + c.LabelW + 6 + c.ValueW + 8;
                    if (x + cw > w - 6 && x > 10) { x = 10; y += rowH; }
                    c.Box = new Rectangle(x, y, cw, rowH - 4);
                    x += cw + gap;
                }
                x += 10;
            }

            int wanted = y + rowH + 4;
            if (_stats.Height != wanted) _stats.Height = wanted;
            _stats.Invalidate();
        }

        private void PaintStats(object sender, PaintEventArgs pe)
        {
            Graphics g = pe.Graphics;
            const TextFormatFlags fl = TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter;

            TextRenderer.DrawText(g, _statsTitle, _statBold, new Rectangle(10, 3, _stats.Width - 20, 20), Color.FromArgb(40, 40, 40),
                TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.VerticalCenter);
            using (var line = new Pen(Color.FromArgb(225, 225, 230)))
                g.DrawLine(line, 0, _stats.Height - 1, _stats.Width, _stats.Height - 1);

            foreach (StatGroup grp in _statGroups)
            {
                TextRenderer.DrawText(g, grp.Title, _statCap, new Rectangle(grp.TitlePos.X, grp.TitlePos.Y - 6, grp.TitleW, 22), Color.FromArgb(130, 130, 140), fl);
                foreach (StatChip c in grp.Chips)
                {
                    bool zero = c.Value == 0;
                    Color col = zero ? Color.FromArgb(175, 175, 180) : c.Color;
                    using (var back = new SolidBrush(zero ? Color.FromArgb(247, 247, 249) : Color.FromArgb(Math.Min(255, 238 + c.Color.R / 20), Math.Min(255, 238 + c.Color.G / 20), Math.Min(255, 238 + c.Color.B / 20))))
                        g.FillRectangle(back, c.Box);
                    using (var edge = new Pen(zero ? Color.FromArgb(225, 225, 230) : Color.FromArgb(120, c.Color)))
                        g.DrawRectangle(edge, c.Box.X, c.Box.Y, c.Box.Width - 1, c.Box.Height - 1);

                    int x = c.Box.X + 8;
                    TextRenderer.DrawText(g, c.Glyph, _statGlyph, new Rectangle(x, c.Box.Y - 1, c.GlyphW + 2, c.Box.Height), col, fl);
                    x += c.GlyphW + 4;
                    TextRenderer.DrawText(g, c.Label, _statFont, new Rectangle(x, c.Box.Y, c.LabelW + 2, c.Box.Height), zero ? Color.FromArgb(150, 150, 155) : Color.FromArgb(50, 50, 55), fl);
                    x += c.LabelW + 6;
                    TextRenderer.DrawText(g, c.Value.ToString(CultureInfo.InvariantCulture), _statBold, new Rectangle(x, c.Box.Y, c.ValueW + 2, c.Box.Height), col, fl);
                }
            }
        }

        private void UpdateStatus()
        {
            if (_loadError != "") { _lblStatus.Text = _loadError; return; }

            int placed = _all.Count(e => e.HasPos);
            string s = _title + "   |   " + _shown.Count + " shown of " + placed + " placed events (" + (_all.Count - placed) + " with no position)";
            if (_unmatched > 0) s += "   |   " + _unmatched + " log lines not matched";
            if (_planInfo != "") s += "   |   " + _planInfo;
            if (_tracks.Count > 0) s += "   |   Tracks: " + _tracks.Count + " player aircraft" + (_tracksComplete ? "" : " (INCOMPLETE file: DCS stopped before the end of the mission)");
            if (_background == null) s += "   |   No web map in the cache: open the Map window of this campaign once.";
            _lblStatus.Text = s;
        }

        // Plans de vol : tous en filigrane ; ceux du filtre (camp, vol) plus marqués, dessinés en dernier (donc dessus)
        private void RebuildPlans()
        {
            // Vol dont le plan est mis en avant : le vol choisi, sinon celui de l'événement sélectionné
            string focus = FocusFlight();
            _focusNow = focus;
            UpdateRelated();

            bool anySel = _selSide != "" || focus != "";
            var faint = new List<Map_Drawing>();
            var strong = new List<Map_Drawing>();

            foreach (KeyValuePair<string, List<Map_Drawing>> kv in _planLegs)
            {
                string side;
                if (!_flightSide.TryGetValue(kv.Key, out side)) side = "";

                bool isStrong = focus != "" ? (InFocus(kv.Key) || _related.ContainsKey(kv.Key)) : (_selSide != "" && side == _selSide);
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

        private void SelectEvent(Map_Event e, bool center, bool fromList = false)
        {
            if (fromList && e != null)
            {
                // Centre la carte, montre la fiche et la grosse icône ; ne touche ni à la sélection du haut, ni au contenu des listes
                _selFromList = true;
                _selected = e;
                _details.Text = Describe(e);
                if (_iconBox != null) _iconBox.Invalidate();
                if (e.HasPos && center) _view.CenterOn(e.X, e.Y);
                _view.Invalidate();
                return;
            }
            _selFromList = false;
            _extraFlights.Clear();                 // un clic simple remplace la sélection multiple
            _selected = e;
            _focus = e == null ? "" : (e.Flight != "" ? e.Flight : e.KillerFlight);      // tout ce qui est lié à ce vol est mis en avant
            if (e != null && _focus != "")
                SyncCombos(_focus, e.Flight != "" ? UnitNameOf(e) : e.Killer);              // les listes Flight / Aircraft du haut suivent le clic
            _details.Text = e == null ? FlightSummary() : Describe(e);
            if (_iconBox != null) _iconBox.Invalidate();
            RefreshList();

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

        // Règle les listes Flight / Aircraft du haut sur le vol (et l'avion) cliqués, comme si on les avait choisis à la main
        private void SyncCombos(string flight, string unit)
        {
            if (flight == "") return;
            if (string.Equals(_selFlight, flight, StringComparison.OrdinalIgnoreCase) && _selUnit == unit) { _comboFromClick = true; return; }      // déjà réglées

            int row = KindOf(flight);
            ComboBox cf = _fCombo[row], cu = _uCombo[row];
            int fi = -1;
            for (int i = 1; i < cf.Items.Count; i++)
            {
                FlightItem it = cf.Items[i] as FlightItem;
                if (it != null && string.Equals(it.Key, flight, StringComparison.OrdinalIgnoreCase)) { fi = i; break; }
            }
            if (fi < 0) return;                                  // vol absent de la liste (ex: autre camp filtré) : la mise en avant simple suffit

            _syncFilter = true;
            try
            {
                ResetOtherRows(row);
                cf.SelectedIndex = fi;
                _selFlight = ((FlightItem)cf.Items[fi]).Key;
                FillUnits();                                      // remet "All ..."
                if (unit != "")
                    for (int j = 1; j < cu.Items.Count; j++)
                    {
                        FlightItem u = cu.Items[j] as FlightItem;
                        if (u != null && u.Key == unit) { cu.SelectedIndex = j; _selUnit = unit; break; }
                    }
            }
            finally { _syncFilter = false; }

            _comboFromClick = true;
            _syncSel = true;                                      // la liste des événements peut être refaite : pas de nouvelle sélection par elle
            try
            {
                if (_chkHide.Checked) ApplyFilters();
                else { RebuildPlans(); UpdateStatus(); UpdateStats(); }
            }
            finally { _syncSel = false; }
        }

        // Un clic dans le vide : si les listes du haut avaient été réglées par un clic, retour sur "All flights"
        private void ClearComboFocus()
        {
            _extraFlights.Clear();
            if (!_comboFromClick) return;
            _comboFromClick = false;

            _syncFilter = true;
            try
            {
                _selFlight = "";
                ResetOtherRows(-1);
                FillUnits();
            }
            finally { _syncFilter = false; }
            ApplyFilters();
        }

        // Clic sur un tracé ou un plan de vol : on met le vol en avant, sans événement choisi
        private void SelectFlight(string flight, string unit)
        {
            _extraFlights.Clear();                 // un clic simple remplace la sélection multiple
            _selected = null;
            _focus = flight;
            SyncCombos(flight, unit);
            RefreshList();
            _syncSel = true;
            try { _list.SelectedItems.Clear(); _listNoPos.SelectedItems.Clear(); }
            finally { _syncSel = false; }
            _details.Text = FlightSummary();
            if (_iconBox != null) _iconBox.Invalidate();
            RebuildPlans();
            _view.Invalidate();
        }

        private static float SegDist(PointF p, PointF a, PointF b)
        {
            float dx = b.X - a.X, dy = b.Y - a.Y;
            float len2 = dx * dx + dy * dy;
            float t = len2 <= 0f ? 0f : Math.Max(0f, Math.Min(1f, ((p.X - a.X) * dx + (p.Y - a.Y) * dy) / len2));
            float qx = a.X + t * dx - p.X, qy = a.Y + t * dy - p.Y;
            return (float)Math.Sqrt(qx * qx + qy * qy);
        }

        // Nom du vol dont le plan passe à moins de 6 pixels du point, sinon null
        private string PlanHitAt(Point p)
        {
            if (_chkPlans == null || !_chkPlans.Checked) return null;
            string best = null;
            float bestD = 6f;
            foreach (KeyValuePair<string, List<Map_Drawing>> kv in _planLegs)
            {
                string side;
                if (_selSide != "" && _flightSide.TryGetValue(kv.Key, out side) && side != _selSide) continue;
                foreach (Map_Drawing leg in kv.Value)
                {
                    for (int i = 0; i + 1 < leg.Points.Count; i++)
                    {
                        PointF a = _view.ToScreen(leg.Points[i][0], leg.Points[i][1]), b = _view.ToScreen(leg.Points[i + 1][0], leg.Points[i + 1][1]);
                        if (float.IsNaN(a.X) || float.IsNaN(b.X) || Math.Abs(a.X) > 1e5f || Math.Abs(a.Y) > 1e5f || Math.Abs(b.X) > 1e5f || Math.Abs(b.Y) > 1e5f) continue;
                        float d = SegDist(p, a, b);
                        if (d < bestD) { bestD = d; best = kv.Key; }
                    }
                }
            }
            return best;
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

        // Vol dont le tracé (ou à défaut le plan de vol) est sous la souris, "" sinon
        private string FlightAt(Point p)
        {
            Map_Track tr;
            int idx;
            if (TrackHitAt(p, out tr, out idx) && tr.Flight != "") return tr.Flight;
            string pf = PlanHitAt(p);
            return pf ?? "";
        }

        // CTRL + clic : ce vol entre dans la sélection (ou en sort s'il y est déjà)
        private void ToggleFlight(string flight)
        {
            string prim = FocusFlight();
            if (prim == "") { SelectFlight(flight, ""); return; }
            if (string.Equals(prim, flight, StringComparison.OrdinalIgnoreCase) || _extraFlights.Contains(flight)) { RemoveFlight(flight); return; }
            _extraFlights.Add(flight);
            RefreshMulti();
        }

        // Clic droit : ce vol sort de la sélection. Au départ rien n'est choisi, ce qui veut dire "tous les vols" : on garde alors tous les vols SAUF celui-ci.
        private void RemoveFlight(string flight)
        {
            string prim = FocusFlight();
            if (prim == "" && _extraFlights.Count == 0)
            {
                var others = new List<string>();
                ComboBox rc = _fCombo[KindOf(flight)];
                for (int i = 1; i < rc.Items.Count; i++)
                {
                    FlightItem it = rc.Items[i] as FlightItem;
                    if (it != null && !string.Equals(it.Key, flight, StringComparison.OrdinalIgnoreCase)) others.Add(it.Key);
                }
                if (others.Count == 0) return;
                SelectFlight(others[0], "");
                for (int i = 1; i < others.Count; i++) _extraFlights.Add(others[i]);
                RefreshMulti();
                return;
            }

            if (_extraFlights.Remove(flight)) { RefreshMulti(); return; }
            if (!string.Equals(prim, flight, StringComparison.OrdinalIgnoreCase)) return;        // ce vol n'est pas dans la sélection

            if (_extraFlights.Count > 0)
            {
                string next = _extraFlights.First();
                List<string> rest = _extraFlights.Where(x => !string.Equals(x, next, StringComparison.OrdinalIgnoreCase)).ToList();
                SelectFlight(next, "");                      // le suivant devient le vol principal (vide _extraFlights)
                foreach (string r in rest) _extraFlights.Add(r);
                RefreshMulti();
                return;
            }

            // c'était le seul : plus rien n'est mis en avant
            SelectEvent(null, false);
            _comboFromClick = true;
            ClearComboFocus();
        }

        // La sélection de vols a changé : tout se redessine (listes, plans, tracés, bilan). Un seul vol : les listes Aircraft reviennent.
        private void RefreshMulti()
        {
            _selected = null;
            _relKey = "\u0001";
            _listKey = "\u0001";
            _syncFilter = true;
            try
            {
                if (_extraFlights.Count == 0) FillUnits();
                else
                {
                    _selUnit = "";                           // un avion précis n'a pas de sens avec plusieurs vols
                    for (int k = 0; k < 3; k++)
                    {
                        _uCombo[k].Items.Clear();
                        _uCombo[k].Items.Add(new FlightItem("", RowAllUnits[k]));
                        _uCombo[k].SelectedIndex = 0;
                        _uCombo[k].Enabled = false;
                    }
                }
            }
            finally { _syncFilter = false; }

            _details.Text = FlightSummary();
            ApplyFilters();                          // listes, plans, tracés, bilan, barre d'état et icône
        }

        private void MapClicked(Point p)
        {
            // CTRL + clic : le vol dont le tracé / plan est sous la souris entre dans la sélection ou en sort, avant les icônes (comme sur la carte de mission)
            if ((Control.ModifierKeys & Keys.Control) == Keys.Control)
            {
                string cf = FlightAt(p);
                if (cf != "") ToggleFlight(cf);
                return;
            }

            Hit h = HitAt(p);
            if (h == null)
            {
                // Ni icône ni tas : un tracé de vol, puis un plan de vol, mettent leur vol en avant ; sinon on efface la sélection
                Map_Track tr;
                int idx;
                if (TrackHitAt(p, out tr, out idx) && tr.Flight != "") { SelectFlight(tr.Flight, tr.Unit); return; }
                string pf = PlanHitAt(p);
                if (pf != null) { SelectFlight(pf, ""); return; }

                _list.SelectedItems.Clear();
                SelectEvent(null, false);
                ClearComboFocus();
                return;
            }
            if (h.Ev != null)
            {
                if (h.Ev.Kind == "killer_gnd") return;       // marqueur du tueur au sol : rien à sélectionner
                SelectEvent(h.Ev, false);
                return;
            }

            // Un tas : on zoome dessus
            _view.ZoomToBox(h.Items.Min(e => e.X), h.Items.Max(e => e.X), h.Items.Min(e => e.Y), h.Items.Max(e => e.Y));
        }

        private void HoverAt(Point p)
        {
            Hit h = HitAt(p);
            if (h == null)
            {
                Map_Track tr;
                int idx;
                if (TrackHitAt(p, out tr, out idx))
                {
                    string tkey = "t" + tr.Id + "_" + idx;
                    if (tkey != _hoverKey)
                    {
                        _hoverKey = tkey;
                        _hoverTip.Show(TrackText(tr, idx), _view, p.X + 16, p.Y + 16);
                    }
                    return;
                }
                string pf = PlanHitAt(p);
                if (pf != null)
                {
                    string pkey = "p" + pf;
                    if (pkey != _hoverKey)
                    {
                        _hoverKey = pkey;
                        _hoverTip.Show("Flight plan: " + pf + "\nClick to focus on this flight.\nCTRL + click: add / remove it.  Right-click: take it out.", _view, p.X + 16, p.Y + 16);
                    }
                    return;
                }
                if (_hoverKey != "") { _hoverKey = ""; _hoverTip.Hide(_view); }
                return;
            }

            string key = h.Ev != null ? "e" + h.Ev.Id + h.Ev.Kind : "c" + h.Items.Count + "_" + h.Items[0].Id;
            if (key == _hoverKey) return;
            _hoverKey = key;

            string text;
            if (h.Ev != null) text = TipText(h.Ev);
            else
            {
                bool bomb = h.Items[0].Group == "bomb";
                bool tobj = h.Items[0].Group == "tobj";
                var sb = new StringBuilder();
                if (tobj) { sb.Append(h.Items.Count + " intact target objects"); goto tipDone; }        // éléments intacts : une seule ligne
                sb.AppendLine(h.Items.Count + (bomb ? " bomb impacts" : " scenery objects destroyed"));
                foreach (Map_Event e in h.Items.Take(12))
                    sb.AppendLine("  " + (tobj ? DisplayName(e) : FmtT(e.T) + "  " + (bomb ? (e.Weapon != "" ? e.Weapon : "bomb") : DisplayName(e))));
                if (h.Items.Count > 12) sb.AppendLine("  ... and " + (h.Items.Count - 12) + " more");
                sb.Append("Click to zoom in.");
            tipDone:
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
                case "target_obj": return e.Status == "dead" ? "Target object (destroyed earlier)" : "Target object NOT destroyed";
                case "ground_kill": return "Vehicle destroyed";
                case "ship_kill": return "Ship destroyed";
                case "static_kill": return "Static destroyed";
                case "scenery_kill": return e.Target != "" ? "Target object destroyed" : "Scenery destroyed";
                case "eject": return "Pilot ejected";
                case "pilot_dead": return "Pilot killed";
                case "pilot_sar": return e.Current ? "Pilot waiting (SAR)" : "Pilot waiting (old)";
                case "rescue": return "Pilot rescued";
                case "landing": return e.Cause == "airlift" ? "Airlift: landed" : "Helicopter landed away from its base";
                case "damage": return "Aircraft damaged";
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
                case "scen": return e.Kind == "target_obj" ? "position of the target object" : "position of the scenery object";
                case "sar": return "position of the SAR zone";
                case "impact": return "impact point";
                case "pickup": return "where the pilot was picked up";
                case "land": return "landing point";
                case "hit": return "position of the aircraft when hit";
                default: return e.PosSrc;
            }
        }

        // Texte de l'info-bulle : court, même architecture pour toutes les icônes (la fiche du panneau, elle, reste complète)
        //   ligne 0 : l'heure ; puis Type (alias) ; le nom ou le vol ; la cause ; et seulement l'indispensable (le tueur)
        private string TipText(Map_Event e)
        {
            if (e.Kind == "target_obj") return DisplayName(e);                  // une seule ligne

            var sb = new StringBuilder();
            string tn = TypeName(e.Type);

            if (e.Kind == "killer_gnd")
            {
                sb.AppendLine(tn != "" ? tn : e.Name);
                if (e.Target != "") sb.AppendLine("Group: " + e.Target);
                if (e.Info != "") sb.AppendLine("Victim: " + e.Info);
                return sb.ToString().TrimEnd();
            }

            // Ligne 0 : l'heure (et, pour les icônes qu'on ne devine pas, un mot)
            string head = FmtT(e.T);
            switch (e.Kind)
            {
                case "eject": head += "   Ejected"; break;
                case "pilot_dead": head += "   Pilot killed"; break;
                case "pilot_sar": head = e.Current ? "Pilot waiting" : "Pilot waiting (old)"; break;
                case "rescue": head += "   Pilot rescued"; break;
                case "landing": head += "   Landed"; break;
                case "damage": head += "   Damaged"; break;
            }
            sb.AppendLine(head);

            switch (e.Kind)
            {
                case "bomb":
                    sb.AppendLine(e.Weapon != "" ? e.Weapon + (e.Mass > 0 ? " (" + e.Mass.ToString("0", CultureInfo.InvariantCulture) + " kg)" : "") : "bomb");
                    break;

                case "air_lost":
                case "damage":
                case "eject":
                case "pilot_dead":
                case "pilot_sar":
                case "landing":
                    if (tn != "") sb.AppendLine("Type: " + tn);
                    if (e.Flight != "") sb.AppendLine(e.Flight);
                    if (e.Pilot != "" && e.Kind != "air_lost" && e.Kind != "damage") sb.AppendLine("Pilot: " + e.Pilot);
                    if (e.Kind == "damage" && e.Health >= 0) sb.AppendLine("Life left: " + e.Health.ToString("0", CultureInfo.InvariantCulture) + " %");
                    if (e.Kind == "pilot_sar" && e.Status != "") sb.AppendLine("Status: " + e.Status);
                    if (e.Cause != "" && e.Kind != "pilot_sar") sb.AppendLine("Cause: " + e.Cause);
                    break;

                case "rescue":
                    if (e.Unit != "") sb.AppendLine(e.Unit);
                    break;

                default:        // ground_kill, static_kill, ship_kill, scenery_kill
                    if (tn != "") sb.AppendLine("Type: " + tn);
                    string nm = DisplayName(e);
                    if (nm != "" && nm != e.Type && nm != tn) sb.AppendLine(nm);
                    break;
            }

            // Le tueur : seulement l'essentiel (le vol, ou le type du tireur au sol)
            if (e.Kind != "bomb" && e.Kind != "eject" && e.Kind != "pilot_sar" && e.Kind != "rescue" && e.Kind != "landing")
            {
                string by = e.GroundKiller ? (TypeName(e.KillerType) != "" ? TypeName(e.KillerType) : e.Killer)
                                           : (e.KillerFlight != "" ? e.KillerFlight : (TypeName(e.KillerType) != "" ? TypeName(e.KillerType) : e.Killer));
                if (by != "") sb.AppendLine("By: " + by);
            }
            else if (e.Kind == "bomb" && e.KillerFlight != "") sb.AppendLine("By: " + e.KillerFlight);

            return sb.ToString().TrimEnd();
        }

        private string Describe(Map_Event e)
        {
            if (e.Kind == "killer_gnd")
                return "Ground unit that shot at the selected aircraft\nUnit:     " + e.Name + (e.Type != "" ? "  [" + e.Type + "]" : "") + "\nGroup:    " + e.Target + "  (the other units of the group are the small squares)\nSide:     " + e.Side
                    + "\nPosition: start position of the mission (the unit may have moved)" + (e.Info != "" ? "\nVictim:   " + e.Info : "");
            var sb = new StringBuilder();
            sb.AppendLine((e.Kind == "target_obj" ? "" : FmtT(e.T) + "   ") + KindLabel(e));
            string name = DisplayName(e);
            if (name != "") sb.AppendLine(name);

            if (e.Side != "") sb.AppendLine("Side:     " + e.Side);
            if (e.Type != "" && e.Type != name) sb.AppendLine("Type:     " + TypeName(e.Type) + (TypeName(e.Type) != e.Type ? " [" + e.Type + "]" : "") + (e.Category != "" ? " (" + e.Category + ")" : ""));
            if (e.Flight != "") sb.AppendLine("Flight:   " + e.Flight);
            if (e.Flight != "" && PlayersOf(e.Flight).Count > 0) sb.AppendLine("Players:  " + string.Join(", ", PlayersOf(e.Flight)));
            if (e.Unit != "" && e.Kind == "pilot_sar") sb.AppendLine("Aircraft: " + e.Unit);
            if (e.Unit != "" && e.Kind == "rescue") sb.AppendLine("Helicopter: " + e.Unit);
            if (e.Kind == "damage" && e.Health >= 0) sb.AppendLine("Life left: " + e.Health.ToString("0", CultureInfo.InvariantCulture) + " %");
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
                string by = e.Killer + (e.KillerType != "" ? " [" + TypeName(e.KillerType) + "]" : "");
                if (e.KillerSide != "") by += "  (" + e.KillerSide + ")";
                sb.AppendLine("Killed by: " + by);
                if (e.KillerFlight != "") sb.AppendLine((e.GroundKiller ? "  group:   " : "  flight:  ") + e.KillerFlight);
                string killerWho;
                if (_unitPlayer.TryGetValue(e.Killer, out killerWho)) sb.AppendLine("  player:  " + killerWho);
            }
            if (e.Friendly) sb.AppendLine("FRIENDLY FIRE");
            if (e.Weapon != "") sb.AppendLine("Weapon:   " + e.Weapon + (e.Mass > 0 ? "  (" + e.Mass.ToString("0", CultureInfo.InvariantCulture) + " kg)" : ""));

            if (e.Target != "") sb.AppendLine("Target:   " + e.Target);
            if (e.Element != "" && e.Kind == "scenery_kill") sb.AppendLine("Element:  " + e.Element);
            if (e.Kind == "target_obj")
            {
                sb.AppendLine("Class:    " + (e.Category == "MapObject" ? "map building (MapObject)" : "object added by the campaign (static)"));
                if (e.Info != "") sb.AppendLine("Nearest bomb impact: " + e.Info + " m");
            }
            else if (e.Info != "" && e.Kind != "pilot_sar") sb.AppendLine("Info:     " + e.Info);
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
            _iconScale = ComputeIconScale();
            _focusNow = FocusFlight();
            UpdateRelated();
            DrawAaaHatch(g);
            DrawTracks(g);
            DrawVictories(g);
            DrawGroundKillers(g);
            DrawTargetObjects(g);
            if (_shown.Count == 0) { DrawTrackHeads(g); return; }

            int w = _view.Width, h = _view.Height;
            var bombs = new Dictionary<long, Cluster>();
            var scenery = new Dictionary<long, Cluster>();
            var ownBombs = new List<Map_Event>();       // bombes / décors du vol mis en avant : jamais regroupés

            // 1) Bombes et décors : regroupés par case de 40 pixels (sinon 600 icônes l'une sur l'autre)
            foreach (Map_Event e in _shown)
            {
                if (e.Group != "bomb" && e.Group != "scenery") continue;
                if (!TimeOk(e)) continue;
                PointF p = _view.ToScreen(e.X, e.Y);
                if (p.X < -40 || p.Y < -40 || p.X > w + 40 || p.Y > h + 40) continue;

                if (_focusNow != "" && FocusRank(e) > 0) { ownBombs.Add(e); continue; }

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

            foreach (Map_Event e in ownBombs)
            {
                PointF p = _view.ToScreen(e.X, e.Y);
                DrawEvent(g, e, p.X, p.Y);
                _hits.Add(new Hit { X = p.X, Y = p.Y, R = Math.Max(6f, 8f * ScaleOf(e)), Ev = e });
            }

            // 2) Les autres événements, couche par couche. Avec un vol mis en avant : d'abord les autres (pâles), puis ceux liés (halo blanc), puis ceux du vol (dessus, halo or)
            var main = new List<Map_Event>();
            foreach (Map_Event e in _shown)
            {
                if (e.Group == "bomb" || e.Group == "scenery") continue;
                if (!TimeOk(e)) continue;
                PointF p0 = _view.ToScreen(e.X, e.Y);
                if (p0.X < -40 || p0.Y < -40 || p0.X > w + 40 || p0.Y > h + 40) continue;
                main.Add(e);
            }

            // Positions où les icônes sont dessinées : le vrai lieu, ou un lieu voisin libre quand les icônes se touchent
            Dictionary<Map_Event, PointF> disp = SpreadIcons(main);
            DrawLeaders(g, main, disp);

            for (int pass = 0; pass < 3; pass++)
            {
                foreach (Map_Event e in main)
                {
                    int rank = FocusRank(e);
                    if (rank != pass) continue;

                    PointF p = disp[e];
                    DrawEvent(g, e, p.X, p.Y);
                    _hits.Add(new Hit { X = p.X, Y = p.Y, R = Math.Max(7f, 10f * ScaleOf(e)), Ev = e });
                }
            }

            DrawTrackHeads(g);          // les silhouettes des avions passent au-dessus de toutes les icônes
        }

        // Icônes qui se touchent : chacune garde son vrai lieu si la place est libre, sinon elle est posée sur le cercle libre le plus proche.
        // Les icônes du vol mis en avant sont placées en premier (elles restent à leur vrai lieu). Distance minimum = taille de l'icône : elle suit le zoom.
        private Dictionary<Map_Event, PointF> SpreadIcons(List<Map_Event> list)
        {
            var res = new Dictionary<Map_Event, PointF>();
            var truth = new Dictionary<Map_Event, PointF>();
            foreach (Map_Event e in list) truth[e] = _view.ToScreen(e.X, e.Y);

            bool spread = _chkSpread != null && _chkSpread.Checked && list.Count > 1 && list.Count <= 250 && !_view.IsMoving;
            float minD = 14f * _iconScale + 2f;              // chevauchement permis jusqu'à la moitié d'une icône (une icône fait 28 unités de large, halo compris)

            // Dézoomé : on n'écarte rien (ce serait un nuage de traits), sauf le tas dont fait partie l'événement cliqué.
            // Zoomé : on écarte tous les tas.
            HashSet<Map_Event> only = null;
            if (spread && !ZoomedIn())
            {
                if (_selected != null && truth.ContainsKey(_selected)) only = TouchingCluster(list, truth, _selected, minD);
                else spread = false;
            }

            if (!spread)
            {
                foreach (Map_Event e in list) res[e] = truth[e];
                return res;
            }

            // Ordre fixe (peu importe qui est cliqué) : la disposition ne saute pas quand on clique un autre icône du même tas
            var order = list.Select((e, i) => new { e, i, r = FocusRank(e), mv = only == null || only.Contains(e) })
                .OrderBy(x => x.mv ? 1 : 0).ThenByDescending(x => x.r).ThenBy(x => x.i).ToList();
            var placed = new List<PointF>();
            var placedR = new List<float>();
            const int Angles = 12, MaxRings = 6;

            foreach (var item in order)
            {
                PointF p = truth[item.e];
                PointF best = p;
                float rq = 14f * ScaleOf(item.e);
                bool found = !item.mv || IsFree(p, rq, placed, placedR);
                for (int k = 1; !found && k <= MaxRings; k++)
                {
                    float bestD = float.MaxValue;
                    for (int a = 0; a < Angles; a++)
                    {
                        double ang = (a + (k % 2) * 0.5) * 2 * Math.PI / Angles;
                        var q = new PointF(p.X + (float)Math.Cos(ang) * minD * k, p.Y + (float)Math.Sin(ang) * minD * k);
                        if (!IsFree(q, rq, placed, placedR)) continue;
                        float d = (q.X - p.X) * (q.X - p.X) + (q.Y - p.Y) * (q.Y - p.Y);
                        if (d < bestD) { bestD = d; best = q; found = true; }
                    }
                }
                placed.Add(best);
                placedR.Add(rq);
                res[item.e] = best;
            }
            return res;
        }

        // Assez zoomé pour écarter tous les tas : à mi-chemin (échelle logarithmique) entre le début et la fin du grossissement des icônes
        private bool ZoomedIn()
        {
            PointF a = _view.ToScreen(0, 0), b = _view.ToScreen(0, 1000);
            float pxKm = Math.Abs(b.X - a.X);
            return !(float.IsNaN(pxKm) || float.IsInfinity(pxKm)) && pxKm >= Math.Sqrt((double)ZoomStartPxKm * ZoomFullPxKm);
        }

        // L'événement et tous ceux qui le touchent (de proche en proche)
        private static HashSet<Map_Event> TouchingCluster(List<Map_Event> list, Dictionary<Map_Event, PointF> truth, Map_Event seed, float minD)
        {
            var set = new HashSet<Map_Event> { seed };
            var todo = new Queue<Map_Event>();
            todo.Enqueue(seed);
            float m2 = minD * minD;
            while (todo.Count > 0)
            {
                PointF c = truth[todo.Dequeue()];
                foreach (Map_Event e in list)
                {
                    if (set.Contains(e)) continue;
                    float dx = truth[e].X - c.X, dy = truth[e].Y - c.Y;
                    if (dx * dx + dy * dy >= m2) continue;
                    set.Add(e);
                    todo.Enqueue(e);
                }
            }
            return set;
        }

        // Libre si le centre est à au moins la moitié des deux rayons des icônes déjà posées (donc elles se recouvrent d'une demi-icône au plus)
        private static bool IsFree(PointF q, float rq, List<PointF> placed, List<float> placedR)
        {
            for (int i = 0; i < placed.Count; i++)
            {
                float need = (placedR[i] + rq) * 0.5f + 1.5f;
                float dx = placed[i].X - q.X, dy = placed[i].Y - q.Y;
                if (dx * dx + dy * dy < need * need) return false;
            }
            return true;
        }

        // Trait pointillé entre le vrai lieu de l'événement (petit point) et son icône écartée, dessiné sous toutes les icônes
        private void DrawLeaders(Graphics g, List<Map_Event> list, Dictionary<Map_Event, PointF> disp)
        {
            foreach (Map_Event e in list)
            {
                PointF t = _view.ToScreen(e.X, e.Y), d = disp[e];
                if ((t.X - d.X) * (t.X - d.X) + (t.Y - d.Y) * (t.Y - d.Y) < 9f) continue;      // à sa place : pas de trait

                int a = (_focusNow != "" && FocusRank(e) == 0) ? 90 : 255;
                using (var dark = new Pen(Color.FromArgb(a, 0, 0, 0), 3.2f))
                    g.DrawLine(dark, t, d);
                using (var line = new Pen(Color.FromArgb(a, 255, 255, 255), 1.6f) { DashStyle = DashStyle.Dot })
                    g.DrawLine(line, t, d);
                using (var br = new SolidBrush(Color.FromArgb(a, 255, 255, 255)))
                using (var pen = new Pen(Color.FromArgb(a, 0, 0, 0), 1.4f))
                {
                    g.FillEllipse(br, t.X - 3.5f, t.Y - 3.5f, 7f, 7f);
                    g.DrawEllipse(pen, t.X - 3.5f, t.Y - 3.5f, 7f, 7f);
                }
            }
        }

        // Hachures denses sur les zones AAA à éviter
        private void DrawAaaHatch(Graphics g)
        {
            if (_chkAaa == null || !_chkAaa.Checked || _aaaHatch.Count == 0) return;
            RectangleF view = new RectangleF(-50, -50, _view.Width + 100, _view.Height + 100);
            GraphicsState st = g.Save();
            try
            {
                g.SmoothingMode = SmoothingMode.None;      // hachures plus nettes, et plus rapides
                foreach (AaaHatch a in _aaaHatch)
                {
                    var pts = new PointF[a.Pts.Count];
                    bool ok = pts.Length >= 3;
                    for (int i = 0; ok && i < pts.Length; i++)
                    {
                        pts[i] = _view.ToScreen(a.Pts[i][0], a.Pts[i][1]);
                        if (float.IsNaN(pts[i].X) || float.IsNaN(pts[i].Y) || Math.Abs(pts[i].X) > 1e6f || Math.Abs(pts[i].Y) > 1e6f) ok = false;
                    }
                    if (!ok) continue;
                    float minX = pts.Min(q => q.X), maxX = pts.Max(q => q.X), minY = pts.Min(q => q.Y), maxY = pts.Max(q => q.Y);
                    if (!view.IntersectsWith(new RectangleF(minX, minY, maxX - minX, maxY - minY))) continue;

                    // Polygone découpé aux bords de l'écran puis rempli : pas de région GDI+ (très lente quand la zone dépasse l'écran de loin)
                    PointF[] cl = Map_UI_View.ClipPolygon(pts, _view.ClipArea);
                    if (cl.Length < 3) continue;
                    using (var hb = new HatchBrush(HatchStyle.DarkUpwardDiagonal, Color.FromArgb(210, a.Color), Color.Transparent))
                        g.FillPolygon(hb, cl);
                }
            }
            finally { g.Restore(st); }
        }

        // Cibles (bâtiments de la target list) encore debout près des bombes : dessinées sous les événements.
        // Icônes deux fois plus petites que les autres ; regroupées par case de 40 px (et par camp) quand on dézoome.
        private void DrawTargetObjects(Graphics g)
        {
            if (_chkTObj == null || !_chkTObj.Checked || _targetObjs.Count == 0) return;
            int w = _view.Width, h = _view.Height;
            bool near = ZoomedIn();

            var groups = new Dictionary<string, Cluster>();
            foreach (Map_Event e in _targetObjs)
            {
                if (!TimeOk(e)) continue;               // pas avant la bombe qui le touche
                PointF p = _view.ToScreen(e.X, e.Y);
                if (p.X < -40 || p.Y < -40 || p.X > w + 40 || p.Y > h + 40) continue;

                if (!near)
                {
                    // Dézoomé : un tout petit carré vide (pas de pastille ni de nombre), pour ne voir surtout que les détruits
                    Color tc = e.Status == "dead" ? Color.FromArgb(130, 130, 130) : SideColor(e.Side);
                    using (var tb = new SolidBrush(Color.FromArgb(235, 255, 255, 255))) g.FillRectangle(tb, p.X - 3f, p.Y - 3f, 6f, 6f);
                    using (var tp = new Pen(tc, 1.6f)) g.DrawRectangle(tp, p.X - 3f, p.Y - 3f, 6f, 6f);
                    _hits.Add(new Hit { X = p.X, Y = p.Y, R = 5f, Ev = e });
                    continue;
                }

                string key = e.Side + "|" + Math.Floor(p.X / CellPx) + "|" + Math.Floor(p.Y / CellPx);
                Cluster c;
                if (!groups.TryGetValue(key, out c)) { c = new Cluster(); groups[key] = c; }
                c.Items.Add(e);
                c.SX += p.X;
                c.SY += p.Y;
            }

            float keep = _iconScale;
            foreach (Cluster c in groups.Values)
            {
                float cx = c.SX / c.Items.Count, cy = c.SY / c.Items.Count;
                if (c.Items.Count == 1)
                {
                    _iconScale = keep * 0.5f;
                    DrawEvent(g, c.Items[0], cx, cy);
                    _iconScale = keep;
                    _hits.Add(new Hit { X = cx, Y = cy, R = 9f * keep * 0.5f, Ev = c.Items[0] });
                    continue;
                }

                // Un tas : pastille blanche cerclée de la couleur du camp, avec le nombre
                float r = 9f + (float)Math.Min(8.0, Math.Sqrt(c.Items.Count));
                bool faint = _focusNow != "";
                Color line = Color.FromArgb(faint ? 80 : 255, SideColor(c.Items[0].Side));
                using (var br = new SolidBrush(Color.FromArgb(faint ? 70 : 215, 255, 255, 255)))
                    g.FillEllipse(br, cx - r, cy - r, 2 * r, 2 * r);
                using (var pen = new Pen(line, 1.8f) { DashStyle = DashStyle.Dash })
                    g.DrawEllipse(pen, cx - r, cy - r, 2 * r, 2 * r);
                using (var tb = new SolidBrush(Color.FromArgb(faint ? 90 : 255, 0, 0, 0)))
                    g.DrawString(c.Items.Count.ToString(CultureInfo.InvariantCulture), _fontBold, tb, new RectangleF(cx - r, cy - r, 2 * r, 2 * r), _sfCenter);
                _hits.Add(new Hit { X = cx, Y = cy, R = r, Items = c.Items });
            }
            _iconScale = keep;
        }

        private void DrawClusters(Graphics g, Dictionary<long, Cluster> dict, Color color)
        {
            foreach (Cluster c in dict.Values)
            {
                float cx = c.SX / c.Items.Count, cy = c.SY / c.Items.Count;
                if (c.Items.Count == 1)
                {
                    DrawEvent(g, c.Items[0], cx, cy);
                    _hits.Add(new Hit { X = cx, Y = cy, R = Math.Max(6f, 8f * ScaleOf(c.Items[0])), Ev = c.Items[0] });
                    continue;
                }

                float r = 14f + (float)Math.Min(12.0, Math.Sqrt(c.Items.Count));
                bool faint = _focusNow != "";
                using (var br = new SolidBrush(Color.FromArgb(faint ? 60 : 205, color)))
                    g.FillEllipse(br, cx - r, cy - r, 2 * r, 2 * r);
                using (var pen = new Pen(Color.FromArgb(faint ? 70 : 230, 30, 30, 30), 1.4f))
                    g.DrawEllipse(pen, cx - r, cy - r, 2 * r, 2 * r);
                using (var tb = new SolidBrush(Color.FromArgb(faint ? 90 : 255, 0, 0, 0)))
                    g.DrawString(c.Items.Count.ToString(CultureInfo.InvariantCulture), _fontBold, tb, new RectangleF(cx - r, cy - r, 2 * r, 2 * r), _sfCenter);
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
        private static readonly Regex RxDecorStatic = new Regex(@"camouflage|sandbag|(^|[^a-z])bag([^a-z]|$)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
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

                // Objets au sol (posés par la mission, décors de la carte, éléments de la target list) : UNE seule règle, donc le même type donne la même icône
                case "static_kill":
                case "scenery_kill":
                case "target_obj":
                    if (RxStaticHeli.IsMatch(typ)) return "heli";
                    if (RxStaticPlane.IsMatch(typ)) return "plane";
                    return FirstMatch(VehicleRules, typ) ?? FirstMatch(StructureRules, typ)
                        ?? FirstMatch(StructureRules, nam) ?? "building";
            }
            return "";
        }

        // Couleur et transparence de l'icône d'un événement (la même sur la carte et dans la grosse icône du panneau)
        private static Color EventColor(Map_Event e, out int alpha)
        {
            // Position approximative : icône plus pâle
            alpha = (e.PosSrc == "sar_approx" || e.PosSrc == "oob_start") ? 130 : 255;
            Color c = Color.FromArgb(alpha, SideColor(e.Side));

            // Décor dont le camp est inconnu : beige (sinon la couleur du camp, comme tout le reste)
            if (e.Kind == "scenery_kill" && SideColor(e.Side) == SideColor("")) c = Color.FromArgb(220, 190, 150, 90);
            return c;
        }

        // Taille des icônes : normale de loin, de plus en plus grosse quand on s'approche du zoom maximum
        private float ComputeIconScale()
        {
            PointF a = _view.ToScreen(0, 0), b = _view.ToScreen(0, 1000);      // 1 km vers l'est
            float pxKm = Math.Abs(b.X - a.X);
            if (float.IsNaN(pxKm) || float.IsInfinity(pxKm) || pxKm <= 0f) { _pxPerM = 0f; return IconScaleMin * _iconMul; }
            _pxPerM = pxKm / 1000f;

            double t = (Math.Log10(pxKm) - Math.Log10(ZoomStartPxKm)) / (Math.Log10(ZoomFullPxKm) - Math.Log10(ZoomStartPxKm));
            t = Math.Max(0.0, Math.Min(1.0, t));
            return (IconScaleMin + (float)t * (IconScaleMax - IconScaleMin)) * _iconMul;
        }

        // Taille réelle au sol (en mètres) de ce que représente chaque famille d'icône
        private static float RealMeters(string fam)
        {
            switch (fam)
            {
                case "plane": return 15f;
                case "heli": return 12f;
                case "tank": return 8f;
                case "truck": return 7f;
                case "inf": return 3f;
                case "sam": return 12f;
                case "aaa": return 6f;
                case "radar": return 8f;
                case "art": return 7f;
                case "ship": return 90f;
                case "building": return 12f;
                case "bridge": return 40f;
                case "crate": return 5f;
                default: return 8f;
            }
        }

        // Échelle d'une icône. Taille à l'écran = taille réelle (mètres x pixels par mètre), avec un minimum lisible :
        // de loin toutes les icônes ont donc le même petit format, et au zoom maximum elles ont leur vraie taille au sol.
        // Le bouton "icônes plus petites / plus grandes" change le minimum. Jamais plus grosse que la taille automatique.
        private float ScaleOf(Map_Event e)
        {
            if (_pxPerM <= 0f) return _iconScale;

            if (e.Kind == "bomb")
            {
                float m = e.Mass > 0 ? 1.3f * (float)Math.Pow(e.Mass, 1.0 / 3.0) : CraterDefaultM;       // 87 kg -> environ 6 m
                float px = Math.Max(CraterMinPx * _iconMul, m * _pxPerM);
                return Math.Min(px, 14f * _iconScale) / 14f;                                            // l'icône du cratère fait 14 unités
            }

            if (e.Kind == "air_lost") return _iconScale;        // même logique que le parachute : taille automatique du zoom

            if (e.Kind == "ground_kill" || e.Kind == "ship_kill" || e.Kind == "static_kill"
                || e.Kind == "scenery_kill" || e.Kind == "target_obj")
            {
                string fam = IconOf(e);
                float units = fam == "truck" ? 20f : 18f;                                               // longueur de l'icône dans son dessin
                float px = Math.Max(FamilyMinPx * _iconMul, RealMeters(fam) * _pxPerM);
                return Math.Min(px, units * _iconScale) / units;
            }
            return _iconScale;
        }

        // Tueur au sol : tout son groupe (petits carrés), le tueur lui-même en gros, et un trait pointillé vers l'avion touché
        private void DrawGroundKillers(Graphics g)
        {
            if (_gndKillers.Count == 0) return;
            SmoothingMode old = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float s = Math.Max(1f, _iconScale);

            foreach (Map_Event ev in _gndKillers)
            {
                if (!TimeOk(ev)) continue;
                Color col = SideColor(ev.KillerSide);
                PointF k = _view.ToScreen(ev.KX, ev.KY);
                if (float.IsNaN(k.X) || Math.Abs(k.X) > 1e5f || Math.Abs(k.Y) > 1e5f) continue;

                if (ev.HasPos)
                {
                    PointF v = _view.ToScreen(ev.X, ev.Y);
                    // Cause du kill : trait ROUGE épais en pointillés (sur un liseré sombre pour qu'il se voie sur tous les fonds)
                    if (Math.Abs(v.X) < 1e5f && Math.Abs(v.Y) < 1e5f)
                    {
                        using (var dark = new Pen(Color.FromArgb(150, 0, 0, 0), 6f))
                            g.DrawLine(dark, k, v);
                        using (var pen = new Pen(Color.FromArgb(245, 255, 40, 30), 3.6f) { DashStyle = DashStyle.Custom, DashPattern = new float[] { 2.2f, 1.6f } })
                            g.DrawLine(pen, k, v);
                    }
                }

                GroundGroup gg;
                if (_groundGroups.TryGetValue(ev.KillerGroup, out gg))
                    foreach (GroundMember m in gg.Units)
                    {
                        if (m.Name == ev.Killer) continue;
                        PointF q = _view.ToScreen(m.X, m.Y);
                        float h = 3.2f * s;
                        using (var br = new SolidBrush(Color.FromArgb(170, col)))
                            g.FillRectangle(br, q.X - h, q.Y - h, 2 * h, 2 * h);
                        g.DrawRectangle(Pens.Black, q.X - h, q.Y - h, 2 * h, 2 * h);
                    }

                float r = 6f * s;
                using (var gold = new Pen(Color.Gold, 2.4f))
                    g.DrawEllipse(gold, k.X - r * 1.7f, k.Y - r * 1.7f, r * 3.4f, r * 3.4f);
                using (var br = new SolidBrush(col))
                    g.FillEllipse(br, k.X - r, k.Y - r, 2 * r, 2 * r);
                g.DrawEllipse(Pens.Black, k.X - r, k.Y - r, 2 * r, 2 * r);
                using (var white = new Pen(Color.White, 1.6f))
                {
                    g.DrawLine(white, k.X - r * 0.6f, k.Y, k.X + r * 0.6f, k.Y);
                    g.DrawLine(white, k.X, k.Y - r * 0.6f, k.X, k.Y + r * 0.6f);
                }
                string label = ev.KillerType != "" ? ev.KillerType : ev.Killer;
                if (_view.ShowLabels)
                {
                    g.DrawString(label, _fontBold, Brushes.Black, k.X + r * 1.8f + 1f, k.Y - 7f);
                    using (var tb = new SolidBrush(col)) g.DrawString(label, _fontBold, tb, k.X + r * 1.8f, k.Y - 8f);
                }

                var info = new Map_Event { Id = -(2000 + (ev.Killer.GetHashCode() & 0xFFFFF)), Kind = "killer_gnd", Name = ev.Killer, Type = ev.KillerType, Side = ev.KillerSide, Target = ev.KillerGroup, HasPos = true, X = ev.KX, Y = ev.KY, Info = ev.Name };
                _hits.Add(new Hit { X = k.X, Y = k.Y, R = r * 1.7f, Ev = info });
            }
            g.SmoothingMode = old;
        }

        // Icône hors du vol mis en avant : dessinée dans une image réutilisée, puis recopiée à 28 % d'opacité
        private void DrawGhost(Graphics g, Map_Event e, float x0, float y0)
        {
            int size = (int)Math.Ceiling(36f * _iconScale) + 8;
            if (_ghost == null || _ghost.Width != size)
            {
                if (_ghost != null) _ghost.Dispose();
                _ghost = new Bitmap(size, size, PixelFormat.Format32bppPArgb);
            }
            if (_ghostAttr == null)
            {
                var m = new ColorMatrix();
                m.Matrix33 = 0.28f;
                _ghostAttr = new ImageAttributes();
                _ghostAttr.SetColorMatrix(m, ColorMatrixFlag.Default, ColorAdjustType.Bitmap);
            }

            using (Graphics gg = Graphics.FromImage(_ghost))
            {
                gg.Clear(Color.Transparent);
                gg.SmoothingMode = SmoothingMode.AntiAlias;
                int alpha;
                Color c = EventColor(e, out alpha);
                gg.TranslateTransform(size / 2f, size / 2f);
                gg.ScaleTransform(_iconScale, _iconScale);
                DrawEventIcon(gg, e, c, alpha);
            }

            var dest = new Rectangle((int)Math.Round(x0 - size / 2f), (int)Math.Round(y0 - size / 2f), size, size);
            g.DrawImage(_ghost, dest, 0, 0, size, size, GraphicsUnit.Pixel, _ghostAttr);
        }

        private void DrawEvent(Graphics g, Map_Event e, float x0, float y0)
        {
            float keepScale = _iconScale;
            _iconScale = ScaleOf(e);              // taille propre à cet événement (camion, cratère) ; remise en place à la fin
            try { DrawEventAt(g, e, x0, y0); }
            finally { _iconScale = keepScale; }
        }

        private void DrawEventAt(Graphics g, Map_Event e, float x0, float y0)
        {
            if (_focusNow != "" && FocusRank(e) == 0)
            {
                if (_view.IsMoving)         // pendant un zoom : un simple point pâle (l'image pâle de l'icône est refaite à chaque icône)
                {
                    int ga;
                    Color gc = EventColor(e, out ga);
                    using (var gb = new SolidBrush(Color.FromArgb(80, gc))) g.FillEllipse(gb, x0 - 5f, y0 - 5f, 10f, 10f);
                    return;
                }
                DrawGhost(g, e, x0, y0);
                return;
            }

            int alpha;
            Color c = EventColor(e, out alpha);

            // Les icônes sont dessinées autour de (0,0) à la taille d'origine, puis agrandies par _iconScale
            GraphicsState st = g.Save();
            g.TranslateTransform(x0, y0);
            g.ScaleTransform(_iconScale, _iconScale);
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
                        DrawCross(g, x, y, e.Kind == "air_lost" ? 6f : 5f);
                        break;

                    case "target_obj":
                        {
                            // Cible encore debout : icône VIDE (fond blanc, gros contour de la couleur du camp) + pastille verte avec une coche ; grise sans coche si détruite avant cette mission
                            bool oldDead = e.Status == "dead";
                            Color line = oldDead ? Color.FromArgb(alpha, 130, 130, 130) : c;
                            using (var tfill = new SolidBrush(Color.FromArgb(oldDead ? 110 : 235, 255, 255, 255)))
                            using (var tpen = new Pen(line, 2.4f) { LineJoin = LineJoin.Round })
                                DrawFamily(g, IconOf(e), tfill, tpen, line);
                            if (!oldDead)
                            {
                                using (var gb = new SolidBrush(Color.FromArgb(255, 40, 170, 70))) g.FillEllipse(gb, x + 3f, y - 14f, 11f, 11f);
                                g.DrawEllipse(Pens.White, x + 3f, y - 14f, 11f, 11f);
                                using (var ck = new Pen(Color.White, 1.8f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
                                    g.DrawLines(ck, new[] { new PointF(x + 5.4f, y - 8.4f), new PointF(x + 7.6f, y - 6f), new PointF(x + 11.6f, y - 11f) });
                            }
                            break;
                        }

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
                        {
                            // Pilote de profil (casque, visière, masque à oxygène), noir avec un fin liseré blanc ; croix rouge dessus = tué
                            using (var wl = new Pen(Color.FromArgb(alpha, 255, 255, 255), 1.0f) { LineJoin = LineJoin.Round })
                            {
                                var sh = new[] { new PointF(x - 10, y + 10), new PointF(x - 8, y + 5), new PointF(x - 2, y + 3.5f), new PointF(x + 4, y + 4.5f), new PointF(x + 10, y + 10) };
                                g.FillPolygon(Brushes.Black, sh);                                                   // épaules
                                g.DrawPolygon(wl, sh);
                                var mask = new[] { new PointF(x + 3, y - 1), new PointF(x + 9, y - 0.5f), new PointF(x + 9.5f, y + 3), new PointF(x + 5.5f, y + 6), new PointF(x + 1, y + 4.5f) };
                                g.FillPolygon(Brushes.Black, mask);                                                 // masque
                                g.DrawPolygon(wl, mask);
                                g.FillEllipse(Brushes.Black, x - 8.5f, y - 11, 16, 15);                             // casque
                                g.DrawEllipse(wl, x - 8.5f, y - 11, 16, 15);
                                var visor = new[] { new PointF(x + 0.5f, y - 6.5f), new PointF(x + 7.5f, y - 5), new PointF(x + 8.5f, y - 1.5f), new PointF(x + 2, y - 1.5f) };
                                g.FillPolygon(Brushes.Black, visor);                                                // visière
                                g.DrawPolygon(wl, visor);
                            }
                            using (var hi = new Pen(Color.White, 1.3f)) g.DrawArc(hi, x - 6, y - 9.5f, 10, 8, 200, 70);           // reflet du casque
                            using (var hose = new Pen(Color.FromArgb(200, 200, 200), 1.4f)) g.DrawCurve(hose, new[] { new PointF(x + 4, y + 5), new PointF(x + 2, y + 8), new PointF(x - 1, y + 10) });   // tuyau
                            using (var rb = new Pen(Color.Black, 4.4f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                            using (var rr = new Pen(Color.FromArgb(255, 235, 30, 30), 2.6f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                            {
                                g.DrawLine(rb, x - 8, y - 9, x + 8, y + 9);
                                g.DrawLine(rb, x - 8, y + 9, x + 8, y - 9);
                                g.DrawLine(rr, x - 8, y - 9, x + 8, y + 9);
                                g.DrawLine(rr, x - 8, y + 9, x + 8, y - 9);
                            }
                            break;
                        }

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

                    case "rescue":
                        // Disque vert, croix blanche
                        using (var gb = new SolidBrush(Color.FromArgb(alpha, 40, 170, 70))) g.FillEllipse(gb, x - 8, y - 8, 16, 16);
                        g.DrawEllipse(edge, x - 8, y - 8, 16, 16);
                        using (var wp = new Pen(Color.White, 3f))
                        {
                            g.DrawLine(wp, x - 4, y, x + 4, y);
                            g.DrawLine(wp, x, y - 4, x, y + 4);
                        }
                        break;

                    case "landing":
                        // Carré de la couleur du camp, un H blanc
                        g.FillRectangle(fill, x - 8, y - 8, 16, 16);
                        g.DrawRectangle(edge, x - 8, y - 8, 16, 16);
                        using (var hp = new Pen(Color.White, 2.2f))
                        {
                            g.DrawLine(hp, x - 3.5f, y - 4.5f, x - 3.5f, y + 4.5f);
                            g.DrawLine(hp, x + 3.5f, y - 4.5f, x + 3.5f, y + 4.5f);
                            g.DrawLine(hp, x - 3.5f, y, x + 3.5f, y);
                        }
                        break;

                    case "damage":
                        {
                            // Avion (ou hélico) du camp + triangle orange d'avertissement en haut à droite
                            DrawFamily(g, e.Category == "helicopter" ? "heli" : "plane", fill, edge, c);
                            var tri = new[] { new PointF(x + 8, y - 12), new PointF(x + 14, y - 1), new PointF(x + 2, y - 1) };
                            using (var ob = new SolidBrush(Color.FromArgb(255, 160, 20))) g.FillPolygon(ob, tri);
                            g.DrawPolygon(edge, tri);
                            g.DrawLine(Pens.Black, x + 8, y - 9, x + 8, y - 5);
                            g.FillEllipse(Brushes.Black, x + 7.2f, y - 3.6f, 1.6f, 1.6f);
                            break;
                        }

                    case "bomb":
                        {
                            // Cratère : bourrelet de terre claire, creux sombre, fond très sombre (14 unités de large)
                            using (var rim = new SolidBrush(Color.FromArgb(alpha, 196, 160, 110))) g.FillEllipse(rim, x - 7f, y - 7f, 14f, 14f);
                            using (var pit = new SolidBrush(Color.FromArgb(alpha, 92, 62, 38))) g.FillEllipse(pit, x - 4.8f, y - 4.8f, 9.6f, 9.6f);
                            using (var core = new SolidBrush(Color.FromArgb(alpha, 34, 22, 14))) g.FillEllipse(core, x - 2.4f, y - 2.4f, 4.8f, 4.8f);
                            g.DrawEllipse(edge, x - 7f, y - 7f, 14f, 14f);
                            break;
                        }

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
                        for (int i = 0; i < pts.Length; i++) pts[i] = new PointF(JetShape[i].X * 11f, JetShape[i].Y * 11f);
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
                    // Camion vu de dessus, cabine en haut (nord), 20 unités de long x 9 de large
                    g.FillRectangle(Brushes.Black, -5.2f, -8f, 1.6f, 3.4f);        // roues avant
                    g.FillRectangle(Brushes.Black, 3.6f, -8f, 1.6f, 3.4f);
                    g.FillRectangle(Brushes.Black, -5.2f, 3.5f, 1.6f, 3.4f);       // roues arrière
                    g.FillRectangle(Brushes.Black, 3.6f, 3.5f, 1.6f, 3.4f);
                    g.FillRectangle(fill, -3.6f, -1f, 7.2f, 11f);                  // benne
                    g.DrawRectangle(edge, -3.6f, -1f, 7.2f, 11f);
                    g.FillRectangle(fill, -3.6f, -10f, 7.2f, 8f);                  // cabine
                    g.DrawRectangle(edge, -3.6f, -10f, 7.2f, 8f);
                    g.FillRectangle(Brushes.DimGray, -2.6f, -8.6f, 5.2f, 2.4f);    // pare-brise
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
            if (_playTimer != null) { _playTimer.Stop(); _playTimer.Dispose(); }
            _view.PlayCache = false;
            _view.OverlayPaint -= PaintEvents;
            _hoverTip.Dispose();
            if (_ghost != null) _ghost.Dispose();
            if (_ghostAttr != null) _ghostAttr.Dispose();
            _tip.Dispose();
            _fontBold.Dispose();
            _statFont.Dispose();
            _statBold.Dispose();
            _statCap.Dispose();
            _statGlyph.Dispose();
            _sfCenter.Dispose();
            if (_background != null) _background.Dispose();
            base.OnFormClosed(e);
        }
    }
}
