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
    // Grande fenêtre "Map" : toutes les bases de la map DCS + les squadrons de chaque base.
    //  - affichage : bases, silhouettes (plusieurs squadrons), fond de carte web calé automatiquement
    //  - édition : Edit / Clone / Delete / Move (clic sur la carte) puis Save
    //
    // Les squadrons modifiés sont ceux de la fenêtre principale (mêmes objets en mémoire) :
    // les onglets OOB se mettent à jour, et on écrit avec la même fonction que leur bouton Save.
    internal class Map_UI_Form : Form
    {
        private readonly string _campaign;
        private Action _runwaysReady;
        private CheckBox _chkRunEdit;           // mode "tirer les bords des pistes" (carte Map seulement)
        private CheckBox _chkWebRunways;        // pistes du web au lieu des données locales (db_airbases + réglages)
        private bool _syncWeb;
        private CheckBox _chkPark;                  // places d'hélicoptère (parkAlertSAR) : voir, déplacer, tourner, ajouter, supprimer
        private Panel _parkBar;
        private Button _btnParkAdd, _btnParkDel, _btnParkKeep;
        private NumericUpDown _numParkHdg;
        private Label _lblParkRad, _lblParkInfo;
        // Places d'hélicoptère : un jeu par dossier ("Init" / "Active" : carte Map = celui qu'on regarde ; carte de mission = first -> Init, ongoing -> Active),
        // avec les bases dont les places ont changé (écrites au Save)
        private readonly Dictionary<string, Dictionary<string, List<Map_ParkSpot>>> _parkStores = new Dictionary<string, Dictionary<string, List<Map_ParkSpot>>>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, HashSet<string>> _parkDirties = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        private string _parkMsg = "";
        private Map_ParkSpot _parkCur;
        private bool _parkSync;
        private Button _btnRunData;             // saisie exacte des pistes + règle de position pour toute la carte
        private readonly bool _justGenerated;
        private string _missionFile = "";       // .miz ouvert par la carte de mission (affiché dans le titre)
        private readonly bool _missionMode;     // true = carte de la mission en cours (lecture seule, plans de vol et menaces)

        // Données lues sur disque (bouton "Reload" pour relire les bases)
        private Dictionary<string, AirbaseInfo> _airbases = new Dictionary<string, AirbaseInfo>(StringComparer.OrdinalIgnoreCase);

        // Squadrons : ceux de la fenêtre principale (modifiables) ou, à défaut, une lecture seule du disque
        private Campaign_Edit_Grid_Right _edit;
        private List<CampaignSquad> _allSquads = new List<CampaignSquad>();
        private List<Squad> _flat;              // liste "à plat" qui sert à écrire les fichiers (null en lecture seule)
        private bool _readOnly;
        private CampaignContext _ctx;           // nécessaire à SquadEdit_Form (créé au premier Edit)

        // Données du mode affiché (Init ou Active)
        private Dictionary<string, List<Squad>> _squadsByBase = new Dictionary<string, List<Squad>>(StringComparer.OrdinalIgnoreCase);
        private readonly List<string> _noPosNames = new List<string>();
        private string _selectedBase = "";
        private bool _loading;

        // Modifications pas encore écrites dans les fichiers
        private bool _dirtyInit;
        private bool _dirtyActive;

        // Photo des squadrons telle qu'elle est dans les fichiers (à l'ouverture, puis à chaque Save réussi) :
        // "Quit" sans sauvegarder remet les squadrons dans cet état (les changements sont faits en direct sur la liste de la fenêtre principale)
        private class SnapEntry
        {
            public Squad Original;
            public Squad Copy;
            public bool IsInit;
            public CampaignSquad Owner;
        }
        private readonly List<SnapEntry> _snapshot = new List<SnapEntry>();

        // Squadron en cours de déplacement : le prochain clic sur une base est sa destination
        private Squad _moveSquad;

        // Fond de carte web
        private Map_Background _background;
        private bool _busy;

        // Contrôles
        private Map_UI_View _view;
        private RadioButton _radioInit;
        private RadioButton _radioActive;
        private CheckBox _chkHideEmpty;
        private CheckBox _chkRuler;
        private CheckBox _chkBackground;
        private Label _lblBase;

        // Carte de mission : onglets Flights / Bases (lecture seule), fiches et liste des vols
        private TabControl _tabs;
        private TabPage _tabFlights, _tabBases;
        private Map_UI_Card _baseCard;
        private Label _flightInfo;                  // une ligne grise sous la liste des vols : camp, nombre d'avions, durée, distance, contrôle du plan
        private Map_UI_Section _secRadio;           // bandeau de la section "Radio presets" (vols joueur / client seulement)
        private Panel _radioPanel;                  // le contenu de la section : un bouton par radio, puis la grille des canaux
        private Panel _radioTabs, _radioList;       // en-tête (un bouton par radio) et liste des canaux (défile si elle est trop haute)
        private Label _radioInfo;                   // ligne grise du bas
        private readonly List<Button> _radioButtons = new List<Button>();
        private readonly List<RadioRow> _radioRows = new List<RadioRow>();     // lignes créées une fois, réutilisées à chaque changement de vol / de radio (rien n'est recréé : pas de scintillement)
        private bool _radioFill;                    // vrai pendant le remplissage : les TextChanged sont ignorés
        private Map_Flight _radioFlight;            // vol dont les presets sont affichés
        private List<List<double>> _radioOrig;      // ses presets d'origine (fichier)
        private List<double[]> _radioRanges = new List<double[]>();     // plages permises pour la radio affichée
        private int _radioWantH = 100;              // hauteur voulue du panneau (limitée à la hauteur de la carte dans PlaceOverlays)
        private string _radioUsesSrc;               // texte de briefing déjà analysé
        private static readonly Font FCell = new Font("Segoe UI", 8.5f), FSym = new Font("Segoe UI Symbol", 8f), FCross = new Font("Segoe UI", 8f, FontStyle.Bold),
            FSmall75 = new Font("Segoe UI", 7.5f), FTitle9 = new Font("Segoe UI", 9f, FontStyle.Bold);         // polices partagées du cadre des avions (plus de new Font à chaque reconstruction)
        private static readonly Font FRadio = new Font("Segoe UI", 8.5f), FRadioSmall = new Font("Segoe UI", 8f), FRadioBold = new Font("Segoe UI", 8.5f, FontStyle.Bold);

        private class RadioRow { public Label Num, Use; public TextBox Box; }
        private class RadioUseTable { public Dictionary<long, string> ByFreq = new Dictionary<long, string>(); public List<KeyValuePair<long, string>> Items = new List<KeyValuePair<long, string>>(); }
        private readonly Dictionary<string, List<int>> _presetFirst = new Dictionary<string, List<int>>();      // vol -> numéro du 1er canal de chaque radio (0 ou 1)
        private RadioUseTable _radioUsesTable = new RadioUseTable();
        private bool _radioPick;                    // la liste de choix des usages est proposée (crayon actif et au moins un usage dans la plage de la radio)

        private bool _radioHas;                     // le vol affiché a des presets radio (la section peut alors être montrée)
        private int _radioTab;                      // radio affichée (0 = Radio 1)
        private readonly Dictionary<string, List<List<double>>> _presetOrig = new Dictionary<string, List<List<double>>>();     // vol -> presets lus dans le fichier (cache)
        private readonly Dictionary<string, int> _presetUnits = new Dictionary<string, int>();                                  // vol -> nombre d'avions qui ont une table Radio
        private readonly Dictionary<string, List<List<double>>> _pendingPresets = new Dictionary<string, List<List<double>>>(); // vol -> presets voulus, écrits au clic sur Save modification
        private Map_UI_Section _secSky;             // bandeau de la section météo (clic = replie la section sur une ligne)
        private Map_UI_FlightList _flightList;
        private Map_MissionData _missionData;
        private bool _syncFlightList, _syncWp;
        private Map_UI_WaypointList _wpList;
        private Map_UI_Card _wpCard;
        private Label _wpTitle;
        private ComboBox _cmbStart;             // CampaignMaker, waypoint 0 : type de départ (liste déroulante dans la ligne "Start type" de la fiche)
        private Button _btnAddWp, _btnDelWp;        // CampaignMaker : ajouter / supprimer un waypoint du vol

        private Panel _flightHost;
        private Map_Flight _shownFlight;
        private Map_UI_GearButton _gearLeft, _gearSky, _gearRight;   // engrenages : bande de gauche / météo + fiche du vol / panneau de droite
        private Panel _panelRight, _wptBar;
        private Splitter _splitRight;
        private TabPage _tabBrief;
        private RichTextBox _briefBox;
        private Button _btnDatacard;
        private string _briefPart = "";
        private ComboBox _briefMode;       // 0 = ce vol, 1 = briefing du camp (Blue / Red task), 2 = description de la mission
        private CheckBox _chkGround, _chkGroundBlue, _chkGroundRed, _chkGroundNames;     // éléments au sol : tous / bleus / rouges / noms de groupes
        private CheckBox _chkBlueFlights, _chkRedFlights;     // CampaignMaker : afficher / masquer un camp
        private Panel _baseHost;
        private const int SquadListHeight = 230;
        private Label _lblInfo;
        private Label _lblStatus;
        private ListView _listSquads;
        private ListBox _listNoPos;
        private Dictionary<string, string> _aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private CheckBox _chkDrawings, _chkZones, _chkSpawn, _chkPlans, _chkThreats, _chkShips, _chkRadars, _chkMove;
        private List<Map_Drawing> _ewrCircles = new List<Map_Drawing>();      // zones radar des EWR au sol (celles des AWACS suivent les vols affichés)
        private ProgressBar _progressTiles;      // téléchargement des tuiles de la carte web
        private ComboBox _cmbStyle;
        private Map_UI_Sky _sky;    // panneau ciel / météo (carte de mission seulement)
        private Button _btnEdit, _btnAdd, _btnTrash, _btnMove, _btnSave, _btnQuit, _btnSaveWpt;
        private NumericUpDown _numAircraft;        // CampaignMaker : nombre d'avions du vol choisi (1 à 4)
        private Button _btnAudit;
        private Panel _toolsPanel;          // au-dessus des waypoints : nombre d'avions + audit (CampaignMaker, mode crayon)
        private ToolTip _toolsTip;
        private bool _syncNum;
        private Button _btnRemove;
        private Map_UI_FastTable _unitRows_tbl;       // sous-tableau des lignes d'avions : seul lui est refait quand on ajoute / retire un avion
        private List<UnitRow> _rowsShown;               // liste d'avions affichée dans le cadre
        private readonly List<Action> _csRefresh = new List<Action>();      // remet à jour la case "indicatif" de chaque avion quand la ligne "Callsign" du vol change
        private TableLayoutPanel _unitsPanel;       // un cadre avec une ligne par avion du vol choisi (nom, indicatif, numéro de queue)

        private class UnitRow
        {
            public int Src;                         // numéro de l'avion dans le fichier (1...), 0 = nouvel avion
            public string Name = "", Callsign = "", Onboard = "";
            public bool Player;
        }
        private readonly Dictionary<string, List<UnitRow>> _unitRows = new Dictionary<string, List<UnitRow>>();   // vols dont les avions sont changés, en attente du Save
        private readonly Dictionary<string, double> _pendingFreq = new Dictionary<string, double>();   // vol -> nouvelle fréquence "flight" (MHz), écrite au clic sur Save modification
        private readonly Random _rng = new Random();
        private readonly Dictionary<string, string> _pendingRemove = new Dictionary<string, string>();   // vol -> "delete#lot" ou "disable#lot", écrit au clic sur Save modification
        private int _removeBatch;

        // Zones wargame de chaque mode : la couleur d'une base suit le camp qui tient la zone où elle se trouve
        private List<WargameZoneData> _zonesInit = new List<WargameZoneData>();
        private List<WargameZoneData> _zonesActive = new List<WargameZoneData>();

        // justGenerated : la carte s'ouvre juste après une génération de mission -> on lit le .miz le plus récent
        public Map_UI_Form(string campaignName, bool missionMode = false, bool justGenerated = false)
        {
            _campaign = campaignName ?? "";
            _runwaysReady = () => { try { if (IsHandleCreated) BeginInvoke(new Action(ApplyMode)); } catch { } };      // pistes publiques téléchargées : on redessine
            Map_RunwayData.Ready += _runwaysReady;
            Map_Web.UseCampaign(_campaign);         // le style de tuiles est propre à chaque campagne
            _missionMode = missionMode;
            _justGenerated = justGenerated;

            BuildUi();

            _loading = true;
            bool startActive = StartInActive();
            _radioInit.Checked = !startActive;
            _radioActive.Checked = startActive;
            _loading = false;

            if (_missionMode)
                _btnSave.Visible = false;       // rien à enregistrer pour l'instant

            LoadFromDisk();
            TakeSnapshot(true);
            TakeSnapshot(false);
            ApplyMode();
            UpdateTitle();
            SyncWebRunwaysButton();

            // Fond web : cache s'il existe, sinon téléchargement et calage automatiques (une seule fois)
            Map_Background cached = Map_Web.TryLoadCached(_campaign);
            if (cached != null)
                UseBackground(cached);
            else
                Shown += async (s, e) => await BuildBackgroundAsync();
        }

        // On démarre en Active si ce fichier existe ET si l'utilisateur regardait Active dans les onglets OOB
        private bool StartInActive()
        {
            try
            {
                string activeOob = Path.Combine(ParamConf.PATH_SavedGames_DCS, @"Mods\tech\DCE\Missions\Campaigns",
                    _campaign, "Active", "oob_air.lua");

                bool wantsActive = Main_Form.Instance != null && Main_Form.Instance.CampaignView != null
                    && Main_Form.Instance.CampaignView.IsOobActive;

                // La carte de mission montre toujours l'état en cours ; sinon Active dès la mission 2
                if (_missionMode) wantsActive = true;

                // Campagne déjà commencée (mission 2 ou plus) : Active par défaut
                if (WargameLogistics.ReadMission(WargameZoneRepository.GetCampaignFolder(_campaign)) > 1)
                    wantsActive = true;

                return wantsActive && File.Exists(activeOob);
            }
            catch
            {
                return false;
            }
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
            KeyPreview = true;
            KeyDown += (s, e) =>
            {
                if (e.KeyCode != Keys.Escape) return;
                if (_moveSquad != null) CancelMove();
                else _view.ClearRuler();
            };

            var tip = new ToolTip { AutoPopDelay = 20000, InitialDelay = 250, ReshowDelay = 100, ShowAlways = true };

            // --- Carte (au milieu) ---
            _view = new Map_UI_View { Dock = DockStyle.Fill };
            _view.BaseSelected += OnBaseClicked;
            _view.RunwaysEdited += OnRunwaysEdited;
            _view.ParkSelected += OnParkSelected;
            _view.ParkChanged += OnParkChanged;
            _view.ParksEdited += OnParksEdited;
            _view.ParkAddClicked += OnParkAddClicked;
            _view.ParkDeleteRequested += ParkDeletePressed;
            _view.FlightLabel = FlightMenuLabel;
            _view.WaypointLabel = WaypointMenuLabel;
            _view.ShowSilhouettes = !_missionMode;      // carte de mission : pas de silhouette d'avion, elle se confondrait avec les plans de vol
            _view.FlightsSelected += OnFlightsClicked;
            _view.WaypointSelected += OnWaypointClicked;
            _view.WaypointMoving += OnWaypointMoving;
            _view.WaypointDropped += OnWaypointDropped;
            _view.WaypointInsertRequested += OnWaypointInsertRequested;
            _view.WaypointDeleteRequested += OnWaypointDeleteRequested;
            _view.GroundClicked += OnGroundClicked;

            // --- Panneau de droite : squadrons de la base choisie ---
            var right = new Panel { Dock = DockStyle.Right, Width = 430, Padding = new Padding(6) };
            _panelRight = right;

            _lblBase = new Label
            {
                Dock = DockStyle.Top,
                Height = 26,
                Font = new Font("Segoe UI", 12f, FontStyle.Bold),
                Text = "Click a base on the map"
            };
            _lblInfo = new Label { Dock = DockStyle.Top, Height = 38, Text = "" };

            // Boutons d'édition
            var buttons = new Panel { Dock = DockStyle.Top, Height = 36 };
            _btnEdit = MakeButton("Edit", 0, 80);
            _btnMove = MakeButton("Move...", 84, 90);
            _btnEdit.Click += (s, e) => EditSelected();
            _btnMove.Click += (s, e) => StartMove();
            tip.SetToolTip(_btnEdit, "Edit the selected squadron (Save in the edit window applies the changes; the file is written with the map Save button)");
            tip.SetToolTip(_btnMove, "Move the selected squadron: then click the destination base on the map (Esc = cancel)");
            buttons.Controls.Add(_btnEdit);
            buttons.Controls.Add(_btnMove);

            // Pistes (carte Map seulement) : tirer les bords à la main, ou saisir les valeurs exactes. Rien n'est écrit avant Save.
            if (!_missionMode)
            {
                _chkRunEdit = MakeToggle("\u25AC", 2);
                _chkRunEdit.Location = new Point(184, 2);
                _chkRunEdit.CheckedChanged += (s, e) => _view.RunwayEdit = _chkRunEdit.Checked;
                tip.SetToolTip(_chkRunEdit, "EDIT RUNWAYS\nClick a base, then drag the white round handles at the ends of its runway to change its length and heading (the other end stays in place). Drag the small square in the middle to move the whole runway. Zoom in if the runway is too small to grab. Nothing is written until you click Save.");

                _btnRunData = MakeToolButton("#", 2);
                _btnRunData.Location = new Point(220, 2);
                _btnRunData.Click += (s, e) => EditRunwaysData();
                tip.SetToolTip(_btnRunData, "RUNWAY DATA\nType the exact heading, length and position of the runways of the selected base, or set the rule for the whole map (where the x, y point of db_airbases is on the runway: middle, start, east end, west end). Nothing is written until you click Save.");

                buttons.Controls.Add(_chkRunEdit);
                buttons.Controls.Add(_btnRunData);
            }

            // Places d'hélicoptère (parkAlertSAR) : carte Map pour tous ; carte de mission pour le CampaignMaker (elle change aussi les hélicos du .miz)
            if (!_missionMode || ParamConf.UserLevel == UserLevel.CampaignMaker)
            {
                // Barre cachée tant que le bouton de la bande de gauche n'est pas enfoncé
                _parkBar = new Panel { Dock = DockStyle.Top, Height = 100, Visible = false };
                _btnParkAdd = new Button { Text = "+ Add spot", Location = new Point(0, 4), Size = new Size(96, 28) };
                _btnParkDel = new Button { Text = "Delete", Location = new Point(100, 4), Size = new Size(76, 28), Enabled = false };
                _btnParkKeep = new Button { Text = "Keep north", Location = new Point(180, 4), Size = new Size(92, 28), Enabled = false };
                var lblHdg = new Label { Text = "Heading", Location = new Point(0, 42), AutoSize = true };
                _numParkHdg = new NumericUpDown { Location = new Point(58, 38), Size = new Size(70, 24), Minimum = 0, Maximum = 359.9M, DecimalPlaces = 1, Increment = 1, Enabled = false };
                var lblDeg = new Label { Text = "deg", Location = new Point(132, 42), AutoSize = true };
                _lblParkRad = new Label { Location = new Point(170, 42), Size = new Size(120, 18), ForeColor = Color.DimGray };
                _lblParkInfo = new Label { Location = new Point(0, 66), Size = new Size(290, 32), ForeColor = Color.DimGray };
                _btnParkAdd.Click += (s, e) => ParkAddPressed();
                _btnParkDel.Click += (s, e) => ParkDeletePressed();
                _btnParkKeep.Click += (s, e) => ParkKeepPressed();
                _numParkHdg.ValueChanged += (s, e) => ParkHeadingChanged();
                tip.SetToolTip(_btnParkAdd, "ADD A SPOT\nClick a base first, press this, then click on the map where the new helicopter spot goes (Esc = cancel). New spots face north.");
                tip.SetToolTip(_btnParkDel, "DELETE THE SPOT\nRemoves the selected spot (Delete key does the same). Written when you click Save.");
                tip.SetToolTip(_btnParkKeep, "KEEP NORTH\nThis spot has no heading in the file, so it is shown facing north. Press to accept north as its heading (or turn it: drag the round handle, or type a value).");
                tip.SetToolTip(_numParkHdg, "HEADING\nIn degrees here (0 = north, clockwise). The file keeps it in radians, like the DCS mission file (heading).");
                _parkBar.Controls.AddRange(new Control[] { _btnParkAdd, _btnParkDel, _btnParkKeep, lblHdg, _numParkHdg, lblDeg, _lblParkRad, _lblParkInfo });
            }

            // "+" et poubelle : posés sur la première ligne vide de la liste (voir PlaceListButtons)
            _btnAdd = new Button { Text = "+", Width = 30, FlatStyle = FlatStyle.System, Font = new Font("Segoe UI", 10f, FontStyle.Bold), Visible = false };
            _btnTrash = new Button { Text = "\uD83D\uDDD1", Width = 30, FlatStyle = FlatStyle.System, Font = new Font("Segoe UI Emoji", 9f), Visible = false };
            _btnAdd.Click += (s, e) => AddSquad();
            _btnTrash.Click += (s, e) => DeleteSelected();
            tip.SetToolTip(_btnAdd, "Add a squadron on this base (a copy of the selected one, or of a squadron of the same side), then you edit it. Written in the file only when you click Save.");
            tip.SetToolTip(_btnTrash, "Remove the selected squadron from the list (written in the file only when you click Save)");

            _listSquads = new ListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                FullRowSelect = true,
                GridLines = true,
                HideSelection = false,
                MultiSelect = false
            };
            _listSquads.Columns.Add("Squadron", 135);
            _listSquads.Columns.Add("Aircraft", 85);
            _listSquads.Columns.Add("Ready", 45, HorizontalAlignment.Right);
            _listSquads.Columns.Add("Reserve", 55, HorizontalAlignment.Right);
            _listSquads.Columns.Add("Info", 60);
            _listSquads.SelectedIndexChanged += (s, e) => UpdateButtons();
            _listSquads.DoubleClick += (s, e) => EditSelected();
            _listSquads.Controls.Add(_btnAdd);
            _listSquads.Controls.Add(_btnTrash);
            _listSquads.Resize += (s, e) => PlaceListButtons();
            _listSquads.MouseWheel += (s, e) => PlaceListButtons();
            _listSquads.KeyUp += (s, e) => PlaceListButtons();

            var noPosPanel = new Panel { Dock = DockStyle.Bottom, Height = 170 };
            var noPosLabel = new Label
            {
                Dock = DockStyle.Top,
                Height = 34,
                Text = "Squadrons with no position on the map (ships, reserves, unknown bases). Click one to see its squadrons."
            };
            _listNoPos = new ListBox { Dock = DockStyle.Fill, IntegralHeight = false };
            _listNoPos.SelectedIndexChanged += (s, e) =>
            {
                int i = _listNoPos.SelectedIndex;
                if (i < 0 || i >= _noPosNames.Count) return;

                if (_moveSquad != null)
                {
                    DoMove(_noPosNames[i]);
                    return;
                }
                _view.SelectedName = "";
                ShowBase(_noPosNames[i]);
            };
            noPosPanel.Controls.Add(_listNoPos);
            noPosPanel.Controls.Add(noPosLabel);

            // Ordre d'ajout : Fill d'abord, puis Bottom, puis les Top (le dernier ajouté est le plus haut)
            if (_missionMode)
            {
                BuildMissionRight(right, noPosPanel);
            }
            else
            {
                right.Controls.Add(_listSquads);
                right.Controls.Add(noPosPanel);
                right.Controls.Add(_parkBar);
                right.Controls.Add(buttons);
                right.Controls.Add(_lblInfo);
                right.Controls.Add(_lblBase);
            }

            // Carte de mission : heure, soleil, lune, nuages, vent (tout en haut du panneau de droite)
            if (_missionMode)
            {
                // Posé sur la carte, en haut à gauche (un bouton de la barre d'outils le cache) ; la hauteur s'adapte (lune = vol de nuit)
                _sky = new Map_UI_Sky { Dock = DockStyle.None, Location = new Point(10, 54), Size = new Size(410, 290) };
                _view.Controls.Add(_sky);
                _sky.BringToFront();
                _sky.SizeChanged += (s, e) => PlaceOverlays();
                _view.Resize += (s, e) => PlaceOverlays();

                _secSky = new Map_UI_Section { Title = "Weather and sun", Size = new Size(410, 24), Expanded = false };
                _secSky.Toggled += (s, e) => PlaceOverlays();
                _view.Controls.Add(_secSky);
                _secSky.BringToFront();

                _secRadio = new Map_UI_Section { Title = "Radio presets", Size = new Size(410, 24), Visible = false, Expanded = false };
                _secRadio.Toggled += (s, e) => { PlaceOverlays(); if (_secRadio.Expanded) RebuildRadioPanel(_shownFlight); };
                _view.Controls.Add(_secRadio);
                _secRadio.BringToFront();
                _radioPanel = new Map_UI_FastPanel { Size = new Size(410, 100), Visible = false, BackColor = Color.FromArgb(250, 250, 250), BorderStyle = BorderStyle.FixedSingle };
                _radioList = new Map_UI_FastPanel { Dock = DockStyle.Fill, AutoScroll = true };
                _radioInfo = new Label { Dock = DockStyle.Bottom, Height = 34, ForeColor = Color.DimGray, Font = FRadioSmall, Padding = new Padding(6, 2, 6, 2) };
                _radioTabs = new Panel { Dock = DockStyle.Top, Height = 34 };
                _radioPanel.Controls.Add(_radioList);       // Fill d'abord, puis Bottom, puis Top : le dernier ajouté prend sa place en premier
                _radioPanel.Controls.Add(_radioInfo);
                _radioPanel.Controls.Add(_radioTabs);
                _view.Controls.Add(_radioPanel);
                _radioPanel.BringToFront();
            }

            // Chaque bouton de commande : un titre court (en majuscules) puis l'explication, affichés quand la souris reste dessus
            Action<Control, string, string> hint = (c, title, text) => tip.SetToolTip(c, title.ToUpper() + "\n" + text);

            // ================= Bande de gauche : outils généraux + wargame (zones, éléments au sol) =================
            var strip = new Panel { Dock = DockStyle.Left, Width = 38, BackColor = Color.FromArgb(243, 243, 243) };
            int sy = _missionMode ? 46 : 6;          // carte de mission : l'engrenage est tout en haut de la bande
            Func<int> nextY = () => { int r = sy; sy += 38; return r; };
            Action gapY = () => sy += 16;

            if (_missionMode)
            {
                _gearLeft = new Map_UI_GearButton(Color.FromArgb(30, 110, 230), "\u27F7") { Location = new Point(2, 5) };
                _gearLeft.CheckedChanged += (s, e) => { foreach (Control c in strip.Controls) if (c != _gearLeft) c.Visible = _gearLeft.Checked; };
                hint(_gearLeft, "Tools column", "Show or hide this column of tools (ruler, zoom, drawings, wargame, ground elements).");
                strip.Controls.Add(_gearLeft);

                // Météo + fiche du vol : posées sur la carte, en haut à gauche
                _gearSky = new Map_UI_GearButton(Color.FromArgb(40, 165, 70), "\u2600") { Location = new Point(10, 10) };
                _gearSky.CheckedChanged += (s, e) => PlaceOverlays();
                hint(_gearSky, "Left column", "Show or hide the sections on the left of the map (weather and sun, later the radio presets). Click the bar of a section to fold it into one line.");
                _view.Controls.Add(_gearSky);
                _gearSky.BringToFront();
            }

            _chkRuler = MakeToggle("⟷", nextY());
            _chkRuler.CheckedChanged += (s, e) => _view.RulerEnabled = _chkRuler.Checked;
            hint(_chkRuler, "Ruler", "Distance in km or Nm (see the units button at the top), like the DCS F10 map: click point A, move the mouse (live distance), click point B. Right-click or Esc = clear. Untick to go back to normal clicks.");

            var btnFit = MakeToolButton("⛶", nextY());
            btnFit.Click += (s, e) => _view.ResetZoom();
            hint(btnFit, "Reset zoom", "Show the whole map again.");

            var btnReload = MakeToolButton("⟳", nextY());
            btnReload.Click += (s, e) => { LoadFromDisk(); ApplyMode(); };
            hint(btnReload, "Reload airbases", "Read the airbases again from the files.");

            gapY();

            _chkDrawings = MakeToggle("⚑", nextY());
            _chkDrawings.CheckedChanged += (s, e) => _view.SetDrawingsVisible("mission", _chkDrawings.Checked);
            hint(_chkDrawings, "Mission drawings", "Show or hide the borders and zones drawn in base_mission.miz (Init folder, mission editor drawings).");

            _chkZones = MakeToggle("⚔", nextY());
            _chkZones.CheckedChanged += (s, e) => _view.SetDrawingsVisible("zones", _chkZones.Checked);
            hint(_chkZones, "Wargame zones", "Show or hide the wargame zones (Init\\Wargame\\DCE_Generated\\wargame_zones_init.lua). Blue / red = side holding the zone, orange = contested, cyan dashed = landing plan.");

            _chkSpawn = MakeToggle("≋", nextY());
            _chkSpawn.CheckedChanged += (s, e) => _view.SetDrawingsVisible("spawn", _chkSpawn.Checked);
            hint(_chkSpawn, "Wargame spawn areas", "Show or hide the wargame spawn areas (Init\\Wargame\\wargame_spawn.miz): yellow lines = roads, green = forest, brown = mountain, grey = town / base.");

            if (!_missionMode || ParamConf.UserLevel == UserLevel.CampaignMaker)
            {
                _chkWebRunways = MakeToggle("\U0001F310", nextY());
                _chkWebRunways.CheckedChanged += (s, e) => { if (!_syncWeb) SetWebRunways(_chkWebRunways.Checked); };
                hint(_chkWebRunways, "Runways from the web", "Ignore the local runway data (db_airbases and your own settings) and draw the real runways of the web data (OurAirports) instead. Untick to go back to the local data. Nothing is written until you click Save: then your own runway settings are removed and the web data is kept for this map (db_airbases itself is never changed). A base with no web runway nearby keeps its local runways.");
                strip.Controls.Add(_chkWebRunways);
            }

            if (_parkBar != null)
            {
                _chkPark = MakeToggle("\U0001F681", nextY());
                _chkPark.CheckedChanged += (s, e) => ParkToggled();
                hint(_chkPark, "Helicopter spots", "Show the helicopter alert / SAR spots (parkAlertSAR of db_airbases.lua): a CH-46 silhouette in a 16 m circle. Click a spot to select it, drag it to move it, drag its round handle to turn it. Use the bar to add or delete a spot. Heading is kept in radians (like DCS)."
                    + (_missionMode
                        ? "\nMission map: the first mission edits Init\\db_airbases.lua, the ongoing mission edits Active\\db_airbases.lua. The helicopters of the .miz standing on a moved spot move with it (and turn with it). Written when you click Save modification."
                        : "\nMap: edits the db_airbases.lua of the list you are viewing (Init or Active). Nothing is written until you click Save."));
                strip.Controls.Add(_chkPark);
            }

            strip.Controls.Add(_chkRuler);
            strip.Controls.Add(btnFit);
            strip.Controls.Add(btnReload);
            strip.Controls.Add(_chkDrawings);
            strip.Controls.Add(_chkZones);
            strip.Controls.Add(_chkSpawn);

            if (_missionMode)
            {
                // Éléments au sol : rien d'affiché au départ, on coche pour les voir
                gapY();
                bool cm = ParamConf.UserLevel == UserLevel.CampaignMaker;
                _chkGround = MakeToggle("\u25A3", nextY());
                hint(_chkGround, "Ground elements", "Show all the ground elements that are not hidden in the mission. Nothing is shown until you tick this." + (cm ? " It ticks / unticks the blue and red buttons together." : " Only your side is shown.")
                    + "\nIcons: square = vehicle, wide rectangle = armor, triangle up = air defense, ring = radar, circle + barrel = artillery, cross = infantry, diamond = static, triangle down = static aircraft, hollow square = FARP, hull = ship, big rectangle = carrier."
                    + "\nClick an element to see its name and all its fields (copyable).");
                strip.Controls.Add(_chkGround);
                if (cm)
                {
                    _chkGroundBlue = MakeToggle("\u25A3", nextY());
                    _chkGroundBlue.ForeColor = Color.FromArgb(30, 90, 200);
                    hint(_chkGroundBlue, "Blue ground elements", "CampaignMaker: show only the blue ground elements.");
                    _chkGroundRed = MakeToggle("\u25A3", nextY());
                    _chkGroundRed.ForeColor = Color.FromArgb(200, 40, 40);
                    hint(_chkGroundRed, "Red ground elements", "CampaignMaker: show only the red ground elements.");
                    strip.Controls.Add(_chkGroundBlue);
                    strip.Controls.Add(_chkGroundRed);
                }
                _chkGroundNames = MakeToggle("Aa", nextY());
                _chkGroundNames.Font = new Font("Segoe UI", 11f, FontStyle.Bold);
                hint(_chkGroundNames, "Ground names", "Show or hide the group names of the ground elements (groups only, not each unit). Names appear when the map is zoomed in enough.");
                strip.Controls.Add(_chkGroundNames);

                EventHandler groundChanged = (s, e) => GroundBoxChanged(s);
                _chkGround.CheckedChanged += groundChanged;
                _chkGroundNames.CheckedChanged += groundChanged;
                if (_chkGroundBlue != null) { _chkGroundBlue.CheckedChanged += groundChanged; _chkGroundRed.CheckedChanged += groundChanged; }
            }

            // ================= Partie droite (carte de mission) : plans de vol et déplacement des waypoints =================
            if (_missionMode)
            {
                var wptBar = new Panel { Dock = DockStyle.Top, Height = 42, BackColor = Color.FromArgb(243, 243, 243) };
                _wptBar = wptBar;
                int bx = 4;
                Action<Control> put = c => { c.Location = new Point(bx, 5); bx += 36; wptBar.Controls.Add(c); };

                // Engrenage du panneau de droite : dans la barre ; quand le panneau est caché, il reste sur la carte (en haut à droite)
                _gearRight = new Map_UI_GearButton(Color.FromArgb(235, 175, 0), "\u2708");
                _gearRight.CheckedChanged += (s, e) =>
                {
                    if (_gearRight.Checked)
                    {
                        _view.Controls.Remove(_gearRight);
                        _gearRight.Anchor = AnchorStyles.Top | AnchorStyles.Left;
                        _gearRight.Location = new Point(4, 4);
                        wptBar.Controls.Add(_gearRight);
                    }
                    else
                    {
                        wptBar.Controls.Remove(_gearRight);
                        _gearRight.Anchor = AnchorStyles.Top | AnchorStyles.Right;
                        _gearRight.Location = new Point(_view.ClientSize.Width - 44, 10);
                        _view.Controls.Add(_gearRight);
                        _gearRight.BringToFront();
                    }
                    _panelRight.Visible = _gearRight.Checked;
                    if (_splitRight != null) _splitRight.Visible = _gearRight.Checked;
                };
                hint(_gearRight, "Flights column", "Show or hide this column (flights, waypoints, briefing, bases). The gear stays at the top right of the map.");
                wptBar.Controls.Add(_gearRight);
                _gearRight.Location = new Point(4, 4);
                bx += 40;

                _chkPlans = MakeToggle("✈", 0);
                _chkPlans.CheckedChanged += (s, e) => _view.SetDrawingsVisible("plans", _chkPlans.Checked);
                hint(_chkPlans, "Flight plans", "Show or hide the flight plans of all the aircraft (thick = players, medium = their package, thin = others). Markers: circle = standard, square = IP / station, star = target, diamond = split / egress / join / attack, triangle = airfield. Click a flight to highlight it (its waypoint numbers appear); click again at the same place to pick the next one.");

                _chkThreats = MakeToggle("◎", 0);
                _chkThreats.CheckedChanged += (s, e) => _view.SetDrawingsVisible("threats", _chkThreats.Checked);
                hint(_chkThreats, "Threat ranges", "Show or hide the range of the SAM sites that are not hidden in the mission (blue / red = side). Ranges are approximate maximums.");

                _chkShips = MakeToggle("\u2693", 0);
                _chkShips.CheckedChanged += (s, e) => _view.SetDrawingsVisible("ships", _chkShips.Checked);
                hint(_chkShips, "Ship routes", "Show or hide the planned routes of the ships (dashed lines with an arrow for the direction; light blue = blue ships, light red = red ships). Hidden at the start: tick to see them.");

                _chkRadars = MakeToggle("\U0001F4E1", 0);
                _chkRadars.CheckedChanged += (s, e) => _view.SetDrawingsVisible("radars", _chkRadars.Checked && _chkRadars.Enabled);
                hint(_chkRadars, "Radar coverage", "Show or hide the radar coverage: ground EWR (circle) and AWACS (oval that follows its orbit; E-3C, EC-121 and E-2D, each with its own range). Light blue = blue side, light red = red side. Ranges are approximate maximums. Hidden at the start: tick to see them.");

                _chkMove = MakeToggle("\u270E", 0);
                _chkMove.CheckedChanged += (s, e) =>
                {
                    _view.AllowMove = _chkMove.Checked;
                    if (_wpCard != null) { _wpCard.LockEnabled = _chkMove.Checked; _wpCard.Invalidate(); }
                    if (_cmbStart != null) _cmbStart.Enabled = _chkMove.Checked;       // le cadenas ne change qu'en mode crayon
                    UpdateFlightTools();                                                                        // nombre d'avions et audit aussi
                    if (_secRadio != null && _secRadio.Expanded) RebuildRadioPanel(_shownFlight);               // les presets radio sont modifiables seulement en mode crayon
                };
                hint(_chkMove, "Edit waypoints (pencil mode)", "Select a flight, then drag one of its waypoints. In this mode you can also change the padlock of a waypoint.\nThe time of the padlocked waypoint (by default the first IP, else the target) is kept: the takeoff time moves (not earlier than 5 min after the mission start), then the speeds adapt. Orange leg = speed beyond the plan speeds, yellow / black stripes = impossible (more than 25 % beyond). After that waypoint the speeds stay as planned and the landing time moves.\nNothing is written in the mission until you press Save modification.");

                var btnResetPlan = MakeToolButton("\u21B6", 0);
                btnResetPlan.Click += (s, e) => ResetMovedWaypoints();
                hint(btnResetPlan, "Undo all moves", "Put all the moved waypoints back where they were.");

                put(_chkPlans);
                put(_chkThreats);
                put(_chkShips);
                put(_chkRadars);
                bx += 14;
                put(_chkMove);
                put(btnResetPlan);

                right.Controls.Add(wptBar);         // Top, ajouté en dernier : tout en haut du panneau
            }

            // ================= Barre du haut : mode, style de carte et outils des tuiles =================
            var top = new Panel { Dock = DockStyle.Top, Height = 38 };
            var lbl = new Label { Text = "Viewing:", Location = new Point(10, 11), AutoSize = true };
            _radioInit = new RadioButton { Text = "Init", Location = new Point(75, 9), AutoSize = true };
            _radioActive = new RadioButton { Text = "Active", Location = new Point(140, 9), AutoSize = true };
            _radioInit.CheckedChanged += (s, e) => { if (_radioInit.Checked && !_loading) ModeChanged(); };
            _radioActive.CheckedChanged += (s, e) => { if (_radioActive.Checked && !_loading) ModeChanged(); };
            _chkHideEmpty = new CheckBox { Text = "Hide bases with no squadron", Location = new Point(230, 9), AutoSize = true };
            _chkHideEmpty.CheckedChanged += (s, e) => { if (!_loading) ApplyMode(); };
            top.Controls.Add(lbl);
            top.Controls.Add(_radioInit);
            top.Controls.Add(_radioActive);
            top.Controls.Add(_chkHideEmpty);

            // Carte de mission : toujours les paramètres de la mission en cours (Active) -> pas de choix Init / Active
            if (_missionMode)
            {
                lbl.Visible = false;
                _radioInit.Visible = false;
                _radioActive.Visible = false;
                _chkHideEmpty.Location = new Point(10, 9);

            }

            var lblStyle = new Label { Text = "Map style:", Location = new Point(600, 11), AutoSize = true };
            _cmbStyle = new ComboBox { Location = new Point(670, 7), Width = 290, DropDownStyle = ComboBoxStyle.DropDownList };
            foreach (Map_Style st in Map_Web.Styles) _cmbStyle.Items.Add(st.Label);
            _cmbStyle.SelectedIndex = Math.Max(0, Map_Web.Styles.FindIndex(x => x.Id == Map_Web.CurrentStyle.Id));
            _cmbStyle.SelectionChangeCommitted += (s, e) => StyleChanged();
            hint(_cmbStyle, "Map style", "Look of the web map. The style is remembered for each campaign. Tiles are downloaded once per style and shared by all campaigns. Satellite and shaded relief have no names at all.");
            top.Controls.Add(lblStyle);
            top.Controls.Add(_cmbStyle);

            // Outils des tuiles, à côté du style
            _chkBackground = MakeToggle("▤", 0);
            _chkBackground.Location = new Point(968, 3);
            _chkBackground.CheckedChanged += ChkBackground_CheckedChanged;
            hint(_chkBackground, "Web map", "Show or hide the web map. It is downloaded and placed automatically the first time, using the real airports (ICAO codes).");

            var btnRebuild = MakeToolButton("↺", 0);
            btnRebuild.Location = new Point(1006, 3);
            btnRebuild.Click += (s, e) => RebuildBackground();
            hint(btnRebuild, "Rebuild web map", "Download and calibrate the web map again.");

            var btnArea = MakeToolButton("\u2316", 0);
            btnArea.Location = new Point(1044, 3);
            btnArea.Click += (s, e) => _view.LoadVisibleArea();
            hint(btnArea, "Load visible area", "Download the map tiles of what you see now, even far from the conflict zone (coarse tiles if it is too big: more than 100 tiles). Then zoom in on this area and the detailed tiles come by themselves. If you look somewhere else, nothing is loaded: click this button again.");

            var btnDetail = MakeToolButton("\u229E", 0);
            btnDetail.Location = new Point(1082, 3);
            btnDetail.Click += (s, e) => _view.ForceDetailHere();
            hint(btnDetail, "Detail now", "Load the detailed tiles of the zoom level you are at, right now. By default the map skips some zoom levels while you zoom (smoother): the finest level of the style and the base map are always shown, the levels in between use the level just below. Click here when the map looks blurry at this zoom. It stays until you change the zoom level; you can move the map meanwhile.");

            top.Controls.Add(_chkBackground);
            top.Controls.Add(btnRebuild);
            top.Controls.Add(btnArea);
            top.Controls.Add(btnDetail);

            // Unités de toute la carte : Metric (m, km, km/h, m/s, hPa) <-> Imperial (ft, Nm, kt, inHg). Effet immédiat.
            _btnUnits = new Button { Size = new Size(84, 26), Location = new Point(1128, 5), FlatStyle = FlatStyle.Flat, TabStop = false, Font = new Font("Segoe UI", 9f, FontStyle.Bold) };
            _btnUnits.FlatAppearance.BorderColor = Color.Silver;
            _btnUnits.Click += (s, e) => Map_Units.Set(!Map_Units.Metric);
            UpdateUnitsButton();
            hint(_btnUnits, "Units", "Click to switch ALL the units of the map windows.\nMetric: metres, km, km/h, wind in m/s, pressure in hPa.\nImperial: feet, Nm, knots, pressure in inHg.\nTemperature stays in degrees C. The choice is remembered.");
            top.Controls.Add(_btnUnits);
            top.Resize += (s, e) => _btnUnits.Left = Math.Max(1128, top.ClientSize.Width - _btnUnits.Width - 10);
            Map_Units.Changed += UnitsChanged;

            Controls.Add(_view);
            _splitRight = new Splitter { Dock = DockStyle.Right, Width = 6, MinSize = 250, MinExtra = 300 };   // sépare la carte et le panneau de droite (redimensionnable)
            Controls.Add(_splitRight);
            Controls.Add(right);
            Controls.Add(strip);
            Controls.Add(top);

            // --- Barre de progression du téléchargement des tuiles (cachée sauf pendant le téléchargement) ---
            _progressTiles = new ProgressBar { Dock = DockStyle.Bottom, Height = 14, Visible = false, Minimum = 0, Maximum = 100 };
            Controls.Add(_progressTiles);

            // --- Ligne d'état en bas ---
            _lblStatus = new Label { Dock = DockStyle.Fill, Padding = new Padding(8, 4, 8, 0), Text = "" };

            _btnSave = new Button { Text = "Save", Location = new Point(4, 6), Size = new Size(170, 32) };
            _btnSave.Click += (s, e) => SaveCurrentMode();
            hint(_btnSave, "Save", "Write in the campaign files: the squadrons of the viewed list (Init or Active) and the runway changes made on this map.");
            _btnQuit = new Button { Text = "Quit", Location = new Point(180, 6), Size = new Size(80, 32) };
            _btnQuit.Click += (s, e) => Close();      // OnFormClosing pose la question s'il reste des changements non sauvés
            hint(_btnQuit, "Quit", "Close the map. If squadron changes are not saved, you will be asked what to do.");

            var bottomButtons = new Panel { Dock = DockStyle.Right, Width = 270 };
            bottomButtons.Controls.Add(_btnSave);
            bottomButtons.Controls.Add(_btnQuit);

            // Carte de mission : "Save modification" écrit les changements faits sur la carte dans le fichier mission
            if (_missionMode)
            {
                _btnSaveWpt = new Button { Text = "Save modification", Location = new Point(180, 6), Size = new Size(170, 32), Enabled = false };
                _btnSaveWpt.Click += (s, e) => SaveMovedWaypoints();
                hint(_btnSaveWpt, "Save modification", "Write the changes made on the map in the mission file (.miz): moved waypoints and the number of aircraft of flights. The original is copied next to it the first time (<name>.wpt_backup). CampaignMaker only; enabled once something was changed.");
                _btnQuit.Location = new Point(356, 6);
                bottomButtons.Width = 446;
                bottomButtons.Controls.Add(_btnSaveWpt);
            }

            var bottomBar = new Panel { Dock = DockStyle.Bottom, Height = 44 };
            bottomBar.Controls.Add(_lblStatus);       // Fill d'abord, puis Right
            bottomBar.Controls.Add(bottomButtons);
            Controls.Add(bottomBar);
        }

        private static Button MakeButton(string text, int x, int width)
        {
            return new Button { Text = text, Location = new Point(x, 3), Size = new Size(width, 28) };
        }

        // Les symboles de la police se posent un peu bas dans le bouton : on les dessine nous-mêmes, remontés de IconNudge pixels
        private const int IconNudge = 2;

        private static void DrawGlyph(Control c, PaintEventArgs e, string symbol)
        {
            var rect = new Rectangle(0, -IconNudge, c.Width, c.Height);
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

        private void UpdateTitle()
        {
            string flag = "";
            var parts = new List<string>();
            if (_dirtyInit) parts.Add("Init");
            if (_dirtyActive) parts.Add("Active");
            if (Map_RunwayData.HasPending) parts.Add("Runways");
            if (ParkAnyDirty) parts.Add("Park spots");
            if (parts.Count > 0)
                flag = "   *unsaved (" + string.Join(" + ", parts.ToArray()) + ")";

            string file = "";
            if (_missionMode && _missionFile != "")
                file = "   -   " + Path.GetFileName(_missionFile) + " (" + Path.GetFileName(Path.GetDirectoryName(_missionFile)) + ")";

            Text = (_missionMode ? "Mission map - " : "Map - ") + _campaign + file + (_readOnly ? "   (read only)" : "") + flag;
            _btnSave.Text = "Save";
            _btnSave.Enabled = !_readOnly || (!_missionMode && (Map_RunwayData.HasPending || ParkAnyDirty));
        }

        private void UpdateButtons()
        {
            bool selected = !_readOnly && _moveSquad == null && SelectedSquad() != null;
            bool baseOk = !_readOnly && _moveSquad == null && _selectedBase != "" && _selectedBase != "(no base)";
            _btnEdit.Enabled = selected;
            _btnTrash.Enabled = selected;
            _btnMove.Enabled = selected;
            _btnAdd.Enabled = baseOk;      // marche même sans squadron sélectionné, ou sur une base vide
            PlaceListButtons();
        }

        // Pose "+" et poubelle sur la ligne vide qui suit le dernier squadron
        private void PlaceListButtons()
        {
            if (_listSquads == null || _btnAdd == null) return;

            int n = _listSquads.Items.Count;
            bool show = _selectedBase != "" && n > 0 && !_readOnly;
            if (!show)
            {
                _btnAdd.Visible = false;
                _btnTrash.Visible = false;
                return;
            }

            Rectangle r = _listSquads.Items[n - 1].GetBounds(ItemBoundsPortion.Entire);     // la ligne vide (dernière)
            bool visible = r.Bottom <= _listSquads.ClientSize.Height + 1 && r.Top >= 0;
            int h = Math.Max(r.Height, 18);

            _btnAdd.SetBounds(r.Left + 2, r.Top, 30, h);
            _btnTrash.SetBounds(r.Left + 34, r.Top, 30, h);
            _btnAdd.Visible = visible;
            _btnTrash.Visible = visible;
        }

        private void ModeChanged()
        {
            CancelMove();
            _parkCur = null;                    // la place choisie appartient à l'autre dossier
            _view.SelectedPark = null;
            EnsureParks();
            UpdateParkBar();
            ApplyMode();
            UpdateTitle();

            // Les zones wargame prennent la couleur du camp de ce mode (Init = départ, Active = situation actuelle)
            ReloadZones();
        }

        // ------------------------------------------------------------------
        //  Fond de carte web
        // ------------------------------------------------------------------

        private void SetBackgroundChecked(bool value)
        {
            _loading = true;
            _chkBackground.Checked = value;
            _loading = false;
        }

        private void ShowDefaultStatus()
        {
            string report = _background != null ? _background.Report : "";
            _lblStatus.Text = _missionMode && _missionInfo != "" ? _missionInfo + (report != "" ? "   |   " + report : "") : report;
        }

        private void UseBackground(Map_Background bg)
        {
            if (_background != null)
                _background.Dispose();

            _background = bg;
            _view.SetBackground(bg);
            Map_RunwayData.SetMeasured(Map_RunwayData.TheatreOf(_campaign), bg != null ? bg.AnchorRule : "");        // règle de position mesurée sur cette carte
            if (_airbases != null && _airbases.Count > 0) ApplyMode();      // pistes replacées (règle mesurée, projection de la carte)
            _view.ShowBackground = true;
            ShowDefaultStatus();
            SetBackgroundChecked(true);
        }

        // Toutes les bases posées sur la carte servent à caler la carte : celles qui ont un code ICAO (cherchées dans la liste des aéroports)
        // et les autres (rattachées à la piste réelle la plus proche, si elle est à moins de 1 km)
        private List<Map_Match> BuildMatchInputs()
        {
            var list = new List<Map_Match>();
            foreach (KeyValuePair<string, AirbaseInfo> kv in _airbases)
            {
                AirbaseInfo a = kv.Value;
                if (a.BaseAirStart) continue;       // base "airstart" loin du théâtre : ne sert pas au calage
                if (kv.Key.IndexOf("FARP", StringComparison.OrdinalIgnoreCase) >= 0) continue;     // un FARP n'existe pas dans la réalité

                bool placed = !(a.X == 0 && a.Y == 0) && Math.Abs(a.X) < 1e7 && Math.Abs(a.Y) < 1e7;
                if (!placed) continue;

                string icao = null;
                if (a.Code == null || !a.Code.TryGetValue("ICAO", out icao) || icao == null) icao = "";

                list.Add(new Map_Match
                {
                    Name = kv.Key, Icao = icao.Trim(), X = a.X, Y = a.Y,
                    DbRunways = a.Runways == null ? null : a.Runways.Select(rw => new Map_Runway { Hdg = rw.Hdg, Length = rw.Length, X = rw.X, Y = rw.Y }).ToList()
                });
            }
            return list;
        }

        // Téléchargement + calage, sans rien demander : on le fait une seule fois, tout reste en cache ensuite
        private async Task BuildBackgroundAsync()
        {
            if (_busy || IsDisposed)
                return;

            // Interrupteur "Web data" de la fenêtre principale : OFF = aucun téléchargement de tuiles
            if (!Main_WebAccess.Enabled)
            {
                _lblStatus.Text = "Web map: web access is OFF (switch \"Web data\" in the main window), nothing was downloaded.";
                SetBackgroundChecked(false);
                return;
            }

            List<Map_Match> inputs = BuildMatchInputs();
            // Toutes les bases posées sur la carte : elles servent à choisir la zone à télécharger
            var allPoints = new List<double[]>();
            foreach (AirbaseInfo a in _airbases.Values)
            {
                if (a.BaseAirStart) continue;       // base "airstart" loin de la zone de conflit : pas de tuiles pour elle
                bool placed = !(a.X == 0 && a.Y == 0) && Math.Abs(a.X) < 1e7 && Math.Abs(a.Y) < 1e7;
                if (placed) allPoints.Add(new double[] { a.X, a.Y });
            }
            if (allPoints.Count == 0)
            {
                _lblStatus.Text = "Web map: no airbase with a position on the map.";
                SetBackgroundChecked(false);
                return;
            }

            _busy = true;
            _chkBackground.Enabled = false;
            _progressTiles.Style = ProgressBarStyle.Marquee;      // on ne sait pas encore combien de tuiles : barre qui défile
            _progressTiles.Visible = true;
            try
            {
                var progress = new Progress<string>(msg => { if (!IsDisposed) _lblStatus.Text = msg; });
                var tilesProgress = new Progress<int[]>(p =>
                {
                    if (IsDisposed) return;
                    if (_progressTiles.Style != ProgressBarStyle.Continuous) _progressTiles.Style = ProgressBarStyle.Continuous;
                    _progressTiles.Maximum = Math.Max(1, p[1]);
                    _progressTiles.Value = Math.Max(0, Math.Min(p[0], _progressTiles.Maximum));
                });
                Map_Background bg = await Task.Run(() => Map_Web.BuildBackground(_campaign, inputs, allPoints, progress, tilesProgress));

                if (IsDisposed)
                {
                    bg.Dispose();
                    return;
                }
                UseBackground(bg);
            }
            catch (Exception ex)
            {
                FormUtils.LogRegister("Map_UI_Form | web map : " + ex.Message);
                if (!IsDisposed)
                {
                    _lblStatus.Text = "Web map not available: " + ex.Message;
                    SetBackgroundChecked(false);
                }
            }
            finally
            {
                _busy = false;
                if (!IsDisposed)
                {
                    _chkBackground.Enabled = true;
                    _progressTiles.Visible = false;
                }
            }
        }

        // Nouveau style choisi : même calage, on charge (ou télécharge) les tuiles de ce style
        private void StyleChanged()
        {
            if (_busy)
            {
                _cmbStyle.SelectedIndex = Math.Max(0, Map_Web.Styles.FindIndex(x => x.Id == Map_Web.CurrentStyle.Id));
                return;
            }

            Map_Web.UseCampaign(_campaign);
            Map_Web.CurrentStyle = Map_Web.Styles[_cmbStyle.SelectedIndex];

            if (_background != null)
            {
                _view.SetBackground(null);
                _background.Dispose();
                _background = null;
            }

            Map_Background cached = Map_Web.TryLoadCached(_campaign);
            if (cached != null)
                UseBackground(cached);
            else
                BuildBackgroundAsync();         // télécharge les tuiles de ce style (calage refait, rapide)
        }

        private async void ChkBackground_CheckedChanged(object sender, EventArgs e)
        {
            if (_loading || _busy)
                return;

            if (!_chkBackground.Checked)
            {
                _view.ShowBackground = false;
                return;
            }

            if (_background != null)
            {
                _view.ShowBackground = true;
                return;
            }

            Map_Background cached = Map_Web.TryLoadCached(_campaign);
            if (cached != null)
            {
                UseBackground(cached);
                return;
            }

            await BuildBackgroundAsync();
        }

        // Efface le calage enregistré puis relance la construction (réseau si besoin)
        private void RebuildBackground()
        {
            if (_busy)
                return;

            Map_Web.ForgetCalibration(_campaign);

            if (_background != null)
            {
                _view.SetBackground(null);
                _background.Dispose();
                _background = null;
            }
            _view.ShowBackground = false;
            _lblStatus.Text = "";

            SetBackgroundChecked(false);
            _chkBackground.Checked = true;      // déclenche ChkBackground_CheckedChanged
        }

        // ------------------------------------------------------------------
        //  Lecture des données
        // ------------------------------------------------------------------

        // Noms de remplacement des bases : BaseNameAlias dans Init\various_table.lua  ["Bodo"] = "Takhli",
        private void LoadAliases()
        {
            _aliases.Clear();
            Map_Flights.TypeAlias.Clear();
            try
            {
                string file = Path.Combine(ParamConf.PATH_SavedGames_DCS, @"Mods\tech\DCE\Missions\Campaigns", _campaign, "Init", "various_table.lua");
                if (!File.Exists(file)) return;

                // Deux blocs dans le fichier : BaseNameAlias (noms de bases) et TypeAlias (noms de types d'avion)
                string block = "";
                foreach (string raw in File.ReadAllLines(file))
                {
                    string line = raw.Trim();
                    if (line.StartsWith("--")) continue;
                    if (line.StartsWith("BaseNameAlias")) { block = "base"; continue; }
                    if (line.StartsWith("TypeAlias")) { block = "type"; continue; }
                    if (block == "") continue;
                    if (line.StartsWith("}")) { block = ""; continue; }

                    var m = System.Text.RegularExpressions.Regex.Match(line, "^\\[\"(.+?)\"\\]\\s*=\\s*\"(.*?)\"");
                    if (!m.Success) continue;
                    if (block == "base") _aliases[m.Groups[1].Value] = m.Groups[2].Value;
                    else Map_Flights.TypeAlias[m.Groups[1].Value] = m.Groups[2].Value;
                }
            }
            catch (Exception ex)
            {
                FormUtils.LogRegister("Map_UI_Form | various_table : " + ex.Message);
            }
        }

        private string AliasOf(string baseName)
        {
            string a;
            if (_aliases.TryGetValue(baseName, out a)) return a;
            AirbaseInfo info;
            if (_airbases.TryGetValue(baseName, out info) && !string.IsNullOrWhiteSpace(info.AliasName)) return info.AliasName;
            return "";
        }

        // Carte de mission : le panneau de droite a deux onglets, en lecture seule.
        //  - Flights : liste des vols (couleur = celle de la carte) + fiche du vol choisi
        //  - Bases   : fiche de la base choisie (altitude, pistes, fréquences, avions) + ses escadrons
        private void BuildMissionRight(Panel right, Panel noPosPanel)
        {
            _tabs = new TabControl { Dock = DockStyle.Fill };
            _tabFlights = new TabPage("Flights") { Padding = new Padding(6) };
            _tabBases = new TabPage("Bases") { Padding = new Padding(6) };

            // --- Flights ---
            _flightList = new Map_UI_FlightList { Dock = DockStyle.Top, Height = 85 };
            _flightList.SelectedIndexChanged += FlightListChanged;

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
                startTip.SetToolTip(_cmbStart, "START TYPE of the flight (first waypoint)\nChanges only [\"type\"] and [\"action\"] of the first waypoint in the mission file. The parking / parking_id fields of the aircraft are NOT changed.\nParking / Runway need a flight that starts from an airfield.\nGround: altitude 0, no airfield attached.\nAir start: the flight is active at mission start, at the altitude and speed of the first waypoint (1500 m or the next leg speed if they were 0); type and action are both \"Turning Point\".\nActive in pencil mode only.");
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
            _tabFlights.Controls.Add(_flightList);

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
            foreach (Map_Flight f in VisibleFlights().OrderBy(x => x.IsPlayer ? 0 : (x.IsPackage ? 1 : 2)).ThenBy(x => x.Title, StringComparer.OrdinalIgnoreCase))
                _flightList.Items.Add(f);
            _flightList.EndUpdate();
            _syncFlightList = false;

            ShowFlight(null);
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
            List<Map_Flight> sel = _flightList.SelectedItems.Cast<Map_Flight>().ToList();
            _view.SetSelectedFlights(sel.Select(x => x.Name));
            _view.SetSelectedWaypoint("", -1);
            ShowFlight(sel.Count == 0 ? null : sel[sel.Count - 1], sel.Count);
            AfterFlightPicked(sel.Count == 0 ? null : sel[sel.Count - 1], sel.Count);
        }

        // Sélectionne ces vols dans la liste sans déclencher FlightListChanged
        private void SelectInList(List<Map_Flight> flights)
        {
            _syncFlightList = true;
            _flightList.ClearSelected();
            foreach (Map_Flight f in flights)
            {
                int i = _flightList.Items.IndexOf(f);
                if (i >= 0) _flightList.SetSelected(i, true);
            }
            _syncFlightList = false;
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
            if (_btnSaveWpt == null) return;
            _btnSaveWpt.Enabled = ParamConf.UserLevel == UserLevel.CampaignMaker && _missionData != null && _missionFile != ""
                && (_missionData.Flights.Any(q => q.Edited) || _unitRows.Count > 0 || _pendingFreq.Count > 0 || _pendingPresets.Count > 0 || _pendingRemove.Count > 0 || Map_RunwayData.HasPending || _parkDirty.Count > 0);
        }

        // Écrit les waypoints déplacés dans le fichier mission (avec copie de sécurité), puis relit la mission
        private void SaveMovedWaypoints()
        {
            if (_missionData == null || _missionFile == "") return;
            List<Map_Flight> edited = _missionData.Flights.Where(q => q.Edited).ToList();
            bool parks = _parkDirty.Count > 0;
            if (edited.Count == 0 && _unitRows.Count == 0 && _pendingFreq.Count == 0 && _pendingPresets.Count == 0 && _pendingRemove.Count == 0 && !Map_RunwayData.HasPending && !parks) return;

            // 0) pistes (réglage de la carte, écrit dans dce_data_user.ini : pas dans le fichier mission)
            if (Map_RunwayData.HasPending && !CommitRunways()) return;
            if (edited.Count == 0 && _unitRows.Count == 0 && _pendingFreq.Count == 0 && _pendingPresets.Count == 0 && _pendingRemove.Count == 0 && !parks)
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

        // Colonne de gauche posée sur la carte (engrenage vert) : des sections empilées, chacune avec son bandeau.
        // Un clic sur le bandeau replie la section sur cette seule ligne. Les prochaines sections (presets radio, datalink) se placent ici, à la suite.
        private void PlaceOverlays()
        {
            if (_secSky == null || _sky == null || _gearSky == null) return;

            bool on = _gearSky.Checked;
            _secSky.Visible = on;
            if (!on) { _sky.Visible = false; if (_secRadio != null) _secRadio.Visible = false; if (_radioPanel != null) _radioPanel.Visible = false; return; }

            int y = 54;
            _secSky.Location = new Point(10, y);
            y += _secSky.Height + 2;

            _sky.Visible = _secSky.Expanded;
            if (_secSky.Expanded)
            {
                _sky.Location = new Point(10, y);
                y += _sky.Height + 6;
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

        // Le bouton "pistes du web" reflète le réglage du théâtre (enregistré ou en attente)
        private void SyncWebRunwaysButton()
        {
            if (_chkWebRunways == null) return;
            _syncWeb = true;
            _chkWebRunways.Checked = Map_RunwayData.GetSource(Map_RunwayData.TheatreOf(_campaign)) == "web";
            _syncWeb = false;
        }

        // Clic sur le bouton : pistes du web (on ignore les données locales) ou retour aux données locales. Écrit au Save seulement.
        private void SetWebRunways(bool on)
        {
            string theatre = Map_RunwayData.TheatreOf(_campaign);
            if (theatre == "")
            {
                _lblStatus.Text = "Runways: the theatre of this campaign is unknown, the web runways can not be chosen.";
                SyncWebRunwaysButton();
                return;
            }

            Map_RunwayData.SetWebView(theatre, on, _airbases.Keys);
            UpdateTitle();
            UpdateSaveWpt();
            ApplyMode();
            _lblStatus.Text = on
                ? "Runways: web data shown, local data ignored. Click " + (_missionMode ? "Save modification" : "Save") + " to keep it (your own runway settings are then removed)."
                : "Runways: local data shown again.";
        }

        // ------------------------------------------------------------------
        //  Places d'hélicoptère (parkAlertSAR de db_airbases.lua)
        //  Carte Map : le dossier regardé (Init ou Active). Carte de mission : <campagne>_first.miz -> Init, <campagne>_ongoing.miz -> Active
        //  (autre nom de fichier : selon Init / Active). Un jeu de places et ses changements en attente par dossier.
        // ------------------------------------------------------------------

        private string ParkFolder()
        {
            if (_missionMode)
            {
                string n = Path.GetFileNameWithoutExtension(_missionFile ?? "");
                if (n.EndsWith("_first", StringComparison.OrdinalIgnoreCase)) return "Init";
                if (n.EndsWith("_ongoing", StringComparison.OrdinalIgnoreCase)) return "Active";
            }
            return _radioActive != null && _radioActive.Checked ? "Active" : "Init";
        }

        private string ParkFile(string folder)
        {
            return Path.Combine(ParamConf.PATH_SavedGames_DCS, @"Mods\tech\DCE\Missions\Campaigns", _campaign, folder, "db_airbases.lua");
        }

        private Dictionary<string, List<Map_ParkSpot>> ParkStoreOf(string folder)
        {
            Dictionary<string, List<Map_ParkSpot>> s;
            if (!_parkStores.TryGetValue(folder, out s))
            {
                s = new Dictionary<string, List<Map_ParkSpot>>(StringComparer.OrdinalIgnoreCase);
                _parkStores[folder] = s;
            }
            return s;
        }

        private HashSet<string> ParkDirtyOf(string folder)
        {
            HashSet<string> d;
            if (!_parkDirties.TryGetValue(folder, out d))
            {
                d = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                _parkDirties[folder] = d;
            }
            return d;
        }

        // Le jeu de places et les changements en attente du dossier en cours
        private Dictionary<string, List<Map_ParkSpot>> _parkStore { get { return ParkStoreOf(ParkFolder()); } }
        private HashSet<string> _parkDirty { get { return ParkDirtyOf(ParkFolder()); } }
        private bool ParkAnyDirty { get { return _parkDirties.Values.Any(d => d.Count > 0); } }

        // Relit les fichiers (sauf un dossier qui a des changements en attente)
        private void LoadParks()
        {
            foreach (string f in new[] { "Init", "Active" })
                if (ParkDirtyOf(f).Count == 0) _parkStores.Remove(f);
            EnsureParks();
        }

        // Lit le dossier en cours s'il ne l'a pas encore été
        private void EnsureParks()
        {
            string folder = ParkFolder();
            if (!_parkStores.ContainsKey(folder))
                _parkStores[folder] = Map_Drawings.ReadParkSpots(ParkFile(folder));
        }

        // db_airbases relu (Init puis Active) : les points SAR utilisés pour placer les avions d'un vol suivent les places changées
        private void ReloadAirbases()
        {
            try
            {
                var loaded = new Parser_db_airbases().Load_db_airbases(_campaign);
                _airbases = new Dictionary<string, AirbaseInfo>(loaded, StringComparer.OrdinalIgnoreCase);
                if (_ctx != null) _ctx.Airbases = _airbases;
            }
            catch (Exception ex)
            {
                FormUtils.LogRegister("Map_UI_Form | db_airbases (reload) : " + ex.Message);
            }
        }

        private List<Map_ParkSpot> ParksOf(string baseName)
        {
            Dictionary<string, List<Map_ParkSpot>> st;
            List<Map_ParkSpot> l;
            return _parkStores.TryGetValue(ParkFolder(), out st) && st.TryGetValue(baseName, out l) ? l : new List<Map_ParkSpot>();
        }

        private void ParkToggled()
        {
            bool on = _chkPark.Checked;
            _view.ParkShow = on;
            if (_parkBar != null) _parkBar.Visible = on;
            _parkCur = null;
            if (_missionMode && on && _tabs != null && _tabBases != null) _tabs.SelectedTab = _tabBases;
            ResizeBaseHost();
            UpdateParkBar();
        }

        private void MarkParkDirty(string baseName)
        {
            _parkDirty.Add(baseName);
            UpdateTitle();
            if (_missionMode) UpdateSaveWpt();
        }

        // Boutons et cases de la barre : selon la place choisie
        private void UpdateParkBar()
        {
            if (_parkBar == null) return;
            _parkSync = true;
            Map_ParkSpot s = _parkCur;
            _numParkHdg.Enabled = s != null;
            _btnParkDel.Enabled = s != null;
            _btnParkKeep.Enabled = s != null && !s.HasHdg;
            if (s != null)
            {
                double deg = ((s.Hdg * 180.0 / Math.PI) % 360 + 360) % 360;
                _numParkHdg.Value = (decimal)Math.Min(359.9, Math.Round(deg, 1));
                _lblParkRad.Text = "= " + s.Hdg.ToString("0.000", CultureInfo.InvariantCulture) + " rad";
            }
            else
            {
                _numParkHdg.Value = 0;
                _lblParkRad.Text = "";
            }

            Map_Base b = _view.GetBase(_selectedBase);
            _btnParkAdd.Enabled = b != null;
            int n = b == null ? 0 : b.Parks.Count;
            _lblParkInfo.Text = b == null ? "Click a base, then a spot (or + Add spot)."
                : b.Name + " (" + ParkFolder() + "): " + n + " spot(s)." + (s != null && !s.HasHdg ? " This spot has no heading in the file (shown facing north)." : "");
            _parkSync = false;
        }

        private void OnParkSelected(Map_ParkSpot s)
        {
            _parkCur = s;
            if (s != null && !string.Equals(_selectedBase, s.Base, StringComparison.OrdinalIgnoreCase))
            {
                _view.SelectedName = s.Base;
                ShowBase(s.Base);
            }
            UpdateParkBar();
        }

        private void OnParkChanged(Map_ParkSpot s)
        {
            if (ReferenceEquals(s, _parkCur)) UpdateParkBar();      // le cap affiché suit la souris
        }

        private void OnParksEdited(string baseName)
        {
            MarkParkDirty(baseName);
            _lblStatus.Text = "Helicopter spots of " + baseName + " changed: click " + (_missionMode ? "Save modification" : "Save") + " to keep them.";
        }

        private void ParkAddPressed()
        {
            if (_view.GetBase(_selectedBase) == null) { _lblStatus.Text = "Helicopter spots: click a base first."; return; }
            _view.ParkAddMode = true;
            _lblStatus.Text = "Helicopter spots: click on the map where the new spot goes (Esc = cancel).";
        }

        private void OnParkAddClicked(double x, double y)
        {
            Map_Base b = _view.GetBase(_selectedBase);
            if (b == null) return;

            List<Map_ParkSpot> list;
            if (!_parkStore.TryGetValue(b.Name, out list)) { list = new List<Map_ParkSpot>(); _parkStore[b.Name] = list; }
            b.Parks = list;

            var s = new Map_ParkSpot { Base = b.Name, X = x, Y = y, Hdg = 0, HasHdg = true };
            list.Add(s);
            _parkCur = s;
            _view.SelectedPark = s;
            MarkParkDirty(b.Name);
            UpdateParkBar();
            _lblStatus.Text = "Helicopter spot added on " + b.Name + " (facing north). Turn it with the round handle, then click " + (_missionMode ? "Save modification" : "Save") + ".";
        }

        private void ParkDeletePressed()
        {
            Map_ParkSpot s = _parkCur;
            if (s == null) return;

            List<Map_ParkSpot> list;
            if (_parkStore.TryGetValue(s.Base, out list)) list.Remove(s);
            _parkCur = null;
            _view.SelectedPark = null;
            MarkParkDirty(s.Base);
            UpdateParkBar();
            _lblStatus.Text = "Helicopter spot removed from " + s.Base + ": click " + (_missionMode ? "Save modification" : "Save") + " to keep it." + (_missionMode ? " (A helicopter of the mission standing on it stays where it is.)" : "");
        }

        private void ParkKeepPressed()
        {
            if (_parkCur == null) return;
            _parkCur.HasHdg = true;
            MarkParkDirty(_parkCur.Base);
            UpdateParkBar();
            _view.Invalidate();
        }

        private void ParkHeadingChanged()
        {
            if (_parkSync || _parkCur == null) return;
            _parkCur.Hdg = (double)_numParkHdg.Value * Math.PI / 180.0;
            _parkCur.HasHdg = true;
            MarkParkDirty(_parkCur.Base);
            _lblParkRad.Text = "= " + _parkCur.Hdg.ToString("0.000", CultureInfo.InvariantCulture) + " rad";
            _btnParkKeep.Enabled = false;
            _view.Invalidate();
        }

        // Écrit les places changées (tous les dossiers qui ont des changements). false = échec (message déjà affiché)
        private bool CommitParks()
        {
            foreach (string folder in _parkDirties.Where(kv => kv.Value.Count > 0).Select(kv => kv.Key).ToList())
                if (!CommitParksOf(folder)) return false;
            return true;
        }

        private bool CommitParksOf(string folder)
        {
            HashSet<string> dirty = ParkDirtyOf(folder);
            if (dirty.Count == 0) return true;
            Dictionary<string, List<Map_ParkSpot>> store = ParkStoreOf(folder);

            var wanted = new Dictionary<string, List<Map_ParkSpot>>(StringComparer.OrdinalIgnoreCase);
            foreach (string n in dirty)
            {
                List<Map_ParkSpot> l;
                wanted[n] = store.TryGetValue(n, out l) ? l : new List<Map_ParkSpot>();
            }

            // Carte de mission : les hélicos du .miz posés sur une place qui a bougé la suivent (contrôle d'abord, écriture après db_airbases)
            List<Map_Drawings.ParkMove> moves = null;
            string msg;
            if (_missionMode && _missionFile != "")
            {
                moves = new List<Map_Drawings.ParkMove>();
                foreach (List<Map_ParkSpot> list in wanted.Values)
                    foreach (Map_ParkSpot sp in list)
                    {
                        if (!sp.Changed) continue;
                        double h = ((sp.Hdg % (2 * Math.PI)) + 2 * Math.PI) % (2 * Math.PI);
                        moves.Add(new Map_Drawings.ParkMove { OldX = sp.OrigX, OldY = sp.OrigY, NewX = sp.X, NewY = sp.Y, NewHdg = Math.Round(h, 5), SetHdg = sp.HasHdg });
                    }
                if (moves.Count > 0 && !Map_Drawings.MoveParkUnits(_missionFile, moves, true, out msg))
                {
                    MessageBox.Show(msg, "Save helicopter spots", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return false;
                }
            }

            if (!Map_Drawings.ApplyParkSpots(ParkFile(folder), wanted, out msg))
            {
                MessageBox.Show(msg, "Save helicopter spots", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }

            if (moves != null && moves.Count > 0)
            {
                string msg2;
                if (Map_Drawings.MoveParkUnits(_missionFile, moves, false, out msg2)) msg += "   " + msg2;
                else MessageBox.Show("The spots are saved in " + folder + "\\db_airbases.lua, but the helicopters of the mission file could not be moved:\n\n" + msg2, "Save helicopter spots", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }

            dirty.Clear();
            _parkCur = null;
            _view.SelectedPark = null;
            _parkStores.Remove(folder);
            EnsureParks();
            ReloadAirbases();
            ApplyMode();
            UpdateParkBar();
            UpdateTitle();
            if (_missionMode) UpdateSaveWpt();
            _parkMsg = msg;
            _lblStatus.Text = msg;
            return true;
        }

        // Pistes tirées à la souris : gardées en attente, écrites seulement au clic sur Save
        private void OnRunwaysEdited(string baseName)
        {
            Map_Base b = _view.GetBase(baseName);
            if (b == null) return;

            Map_RunwayData.SetPending(b.Name, b.X, b.Y, b.Runways);
            UpdateTitle();
            _lblStatus.Text = "Runways of " + b.Name + " changed: click Save to keep them.";
        }

        // Saisie exacte : pistes de la base choisie (onglet "This base") + règle de position pour toute la carte (onglet "Whole map")
        private void EditRunwaysData()
        {
            Map_Base b = _view.GetBase(_selectedBase);
            if (b != null && (b.IsFarp || Map_UI_View.IsCarrier(b))) b = null;

            string theatre = Map_RunwayData.TheatreOf(_campaign);
            using (var dlg = new Map_RunwayDialog(b == null ? null : b.Name, b == null ? 0 : b.X, b == null ? 0 : b.Y,
                b == null ? new List<Map_Runway>() : Map_RunwayData.Merged(b),
                theatre, Map_RunwayData.GetAnchorSetting(theatre), Map_RunwayData.AnchorStatus(theatre),
                _background != null ? _background.AnchorInfo : "", Map_RunwayData.GetSource(theatre)))
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;

                if (b != null && dlg.BaseReset) Map_RunwayData.SetPending(b.Name, b.X, b.Y, null);
                else if (b != null && dlg.BaseChanged) Map_RunwayData.SetPending(b.Name, b.X, b.Y, dlg.BaseResult);
                if (dlg.AnchorChanged) Map_RunwayData.SetPendingAnchor(theatre, dlg.AnchorResult);
                if (dlg.SourceChanged)
                {
                    Map_RunwayData.SetPendingSource(theatre, dlg.SourceResult);
                    if (dlg.SourceResult == "web") Map_RunwayData.ResetSettingsOf(_airbases.Keys);      // les réglages à la main de cette carte sont écartés (au prochain Save)
                }

                UpdateTitle();
                _lblStatus.Text = "Runway changes made: click Save to keep them.";
                ApplyMode();
            }
        }

        // Fiche de la base (carte de mission) : altitude, pistes, fréquences, avions
        private List<Squad> _cardSquads;        // derniers arguments de la fiche base (pour la redessiner quand on change d'unités)
        private int _cardReady, _cardReserve;

        private void UpdateBaseCard(List<Squad> squads, int ready, int reserve)
        {
            if (!_missionMode || _baseCard == null) return;
            _cardSquads = squads; _cardReady = ready; _cardReserve = reserve;

            if (_selectedBase == "")
            {
                _baseCard.SetContent("Bases", "Click a base on the map.", Color.Gray, null);
                ResizeBaseHost();
                return;
            }

            CultureInfo c = CultureInfo.InvariantCulture;
            AirbaseInfo a;
            _airbases.TryGetValue(_selectedBase, out a);

            string alias = AliasOf(_selectedBase);
            string title = alias != "" ? _selectedBase + " (" + alias + ")" : _selectedBase;

            string side = a != null && a.Side != null ? a.Side : "";
            string sub = side.ToUpperInvariant();
            if (a != null)
            {
                string zs = ZoneSideOf(a.X, a.Y, _radioInit.Checked);
                if (zs != "" && !string.Equals(zs, side, StringComparison.OrdinalIgnoreCase)) sub += "  -  held by " + zs + " (zone)";
                if (a.Inactive) sub += "  -  INACTIVE";
            }

            Color accent = side.Equals("blue", StringComparison.OrdinalIgnoreCase) ? Color.FromArgb(70, 140, 255)
                         : side.Equals("red", StringComparison.OrdinalIgnoreCase) ? Color.FromArgb(235, 70, 70) : Color.Gray;

            var rows = new List<KeyValuePair<string, string>>();
            if (a != null)
            {
                rows.Add(new KeyValuePair<string, string>("Altitude", Map_Units.Alt(a.Elevation)));

                string rwSrc;
                List<Map_Runway> eff = Map_RunwayData.Get(_selectedBase, out rwSrc);
                if (eff != null && eff.Count > 0 && rwSrc != "db_airbases")
                    rows.Add(new KeyValuePair<string, string>("Runways", string.Join("\n",
                        eff.Select(r => Map_Units.Alt(r.Length) + "   " + (((r.Hdg % 360) + 360) % 360).ToString("0", c) + "\u00B0")) + "\n(" + rwSrc + ")"));
                else if (a.Runways != null && a.Runways.Count > 0)
                    rows.Add(new KeyValuePair<string, string>("Runways", string.Join("\n",
                        a.Runways.Select(r => (r.Name ?? "?") + "   " + Map_Units.Alt(r.Length) + "   " + r.Hdg.ToString("0", c) + "\u00B0"))));

                if (a.ATCFrequencies != null && a.ATCFrequencies.Count > 0)
                    rows.Add(new KeyValuePair<string, string>("ATC", string.Join("\n",
                        a.ATCFrequencies.Select(f => (f > 100000 ? f / 1000000.0 : f).ToString("0.000", c) + " MHz"))));

                if (a.Code != null && a.Code.Count > 0)
                    rows.Add(new KeyValuePair<string, string>("Codes", string.Join("   ", a.Code.Select(k => k.Key + " " + k.Value))));
            }
            rows.Add(new KeyValuePair<string, string>("Squadrons", squads.Count.ToString(c)));
            rows.Add(new KeyValuePair<string, string>("Aircraft", ready.ToString(c) + " ready   /   " + reserve.ToString(c) + " in reserve"));

            _baseCard.SetContent(title, sub, accent, rows);
            ResizeBaseHost();
        }

        // Le conteneur de l'onglet Bases défile quand fiche + escadrons + bases sans position dépassent la hauteur
        private void ResizeBaseHost()
        {
            if (_baseHost == null) return;
            _baseHost.AutoScrollMinSize = new Size(0, _baseCard.Height + SquadListHeight + 120 + 8 + (_parkBar != null && _parkBar.Visible ? _parkBar.Height : 0));
        }

        // Frontières / zones du fichier mission : lues une fois, affichées par défaut s'il y en a
        private void LoadDrawings()
        {
            _presetOrig.Clear();
            _presetUnits.Clear();
            _presetFirst.Clear();
            SetGroup("mission", Map_Drawings.Load(_campaign), _chkDrawings, true);
            SetGroup("zones", Map_Drawings.LoadWargameZones(_campaign, _radioActive.Checked), _chkZones, true);
            SetGroup("spawn", Map_Drawings.LoadWargameSpawn(_campaign), _chkSpawn, false);

            if (_missionMode)
            {
                Map_MissionData data = Map_Drawings.LoadMission(_campaign, _justGenerated);
                SetGroup("plans", data.Plans, _chkPlans, true);
                SetGroup("threats", data.Threats, _chkThreats, true);
                SetGroup("ships", data.ShipRoutes, _chkShips, false);       // trajets des navires : à cocher pour les voir
                _ewrCircles = data.Radars;
                _chkRadars.Checked = false;                                 // zones radar : à cocher pour les voir

                _view.SetGround(data.Ground, data.GroundGroups);
                bool hasBlue = data.Ground.Any(u => !u.Red), hasRed = data.Ground.Any(u => u.Red);
                _chkGround.Enabled = data.Ground.Count > 0;
                _chkGroundNames.Enabled = data.Ground.Count > 0;
                if (_chkGroundBlue != null) { _chkGroundBlue.Enabled = hasBlue; _chkGroundRed.Enabled = hasRed; }
                ApplyGround();

                _missionInfo = data.Error != ""
                    ? data.Error
                    : "Mission: " + Path.GetFileName(data.Source) + (data.Fallback ? " (the expected file had no player flight)" : "") + "   |   " + data.PlayerFlights + " player flight(s) + " + data.PackageFlights + " package flight(s) + " + data.OtherFlights + " other flight(s), " + data.SamSites + " SAM site(s) shown, " + data.Ground.Count + " ground element(s) available";
                _lblStatus.Text = _missionInfo;

                SetDefaultCampBoxes(data);      // CampaignMaker : le camp adverse est décoché
                FillFlightList(data);
                SelectMyFlights();              // le vol du joueur et son package sont choisis, la carte se cadre dessus
                RefreshRadars();

                _missionFile = data.Source;
                UpdateTitle();

                if (_sky != null) _sky.Reload(_campaign, _justGenerated, data.Source == "" ? null : data.Source);
                if (_secSky != null) _secSky.Summary = "mission start " + ((int)(data.StartTime / 3600) % 24).ToString("00") + ":" + ((int)(data.StartTime % 3600) / 60).ToString("00");
            }
        }

        private string _missionInfo = "";

        // Cases des éléments au sol : "tous" coche / décoche bleu + rouge ; bleu et rouge cochés ensemble = "tous" coché
        private bool _syncGround;

        private void GroundBoxChanged(object sender)
        {
            if (_syncGround) return;
            if (_chkGroundBlue != null)
            {
                _syncGround = true;
                if (sender == _chkGround)
                    _chkGroundBlue.Checked = _chkGroundRed.Checked = _chkGround.Checked;
                else if (sender == _chkGroundBlue || sender == _chkGroundRed)
                    _chkGround.Checked = (_chkGroundBlue.Checked || !_chkGroundBlue.Enabled) && (_chkGroundRed.Checked || !_chkGroundRed.Enabled);
                _syncGround = false;
            }
            ApplyGround();
        }

        private void ApplyGround()
        {
            if (_view == null || _chkGround == null) return;
            bool blue = _chkGroundBlue != null ? _chkGroundBlue.Checked : _chkGround.Checked;
            bool red = _chkGroundRed != null ? _chkGroundRed.Checked : _chkGround.Checked;
            _view.SetGroundShown(blue, red, _chkGroundNames.Checked);
        }

        // Clic sur un élément au sol : petite fenêtre avec son nom et ses champs (texte copiable)
        private Map_UI_GroundInfo _groundInfo;

        private void OnGroundClicked(Map_GroundUnit u)
        {
            try
            {
                if (_groundInfo == null || _groundInfo.IsDisposed)
                {
                    _groundInfo = new Map_UI_GroundInfo();
                    _groundInfo.Location = PointToScreen(new Point(Width - _groundInfo.Width - 460, 140));
                }
                _groundInfo.ShowUnit(u, _background != null ? _background.Proj : null);
                if (!_groundInfo.Visible) _groundInfo.Show(this);
            }
            catch (Exception ex)
            {
                FormUtils.LogRegister("Map_UI_Form | ground info : " + ex);
            }
        }

        // Recharge seulement les zones wargame (couleur = camp du mode Init ou Active), en gardant l'état du bouton
        private void ReloadZones()
        {
            bool wasOn = _chkZones.Checked;
            List<Map_Drawing> list = Map_Drawings.LoadWargameZones(_campaign, _radioActive.Checked);

            _view.SetDrawings("zones", list);
            _chkZones.Enabled = list.Count > 0;
            _view.SetDrawingsVisible("zones", wasOn && list.Count > 0);
        }

        // Charge un groupe de tracés dans la carte : bouton grisé s'il n'y a rien à montrer
        private void SetGroup(string group, List<Map_Drawing> list, CheckBox button, bool shownByDefault)
        {
            _view.SetDrawings(group, list);
            button.Enabled = list.Count > 0;
            bool on = list.Count > 0 && shownByDefault;
            button.Checked = on;
            _view.SetDrawingsVisible(group, on);
        }

        private void LoadFromDisk()
        {
            try
            {
                var loaded = new Parser_db_airbases().Load_db_airbases(_campaign);
                _airbases = new Dictionary<string, AirbaseInfo>(loaded, StringComparer.OrdinalIgnoreCase);
            }
            catch (Exception ex)
            {
                FormUtils.LogRegister("Map_UI_Form | db_airbases : " + ex.Message);
                _airbases = new Dictionary<string, AirbaseInfo>(StringComparer.OrdinalIgnoreCase);
                MessageBox.Show("The airbases could not be read: " + ex.Message, "Map");
            }

            if (_ctx != null)
                _ctx.Airbases = _airbases;

            LoadAliases();
            LoadDrawings();
            LoadParks();

            _zonesInit = Map_Drawings.LoadWargameZoneData(_campaign, false);
            _zonesActive = Map_Drawings.LoadWargameZoneData(_campaign, true);

            // Squadrons : on prend ceux de la fenêtre principale s'ils concernent bien cette campagne
            Campaign_Edit_Grid_Right edit = null;
            try
            {
                edit = Main_Form.Instance.CampaignGridLeft.CurrentCampaignEdit;
            }
            catch
            {
                edit = null;
            }

            bool sameCampaign = string.Equals(ParamCampaignSelected.NameCampaign, _campaign, StringComparison.OrdinalIgnoreCase);

            if (edit != null && sameCampaign && edit.CurrentCampaignSquads != null && edit.CurrentCampaignSquads.Count > 0)
            {
                _edit = edit;
                _allSquads = edit.CurrentCampaignSquads;
                _flat = edit.CurrentSquads;
                _readOnly = false;
            }
            else
            {
                _edit = null;
                _flat = null;
                _readOnly = true;
                _allSquads = LoadSquadsWithoutSideEffect();
            }

            if (_missionMode)
                _readOnly = true;       // pour l'instant la carte de mission ne modifie rien
        }

        // Le parseur d'oob_air remplit des listes globales (List_oob_air_Manager) que les onglets OOB utilisent.
        // On les sauvegarde puis on les remet : ouvrir la carte ne touche donc pas aux modifications en cours.
        private List<CampaignSquad> LoadSquadsWithoutSideEffect()
        {
            List<Squad> oldSquads = List_oob_air_Manager.List_oob_air;
            List<CampaignSquad> oldCampaignSquads = List_oob_air_Manager.List_campaignSquads;
            string oldParseName = ParamCampaign.NameFileParse;

            try
            {
                List<CampaignSquad> result = new Parser_OobAir().LoadCampaignSquads(_campaign);
                return result != null ? new List<CampaignSquad>(result) : new List<CampaignSquad>();
            }
            catch (Exception ex)
            {
                FormUtils.LogRegister("Map_UI_Form | oob_air : " + ex.Message);
                MessageBox.Show("The squadrons could not be read: " + ex.Message, "Map");
                return new List<CampaignSquad>();
            }
            finally
            {
                List_oob_air_Manager.List_oob_air = oldSquads;
                List_oob_air_Manager.List_campaignSquads = oldCampaignSquads;
                ParamCampaign.NameFileParse = oldParseName;
            }
        }

        // ------------------------------------------------------------------
        //  Affichage selon le mode (Init / Active)
        // ------------------------------------------------------------------

        // Le squadron à afficher pour ce mode (null = il n'existe pas dans ce mode ou il a été supprimé)
        private Squad SquadOf(CampaignSquad cs, bool init, HashSet<Squad> liveSet)
        {
            Squad s;
            if (_readOnly)
                s = init ? cs.Init : (cs.Active ?? cs.Init);     // lecture seule : on se contente de ce qui existe
            else
                s = init ? cs.Init : cs.Active;

            if (s == null) return null;
            if (liveSet != null && !liveSet.Contains(s)) return null;      // supprimé de la liste
            return s;
        }

        private void ApplyMode()
        {
            bool init = _radioInit.Checked;
            HashSet<Squad> liveSet = _flat != null ? new HashSet<Squad>(_flat) : null;

            // Regroupe les squadrons par base
            _squadsByBase = new Dictionary<string, List<Squad>>(StringComparer.OrdinalIgnoreCase);
            var aircraftByBase = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            foreach (CampaignSquad cs in _allSquads)
            {
                Squad s = SquadOf(cs, init, liveSet);
                if (s == null) continue;

                string baseName = (s.Base ?? "").Trim();
                if (baseName == "") baseName = "(no base)";

                List<Squad> list;
                if (!_squadsByBase.TryGetValue(baseName, out list))
                {
                    list = new List<Squad>();
                    _squadsByBase[baseName] = list;
                }
                list.Add(s);

                // Tous les squadrons comptent (même inactifs, souvent le cas en Init) : la silhouette montre ce qui est basé là
                {
                    int n;
                    aircraftByBase.TryGetValue(baseName, out n);
                    aircraftByBase[baseName] = n + (init ? s.Number : s.DisplayReady);
                }
            }

            // Bases à dessiner / squadrons sans position
            bool hideEmpty = _chkHideEmpty.Checked;
            var markers = new List<Map_Base>();
            var noPos = new List<string>();

            foreach (KeyValuePair<string, AirbaseInfo> kv in _airbases)
            {
                AirbaseInfo a = kv.Value;
                int count = _squadsByBase.ContainsKey(kv.Key) ? _squadsByBase[kv.Key].Count : 0;
                int aircraft;
                aircraftByBase.TryGetValue(kv.Key, out aircraft);

                // Porte-avions, LHA : pas de x/y ; "Reserves" : x = 9999999999 (volontairement hors carte)
                bool placed = !(a.X == 0 && a.Y == 0) && Math.Abs(a.X) < 1e7 && Math.Abs(a.Y) < 1e7;

                if (placed)
                {
                    if (hideEmpty && count == 0) continue;

                    markers.Add(new Map_Base
                    {
                        Name = kv.Key,
                        X = a.X,
                        Y = a.Y,
                        Side = a.Side,
                        ZoneSide = ZoneSideOf(a.X, a.Y, init),
                        Inactive = a.Inactive,
                        IsFarp = kv.Key.IndexOf("FARP", StringComparison.OrdinalIgnoreCase) >= 0,
                        Squads = count,
                        Aircraft = aircraft,
                        Alias = AliasOf(kv.Key),
                        Parks = ParksOf(kv.Key),
                        Runways = a.Runways == null ? new List<Map_Runway>()
                            : a.Runways.Select(rw => new Map_Runway { Hdg = rw.Hdg, Length = rw.Length, X = rw.X, Y = rw.Y }).ToList()
                    });
                }
                else if (count > 0)
                {
                    noPos.Add(kv.Key);
                }
            }

            // Squadrons dont la base n'existe pas du tout dans db_airbases
            foreach (string baseName in _squadsByBase.Keys)
            {
                if (!_airbases.ContainsKey(baseName))
                    noPos.Add(baseName);
            }

            noPos.Sort(StringComparer.OrdinalIgnoreCase);
            _noPosNames.Clear();
            _noPosNames.AddRange(noPos);

            _listNoPos.Items.Clear();
            foreach (string n in _noPosNames)
                _listNoPos.Items.Add(n + "  (" + _squadsByBase[n].Count + ")");

            Map_RunwayData.Fill(_campaign, markers, _background != null ? _background.Proj : null);        // réglage de l'utilisateur > db_airbases > données publiques

            _view.SetBases(markers);

            // On garde la base choisie quand on change de mode ou après une modification
            if (_selectedBase != "")
            {
                bool onMap = markers.Any(m => string.Equals(m.Name, _selectedBase, StringComparison.OrdinalIgnoreCase));
                _view.SelectedName = onMap ? _selectedBase : "";
                ShowBase(_selectedBase);
            }
            else
            {
                UpdateButtons();
            }
        }

        // Camp qui tient la zone wargame contenant la position (x = nord, y = est) : "blue" / "red" / "other", "" si hors zone
        private string ZoneSideOf(double x, double y, bool init)
        {
            List<WargameZoneData> zones = init ? _zonesInit : _zonesActive;
            if (zones == null) return "";

            var p = new PointF((float)x, (float)y);
            foreach (WargameZoneData z in zones)
            {
                if (z.Id != null && z.Id.StartsWith("LANDING", StringComparison.OrdinalIgnoreCase)) continue;   // plans de débarquement : pas des zones tenues

                bool inside;
                if (z.Shape == WargameZoneShape.Circle)
                {
                    double dx = x - z.Center.X, dy = y - z.Center.Y;
                    inside = dx * dx + dy * dy <= (double)z.Radius * z.Radius;
                }
                else
                {
                    inside = WargameEngineCombat.FindZoneContaining(new List<WargameZoneData> { z }, p) != null;
                }

                if (!inside) continue;
                return z.Control == WargameSide.Blue ? "blue" : z.Control == WargameSide.Red ? "red" : "other";
            }
            return "";
        }

        // Remplit le panneau de droite pour la base choisie
        private void ShowBase(string name, Squad reselect = null)
        {
            _selectedBase = name ?? "";
            _listSquads.BeginUpdate();
            _listSquads.Items.Clear();

            List<Squad> squads;
            if (!_squadsByBase.TryGetValue(_selectedBase, out squads))
                squads = new List<Squad>();

            bool init = _radioInit.Checked;
            int ready = 0, reserve = 0;

            foreach (Squad s in squads.OrderBy(x => x.DisplayName ?? x.Name, StringComparer.OrdinalIgnoreCase))
            {
                int r = init ? s.Number : s.DisplayReady;
                int res = ReserveOf(s);
                ready += r;
                reserve += res;

                var item = new ListViewItem(s.DisplayName ?? s.Name ?? "?");
                item.Tag = s;
                item.SubItems.Add(s.Type ?? "");
                item.SubItems.Add(r.ToString());
                item.SubItems.Add(res.ToString());

                string flags = "";
                if (s.Player) flags += "PLAYER ";
                if (s.Squad_Inactive) flags += "inactive";
                item.SubItems.Add(flags.Trim());

                string side = (s.SideString ?? s.Side ?? "").ToLowerInvariant();
                if (side == "blue") item.ForeColor = Color.RoyalBlue;
                else if (side == "red") item.ForeColor = Color.Firebrick;
                if (s.Squad_Inactive) item.ForeColor = Color.Gray;

                if (reselect != null && ReferenceEquals(s, reselect))
                    item.Selected = true;

                _listSquads.Items.Add(item);
            }

            // Ligne vide finale : c'est là que s'affichent "+" et poubelle (jamais sélectionnable)
            if (_selectedBase != "")
                _listSquads.Items.Add(new ListViewItem(""));

            _listSquads.EndUpdate();

            UpdateBaseCard(squads, ready, reserve);

            string alias = _selectedBase == "" ? "" : AliasOf(_selectedBase);
            _lblBase.Text = _selectedBase == "" ? "Click a base on the map" : (alias != "" ? _selectedBase + " (" + alias + ")" : _selectedBase);

            if (_selectedBase == "")
            {
                _lblInfo.Text = "";
                UpdateButtons();
                return;
            }

            AirbaseInfo a;
            string info = "";
            if (_airbases.TryGetValue(_selectedBase, out a))
            {
                string zs = ZoneSideOf(a.X, a.Y, init);
                if (zs != "" && !string.Equals(zs, a.Side, StringComparison.OrdinalIgnoreCase)) info += "held by " + zs + " (zone)  -  ";
                else if (!string.IsNullOrEmpty(a.Side)) info += a.Side + "  -  ";
                if (a.Inactive) info += "INACTIVE  -  ";
            }
            info += squads.Count + " squadron(s), " + ready + " ready, " + reserve + " in reserve";
            _lblInfo.Text = info;
            UpdateButtons();
        }

        // Avions en réserve : roster.reserve en Active, sinon la valeur du squadron
        private static int ReserveOf(Squad s)
        {
            object value;
            int parsed;
            if (s.Roster != null && s.Roster.TryGetValue("reserve", out value)
                && value != null && int.TryParse(value.ToString(), out parsed))
                return parsed;

            return s.Reserve;
        }

        // Clic sur une base de la carte : soit on affiche ses squadrons, soit c'est la destination d'un Move
        private void OnBaseClicked(string name)
        {
            if (_moveSquad != null)
            {
                DoMove(name);
                return;
            }

            _listNoPos.ClearSelected();

            if (_missionMode && _tabs != null)
            {
                _view.SelectedFlight = "";
                OnFlightsClicked(new List<string>());
                _tabs.SelectedTab = _tabBases;
            }
            ShowBase(name);
        }

        // ------------------------------------------------------------------
        //  Édition des squadrons
        // ------------------------------------------------------------------

        private Squad SelectedSquad()
        {
            if (_listSquads.SelectedItems.Count == 0)
                return null;
            return _listSquads.SelectedItems[0].Tag as Squad;
        }

        // Une modification a été faite sur le mode affiché (Init ou Active)
        private void MarkDirty(bool alsoOtherMode)
        {
            if (_radioInit.Checked || alsoOtherMode) _dirtyInit = true;
            if (!_radioInit.Checked || alsoOtherMode) _dirtyActive = true;
        }

        // Après une modification : on remet à jour les onglets OOB de la fenêtre principale puis cette carte
        private void AfterChange(Squad reselect = null)
        {
            try
            {
                Main_Form.Instance.CampaignGridLeft.RefreshGrids();
            }
            catch (Exception ex)
            {
                FormUtils.LogRegister("Map_UI_Form | RefreshGrids : " + ex.Message);
            }

            ApplyMode();
            if (reselect != null && _selectedBase != "")
                ShowBase(_selectedBase, reselect);
            UpdateTitle();
        }

        // Contexte demandé par la fenêtre d'édition d'un squadron (listes de pays, avions, bases...)
        private bool EnsureContext()
        {
            if (_ctx != null)
                return true;

            try
            {
                CampaignLuaData lua = new CampaignLuaLoader().Load(_campaign);
                if (lua == null)
                {
                    MessageBox.Show("The campaign data (aircraft, tasks...) could not be loaded.", "Map");
                    return false;
                }

                _ctx = new CampaignContext
                {
                    CampaignName = _campaign,
                    LuaData = lua,
                    Airbases = _airbases,
                    CampaignEditRef = _edit
                };
                return true;
            }
            catch (Exception ex)
            {
                FormUtils.LogRegister("Map_UI_Form | contexte : " + ex.Message);
                MessageBox.Show("The campaign data needed to edit a squadron could not be loaded: " + ex.Message, "Map");
                return false;
            }
        }

        // Renvoie true si l'utilisateur a validé (Save) dans la fenêtre d'édition
        private bool OpenEditor(Squad squad)
        {
            if (!EnsureContext())
                return false;

            try
            {
                using (var frm = new SquadEdit_Form(squad, _ctx, false, "MapEdit"))
                {
                    return frm.ShowDialog(this) == DialogResult.OK;
                }
            }
            catch (Exception ex)
            {
                FormUtils.LogRegister("Map_UI_Form | SquadEdit_Form : " + ex);
                MessageBox.Show("The squadron could not be edited: " + ex.Message, "Map");
                return false;
            }
        }

        private void EditSelected()
        {
            Squad squad = SelectedSquad();
            if (squad == null || _readOnly)
                return;

            // Cancel : rien n'a changé ; Save : le squadron est à jour (fichier écrit avec le Save de la carte)
            if (!OpenEditor(squad))
                return;

            MarkDirty(false);
            AfterChange(squad);
        }

        // Modèle pour un nouveau squadron : celui qui est choisi, sinon un de la base, sinon un du même camp que la base, sinon n'importe lequel
        private Squad PickTemplate(bool init)
        {
            Squad sel = SelectedSquad();
            if (sel != null) return sel;

            List<Squad> here;
            if (_squadsByBase.TryGetValue(_selectedBase, out here) && here.Count > 0)
                return here[0];

            HashSet<Squad> liveSet = _flat != null ? new HashSet<Squad>(_flat) : null;
            string baseSide = "";
            AirbaseInfo a;
            if (_airbases.TryGetValue(_selectedBase, out a) && a != null)
            {
                string z = ZoneSideOf(a.X, a.Y, init);
                baseSide = ((z == "blue" || z == "red") ? z : (a.Side ?? "")).ToLowerInvariant();
            }

            Squad any = null;
            foreach (CampaignSquad cs in _allSquads)
            {
                Squad s = SquadOf(cs, init, liveSet);
                if (s == null) continue;
                if (any == null) any = s;
                if (baseSide != "" && string.Equals((cs.SideString ?? s.SideString ?? "").Trim(), baseSide, StringComparison.OrdinalIgnoreCase))
                    return s;
            }
            return any;
        }

        // Bouton "+" : nouveau squadron sur la base affichée (marche aussi sur une base vide)
        private void AddSquad()
        {
            if (_readOnly || _flat == null || _selectedBase == "" || _selectedBase == "(no base)")
                return;

            bool init = _radioInit.Checked;
            Squad src = PickTemplate(init);
            if (src == null)
            {
                MessageBox.Show("There is no squadron at all in this campaign to use as a model.", "Map");
                return;
            }
            CampaignSquad owner = _allSquads.FirstOrDefault(c => ReferenceEquals(c.Init, src) || ReferenceEquals(c.Active, src));

            // Copie de la version affichée, puis de l'autre version (même nom, même identifiant)
            Squad main = CopySquad(src);
            main.GenerateCloneIdentity(_flat);
            main.Player = false;
            main.Base = _selectedBase;
            main.FolderFile = init ? "Init" : "Active";

            Squad twin = CopySquad(main);
            twin.Base = _selectedBase;
            twin.FolderFile = init ? "Active" : "Init";

            string side = owner != null && owner.SideString != null ? owner.SideString : src.SideString;
            var created = new CampaignSquad
            {
                SideString = side,
                Key = (side ?? "").Trim().ToLowerInvariant() + "|" + (main.Name ?? "").Trim().ToLowerInvariant()
            };
            if (init) { created.Init = main; created.Active = twin; }
            else { created.Active = main; created.Init = twin; }

            _allSquads.Add(created);
            if (created.Init != null) _flat.Add(created.Init);
            if (created.Active != null) _flat.Add(created.Active);

            MarkDirty(true);
            AfterChange(main);

            // On ouvre directement l'édition de la copie (nom, avion, nombre...)
            if (!OpenEditor(main))
            {
                // Cancel : la copie n'est pas gardée
                _flat.Remove(created.Init);
                _flat.Remove(created.Active);
                _allSquads.Remove(created);
                AfterChange();
                return;
            }

            AfterChange(main);
        }

        // Copie d'un squadron avec ses tables (sinon l'original et la copie partageraient les mêmes listes)
        private static Squad CopySquad(Squad s)
        {
            Squad c = s.Clone();

            c.BaseAlternative = s.BaseAlternative != null ? new List<string>(s.BaseAlternative) : new List<string>();
            c.Tasks = s.Tasks != null ? new Dictionary<string, bool>(s.Tasks) : new Dictionary<string, bool>();
            c.TasksCoef = s.TasksCoef != null ? new Dictionary<string, double>(s.TasksCoef) : new Dictionary<string, double>();
            c.TasksCoefPourcent = s.TasksCoefPourcent != null ? new Dictionary<string, int>(s.TasksCoefPourcent) : null;
            c.Roster = s.Roster != null ? new Dictionary<string, object>(s.Roster) : null;
            c.Score = s.Score != null ? new Dictionary<string, object>(s.Score) : null;
            c.ScoreLast = s.ScoreLast != null ? new Dictionary<string, object>(s.ScoreLast) : null;
            c.AdditionalProperties = s.AdditionalProperties != null ? new Dictionary<string, object>(s.AdditionalProperties) : new Dictionary<string, object>();
            c.parking_id = s.parking_id != null ? new Dictionary<string, object>(s.parking_id) : new Dictionary<string, object>();
            c.Livery = s.Livery != null ? new Dictionary<int, string>(s.Livery) : new Dictionary<int, string>();
            c.LiveryModex = s.LiveryModex != null ? new Dictionary<int, string>(s.LiveryModex) : new Dictionary<int, string>();

            return c;
        }

        private void DeleteSelected()
        {
            Squad squad = SelectedSquad();
            if (squad == null || _readOnly || _flat == null)
                return;

            string mode = _radioInit.Checked ? "Init" : "Active";
            if (MessageBox.Show("Delete squadron \"" + (squad.DisplayName ?? squad.Name) + "\" from the " + mode + " list?",
                    "Confirm delete", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return;

            _flat.Remove(squad);
            MarkDirty(false);
            AfterChange();
        }

        // ------------------------------------------------------------------
        //  Déplacement : on choisit le squadron, puis on clique sa nouvelle base sur la carte
        // ------------------------------------------------------------------

        private void StartMove()
        {
            Squad squad = SelectedSquad();
            if (squad == null || _readOnly)
                return;

            _moveSquad = squad;
            _chkRuler.Checked = false;
            _lblStatus.Text = "Move \"" + (squad.DisplayName ?? squad.Name) + "\": click the destination base on the map (Esc = cancel).";
            _lblStatus.ForeColor = Color.DarkOrange;
            UpdateButtons();
        }

        private void CancelMove()
        {
            if (_moveSquad == null)
                return;

            _moveSquad = null;
            _lblStatus.ForeColor = SystemColors.ControlText;
            ShowDefaultStatus();
            UpdateButtons();
        }

        private void DoMove(string destination)
        {
            Squad squad = _moveSquad;
            if (squad == null)
                return;

            AirbaseInfo a;
            _airbases.TryGetValue(destination, out a);

            // Camp différent ou base inactive : on prévient avant de faire la modification
            string warning = "";
            string squadSide = (squad.SideString ?? squad.Side ?? "").ToLowerInvariant();
            if (a != null && !string.IsNullOrEmpty(a.Side) && squadSide != "" && !string.Equals(a.Side, squadSide, StringComparison.OrdinalIgnoreCase))
                warning += "This base belongs to the " + a.Side + " side, the squadron is " + squadSide + ".\n";
            if (a != null && a.Inactive)
                warning += "This base is inactive.\n";

            if (warning != "" && MessageBox.Show(warning + "\nMove it anyway?", "Move squadron",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
            {
                CancelMove();
                return;
            }

            squad.Base = destination;

            // Squadron avec plusieurs bases : la nouvelle base passe en tête de la liste (comme dans l'édition)
            if (squad.BaseAlternative != null && squad.BaseAlternative.Count > 0)
            {
                var list = squad.BaseAlternative.Where(b => !string.Equals(b, destination, StringComparison.OrdinalIgnoreCase)).ToList();
                list.Insert(0, destination);
                squad.BaseAlternative = list;
            }

            _moveSquad = null;
            _lblStatus.ForeColor = SystemColors.ControlText;
            ShowDefaultStatus();

            MarkDirty(false);
            _selectedBase = destination;
            AfterChange(squad);
            _view.SelectedName = destination;
        }

        // ------------------------------------------------------------------
        //  Enregistrement
        // ------------------------------------------------------------------

        private void SaveCurrentMode()
        {
            SaveFolder(_radioInit.Checked);
            CommitRunways();
            CommitParks();
            UpdateTitle();
        }

        // Écrit les changements de pistes (réglages dans dce_data_user.ini). false = échec (message déjà affiché)
        private bool CommitRunways()
        {
            if (!Map_RunwayData.HasPending) return true;
            try
            {
                Map_RunwayData.CommitPending();
                _lblStatus.Text = "Runway changes saved.";
                return true;
            }
            catch (Exception ex)
            {
                FormUtils.LogRegister("Map_UI_Form | runways : " + ex);
                MessageBox.Show("The runway changes could not be saved: " + ex.Message, "Save");
                return false;
            }
        }

        // Écrit oob_air_init.lua (Init) ou oob_air.lua (Active), avec la même fonction que le bouton Save de la fenêtre principale
        private bool SaveFolder(bool init)
        {
            if (_readOnly || _edit == null)
                return false;

            string root = Path.Combine(ParamConf.PATH_SavedGames_DCS, @"Mods\tech\DCE\Missions\Campaigns", _campaign);
            string file = init ? Path.Combine(root, "Init", "oob_air_init.lua") : Path.Combine(root, "Active", "oob_air.lua");
            string backup = init ? Path.Combine(root, "Init", "oob_air_init_backup_DTT.lua") : null;

            if (!File.Exists(file))
            {
                MessageBox.Show("The file does not exist yet:\n" + file, "Save");
                return false;
            }

            try
            {
                _edit.modifiedCampaign(file, backup, init ? "Init" : "Active");

                if (init) _dirtyInit = false;
                else _dirtyActive = false;
                TakeSnapshot(init);       // c'est maintenant l'état "sauvé" auquel Quit sans sauver ne revient plus
                return true;
            }
            catch (Exception ex)
            {
                FormUtils.LogRegister("Map_UI_Form | save : " + ex);
                MessageBox.Show("The squadrons could not be saved: " + ex.Message, "Save");
                return false;
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            bool squadsDirty = !_readOnly && (_dirtyInit || _dirtyActive);
            bool runwaysDirty = Map_RunwayData.HasPending;
            bool parksDirty = ParkAnyDirty;
            if ((squadsDirty || runwaysDirty || parksDirty) && (e.CloseReason == CloseReason.UserClosing || e.CloseReason == CloseReason.None))
            {
                var what = new List<string>();
                if (squadsDirty && _dirtyInit) what.Add("Init squadrons");
                if (squadsDirty && _dirtyActive) what.Add("Active squadrons");
                if (runwaysDirty) what.Add("runways");
                if (parksDirty) what.Add("helicopter spots");
                DialogResult answer = MessageBox.Show(
                    "The changes (" + string.Join(" + ", what.ToArray()) + ") have not been saved.\n\n"
                    + "Yes = Save, then quit\nNo = Quit WITHOUT saving (the changes are lost)\nCancel = stay on the map",
                    "Unsaved changes", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Warning);

                if (answer == DialogResult.Cancel)
                {
                    e.Cancel = true;
                    return;
                }
                if (answer == DialogResult.Yes)
                {
                    if (squadsDirty && _dirtyInit && !SaveFolder(true)) { e.Cancel = true; return; }
                    if (squadsDirty && _dirtyActive && !SaveFolder(false)) { e.Cancel = true; return; }
                    if (runwaysDirty && !CommitRunways()) { e.Cancel = true; return; }
                    if (parksDirty && !CommitParks()) { e.Cancel = true; return; }
                }
                else
                {
                    if (squadsDirty) RestoreSnapshot(_dirtyInit, _dirtyActive);
                    _dirtyInit = false;
                    _dirtyActive = false;
                    Map_RunwayData.DiscardPending();
                }
            }
            base.OnFormClosing(e);
        }

        // ------------------------------------------------------------------
        //  Photo des squadrons (pour Quit sans Save)
        // ------------------------------------------------------------------

        // Photo de la version Init ou Active de tous les squadrons (liste de la fenêtre principale)
        private void TakeSnapshot(bool init)
        {
            if (_readOnly || _flat == null) return;

            _snapshot.RemoveAll(x => x.IsInit == init);

            foreach (CampaignSquad cs in _allSquads)
            {
                Squad s = init ? cs.Init : cs.Active;
                if (s == null) continue;
                _snapshot.Add(new SnapEntry { Original = s, Copy = CopySquad(s), IsInit = init, Owner = cs });
            }
        }

        // Remet les squadrons des versions demandées dans l'état de la photo : champs recopiés, ajouts annulés, suppressions annulées
        private void RestoreSnapshot(bool init, bool active)
        {
            if (_readOnly || _flat == null) return;

            try
            {
                var keep = new HashSet<Squad>(_snapshot.Where(x => (x.IsInit && init) || (!x.IsInit && active)).Select(x => x.Original));

                // Squadrons ajoutés depuis la photo (+ Add) : retirés
                foreach (CampaignSquad cs in _allSquads.ToList())
                {
                    bool known = _snapshot.Any(x => ReferenceEquals(x.Owner, cs));
                    if (known) continue;

                    if (init && cs.Init != null) _flat.Remove(cs.Init);
                    if (active && cs.Active != null) _flat.Remove(cs.Active);
                    if (init && active) _allSquads.Remove(cs);
                }

                // Squadrons de la photo : champs remis, et remis dans la liste s'ils avaient été retirés (- Remove)
                foreach (SnapEntry entry in _snapshot)
                {
                    if (entry.IsInit ? !init : !active) continue;

                    RestoreFields(entry.Original, entry.Copy);
                    if (!_flat.Contains(entry.Original)) _flat.Add(entry.Original);
                }
            }
            catch (Exception ex)
            {
                FormUtils.LogRegister("Map_UI_Form | restore : " + ex);
            }
        }

        // Recopie tous les champs (y compris privés, y compris ceux des classes parentes) de la photo vers le squadron
        private static void RestoreFields(Squad target, Squad source)
        {
            for (Type t = typeof(Squad); t != null && t != typeof(object); t = t.BaseType)
            {
                foreach (FieldInfo f in t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                {
                    if (f.IsInitOnly || f.IsLiteral) continue;
                    f.SetValue(target, f.GetValue(source));
                }
            }
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            Map_RunwayData.Ready -= _runwaysReady;
            Map_Units.Changed -= UnitsChanged;
            if (_background != null)
                _background.Dispose();

            // Les onglets OOB de la fenêtre principale reflètent les changements faits ici
            try
            {
                if (!_readOnly)
                    Main_Form.Instance.CampaignGridLeft.RefreshGrids();
            }
            catch { }

            base.OnFormClosed(e);
        }
    }

    // Fenêtre de saisie des pistes. Deux onglets :
    //  - "This base" : une ligne par piste (cap, longueur, où se trouve le point de la base sur la piste)
    //  - "Whole map" : règle de position du point (x, y) de db_airbases pour tout le théâtre
    // La fenêtre ne change rien elle-même : la carte garde le résultat en attente jusqu'au clic sur Save.
    internal class Map_RunwayDialog : Form
    {
        private readonly DataGridView _grid = new DataGridView();
        private readonly double _bx, _by;
        private readonly List<Map_Runway> _initial;
        private readonly string _initialAnchor;
        private readonly RadioButton[] _radios = new RadioButton[5];
        private static readonly string[] AnchorKeys = { "auto", "middle", "start", "east", "west" };
        private readonly CheckBox _chkWeb = new CheckBox();
        private readonly string _initialSource;

        // Onglet "This base"
        public List<Map_Runway> BaseResult;
        public bool BaseChanged, BaseReset;

        // Onglet "Whole map"
        public string AnchorResult = "auto";
        public bool AnchorChanged;
        public string SourceResult = "db";
        public bool SourceChanged;

        // anchor : réglage de la carte ("auto" ou middle / start / east / west) ; status : règle appliquée en ce moment ; source : "db" ou "web"
        public Map_RunwayDialog(string baseName, double baseX, double baseY, List<Map_Runway> current, string theatre, string anchor, string status, string measured, string source)
        {
            _initialSource = source;
            _bx = baseX;
            _by = baseY;
            _initial = current;
            _initialAnchor = anchor;

            Text = "Runways";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            ClientSize = new Size(540, 560);

            var tabs = new TabControl { Dock = DockStyle.Fill };
            var tabBase = new TabPage("This base" + (baseName != null ? " (" + baseName + ")" : ""));
            var tabMap = new TabPage("Whole map" + (string.IsNullOrEmpty(theatre) ? "" : " (" + theatre + ")"));
            tabs.TabPages.Add(tabBase);
            tabs.TabPages.Add(tabMap);

            // ---------- Onglet "This base" ----------
            var info = new Label
            {
                Text = baseName == null ? "Click a base on the map first (then press # again) to type its runways."
                     : "One line per runway.\nHeading: direction of the runway, 0 = north, 90 = east.   Length in metres.\n"
                     + "Base point at: where the point of the base is on the runway. Middle = the middle; East end / West end = one end (north / south end if the runway runs north-south); Custom = a position you set by dragging, kept as it is.",
                Dock = DockStyle.Top, Height = 78, Padding = new Padding(4, 6, 4, 0)
            };

            _grid.Dock = DockStyle.Fill;
            _grid.AllowUserToAddRows = baseName != null;
            _grid.AllowUserToDeleteRows = baseName != null;
            _grid.Enabled = baseName != null;
            _grid.RowHeadersVisible = false;
            _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            _grid.Columns.Add("hdg", "Heading (deg)");
            _grid.Columns.Add("len", "Length (m)");
            var at = new DataGridViewComboBoxColumn { Name = "at", HeaderText = "Base point at", FlatStyle = FlatStyle.Flat };
            at.Items.AddRange("Middle", "East end", "West end", "Custom");
            _grid.Columns.Add(at);

            foreach (Map_Runway r in current)
            {
                double h = ((r.Hdg % 180) + 180) % 180, half = r.Length / 2;
                double dx = (r.X == 0 && r.Y == 0) ? 0 : r.X - baseX, dy = (r.X == 0 && r.Y == 0) ? 0 : r.Y - baseY;
                double ux = Math.Cos(h * Math.PI / 180), uy = Math.Sin(h * Math.PI / 180);       // vers le bout est (ou nord)
                double along = dx * ux + dy * uy, perp = -dx * uy + dy * ux;                        // along > 0 : le milieu de la piste est du côté est du point de la base

                string kind = "Custom";
                if (Math.Abs(perp) < 25)
                {
                    if (Math.Abs(along) < 25) kind = "Middle";
                    else if (Math.Abs(along + half) < 40) kind = "East end";        // milieu = point - moitie vers l'est : le point est au bout est
                    else if (Math.Abs(along - half) < 40) kind = "West end";
                }
                int i = _grid.Rows.Add(Math.Round(h, 1), Math.Round(r.Length), kind);
                _grid.Rows[i].Tag = new[] { dx, dy };
            }

            var reset = new Button { Text = "Back to the original runways of this base", Dock = DockStyle.Bottom, Height = 28, Enabled = baseName != null };
            reset.Click += (s, e) => { BaseReset = true; DialogResult = DialogResult.OK; };

            tabBase.Controls.Add(_grid);
            tabBase.Controls.Add(reset);
            tabBase.Controls.Add(info);

            // ---------- Onglet "Whole map" ----------
            var mapInfo = new Label
            {
                Text = "Where is the point (x, y) of db_airbases on a runway? The answer is the same for all the runways of this map: each runway is moved "
                     + "by HALF ITS OWN LENGTH along its own heading (so a base with several runways of different headings or lengths is handled runway by runway). "
                     + "A base with a setting of its own (tab \"This base\") is left alone.",
                Dock = DockStyle.Top, Height = 78, Padding = new Padding(4, 6, 4, 0)
            };
            string[] texts =
            {
                "Automatic: the rule of the data file for this map, else the measure below, else middle",
                "In the middle of the runway",
                "At the START of the runway (threshold): the runway goes from this point in the direction of its heading",
                "At the east end of the runway (north end if it runs north-south)",
                "At the west end of the runway (south end if it runs north-south)"
            };
            var radioPanel = new Panel { Dock = DockStyle.Top, Height = 160, Padding = new Padding(8, 4, 4, 0) };
            for (int i = texts.Length - 1; i >= 0; i--)
            {
                _radios[i] = new RadioButton { Text = texts[i], Dock = DockStyle.Top, Height = 30, Checked = AnchorKeys[i] == anchor };
                radioPanel.Controls.Add(_radios[i]);
            }
            if (!_radios.Any(r => r.Checked)) _radios[0].Checked = true;

            _chkWeb.Text = "Use the WEB runways (OurAirports) for the whole map. They replace db_airbases and the runway settings of all the bases of this map "
                         + "(cleared when you click Save). A base the web does not know keeps db_airbases.";
            _chkWeb.Checked = source == "web";
            _chkWeb.Dock = DockStyle.Top;
            _chkWeb.Height = 58;
            _chkWeb.Padding = new Padding(8, 4, 4, 0);

            var measure = new Label
            {
                Text = status + "\n\n" + (string.IsNullOrEmpty(measured) ? "Measure: not available yet (it comes with the web map calibration, which needs the internet)." : "Measure: " + measured)
                     + "\n\nHeadings of db_airbases are read in degrees (0 = north, 90 = east). Check it on the map: the drawn runway must lie along the real one.",
                Dock = DockStyle.Fill, Padding = new Padding(4, 8, 4, 0)
            };
            tabMap.Controls.Add(measure);
            tabMap.Controls.Add(_chkWeb);
            tabMap.Controls.Add(radioPanel);
            tabMap.Controls.Add(mapInfo);

            // ---------- Boutons ----------
            var bottom = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 40, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(6) };
            var ok = new Button { Text = "OK", Width = 80 };
            var cancel = new Button { Text = "Cancel", Width = 80, DialogResult = DialogResult.Cancel };
            ok.Click += (s, e) => OnOk(baseName != null);
            bottom.Controls.Add(ok);
            bottom.Controls.Add(cancel);

            Controls.Add(tabs);
            Controls.Add(bottom);
            AcceptButton = ok;
            CancelButton = cancel;
        }

        private void OnOk(bool hasBase)
        {
            for (int i = 0; i < _radios.Length; i++)
                if (_radios[i].Checked) AnchorResult = AnchorKeys[i];
            AnchorChanged = AnchorResult != _initialAnchor;
            SourceResult = _chkWeb.Checked ? "web" : "db";
            SourceChanged = SourceResult != _initialSource;

            if (hasBase)
            {
                _grid.EndEdit();
                var list = new List<Map_Runway>();
                foreach (DataGridViewRow row in _grid.Rows)
                {
                    if (row.IsNewRow) continue;
                    double h, l;
                    string sh = Convert.ToString(row.Cells[0].Value) ?? "", sl = Convert.ToString(row.Cells[1].Value) ?? "";
                    if (!double.TryParse(sh.Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out h) || h < 0 || h > 360
                        || !double.TryParse(sl.Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out l) || l < 50 || l > 10000)
                    {
                        MessageBox.Show("Line " + (row.Index + 1) + ": heading must be 0 to 360 and length 50 to 10000 m.", "Runways");
                        return;
                    }

                    string at = Convert.ToString(row.Cells[2].Value) ?? "Middle";
                    double hm = ((h % 180) + 180) % 180;
                    double ux = Math.Cos(hm * Math.PI / 180), uy = Math.Sin(hm * Math.PI / 180), half = l / 2;
                    double dx = 0, dy = 0;
                    if (at == "East end") { dx = -half * ux; dy = -half * uy; }          // le point est au bout est : le milieu est vers l'ouest
                    else if (at == "West end") { dx = half * ux; dy = half * uy; }
                    else if (at == "Custom")
                    {
                        double[] t = row.Tag as double[];
                        if (t != null) { dx = t[0]; dy = t[1]; }
                    }
                    list.Add(new Map_Runway { Hdg = h, Length = l, X = _bx + dx, Y = _by + dy });
                }

                // Rien n'a changé : on ne marque rien comme modifié
                BaseChanged = !SameRunways(list, _initial);
                BaseResult = list;
                if (list.Count == 0 && BaseChanged)
                {
                    MessageBox.Show("Add at least one runway, or use the button \"Back to the original runways\".", "Runways");
                    return;
                }
            }
            DialogResult = DialogResult.OK;
        }

        private static bool SameRunways(List<Map_Runway> a, List<Map_Runway> b)
        {
            if (a.Count != b.Count) return false;
            for (int i = 0; i < a.Count; i++)
            {
                double bx = (b[i].X == 0 && b[i].Y == 0) ? 0 : b[i].X, by = (b[i].X == 0 && b[i].Y == 0) ? 0 : b[i].Y;
                if (Math.Abs(a[i].Hdg - b[i].Hdg) > 0.6 || Math.Abs(a[i].Length - b[i].Length) > 1.5) return false;
                if (!(b[i].X == 0 && b[i].Y == 0) && (Math.Abs(a[i].X - bx) > 45 || Math.Abs(a[i].Y - by) > 45)) return false;      // tolérance : "Middle" est pris pour milieu à 25 m près
            }
            return true;
        }
    }

    // Bandeau d'une section de la colonne de gauche de la carte (météo, plus tard presets radio et datalink) : un clic le replie sur cette seule ligne
    // Panneau sans scintillement : double tampon + WS_EX_COMPOSITED (les contrôles enfants se dessinent d'un bloc)
    internal class Map_UI_FastPanel : Panel
    {
        public Map_UI_FastPanel() { DoubleBuffered = true; ResizeRedraw = true; }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= 0x02000000;
                return cp;
            }
        }
    }

    // Tableau sans scintillement (double tampon) : le cadre des avions du vol est reconstruit à chaque ajout / retrait d'avion
    internal class Map_UI_FastTable : TableLayoutPanel
    {
        public Map_UI_FastTable() { DoubleBuffered = true; }
    }

    internal class Map_UI_Section : Control
    {
        private bool _expanded = true;
        private string _summary = "";
        public string Title = "";
        public event EventHandler Toggled;

        private static readonly Font FTitle = new Font("Segoe UI", 8.5f, FontStyle.Bold);
        private static readonly Font FSym = new Font("Segoe UI Symbol", 9f);

        public Map_UI_Section()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Cursor = Cursors.Hand;
        }

        public bool Expanded
        {
            get { return _expanded; }
            set
            {
                if (_expanded == value) return;
                _expanded = value;
                Invalidate();
                if (Toggled != null) Toggled(this, EventArgs.Empty);
            }
        }

        public string Summary
        {
            get { return _summary; }
            set { _summary = value ?? ""; Invalidate(); }
        }

        protected override void OnClick(EventArgs e)
        {
            base.OnClick(e);
            Expanded = !Expanded;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            using (var fill = new SolidBrush(Color.FromArgb(33, 41, 60))) g.FillRectangle(fill, 0, 0, Width, Height);
            using (var pen = new Pen(Color.FromArgb(62, 78, 112))) g.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);

            TextRenderer.DrawText(g, _expanded ? "\u25BE" : "\u25B8", FSym, new Rectangle(4, 0, 18, Height), Color.FromArgb(120, 225, 150), TextFormatFlags.VerticalCenter | TextFormatFlags.Left);
            TextRenderer.DrawText(g, Title, FTitle, new Rectangle(24, 0, Width - 28, Height), Color.FromArgb(225, 230, 240), TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPadding);

            int tw = TextRenderer.MeasureText(g, Title, FTitle).Width;
            int sx = 24 + tw + 10;
            if (_summary != "" && sx < Width - 20)
                TextRenderer.DrawText(g, _summary, Font, new Rectangle(sx, 0, Width - sx - 6, Height), Color.FromArgb(140, 150, 170), TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
        }
    }
}
