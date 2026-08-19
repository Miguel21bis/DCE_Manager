using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using DCE_Manager.Parameters;
using DCE_Manager.Utils;

namespace DCE_Manager
{
    // Pilote un FirstMission.bat / SkipMission.bat (ScriptsMod) en tâche de fond : aucune
    // fenêtre console visible, tout se passe dans cette Form.
    //
    // Fonctionnement : ScriptsModBatParser déduit directement la commande luae.exe + le script
    // .lua à lancer (structure fixe, seul Init\path.bat change d'un PC à l'autre). On lance cette
    // commande nous-mêmes, avec entrée/sortie redirigées (ProcessConsoleBridge, tuyaux simples).
    //
    // Affichage : par défaut, on ne montre PAS le flot brut de la console (trop verbeux). À la
    // place, lblStage affiche un texte fixe décrivant le type d'écran courant, et
    // panelQuickOptions propose des contrôles adaptés au type de question posée :
    //   - menu à un seul caractère ("[X] Label" ou "x - Label" ou "x. Label") -> boutons empilés
    //   - demande d'un nombre ("Select number of ...")            -> boutons 1-8
    //   - composition d'un vol ("Select your flight" + table)     -> 3 contrôles (nb/ID/tâche)
    //   - choix parmi des paquets de vol déjà générés              -> tout affiché d'un coup,
    //     sélection multiple, un seul bouton "Confirm all flights" (voir BuildAllFlightSectionsUI)
    // Le détail technique brut reste consultable via "Show technical details".
    //
    // Convention : tout ce qui est visible/manipulé par l'utilisateur (labels, boutons, titres,
    // messages) est en anglais. Les commentaires et noms internes restent en français.
    public class ScriptsModRunner_Form : Form
    {
        private readonly ProcessConsoleBridge _bridge = new ProcessConsoleBridge();
        private readonly List<string> _pendingLines = new List<string>();
        private readonly ToolTip _toolTip = new ToolTip();

        private readonly string _batPath;
        private readonly string _luaScriptName; // null = déduit du nom du .bat (convention BAT_xxx.lua)
        private readonly string _workingDirectory;
        private readonly string _campaignName;
        private bool _errorWarningShown = false;
        private bool _userCancelled = false;
        private Button btnForceExit;

        private bool _showTechnicalLog = false; // caché par défaut, "Show technical details" pour l'afficher
        private const int TechnicalLogHeight = 260;

        // Préfixe de la ligne technique ajoutée dans BAT_FirstMission.lua / BAT_SkipMission.lua.
        // Format : ##DCEM_AC##side|id|type|base|squadron|CODE1:code1,CODE2:code2,...
        private const string AircraftMarkerPrefix = "##DCEM_AC##";
        private const string FlightOptionMarkerPrefix = "##DCEM_FLIGHTOPT##";
        private const string FlightWishMarkerPrefix = "##DCEM_FLIGHTWISH##";
        private const string FlightPreviewDoneMarkerPrefix = "##DCEM_FLIGHTPREVIEW_DONE##";

        // Vrai entre le moment où on a vu "Select your flight" et l'envoi de la réponse.
        // Sert à savoir qu'il faut reconstruire les contrôles à chaque ligne ##DCEM_AC## reçue,
        // puisque le tableau arrive APRÈS l'annonce du prompt, pas avant.
        private bool _flightPromptPending = false;

        // Vrai une fois que le panel FlightBuilder a été construit pour la sélection en cours.
        // Évite de reconstruire tous les contrôles à chaque ligne ##DCEM_AC## reçue.
        private bool _flightBuilderControlsBuilt = false;
        private bool _oobAirLoaded = false; // pour ne charger oob_air qu'une fois par session

        private int _selectedFlightCount = 1;
        private readonly List<Button> _flightCountButtons = new List<Button>();

        private TableLayoutPanel _table;
        private Label lblStatus;
        private Label lblStage;
        private ProgressBar progressRunning;
        private Button btnToggleLog;
        private TextBox txtOutput;
        private RichTextBox txtDebrief;
        private Panel pnlDebrief;
        private FlowLayoutPanel pnlDebriefActions;
        private FlowLayoutPanel panelQuickOptions;
        private TextBox txtInput;
        private Button btnSend;
        private Button btnClose;
        private Button btnFallback;
        private Button btnViewDebrief;
        private Button btnStop;


        private string _debriefText;

        // "[S] Singleplayer"  ->  clé = "S", libellé = "Singleplayer"
        private static readonly Regex RegexBracket = new Regex(@"^\[(.)\]\s+(.+)$");
        // "s - skip mission"  ->  clé = "s", libellé = "skip mission"
        private static readonly Regex RegexDash = new Regex(@"^([a-zA-Z0-9])\s*-\s*(.+)$");
        // "1. targets in the RED camp"  ->  clé = "1", libellé = "targets in the RED camp"
        private static readonly Regex RegexDot = new Regex(@"^(\d+)\.\s+(.+)$");

        private static readonly Regex RegexLuaCrashSignature =
                new Regex(@"\.lua:\d+:|stack traceback", RegexOptions.IgnoreCase);

        // Légende "STR=Strike  ESC=Escort ..." pour afficher un libellé complet dans le combo tâche.
        private static readonly Regex RegexLegendPair =
            new Regex(@"\b([A-Z]{2,5})=([A-Za-z]+)\b");

        private enum InputMode { QuickOptions, Numeric, FlightBuilder, FlightOptionSelect, TargetSelect, YesNo, DebriefAccept }
        private InputMode _inputMode = InputMode.QuickOptions;

        private const string FatalMarkerPrefix = "##DCEM_FATAL##";
        private const string HeaderMarkerPrefix = "##DCEM_HEADER##";
        private const string TimeMarkerPrefix = "##DCEM_TIME##";
        private const string DayNightMarkerPrefix = "##DCEM_DAYNIGHT##";
        private const string TargetMarkerPrefix = "##DCEM_TARGET##";
        private const string CsarTargetMarkerPrefix = "##DCEM_CSARTARGET##";
        private const string MissionDateMarkerPrefix = "##DCEM_MISSIONDATE##";
        private const string DebriefTextPathMarkerPrefix = "##DCEM_DEBRIEFTEXT_PATH##";
        private const string CycleMarkerPrefix = "##DCEM_CYCLE##";
        private string _headerCycle = "";
        private string _headerCurrentMissionDate = "";

        private const string BugListStartMarkerPrefix = "##DCEM_BUGLIST_START##";
        private const string BugListItemMarkerPrefix = "##DCEM_BUGLISTITEM##";
        private const string BugListEndMarkerPrefix = "##DCEM_BUGLIST_END##";
        private readonly List<string> _bugListItems = new List<string>();
        private bool _bugListCapturePending = false;

        private const string OutcomeMarkerPrefix = "##DCEM_OUTCOME##";
        private string _outcomeStatus = "";   // "SUCCESS" / "FAILED" / "STOPPED" / "ENDCAMPAIGN" - vide si jamais reçu
        private string _outcomeMessage = "";

        private Panel panelHeader;
        private Label lblHeaderTitle;
        private Label lblHeaderVersion;
        private Label lblHeaderSubtitle;
        private Label lblHeaderExtra;
        private Label lblHeaderBadge;
        private string _headerCampaignTitle = "", _headerVersion = "", _headerAircraft = "",
            _headerSquadron = "", _headerCountry = "", _headerDebug = "", _headerDayNight = "", _headerDate = "";
        private string _headerStartDate = "";
        private string _headerMetar = "";

        // ---- Checkboxes Debug/ShowFlight (lues/écrites dans conf_mod.lua) ----
        private ConfModDynamicData _confModData;
        private CheckBox chkDebugMode;
        private CheckBox chkShowFlight;

        private readonly List<TargetOption> _targetOptions = new List<TargetOption>();
        private bool _targetListPending = false;
        private ListBox _lstTargets;
        private TextBox _txtTargetFilter;

        private int _pendingFlightCount = 0;
        private readonly Queue<string> _pendingFlightAnswers = new Queue<string>();
        private bool _multiFlightComposerActive = false; // vrai le temps d'envoyer le lot de vols préparés
        private readonly List<FlightComposerRow> _composerRows = new List<FlightComposerRow>();
        private Button _btnConfirmComposer;
        private Label _lblComposerWarning;

        private const string PromptMarkerPrefix = "##DCEM_PROMPT##";
        private bool _flightOptionUIBuilt = false;


        // ---- Sélection de paquets de vol déjà générés ("tout d'un coup") ----

        // Une section = un vol demandé par le joueur (creaClientFlight[k] côté Lua), avec la
        // liste de tous les paquets compatibles pour ce vol précis.
        private class FlightWishSection
        {
            public int Nb;
            public string Type;
            public string Side;
            public string Task;
            public CheckBox OverrideCheckBox;
            public readonly List<FlightOptionEntry> Entries = new List<FlightOptionEntry>();
            public readonly Dictionary<int, Button> ButtonsByIndex = new Dictionary<int, Button>();
            public readonly List<FlightOptionEntry> Selected = new List<FlightOptionEntry>();
            public Label QuotaLabel;
        }

        private readonly List<FlightWishSection> _flightWishSections = new List<FlightWishSection>();
        private FlightWishSection _currentBuildingSection;
        // Un même paquet physique (même index Lua) peut apparaître dans plusieurs sections si
        // plusieurs vols demandés partagent le même type d'avion - on le retrouve ici pour
        // pouvoir le griser partout ailleurs dès qu'il est choisi une fois.
        private readonly Dictionary<int, List<Button>> _flightOptionButtonsByGlobalIndex = new Dictionary<int, List<Button>>();
        private Button _btnConfirmAllFlights;

        // Vrai le temps d'envoyer, un par un, tous les choix déjà décidés par l'utilisateur -
        // la vraie boucle Lua continue de fonctionner normalement pendant ce temps, on ignore
        // juste tout ce qu'elle réaffiche puisqu'on a déjà tout montré à l'avance.
        private bool _multiFlightOptionActive = false;
        private readonly Queue<string> _pendingFlightOptionAnswers = new Queue<string>();

        // Une ligne de la table d'avions, une fois parsée.
        private class AircraftEntry
        {
            public string Id;
            public string AircraftType;
            public string Base;
            public string Squadron;
            public string Side;
            // code de tâche (1 ou 2 lettres, ex "s" ou "cs") -> libellé complet si connu via la
            // légende ("Strike"), sinon le code 3 lettres tel quel ("STR").
            public Dictionary<string, string> Tasks = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        // Entrée factice utilisée comme "ligne vide" en tête de chaque combo Bleu/Rouge - permet de
        // revenir explicitement à "aucun avion choisi" pour cette ligne. Id reste null exprès : c'est
        // ce qui la distingue d'un vrai avion partout où on la teste (entry.Id == null).
        private static readonly AircraftEntry EmptyAircraftEntry = new AircraftEntry();

        private class TaskOption
        {
            public string Code;
            public string Label;
            public override string ToString() { return Label; }
        }

        private class FlightOptionEntry
        {
            public int Index;
            public bool Selectable;
            public string Nb;
            public string Base;
            public string AircraftType;
            public string GroupName;
            public string Target;
            public int ForcedNb;                    // valeur affichée/éditable dans le NumericUpDown (1 à 4)
            public NumericUpDown ForcedNbControl;    // référence pour la verrouiller/déverrouiller depuis RefreshAllFlightSectionsState
        }

        private class TargetOption
        {
            public int Index;
            public string Side;
            public string Name;
            public string AlivePct; // vide pour une cible CSAR
            public string Priority; // vide pour une cible CSAR
            public string Mgrs;     // rempli seulement pour une cible CSAR

            public override string ToString()
            {
                string label = Index + " - " + Name;
                if (!string.IsNullOrEmpty(Priority))
                    label += "  (" + AlivePct + "%, priority X" + Priority + ")";
                else if (!string.IsNullOrEmpty(Mgrs))
                    label += "  (" + Mgrs + ")";
                return label;
            }
        }

        private class FlightComposerRow
        {
            public int SelectedCount = 0; // 0 = pas encore choisi par l'utilisateur
            public readonly List<Button> CountButtons = new List<Button>();
            public ComboBox CmbAircraftBlue;
            public ComboBox CmbAircraftRed;
            public ComboBox CmbTask;

            // Renvoie l'avion actuellement choisi, quel que soit le combo (Bleu ou Rouge) où il a été pris.
            // Grâce à la sélection exclusive (voir ShowMultiFlightComposer), un seul des deux est jamais rempli.
            public AircraftEntry SelectedAircraft
            {
                get
                {
                    var blue = CmbAircraftBlue.SelectedItem as AircraftEntry;
                    if (blue != null && blue.Id != null) return blue;

                    var red = CmbAircraftRed.SelectedItem as AircraftEntry;
                    if (red != null && red.Id != null) return red;

                    return null;
                }
            }
        }

        private readonly Dictionary<string, AircraftEntry> _aircraftById =
            new Dictionary<string, AircraftEntry>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> _taskLegend =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        private ComboBox _cmbAircraftId;
        private ComboBox _cmbTaskCode;

        public ScriptsModRunner_Form(string batPath, string workingDirectory, string campaignName, string luaScriptName = null)
        {
            _batPath = batPath;
            _luaScriptName = luaScriptName;
            _workingDirectory = workingDirectory;
            _campaignName = campaignName;
            _confModData = new ConfModLoader().Load(campaignName);

            Text = "ScriptsMod - " + campaignName + " (" + Path.GetFileNameWithoutExtension(batPath) + ")";
            try
            {
                Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            }
            catch { /* pas grave si indisponible, la fenêtre garde l'icône par défaut */ }

            Width = 1060;
            Height = 760;
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = false;

            BuildLayout();

            _bridge.OutputReceived += OnOutputReceived;
            _bridge.ProcessExited += OnProcessExited;

            Load += (s, e) => { RefreshHeaderLabel(); StartInteractiveSession(); };

            FormClosing += ScriptsModRunner_Form_FormClosing;
        }

        private void StartInteractiveSession()
        {
            ScriptsModBatParser.LaunchInfo launchInfo = ScriptsModBatParser.Parse(_batPath, _workingDirectory, _luaScriptName);

            if (launchInfo == null)
            {
                lblStatus.Text = "Init\\path.bat not found or unexpected format";
                AppendOutputLine("[ERROR] Unable to read " + Path.Combine(_workingDirectory, "Init", "path.bat"));
                btnFallback.Visible = true;
                btnClose.Enabled = true;
                return;
            }

            if (!File.Exists(launchInfo.ExePath))
            {
                lblStatus.Text = "Executable not found";
                AppendOutputLine("[ERROR] " + launchInfo.ExePath + " not found.");
                btnFallback.Visible = true;
                btnClose.Enabled = true;
                return;
            }

            try
            {
                // Signale au script Lua qu'il est piloté par DCE_Manager : il peut alors imprimer ses
                // lignes techniques (##DCEM_AC## etc.) en plus de l'affichage humain habituel. Un
                // lancement manuel (double-clic sur FirstMission.bat) n'aura jamais cette variable,
                // donc n'affichera jamais ces lignes techniques.
                launchInfo.EnvironmentVariables["DCEM_MACHINE_MODE"] = "1";

                _bridge.Start(launchInfo.ExePath, launchInfo.Arguments, _workingDirectory, launchInfo.EnvironmentVariables);
                lblStatus.Text = "Running...";
                progressRunning.Visible = true;
            }
            catch (Exception ex)
            {
                lblStatus.Text = "Launch error";
                AppendOutputLine("[ERROR] " + ex.GetType().Name + ": " + ex.Message);
                btnFallback.Visible = true;
                btnClose.Enabled = true;
            }
        }

        // Repli : relance le .bat "à l'ancienne" (fenêtre console visible), pour ne pas rester
        // bloqué si Init\path.bat n'est pas trouvé/lisible.
        private void RunFallbackVisible()
        {
            try
            {
                Process.Start(new ProcessStartInfo()
                {
                    FileName = _batPath,
                    WorkingDirectory = _workingDirectory,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show("Unable to launch " + _batPath + ":\r\n" + ex.Message);
            }

            Close();
        }

        private Panel BuildHeaderPanel()
        {
            panelHeader = new Panel() { Dock = DockStyle.Fill, BackColor = Color.WhiteSmoke, BorderStyle = BorderStyle.FixedSingle };
            var picLogo = new PictureBox()
            {
                Location = new Point(8, 8),
                Size = new Size(48, 48),
                SizeMode = PictureBoxSizeMode.Zoom
            };
            picLogo.Image = Properties.Resources.icons8_paramètres_50_blue;
            panelHeader.Controls.Add(picLogo);

            lblHeaderTitle = new Label()
            {
                AutoSize = true,
                Location = new Point(64, 8),
                Font = new Font(Font.FontFamily, 11F, FontStyle.Bold)
            };
            lblHeaderVersion = new Label()
            {
                AutoSize = true,
                Location = new Point(64, 10),
                Font = new Font(Font.FontFamily, 9F),
                ForeColor = Color.Gray
            };
            lblHeaderSubtitle = new Label()
            {
                AutoSize = true,
                Location = new Point(64, 32),
                Font = new Font(Font.FontFamily, 9F),
                ForeColor = Color.DimGray
            };
            lblHeaderExtra = new Label()
            {
                AutoSize = true,
                Location = new Point(64, 52),
                Font = new Font(Font.FontFamily, 8.5F),
                ForeColor = Color.Gray,
                Visible = false
            };
            lblHeaderBadge = new Label()
            {
                AutoSize = true,
                Location = new Point(10, 8),
                Font = new Font(Font.FontFamily, 8.5F, FontStyle.Bold),
                ForeColor = Color.White,
                BackColor = Color.MediumPurple,
                Padding = new Padding(6, 2, 6, 2),
                Visible = false
            };

            // Reflètent Debug.debug / Debug.ShowFlight de conf_mod.lua. Checked est posé
            // AVANT l'abonnement à CheckedChanged pour ne pas déclencher une écriture disque
            // dès la construction de la Form (juste une lecture de l'état déjà connu).
            chkDebugMode = new CheckBox()
            {
                AutoSize = true,
                Text = "Debug mode",
                Font = new Font(Font.FontFamily, 8.5F),
                Checked = GetDebugFlag("Debug.debug"),
                Enabled = _confModData != null,
                Visible = false
            };
            chkDebugMode.CheckedChanged += ChkDebugMode_CheckedChanged;

            chkShowFlight = new CheckBox()
            {
                AutoSize = true,
                Text = "Show flights",
                Font = new Font(Font.FontFamily, 8.5F),
                Checked = GetDebugFlag("Debug.AfficheFlight"),
                Enabled = _confModData != null,
                Visible = false
            };
            chkShowFlight.CheckedChanged += ChkShowFlight_CheckedChanged;

            panelHeader.Controls.Add(lblHeaderTitle);
            panelHeader.Controls.Add(lblHeaderVersion);
            panelHeader.Controls.Add(lblHeaderSubtitle);
            panelHeader.Controls.Add(lblHeaderExtra);
            panelHeader.Controls.Add(lblHeaderBadge);
            panelHeader.Controls.Add(chkDebugMode);
            panelHeader.Controls.Add(chkShowFlight);

            return panelHeader;
        }

        private void BuildLayout()
        {
            _table = new TableLayoutPanel()
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 7
            };
            _table.RowStyles.Add(new RowStyle(SizeType.Absolute, 92));  // bandeau permanent (campagne + heure + météo)
            _table.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));  // bandeau d'étapes
            _table.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));  // statut + bouton détails
            _table.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));  // lblStage
            _table.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); // panelQuickOptions - seule ligne Percent, prend TOUT l'espace restant
            _table.RowStyles.Add(new RowStyle(SizeType.Absolute, 0));   // txtOutput - togglée entre 0 et TechnicalLogHeight (voir ToggleTechnicalLog), cachée par défaut
            _table.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));  // panelBottomButtons - 44 au lieu de 26 : les boutons étaient trop plats

            lblStatus = new Label()
            {
                Dock = DockStyle.Fill,
                Text = "Initializing...",
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(6, 0, 0, 0)
            };

            btnToggleLog = new Button()
            {
                Text = "Show technical details",
                Dock = DockStyle.Right,
                Width = 190,
                Font = new Font(Font.FontFamily, 10F)
            };
            btnToggleLog.Click += (s, e) => ToggleTechnicalLog();

            // Ouvre le debrief en popup à la demande, une fois qu'il a été chargé une 1re fois
            // (voir ShowDebriefText). Reste utilisable même après être passé à l'écran de vol,
            // contrairement au panneau pnlDebrief qui lui disparaît (voir ExitDebriefView).
            btnViewDebrief = new Button()
            {
                Text = "View debriefing",
                Dock = DockStyle.Right,
                Width = 140,
                Font = new Font(Font.FontFamily, 10F),
                Visible = false
            };
            btnViewDebrief.Click += (s, e) => ShowDebriefPopup();

            lblStatus.Dock = DockStyle.Left;
            lblStatus.Width = 110;

            progressRunning = new ProgressBar()
            {
                Dock = DockStyle.Fill,
                Style = ProgressBarStyle.Continuous,
                Minimum = 0,
                Maximum = 100,
                Visible = false,
                Margin = new Padding(0, 4, 0, 4)
            };

            var panelStatusRow = new Panel() { Dock = DockStyle.Fill };
            panelStatusRow.Controls.Add(progressRunning);
            panelStatusRow.Controls.Add(lblStatus);
            panelStatusRow.Controls.Add(btnToggleLog);      // ajouté en 1er = reste le plus à droite
            panelStatusRow.Controls.Add(btnViewDebrief);    // ajouté après = se place juste à sa gauche

            lblStage = new Label()
            {
                Dock = DockStyle.Fill,
                Text = "",
                Font = new Font(Font.FontFamily, 12F, FontStyle.Bold),
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(8, 4, 8, 4)
            };

            txtOutput = new TextBox()
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                Font = new Font(FontFamily.GenericMonospace, 9F),
                BackColor = Color.Black,
                ForeColor = Color.Gainsboro,
                Visible = false // cachée par défaut, voir _showTechnicalLog
            };

            txtDebrief = new RichTextBox()
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                Font = new Font(FontFamily.GenericMonospace, 9F),
                BackColor = Color.White,
                ForeColor = Color.Black,
                BorderStyle = BorderStyle.None,
                WordWrap = false,
                ScrollBars = RichTextBoxScrollBars.Both
            };

            pnlDebriefActions = new FlowLayoutPanel()
            {
                Dock = DockStyle.Bottom,
                Height = 44,
                FlowDirection = FlowDirection.LeftToRight,
                Padding = new Padding(6)
            };

            pnlDebrief = new Panel() { Dock = DockStyle.Fill, Visible = false };
            pnlDebrief.Controls.Add(txtDebrief);
            pnlDebrief.Controls.Add(pnlDebriefActions); // ajouté APRÈS txtDebrief : Dock=Bottom prend sa bande, txtDebrief (Fill) occupe le reste

            panelQuickOptions = new FlowLayoutPanel()
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoScroll = true,
                Padding = new Padding(4)
            };

            // Zone d'entrée manuelle : gardée en mémoire mais plus affichée pour l'instant
            // (pas ajoutée à _table.Controls). On verra plus tard si on la supprime pour de bon.
            txtInput = new TextBox() { Dock = DockStyle.Fill };
            txtInput.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter)
                {
                    e.SuppressKeyPress = true;
                    SendManualInput();
                }
            };
            btnSend = new Button() { Text = "Send", Dock = DockStyle.Right, Width = 90 };
            btnSend.Click += (s, e) => SendManualInput();

            btnClose = new Button()
            {
                Text = "Close",
                Dock = DockStyle.Right,
                Width = 130,
                Font = new Font(Font.FontFamily, 10F),
                Enabled = false
            };
            btnClose.Click += (s, e) => Close();

            // Interrompt le script à tout moment (utile notamment quand ScriptsMod boucle sur
            // ses Cycles sans jamais arriver à proposer de mission). Ne ferme PAS la fenêtre :
            // Kill() déclenche l'event ProcessExited comme pour un arrêt normal, ce qui remet
            // l'UI dans l'état "terminé" (OnProcessExited) sans qu'on ait besoin de dupliquer
            // cette logique ici. On force juste l'affichage du détail technique pour que
            // l'utilisateur voie tout de suite pourquoi rien n'a été généré.
            btnStop = new Button()
            {
                Text = "Stop",
                Dock = DockStyle.Right,
                Width = 100,
                Font = new Font(Font.FontFamily, 10F),
                BackColor = Color.MistyRose
            };
            btnStop.Click += (s, e) =>
            {
                if (!_bridge.IsRunning)
                    return;

                var confirm = MessageBox.Show(
                    "This will stop the ScriptsMod script right now.\r\n\r\nThe mission will probably not be generated.\r\n\r\nContinue?",
                    "Stop ScriptsMod",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning);

                if (confirm != DialogResult.Yes)
                    return;

                _userCancelled = true;
                _bridge.Kill();

                if (!_showTechnicalLog)
                    ToggleTechnicalLog();
            };

            btnFallback = new Button()
            {
                Text = "Launch in classic mode (visible window)",
                Dock = DockStyle.Left,
                Width = 280,
                Font = new Font(Font.FontFamily, 10F),
                Visible = false
            };
            btnFallback.Click += (s, e) => RunFallbackVisible();

            btnForceExit = new Button()
            {
                Text = "Force close (if stuck after an error)",
                Dock = DockStyle.Left,
                Width = 220,
                Font = new Font(Font.FontFamily, 10F),
                Visible = false,
                BackColor = Color.MistyRose
            };
            btnForceExit.Click += (s, e) => SendForceExit();

            var panelBottomButtons = new Panel() { Dock = DockStyle.Fill };
            panelBottomButtons.Controls.Add(btnClose);      // ajouté en 1er = reste le plus à droite
            panelBottomButtons.Controls.Add(btnStop);       // se place juste à sa gauche
            panelBottomButtons.Controls.Add(btnForceExit);
            panelBottomButtons.Controls.Add(btnFallback);

            _table.Controls.Add(BuildHeaderPanel(), 0, 0);
            _table.Controls.Add(BuildStepperPanel(), 0, 1);
            _table.Controls.Add(panelStatusRow, 0, 2);
            _table.Controls.Add(lblStage, 0, 3);
            _table.Controls.Add(panelQuickOptions, 0, 4);
            _table.Controls.Add(txtOutput, 0, 5);
            _table.Controls.Add(panelBottomButtons, 0, 6);
            _table.Controls.Add(pnlDebrief, 0, 4); // même cellule (Percent 100) - un seul des deux visible à la fois

            Controls.Add(_table);
        }

        private Label[] _stepLabels;
        private int _currentStep = 0;
        private static readonly string[] StepNames = { "Running", "Build flights", "Packages", "Confirm" };

        private Panel BuildStepperPanel()
        {
            var panel = new FlowLayoutPanel()
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                Padding = new Padding(10, 5, 10, 5),
                BackColor = Color.WhiteSmoke
            };

            _stepLabels = new Label[StepNames.Length];
            for (int i = 0; i < StepNames.Length; i++)
            {
                var lbl = new Label()
                {
                    Text = (i + 1) + "  " + StepNames[i],
                    AutoSize = true,
                    Font = new Font(Font.FontFamily, 9F, FontStyle.Bold),
                    Padding = new Padding(10, 4, 10, 4),
                    Margin = new Padding(i == 0 ? 0 : 4, 0, 4, 0)
                };
                _stepLabels[i] = lbl;
                panel.Controls.Add(lbl);

                if (i < StepNames.Length - 1)
                {
                    var arrow = new Label() { Text = "→", AutoSize = true, ForeColor = Color.Silver, Padding = new Padding(0, 6, 0, 0) };
                    panel.Controls.Add(arrow);
                }
            }

            return panel;
        }

        // violet = étape courante, vert = étape passée (coché), gris = pas encore atteinte
        private void UpdateStepper(int step)
        {
            _currentStep = step;
            if (_stepLabels == null) return;

            for (int i = 0; i < _stepLabels.Length; i++)
            {
                if (i == step)
                {
                    _stepLabels[i].Text = (i + 1) + "  " + StepNames[i];
                    _stepLabels[i].BackColor = Color.MediumPurple;
                    _stepLabels[i].ForeColor = Color.White;
                }
                else if (i < step)
                {
                    _stepLabels[i].Text = "✓  " + StepNames[i];
                    _stepLabels[i].BackColor = Color.Honeydew;
                    _stepLabels[i].ForeColor = Color.SeaGreen;
                }
                else
                {
                    _stepLabels[i].Text = (i + 1) + "  " + StepNames[i];
                    _stepLabels[i].BackColor = SystemColors.Control;
                    _stepLabels[i].ForeColor = Color.Gray;
                }
            }
        }

        // La console technique est maintenant une ligne Absolute tout en bas, togglée entre 0
        // et TechnicalLogHeight - ça ne touche plus au reste de la table ni à la taille de la fenêtre.
        private void ToggleTechnicalLog()
        {
            _showTechnicalLog = !_showTechnicalLog;

            txtOutput.Visible = _showTechnicalLog;
            _table.RowStyles[5] = new RowStyle(SizeType.Absolute, _showTechnicalLog ? TechnicalLogHeight : 0);

            btnToggleLog.Text = _showTechnicalLog ? "Hide technical details" : "Show technical details";
        }

        // Toute remise à zéro du panneau de propositions doit passer par ici : ça garantit qu'on
        // oublie bien les états spécifiques (accumulateur d'avions, sections de vol, etc.) au
        // passage vers un autre type de prompt.
        private void ClearQuickOptionsPanel()
        {
            panelQuickOptions.Controls.Clear();
            pnlDebriefActions.Controls.Clear();
        }

        // Tant que le debrief est affiché, les petits menus (yes/no, etc.) vont dans sa propre
        // barre d'actions plutôt que dans panelQuickOptions, pour rester visibles EN MÊME TEMPS
        // que le texte du debrief (comme le faisait le duo "notepad + console" en mode DOS).
        private FlowLayoutPanel CurrentOptionsHost()
        {
            return (pnlDebrief != null && pnlDebrief.Visible) ? pnlDebriefActions : panelQuickOptions;
        }

        private void AppendOutputLine(string line)
        {
            // Des lignes peuvent encore arriver après la fermeture (le process n'est pas
            // instantanément tué) : on évite alors de toucher un contrôle déjà détruit.
            if (IsDisposed || txtOutput == null || txtOutput.IsDisposed)
                return;

            txtOutput.AppendText(line + Environment.NewLine);
        }

        private void SetStageCaption(string caption)
        {
            progressRunning.Visible = false;

            // Filet de sécurité : dès qu'un prompt quel qu'il soit apparaît, on est au minimum
            // sortis de l'étape "Running" - les écrans plus précis (Packages, Confirm) avancent
            // encore plus loin via leurs propres appels à UpdateStepper.
            if (_currentStep < 1)
                UpdateStepper(1);

            lblStage.Text = caption;
            lblStage.ForeColor = Color.Black;
        }

        // Détecte si une ligne est une option de menu à un seul caractère :
        // "[X] Label" ou "x - Label" ou "x. Label".
        private bool TryParseOptionLine(string line, out string key, out string label)
        {
            key = null;
            label = null;

            string trimmed = line.Trim();
            if (trimmed.Length == 0)
                return false;

            Match match = RegexBracket.Match(trimmed);
            if (!match.Success)
                match = RegexDash.Match(trimmed);
            if (!match.Success)
                match = RegexDot.Match(trimmed);

            if (!match.Success)
                return false;

            string candidateLabel = match.Groups[2].Value.Trim();

            // Garde-fou : un bandeau ASCII décoratif ("==== ... F-14B EastMed Fleet Defense   |
            // ====") peut accidentellement matcher le même motif "X - Label" qu'un vrai choix
            // (ex: le nom de l'avion "F-14B..." donne clé "F", libellé "14B ... Defense   |").
            // Un vrai libellé de menu ne se termine jamais par un "|" isolé (bordure de cadre).
            if (candidateLabel.EndsWith("|"))
                return false;

            key = match.Groups[1].Value;
            label = candidateLabel;
            return true;
        }

        // "STR=Strike  ESC=Escort  CAP=CAP ..." -> alimente _taskLegend avec STR->Strike etc.
        private void ParseLegendLine(string line)
        {
            foreach (Match m in RegexLegendPair.Matches(line))
                _taskLegend[m.Groups[1].Value] = m.Groups[2].Value;
        }

        // "##DCEM_AC##blue|d|F-16C_50|Dubai Intl|2nd Shaheen Squadron|STR:s,FS:f,ESC:e,..."
        // -> une entrée dans _aircraftById, avec les vraies valeurs complètes (jamais tronquées,
        // contrairement au tableau affiché pour l'humain).
        private AircraftEntry ParseAircraftMarkerLine(string line)
        {
            string payload = line.Substring(AircraftMarkerPrefix.Length);
            string[] parts = payload.Split('|');

            if (parts.Length != 6)
                return null; // format inattendu (le Lua a changé ?) : on ignore plutôt que planter

            string id = parts[1];

            var entry = new AircraftEntry
            {
                Id = id,
                Side = parts[0],
                AircraftType = parts[2],
                Base = parts[3],
                Squadron = parts[4]
            };

            foreach (string taskPair in parts[5].Split(','))
            {
                string[] kv = taskPair.Split(':');
                if (kv.Length != 2)
                    continue;

                string fullCode = kv[0];  // "STR"
                string shortCode = kv[1]; // "s" ou "cs"

                string label;
                if (!_taskLegend.TryGetValue(fullCode, out label))
                    label = fullCode; // légende pas encore vue, on affiche juste "STR"

                entry.Tasks[shortCode] = label;
            }

            _aircraftById[id] = entry;
            return entry;
        }

        // "##DCEM_FLIGHTOPT##1|4|2|CVN-71 Theodore Roosevelt|FA-18C_hornet|Pack 6 -...106 - Strike 2|Bandar-E-Jask..."
        // Version "brute" : ne touche à aucun état partagé, utilisée par le système de sections
        // (BuildAllFlightSectionsUI) qui gère lui-même où ranger chaque entrée.
        private FlightOptionEntry ParseFlightOptionMarkerLineRaw(string line)
        {
            string payload = line.Substring(FlightOptionMarkerPrefix.Length);
            string[] parts = payload.Split('|');

            if (parts.Length != 7)
                return null;

            int index;
            if (!int.TryParse(parts[1], out index))
                return null;

            int proposedNb;
            int.TryParse(parts[2], out proposedNb); // reste à 0 si échec, sera borné juste après

            var entry = new FlightOptionEntry
            {
                Selectable = parts[0] == "1",
                Index = index,
                Nb = parts[2],
                Base = parts[3],
                AircraftType = parts[4],
                GroupName = parts[5],
                Target = parts[6]
            };

            // Valeur de départ du NumericUpDown : le chiffre proposé par le script, borné à [1,4].
            entry.ForcedNb = Math.Max(1, Math.Min(4, proposedNb));

            return entry;
        }

        // Fait avancer la barre par à-coups à chaque ligne reçue du process (repart à 0 en boucle) -
        // preuve visuelle que le script est VRAIMENT actif, contrairement à une Marquee qui tourne
        // même si le process est planté.
        private void PulseProgress()
        {
            if (progressRunning == null || !progressRunning.Visible) return;

            int next = progressRunning.Value + 8;
            progressRunning.Value = next > 100 ? 8 : next;
        }

        private void OnOutputReceived(string line, bool isError)
        {
            PulseProgress();

            // Lignes techniques (invisibles pour l'utilisateur) : traitées et on s'arrête là.
            if (!isError && line.StartsWith(AircraftMarkerPrefix))
            {
                AircraftEntry entry = ParseAircraftMarkerLine(line);

                if (_multiFlightComposerActive)
                    return;

                if (_flightPromptPending && entry != null)
                {
                    _inputMode = InputMode.FlightBuilder;

                    if (!_flightBuilderControlsBuilt)
                    {
                        if (_pendingFlightCount > 1)
                            ShowMultiFlightComposer();
                        else
                            ShowFlightBuilderControls();
                    }
                    else if (_pendingFlightCount > 1)
                    {
                        AppendAircraftToAllComposerRows(entry);
                    }
                    else
                    {
                        AppendAircraftToCombo(entry);
                    }
                }
                return;
            }

            // APRÈS
            if (!isError && line.StartsWith(FlightWishMarkerPrefix))
            {
                if (_multiFlightOptionActive || _flightOptionUIBuilt)
                    return; // vraie boucle qui redémarre pendant qu'on attend le clic sur Confirm : on ignore

                string[] parts = line.Substring(FlightWishMarkerPrefix.Length).Split('|');
                if (parts.Length == 4)
                {
                    int nb;
                    int.TryParse(parts[0], out nb);

                    _currentBuildingSection = new FlightWishSection
                    {
                        Nb = nb,
                        Type = parts[1],
                        Side = parts[2],
                        Task = parts[3]
                    };
                    _flightWishSections.Add(_currentBuildingSection);
                }
                return;
            }

            // APRÈS
            if (!isError && line.StartsWith(FlightOptionMarkerPrefix))
            {
                if (_multiFlightOptionActive || _flightOptionUIBuilt)
                    return;

                FlightOptionEntry entry = ParseFlightOptionMarkerLineRaw(line);
                if (entry != null && _currentBuildingSection != null)
                    _currentBuildingSection.Entries.Add(entry);
                return;
            }

            // APRÈS
            if (!isError && line.StartsWith(FlightPreviewDoneMarkerPrefix))
            {
                if (!_multiFlightOptionActive && !_flightOptionUIBuilt)
                    BuildAllFlightSectionsUI();
                return;
            }

            if (!isError && line.StartsWith(FatalMarkerPrefix))
            {
                ShowFatalStopWarning(line.Substring(FatalMarkerPrefix.Length));
                return;
            }

            if (!isError && line.StartsWith(HeaderMarkerPrefix))
            {
                ParseHeaderMarkerLine(line);
                _pendingLines.Clear(); // repart propre : tout ce qui précède est du bandeau/texte d'accueil
                return;
            }
            if (!isError && line.StartsWith(TimeMarkerPrefix)) { ParseTimeMarkerLine(line); return; }
            if (!isError && line.StartsWith(DayNightMarkerPrefix)) { ParseDayNightMarkerLine(line); return; }
            if (!isError && line.StartsWith(CycleMarkerPrefix)) { ParseCycleMarkerLine(line); return; }
            if (!isError && line.StartsWith(MetarMarkerPrefix)) { ParseMetarMarkerLine(line); return; }
            if (!isError && line.StartsWith(MissionDateMarkerPrefix)) { ParseMissionDateMarkerLine(line); return; }
            if (!isError && line.StartsWith(DebriefTextPathMarkerPrefix)) { ShowDebriefText(line.Substring(DebriefTextPathMarkerPrefix.Length)); return; }

            if (!isError && line.StartsWith(BugListStartMarkerPrefix)) { _bugListItems.Clear(); _bugListCapturePending = true; return; }
            if (!isError && line.StartsWith(BugListItemMarkerPrefix))
            {
                if (_bugListCapturePending)
                    _bugListItems.Add(line.Substring(BugListItemMarkerPrefix.Length));
                return;
            }
            if (!isError && line.StartsWith(BugListEndMarkerPrefix)) { _bugListCapturePending = false; return; }

            if (!isError && line.StartsWith(TargetMarkerPrefix))
            {
                TargetOption entry = ParseTargetMarkerLine(line);
                if (_targetListPending && entry != null)
                {
                    _inputMode = InputMode.TargetSelect;
                    if (_lstTargets == null)
                        ShowTargetListControls();
                    else
                        _lstTargets.Items.Add(entry);
                }
                return;
            }

            if (!isError && line.StartsWith(CsarTargetMarkerPrefix))
            {
                TargetOption entry = ParseCsarTargetMarkerLine(line);
                if (_targetListPending && entry != null)
                {
                    _inputMode = InputMode.TargetSelect;
                    if (_lstTargets == null)
                        ShowTargetListControls();
                    else
                        _lstTargets.Items.Add(entry);
                }
                return;
            }

            if (!isError && line.StartsWith(PromptMarkerPrefix))
            {
                HandlePromptMarker(line.Substring(PromptMarkerPrefix.Length));
                return;
            }

            if (isError && RegexLuaCrashSignature.IsMatch(line))
                ShowCrashWarning(line);

            AppendOutputLine(isError ? "[ERR] " + line : line);

            _pendingLines.Add(line);
            if (_pendingLines.Count > 60)
                _pendingLines.RemoveAt(0);

            if (RegexLegendPair.Matches(line).Count > 0)
                ParseLegendLine(line);

            // Filet de sécurité générique, indépendant de la langue (forme "[X] Label" / "X - Label" /
            // "X. Label"), pour tout menu qui n'a pas encore son propre marqueur ##DCEM_PROMPT##.
            if (_inputMode == InputMode.QuickOptions)
                RebuildQuickOptions();
        }

        // ---- Menu à lettre classique (comportement d'origine) ----

        private void RebuildQuickOptions()
        {
            ClearQuickOptionsPanel();

            var seenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var options = new List<KeyValuePair<string, string>>(); // key, label

            foreach (string line in _pendingLines)
            {
                string key, label;
                if (!TryParseOptionLine(line, out key, out label))
                    continue;

                if (!seenKeys.Add(key))
                    continue;

                options.Add(new KeyValuePair<string, string>(key, label));
            }

            if (options.Count == 0)
            {
                progressRunning.Visible = true; // le script écrit encore ce menu, rien d'exploitable pour l'instant
                return;
            }

            SetStageCaption("Choose an option"); // remet Visible=false, normal : le menu est prêt, à l'utilisateur de jouer

            var fullTexts = new List<string>();
            foreach (var opt in options)
                fullTexts.Add(opt.Key.ToUpperInvariant() + " - " + opt.Value);

            int width = ComputeUniformButtonWidth(fullTexts);

            for (int i = 0; i < options.Count; i++)
                AddSendButton(options[i].Key, fullTexts[i], width);
        }

        private void ShowFlightCountControl()
        {
            ClearQuickOptionsPanel();
            SetStageCaption("How many flights do you want?");
            _pendingFlightAnswers.Clear();
            _multiFlightComposerActive = false;

            var panel = new Panel() { Width = 420, Height = 40 };

            const int btnSize = 34;
            for (int i = 1; i <= 8; i++)
            {
                int captured = i;
                var button = new Button()
                {
                    Text = i.ToString(),
                    Width = btnSize,
                    Height = btnSize,
                    Location = new Point((i - 1) * (btnSize + 4), 0),
                    Font = new Font(Font.FontFamily, 10F, FontStyle.Bold)
                };
                button.Click += (s, e) =>
                {
                    _pendingFlightCount = captured;
                    SendInput(captured.ToString());
                };
                panel.Controls.Add(button);
            }

            panelQuickOptions.Controls.Add(panel);
        }

        // ---- Prompt "nombre" générique (réservé à un futur prompt numérique sans marqueur dédié) ----

        private void ShowNumericControl()
        {
            ClearQuickOptionsPanel();
            SetStageCaption("Choose a number");

            var panel = new Panel() { Width = 420, Height = 40 };

            const int btnSize = 34;
            for (int i = 1; i <= 8; i++)
            {
                int captured = i; // capture par valeur, pas par référence de boucle
                var button = new Button()
                {
                    Text = i.ToString(),
                    Width = btnSize,
                    Height = btnSize,
                    Location = new Point((i - 1) * (btnSize + 4), 0),
                    Font = new Font(Font.FontFamily, 10F, FontStyle.Bold)
                };
                button.Click += (s, e) => SendInput(captured.ToString());
                panel.Controls.Add(button);
            }

            panelQuickOptions.Controls.Add(panel);
        }
        // Écran spécifique au y/n "Accept mission results?" de DEBRIEF_Master.lua (marqueur
        // ##DCEM_PROMPT##debriefaccept). Distinct du "yesno" générique (Continue?) utilisé
        // ailleurs, car le texte et le comportement de sortie diffèrent.
        private void ShowDebriefAcceptControl()
        {
            ClearQuickOptionsPanel();
            SetStageCaption("Accept mission results?");

            int width = ComputeUniformButtonWidth(new[] { "Yes", "No" });

            var btnYes = new Button()
            {
                Text = "Yes",
                AutoSize = false,
                Width = width,
                Height = 34,
                Font = new Font(Font.FontFamily, 10F)
            };
            btnYes.Click += (s, e) =>
            {
                ExitDebriefView();
                SendInput("y");
            };
            CurrentOptionsHost().Controls.Add(btnYes);

            var btnNo = new Button()
            {
                Text = "No",
                AutoSize = false,
                Width = width,
                Height = 34,
                Font = new Font(Font.FontFamily, 10F)
            };
            btnNo.Click += (s, e) =>
            {
                _userCancelled = true; // le script fait os.exit() sans rien générer : pas un vrai succès
                ExitDebriefView();
                SendInput("n");
            };
            CurrentOptionsHost().Controls.Add(btnNo);
        }

        // Referme la vue debrief et redonne la main au panneau de propositions habituel
        // (stepper Build flights/Packages/Confirm). Appelé dès que l'utilisateur répond au
        // y/n "Accept mission results?", qu'il ait accepté ou refusé.
        private void ExitDebriefView()
        {
            if (pnlDebrief == null)
                return;

            pnlDebrief.Visible = false;
            panelQuickOptions.Visible = true;
        }
        private void ShowYesNoControl(string caption = "Continue?")
        {
            ClearQuickOptionsPanel();
            SetStageCaption(caption);

            int width = ComputeUniformButtonWidth(new[] { "Y - Yes, continue", "N - No, cancel" });

            AddSendButton("y", "Y - Yes, continue", width);

            var btnNo = new Button()
            {
                Text = "N - No, cancel",
                AutoSize = false,
                Width = width,
                Height = 34,
                Font = new Font(Font.FontFamily, 10F),
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(8, 0, 0, 0)
            };
            _toolTip.SetToolTip(btnNo, "N - No, cancel");
            btnNo.Click += (s, e) =>
            {
                _userCancelled = true;
                SendInput("n");
            };
            CurrentOptionsHost().Controls.Add(btnNo); // corrigé : passait par panelQuickOptions en dur avant
        }

        // ---- Prompt "composition de vol" (ex: "4de" = 4 avions, ID d, tâche e) ----

        private void ShowFlightBuilderControls()
        {
            ClearQuickOptionsPanel();
            SetStageCaption("Build your flight");
            UpdateStepper(1);
            _flightCountButtons.Clear();
            _selectedFlightCount = 1;

            var panel = new Panel() { Width = 820, Height = 100 };

            var lblCount = new Label() { Text = "Aircraft count:", Location = new Point(0, 4), AutoSize = true };

            const int btnSize = 32;
            for (int i = 1; i <= 8; i++)
            {
                int captured = i; // capture par valeur, pas par référence de boucle
                var countBtn = new Button()
                {
                    Text = i.ToString(),
                    Width = btnSize,
                    Height = btnSize,
                    Location = new Point((i - 1) * (btnSize + 4), 22),
                    Font = new Font(Font.FontFamily, 10F, FontStyle.Bold)
                };
                countBtn.Click += (s, e) => SelectFlightCount(captured);
                _flightCountButtons.Add(countBtn);
                panel.Controls.Add(countBtn);
            }
            HighlightSelectedFlightCount();

            const int rowY = 62;

            var lblId = new Label() { Text = "Aircraft:", Location = new Point(0, rowY), AutoSize = true };
            _cmbAircraftId = new ComboBox()
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Width = 340,
                Location = new Point(80, rowY - 3),
                FormattingEnabled = true
            };

            foreach (AircraftEntry entry in _aircraftById.Values.OrderBy(a => a.Id))
                _cmbAircraftId.Items.Add(entry);

            _cmbAircraftId.Format += (s, e) =>
            {
                var entry = (AircraftEntry)e.ListItem;
                // Plus de lettre d'ID : le type + squadron + base suffisent à identifier l'avion.
                // L'ID reste utilisé en interne (entry.Id) pour composer la réponse envoyée au script.
                e.Value = entry.AircraftType + " - " + entry.Squadron + " (" + entry.Base + ")";
            };

            var lblTask = new Label() { Text = "Task:", Location = new Point(430, rowY), AutoSize = true };
            _cmbTaskCode = new ComboBox()
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Width = 180,
                Location = new Point(490, rowY - 3),
                FormattingEnabled = true
            };

            _cmbAircraftId.SelectedIndexChanged += (s, e) => RebuildTaskCombo();

            var btnSendFlight = new Button()
            {
                Text = "Send",
                Width = 100,
                Height = 30,
                Location = new Point(690, rowY - 4)
            };
            btnSendFlight.Click += (s, e) => SendFlightSelection();

            panel.Controls.Add(lblCount);
            panel.Controls.Add(lblId);
            panel.Controls.Add(_cmbAircraftId);
            panel.Controls.Add(lblTask);
            panel.Controls.Add(_cmbTaskCode);
            panel.Controls.Add(btnSendFlight);

            panelQuickOptions.Controls.Add(panel);

            if (_cmbAircraftId.Items.Count > 0)
                _cmbAircraftId.SelectedIndex = 0; // déclenche RebuildTaskCombo()

            _flightBuilderControlsBuilt = true;
        }

        // Déduit le camp du joueur en retrouvant, parmi les avions connus, celui dont l'escadron
        // correspond à celui affiché dans l'en-tête (_headerSquadron, rempli via ##DCEM_HEADER##).
        // Renvoie "blue"/"red" en minuscules, ou "" si aucune correspondance trouvée.
        // Charge (une fois par session) la liste des escadrons de la campagne via le même parser
        // que le reste de l'appli (Init\oob_air_init.lua + Active\oob_air.lua, avec repli automatique
        // entre les deux) - pour en tirer les pays par camp, sans rien demander de plus à ScriptsMod/Lua.
        private void EnsureOobAirLoaded()
        {
            if (_oobAirLoaded)
                return;

            _oobAirLoaded = true;

            try
            {
                new Parser_OobAir().LoadCampaignSquads(_campaignName);
            }
            catch (Exception ex)
            {
                FormUtils.LogRegister("ScriptsModRunner_Form | EnsureOobAirLoaded | " + ex.Message);
            }
        }

        // Retrouve, parmi les avions déjà reçus (##DCEM_AC##), celui qui correspond à l'escadron du
        // joueur (Squad.Player == true dans oob_air). Renvoie null tant qu'il n'est pas encore arrivé
        // dans la table (le tableau texte n'est pas forcément trié avec le joueur en premier).
        private AircraftEntry FindPlayerAircraftEntry()
        {
            EnsureOobAirLoaded();

            Squad playerSquad = List_oob_air_Manager.List_oob_air.FirstOrDefault(s => s.Player);
            if (playerSquad == null)
                return null;

            return _aircraftById.Values.FirstOrDefault(a =>
                !string.IsNullOrEmpty(a.Squadron) &&
                a.Squadron.Equals(playerSquad.Name, StringComparison.OrdinalIgnoreCase) &&
                a.Side.Equals(playerSquad.SideString, StringComparison.OrdinalIgnoreCase));
        }

        // Texte du petit label au-dessus de chaque colonne :
        // - camp du joueur (repéré via Squad.Player == true) : "<SIDE> - <son pays exact>"
        // - autre camp : "<SIDE> - <tous les pays distincts trouvés dans l'OOB>" (ex: "Russia - Iraq")
        private string BuildSideHeaderText(string side)
        {
            EnsureOobAirLoaded();

            List<Squad> squads = List_oob_air_Manager.List_oob_air;
            Squad playerSquad = squads.FirstOrDefault(s => s.Player);

            bool isPlayerSide = playerSquad != null &&
                !string.IsNullOrEmpty(playerSquad.SideString) &&
                playerSquad.SideString.Equals(side, StringComparison.OrdinalIgnoreCase);

            string countries;
            if (isPlayerSide)
            {
                countries = playerSquad.Country;
            }
            else
            {
                countries = string.Join(" - ", squads
                    .Where(s => !string.IsNullOrEmpty(s.SideString) &&
                                s.SideString.Equals(side, StringComparison.OrdinalIgnoreCase))
                    .Select(s => s.Country)
                    .Where(c => !string.IsNullOrWhiteSpace(c))
                    .Distinct(StringComparer.OrdinalIgnoreCase));
            }

            return string.IsNullOrEmpty(countries)
                ? side.ToUpperInvariant()
                : side.ToUpperInvariant() + " - " + countries;
        }


        private void ShowMultiFlightComposer()
        {
            ClearQuickOptionsPanel();
            SetStageCaption("Build your flights");
            _composerRows.Clear();

            const int rowHeight = 46;
            const int btnSize = 26;

            var outer = new Panel() { Width = 960, Height = rowHeight * _pendingFlightCount + 100 };

            var lblBlueHeader = new Label { Text = "BLUE - France", Location = new Point(313, 0), AutoSize = true, ForeColor = Color.Blue, Font = new Font(Font.FontFamily, 8.5F, FontStyle.Bold) };
            var lblRedHeader = new Label { Text = "RED - Iran", Location = new Point(553, 0), AutoSize = true, ForeColor = Color.Red, Font = new Font(Font.FontFamily, 8.5F, FontStyle.Bold) };
            outer.Controls.Add(lblBlueHeader);
            outer.Controls.Add(lblRedHeader);

            ListControlConvertEventHandler formatAircraft = (s, e) =>
            {
                var entry = (AircraftEntry)e.ListItem;
                e.Value = entry.Id == null ? "" : entry.AircraftType + " - " + entry.Squadron + " (" + entry.Base + ")";
            };

            int y = 20;
            for (int f = 1; f <= _pendingFlightCount; f++)
            {
                var row = new FlightComposerRow();

                var lblFlight = new Label()
                {
                    Text = "Flight #" + f + ":",
                    Location = new Point(0, y + 5),
                    AutoSize = true,
                    Font = new Font(Font.FontFamily, 9F, FontStyle.Bold)
                };
                outer.Controls.Add(lblFlight);

                for (int i = 1; i <= 8; i++)
                {
                    int captured = i;
                    var countBtn = new Button()
                    {
                        Text = i.ToString(),
                        Width = btnSize,
                        Height = btnSize,
                        Location = new Point(74 + (i - 1) * (btnSize + 3), y),
                        Font = new Font(Font.FontFamily, 9F, FontStyle.Bold)
                    };
                    FlightComposerRow capturedRow = row;
                    countBtn.Click += (s, e) => SelectComposerRowCount(capturedRow, captured);
                    row.CountButtons.Add(countBtn);
                    outer.Controls.Add(countBtn);
                }

                var cmbBlue = new ComboBox()
                {
                    DropDownStyle = ComboBoxStyle.DropDownList,
                    Width = 230,
                    Location = new Point(313, y),
                    FormattingEnabled = true
                };
                var cmbRed = new ComboBox()
                {
                    DropDownStyle = ComboBoxStyle.DropDownList,
                    Width = 230,
                    Location = new Point(553, y),
                    FormattingEnabled = true
                };
                cmbBlue.Format += formatAircraft;
                cmbRed.Format += formatAircraft;
                cmbBlue.Items.Add(EmptyAircraftEntry);
                cmbRed.Items.Add(EmptyAircraftEntry);
                cmbBlue.SelectedIndex = 0;
                cmbRed.SelectedIndex = 0;

                var cmbTask = new ComboBox()
                {
                    DropDownStyle = ComboBoxStyle.DropDownList,
                    Width = 140,
                    Location = new Point(793, y),
                    FormattingEnabled = true
                };

                row.CmbAircraftBlue = cmbBlue;
                row.CmbAircraftRed = cmbRed;
                row.CmbTask = cmbTask;

                FlightComposerRow capturedRow2 = row;

                // Sélection exclusive : choisir un avion Bleu vide le combo Rouge de la même ligne,
                // et inversement - un flight ne peut être que d'un seul camp.
                cmbBlue.SelectedIndexChanged += (s, e) =>
                {
                    var sel = cmbBlue.SelectedItem as AircraftEntry;
                    if (sel != null && sel.Id != null && cmbRed.SelectedIndex != 0)
                        cmbRed.SelectedIndex = 0;
                    RebuildComposerRowTaskCombo(capturedRow2);
                };
                cmbRed.SelectedIndexChanged += (s, e) =>
                {
                    var sel = cmbRed.SelectedItem as AircraftEntry;
                    if (sel != null && sel.Id != null && cmbBlue.SelectedIndex != 0)
                        cmbBlue.SelectedIndex = 0;
                    RebuildComposerRowTaskCombo(capturedRow2);
                };

                outer.Controls.Add(cmbBlue);
                outer.Controls.Add(cmbRed);
                outer.Controls.Add(cmbTask);

                _composerRows.Add(row);
                y += rowHeight;
            }

            // Les avions déjà connus au moment de la construction (au moins celui qui vient de
            // déclencher cet écran) sont ajoutés APRÈS coup, via la même méthode que pour les
            // suivants - une seule logique de répartition Bleu/Rouge + placement du joueur.
            foreach (AircraftEntry entry in _aircraftById.Values.OrderBy(a => a.Id))
                AppendAircraftToAllComposerRows(entry);

            _lblComposerWarning = new Label()
            {
                Text = "⚠ Select a number (1-8) for every flight before confirming.",
                Location = new Point(0, y + 6),
                AutoSize = true,
                ForeColor = Color.DarkOrange,
                Font = new Font(Font.FontFamily, 9F, FontStyle.Bold)
            };
            outer.Controls.Add(_lblComposerWarning);

            _btnConfirmComposer = new Button()
            {
                Text = "Confirm " + _pendingFlightCount + " flights",
                Width = 200,
                Height = 32,
                Location = new Point(0, y + 26),
                Enabled = false
            };
            _btnConfirmComposer.Click += (s, e) => ValidateMultiFlightComposer();
            outer.Controls.Add(_btnConfirmComposer);

            panelQuickOptions.Controls.Add(outer);

            _flightBuilderControlsBuilt = true;
        }

        private void SelectComposerRowCount(FlightComposerRow row, int count)
        {
            row.SelectedCount = count;
            foreach (Button b in row.CountButtons)
                b.BackColor = (b.Text == count.ToString()) ? Color.LightSteelBlue : SystemColors.Control;

            RefreshComposerValidationState();
        }

        // Le bouton "Confirm N flights" ne s'active que quand CHAQUE ligne a ivate void ShowMultiFlightComposer()un chiffre choisi.
        private void RefreshComposerValidationState()
        {
            bool allSelected = _composerRows.Count > 0 && _composerRows.All(r => r.SelectedCount > 0);

            if (_btnConfirmComposer != null)
                _btnConfirmComposer.Enabled = allSelected;
            if (_lblComposerWarning != null)
                _lblComposerWarning.Visible = !allSelected;
        }

        private void RebuildComposerRowTaskCombo(FlightComposerRow row)
        {
            row.CmbTask.Items.Clear();

            AircraftEntry entry = row.SelectedAircraft;
            if (entry == null)
                return;

            foreach (KeyValuePair<string, string> task in entry.Tasks)
                row.CmbTask.Items.Add(new TaskOption { Code = task.Key, Label = task.Value });

            if (row.CmbTask.Items.Count > 0)
                row.CmbTask.SelectedIndex = 0;
        }

        // Reçoit les nouvelles entrées d'avions (rounds suivants du tableau texte) tant que le
        // composeur multi-vol n'a pas encore été validé - les ajoute à toutes les lignes d'un coup.
        private void AppendAircraftToAllComposerRows(AircraftEntry entry)
        {
            bool isBlue = entry.Side.Equals("blue", StringComparison.OrdinalIgnoreCase);
            bool isRed = entry.Side.Equals("red", StringComparison.OrdinalIgnoreCase);
            if (!isBlue && !isRed)
                return; // camp inconnu/inattendu : on ignore plutôt que planter

            AircraftEntry playerEntry = FindPlayerAircraftEntry();
            bool isPlayerEntry = playerEntry != null && playerEntry == entry;

            for (int i = 0; i < _composerRows.Count; i++)
            {
                FlightComposerRow row = _composerRows[i];
                ComboBox target = isBlue ? row.CmbAircraftBlue : row.CmbAircraftRed;

                bool alreadyPresent = false;
                foreach (object item in target.Items)
                {
                    var existing = item as AircraftEntry;
                    if (existing != null && existing.Id != null &&
                        existing.Id.Equals(entry.Id, StringComparison.OrdinalIgnoreCase))
                    {
                        alreadyPresent = true;
                        break;
                    }
                }
                if (alreadyPresent) continue;

                target.BeginUpdate();

                // L'avion du joueur, sur la ligne #1 côté Bleu : juste après la ligne vide (index 0).
                bool isFirstBlueRowForPlayer = isBlue && i == 0 && isPlayerEntry;
                if (isFirstBlueRowForPlayer)
                    target.Items.Insert(1, entry);
                else
                    target.Items.Add(entry);

                target.EndUpdate();

                if (isFirstBlueRowForPlayer && row.SelectedAircraft == null)
                    target.SelectedIndex = 1;
            }
        }

        // Compose les N chaînes ("2fs", "4de", ...), envoie la 1re, met les autres en file d'attente.
        // Les suivantes partiront toutes seules via OnOutputReceived, au fil des prompts suivants.
        private void ValidateMultiFlightComposer()
        {
            var composed = new List<string>();

            foreach (FlightComposerRow row in _composerRows)
            {
                AircraftEntry entry = row.SelectedAircraft;
                var task = row.CmbTask.SelectedItem as TaskOption;

                if (row.SelectedCount == 0)
                {
                    MessageBox.Show("Each flight must have a count (1-8) selected.");
                    return;
                }

                if (entry == null || task == null)
                {
                    MessageBox.Show("Each flight must have an aircraft and a task selected.");
                    return;
                }

                composed.Add(row.SelectedCount + entry.Id + task.Code);
            }

            _pendingFlightAnswers.Clear();
            foreach (string answer in composed)
                _pendingFlightAnswers.Enqueue(answer);

            _multiFlightComposerActive = true;
            lblStage.Text = "Sending " + composed.Count + " prepared flights...";

            string first = _pendingFlightAnswers.Dequeue();
            SendInput(first);
        }

        // Sélectionne le nombre d'avions (1 clic) et met en surbrillance le bouton choisi.
        private void SelectFlightCount(int count)
        {
            _selectedFlightCount = count;
            HighlightSelectedFlightCount();
        }

        private void HighlightSelectedFlightCount()
        {
            foreach (Button b in _flightCountButtons)
                b.BackColor = (b.Text == _selectedFlightCount.ToString())
                    ? Color.LightSteelBlue
                    : SystemColors.Control;
        }

        // Ajoute une seule entrée fraîchement parsée au combo déjà construit, sans rien reconstruire.
        // Appelée pour toutes les lignes ##DCEM_AC## qui arrivent APRÈS la 1re (déjà prise en compte
        // par ShowFlightBuilderControls au moment de sa construction).
        private void AppendAircraftToCombo(AircraftEntry entry)
        {
            foreach (object item in _cmbAircraftId.Items)
            {
                var existing = item as AircraftEntry;
                if (existing != null && existing.Id.Equals(entry.Id, StringComparison.OrdinalIgnoreCase))
                    return; // déjà présent (ne devrait pas arriver, sécurité)
            }

            _cmbAircraftId.BeginUpdate();
            _cmbAircraftId.Items.Add(entry);
            _cmbAircraftId.EndUpdate();

            if (_cmbAircraftId.Items.Count == 1)
                _cmbAircraftId.SelectedIndex = 0; // 1re entrée : sélectionne-la pour peupler la combo Tâche
        }

        // Repeuple le combo "Tâche" avec seulement les tâches dispo pour l'avion sélectionné.
        private void RebuildTaskCombo()
        {
            _cmbTaskCode.Items.Clear();

            var entry = _cmbAircraftId.SelectedItem as AircraftEntry;
            if (entry == null)
                return;

            foreach (KeyValuePair<string, string> task in entry.Tasks)
                _cmbTaskCode.Items.Add(new TaskOption { Code = task.Key, Label = task.Value });

            if (_cmbTaskCode.Items.Count > 0)
                _cmbTaskCode.SelectedIndex = 0;
        }

        // Compose "4de" (nombre + ID + code tâche 1 lettre) et l'envoie comme une réponse normale.
        private void SendFlightSelection()
        {
            var entry = _cmbAircraftId.SelectedItem as AircraftEntry;
            var task = _cmbTaskCode.SelectedItem as TaskOption;

            if (entry == null || task == null)
            {
                MessageBox.Show("Choose an aircraft and a task before sending.");
                return;
            }

            string composed = _selectedFlightCount + entry.Id + task.Code;
            SendInput(composed);
        }

        private void SendManualInput()
        {
            string text = txtInput.Text;
            txtInput.Clear();
            SendInput(text);
        }

        private void SendInput(string text)
        {
            if (!_bridge.IsRunning)
                return;

            AppendOutputLine("> " + text);

            _bridge.SendLine(text);

            progressRunning.Visible = true;

            lblStage.Text = "Please wait..."; // évite que le titre de la question précédente reste affiché pendant le traitement


            _pendingLines.Clear();
            ClearQuickOptionsPanel();

            // Retour au mode "menu à lettre" par défaut ; si le prochain prompt est encore un
            // nombre ou une composition de vol, OnOutputReceived le redétectera tout seul.
            // On NE vide PAS _aircraftById/_taskLegend : la table d'avions reste valable pour
            // les prochains vols (Flight n°2, n°3...).
            _inputMode = InputMode.QuickOptions;
            _flightPromptPending = false;
            _flightBuilderControlsBuilt = false;
            _targetListPending = false;
            _lstTargets = null;
        }

        // Envoie une réponse sans toucher au panneau (contrairement à SendInput) : utilisé pour
        // drainer une file de réponses déjà décidées par l'utilisateur (composeur multi-vol,
        // sélection de paquets de vol) sans faire disparaître l'écran récapitulatif.
        private void SendQueuedAnswer(string text)
        {
            if (!_bridge.IsRunning) return;
            AppendOutputLine("> " + text);
            _bridge.SendLine(text);
        }

        private void OnProcessExited(bool success)
        {
            lblStatus.Text = success ? "Done." : "Done (error code).";
            lblStage.Text = success ? "Mission generated." : "The script stopped with an error - see technical details.";
            progressRunning.Visible = false;
            UpdateStepper(3); // dernière étape : "Confirm"

            ClearQuickOptionsPanel();
            txtInput.Enabled = false;
            btnSend.Enabled = false;
            //btnNudge.Enabled = false;
            btnClose.Enabled = true;

            BuildCompletionReportPanel(success);
        }

        // Panneau final ("Style 1" de la maquette) : réutilise panelQuickOptions, libre une fois
        // le script terminé (ClearQuickOptionsPanel vient d'être appelé juste avant dans
        // OnProcessExited). Onglet "Summary" toujours présent, onglet "Debug log" seulement
        // s'il y a des entrées dans BugList (Lua), reçues via ##DCEM_BUGLIST...##.
        //
        // ATTENTION : panelQuickOptions est un FlowLayoutPanel en TopDown. Il ne faut PAS ancrer
        // un contrôle sur Top+Bottom (= sens du flux), sinon sa hauteur est écrasée à 0 et il
        // devient invisible. On donne donc une taille explicite, comme partout ailleurs, et on la
        // recalcule si la fenêtre est redimensionnée.
        private void BuildCompletionReportPanel(bool success)
        {
            // Palette douce, proche de la maquette : pas de blanc pur, léger fond bleuté.
            Color fondDoux = Color.FromArgb(247, 248, 251);
            Color texteDoux = Color.FromArgb(52, 58, 70);
            Color bordureDouce = Color.FromArgb(220, 224, 232);

            var tabs = new TabControl()
            {
                Width = Math.Max(300, panelQuickOptions.ClientSize.Width - 25),
                Height = Math.Max(200, panelQuickOptions.ClientSize.Height - 12),
                Margin = new Padding(4)
            };

            var tabSummary = new TabPage("Summary") { BackColor = fondDoux };
            var lblSummary = new Label()
            {
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.TopLeft,
                Padding = new Padding(18, 20, 14, 14),
                Font = new Font(Font.FontFamily, 11F, FontStyle.Bold),
                ForeColor = success ? Color.SeaGreen : Color.Red,
                Text = success
                    ? "✓  Mission generated.\r\n\r\nDone."
                    : "✗  The script stopped with an error.\r\n\r\nSee the technical details."
            };
            tabSummary.Controls.Add(lblSummary);
            tabs.TabPages.Add(tabSummary);

            if (_bugListItems.Count > 0)
            {
                var tabDebug = new TabPage("Debug log (" + _bugListItems.Count + ")") { BackColor = fondDoux };

                var txtBugList = new TextBox()
                {
                    Dock = DockStyle.Fill,
                    Multiline = true,
                    ReadOnly = true,
                    ScrollBars = ScrollBars.Both,
                    WordWrap = false,
                    BorderStyle = BorderStyle.None,
                    Font = new Font(FontFamily.GenericMonospace, 8.5F),
                    BackColor = fondDoux,
                    ForeColor = texteDoux,
                    Text = string.Join(Environment.NewLine,
                        _bugListItems.Select((item, i) => (i + 1) + ".  " + item))
                };

                // Le TextBox n'a pas de Padding utilisable : on l'entoure d'un Panel bordé qui
                // fait à la fois la marge intérieure et le petit cadre gris de la maquette.
                var cadreLog = new Panel()
                {
                    Dock = DockStyle.Fill,
                    BackColor = fondDoux,
                    BorderStyle = BorderStyle.FixedSingle,
                    Padding = new Padding(10, 8, 4, 6),
                    Margin = new Padding(10)
                };
                cadreLog.Controls.Add(txtBugList);

                // Marge extérieure entre le cadre et les bords de l'onglet.
                var zoneLog = new Panel()
                {
                    Dock = DockStyle.Fill,
                    BackColor = fondDoux,
                    Padding = new Padding(10, 4, 10, 10)
                };
                zoneLog.Controls.Add(cadreLog);

                var btnCopy = MakeReportButton("Copy to clipboard", 150, bordureDouce, texteDoux);
                btnCopy.Click += (s, e) => Clipboard.SetText(txtBugList.Text);

                var btnSave = MakeReportButton("Save to file...", 130, bordureDouce, texteDoux);
                btnSave.Click += (s, e) => SaveBugListToFile(txtBugList.Text);

                // RightToLeft : les boutons se rangent à droite comme sur la maquette. Ordre
                // d'ajout inversé pour que "Copy" reste à gauche de "Save".
                var barreBoutons = new FlowLayoutPanel()
                {
                    Dock = DockStyle.Top,
                    Height = 38,
                    FlowDirection = FlowDirection.RightToLeft,
                    BackColor = fondDoux,
                    Padding = new Padding(0, 6, 12, 0)
                };
                barreBoutons.Controls.Add(btnSave);
                barreBoutons.Controls.Add(btnCopy);

                // Le Dock.Fill doit être ajouté AVANT le Dock.Top pour que la barre reste en haut.
                tabDebug.Controls.Add(zoneLog);
                tabDebug.Controls.Add(barreBoutons);
                tabs.TabPages.Add(tabDebug);

                // Si le script a planté, on ouvre directement sur le détail plutôt que sur Summary.
                tabs.SelectedTab = success ? tabSummary : tabDebug;
            }

            panelQuickOptions.Controls.Add(tabs);

            // Le TabControl n'étant pas ancré (cf. commentaire plus haut), on le resuit à la main
            // quand la fenêtre change de taille. Le handler se retire tout seul dès que le
            // TabControl n'est plus dans le panneau (écran suivant, fermeture...).
            EventHandler resizeHandler = null;
            resizeHandler = (s, e) =>
            {
                if (tabs.IsDisposed || tabs.Parent != panelQuickOptions)
                {
                    panelQuickOptions.Resize -= resizeHandler;
                    return;
                }
                tabs.Width = Math.Max(300, panelQuickOptions.ClientSize.Width - 25);
                tabs.Height = Math.Max(200, panelQuickOptions.ClientSize.Height - 12);
            };
            panelQuickOptions.Resize += resizeHandler;
        }

        // Petit bouton plat blanc à bordure grise, comme sur la maquette.
        private Button MakeReportButton(string texte, int largeur, Color bordure, Color couleurTexte)
        {
            var bouton = new Button()
            {
                Text = texte,
                Width = largeur,
                Height = 26,
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.White,
                ForeColor = couleurTexte,
                Font = new Font(Font.FontFamily, 8.5F),
                Margin = new Padding(6, 0, 0, 0)
            };
            bouton.FlatAppearance.BorderColor = bordure;
            bouton.FlatAppearance.BorderSize = 1;
            return bouton;
        }

        private void SaveBugListToFile(string content)
        {
            using (var dlg = new SaveFileDialog() { Filter = "Text file (*.txt)|*.txt", FileName = "BugList.txt" })
            {
                if (dlg.ShowDialog(this) == DialogResult.OK)
                {
                    try { File.WriteAllText(dlg.FileName, content); }
                    catch (Exception ex) { MessageBox.Show("Unable to save the file:\r\n" + ex.Message); }
                }
            }
        }

        private void ScriptsModRunner_Form_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (_bridge.IsRunning)
            {
                var confirm = MessageBox.Show(
                    "The ScriptsMod script is still running.\r\n\r\n" +
                    "Closing now will stop it before completion (the mission may not be generated).\r\n\r\n" +
                    "Close anyway?",
                    "ScriptsMod running",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning);

                if (confirm != DialogResult.Yes)
                {
                    e.Cancel = true;
                    return;
                }

                _bridge.Kill();
            }

            _bridge.Dispose();
        }

        // Petite fenêtre de remplacement pour MessageBox.Show : le texte est dans une TextBox
        // multi-ligne (sélectionnable/copiable par glisser + Ctrl+C), plus un bouton dédié
        // "Copy to clipboard" pour ne pas dépendre du raccourci caché de MessageBox.
        private void ShowCopyableErrorDialog(string title, string message)
        {
            using (var dlg = new Form())
            {
                dlg.Text = title;
                dlg.StartPosition = FormStartPosition.CenterParent;
                dlg.Width = 560;
                dlg.Height = 340;
                dlg.MinimizeBox = false;
                dlg.MaximizeBox = false;
                dlg.FormBorderStyle = FormBorderStyle.FixedDialog;
                dlg.ShowInTaskbar = false;

                var txt = new TextBox()
                {
                    Multiline = true,
                    ReadOnly = true,
                    ScrollBars = ScrollBars.Vertical,
                    Dock = DockStyle.Fill,
                    Text = message,
                    Font = new Font(FontFamily.GenericMonospace, 9F)
                };

                var panelButtons = new FlowLayoutPanel()
                {
                    Dock = DockStyle.Bottom,
                    Height = 44,
                    FlowDirection = FlowDirection.RightToLeft,
                    Padding = new Padding(8)
                };

                var btnOk = new Button() { Text = "OK", Width = 80, DialogResult = DialogResult.OK };

                var btnCopy = new Button() { Text = "Copy to clipboard", Width = 130 };
                btnCopy.Click += (s, e) =>
                {
                    try { Clipboard.SetText(message); }
                    catch { /* presse-papier parfois verrouillé par une autre appli, pas grave */ }
                };

                panelButtons.Controls.Add(btnOk);   // RightToLeft : ajouté en 1er = tout à droite
                panelButtons.Controls.Add(btnCopy);

                dlg.Controls.Add(txt);
                dlg.Controls.Add(panelButtons);
                dlg.AcceptButton = btnOk;

                dlg.ShowDialog(this);
            }
        }

        // Un stderr = un crash quasi certain (le script n'écrit jamais là-dedans en fonctionnement
        // normal). On force l'affichage du détail technique et on alerte, une seule fois par session
        // (les tracebacks Lua tiennent sur plusieurs lignes, pas la peine de répéter l'alerte).
        private void ShowCrashWarning(string firstErrorLine)
        {
            if (_errorWarningShown)
                return;
            _errorWarningShown = true;

            progressRunning.Visible = false;

            if (!_showTechnicalLog)
                ToggleTechnicalLog();

            lblStatus.Text = "Error detected in the script";
            lblStatus.ForeColor = Color.Red;

            lblStage.Text = "The script ran into an error - see the technical detail below.";
            lblStage.ForeColor = Color.Red;

            btnForceExit.Visible = true;

            ShowCopyableErrorDialog(
                "ScriptsMod - Error",
                "The Lua script ran into an error:\r\n\r\n" + firstErrorLine +
                "\r\n\r\nThe technical detail is shown in the window.\r\n\r\n" +
                "If it looks stuck: try \"Refresh display\", or \"Force close\" " +
                "if nothing happens, or close this window to stop it.");
        }

        private void ShowFatalStopWarning(string message)
        {
            if (_errorWarningShown)
                return;
            _errorWarningShown = true;

            progressRunning.Visible = false;

            if (!_showTechnicalLog)
                ToggleTechnicalLog();

            lblStatus.Text = "Mission generator stopped";
            lblStatus.ForeColor = Color.Red;

            lblStage.Text = "The mission generator stopped - see the detail below.";
            lblStage.ForeColor = Color.Red;

            btnForceExit.Visible = true;

            ShowCopyableErrorDialog(
                "ScriptsMod - Generator stopped",
                "The mission generator stopped and cannot continue:\r\n\r\n" + message +
                "\r\n\r\nThe technical detail is shown in the window.\r\n\r\n" +
                "If it looks stuck: try \"Refresh display\", or \"Force close\" " +
                "if nothing happens, or close this window to stop it.");
        }

        private void ParseCycleMarkerLine(string line)
        {
            _headerCycle = line.Substring(CycleMarkerPrefix.Length);
            RefreshHeaderLabel();
        }

        // Filet de sécurité si le script est resté bloqué en REPL interactif après un crash (plus
        // d'os.exit atteint) : le REPL de luae.exe évalue ce qu'on lui envoie comme du code Lua, donc
        // lui envoyer "os.exit()" littéralement suffit en général à le faire quitter proprement.
        private void SendForceExit()
        {
            if (!_bridge.IsRunning)
                return;

            AppendOutputLine("[FORCE CLOSE] os.exit() sent");
            _bridge.SendLine("os.exit()");
        }

        private void RefreshHeaderLabel()
        {
            if (panelHeader == null) return;

            lblHeaderTitle.Text = _headerCampaignTitle;
            lblHeaderVersion.Text = string.IsNullOrEmpty(_headerVersion) ? "" : _headerVersion;
            lblHeaderVersion.Location = new Point(lblHeaderTitle.Right + 8, lblHeaderTitle.Top + 3);

            lblHeaderSubtitle.Text = "Aircraft: " + _headerAircraft + "   |   " + _headerSquadron + " - " + _headerCountry
                + (string.IsNullOrEmpty(_headerStartDate) ? "" : "   |   Start: " + _headerStartDate);

            string extra = "";
            if (!string.IsNullOrEmpty(_headerCurrentMissionDate))
                extra += "Mission date: " + _headerCurrentMissionDate;
            if (!string.IsNullOrEmpty(_headerDayNight))
                extra += (extra.Length > 0 ? "   " : "") + "(" + _headerDayNight + ")";
            if (!string.IsNullOrEmpty(_headerMetar))
                extra += (extra.Length > 0 ? "   |   " : "") + _headerMetar;
            if (!string.IsNullOrEmpty(_headerCycle))
                extra += (extra.Length > 0 ? "   |   " : "") + "Cycle: " + _headerCycle + " / 20";

            lblHeaderExtra.Text = extra;
            lblHeaderExtra.Visible = extra.Length > 0;

            lblHeaderBadge.Text = string.IsNullOrEmpty(_headerDebug) ? "" : "[" + _headerDebug + "]";
            lblHeaderBadge.Visible = !string.IsNullOrEmpty(_headerDebug);
            if (lblHeaderBadge.Visible)
                lblHeaderBadge.Location = new Point(panelHeader.ClientSize.Width - lblHeaderBadge.Width - 10, 8);

            // Visible pour le CampaignMaker, ou pour un Player si le debug est déjà actif
            // (évite qu'un joueur ne puisse jamais désactiver un debug laissé actif par erreur).
            bool showDebugCheckboxes = ParamConf.UserLevel == UserLevel.CampaignMaker || GetDebugFlag("Debug.debug");
            chkDebugMode.Visible = showDebugCheckboxes;
            chkShowFlight.Visible = showDebugCheckboxes;

            if (showDebugCheckboxes)
            {
                int badgeBottom = lblHeaderBadge.Visible ? lblHeaderBadge.Bottom : 8;

                // Même X pour les deux (aligné sur le plus large des deux libellés),
                // plutôt qu'un alignement à droite indépendant qui décale les cases
                // selon la longueur du texte.
                int chkX = panelHeader.ClientSize.Width - Math.Max(chkDebugMode.Width, chkShowFlight.Width) - 10;

                chkDebugMode.Location = new Point(chkX, badgeBottom + 6);
                chkShowFlight.Location = new Point(chkX, chkDebugMode.Bottom + 2);
            }
        }

        private void ParseHeaderMarkerLine(string line)
        {
            string[] parts = line.Substring(HeaderMarkerPrefix.Length).Split('|');
            if (parts.Length != 6) return;

            _headerCampaignTitle = parts[0];
            _headerVersion = parts[1];
            _headerAircraft = parts[2];
            _headerSquadron = parts[3];
            _headerCountry = parts[4];
            _headerDebug = parts[5];
            RefreshHeaderLabel();
        }

        private void ParseTimeMarkerLine(string line)
        {
            string[] parts = line.Substring(TimeMarkerPrefix.Length).Split('|');
            if (parts.Length != 2) return;

            string formatted = parts[0] + "  " + parts[1];

            if (string.IsNullOrEmpty(_headerStartDate))
                _headerStartDate = formatted; // 1re valeur reçue = avant le début de la génération : figée

            _headerDate = formatted; // mise à jour à chaque cycle suivant
            RefreshHeaderLabel();
        }

        private void ParseDayNightMarkerLine(string line)
        {
            _headerDayNight = line.Substring(DayNightMarkerPrefix.Length);
            RefreshHeaderLabel();
        }

        private TargetOption ParseTargetMarkerLine(string line)
        {
            string[] parts = line.Substring(TargetMarkerPrefix.Length).Split('|');
            if (parts.Length != 5) return null;

            int idx;
            if (!int.TryParse(parts[0], out idx)) return null;

            var entry = new TargetOption { Index = idx, Side = parts[1], Name = parts[2], AlivePct = parts[3], Priority = parts[4] };
            _targetOptions.Add(entry);
            return entry;
        }

        private TargetOption ParseCsarTargetMarkerLine(string line)
        {
            string[] parts = line.Substring(CsarTargetMarkerPrefix.Length).Split('|');
            if (parts.Length != 3) return null;

            int idx;
            if (!int.TryParse(parts[0], out idx)) return null;

            var entry = new TargetOption { Index = idx, Name = parts[1], Mgrs = parts[2] };
            _targetOptions.Add(entry);
            return entry;
        }

        private void ShowTargetListControls()
        {
            ClearQuickOptionsPanel();
            SetStageCaption("Choose a target");

            var panel = new Panel() { Width = 820, Height = 170 };

            _txtTargetFilter = new TextBox() { Dock = DockStyle.Top, Height = 24 };
            _txtTargetFilter.TextChanged += (s, e) => FilterTargetList();

            var btnSendTarget = new Button() { Text = "Send selected target", Dock = DockStyle.Bottom, Height = 30 };
            btnSendTarget.Click += (s, e) => SendSelectedTarget();

            _lstTargets = new ListBox() { Dock = DockStyle.Fill, Font = new Font(Font.FontFamily, 9.5F) };
            _lstTargets.DoubleClick += (s, e) => SendSelectedTarget();

            panel.Controls.Add(_txtTargetFilter);
            panel.Controls.Add(btnSendTarget);
            panel.Controls.Add(_lstTargets);

            panelQuickOptions.Controls.Add(panel);

            foreach (TargetOption t in _targetOptions)
                _lstTargets.Items.Add(t);
        }

        private void FilterTargetList()
        {
            if (_lstTargets == null) return;

            string filter = _txtTargetFilter.Text.Trim();
            _lstTargets.BeginUpdate();
            _lstTargets.Items.Clear();
            foreach (TargetOption t in _targetOptions)
            {
                if (filter.Length == 0 || t.Name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0)
                    _lstTargets.Items.Add(t);
            }
            _lstTargets.EndUpdate();
        }

        private void SendSelectedTarget()
        {
            var selected = _lstTargets.SelectedItem as TargetOption;
            if (selected == null)
            {
                MessageBox.Show("Select a target from the list before sending.");
                return;
            }

            SendInput(selected.Index.ToString());
        }

        // ---- Sélection de paquets de vol déjà générés ("tout d'un coup") ----

        // Construit l'écran complet une fois que TOUTES les sections (tous les vols demandés)
        // et TOUTES leurs lignes candidates ont été reçues (signalé par ##DCEM_FLIGHTPREVIEW_DONE##).
        private void BuildAllFlightSectionsUI()
        {
            ClearQuickOptionsPanel();
            SetStageCaption("Choose your flight packages");
            UpdateStepper(2);
            _inputMode = InputMode.FlightOptionSelect;

            _flightOptionButtonsByGlobalIndex.Clear();

            var container = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Width = 820
            };

            int sectionNumber = 0;
            foreach (FlightWishSection section in _flightWishSections)
            {
                sectionNumber++;

                var header = new Label
                {
                    Text = "Flight " + sectionNumber + " - need " + section.Nb + "x " + section.Type + " (" + section.Task + "):",
                    AutoSize = true,
                    Font = new Font(Font.FontFamily, 9F, FontStyle.Bold),
                    Margin = new Padding(0, 10, 0, 2)
                };
                container.Controls.Add(header);

                section.QuotaLabel = new Label
                {
                    Text = section.Entries.Count == 0
                        ? "No compatible package available - skipped automatically"
                        : "0 / " + section.Nb + " selected",
                    AutoSize = true,
                    ForeColor = section.Entries.Count == 0 ? Color.DarkOrange : Color.Gray,
                    Margin = new Padding(0, 0, 0, 4)
                };
                container.Controls.Add(section.QuotaLabel);

                foreach (FlightOptionEntry entry in section.Entries)
                {
                    string label = "x " + entry.AircraftType + " | " + entry.GroupName;
                    if (!string.IsNullOrWhiteSpace(entry.Target))
                        label += " | " + entry.Target;

                    // Une ligne = [NumericUpDown éditable 1-4] + [Bouton de sélection du paquet]
                    var rowPanel = new Panel { Width = 800, Height = 24, Margin = new Padding(0, 0, 0, 2) };

                    var nud = new NumericUpDown
                    {
                        Minimum = 1,
                        Maximum = 4,
                        Value = entry.ForcedNb,
                        Width = 45,
                        Height = 22,
                        Location = new Point(0, 0),
                        TextAlign = HorizontalAlignment.Center
                    };
                    entry.ForcedNbControl = nud;

                    var button = new Button
                    {
                        Text = label,
                        AutoSize = false,
                        Width = 745,
                        Height = 22,
                        Location = new Point(50, 0),
                        Font = new Font(Font.FontFamily, 8.5F),
                        TextAlign = ContentAlignment.MiddleLeft,
                        Padding = new Padding(8, 0, 0, 0),
                        Tag = entry
                    };
                    _toolTip.SetToolTip(button, entry.Nb + "x proposé par le système - " + label);

                    FlightWishSection capturedSection = section;
                    button.Click += (s, e) => ToggleFlightOptionSelection(capturedSection, entry, button);

                    nud.ValueChanged += (s, e) =>
                    {
                        entry.ForcedNb = (int)nud.Value;
                        RefreshAllFlightSectionsState();
                    };

                    section.ButtonsByIndex[entry.Index] = button;

                    List<Button> siblings;
                    if (!_flightOptionButtonsByGlobalIndex.TryGetValue(entry.Index, out siblings))
                    {
                        siblings = new List<Button>();
                        _flightOptionButtonsByGlobalIndex[entry.Index] = siblings;
                    }
                    siblings.Add(button);

                    rowPanel.Controls.Add(nud);
                    rowPanel.Controls.Add(button);
                    container.Controls.Add(rowPanel);
                }
            }

            // Rangée horizontale : "Skip" à gauche (action négative), "Confirm" à droite
            // (action positive) - convention volontaire pour limiter les clics par erreur.
            var buttonRow = new Panel { Width = 800, Height = 40, Margin = new Padding(0, 10, 0, 10) };

            var btnSkip = new Button { Text = "Skip mission", Width = 150, Height = 26, Location = new Point(0, 7) };
            btnSkip.Click += (s, e) => SkipAllFlightSections();
            buttonRow.Controls.Add(btnSkip);

            _btnConfirmAllFlights = new Button
            {
                Text = "Confirm all flights",
                Width = 220,
                Height = 32,
                Enabled = false,
                Location = new Point(buttonRow.Width - 220, 4)
            };
            _btnConfirmAllFlights.Click += (s, e) => ConfirmAllFlightSections();
            buttonRow.Controls.Add(_btnConfirmAllFlights);

            container.Controls.Add(buttonRow);

            panelQuickOptions.Controls.Add(container);

            _flightOptionUIBuilt = true;
        }

        private void ToggleFlightOptionSelection(FlightWishSection section, FlightOptionEntry entry, Button button)
        {
            if (section.Selected.Contains(entry))
                section.Selected.Remove(entry);
            else
                section.Selected.Add(entry);

            RefreshAllFlightSectionsState();
        }

        // Recalcule tout à chaque clic (jeux de données petits, pas besoin d'optimiser) :
        // - le quota par section (nombre d'avions choisis / nombre demandé)
        // - l'exclusivité entre sections : un même paquet physique ne peut être pris qu'une fois,
        //   même s'il apparaît dans plusieurs sections (même type d'avion demandé deux fois)
        private void RefreshAllFlightSectionsState()
        {
            var takenIndexes = new HashSet<int>();
            foreach (FlightWishSection s in _flightWishSections)
                foreach (FlightOptionEntry e in s.Selected)
                    takenIndexes.Add(e.Index);

            bool allQuotasReached = true;

            foreach (FlightWishSection s in _flightWishSections)
            {
                // Le quota se calcule maintenant sur le chiffre FORCÉ (choisi dans le NumericUpDown),
                // plus sur le chiffre brut proposé par le script.
                int selectedNb = 0;
                foreach (FlightOptionEntry e in s.Selected)
                    selectedNb += e.ForcedNb;

                if (s.QuotaLabel != null && s.Entries.Count > 0)
                {
                    s.QuotaLabel.Text = selectedNb + " / " + s.Nb + " selected";
                    s.QuotaLabel.ForeColor = selectedNb >= s.Nb ? Color.SeaGreen : Color.Gray;
                }

                bool quotaReached = selectedNb >= s.Nb || s.Entries.Count == 0;
                if (!quotaReached) allQuotasReached = false;

                foreach (KeyValuePair<int, Button> kv in s.ButtonsByIndex)
                {
                    var e = kv.Value.Tag as FlightOptionEntry;
                    bool isSelectedHere = e != null && s.Selected.Contains(e);
                    bool takenElsewhere = e != null && takenIndexes.Contains(e.Index) && !isSelectedHere;

                    kv.Value.Enabled = isSelectedHere || (!quotaReached && !takenElsewhere);
                    kv.Value.BackColor = isSelectedHere ? Color.LightGreen : SystemColors.Control;

                    if (e != null && e.ForcedNbControl != null)
                        e.ForcedNbControl.Enabled = kv.Value.Enabled;
                }
            }

            if (_btnConfirmAllFlights != null)
                _btnConfirmAllFlights.Enabled = allQuotasReached && _flightWishSections.Count > 0;
        }

        // Fin de séquence "envoi automatique des choix déjà décidés" : remet l'écran en état
        // normal. À appeler DÈS que la file est vide, sinon tous les prompts suivants du Lua
        // sont ignorés en silence (écran figé, Lua bloqué sur son io.stdin:read()).
        private void EndMultiFlightOptionSequence()
        {
            _multiFlightOptionActive = false;
            _flightOptionUIBuilt = false;
            _inputMode = InputMode.QuickOptions;
        }

        // Envoie tous les choix (section par section, dans l'ordre) - la vraie boucle Lua les
        // consomme un par un via ses propres io.read(), sans plus rien réafficher côté écran
        // puisqu'on ignore désormais tout ce qui arrive tant que _multiFlightOptionActive est vrai.
        private void ConfirmAllFlightSections()
        {
            _pendingFlightOptionAnswers.Clear();

            foreach (FlightWishSection s in _flightWishSections)
                foreach (FlightOptionEntry e in s.Selected)
                    _pendingFlightOptionAnswers.Enqueue(e.Index + "F" + e.ForcedNb);

            if (_pendingFlightOptionAnswers.Count == 0)
                return;

            _multiFlightOptionActive = true;

            foreach (FlightWishSection s in _flightWishSections)
            {
                foreach (Button b in s.ButtonsByIndex.Values)
                    b.Enabled = false;

                foreach (FlightOptionEntry e in s.Entries)
                    if (e.ForcedNbControl != null)
                        e.ForcedNbControl.Enabled = false;
            }

            if (_btnConfirmAllFlights != null)
                _btnConfirmAllFlights.Enabled = false;

            string first = _pendingFlightOptionAnswers.Dequeue();
            UpdateStepper(3);
            SendQueuedAnswer(first);

            // Cas très courant depuis le forçage : un seul paquet suffit à couvrir un vol de 4
            // avions, donc la file est déjà vide après ce premier envoi.
            if (_pendingFlightOptionAnswers.Count == 0)
                EndMultiFlightOptionSequence();
        }

        // On n'envoie plus jamais "s" : côté Lua ça tire un index AU HASARD parmi TOUS les avions
        // jouables (pas seulement ceux compatibles avec le vol demandé), ce qui peut reboucler
        // très longtemps si peu d'entrées sont compatibles. On choisit nous-mêmes le premier
        // paquet compatible ("Selectable") de chaque section et on envoie directement son index -
        // un seul aller-retour garanti, aucun tirage au sort, aucune boucle possible.
        // Envoie "s" une fois par vol demandé - fiable désormais grâce au patch Lua
        // (ATO_PlayerAssign.lua, ligne 848 : "until tabIndex[groupNChoice] or TaskRefused").
        // Réutilise exactement le même mécanisme de file que ConfirmAllFlightSections.
        private void SkipAllFlightSections()
        {
            _pendingFlightOptionAnswers.Clear();

            foreach (FlightWishSection s in _flightWishSections)
                _pendingFlightOptionAnswers.Enqueue("s");

            if (_pendingFlightOptionAnswers.Count == 0)
                return;

            _multiFlightOptionActive = true;

            foreach (FlightWishSection s in _flightWishSections)
                foreach (Button b in s.ButtonsByIndex.Values)
                    b.Enabled = false;
            if (_btnConfirmAllFlights != null)
                _btnConfirmAllFlights.Enabled = false;

            UpdateStepper(1); // on quitte l'écran Packages sans l'avoir validé

            string first = _pendingFlightOptionAnswers.Dequeue();
            SendQueuedAnswer(first);

            if (_pendingFlightOptionAnswers.Count == 0)
                EndMultiFlightOptionSequence();
        }      

        // Mesure la largeur du texte le plus long PARMI CE LOT de propositions (pas tout le script),
        // pour que tous les boutons de ce menu aient la même largeur, ajustée au contenu.
        private int ComputeUniformButtonWidth(IEnumerable<string> labels)
        {
            int maxTextWidth = 0;
            var font = new Font(Font.FontFamily, 10F);

            foreach (string label in labels)
            {
                Size measured = TextRenderer.MeasureText(label, font);
                if (measured.Width > maxTextWidth)
                    maxTextWidth = measured.Width;
            }

            int width = maxTextWidth + 40; // marge pour le padding interne du bouton
            int panelAvailableWidth = panelQuickOptions.ClientSize.Width > 40 ? panelQuickOptions.ClientSize.Width - 30 : 800;

            return width > panelAvailableWidth ? panelAvailableWidth : width;
        }

        // Bouton générique : envoie "value" directement au clic, affiche "label".
        private void AddSendButton(string value, string label, int width)
        {
            var button = new Button()
            {
                Text = label,
                AutoSize = false,
                Width = width,
                Height = 34,
                Font = new Font(Font.FontFamily, 10F),
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(8, 0, 0, 0),
                Tag = value
            };
            _toolTip.SetToolTip(button, label);
            button.Click += (s, e) => SendInput((string)((Button)s).Tag);

            //panelQuickOptions.Controls.Add(button);
            CurrentOptionsHost().Controls.Add(button);
        }
        private void HandlePromptMarker(string promptType)
        {
            // Cas générique "yesno", avec ou sans titre personnalisé (yesno::<texte>).
            // Traité en dehors du switch ci-dessous car StartsWith() n'y est pas pratique.
            if (promptType == "yesno" || promptType.StartsWith("yesno::"))
            {
                _inputMode = InputMode.YesNo;
                string caption = promptType.StartsWith("yesno::")
                    ? promptType.Substring("yesno::".Length)
                    : "Continue?";
                ShowYesNoControl(caption);
                return;
            }

            switch (promptType)
            {
                case "flightcompose":
                    if (_multiFlightComposerActive && _pendingFlightAnswers.Count > 0)
                    {
                        string nextAnswer = _pendingFlightAnswers.Dequeue();
                        SendQueuedAnswer(nextAnswer);

                        if (_pendingFlightAnswers.Count == 0)
                        {
                            _multiFlightComposerActive = false;
                            _pendingFlightCount = 0;
                        }
                    }
                    else if (_multiFlightComposerActive)
                    {
                        _multiFlightComposerActive = false;
                        _pendingFlightCount = 0;
                        _flightPromptPending = true;
                        _flightBuilderControlsBuilt = false;
                    }
                    else
                    {
                        _flightPromptPending = true;
                        _flightBuilderControlsBuilt = false;

                        if (_aircraftById.Count > 0)
                        {
                            _inputMode = InputMode.FlightBuilder;

                            if (_pendingFlightCount > 1)
                                ShowMultiFlightComposer();
                            else
                                ShowFlightBuilderControls();
                        }
                    }
                    break;

                case "flightcount":
                    _inputMode = InputMode.Numeric;
                    ShowFlightCountControl();
                    break;

                case "flightoption":

                    if (_multiFlightOptionActive && _pendingFlightOptionAnswers.Count > 0)
                    {
                        // La vraie boucle Lua redemande le prochain choix pour continuer à
                        // consommer la file déjà décidée par l'utilisateur.
                        string nextAnswer = _pendingFlightOptionAnswers.Dequeue();
                        SendQueuedAnswer(nextAnswer);

                        if (_pendingFlightOptionAnswers.Count == 0)
                            EndMultiFlightOptionSequence();
                    }
                    else if (_multiFlightOptionActive)
                    {
                        // Filet de sécurité : le Lua redemande un choix alors que notre file est
                        // vide (un index a été refusé, ou nos choix ne couvraient pas tous les
                        // vols). On rend la main à l'affichage normal plutôt que de rester figé.
                        EndMultiFlightOptionSequence();
                    }
                    else if (_flightOptionUIBuilt)
                    {
                        // L'écran est déjà construit et on attend encore le clic sur
                        // "Confirm all flights" : on ignore, c'est lui la source de vérité.
                    }
                    break;

                case "debriefaccept":
                    _inputMode = InputMode.DebriefAccept;
                    ShowDebriefAcceptControl();
                    break;

                case "acceptmission":
                    UpdateStepper(3); // dernière étape : "Confirm" - ce menu (a/s) est final,
                                      // qu'il y ait eu ou non une sélection de packages avant
                    break;

                case "targetlist":
                    _targetOptions.Clear();
                    _lstTargets = null;
                    _targetListPending = true;
                    break;
            }
        }
        //private void HandlePromptMarker(string promptType)
        //{

        //    switch (promptType)
        //    {
        //        case "flightcompose":
        //            if (_multiFlightComposerActive && _pendingFlightAnswers.Count > 0)
        //            {
        //                string nextAnswer = _pendingFlightAnswers.Dequeue();
        //                SendQueuedAnswer(nextAnswer);

        //                if (_pendingFlightAnswers.Count == 0)
        //                {
        //                    _multiFlightComposerActive = false;
        //                    _pendingFlightCount = 0;
        //                }
        //            }
        //            else if (_multiFlightComposerActive)
        //            {
        //                _multiFlightComposerActive = false;
        //                _pendingFlightCount = 0;
        //                _flightPromptPending = true;
        //                _flightBuilderControlsBuilt = false;
        //            }
        //            else
        //            {
        //                _flightPromptPending = true;
        //                _flightBuilderControlsBuilt = false;

        //                if (_aircraftById.Count > 0)
        //                {
        //                    _inputMode = InputMode.FlightBuilder;

        //                    if (_pendingFlightCount > 1)
        //                        ShowMultiFlightComposer();
        //                    else
        //                        ShowFlightBuilderControls();
        //                }
        //            }
        //            break;

        //        case "flightcount":
        //            _inputMode = InputMode.Numeric;
        //            ShowFlightCountControl();
        //            break;

        //        case "flightoption":

        //            if (_multiFlightOptionActive && _pendingFlightOptionAnswers.Count > 0)
        //            {
        //                // La vraie boucle Lua redemande le prochain choix pour continuer à
        //                // consommer la file déjà décidée par l'utilisateur.
        //                string nextAnswer = _pendingFlightOptionAnswers.Dequeue();
        //                SendQueuedAnswer(nextAnswer);

        //                if (_pendingFlightOptionAnswers.Count == 0)
        //                {
        //                    _multiFlightOptionActive = false;
        //                    _flightOptionUIBuilt = false;
        //                    _inputMode = InputMode.QuickOptions; // sinon plus aucun futur menu ne s'affichera
        //                }
        //            }
        //            else if (_flightOptionUIBuilt)
        //            {
        //                // La vraie boucle a démarré (ou continue) pendant qu'on attend encore le clic de
        //                // l'utilisateur sur "Confirm all flights" - on ignore complètement, l'écran déjà
        //                // construit reste la seule source de vérité tant qu'il n'a pas validé.
        //            }
        //            else
        //            {
        //                // Vraiment un nouveau cycle de prévisualisation qui démarre.
        //                _flightWishSections.Clear();
        //                _currentBuildingSection = null;
        //                _inputMode = InputMode.FlightOptionSelect;
        //            }
        //            break;


        //        case "debriefaccept":
        //            _inputMode = InputMode.DebriefAccept;
        //            ShowDebriefAcceptControl();
        //            break;

        //        case "yesno":
        //            _inputMode = InputMode.YesNo;
        //            ShowYesNoControl();
        //            break;

        //        case "acceptmission":
        //            UpdateStepper(3); // dernière étape : "Confirm" - ce menu (a/s) est final,
        //                              // qu'il y ait eu ou non une sélection de packages avant
        //            break;

        //        case "targetlist":
        //            _targetOptions.Clear();
        //            _lstTargets = null;
        //            _targetListPending = true;
        //            break;
        //    }
        //}

        private const string MetarMarkerPrefix = "##DCEM_METAR##";

        // Ne garde que le tout premier METAR reçu, comme demandé - s'il y en a plusieurs (plusieurs
        // porte-avions par ex.), les suivants sont ignorés.
        private void ParseMetarMarkerLine(string line)
        {
            if (!string.IsNullOrEmpty(_headerMetar))
                return;

            _headerMetar = line.Substring(MetarMarkerPrefix.Length);
            RefreshHeaderLabel();
        }

        private void ParseMissionDateMarkerLine(string line)
        {
            _headerCurrentMissionDate = line.Substring(MissionDateMarkerPrefix.Length);
            RefreshHeaderLabel();
        }

        // Reçoit le chemin (relatif au dossier de la campagne) du .txt de debrief déjà écrit par
        // DEBRIEF_Master.lua, le charge et l'affiche à la place du panneau de propositions habituel.
        // Redevient invisible automatiquement dès qu'un nouveau prompt réel arrive, voir
        // ClearQuickOptionsPanel().
        private void ShowDebriefText(string relativePath)
        {
            string fullPath = Path.Combine(_workingDirectory, relativePath);

            string content;
            try
            {
                content = File.ReadAllText(fullPath);
            }
            catch (Exception ex)
            {
                content = "Unable to read debriefing file:\r\n" + fullPath + "\r\n\r\n" + ex.Message;
            }

            txtDebrief.Clear();
            txtDebrief.Text = content;

            // Met en gras les lignes de titre de section : une ligne suivie d'une ligne de tirets,
            // convention utilisée par DEBRIEF_Text.lua ("Order of Battle:" / "----------------").
            string[] lines = content.Replace("\r\n", "\n").Split('\n');
            int charIndex = 0;
            for (int i = 0; i < lines.Length; i++)
            {
                bool isHeaderLine = i + 1 < lines.Length &&
                                     lines[i + 1].Length > 0 &&
                                     lines[i + 1].TrimEnd().All(c => c == '-');

                if (isHeaderLine && lines[i].Trim().Length > 0)
                {
                    txtDebrief.Select(charIndex, lines[i].Length);
                    txtDebrief.SelectionFont = new Font(txtDebrief.Font, FontStyle.Bold);
                }

                charIndex += lines[i].Length + 1; // +1 pour le \n remis par le Split
            }
            txtDebrief.SelectionStart = 0;
            txtDebrief.SelectionLength = 0;

            _debriefText = content;
            btnViewDebrief.Visible = true;

            lblStage.Text = "Debriefing";
            pnlDebriefActions.Controls.Clear();
            panelQuickOptions.Visible = false;
            pnlDebrief.Visible = true;
        }

        // Popup non-destructive pour revoir le texte du debrief à tout moment après coup
        // (bouton "View debriefing"), sans toucher à l'état de l'écran de génération en cours.
        // Largeur calculée sur la ligne la plus longue pour éviter tout ascenseur horizontal,
        // plafonnée à 90% de la largeur de l'écran au cas où une ligne serait démesurée.
        private void ShowDebriefPopup()
        {
            if (string.IsNullOrEmpty(_debriefText))
                return;

            var font = new Font(FontFamily.GenericMonospace, 9F);

            string longestLine = "";
            foreach (string line in _debriefText.Replace("\r\n", "\n").Split('\n'))
            {
                if (line.Length > longestLine.Length)
                    longestLine = line;
            }

            int textWidth = TextRenderer.MeasureText(longestLine, font).Width;
            int desiredWidth = textWidth + 60; // marges + ascenseur vertical

            Rectangle workingArea = Screen.FromControl(this).WorkingArea;
            int maxWidth = (int)(workingArea.Width * 0.9);
            int minWidth = 500;

            int dlgWidth = Math.Max(minWidth, Math.Min(desiredWidth, maxWidth));

            using (var dlg = new Form())
            {
                dlg.Text = "Debriefing";
                dlg.StartPosition = FormStartPosition.CenterParent;
                dlg.Width = dlgWidth;
                dlg.Height = 600;
                dlg.MinimizeBox = false;
                dlg.MaximizeBox = true;
                dlg.FormBorderStyle = FormBorderStyle.Sizable;

                var txt = new RichTextBox()
                {
                    Dock = DockStyle.Fill,
                    ReadOnly = true,
                    Font = font,
                    Text = _debriefText,
                    WordWrap = false,
                    ScrollBars = RichTextBoxScrollBars.Both
                };

                var panelButtons = new FlowLayoutPanel()
                {
                    Dock = DockStyle.Bottom,
                    Height = 44,
                    FlowDirection = FlowDirection.RightToLeft,
                    Padding = new Padding(8)
                };

                var btnOk = new Button() { Text = "Close", Width = 90, DialogResult = DialogResult.OK };

                var btnCopy = new Button() { Text = "Copy to clipboard", Width = 140 };
                btnCopy.Click += (s, e) =>
                {
                    try { Clipboard.SetText(_debriefText); }
                    catch { /* presse-papier parfois verrouillé par une autre appli, pas grave */ }
                };

                panelButtons.Controls.Add(btnOk);
                panelButtons.Controls.Add(btnCopy);

                dlg.Controls.Add(txt);
                dlg.Controls.Add(panelButtons);
                dlg.AcceptButton = btnOk;

                dlg.ShowDialog(this);
            }
        }

        // ---- Debug.debug / Debug.AfficheFlight : lecture/écriture directe dans conf_mod.lua ----
        // Remplace l'ancien "s+"/"s-" tapé au clavier : plus de valeur ici, la persistance
        // se fait dans conf_mod.lua et prend effet au PROCHAIN lancement de luae.exe
        // (dofile("Init/conf_mod.lua") en tout début de script), pas sur la session en cours.

        private bool GetDebugFlag(string path)
        {
            object value;
            if (_confModData != null && _confModData.Values.TryGetValue(path, out value))
                return Convert.ToBoolean(value);

            return false;
        }

        private void SetDebugFlag(string path, bool value)
        {
            if (_confModData == null) return;

            _confModData.Values[path] = value;
            new ConfModWriter().Save(_confModData);
        }

        private void ChkDebugMode_CheckedChanged(object sender, EventArgs e)
        {
            SetDebugFlag("Debug.debug", chkDebugMode.Checked);

            // Retour visuel immédiat sur le badge - voir la remarque plus bas sur ce
            // que ça représente vraiment.
            _headerDebug = chkDebugMode.Checked ? "DEBUG ON" : "";
            RefreshHeaderLabel();
        }

        private void ChkShowFlight_CheckedChanged(object sender, EventArgs e)
        {
            SetDebugFlag("Debug.AfficheFlight", chkShowFlight.Checked);
        }



    }
}
