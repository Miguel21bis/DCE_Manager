using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Xml.Linq;
using DCE_Manager.Parameters;
using DCE_Manager.Update;
using DCE_Manager.Utils;

namespace DCE_Manager
{
    // Liste des campagnes (grille de gauche de l'onglet Campaigns), en 5 fichiers (partial) :
    //   Campaigns_List_Grid_Left.cs             : colonnes, style, clic sur une cellule, panneau de droite (ce fichier)
    //   Campaigns_List_Grid_Left.Load.cs        : chargement de la liste des campagnes dans la grille
    //   Campaigns_List_Grid_Left.QuickActions.cs: les icônes First / Skip / Debrief (+ clic droit = options de lancement)
    //   Campaigns_List_Grid_Left.Family.cs      : la colonne Family (maître / filles)
    //   Campaigns_List_Grid_Left.Maintenance.cs : suppression, renommage, réparation
    public partial class CampaignGridLeft
    {

        // Cache des images de campagnes (évite rechargement disque)
        private Dictionary<string, Image> campaignImageCache = new Dictionary<string, Image>();
        //private CampaignEdit _currentCampaignEdit;
        private static readonly HashSet<string> _alreadyUpdated = new HashSet<string>();

        // Noms (dossier de campagne OU fichier orphelin) détectés comme incomplets/orphelins
        // lors du dernier LoadCampaignsAsync(). Sert à limiter les actions possibles sur ces
        // lignes dans la grid à Delete/Folder (voir GridCampaigns_CellClick).
        private readonly HashSet<string> _incompleteOrOrphanNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Sous-ensemble de _incompleteOrOrphanNames concernant un dossier de campagne incomplet
        // (pas un fichier orphelin isolé) : seuls ceux-là peuvent afficher l'icône 🔧 Repair,
        // un fichier orphelin seul n'a pas de "campagne" à réparer derrière.
        private readonly HashSet<string> _repairableIncompleteNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Liste centralisée pour éviter la divergence entre le test de complétude (LoadCampaignsAsync)
        // et le calcul "encore manquant après réparation" (RepairCampaignAsync).
        private static readonly string[] RequiredInitFiles =
            { "camp_init.lua", "camp_triggers_init.lua", "conf_mod.lua", "db_airbases.lua", "targetlist_init.lua", "oob_air_init.lua", "path.bat" };

        // ScriptsMod installé ? Calculé UNE fois par chargement de la grille (pas à chaque dessin).
        // true par défaut : pas de faux avertissement avant le premier calcul.
        private bool _scriptsModInstalled = true;

        // Recalcule l'état de ScriptsMod et redessine la grille seulement si ça a changé.
        // Appelée au chargement de la grille et après une installation de ScriptsMod.
        public void RefreshScriptsModState()
        {
            bool installed = false;

            if (!string.IsNullOrWhiteSpace(ParamConf.PATH_SavedGames_DCS))
            {
                string folder = Path.Combine(ParamConf.PATH_SavedGames_DCS, @"Mods\tech\DCE\ScriptsMod.NG");

                try { installed = Directory.Exists(folder) && Directory.EnumerateFileSystemEntries(folder).Any(); }
                catch { installed = false; }
            }

            if (installed != _scriptsModInstalled)
            {
                _scriptsModInstalled = installed;

                if (installed)
                    _alreadyUpdated.Clear();   // ScriptsMod vient d'arriver : toutes les campagnes doivent être recalées sur ses fichiers de référence

                _mainForm.dataGridViewCampaigns.Invalidate();
            }
        }

        // Compte à jour après chaque LoadCampaignsAsync(). Utilisable par Main_Form pour
        // afficher "Installed Campaigns" (voir le panneau INFO).
        public int InstalledCampaignCount { get; private set; }
        public int IncompleteOrOrphanCount { get; private set; }

        // Marqueur posé sur la dernière ligne de la grid (celle qui porte l'icône corbeille de
        // suppression groupée). Sert à la reconnaître au clic et à l'exclure partout où on
        // parcourt les lignes de campagnes.
        private const string DeleteSelectedRowTag = "DELETE_SELECTED_ROW";


        // Référence vers la Form principale
        private readonly Main_Form _mainForm;

        private static bool _referenceWarningShown = false;


        public Campaign_Edit_Grid_Right CurrentCampaignEdit { get; private set; }


        // Constructeur qui reçoit la référence de Main_Form
        public CampaignGridLeft(Main_Form mainForm)
        {
            _mainForm = mainForm;
        }

        private void GridCampaigns_Init()
        {
            _mainForm.dataGridViewCampaigns.Columns.Clear();

            // ===== CONFIG GLOBALE =====
            _mainForm.dataGridViewCampaigns.AllowUserToResizeColumns = true;
            _mainForm.dataGridViewCampaigns.AllowUserToResizeRows = true;
            _mainForm.dataGridViewCampaigns.RowHeadersVisible = false;
            _mainForm.dataGridViewCampaigns.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            _mainForm.dataGridViewCampaigns.MultiSelect = false;

            // IMPORTANT : sans ça, la grid ajoute d'office une ligne vierge en bas de liste (la
            // ligne "nouvelle entrée", repérable à son "False" dans la colonne à cocher). Elle
            // n'est ni une campagne ni supprimable, et elle fausse Rows.Count (donc le calcul de
            // repositionnement après suppression).
            _mainForm.dataGridViewCampaigns.AllowUserToAddRows = false;

            // ===== COLONNE CLONE =====
            GridCampaigns_AddButtonColumn("Clone", "＋", 40);

            // ===== IMAGE =====
            var colImg = new DataGridViewImageColumn()
            {
                Name = "Image",
                HeaderText = "",
                Width = 90,
                ImageLayout = DataGridViewImageCellLayout.Zoom,
                DefaultCellStyle = { NullValue = null }   // pas de croix rouge quand il n'y a pas d'image
            };
            _mainForm.dataGridViewCampaigns.Columns.Add(colImg);

            // ===== TEXTE =====
            _mainForm.dataGridViewCampaigns.Columns.Add(new DataGridViewTextBoxColumn()
            {
                Name = "Name",
                HeaderText = "Campaign",
                Width = 250 // avant : AutoSizeMode Fill, qui empêchait le redimensionnement à la souris
            });

            // Colonne pour ouvrir le dossier
            GridCampaigns_AddButtonColumn("Folder", "📂", 55);

            // Colonne pour exporter la campagne en .zip (distribution vers un autre PC)
            //GridCampaigns_AddButtonColumn("Export", "📦", 55);
            GridCampaigns_AddButtonColumn("Export", "Export", 70);


            _mainForm.dataGridViewCampaigns.Columns.Add(new DataGridViewTextBoxColumn()
            {
                Name = "Version",
                Width = 60,
                DefaultCellStyle = { Alignment = DataGridViewContentAlignment.MiddleCenter }

            });

            _mainForm.dataGridViewCampaigns.Columns.Add(new DataGridViewTextBoxColumn()
            {
                Name = "Missions",
                Width = 50,
                DefaultCellStyle = { Alignment = DataGridViewContentAlignment.MiddleCenter }

            });

            _mainForm.dataGridViewCampaigns.Columns.Add(new DataGridViewTextBoxColumn()
            {
                Name = "Aircraft",
                Width = 90
            });

            // ===== BOUTONS =====

            GridCampaigns_AddQuickActionsColumn(); // First + Skip + Debrief regroupés dans une seule case
            GridCampaigns_AddButtonColumn("CampaignSetup", "🛠", 55, headerText: "Setup");
            GridCampaigns_AddButtonColumn("Parameters", "⚙", 55);
            GridCampaigns_AddButtonColumn("Delete", "🗑", 55);

            // Case à cocher pour sélectionner plusieurs lignes "problème" (incomplet/orphelin)
            // et les supprimer d'un coup via le bouton Delete existant.
            // IMPORTANT : ReadOnly=true volontairement. La bascule est faite à la main dans
            // GridCampaigns_CellClick (affectation directe de Value, qui marche même sur une
            // cellule ReadOnly). Laisser l'édition native active créait un conflit : la grid et
            // notre code basculaient la valeur chacun de leur côté, et le clic paraissait sans
            // effet. Ça rend aussi le comportement indépendant d'un éventuel ReadOnly global
            // posé sur la grid dans le Designer, qui empêcherait toute édition native.
            _mainForm.dataGridViewCampaigns.Columns.Add(new DataGridViewCheckBoxColumn()
            {
                Name = "Select",
                HeaderText = "☑",
                Width = 40,
                ReadOnly = true
            });

            // Colonne invisible : mémorise si un debrief est en attente pour la ligne
            // (fichiers de transition présents). "1" = en attente. Lue par
            // GridCampaigns_QuickActions_CellPainting et ...CellMouseClick.
            _mainForm.dataGridViewCampaigns.Columns.Add(new DataGridViewTextBoxColumn()
            {
                Name = "DebriefPending",
                Visible = false
            });

            // ===== COLONNE FAMILLE (maître/fille) =====
            // Ajoutée en DERNIER exprès : Rows.Add(...) plus bas matche les valeurs par
            // ordre des colonnes (pas par DisplayIndex), donc l'ajouter ici ne décale
            // aucun des appels positionnels existants. DisplayIndex la replace juste
            // après "Clone" visuellement.
            _mainForm.dataGridViewCampaigns.Columns.Add(new DataGridViewTextBoxColumn()
            {
                Name = "Family",
                HeaderText = "",
                ToolTipText = "Left button: collapse all families. Right button: expand all families.",
                Width = 105,
                ReadOnly = true,
                SortMode = DataGridViewColumnSortMode.NotSortable, // sinon cliquer l'en-tête trierait les lignes
                DefaultCellStyle = { Alignment = DataGridViewContentAlignment.MiddleCenter }
            });
            _mainForm.dataGridViewCampaigns.Columns["Family"].DisplayIndex = 1;

            // ===== COLONNE TRIGGERS (⚡) =====
            // Ajoutée en dernier (comme Rename) pour ne pas décaler les Rows.Add positionnels.
            GridCampaigns_AddButtonColumn("Triggers", "⚡", 55);
            _mainForm.dataGridViewCampaigns.Columns["Triggers"].DisplayIndex =
                _mainForm.dataGridViewCampaigns.Columns["CampaignSetup"].DisplayIndex; // juste avant Setup

            // ===== COLONNE RENAME (✏) =====
            // Ajoutée en dernier (comme Family) pour ne pas décaler les Rows.Add positionnels.
            GridCampaigns_AddButtonColumn("Rename", "✏", 45, headerText: "");
            _mainForm.dataGridViewCampaigns.Columns["Rename"].DisplayIndex = 4; // juste après Name

            // ===== COLONNE WARGAME (🗺) : icône dessinée à la main (grisée si pas de wargame) =====
            GridCampaigns_AddButtonColumn("Wargame", "🗺", 55, headerText: "Wargame");
            _mainForm.dataGridViewCampaigns.Columns["Wargame"].DisplayIndex =
                _mainForm.dataGridViewCampaigns.Columns["Triggers"].DisplayIndex;
            _mainForm.dataGridViewCampaigns.CellPainting += GridCampaigns_Wargame_CellPainting;
            _mainForm.dataGridViewCampaigns.CellClick += GridCampaigns_Wargame_CellClick;


            // ===== STYLE BOUTONS =====
            foreach (DataGridViewColumn col in _mainForm.dataGridViewCampaigns.Columns)
            {
                //bloquer le redimensionnement
                //col.Resizable = DataGridViewTriState.False;

                if (col is DataGridViewButtonColumn)
                {
                    col.DefaultCellStyle.BackColor = Color.FromArgb(230, 230, 230);
                    col.DefaultCellStyle.ForeColor = Color.Black;
                    col.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
                    col.DefaultCellStyle.Font = new Font("Segoe UI", 14, FontStyle.Bold);
                }
            }

            // "Export" porte du texte, pas une icône : la police 14 Bold commune aux
            // boutons serait illisible/trop grande ici.
            if (_mainForm.dataGridViewCampaigns.Columns.Contains("Export"))
            {
                _mainForm.dataGridViewCampaigns.Columns["Export"].DefaultCellStyle.Font = new Font("Segoe UI", 8, FontStyle.Regular);
            }
        }
        private void GridCampaigns_InitStyle()
        {
            //######## STYLE ##########
            _mainForm.dataGridViewCampaigns.BackgroundColor = Color.FromArgb(240, 240, 240);

            _mainForm.dataGridViewCampaigns.DefaultCellStyle.BackColor = Color.White;
            _mainForm.dataGridViewCampaigns.DefaultCellStyle.ForeColor = Color.Black;

            _mainForm.dataGridViewCampaigns.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(245, 245, 245);

            _mainForm.dataGridViewCampaigns.DefaultCellStyle.SelectionBackColor = Color.FromArgb(0, 120, 215);
            _mainForm.dataGridViewCampaigns.DefaultCellStyle.SelectionForeColor = Color.White;

            _mainForm.dataGridViewCampaigns.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(230, 230, 230);
            _mainForm.dataGridViewCampaigns.ColumnHeadersDefaultCellStyle.ForeColor = Color.Black;

            _mainForm.dataGridViewCampaigns.CellBorderStyle = DataGridViewCellBorderStyle.None;
            _mainForm.dataGridViewCampaigns.RowTemplate.Height = 60;

            _mainForm.dataGridViewCampaigns.DefaultCellStyle.Padding = new Padding(0);

            // DisableResizing : sans ça, le mode auto du Designer ignore la hauteur demandée
            _mainForm.dataGridViewCampaigns.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
            _mainForm.dataGridViewCampaigns.ColumnHeadersHeight = 45;

            // SCROLL
            _mainForm.dataGridViewCampaigns.ScrollBars = ScrollBars.Both;

            // SCROLL//empêche le mode “compression”
            _mainForm.dataGridViewCampaigns.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;

            _mainForm.dataGridViewCampaigns.CellMouseEnter += (s, e) =>
            {
                if (e.RowIndex >= 0)
                    _mainForm.dataGridViewCampaigns.Rows[e.RowIndex].DefaultCellStyle.BackColor = Color.FromArgb(220, 235, 252);
            };

            _mainForm.dataGridViewCampaigns.CellMouseLeave += (s, e) =>
            {
                if (e.RowIndex >= 0)
                    _mainForm.dataGridViewCampaigns.Rows[e.RowIndex].DefaultCellStyle.BackColor =
                        (e.RowIndex % 2 == 0) ? Color.White : Color.FromArgb(245, 245, 245);
            };

            //############# GridCampaigns_Init END
        }

        // Initialise entièrement le DataGridView des campagnes.
        // Pourquoi : allège le constructeur et regroupe toute la configuration du grid au même endroit.
        public void GridCampaigns_Init_DataGridView()
        {
            // Double buffering : DoubleBuffered est protégée sur DataGridView, d'où la reflection
            typeof(DataGridView).GetProperty("DoubleBuffered",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                .SetValue(_mainForm.dataGridViewCampaigns, true, null);

            GridCampaigns_Init();
            GridCampaigns_InitStyle();

            _mainForm.dataGridViewCampaigns.CellClick += GridCampaigns_CellClick;
            _mainForm.dataGridViewCampaigns.CellPainting += GridCampaigns_QuickActions_CellPainting;
            _mainForm.dataGridViewCampaigns.CellMouseClick += GridCampaigns_QuickActions_CellMouseClickAsync;
            _mainForm.dataGridViewCampaigns.CellMouseMove += GridCampaigns_QuickActions_CellMouseMove;
            _mainForm.dataGridViewCampaigns.CellMouseLeave += GridCampaigns_QuickActions_CellMouseLeave;

            _mainForm.dataGridViewCampaigns.CellPainting += GridCampaigns_Family_CellPainting;
            _mainForm.dataGridViewCampaigns.CellMouseClick += GridCampaigns_Family_CellMouseClickAsync;
            _mainForm.dataGridViewCampaigns.CellMouseMove += GridCampaigns_Family_CellMouseMove;
            _mainForm.dataGridViewCampaigns.CellMouseLeave += GridCampaigns_Family_CellMouseLeave;
            _mainForm.dataGridViewCampaigns.ColumnHeaderMouseClick += GridCampaigns_Family_HeaderMouseClickAsync;
            _mainForm.dataGridViewCampaigns.CellPainting += GridCampaigns_Family_HeaderPainting;

            _mainForm.dataGridViewCampaigns.ShowCellToolTips = true; // true par défaut, explicite pour être sûr


            _mainForm.dataGridViewCampaigns.RowTemplate.Height = 70;

            _mainForm.dataGridViewCampaigns.DefaultCellStyle.Font = new Font("Segoe UI", 9);
            _mainForm.dataGridViewCampaigns.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI", 9, FontStyle.Bold);

            _mainForm.dataGridViewCampaigns.EnableHeadersVisualStyles = false;
            _mainForm.dataGridViewCampaigns.ColumnHeadersDefaultCellStyle.BackColor = SystemColors.Control;
            _mainForm.dataGridViewCampaigns.ColumnHeadersDefaultCellStyle.ForeColor = Color.Black;

            _mainForm.dataGridViewCampaigns.GridColor = Color.LightGray;
            _mainForm.dataGridViewCampaigns.BorderStyle = BorderStyle.None;

            UpdateCampaignSetupColumnVisibility();

            GridCampaigns_ApplyColumnWidths();

            // Fige Name (comme "Figer les volets" sur Excel). Frozen fige AUSSI toutes les colonnes
            // affichées à sa gauche : Clone, Family, Image restent donc visibles avec le nom.
            _mainForm.dataGridViewCampaigns.Columns["Name"].Frozen = true;
        }


        // Remet les largeurs de colonnes voulues, quel que soit le mode de redimensionnement
        // que le Designer ou un autre réglage aurait imposé à la grid.
        private void GridCampaigns_ApplyColumnWidths()
        {
            var grid = _mainForm.dataGridViewCampaigns;

            grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;

            var widths = new Dictionary<string, int>
            {
                { "Clone", 40 },
                { "Family", 105 },
                { "Image", 90 },
                { "Name", 250 },
                { "Rename", 45 },
                { "Folder", 55 },
                { "Export", 70 },
                { "Version", 60 },
                { "Missions", 50 },
                { "Aircraft", 90 },
                { "QuickActions", 100 },
                { "CampaignSetup", 55 },
                { "Triggers", 55 },
                { "Wargame", 55 },
                { "Parameters", 55 },
                { "Delete", 55 },
                { "Select", 40 }
            };

            foreach (var kvp in widths)
            {
                if (grid.Columns.Contains(kvp.Key))
                {
                    grid.Columns[kvp.Key].AutoSizeMode = DataGridViewAutoSizeColumnMode.None;
                    grid.Columns[kvp.Key].Width = kvp.Value;
                }
            }
        }

        private void GridCampaigns_AddButtonColumn(string name, string text, int width, bool useColumnTextForButtonValue = true, string headerText = null)
        {
            _mainForm.dataGridViewCampaigns.Columns.Add(new DataGridViewButtonColumn()
            {
                Name = name,
                HeaderText = headerText ?? name, // si non précisé, garde l'ancien comportement (Name brut)
                Text = text,
                UseColumnTextForButtonValue = useColumnTextForButtonValue,
                Width = width,
                FlatStyle = FlatStyle.Flat
            });
        }

        public void UpdateCampaignSetupColumnVisibility()
        {
            bool isCampaignMaker = ParamConf.UserLevel == UserLevel.CampaignMaker;

            if (_mainForm.dataGridViewCampaigns.Columns.Contains("CampaignSetup"))
            {
                _mainForm.dataGridViewCampaigns.Columns["CampaignSetup"].Visible = isCampaignMaker;
            }

            // Export : distribuer une campagne n'a de sens que pour celui qui l'a conçue.
            if (_mainForm.dataGridViewCampaigns.Columns.Contains("Export"))
            {
                _mainForm.dataGridViewCampaigns.Columns["Export"].Visible = isCampaignMaker;
            }

            // Triggers : réservé à celui qui conçoit la campagne.
            if (_mainForm.dataGridViewCampaigns.Columns.Contains("Triggers"))
            {
                _mainForm.dataGridViewCampaigns.Columns["Triggers"].Visible = isCampaignMaker;
            }

            // Wargame : même règle (et l'onglet Wargame change de vue joueur / campaignMaker)
            if (_mainForm.dataGridViewCampaigns.Columns.Contains("Wargame"))
            {
                _mainForm.dataGridViewCampaigns.Columns["Wargame"].Visible = isCampaignMaker;
            }

            _mainForm.CampaignView?.RefreshWargameTab(ParamCampaignSelected.NameCampaign);
        }

        // Dessine l'icône Wargame : noire si la campagne a un wargame, grisée sinon.
        // Rien sur la ligne corbeille ni sur les lignes "problème".
        private void GridCampaigns_Wargame_CellPainting(object sender, DataGridViewCellPaintingEventArgs e)
        {
            var grid = _mainForm.dataGridViewCampaigns;

            if (e.RowIndex < 0 || e.ColumnIndex < 0 || grid.Columns[e.ColumnIndex].Name != "Wargame")
                return;

            e.PaintBackground(e.ClipBounds, true);
            e.Handled = true;

            var row = grid.Rows[e.RowIndex];
            string name = row.Cells["Name"].Value?.ToString();

            if (DeleteSelectedRowTag.Equals(row.Tag) || string.IsNullOrEmpty(name) || _incompleteOrOrphanNames.Contains(name))
                return;

            bool hasWargame = row.Cells[e.ColumnIndex].Tag is bool b && b;
            Color color = hasWargame ? Color.Black : Color.FromArgb(190, 190, 190);

            using (var font = new Font("Segoe UI", 14, FontStyle.Bold))
            {
                TextRenderer.DrawText(e.Graphics, "🗺", font, e.CellBounds, color,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }
        }

        // Clic sur l'icône (campaignMaker seulement) : crée le wargame s'il n'existe pas encore, puis ouvre l'éditeur.
        private void GridCampaigns_Wargame_CellClick(object sender, DataGridViewCellEventArgs e)
        {
            var grid = _mainForm.dataGridViewCampaigns;

            if (e.RowIndex < 0 || e.ColumnIndex < 0 || grid.Columns[e.ColumnIndex].Name != "Wargame")
                return;

            if (ParamConf.UserLevel != UserLevel.CampaignMaker)
                return;

            var row = grid.Rows[e.RowIndex];
            string name = row.Cells["Name"].Value?.ToString();

            if (DeleteSelectedRowTag.Equals(row.Tag) || string.IsNullOrEmpty(name) || _incompleteOrOrphanNames.Contains(name))
                return;

            if (!DCE_Manager.UserControls.ucCampaign.CampaignHasWargame(name))
            {
                string mizPath = Path.Combine(WargameZoneRepository.GetCampaignFolder(name), "Init", "Wargame", "wargame_zone.miz");

                if (!File.Exists(mizPath))
                {
                    MessageBox.Show(
                        "To create a wargame, first draw its zones in the DCS Mission Editor\r\nand save the mission as:\r\n\r\n" + mizPath,
                        "Create a wargame", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                var confirm = MessageBox.Show(
                    "Create a wargame for '" + name + "' ?\r\n\r\nThe zones are read from wargame_zone.miz, then the wargame editor opens.",
                    "Create a wargame", MessageBoxButtons.YesNo, MessageBoxIcon.Question);

                if (confirm != DialogResult.Yes)
                    return;
            }

            using (var form = new WargameEditor_Form(name))
            {
                form.ShowDialog(_mainForm);
            }

            // Mise à jour de la ligne sur place (pas de rechargement de la grid : le clic est encore en cours)
            bool hasWargame = DCE_Manager.UserControls.ucCampaign.CampaignHasWargame(name);
            row.Cells["Wargame"].Tag = hasWargame;
            row.Cells["Wargame"].ToolTipText = hasWargame ? "Edit the wargame of this campaign" : "Create a wargame for this campaign";
            grid.InvalidateCell(row.Cells["Wargame"]);
        }

        // Lance FirstMission.bat / SkipMission.bat / DEBUG_DebriefMission.bat via le GUI
        // ScriptsModRunner_Form (marqueurs ##DCEM_...##) au lieu d'ouvrir une fenêtre console brute.
        // luaScriptName : uniquement pour les .bat hors convention BAT_xxx.lua (cf. Debrief).
        private void OpenScriptsModRunner(string folderPath, string batFileName, string campaignName, string luaScriptName = null)
        {
            string batPath = Path.Combine(folderPath, batFileName);

            if (!File.Exists(batPath))
                return;

            using (var form = new ScriptsModRunner_Form(batPath, folderPath, campaignName, luaScriptName))
            {
                form.ShowDialog(_mainForm);
            }
        }

        // Extrait de GridCampaigns_CellClick : recale camp_init.lua puis conf_mod.lua sur
        // leurs fichiers de référence, une fois par campagne et par session. Appelé aussi
        // bien depuis GridCampaigns_CellClick (Parameters/CampaignSetup) que depuis
        // GridCampaigns_QuickActions_CellMouseClick (First/Skip/Debrief), qui ne passe plus
        // par GridCampaigns_CellClick pour ces 3 actions.
        private void EnsureCampaignFilesUpToDate(string name)
        {
            if (!_scriptsModInstalled)
                return;   // sans ScriptsMod, pas de fichiers de référence : on ne marque PAS la campagne comme "recalée"

            if (!_alreadyUpdated.Add(name))
                return;

            ConfUpdateResult campInitResult = new CampInitUpdater().UpdateCampaign(name);
            ConfUpdateResult confModResult = new ConfModTemplateUpdater().UpdateCampaign(name); // dans cet ordre

            ConfMod_SunTimes.UpdateConfMod(name);   // dawn / dusk d'après le soleil (une fois par session)

            if (!_referenceWarningShown &&
                (campInitResult == ConfUpdateResult.ReferenceMissing || confModResult == ConfUpdateResult.ReferenceMissing))
            {
                _referenceWarningShown = true;

                MessageBox.Show(
                    "Some reference files used to keep campaign configuration up to date (UTIL_REF_conf_mod.lua and/or UTIL_REF_camp_init.lua) could not be found in your ScriptsMod folder.\r\n\r\nPlease update your ScriptsMod so this feature can work correctly.",
                    "ScriptsMod update needed",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
        }

        private async void GridCampaigns_CellClick(object sender, DataGridViewCellEventArgs e)
        {
            // Ignore header
            if (e.RowIndex < 0 || e.ColumnIndex < 0)
            {
                FormUtils.LogRegister("GridCampaigns_CellClick RETURN A (header ou index negatif)");
                return;
            }

            if (e.ColumnIndex >= _mainForm.dataGridViewCampaigns.Columns.Count)
            {
                FormUtils.LogRegister("GridCampaigns_CellClick RETURN B (colonne hors limites)");
                return;
            }

            string columnName = _mainForm.dataGridViewCampaigns.Columns[e.ColumnIndex].Name;

            var clickedRow = _mainForm.dataGridViewCampaigns.Rows[e.RowIndex];

            // Dernière ligne (corbeille) : elle n'a pas de campagne associée, seul un clic sur
            // la colonne Delete y a du sens. On la traite AVANT le contrôle du nom, sinon le
            // "return si nom vide" plus bas l'intercepterait.
            if (DeleteSelectedRowTag.Equals(clickedRow.Tag))
            {
                if (columnName == "Delete")
                {
                    await DeleteSelectedRowsAsync();
                }

                return;
            }

            // Bascule de la case Select, faite entièrement à la main.
            // Pourquoi : l'affectation directe de Value fonctionne toujours, même si la colonne
            // (ou la grid entière, via le Designer) est en ReadOnly — contrairement à l'édition
            // native, qui ne démarre pas dans ce cas. On n'utilise donc PAS cell.ReadOnly comme
            // condition : une grid globalement ReadOnly ferait remonter ReadOnly=true sur toutes
            // les cellules et bloquerait tout.
            if (columnName == "Select")
            {
                string rowNameForSelect = clickedRow.Cells["Name"].Value?.ToString();

                if (!string.IsNullOrEmpty(rowNameForSelect))
                {
                    var selectCell = clickedRow.Cells["Select"];

                    bool current = selectCell.Value is bool b && b;
                    selectCell.Value = !current;

                    _mainForm.dataGridViewCampaigns.InvalidateCell(selectCell);

                    UpdateDeleteSelectedRow();
                }

                return;
            }

            // Récupérer les infos de la ligne
            string name = _mainForm.dataGridViewCampaigns.Rows[e.RowIndex].Cells["Name"].Value?.ToString();

            if (string.IsNullOrEmpty(name))
            {
                FormUtils.LogRegister("GridCampaigns_CellClick RETURN C (nom de campagne vide)");
                return;
            }

            // Ligne "problème" (dossier incomplet ou fichier orphelin) : Delete et Folder sont
            // toujours permis. Parameters et CampaignSetup le sont aussi si c'est un vrai dossier
            // de campagne (pas un simple fichier orphelin sans dossier) : ConfModTemplateUpdater
            // et CampInitUpdater savent créer conf_mod.lua/camp_init.lua depuis la référence si le
            // fichier local manque encore, pas besoin d'attendre que la campagne soit complète.
            // Les autres colonnes (First/Skip, ouverture normale du panneau de droite) restent
            // bloquées : elles ont besoin de bien plus de fichiers (oob_air_init.lua, targetlist...)
            // pour ne pas planter.
            bool isRepairableFolder = _repairableIncompleteNames.Contains(name);

            bool allowedOnProblemRow = columnName == "Delete" || columnName == "Folder"
                || ((columnName == "Parameters" || columnName == "CampaignSetup") && isRepairableFolder);

            if (_incompleteOrOrphanNames.Contains(name) && !allowedOnProblemRow)
            {
                FormUtils.LogRegister("GridCampaigns_CellClick RETURN ligne 'probleme' : '" + name +
                                      "' est dans _incompleteOrOrphanNames (colonne '" + columnName + "')");
                return;
            }

            EnsureCampaignFilesUpToDate(name);

            string basePath = ParamConf.PATH_SavedGames_DCS + @"\Mods\tech\DCE\Missions\Campaigns\";

            string folderPath = Path.Combine(basePath, name);

            Utils.FormUtils.LogRegister($"Clicked on column '{columnName}' for campaign '{name}' folderPath '{folderPath}'");

            if (columnName == "Parameters")
            {
                Utils.FormUtils.LogRegister(Utils.FormUtils.ToTitleCase("Open Parameters for campaign '" + name + "'"));

                new ConfModTemplateUpdater().UpdateCampaign(name);

                using (var form = new ConfModForm(name))
                {
                    form.ShowDialog(_mainForm);
                }
            }
            else if (columnName == "CampaignSetup")
            {
                Utils.FormUtils.LogRegister(Utils.FormUtils.ToTitleCase("Open Campaign Setup for campaign '" + name + "'"));

                new CampInitUpdater().UpdateCampaign(name);

                string campInitPath = new CampInitUpdater().GetCampInitPath(name);

                using (var form = new ConfModForm(
                    name,
                    campInitPath,
                    "Campaign Setup",
                    "Changes here require restarting the campaign (FirstMission.bat)."))
                {
                    form.ShowDialog(_mainForm);
                }
            }
            else if (columnName == "Triggers")
            {
                Utils.FormUtils.LogRegister(Utils.FormUtils.ToTitleCase("Open Triggers for campaign '" + name + "'"));

                using (var form = new Triggers_Form(name))
                {
                    form.ShowDialog(_mainForm);
                }
                return; // pas d'ouverture du panneau de droite
            }
            else if (columnName == "Delete")
            {
                // Suppression groupée : si des lignes "problème" ont leur case Select cochée,
                // Delete les traite toutes en une fois, peu importe la ligne cliquée.
                if (GetCheckedRows().Count > 1)
                {
                    await DeleteSelectedRowsAsync();
                    return; //  IMPORTANT : stoppe ici
                }

                var confirm = MessageBox.Show(
                    "Delete campaign " + name + " ?",
                    "Confirm",
                    MessageBoxButtons.YesNo
                );

                if (confirm == DialogResult.Yes)
                {
                    DeleteOutcome outcome = null;

                    _mainForm.Cursor = Cursors.WaitCursor;
                    try
                    {
                        outcome = await DeleteCampaignFilesAndFolderAsync(name, folderPath);
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show("Error: " + ex.Message);
                    }
                    finally
                    {
                        _mainForm.Cursor = Cursors.Default;
                    }

                    if (outcome != null)
                    {
                        ShowDeleteReport(new List<(string Name, DeleteOutcome Outcome)> { (name, outcome) });
                    }

                    // Recharge la liste en essayant de rester à peu près à la même position.
                    await LoadCampaignsAsync(restoreRowIndex: e.RowIndex);
                }
                return; //  IMPORTANT : stoppe ici
            }
            else if (columnName == "Clone")
            {
                Campaign_CLONE_ClickOneEvent(null, null, basePath, name);
                return;
            }
            else if (columnName == "Rename")
            {
                await RenameCampaignAsync(name);
                return;
            }
            else if (columnName == "Family")
            {
                // Géré entièrement par GridCampaigns_Family_CellMouseClickAsync (CellMouseClick).
                // Sans ce return, le code plus bas ouvrirait quand même le panneau de droite
                // (CampaignEdit1) à chaque clic sur ★/+/⋯ — tout le calcul Lua qui va avec,
                // pour rien.
                return;
            }
            // Si on clique sur la colonne "Folder"
            // Ouvre le dossier de la campagne dans l'explorateur Windows
            else if (columnName == "Folder")
            {
                if (Directory.Exists(folderPath))
                {
                    System.Diagnostics.Process.Start(new ProcessStartInfo()
                    {
                        FileName = "explorer.exe",
                        Arguments = "\"" + folderPath + "\"",
                        UseShellExecute = true
                    });
                }
                else
                {
                    MessageBox.Show(
                        "Campaign folder not found:\r\n" + folderPath,
                        "Folder",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning
                    );
                }
            }

            // Si on clique sur la colonne "Export"
            // Empaquette la campagne dans un .zip distribuable (Active/Debug/Debriefing vides,
            // Doc et livrees custom selon les cases cochees par l'utilisateur)
            else if (columnName == "Export")
            {
                using (var optionsDlg = new CampaignExportOptions_Form(name))
                {
                    if (optionsDlg.ShowDialog(_mainForm) != DialogResult.OK)
                    {
                        return;
                    }

                    bool includeLiveries = optionsDlg.IncludeLiveries;
                    bool includeDoc = optionsDlg.IncludeDoc;

                    using (var dlg = new SaveFileDialog())
                    {
                        dlg.Filter = "Campaign package (*.zip)|*.zip";
                        dlg.FileName = name + ".zip";
                        dlg.Title = "Export campaign '" + name + "'";

                        if (dlg.ShowDialog() == DialogResult.OK)
                        {
                            List<string> liveryReport = null;
                            bool cancelled = false;

                            // Compression potentiellement tres longue (livrees = centaines de
                            // Mo) : sur un thread du pool, sinon le thread UI ne pompe plus les
                            // messages Windows (ContextSwitchDeadlock, appli figee).
                            using (var progressForm = new CampaignProgress_Form("Export campaign"))
                            {
                                progressForm.Show(_mainForm);
                                _mainForm.Enabled = false;

                                var progress = new Progress<CampaignProgressInfo>(p => progressForm.UpdateProgress(p));
                                IProgress<CampaignProgressInfo> reporter = progress;
                                CancellationToken token = progressForm.Token;

                                try
                                {
                                    liveryReport = await Task.Run(() => CampaignExporter.ExportCampaign(
                                        basePath, name, dlg.FileName, includeLiveries, includeDoc,
                                        p => reporter.Report(p), token), token);
                                }
                                catch (OperationCanceledException)
                                {
                                    cancelled = true;
                                }
                                catch (Exception ex)
                                {
                                    FormUtils.ErrorGeneral_BoxOrLog(ex, "Export campaign", name, true, true);
                                    _mainForm.Enabled = true;
                                    return;
                                }
                                finally
                                {
                                    _mainForm.Enabled = true;
                                }
                            }

                            if (cancelled)
                            {
                                // Zip partiel inutilisable : on le supprime pour ne pas laisser
                                // trainer une archive incomplete.
                                try { if (File.Exists(dlg.FileName)) File.Delete(dlg.FileName); }
                                catch (Exception ex) { FormUtils.LogRegister("Export annule, suppression du zip partiel impossible : " + ex.Message); }

                                MessageBox.Show("Export cancelled.", "Export", MessageBoxButtons.OK, MessageBoxIcon.Information);
                                return;
                            }

                            // Compte-rendu dans une fenetre selectionnable/copiable : les chemins
                            // complets sont longs et l'utilisateur doit pouvoir les recuperer.
                            var warnings = liveryReport.Where(l => !l.StartsWith("Included:")).ToList();
                            if (warnings.Count > 0)
                            {
                                string reportText = "Export done, but:" + Environment.NewLine + Environment.NewLine
                                                  + string.Join(Environment.NewLine + Environment.NewLine, warnings);

                                using (var reportForm = new CampaignExportReport_Form("Export - livery warnings", reportText))
                                {
                                    reportForm.ShowDialog(_mainForm);
                                }
                            }

                            // Ouvre l'explorateur sur le dossier de destination, fichier
                            // selectionne en surbrillance, sans lancer/ouvrir le zip lui-meme.
                            Process.Start(new ProcessStartInfo()
                            {
                                FileName = "explorer.exe",
                                Arguments = "/select,\"" + dlg.FileName + "\"",
                                UseShellExecute = true
                            });
                        }
                    }
                }
                return; // pas d'ouverture du panneau de droite apres un export, comme Delete/Clone
            }

            // GARDE-FOU (point unique) : si le dossier de la campagne ou son fichier Init a
            // disparu — suppression en cours/récente, dossier déplacé, clonage interrompu...
            // — on n'essaie pas d'ouvrir le panneau de droite. Sans ça, CampaignEdit1 déclenche
            // des lectures Lua (oob_air_init.lua notamment) qui peuvent planter toute l'appli
            // avec une NLua.Exceptions.LuaScriptException non gérée proprement.
            if (!Directory.Exists(folderPath) || !File.Exists(Path.Combine(folderPath, "Init", "oob_air_init.lua")))
            {
                Utils.FormUtils.LogRegister($"Campagne '{name}' introuvable ou incomplète (folderPath='{folderPath}') : ouverture annulée, rafraîchissement de la liste.");
                await LoadCampaignsAsync();
                return;
            }

            _mainForm.dataGridViewCampaigns.ClearSelection();
            _mainForm.dataGridViewCampaigns.Rows[e.RowIndex].Selected = true;

            // Coche automatiquement Init ou Active selon le nombre de missions jouées
            string nbMissionText = _mainForm.dataGridViewCampaigns.Rows[e.RowIndex].Cells["Missions"].Value?.ToString();

            int nbMission = 0;
            int.TryParse(nbMissionText, out nbMission);

            // 1. Charger la campagne AVANT
            CampaignEdit1(null, null, folderPath + "\\" + name, name);

            // 2. Ensuite seulement appliquer le state UI
            if (nbMission <= 0)
            {
                Main_Form.Instance.CampaignView.SetOobInitMode(true);
            }
            else
            {
                Main_Form.Instance.CampaignView.SetOobActiveMode(true);
            }
        }

        public void Campaign_CLONE_ClickOneEvent(object sender, EventArgs e, string path, string OldNameCamp)
        {

            // Assurez-vous d'appeler UpdateSharedData avant d'ouvrir Form3_Clonage
            //UpdateSharedData();

            //Test.Form3_Clonage CloneForm = new Test.Form3_Clonage(this, path, OldNameCamp);
            DCE_Manager.Clone_Form CloneForm = new DCE_Manager.Clone_Form(_mainForm, path, OldNameCamp);
            CloneForm.Show();


        }

        public void CampaignEdit1(object sender, EventArgs e, string path, string NameCamp)
        {
            var time_CampaignEdit1 = Stopwatch.StartNew();

            // 🔧 Nettoyage de l'ancienne instance
            if (CurrentCampaignEdit != null)
            {
                CurrentCampaignEdit.Dispose(); // 🔥 IMPORTANT

                CurrentCampaignEdit = null;
                UpdateCampaignButtonsVisibility();


                //_mainForm.label_Right_Campaign_Name.Text = "";
                Main_Form.Instance.CampaignView.label_Right_Campaign_Name.Text = "";
            }

            // 🔧 Nouvelle instance
            CurrentCampaignEdit = new Campaign_Edit_Grid_Right(_mainForm, this, NameCamp);

            UpdateCampaignButtonsVisibility();

            time_CampaignEdit1.Stop();
        }


        public void UpdateCampaignButtonsVisibility()
        {
            bool campaignSelected = CurrentCampaignEdit != null;

            bool show_A = campaignSelected && _mainForm.CampaignView.IsOobTabSelected;


            bool show_B = campaignSelected;

            Main_Form.Instance.CampaignView.EnableSaveButton(show_A);
            Main_Form.Instance.CampaignView.EnableResetButton(show_A);
            Main_Form.Instance.CampaignView.SetOobInitMode(show_A);
            Main_Form.Instance.CampaignView.SetOobActiveMode(show_A);

            _mainForm.CampaignView.ShowCampaignName(show_B);
        }



        public void RefreshGrids()
        {
            Campaign_Edit_Grid_Right.LoadGridStatic(Main_Form.Instance.CampaignView.DataGridViewBlue, _mainForm.currentSquads, "blue", _mainForm.currentState);
            Campaign_Edit_Grid_Right.LoadGridStatic(Main_Form.Instance.CampaignView.DataGridViewRed, _mainForm.currentSquads, "red", _mainForm.currentState);

        }


        public async void CampaignDataGridView_CellContentClick(object sender, DataGridViewCellEventArgs e)
        {


            if (e.RowIndex < 0)
                return;

            if (_mainForm.CampaignDataGridView.Columns[e.ColumnIndex].Name == "Repo")
            {
                CampaignInfo campaignRepo = _mainForm.campaignUpdater.GetCampaignFromRow(e.RowIndex);

                if (campaignRepo != null && !string.IsNullOrWhiteSpace(campaignRepo.RepositoryUrl))
                {
                    System.Diagnostics.Process.Start(new ProcessStartInfo
                    {
                        FileName = campaignRepo.RepositoryUrl,
                        UseShellExecute = true
                    });
                }

                return;
            }

            if (_mainForm.CampaignDataGridView.Columns[e.ColumnIndex].Name != "Action")
                return;

            CampaignInfo campaign = _mainForm.campaignUpdater.GetCampaignFromRow(e.RowIndex);

            if (campaign == null || string.IsNullOrWhiteSpace(campaign.DownloadUrl))
                return;

            if (!campaign.UpdateAvailable)
                return;


            //CampaignDataGridView.Enabled = false;
            //groupBox_DwlCampaign.Visible = true;

            _mainForm.pictureBoxCampaignDownload.Visible = true;

            _mainForm.labelCampaignDownload.Visible = true;

            _mainForm.progressBarCampaignDownload.Visible = true;
            _mainForm.buttonCampaignCancel.Visible = true;

            try
            {

                _mainForm.buttonCampaignCancel.Enabled = true;
                _mainForm.buttonCampaignCancel.Visible = true;
                _mainForm.labelCampaignDownload.Visible = true;


                string zipFile = await _mainForm.campaignUpdater.DownloadCampaign(
                    campaign,
                    _mainForm.progressBarCampaignDownload,
                    _mainForm.labelCampaignDownload,
                    _mainForm.labelCampaignDld_Pct,
                    _mainForm.labelCampaignTitle);

                if (string.IsNullOrEmpty(zipFile))
                {
                    FormUtils.LogRegister("Campaign download cancelled.");

                    return;
                }

                FormUtils.LogRegister("Campaign downloaded : " + zipFile);

                _mainForm.campaignUpdater.ExtractCampaignZip(
                    zipFile,
                    ParamConf.PATH_SavedGames_DCS,
                    campaign);

                FormUtils.LogRegister("FormMain Campaign installed - rafraîchissement local (sans requête GitHub)");

                _mainForm.campaignUpdater.RefreshCampaignUpdatesLocalOnly(
                    _mainForm.CampaignDataGridView,
                    ParamConf.PATH_SavedGames_DCS);


            }
            finally
            {
                _mainForm.CampaignDataGridView.Enabled = true;

                _mainForm.buttonCampaignCancel.Enabled = false;
                _mainForm.buttonCampaignCancel.Visible = false;

                _mainForm.progressBarCampaignDownload.Visible = false;
                _mainForm.labelCampaignDownload.Visible = false;
                _mainForm.labelCampaignDld_Pct.Visible = false;
                _mainForm.labelCampaignDownload.Visible = false;
                _mainForm.groupBox_DwlCampaign.Visible = true;

                _mainForm.pictureBoxCampaignDownload.Image = Properties.Resources.icons8_ok_24;

            }

            //string zipFile = await campaignUpdater.DownloadCampaign(campaign);


        }

        public void ResetCurrentCampaign()
        {
            if (CurrentCampaignEdit != null)
            {
                CurrentCampaignEdit.Dispose();
                CurrentCampaignEdit = null;
            }

            _mainForm.dataGridViewCampaigns.ClearSelection();

            Main_Form.Instance.CampaignView.label_Right_Campaign_Name.Text = "";
            Main_Form.Instance.CampaignView.textBoxCampBriefing.Clear();

            Main_Form.Instance.CampaignView.pictureBoxCampImage.Image?.Dispose();
            Main_Form.Instance.CampaignView.pictureBoxCampImage.Image = null;

            Main_Form.Instance.CampaignView.DataGridViewBlue.DataSource = null;
            Main_Form.Instance.CampaignView.DataGridViewBlue.Columns.Clear();

            Main_Form.Instance.CampaignView.DataGridViewRed.DataSource = null;
            Main_Form.Instance.CampaignView.DataGridViewRed.Columns.Clear();

            _mainForm.currentSquads = new List<Squad>();

            //_mainForm.CampaignTab.Visible = false;
            Main_Form.Instance.ShowHome();

            ParamCampaignSelected.NameCampaign = "";
            Main_Form.Instance.CampaignView.RefreshWargameTab("");

            CurrentCampaignEdit = null;
            UpdateCampaignButtonsVisibility();
        }

        public void buttonCampaignCancel_Click(object sender, EventArgs e)
        {
            FormUtils.LogRegister("buttonCampaignCancel_Click");

            _mainForm.campaignUpdater.CancelDownload();


            _mainForm.labelCampaignTitle.Visible = false;
            _mainForm.pictureBoxCampaignDownload.Visible = false;
        }


        public void groupBox_UpdateCampaign_Enter(object sender, EventArgs e)
        {

        }

        // Sélectionne et fait défiler la grid jusqu'à la ligne de cette campagne, après un
        // rechargement complet (LoadCampaignsAsync vide et reconstruit toutes les lignes).
        private void SelectAndScrollToCampaignRow(string name)
        {
            foreach (DataGridViewRow row in _mainForm.dataGridViewCampaigns.Rows)
            {
                if (name.Equals(row.Cells["Name"].Value?.ToString(), StringComparison.OrdinalIgnoreCase))
                {
                    _mainForm.dataGridViewCampaigns.ClearSelection();
                    row.Selected = true;
                    _mainForm.dataGridViewCampaigns.CurrentCell = row.Cells["Name"];
                    _mainForm.dataGridViewCampaigns.FirstDisplayedScrollingRowIndex = Math.Max(0, row.Index - 2);
                    break;
                }
            }
        }

    }
}
