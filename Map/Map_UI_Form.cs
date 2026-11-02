using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
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
        private Label _lblInfo;
        private Label _lblStatus;
        private ListView _listSquads;
        private ListBox _listNoPos;
        private Dictionary<string, string> _aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private CheckBox _chkDrawings, _chkZones, _chkSpawn, _chkPlans, _chkThreats;
        private ComboBox _cmbStyle;
        private Button _btnEdit, _btnAdd, _btnTrash, _btnMove, _btnSave, _btnQuit;

        // Zones wargame de chaque mode : la couleur d'une base suit le camp qui tient la zone où elle se trouve
        private List<WargameZoneData> _zonesInit = new List<WargameZoneData>();
        private List<WargameZoneData> _zonesActive = new List<WargameZoneData>();

        public Map_UI_Form(string campaignName, bool missionMode = false)
        {
            _campaign = campaignName ?? "";
            _missionMode = missionMode;

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

            var tip = new ToolTip { AutoPopDelay = 15000 };

            // --- Carte (au milieu) ---
            _view = new Map_UI_View { Dock = DockStyle.Fill };
            _view.BaseSelected += OnBaseClicked;

            // --- Panneau de droite : squadrons de la base choisie ---
            var right = new Panel { Dock = DockStyle.Right, Width = 430, Padding = new Padding(6) };

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
            tip.SetToolTip(_btnEdit, "Edit the selected squadron (the changes are applied at once; use Save to write the file)");
            tip.SetToolTip(_btnMove, "Move the selected squadron: then click the destination base on the map (Esc = cancel)");
            buttons.Controls.Add(_btnEdit);
            buttons.Controls.Add(_btnMove);

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
            _listSquads.Columns.Add("Squadron", 160);
            _listSquads.Columns.Add("Aircraft", 90);
            _listSquads.Columns.Add("Ready", 50, HorizontalAlignment.Right);
            _listSquads.Columns.Add("Reserve", 55, HorizontalAlignment.Right);
            _listSquads.Columns.Add("Info", 70);
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
            right.Controls.Add(_listSquads);
            right.Controls.Add(noPosPanel);
            right.Controls.Add(buttons);
            right.Controls.Add(_lblInfo);
            right.Controls.Add(_lblBase);

            // --- Bande d'outils à gauche (de la place pour en ajouter d'autres dessous) ---
            var strip = new Panel { Dock = DockStyle.Left, Width = 38, BackColor = Color.FromArgb(243, 243, 243) };

            _chkRuler = MakeToggle("⟷", 6);
            _chkRuler.CheckedChanged += (s, e) => _view.RulerEnabled = _chkRuler.Checked;
            tip.SetToolTip(_chkRuler, "Ruler (km / Nm), like the DCS F10 map: click point A, move the mouse (live distance), click point B. Right-click or Esc = clear. Untick to go back to normal clicks.");

            var btnFit = MakeToolButton("⛶", 44);
            btnFit.Click += (s, e) => _view.ResetZoom();
            tip.SetToolTip(btnFit, "Reset zoom (whole map)");

            var btnReload = MakeToolButton("⟳", 82);
            btnReload.Click += (s, e) => { LoadFromDisk(); ApplyMode(); };
            tip.SetToolTip(btnReload, "Reload the airbases from the files");

            _chkDrawings = MakeToggle("⚑", 196);
            _chkDrawings.CheckedChanged += (s, e) => _view.SetDrawingsVisible("mission", _chkDrawings.Checked);
            tip.SetToolTip(_chkDrawings, "Show or hide the borders and zones drawn in base_mission.miz (Init folder, mission editor drawings).");

            _chkZones = MakeToggle("⚔", 234);
            _chkZones.CheckedChanged += (s, e) => _view.SetDrawingsVisible("zones", _chkZones.Checked);
            tip.SetToolTip(_chkZones, "Show or hide the wargame zones (Init\\Wargame\\DCE_Generated\\wargame_zones_init.lua). Blue / red = side holding the zone, orange = contested, cyan dashed = landing plan.");

            _chkSpawn = MakeToggle("≋", 272);
            _chkSpawn.CheckedChanged += (s, e) => _view.SetDrawingsVisible("spawn", _chkSpawn.Checked);
            tip.SetToolTip(_chkSpawn, "Show or hide the wargame spawn areas (Init\\Wargame\\wargame_spawn.miz): yellow lines = roads, green = forest, brown = mountain, grey = town / base.");

            _chkBackground = MakeToggle("▤", 120);
            _chkBackground.CheckedChanged += ChkBackground_CheckedChanged;
            tip.SetToolTip(_chkBackground, "Show or hide the web map (OpenStreetMap). It is downloaded and placed automatically the first time, using the real airports (ICAO codes).");

            var btnRebuild = MakeToolButton("↺", 158);
            btnRebuild.Click += (s, e) => RebuildBackground();
            tip.SetToolTip(btnRebuild, "Rebuild the web map and its calibration");

            strip.Controls.Add(_chkRuler);
            strip.Controls.Add(btnFit);
            strip.Controls.Add(btnReload);
            strip.Controls.Add(_chkBackground);
            strip.Controls.Add(btnRebuild);
            strip.Controls.Add(_chkDrawings);
            strip.Controls.Add(_chkZones);
            strip.Controls.Add(_chkSpawn);

            if (_missionMode)
            {
                _chkPlans = MakeToggle("✈", 310);
                _chkPlans.CheckedChanged += (s, e) => _view.SetDrawingsVisible("plans", _chkPlans.Checked);
                tip.SetToolTip(_chkPlans, "Show or hide the flight plans of the players (skill Client / Player): waypoints and legs.");

                _chkThreats = MakeToggle("◎", 348);
                _chkThreats.CheckedChanged += (s, e) => _view.SetDrawingsVisible("threats", _chkThreats.Checked);
                tip.SetToolTip(_chkThreats, "Show or hide the threat range of the SAM sites that are not hidden in the mission (blue / red = side). Ranges are approximate maximums.");

                strip.Controls.Add(_chkPlans);
                strip.Controls.Add(_chkThreats);
            }

            // --- Barre du haut ---
            var top = new Panel { Dock = DockStyle.Top, Height = 34 };
            var lbl = new Label { Text = "Viewing:", Location = new Point(10, 9), AutoSize = true };
            _radioInit = new RadioButton { Text = "Init", Location = new Point(75, 7), AutoSize = true };
            _radioActive = new RadioButton { Text = "Active", Location = new Point(140, 7), AutoSize = true };
            _radioInit.CheckedChanged += (s, e) => { if (_radioInit.Checked && !_loading) ModeChanged(); };
            _radioActive.CheckedChanged += (s, e) => { if (_radioActive.Checked && !_loading) ModeChanged(); };
            _chkHideEmpty = new CheckBox { Text = "Hide bases with no squadron", Location = new Point(230, 7), AutoSize = true };
            _chkHideEmpty.CheckedChanged += (s, e) => { if (!_loading) ApplyMode(); };
            _btnSave = new Button { Text = "Save", Location = new Point(470, 3), Size = new Size(150, 28) };
            _btnSave.Click += (s, e) => SaveCurrentMode();
            tip.SetToolTip(_btnSave, "Write the squadrons of the viewed list (Init or Active) in the campaign file");
            _btnQuit = new Button { Text = "Quit", Size = new Size(80, 28), Location = new Point(1000, 3), Anchor = AnchorStyles.Top | AnchorStyles.Right };
            _btnQuit.Click += (s, e) => Close();      // OnFormClosing pose la question s'il reste des changements non sauvés
            tip.SetToolTip(_btnQuit, "Close the map. If squadron changes are not saved, you will be asked what to do");
            top.Resize += (s, e) => _btnQuit.Left = top.ClientSize.Width - _btnQuit.Width - 10;
            top.Controls.Add(_btnQuit);
            top.Controls.Add(lbl);
            top.Controls.Add(_radioInit);
            top.Controls.Add(_radioActive);
            top.Controls.Add(_chkHideEmpty);
            top.Controls.Add(_btnSave);

            var lblStyle = new Label { Text = "Map style:", Location = new Point(640, 9), AutoSize = true };
            _cmbStyle = new ComboBox { Location = new Point(710, 5), Width = 330, DropDownStyle = ComboBoxStyle.DropDownList };
            foreach (Map_Style st in Map_Web.Styles) _cmbStyle.Items.Add(st.Label);
            _cmbStyle.SelectedIndex = Math.Max(0, Map_Web.Styles.FindIndex(x => x.Id == Map_Web.CurrentStyle.Id));
            _cmbStyle.SelectionChangeCommitted += (s, e) => StyleChanged();
            tip.SetToolTip(_cmbStyle, "Look of the web map. Tiles are downloaded once per style, then kept for all campaigns. Satellite and shaded relief have no names at all.");
            top.Controls.Add(lblStyle);
            top.Controls.Add(_cmbStyle);

            Controls.Add(_view);
            Controls.Add(right);
            Controls.Add(strip);
            Controls.Add(top);

            // --- Ligne d'état en bas ---
            _lblStatus = new Label { Dock = DockStyle.Bottom, Height = 40, Padding = new Padding(8, 4, 8, 0), Text = "" };
            Controls.Add(_lblStatus);
        }

        private static Button MakeButton(string text, int x, int width)
        {
            return new Button { Text = text, Location = new Point(x, 3), Size = new Size(width, 28) };
        }

        private static CheckBox MakeToggle(string symbol, int y)
        {
            var chk = new CheckBox
            {
                Text = symbol,
                Appearance = Appearance.Button,
                TextAlign = ContentAlignment.MiddleCenter,
                Font = new Font("Segoe UI Symbol", 15f),
                Location = new Point(3, y),
                Size = new Size(32, 32),
                FlatStyle = FlatStyle.Flat,
                TabStop = false
            };
            chk.FlatAppearance.BorderColor = Color.Silver;
            chk.FlatAppearance.CheckedBackColor = Color.Gold;
            return chk;
        }

        private static Button MakeToolButton(string symbol, int y)
        {
            var b = new Button
            {
                Text = symbol,
                Font = new Font("Segoe UI Symbol", 15f),
                Location = new Point(3, y),
                Size = new Size(32, 32),
                FlatStyle = FlatStyle.Flat,
                TabStop = false
            };
            b.FlatAppearance.BorderColor = Color.Silver;
            return b;
        }

        private void UpdateTitle()
        {
            string flag = "";
            if (_dirtyInit || _dirtyActive)
                flag = "   *unsaved (" + (_dirtyInit ? "Init" : "") + (_dirtyInit && _dirtyActive ? " + " : "") + (_dirtyActive ? "Active" : "") + ")";

            Text = (_missionMode ? "Mission map - " : "Map - ") + _campaign + (_readOnly ? "   (read only)" : "") + flag;
            _btnSave.Text = "Save " + (_radioInit.Checked ? "Init" : "Active") + " squadrons";
            _btnSave.Enabled = !_readOnly;
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
            _view.ShowBackground = true;
            ShowDefaultStatus();
            SetBackgroundChecked(true);
        }

        // Bases qui ont un code ICAO et une position : ce sont elles qui servent à caler la carte
        private List<Map_Match> BuildMatchInputs()
        {
            var list = new List<Map_Match>();
            foreach (KeyValuePair<string, AirbaseInfo> kv in _airbases)
            {
                AirbaseInfo a = kv.Value;
                string icao = null;
                if (a.Code == null || !a.Code.TryGetValue("ICAO", out icao) || string.IsNullOrWhiteSpace(icao))
                    continue;

                bool placed = !(a.X == 0 && a.Y == 0) && Math.Abs(a.X) < 1e7 && Math.Abs(a.Y) < 1e7;
                if (!placed) continue;

                list.Add(new Map_Match { Name = kv.Key, Icao = icao.Trim(), X = a.X, Y = a.Y });
            }
            return list;
        }

        // Téléchargement + calage, sans rien demander : on le fait une seule fois, tout reste en cache ensuite
        private async Task BuildBackgroundAsync()
        {
            if (_busy || IsDisposed)
                return;

            List<Map_Match> inputs = BuildMatchInputs();
            // Toutes les bases posées sur la carte : elles servent à choisir la zone à télécharger
            var allPoints = new List<double[]>();
            foreach (AirbaseInfo a in _airbases.Values)
            {
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
            try
            {
                var progress = new Progress<string>(msg => { if (!IsDisposed) _lblStatus.Text = msg; });
                Map_Background bg = await Task.Run(() => Map_Web.BuildBackground(_campaign, inputs, allPoints, progress));

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
                    _chkBackground.Enabled = true;
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
            try
            {
                string file = Path.Combine(ParamConf.PATH_SavedGames_DCS, @"Mods\tech\DCE\Missions\Campaigns", _campaign, "Init", "various_table.lua");
                if (!File.Exists(file)) return;

                bool inBlock = false;
                foreach (string raw in File.ReadAllLines(file))
                {
                    string line = raw.Trim();
                    if (line.StartsWith("--")) continue;
                    if (line.StartsWith("BaseNameAlias")) { inBlock = true; continue; }
                    if (!inBlock) continue;
                    if (line.StartsWith("}")) break;

                    var m = System.Text.RegularExpressions.Regex.Match(line, "^\\[\"(.+?)\"\\]\\s*=\\s*\"(.*?)\"");
                    if (m.Success)
                        _aliases[m.Groups[1].Value] = m.Groups[2].Value;
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

        // Frontières / zones du fichier mission : lues une fois, affichées par défaut s'il y en a
        private void LoadDrawings()
        {
            SetGroup("mission", Map_Drawings.Load(_campaign), _chkDrawings, true);
            SetGroup("zones", Map_Drawings.LoadWargameZones(_campaign, _radioActive.Checked), _chkZones, true);
            SetGroup("spawn", Map_Drawings.LoadWargameSpawn(_campaign), _chkSpawn, false);

            if (_missionMode)
            {
                Map_MissionData data = Map_Drawings.LoadMission(_campaign);
                SetGroup("plans", data.Plans, _chkPlans, true);
                SetGroup("threats", data.Threats, _chkThreats, true);

                _missionInfo = data.Error != ""
                    ? data.Error
                    : "Mission: " + Path.GetFileName(data.Source) + "   |   " + data.PlayerFlights + " player flight(s), " + data.SamSites + " SAM site(s) shown";
                _lblStatus.Text = _missionInfo;
            }
        }

        private string _missionInfo = "";

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
                        Alias = AliasOf(kv.Key)
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

        private void OpenEditor(Squad squad)
        {
            if (!EnsureContext())
                return;

            try
            {
                using (var frm = new SquadEdit_Form(squad, _ctx, false, "MapEdit"))
                {
                    frm.ShowDialog(this);
                }
            }
            catch (Exception ex)
            {
                FormUtils.LogRegister("Map_UI_Form | SquadEdit_Form : " + ex);
                MessageBox.Show("The squadron could not be edited: " + ex.Message, "Map");
            }
        }

        private void EditSelected()
        {
            Squad squad = SelectedSquad();
            if (squad == null || _readOnly)
                return;

            OpenEditor(squad);

            // Les changements de la fenêtre d'édition sont appliqués en direct sur le squadron
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
            OpenEditor(main);
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
            UpdateTitle();
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
            if (!_readOnly && (_dirtyInit || _dirtyActive) && (e.CloseReason == CloseReason.UserClosing || e.CloseReason == CloseReason.None))
            {
                string what = (_dirtyInit ? "Init" : "") + (_dirtyInit && _dirtyActive ? " + " : "") + (_dirtyActive ? "Active" : "");
                DialogResult answer = MessageBox.Show(
                    "The squadron changes (" + what + ") have not been saved.\n\n"
                    + "Yes = Save, then quit\nNo = Quit WITHOUT saving (the changes are lost)\nCancel = stay on the map",
                    "Unsaved changes", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Warning);

                if (answer == DialogResult.Cancel)
                {
                    e.Cancel = true;
                    return;
                }
                if (answer == DialogResult.Yes)
                {
                    if (_dirtyInit && !SaveFolder(true)) { e.Cancel = true; return; }
                    if (_dirtyActive && !SaveFolder(false)) { e.Cancel = true; return; }
                }
                else
                {
                    RestoreSnapshot(_dirtyInit, _dirtyActive);
                    _dirtyInit = false;
                    _dirtyActive = false;
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
}
