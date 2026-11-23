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
    internal partial class Map_UI_Form : Form
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
        private Map_UI_Section _secOpts;            // bandeau de la section "Debug and flight plans"
        private Panel _optsPanel;
        private CheckBox _chkMissionDebug, _chkTrPlayers, _chkTrAI;
        private bool _syncOpts;
        private Map_UI_Section _secStats;           // bandeau de la section "Mission content" (groupes, unités, prune, modules), sous la météo
        private Panel _statsPanel;
        private RichTextBox _statsBox;              // texte sélectionnable (copier / coller)
        private int _statsWantH = 100;
        private Map_UI_FlightList _flightList;
        // Liste des vols : bandeau repliable + liste qui s'agrandit PAR-DESSUS les autres blocs (sans les pousser) + filtres
        private const int FlightListH = 85;             // hauteur de la liste repliée (comme avant)
        private const int FlightFilterH = 58;           // hauteur de la barre de filtres (visible quand la liste est agrandie)
        private Map_UI_Section _secFlights;
        private Panel _flightSlot, _flightPop, _flightFilterBar;
        private ComboBox _cmbFlPack, _cmbFlType, _cmbFlKind;
        private TextBox _txtFlName;
        private bool _flightPopOpen, _filterSync;
        private int _popInTicks, _popOutTicks;
        private System.Windows.Forms.Timer _popTimer;
        private Map_MissionData _missionData;
        private bool _syncFlightList, _syncWp;
        private Map_UI_WaypointList _wpList;
        private Map_UI_Card _wpCard;
        private Label _wpTitle;
        private ComboBox _cmbStart;             // CampaignMaker, waypoint 0 : type de départ (liste déroulante dans la ligne "Start type" de la fiche)
        private Button _btnAddWp, _btnDelWp;        // CampaignMaker : ajouter / supprimer un waypoint du vol

        private Panel _flightHost;
        private Map_Flight _shownFlight;
        private Map_UI_SideToggle _tglLeft, _tglRight;               // boutons de la barre du haut : colonne de gauche (outils + météo) / panneau de droite
        private Panel _panelRight, _wptBar;
        private Splitter _splitRight;
        private TabPage _tabBrief;
        private RichTextBox _briefBox;
        private Button _btnDatacard;
        private string _briefPart = "";
        private ComboBox _briefMode;       // 0 = ce vol, 1 = briefing du camp (Blue / Red task), 2 = description de la mission
        private CheckBox _chkGroundHidden;       // éléments des groupes "hidden" du fichier mission (debug / CampaignMaker seulement)
        private CheckBox _chkGround, _chkGroundBlue, _chkGroundRed, _chkGroundNames;     // éléments au sol : tous / bleus / rouges / noms de groupes
        private CheckBox _chkBlueFlights, _chkRedFlights;     // CampaignMaker : afficher / masquer un camp
        private Panel _baseHost;
        private const int SquadListHeight = 230;
        private Label _lblInfo;
        private Label _lblStatus;
        private ListView _listSquads;
        private ListBox _listNoPos;
        private Dictionary<string, string> _aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private Button _btnIssues;                  // "Mission check" : pastille en haut à droite de la carte (nombre de problèmes), réduite au départ
        private Panel _issuesPanel;
        private RichTextBox _issuesBox;
        private List<Map_Issue> _fileIssues = new List<Map_Issue>();       // problèmes trouvés dans le fichier mission (lus une fois par chargement)
        private CheckBox _chkAaa, _chkLabels;          // zones AAA hachurées (trigger zones AAA_ZONE_*) / noms écrits sur la carte
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
        private readonly Dictionary<string, bool> _pendingCamp = new Dictionary<string, bool>();           // variable de campL (camp_status.lua du .miz) -> valeur voulue, écrite au clic sur Save modification
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
            _view.GroundDismissed += () => { if (_groundInfo != null && !_groundInfo.IsDisposed && _groundInfo.Visible) _groundInfo.Hide(); };

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

                _secStats = new Map_UI_Section { Title = "Mission content", Size = new Size(410, 24), Visible = false, Expanded = true };
                _secStats.Toggled += (s, e) => PlaceOverlays();
                _view.Controls.Add(_secStats);
                _secStats.BringToFront();
                _statsPanel = new Panel { Size = new Size(410, 100), Visible = false, BackColor = Color.White, BorderStyle = BorderStyle.FixedSingle };
                _statsBox = new RichTextBox { ReadOnly = true, Dock = DockStyle.Fill, BorderStyle = BorderStyle.None, BackColor = Color.White, Font = new Font("Consolas", 8.5f), ForeColor = Color.FromArgb(30, 30, 30), WordWrap = false, ScrollBars = RichTextBoxScrollBars.Vertical, DetectUrls = false, TabStop = false };
                _statsPanel.Padding = new Padding(6, 4, 2, 4);
                _statsPanel.Controls.Add(_statsBox);
                _view.Controls.Add(_statsPanel);
                _statsPanel.BringToFront();

                _secOpts = new Map_UI_Section { Title = "Debug and flight plans", Size = new Size(410, 24), Visible = false, Expanded = false };
                _secOpts.Toggled += (s, e) => PlaceOverlays();
                _view.Controls.Add(_secOpts);
                _secOpts.BringToFront();
                _optsPanel = new Panel { Size = new Size(410, 96), Visible = false, BackColor = Color.White, BorderStyle = BorderStyle.FixedSingle };
                _chkMissionDebug = new CheckBox { Text = "Debug mode of this mission", AutoSize = true, Location = new Point(10, 9), Enabled = false };
                _chkTrPlayers = new CheckBox { Text = "Record the flights of the players", AutoSize = true, Location = new Point(10, 36), Enabled = false };
                _chkTrAI = new CheckBox { Text = "Record the flights of the AI", AutoSize = true, Location = new Point(10, 63), Enabled = false };
                var optTip = new ToolTip { AutoPopDelay = 25000, InitialDelay = 300, ReshowDelay = 100, ShowAlways = true };
                optTip.SetToolTip(_chkMissionDebug, "DEBUG MODE OF THE MISSION\nThe [\"debug\"] value of campL in the camp_status.lua stored inside the .miz (l10n/DEFAULT). It is written in the .miz when you click \"Save modification\" (CampaignMaker). Grey: this .miz has no camp_status.lua.");
                optTip.SetToolTip(_chkTrPlayers, "TRACES OF THE PLAYERS (in DCS)\nThe [\"" + Map_MissionStats.KeyTracePlayers + "\"] value of campL in the camp_status.lua stored inside the .miz. It is written in the .miz when you click \"Save modification\" (CampaignMaker). It does not change this map.");
                optTip.SetToolTip(_chkTrAI, "TRACES OF THE AI (in DCS)\nThe [\"" + Map_MissionStats.KeyTraceAI + "\"] value of campL in the camp_status.lua stored inside the .miz. It is written in the .miz when you click \"Save modification\" (CampaignMaker). It does not change this map.");
                _chkMissionDebug.CheckedChanged += (s, e) => CampBoolChanged(_chkMissionDebug, Map_MissionStats.KeyDebug);
                _chkTrPlayers.CheckedChanged += (s, e) => CampBoolChanged(_chkTrPlayers, Map_MissionStats.KeyTracePlayers);
                _chkTrAI.CheckedChanged += (s, e) => CampBoolChanged(_chkTrAI, Map_MissionStats.KeyTraceAI);
                _optsPanel.Controls.Add(_chkMissionDebug);
                _optsPanel.Controls.Add(_chkTrPlayers);
                _optsPanel.Controls.Add(_chkTrAI);
                _view.Controls.Add(_optsPanel);
                _optsPanel.BringToFront();

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

                // Contrôle du fichier mission (CampaignMaker, qui est le seul à pouvoir le modifier) : réduit au départ, juste un chiffre et un panneau d'avertissement
                if (ParamConf.UserLevel == UserLevel.CampaignMaker)
                {
                    _btnIssues = new Button { Size = new Size(70, 28), FlatStyle = FlatStyle.Flat, TabStop = false, Font = new Font("Segoe UI Symbol", 10f, FontStyle.Bold), Text = "\u2714 0" };
                    _btnIssues.FlatAppearance.BorderColor = Color.Silver;
                    _btnIssues.Click += (s, e) => { _issuesPanel.Visible = !_issuesPanel.Visible; PlaceIssues(); };

                    _issuesPanel = new Panel { Size = new Size(430, 230), Visible = false, BackColor = Color.White, BorderStyle = BorderStyle.FixedSingle };
                    _issuesBox = new RichTextBox { ReadOnly = true, ScrollBars = RichTextBoxScrollBars.Vertical, Dock = DockStyle.Fill, Font = new Font("Consolas", 8.5f), BackColor = Color.White, BorderStyle = BorderStyle.None, DetectUrls = false };
                    var issuesTitle = new Label { Dock = DockStyle.Top, Height = 24, Text = "  Mission check (before you save anything)", Font = new Font("Segoe UI", 9f, FontStyle.Bold), TextAlign = ContentAlignment.MiddleLeft, BackColor = Color.FromArgb(255, 244, 214) };
                    _issuesPanel.Controls.Add(_issuesBox);
                    _issuesPanel.Controls.Add(issuesTitle);
                    _view.Controls.Add(_issuesPanel);
                    _view.Controls.Add(_btnIssues);
                    _view.Resize += (s, e) => PlaceIssues();
                    PlaceIssues();
                }
            }

            // Chaque bouton de commande : un titre court (en majuscules) puis l'explication, affichés quand la souris reste dessus
            Action<Control, string, string> hint = (c, title, text) => tip.SetToolTip(c, title.ToUpper() + "\n" + text);
            if (_btnIssues != null)
                hint(_btnIssues, "Mission check", "Problems found in the mission file BEFORE you change anything (and in the changes waiting for Save modification). Red number = duplicate ids (DCS can not use them), orange = duplicate names or tail numbers. Click to open or close the list. Problems already in the file do not block a change that does not make them worse.");

            // ================= Bande de gauche : outils généraux + wargame (zones, éléments au sol) =================
            var strip = new Panel { Dock = DockStyle.Left, Width = 38, BackColor = Color.FromArgb(243, 243, 243) };
            int sy = 6;
            Func<int> nextY = () => { int r = sy; sy += 38; return r; };
            Action gapY = () => sy += 16;


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

            _chkAaa = MakeToggle("\u2739", nextY());
            _chkAaa.Checked = true;
            _chkAaa.CheckedChanged += (s, e) => _view.SetDrawingsVisible("aaa", _chkAaa.Checked && _chkAaa.Enabled);
            hint(_chkAaa, "AAA zones", "Show or hide the AAA zones (trigger zones AAA_ZONE_BLUE_* / AAA_ZONE_RED_* of the mission). Zones that overlap are drawn as ONE hatched area to avoid. The colour is the side that DEFENDS the zone.");
            strip.Controls.Add(_chkAaa);

            _chkLabels = MakeToggle("Tt", nextY());
            _chkLabels.Font = new Font("Segoe UI", 11f, FontStyle.Bold);
            _chkLabels.Checked = true;
            _chkLabels.CheckedChanged += (s, e) => _view.ShowLabels = _chkLabels.Checked;
            hint(_chkLabels, "Labels", "Show or hide ALL the names written on the map: bases, ground groups, zones, texts of the mission drawings. Untick for a clean map.");
            strip.Controls.Add(_chkLabels);

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
                _chkGroundHidden = MakeToggle("H", nextY());
                _chkGroundHidden.Font = new Font("Segoe UI", 11f, FontStyle.Bold);
                hint(_chkGroundHidden, "Hidden groups", "Also show the ground groups that are marked hidden in the mission file (['hidden'] = true: DCE hides them on the F10 map).\nOnly for CampaignMaker, or when Debug is on in conf_mod.lua; grey otherwise. They use the same blue / red icons as the other ground elements.");
                strip.Controls.Add(_chkGroundHidden);
                _chkGroundHidden.CheckedChanged += (s, e) => ShowHiddenGroundChanged();

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

                _chkPlans = MakeToggle("✈", 0);
                _chkPlans.CheckedChanged += (s, e) => _view.SetDrawingsVisible("plans", _chkPlans.Checked);
                hint(_chkPlans, "Flight plans", "Show or hide the flight plans of all the aircraft (thick = players, medium = their package, thin = others). Markers: circle = standard, square = IP / station, star = target, diamond = split / egress / join / attack, triangle = airfield. Click a flight to highlight it (its waypoint numbers appear); click again at the same place to pick the next one. CTRL + click adds / removes a flight to the selection. Right-click a flight plan = take it out of the selection (at the start all flights are selected). Click on an empty place = all flights again.");

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
            Panel top;
            Panel topBar = Map_UI_SideToggle.Wrap(38, out top, out _tglLeft, out _tglRight);       // les 2 boutons de colonne, comme sur les autres cartes
            _tglLeft.CheckedChanged += (s, e) => { strip.Visible = _tglLeft.Checked; PlaceOverlays(); };
            _tglRight.CheckedChanged += (s, e) =>
            {
                _panelRight.Visible = _tglRight.Checked;
                if (_splitRight != null) _splitRight.Visible = _tglRight.Checked;
            };
            hint(_tglLeft, "Left column", "Show or hide the left part of the map: the column of tools (ruler, zoom, drawings, wargame, ground elements) and the sections laid on the map (weather and sun, radio presets).");
            hint(_tglRight, "Right column", "Show or hide the right part: flights, waypoints, briefing and bases (squadrons, edit and move buttons). Hidden = a bigger map.");
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
            Controls.Add(topBar);

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
    }
}
