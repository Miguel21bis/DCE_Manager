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
    public class CampaignGridLeft
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

        // État déplié/replié des campagnes maîtres (colonne Family). En mémoire
        // uniquement : remis à zéro à chaque relance de l'appli, pas persisté.
        private readonly HashSet<string> _expandedMasters = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private int _lastFamilyMouseX = -1;

        // Liste centralisée pour éviter la divergence entre le test de complétude (LoadCampaignsAsync)
        // et le calcul "encore manquant après réparation" (RepairCampaignAsync).
        private static readonly string[] RequiredInitFiles =
            { "camp_init.lua", "camp_triggers_init.lua", "conf_mod.lua", "db_airbases.lua", "targetlist_init.lua", "oob_air_init.lua", "path.bat" };

        private int _lastQuickActionsMouseX = -1;

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
                ImageLayout = DataGridViewImageCellLayout.Zoom
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

            //GridCampaigns_AddButtonColumn("First", "▶", 55);
            //GridCampaigns_AddButtonColumn("Skip", "⏭", 55, useColumnTextForButtonValue: false);
            //GridCampaigns_AddButtonColumn("Parameters", "⚙", 55);
            //GridCampaigns_AddButtonColumn("CampaignSetup", "🛠", 55);
            //GridCampaigns_AddButtonColumn("Delete", "🗑", 55);

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
                Width = 75,
                ReadOnly = true,
                DefaultCellStyle = { Alignment = DataGridViewContentAlignment.MiddleCenter }
            });
            _mainForm.dataGridViewCampaigns.Columns["Family"].DisplayIndex = 1;


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
            //_mainForm.dataGridViewCampaigns.DefaultCellStyle.Padding = new Padding(10);

            _mainForm.dataGridViewCampaigns.ColumnHeadersHeight = 35;

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

            _mainForm.dataGridViewCampaigns.ShowCellToolTips = true; // true par défaut, explicite pour être sûr
            _mainForm.dataGridViewCampaigns.CellToolTipTextNeeded += GridCampaigns_QuickActions_CellToolTipTextNeeded;

            _mainForm.dataGridViewCampaigns.RowTemplate.Height = 70;

            _mainForm.dataGridViewCampaigns.DefaultCellStyle.Font = new Font("Segoe UI", 9);
            _mainForm.dataGridViewCampaigns.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI", 9, FontStyle.Bold);

            _mainForm.dataGridViewCampaigns.EnableHeadersVisualStyles = false;
            _mainForm.dataGridViewCampaigns.ColumnHeadersDefaultCellStyle.BackColor = SystemColors.Control;
            _mainForm.dataGridViewCampaigns.ColumnHeadersDefaultCellStyle.ForeColor = Color.Black;

            _mainForm.dataGridViewCampaigns.GridColor = Color.LightGray;
            _mainForm.dataGridViewCampaigns.BorderStyle = BorderStyle.None;

            UpdateCampaignSetupColumnVisibility();
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

        // Colonne "First + Skip + Debrief" regroupée en une seule case (gain de place).
        // Ce n'est PAS une colonne bouton : les 3 icônes sont dessinées à la main dans
        // GridCampaigns_QuickActions_CellPainting, et le clic est découpé en 3 zones (tiers
        // de la largeur de la cellule) dans GridCampaigns_QuickActions_CellMouseClick.
        private void GridCampaigns_AddQuickActionsColumn()
        {
            _mainForm.dataGridViewCampaigns.Columns.Add(new DataGridViewTextBoxColumn()
            {
                Name = "QuickActions",
                HeaderText = "Actions",
                Width = 100, // avant 130 : resserre les 3 icônes pour laisser de la place à Campaign
                ReadOnly = true,
                DefaultCellStyle = { Alignment = DataGridViewContentAlignment.MiddleCenter }
            });
        }

        // Centralise les règles de visibilité des 3 icônes, utilisées par le dessin
        // (CellPainting), le clic (CellMouseClick) et le survol (CellMouseMove).
        private void GetQuickActionVisibility(int rowIndex, out bool showFirst, out bool showSkip, out bool showDebrief)
        {
            var row = _mainForm.dataGridViewCampaigns.Rows[rowIndex];

            int nbMission;
            int.TryParse(row.Cells["Missions"].Value?.ToString(), out nbMission);

            showFirst = true;
            showSkip = nbMission >= 1;
            showDebrief = "1".Equals(row.Cells["DebriefPending"].Value?.ToString());
        }

        // Peint les 1 à 3 icônes de la colonne QuickActions. Toujours 3 emplacements de
        // largeur égale (un tiers de cellule chacun), qu'ils soient utilisés ou non : ça
        // garde le découpage des clics simple et stable, peu importe la combinaison visible.
        private void GridCampaigns_QuickActions_CellPainting(object sender, DataGridViewCellPaintingEventArgs e)
        {
            if (e.RowIndex < 0 || _mainForm.dataGridViewCampaigns.Columns[e.ColumnIndex].Name != "QuickActions")
                return;

            e.PaintBackground(e.ClipBounds, true);
            e.Handled = true;

            var row = _mainForm.dataGridViewCampaigns.Rows[e.RowIndex];
            string name = row.Cells["Name"].Value?.ToString();

            if (DeleteSelectedRowTag.Equals(row.Tag) || string.IsNullOrEmpty(name))
                return;

            if (_incompleteOrOrphanNames.Contains(name))
            {
                if (_repairableIncompleteNames.Contains(name))
                {
                    using (var repairFont = new Font("Segoe UI", 14, FontStyle.Bold))
                    {
                        DrawQuickActionIcon(e.Graphics, "🔧", true, e.CellBounds.Left, e.CellBounds.Width, e.CellBounds.Top, e.CellBounds.Height, repairFont);
                    }
                }

                return; // ligne orpheline non réparable, ou 🔧 déjà dessiné : rien d'autre à peindre ici
            }

            bool showFirst, showSkip, showDebrief;
            GetQuickActionVisibility(e.RowIndex, out showFirst, out showSkip, out showDebrief);

            int thirdWidth = e.CellBounds.Width / 3;

            using (var font = new Font("Segoe UI", 14, FontStyle.Bold))
            {
                DrawQuickActionIcon(e.Graphics, "▶", showFirst, e.CellBounds.Left, thirdWidth, e.CellBounds.Top, e.CellBounds.Height, font);
                DrawQuickActionIcon(e.Graphics, "⏭", showSkip, e.CellBounds.Left + thirdWidth, thirdWidth, e.CellBounds.Top, e.CellBounds.Height, font);
                DrawQuickActionIcon(e.Graphics, "📋", showDebrief, e.CellBounds.Left + 2 * thirdWidth, thirdWidth, e.CellBounds.Top, e.CellBounds.Height, font);
            }
        }

        // Vrai si la zone (tiers de cellule) sous la souris correspond à une icône
        // effectivement affichée pour cette ligne. Sert au clic ET au curseur main.
        private bool IsQuickActionZoneActive(int rowIndex, int mouseX)
        {
            var row = _mainForm.dataGridViewCampaigns.Rows[rowIndex];
            string name = row.Cells["Name"].Value?.ToString();

            if (string.IsNullOrEmpty(name))
                return false;

            if (_incompleteOrOrphanNames.Contains(name))
                return _repairableIncompleteNames.Contains(name); // toute la cellule = zone "Repair"

            bool showFirst, showSkip, showDebrief;
            GetQuickActionVisibility(rowIndex, out showFirst, out showSkip, out showDebrief);

            int cellWidth = _mainForm.dataGridViewCampaigns.Columns["QuickActions"].Width;
            int thirdWidth = cellWidth / 3;
            int zone = Math.Min(mouseX / thirdWidth, 2);

            if (zone == 0) return showFirst;
            if (zone == 1) return showSkip;
            return showDebrief;
        }

        private void DrawQuickActionIcon(Graphics g, string icon, bool visible, int x, int width, int y, int height, Font font)
        {
            if (!visible)
                return;

            var rect = new Rectangle(x, y, width, height);
            TextRenderer.DrawText(g, icon, font, rect, Color.Black, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }

        // Découpe le clic sur la colonne QuickActions en 3 zones (mêmes tiers que le dessin).
        // e.X/e.Y sont relatifs à la cellule dans CellMouseClick (contrairement à CellClick).
        private async void GridCampaigns_QuickActions_CellMouseClickAsync(object sender, DataGridViewCellMouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left)
                return;

            if (e.RowIndex < 0 || _mainForm.dataGridViewCampaigns.Columns[e.ColumnIndex].Name != "QuickActions")
                return;

            if (!IsQuickActionZoneActive(e.RowIndex, e.X))
                return; // zone vide, pas d'action

            var row = _mainForm.dataGridViewCampaigns.Rows[e.RowIndex];
            string name = row.Cells["Name"].Value?.ToString();

            if (_repairableIncompleteNames.Contains(name))
            {
                await RepairCampaignAsync(name);
                return;
            }

            EnsureCampaignFilesUpToDate(name);

            //var row = _mainForm.dataGridViewCampaigns.Rows[e.RowIndex];
            //string name = row.Cells["Name"].Value?.ToString();

            EnsureCampaignFilesUpToDate(name);

            int cellWidth = _mainForm.dataGridViewCampaigns.Columns["QuickActions"].Width;
            int thirdWidth = cellWidth / 3;
            int zone = Math.Min(e.X / thirdWidth, 2);

            string folderPath = Path.Combine(ParamConf.PATH_SavedGames_DCS + @"\Mods\tech\DCE\Missions\Campaigns\", name);

            if (zone == 0)
            {
                Saver_TargetList_Wargame.WriteInitialFormations(name);
                await RunScriptsModInteractiveAsync(name, folderPath, "FirstMission.bat");
            }
            else if (zone == 1)
            {
                WargameEngineLosses.ProcessBeforeMission(name);
                Saver_TargetList_Wargame.WriteNewActiveFormations(name);
                await RunScriptsModInteractiveAsync(name, folderPath, "SkipMission.bat");
            }
            else
            {
                //OpenScriptsModRunner(folderPath, "DEBUG_DebriefMission.bat", name, "Debrief_Master.lua");
                await RunScriptsModInteractiveAsync(name, folderPath, "DEBUG_DebriefMission.bat", "Debrief_Master.lua");
            }

        }

        private void GridCampaigns_QuickActions_CellMouseMove(object sender, DataGridViewCellMouseEventArgs e)
        {
            if (e.RowIndex < 0 || _mainForm.dataGridViewCampaigns.Columns[e.ColumnIndex].Name != "QuickActions")
            {
                _mainForm.dataGridViewCampaigns.Cursor = Cursors.Default;
                _lastQuickActionsMouseX = -1;
                return;
            }

            _lastQuickActionsMouseX = e.X; // mémorisé pour GridCampaigns_QuickActions_CellToolTipTextNeeded
            _mainForm.dataGridViewCampaigns.Cursor = IsQuickActionZoneActive(e.RowIndex, e.X) ? Cursors.Hand : Cursors.Default;
        }

        // Passe par le mécanisme d'infobulle interne du DataGridView (le seul qui fonctionne
        // fiablement sur ce contrôle - un ToolTip externe attaché via SetToolTip ne s'affiche
        // pas dessus). _lastQuickActionsMouseX vient de CellMouseMove : cet event-ci ne donne
        // pas la position X, seulement la cellule.
        private void GridCampaigns_QuickActions_CellToolTipTextNeeded(object sender, DataGridViewCellToolTipTextNeededEventArgs e)
        {
            if (e.RowIndex < 0 || _mainForm.dataGridViewCampaigns.Columns[e.ColumnIndex].Name != "QuickActions")
                return;

            if (_lastQuickActionsMouseX >= 0 && IsQuickActionZoneActive(e.RowIndex, _lastQuickActionsMouseX))
                e.ToolTipText = GetQuickActionTooltipText(_lastQuickActionsMouseX);
        }

        // Texte d'infobulle (en anglais, comme le reste de l'UI visible) pour l'icône
        // actuellement sous la souris. N'est appelé que si IsQuickActionZoneActive a déjà
        // confirmé que l'icône concernée est bien affichée pour cette ligne.
        private string GetQuickActionTooltipText(int mouseX)
        {
            int cellWidth = _mainForm.dataGridViewCampaigns.Columns["QuickActions"].Width;
            int thirdWidth = cellWidth / 3;
            int zone = Math.Min(mouseX / thirdWidth, 2);

            switch (zone)
            {
                case 0: return "Generate the first mission of this campaign";
                case 1: return "Skip the current mission and generate the next one";
                default: return "Debrief the last played mission";
            }
        }

        private void GridCampaigns_QuickActions_CellMouseLeave(object sender, DataGridViewCellEventArgs e)
        {
            if (e.ColumnIndex >= 0 && _mainForm.dataGridViewCampaigns.Columns[e.ColumnIndex].Name == "QuickActions")
            {
                _mainForm.dataGridViewCampaigns.Cursor = Cursors.Default;
                _lastQuickActionsMouseX = -1;
            }
        }

        // Détermine le rôle d'une campagne dans la hiérarchie (maître / fille / seule),
        // recalculé à la volée à chaque peinture/clic - pas de cache ici, CampaignHierarchy
        // fait déjà le sien en interne.
        private void GetFamilyRole(string name, out bool isMaster, out bool isChild, out bool expanded)
        {
            isChild = CampaignHierarchy.IsChild(name);
            isMaster = !isChild && CampaignHierarchy.IsMaster(name); // une fille n'est jamais aussi maître (2 niveaux max)
            expanded = isMaster && _expandedMasters.Contains(name);
        }

        // Ordonne les noms de dossiers pour l'affichage : les maîtres (et campagnes
        // seules) gardent un ordre alphabétique global, mais chaque maître est
        // TOUJOURS immédiatement suivi de ses filles (triées entre elles) - visibles
        // ou pas, la boucle décidera ensuite ligne par ligne (voir "isHiddenChild").
        // Un simple tri alphabétique global ne suffit pas : il ne regroupe pas les
        // familles dont le nom commun est à la FIN de la chaîne (ex: "Falcon over PG"
        // / "Tomcat over PG"), et éparpille les filles n'importe où une fois dépliées.
        private List<string> OrderCampaignFoldersForDisplay(IEnumerable<string> allNames)
        {
            List<string> allNamesList = allNames.ToList();
            HashSet<string> nameSet = new HashSet<string>(allNamesList, StringComparer.OrdinalIgnoreCase);

            List<string> topLevel = allNamesList
                .Where(n => !CampaignHierarchy.IsChild(n) || !nameSet.Contains(CampaignHierarchy.ResolveMaster(n)))
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                .ToList();

            List<string> result = new List<string>();

            foreach (string name in topLevel)
            {
                result.Add(name);

                IEnumerable<string> children = CampaignHierarchy.GetChildren(name)
                    .Where(c => nameSet.Contains(c))
                    .OrderBy(c => c, StringComparer.OrdinalIgnoreCase);

                result.AddRange(children);
            }

            return result;
        }

        // Colonne Family : 2 zones, moitié/moitié.
        // - Gauche : ▸/▾ + ★ sur un maître (bascule déplié/replié). ↳ grisé sur une fille
        //   (juste indicatif, pas cliquable). Rien sur une campagne seule.
        // - Droite : "⋯" toujours affiché (même sur une campagne seule) -> ouvre la popup
        //   "Gérer la famille".
        private void GridCampaigns_Family_CellPainting(object sender, DataGridViewCellPaintingEventArgs e)
        {
            if (e.RowIndex < 0 || _mainForm.dataGridViewCampaigns.Columns[e.ColumnIndex].Name != "Family")
                return;

            e.PaintBackground(e.ClipBounds, true);
            e.Handled = true;

            var row = _mainForm.dataGridViewCampaigns.Rows[e.RowIndex];
            string name = row.Cells["Name"].Value?.ToString();

            if (DeleteSelectedRowTag.Equals(row.Tag) || string.IsNullOrEmpty(name) || _incompleteOrOrphanNames.Contains(name))
                return; // ligne "corbeille" ou "problème" : pas de gestion de famille dessus

            bool isMaster, isChild, expanded;
            GetFamilyRole(name, out isMaster, out isChild, out expanded);

            int halfWidth = e.CellBounds.Width / 2;

            using (var font = new Font("Segoe UI", 15, FontStyle.Bold))
            using (var smallFont = new Font("Segoe UI", 13, FontStyle.Bold))
            {
                string leftIcon = isMaster ? (expanded ? "▾ ★" : "▸ ★") : (isChild ? "↳" : "");

                if (!string.IsNullOrEmpty(leftIcon))
                {
                    var leftRect = new Rectangle(e.CellBounds.Left, e.CellBounds.Top, halfWidth, e.CellBounds.Height);
                    TextRenderer.DrawText(e.Graphics, leftIcon, font, leftRect, isChild ? Color.Gray : Color.Black,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                }

                var rightRect = new Rectangle(e.CellBounds.Left + halfWidth, e.CellBounds.Top, e.CellBounds.Width - halfWidth, e.CellBounds.Height);
                TextRenderer.DrawText(e.Graphics, "⋯", smallFont, rightRect, Color.DimGray,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }
        }

        // e.X est relatif à la cellule dans CellMouseClick (comme pour QuickActions).
        private async void GridCampaigns_Family_CellMouseClickAsync(object sender, DataGridViewCellMouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left || e.RowIndex < 0)
                return;

            if (_mainForm.dataGridViewCampaigns.Columns[e.ColumnIndex].Name != "Family")
                return;

            var row = _mainForm.dataGridViewCampaigns.Rows[e.RowIndex];
            string name = row.Cells["Name"].Value?.ToString();

            if (DeleteSelectedRowTag.Equals(row.Tag) || string.IsNullOrEmpty(name) || _incompleteOrOrphanNames.Contains(name))
                return;

            int halfWidth = _mainForm.dataGridViewCampaigns.Columns["Family"].Width / 2;

            bool isMaster, isChild, expanded;
            GetFamilyRole(name, out isMaster, out isChild, out expanded);

            if (e.X < halfWidth)
            {
                // Zone gauche : bascule déplié/replié, seulement si c'est un maître.
                if (!isMaster)
                    return;

                if (expanded)
                    _expandedMasters.Remove(name);
                else
                    _expandedMasters.Add(name);

                await LoadCampaignsAsync(selectCampaignName: name);
            }
            else
            {
                // Zone droite : ouvre la popup de gestion, pour tout le monde (maître,
                // fille ou campagne seule - elle peut y être rattachée à une autre).
                using (var form = new ManageFamily_Form(name))
                {
                    form.ShowDialog(_mainForm);
                }

                await LoadCampaignsAsync(selectCampaignName: name);
            }
        }

        private void GridCampaigns_Family_CellMouseMove(object sender, DataGridViewCellMouseEventArgs e)
        {
            if (e.RowIndex < 0 || _mainForm.dataGridViewCampaigns.Columns[e.ColumnIndex].Name != "Family")
            {
                if (_lastFamilyMouseX >= 0)
                    _mainForm.dataGridViewCampaigns.Cursor = Cursors.Default;
                _lastFamilyMouseX = -1;
                return;
            }

            _lastFamilyMouseX = e.X;
            _mainForm.dataGridViewCampaigns.Cursor = Cursors.Hand;
        }

        private void GridCampaigns_Family_CellMouseLeave(object sender, DataGridViewCellEventArgs e)
        {
            if (e.ColumnIndex >= 0 && _mainForm.dataGridViewCampaigns.Columns[e.ColumnIndex].Name == "Family")
            {
                _mainForm.dataGridViewCampaigns.Cursor = Cursors.Default;
                _lastFamilyMouseX = -1;
            }
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
            if (!_alreadyUpdated.Add(name))
                return;

            ConfUpdateResult campInitResult = new CampInitUpdater().UpdateCampaign(name);
            ConfUpdateResult confModResult = new ConfModTemplateUpdater().UpdateCampaign(name); // dans cet ordre

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
        }

        // Lance FirstMission.bat / SkipMission.bat / DEBUG_DebriefMission.bat en tâche de fond
        // (fenêtre cachée) et pilote l'interaction console (ScriptsMod) via une Form dédiée, au
        // lieu d'une fenêtre console visible.
        // luaScriptName : uniquement pour les .bat hors convention BAT_xxx.lua (ex: Debriefing,
        // qui appelle Debrief_Master.lua directement) - laisser null pour First/Skip.
        private async Task RunScriptsModInteractiveAsync(string campaignName, string folderPath, string batFileName, string luaScriptName = null)
        {
            string batPath = Path.Combine(folderPath, batFileName);

            if (!File.Exists(batPath))
                return;

            using (var runnerForm = new ScriptsModRunner_Form(batPath, folderPath, campaignName, luaScriptName))
            {
                runnerForm.ShowDialog(_mainForm);
            }

            // Que la mission ait été générée ou que l'utilisateur ait fermé en cours de route,
            // on rafraîchit la liste (nombre de missions, bouton Skip, icône Debriefing... peuvent avoir changé).
            await LoadCampaignsAsync(selectCampaignName: campaignName);
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

            // Ligne "problème" (dossier incomplet ou fichier orphelin) : seuls Delete et Folder
            // ont un sens ici. Les autres colonnes (First/Skip/Parameters/CampaignSetup, ou
            // l'ouverture normale du panneau de droite en fin de méthode) sont ignorées, plutôt
            // que de tenter d'agir sur des fichiers qui peuvent ne pas exister.
            if (_incompleteOrOrphanNames.Contains(name) && columnName != "Delete" && columnName != "Folder")
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
            // Empaquette la campagne dans un .zip distribuable (Active/Debug/Debriefing vidés,
            // Doc et livrées custom selon les cases cochées par l'utilisateur)
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

        // Charge toutes les campagnes (code existant déplacé ici)
        // selectCampaignName : après rechargement, sélectionne/scroll sur la ligne portant ce
        // nom (ex: la campagne qui vient d'être clonée). Prioritaire sur restoreRowIndex.
        // restoreRowIndex : si selectCampaignName est vide, restaure la vue à peu près là où
        // elle était (ex: ligne 34 après suppression de la ligne 35), borné à la nouvelle taille
        // de la grid.
        // Pas de async : tout le corps est synchrone (accès disque local, remplissage de grid).
        // On renvoie quand même un Task pour ne rien changer aux appelants qui font "await".
        public Task LoadCampaignsAsync(string selectCampaignName = null, int? restoreRowIndex = null)
        {
            // Different configurations (DCSA/DCSB...) can contain campaigns with the
            // same folder name; the ConfMod cache is only keyed by that name, so it
            // must be cleared whenever the campaign list is (re)loaded.
            ConfModLoader.ClearCache();

            ResetCurrentCampaign();

            _repairableIncompleteNames.Clear();
            _mainForm.dataGridViewCampaigns.Rows.Clear();
            _incompleteOrOrphanNames.Clear();

            List<CampaignInfo> campaignUpdateList = new List<CampaignInfo>();

            var LoadCampaigns = Stopwatch.StartNew();

            int nbCampaign = 0;

            string campaignsRoot = ParamConf.PATH_SavedGames_DCS + @"\Mods\tech\DCE\Missions\Campaigns";

            bool folderCampExists = System.IO.Directory.Exists(campaignsRoot);

            // Classement auto maître/fille (1ère passe seulement, voir CampaignHierarchy) :
            // doit tourner AVANT la boucle, puisqu'elle a besoin de savoir qui est fille de
            // qui pour décider quelles lignes masquer.
            if (folderCampExists)
                CampaignHierarchy.ClassifyUnknown(Directory.GetDirectories(campaignsRoot).Select(Path.GetFileName));

            // Contenu identique pour toutes les campagnes (dépend seulement de ParamConf) :
            // calculé UNE FOIS ici, plutôt qu'à chaque itération de la boucle.
            string textPathBatGlobal = "REM Core or Main DCS ou DCS.beta path, always end the line with \\ \r\n" +
                "set \"pathDCS=" + ParamConf.PATH_DCS_Root + "\\\"\r\n" +
                "REM Core or Main DCS ou DCS.beta path, always end the line with \\ \r\n" +
                "set \"pathSavedGames=" + ParamConf.PATH_SavedGames_DCS + "\\\"\r\n" +
                "REM DCE ScriptMod version not any / or \\ and no space before and after = \r\n" +
                "set \"versionPackageICM=" + TestFile.ScriptsMod + "\"\r\n" +
                "\r\n" +
                "\r\n" +
                "REM After each change, You must launch the FirsMission.bat for it to be taken into account.";

            bool canWritePathBat = ParamConf.PATH_DCS_Root != "" & ParamConf.PATH_SavedGames_DCS != "";

            if (folderCampExists)          
            {
                var orderedNames = OrderCampaignFoldersForDisplay(Directory.GetDirectories(campaignsRoot).Select(Path.GetFileName));

                foreach (string NameCamp in orderedNames)
                {
                    string subFolder = Path.Combine(campaignsRoot, NameCamp);

                    bool folderLocExists = System.IO.Directory.Exists(subFolder);

                    // 🔥 cache local des fichiers (1 lecture max) - APRÈS le dépannage ci-dessus, pour lire
                    // un camp_init.lua fraîchement créé le cas échéant, pas un fichier absent.
                    string campInitContent = null;
                    string campStatusContent = null;
                    string oobAirContent = null;

                    string pathCampInitFile = subFolder + @"\Init\camp_init.lua";
                    if (File.Exists(pathCampInitFile))
                        campInitContent = File.ReadAllText(pathCampInitFile);

                    string pathCampstatusFile = subFolder + @"\Active\camp_status.lua";
                    if (File.Exists(pathCampstatusFile))
                        campStatusContent = File.ReadAllText(pathCampstatusFile);

                    string path_oob_air;

                    // Complétude du dossier : les 6 fichiers Init attendus + les 2 .miz + le .cmp.
                    string initFolder = subFolder + @"\Init\";
                    string[] requiredInitFiles = RequiredInitFiles;

                    var missingFiles = requiredInitFiles.Where(f => !File.Exists(initFolder + f)).ToList();

                    string[] requiredMissionFiles = { NameCamp + "_first.miz", NameCamp + "_ongoing.miz", NameCamp + ".cmp" };
                    missingFiles.AddRange(requiredMissionFiles.Where(f => !File.Exists(campaignsRoot + @"\" + f)));


                    if (missingFiles.Count > 0)
                    {
                        _incompleteOrOrphanNames.Add(NameCamp);
                        _repairableIncompleteNames.Add(NameCamp); // ce sont les seules lignes où 🔧 sera proposé
                        AddProblemRow(NameCamp, "Dossier incomplet — manque : " + string.Join(", ", missingFiles));
                        continue;
                    }

                    //cherche la version inscrite dans path.bat
                    string PathBatFile = subFolder + @"\Init\path.bat";
                    bool fileExistPathBat = File.Exists(PathBatFile);

                    if (fileExistPathBat)
                    {
                        if (canWritePathBat)
                        {
                            // On ne réécrit que si le contenu a réellement changé (évite une écriture
                            // disque inutile à chaque affichage de la grid, pour toutes les campagnes).
                            string existingContent = null;
                            try { existingContent = File.ReadAllText(PathBatFile); } catch { /* tant pis, on écrira */ }

                            if (existingContent != textPathBatGlobal)
                            {
                                System.IO.File.WriteAllText(PathBatFile, textPathBatGlobal);
                            }
                        }

                        nbCampaign++;

                        //Cherche la version de la campagne
                        string VerCamp = "";
                        string campaignId = "";
                        string repositoryUrl = "";
                        if (campInitContent != null)
                        {
                            Match match;

                            match = Regex.Match(
                                campInitContent,
                                @"(?<!\w)version\s*=\s*""([^""]+)""");

                            if (match.Success)
                                VerCamp = match.Groups[1].Value;

                            match = Regex.Match(
                                campInitContent,
                                @"campaignId\s*=\s*""([^""]+)""");

                            if (match.Success)
                                campaignId = match.Groups[1].Value;

                            match = Regex.Match(
                                campInitContent,
                                @"repositoryUrl\s*=\s*""([^""]+)""");

                            if (match.Success)
                                repositoryUrl = match.Groups[1].Value;
                        }


                        //Cherche le nombre de mission joué
                        //['mission'] = 1,
                        string NbMission = "0";
                        if (campStatusContent != null)
                        {
                            var match = Regex.Match(campStatusContent, @"mission[""']?\]\s*=\s*(\d+)");
                            if (match.Success)
                                NbMission = (Int32.Parse(match.Groups[1].Value) - 1).ToString();
                        }


                        //cherche si une campagne doit etre reset a la suite d'un update 
                        //TODO ? non, il faudra sortir "reset si upadate fait"
                        var campaignNameTab = new Dictionary<string, string>();

                        //string colorFM = "";
                        //string colorSM = "";
                        if (folderLocExists)
                        {

                            //string path_oob_air = "";
                            if (Int32.Parse(NbMission) >= 1)
                                path_oob_air = subFolder + @"\Active\oob_air.lua";
                            else
                                path_oob_air = subFolder + @"\Init\oob_air_init.lua";

                            if (File.Exists(path_oob_air))
                                oobAirContent = File.ReadAllText(path_oob_air);


                            string type = "default";

                            if (!string.IsNullOrEmpty(oobAirContent))
                            {
                                string content = oobAirContent;

                                // Supprime les commentaires Lua
                                content = Regex.Replace(content, @"--\[\[.*?\]\]", "", RegexOptions.Singleline);
                                content = Regex.Replace(content, @"--.*?$", "", RegexOptions.Multiline);

                                // Trouve player = true ou ["player"] = true
                                Match playerMatch = Regex.Match(
                                    content,
                                    @"(?:\[\s*""player""\s*\]|player)\s*=\s*true",
                                    RegexOptions.IgnoreCase);

                                if (playerMatch.Success)
                                {
                                    int playerPos = playerMatch.Index;

                                    // Remonte jusqu'au { du bloc contenant ce player
                                    int level = 0;
                                    int blockStart = -1;

                                    for (int i = playerPos; i >= 0; i--)
                                    {
                                        if (content[i] == '}')
                                        {
                                            level++;
                                        }
                                        else if (content[i] == '{')
                                        {
                                            if (level == 0)
                                            {
                                                blockStart = i;
                                                break;
                                            }

                                            level--;
                                        }
                                    }

                                    if (blockStart >= 0)
                                    {
                                        // Redescend jusqu'à la } correspondante
                                        level = 1;
                                        int blockEnd = -1;

                                        for (int i = blockStart + 1; i < content.Length; i++)
                                        {
                                            if (content[i] == '{')
                                                level++;
                                            else if (content[i] == '}')
                                                level--;

                                            if (level == 0)
                                            {
                                                blockEnd = i;
                                                break;
                                            }
                                        }

                                        if (blockEnd > blockStart)
                                        {
                                            string block = content.Substring(blockStart, blockEnd - blockStart + 1);

                                            Match typeMatch = Regex.Match(
                                                block,
                                                @"(?:\[\s*""type""\s*\]|type)\s*=\s*""([^""]+)""",
                                                RegexOptions.IgnoreCase);

                                            if (typeMatch.Success)
                                                type = typeMatch.Groups[1].Value;
                                        }
                                    }
                                }
                            }


                            //check si plusieurs images par type d'avion existe dans le dossier image
                            string filePNGbyePlane = (ParamConf.PATH_SavedGames_DCS + @"\Mods\tech\DCE\Missions\Campaigns\" + NameCamp + @"\Images\planescreen_" + type + ".png");
                            string filePNG = (ParamConf.PATH_SavedGames_DCS + @"\Mods\tech\DCE\Missions\Campaigns\" + NameCamp + ".png");
                            string fileBMP = ParamConf.PATH_SavedGames_DCS + @"\Mods\tech\DCE\Missions\Campaigns\" + NameCamp + ".bmp";

                            // Copie l'image spécifique à l'avion vers l'image principale de la campagne.
                            // Pourquoi : File.Copy utilise l'API native Windows et consomme moins de CPU que CopyTo.
                            if (File.Exists(filePNGbyePlane))
                            {
                                bool needsCopy = true;
                                try
                                {
                                    if (File.Exists(filePNG))
                                    {
                                        var srcInfo = new FileInfo(filePNGbyePlane);
                                        var dstInfo = new FileInfo(filePNG);
                                        needsCopy = srcInfo.Length != dstInfo.Length || srcInfo.LastWriteTimeUtc != dstInfo.LastWriteTimeUtc;
                                    }
                                }
                                catch { /* en cas de doute, on recopie */ }

                                if (needsCopy)
                                {
                                    try
                                    {
                                        File.Copy(filePNGbyePlane, filePNG, true);
                                    }
                                    catch (IOException)
                                    {
                                        // ignore si en cours d'utilisation
                                    }
                                }

                                if (File.Exists(fileBMP))
                                {
                                    File.Delete(fileBMP);
                                }
                            }


                            // Image (avec cache)
                            Image img = null;
                            string imagePath = filePNG;

                            if (campaignImageCache.ContainsKey(imagePath))
                            {
                                img = campaignImageCache[imagePath];
                            }
                            else if (File.Exists(imagePath))
                            {
                                try
                                {
                                    var fileInfo = new FileInfo(imagePath);
                                    if (fileInfo.Length < 100) // seuil sécurité
                                    {
                                        throw new Exception("Image corrompue ou vide : " + imagePath);
                                    }

                                    if (fileInfo.Length < 100)
                                    {
                                        throw new Exception("Image corrompue ou vide : " + imagePath);
                                    }

                                    for (int i = 0; i < 3; i++)
                                    {
                                        try
                                        {
                                            using (var fs = new FileStream(imagePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                                            using (var temp = Image.FromStream(fs))
                                            {
                                                img = new Bitmap(temp);
                                            }
                                            break;
                                        }
                                        catch (IOException)
                                        {
                                            System.Threading.Thread.Sleep(50);
                                        }
                                    }

                                    if (img == null)
                                    {
                                        throw new Exception("Impossible de charger l'image après plusieurs tentatives : " + imagePath);
                                    }

                                    campaignImageCache[imagePath] = img;
                                }
                                catch (Exception ex)
                                {
                                    throw new Exception("Erreur lors du chargement de l'image : " + imagePath, ex);
                                }
                            }

                            // Ajout dans le DataGridView
                            campaignUpdateList.Add(
                                new CampaignInfo()
                                {
                                    Name = NameCamp,
                                    CampaignId = campaignId,
                                    RepositoryUrl = repositoryUrl,
                                    LocalVersion = VerCamp,
                                    Folder = subFolder
                                });

                            // Fille dont le maître n'est pas déplié : elle compte quand même
                            // dans nbCampaign (déjà fait plus haut) et dans campaignUpdateList
                            // (mise à jour possible même masquée), mais pas de ligne dans la grid.
                            bool isHiddenChild = CampaignHierarchy.IsChild(NameCamp) &&
                                !_expandedMasters.Contains(CampaignHierarchy.ResolveMaster(NameCamp));

                            if (isHiddenChild)
                                continue;

                            // Le bouton Skip ne doit être visible que si au moins une mission a été jouée
                            int nbMissionParsed;
                            int.TryParse(NbMission, out nbMissionParsed);

                            // Détecte un debrief en attente : fichiers de transition écrits par
                            // EventsTracker.lua en fin de mission, supprimés par DEBRIEF_Master.lua
                            // une fois traités. Encore là -> le debrief n'a pas (ou pas complètement)
                            // tourné, on permet de le relancer à la main.
                            bool debriefPending =
                                File.Exists(Path.Combine(subFolder, "camp_status.lua")) &&
                                File.Exists(Path.Combine(subFolder, "MissionEventsLog.lua")) &&
                                File.Exists(Path.Combine(subFolder, "scen_destroyed.lua")) &&
                                File.Exists(Path.Combine(subFolder, "zoneSAR.lua"));

                            _mainForm.dataGridViewCampaigns.Rows.Add(
                                null,       // Clone (bouton)
                                img,        // Image
                                NameCamp,   // Name
                                null,       // Folder
                                null,       // Export (bouton, texte fixe)
                                VerCamp,    // Version
                                NbMission,  // Missions
                                type,       // Aircraft
                                null,       // QuickActions (dessinée à la main, voir CellPainting)
                                null,       // Parameters
                                null        // CampaignSetup
                            );

                            int rowIndex = _mainForm.dataGridViewCampaigns.Rows.Count - 1;

                            // Case à cocher disponible sur toutes les lignes, campagnes normales
                            // comprises (et pas seulement les lignes "problème").
                            _mainForm.dataGridViewCampaigns.Rows[rowIndex].Cells["Select"].Value = false;

                            _mainForm.dataGridViewCampaigns.Rows[rowIndex].Cells["DebriefPending"].Value = debriefPending ? "1" : "";
                        }

                      
                    }
                }

            }

            // ----- Fichiers orphelins à la racine de Campaigns\ -----
            // .miz / .cmp / .png dont le nom de base ne correspond à AUCUN dossier de campagne
            // (existant ou déjà signalé incomplet ci-dessus). Reliquat probable d'une suppression
            // ou d'un clonage qui s'est mal terminé.
            if (folderCampExists)
            {
                var orphanFiles = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

                foreach (string file in Directory.GetFiles(campaignsRoot))
                {
                    string ext = Path.GetExtension(file).ToLowerInvariant();
                    if (ext != ".miz" && ext != ".cmp" && ext != ".png")
                        continue;

                    string baseName = Path.GetFileNameWithoutExtension(file);

                    if (baseName.EndsWith("_first", StringComparison.OrdinalIgnoreCase))
                        baseName = baseName.Substring(0, baseName.Length - "_first".Length);
                    else if (baseName.EndsWith("_ongoing", StringComparison.OrdinalIgnoreCase))
                        baseName = baseName.Substring(0, baseName.Length - "_ongoing".Length);

                    // Un dossier existe pour ce nom (complet, ou déjà listé comme incomplet
                    // juste au-dessus) : ce n'est pas un orphelin isolé, pas de ligne en plus.
                    if (Directory.Exists(campaignsRoot + @"\" + baseName))
                        continue;

                    if (!orphanFiles.ContainsKey(baseName))
                        orphanFiles[baseName] = new List<string>();

                    orphanFiles[baseName].Add(Path.GetFileName(file));
                }

                foreach (var kvp in orphanFiles)
                {
                    _incompleteOrOrphanNames.Add(kvp.Key);
                    AddProblemRow(kvp.Key, "Fichier(s) orphelin(s) : " + string.Join(", ", kvp.Value));
                }
            }

            InstalledCampaignCount = nbCampaign;
            IncompleteOrOrphanCount = _incompleteOrOrphanNames.Count;

            _mainForm.UpdateInstalledCampaignsLabel(nbCampaign);

            // Dernière ligne de la grid : la corbeille de suppression groupée.
            AddDeleteSelectedRow();

            LoadCampaigns.Stop();
            FormUtils.LogRegister($"LoadCampaigns : {LoadCampaigns.ElapsedMilliseconds} ms");

            // Repositionne la vue : priorité au nom (ex: campagne fraîchement clonée), sinon
            // on retombe à peu près là où on était avant le rechargement (ex: suppression).
            if (!string.IsNullOrEmpty(selectCampaignName))
            {
                for (int i = 0; i < _mainForm.dataGridViewCampaigns.Rows.Count; i++)
                {
                    string rowName = _mainForm.dataGridViewCampaigns.Rows[i].Cells["Name"].Value?.ToString();
                    if (string.Equals(rowName, selectCampaignName, StringComparison.OrdinalIgnoreCase))
                    {
                        SelectAndScrollToRow(i);
                        break;
                    }
                }
            }
            else if (restoreRowIndex.HasValue)
            {
                int target = Math.Min(restoreRowIndex.Value, _mainForm.dataGridViewCampaigns.Rows.Count - 1);
                if (target >= 0)
                {
                    SelectAndScrollToRow(target);
                }
            }

            return Task.CompletedTask;

        }

        // Sélectionne une ligne et essaie de la centrer dans la vue visible.
        private void SelectAndScrollToRow(int rowIndex)
        {
            if (rowIndex < 0 || rowIndex >= _mainForm.dataGridViewCampaigns.Rows.Count)
                return;

            _mainForm.dataGridViewCampaigns.ClearSelection();
            _mainForm.dataGridViewCampaigns.Rows[rowIndex].Selected = true;
            _mainForm.dataGridViewCampaigns.CurrentCell = _mainForm.dataGridViewCampaigns.Rows[rowIndex].Cells[0];

            try
            {
                int visibleRows = _mainForm.dataGridViewCampaigns.DisplayedRowCount(false);
                int firstRow = Math.Max(0, rowIndex - visibleRows / 2);
                _mainForm.dataGridViewCampaigns.FirstDisplayedScrollingRowIndex =
                    Math.Min(firstRow, _mainForm.dataGridViewCampaigns.Rows.Count - 1);
            }
            catch
            {
                // Pas critique si le calcul de défilement échoue (ex: grid pas encore affichée).
            }
        }

        // Ajoute une ligne "problème" (dossier incomplet ou fichier orphelin) dans la grid.
        // Name reste le nom exact (dossier ou base de fichier) : GridCampaigns_CellClick
        // reconstruit folderPath à partir de ce nom, donc Delete doit pouvoir s'en servir tel quel.
        private void AddProblemRow(string name, string reason)
        {
            _mainForm.dataGridViewCampaigns.Rows.Add(
                null,           // Clone
                null,           // Image
                name,           // Name
                null,           // Folder
                null,           // Export (bouton, texte fixe)
                "",             // Version
                "",             // Missions
                "⚠ " + reason,  // Aircraft (utilisée ici comme colonne de statut)
                null,           // QuickActions
                null,           // Parameters
                null            // CampaignSetup
            );

            int rowIndex = _mainForm.dataGridViewCampaigns.Rows.Count - 1;
            _mainForm.dataGridViewCampaigns.Rows[rowIndex].Cells["Select"].Value = false;
            _mainForm.dataGridViewCampaigns.Rows[rowIndex].Cells["DebriefPending"].Value = "";
            _mainForm.dataGridViewCampaigns.Rows[rowIndex].DefaultCellStyle.ForeColor = Color.DarkRed;
        }

        // Compte-rendu d'une suppression : ce qui a été fait, ce qui a bloqué.
        private class DeleteOutcome
        {
            public int DeletedCount;
            public List<string> LockedFiles = new List<string>();
            public bool FolderRemoved = true; // true aussi si aucun dossier n'existait (fichier orphelin seul)
            public string FolderRemovalError;
        }

        // Supprime le dossier d'une campagne et ses fichiers associés (mission .miz, .cmp,
        // image .png) à partir de son nom et de son folderPath. Marche aussi pour un fichier
        // orphelin sans dossier (folderPath n'existe simplement pas, ignoré).
        // Async pour ne pas geler l'UI pendant les tentatives (voir TryDeleteFileWithRetryAsync).
        private async Task<DeleteOutcome> DeleteCampaignFilesAndFolderAsync(string name, string folderPath)
        {
            var outcome = new DeleteOutcome();

            List<string> filesToDelete = Directory.Exists(folderPath)
                ? Directory.GetFiles(folderPath, "*", SearchOption.AllDirectories).ToList()
                : new List<string>();

            string parentFolder = Path.GetDirectoryName(folderPath) ?? "";
            string baseName = name;

            HashSet<string> expected = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                baseName,
                baseName + "_first",
                baseName + "_ongoing"
            };

            var extraFiles = Directory.GetFiles(parentFolder)
                .Where(f => expected.Contains(Path.GetFileNameWithoutExtension(f)))
                .ToList();

            filesToDelete.AddRange(extraFiles);

            // IMPORTANT : un verrou de fichier est souvent transitoire (DCS qui vient de se
            // fermer, antivirus qui scanne au mauvais moment...) et se lève en une fraction de
            // seconde. On retente plusieurs fois par fichier avant d'abandonner, on supprime
            // tout ce qui est possible, et on renvoie PRÉCISÉMENT ce qui reste bloqué — au lieu
            // du message générique de Directory.Delete qui ne cite que le dossier racine.
            foreach (string file in filesToDelete.Where(File.Exists))
            {
                if (await TryDeleteFileWithRetryAsync(file))
                {
                    outcome.DeletedCount++;
                }
                else
                {
                    outcome.LockedFiles.Add(file);
                }
            }

            // Retire le dossier (normalement vide à ce stade, ou avec juste des sous-dossiers
            // vides) s'il en reste un. IMPORTANT : avant, un échec ici était juste loggé dans un
            // fichier, sans que l'utilisateur ne voie jamais rien — d'où l'ajout de
            // FolderRemoved/FolderRemovalError, repris dans le compte-rendu affiché.
            if (Directory.Exists(folderPath))
            {
                try
                {
                    Directory.Delete(folderPath, true);
                }
                catch (Exception exDir)
                {
                    outcome.FolderRemoved = false;
                    outcome.FolderRemovalError = exDir.Message;
                    FormUtils.LogRegister($"Suppression de '{folderPath}' : le dossier n'a pas pu être retiré ({exDir.Message}).");
                }
            }

            CampaignHierarchy.OnCampaignDeleted(name);

            return outcome;
        }

        // Retente la suppression d'un fichier avant d'abandonner.
        // Pourquoi : un verrou (antivirus, DCS qui vient de libérer le fichier...) est souvent
        // transitoire et se lève en une fraction de seconde ; ça évite de bloquer toute une
        // suppression pour un verrou qui n'existe déjà plus une demi-seconde après.
        // Task.Delay (pas Thread.Sleep) : laisse l'UI répondre (curseur sablier, pas de "ne
        // répond pas") pendant l'attente au lieu de geler le thread principal.
        private async Task<bool> TryDeleteFileWithRetryAsync(string file, int attempts = 3, int delayMs = 400)
        {
            for (int i = 0; i < attempts; i++)
            {
                try
                {
                    if (File.Exists(file))
                    {
                        File.SetAttributes(file, FileAttributes.Normal); // au cas où le fichier serait en lecture seule
                        File.Delete(file);
                    }
                    return true;
                }
                catch (IOException)
                {
                    if (i < attempts - 1)
                        await Task.Delay(delayMs);
                }
                catch (UnauthorizedAccessException)
                {
                    if (i < attempts - 1)
                        await Task.Delay(delayMs);
                }
            }

            return false;
        }

        // Compte-rendu (en anglais) affiché après une suppression simple ou groupée : ce qui a
        // été supprimé avec succès, et le détail de ce qui a bloqué (fichiers verrouillés,
        // dossier non retiré). Toujours affiché, même en cas de succès complet.
        // Ajoute la dernière ligne de la grid : celle qui porte l'icône corbeille de suppression
        // groupée. Appelée en fin de LoadCampaignsAsync, une fois toutes les campagnes ajoutées.
        private void AddDeleteSelectedRow()
        {
            var grid = _mainForm.dataGridViewCampaigns;

            grid.Rows.Add();
            int rowIndex = grid.Rows.Count - 1;
            var row = grid.Rows[rowIndex];

            row.Tag = DeleteSelectedRowTag;
            row.Height = 34;

            // On vide les autres colonnes bouton (Clone, Folder, First, Parameters,
            // CampaignSetup) et la case à cocher : sur cette ligne, seule la corbeille agit.
            foreach (string colName in new[] { "Clone", "Folder", "Export", "QuickActions", "Parameters", "CampaignSetup", "Select" })
            {
                if (grid.Columns.Contains(colName))
                {
                    row.Cells[colName] = new DataGridViewTextBoxCell();
                    row.Cells[colName].Value = "";
                    row.Cells[colName].ReadOnly = true;
                }
            }

            row.DefaultCellStyle.BackColor = Color.WhiteSmoke;
            row.DefaultCellStyle.ForeColor = Color.DimGray;

            UpdateDeleteSelectedRow();
        }

        // Retrouve la ligne corbeille, ou null si elle n'existe pas (encore).
        private DataGridViewRow GetDeleteSelectedRow()
        {
            foreach (DataGridViewRow row in _mainForm.dataGridViewCampaigns.Rows)
            {
                if (DeleteSelectedRowTag.Equals(row.Tag))
                    return row;
            }

            return null;
        }

        // Lignes actuellement cochées dans la grid.
        private List<(int RowIndex, string Name, string FolderPath)> GetCheckedRows()
        {
            string basePath = ParamConf.PATH_SavedGames_DCS + @"\Mods\tech\DCE\Missions\Campaigns\";
            var checkedRows = new List<(int RowIndex, string Name, string FolderPath)>();

            foreach (DataGridViewRow row in _mainForm.dataGridViewCampaigns.Rows)
            {
                if (row.IsNewRow) continue;
                if (DeleteSelectedRowTag.Equals(row.Tag)) continue;

                if (row.Cells["Select"].Value is bool isChecked && isChecked)
                {
                    string rowName = row.Cells["Name"].Value?.ToString();
                    if (!string.IsNullOrEmpty(rowName))
                    {
                        checkedRows.Add((row.Index, rowName, Path.Combine(basePath, rowName)));
                    }
                }
            }

            return checkedRows;
        }

        // Supprime toutes les lignes cochées, avec confirmation, sablier et compte-rendu.
        // Appelée par la corbeille de la dernière ligne et par la colonne Delete.
        private async Task DeleteSelectedRowsAsync()
        {
            var checkedRows = GetCheckedRows();

            if (checkedRows.Count == 0)
            {
                MessageBox.Show(
                    "No item selected. Tick the boxes in the last column first.",
                    "Delete selected",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            // IMPORTANT : les cases sont désormais disponibles sur TOUTES les lignes, donc une
            // suppression groupée peut emporter de vraies campagnes. On liste les noms dans la
            // confirmation plutôt qu'un simple compte, pour éviter la mauvaise surprise.
            string namesPreview = string.Join("\r\n", checkedRows.Take(15).Select(r => "  - " + r.Name));
            if (checkedRows.Count > 15)
                namesPreview += "\r\n  ... and " + (checkedRows.Count - 15) + " more";

            var confirmBulk = MessageBox.Show(
                "Delete " + checkedRows.Count + " selected item(s) ?\r\n\r\n" + namesPreview,
                "Confirm",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning
            );

            if (confirmBulk != DialogResult.Yes)
                return;

            int minRowIndex = checkedRows.Min(r => r.RowIndex);
            var results = new List<(string Name, DeleteOutcome Outcome)>();

            // Curseur sablier : les tentatives sur fichier verrouillé peuvent prendre quelques
            // secondes (surtout à plusieurs éléments) ; sans ça, l'appli paraît figée.
            _mainForm.Cursor = Cursors.WaitCursor;
            try
            {
                foreach (var item in checkedRows)
                {
                    DeleteOutcome outcome = await DeleteCampaignFilesAndFolderAsync(item.Name, item.FolderPath);
                    results.Add((item.Name, outcome));
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error: " + ex.Message);
            }
            finally
            {
                _mainForm.Cursor = Cursors.Default;
            }

            if (results.Count > 0)
            {
                ShowDeleteReport(results);
            }

            await LoadCampaignsAsync(restoreRowIndex: minRowIndex);
        }

        // Met à jour le libellé de la ligne corbeille selon le nombre de cases cochées.
        private void UpdateDeleteSelectedRow()
        {
            var row = GetDeleteSelectedRow();

            if (row == null)
                return;

            int count = GetCheckedRows().Count;

            row.Cells["Aircraft"].Value = count > 0
                ? "Delete " + count + " selected item(s)  →"
                : "Tick boxes to select items, then click the bin  →";
        }

        private void ShowDeleteReport(List<(string Name, DeleteOutcome Outcome)> results)
        {
            var problems = results.Where(r => r.Outcome.LockedFiles.Count > 0 || !r.Outcome.FolderRemoved).ToList();
            int fullySuccessful = results.Count - problems.Count;

            var sb = new StringBuilder();

            if (results.Count == 1)
            {
                var r = results[0];

                if (problems.Count == 0)
                {
                    sb.AppendLine("'" + r.Name + "' deleted successfully (" + r.Outcome.DeletedCount + " file(s) removed).");
                }
                else
                {
                    sb.AppendLine("'" + r.Name + "' : " + r.Outcome.DeletedCount + " file(s) deleted.");

                    if (r.Outcome.LockedFiles.Count > 0)
                    {
                        sb.AppendLine();
                        sb.AppendLine(r.Outcome.LockedFiles.Count + " file(s) still in use (not deleted):");
                        foreach (string f in r.Outcome.LockedFiles)
                            sb.AppendLine("  - " + Path.GetFileName(f));
                    }

                    if (!r.Outcome.FolderRemoved)
                    {
                        sb.AppendLine();
                        sb.AppendLine("The campaign folder itself could not be removed: " + r.Outcome.FolderRemovalError);
                    }

                    sb.AppendLine();
                    sb.AppendLine("Close DCS (and the mission editor), then try again.");
                }
            }
            else
            {
                sb.AppendLine(fullySuccessful + " of " + results.Count + " item(s) deleted successfully.");

                if (problems.Count > 0)
                {
                    sb.AppendLine();
                    sb.AppendLine(problems.Count + " item(s) could not be fully removed:");

                    foreach (var r in problems)
                    {
                        sb.Append("  - " + r.Name);

                        if (r.Outcome.LockedFiles.Count > 0)
                        {
                            sb.Append(" : " + r.Outcome.LockedFiles.Count + " file(s) still in use (" +
                                string.Join(", ", r.Outcome.LockedFiles.Select(Path.GetFileName)) + ")");
                        }

                        if (!r.Outcome.FolderRemoved)
                        {
                            sb.Append(" — folder not removed (" + r.Outcome.FolderRemovalError + ")");
                        }

                        sb.AppendLine();
                    }

                    sb.AppendLine();
                    sb.AppendLine("Close DCS (and the mission editor), then try again.");
                }
            }

            MessageBox.Show(
                sb.ToString(),
                "Delete report",
                MessageBoxButtons.OK,
                problems.Count > 0 ? MessageBoxIcon.Warning : MessageBoxIcon.Information);
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

                //FormUtils.LogRegister("FormMain Campaign installed RefreshCampaignUpdates()");

                //await _mainForm.campaignUpdater.RefreshCampaignUpdates(
                //    _mainForm.CampaignDataGridView,
                //    _mainForm.textBox_SavedGames.Text);

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

        // contextLabel : permet de réutiliser cette méthode ailleurs (ex: après un Import)
        // sans que le message final ne parle à tort de "Repair".
        internal async Task RepairCampaignAsync(string name, string contextLabel = "Repair")
        {
            Utils.FormUtils.LogRegister(contextLabel + " demandé pour la campagne '" + name + "'");

            string campaignsRoot = ParamConf.PATH_SavedGames_DCS + @"\Mods\tech\DCE\Missions\Campaigns";
            string folderPath = Path.Combine(campaignsRoot, name);
            string initFolderPath = Path.Combine(folderPath, "Init");

            var report = new List<string>();

            // camp_init.lua / conf_mod.lua : créés depuis la référence s'ils sont absents,
            // sinon simplement recalés (voir CampInitUpdater / ConfModTemplateUpdater).
            bool campInitExistedBefore = File.Exists(new CampInitUpdater().GetCampInitPath(name));
            bool confModExistedBefore = File.Exists(new ConfModLoader().GetConfModPath(name));

            ConfUpdateResult campInitResult = new CampInitUpdater().UpdateCampaign(name);
            ConfUpdateResult confModResult = new ConfModTemplateUpdater().UpdateCampaign(name); // dans cet ordre

            report.Add(DescribeResult("camp_init.lua", campInitExistedBefore, campInitResult));
            report.Add(DescribeResult("conf_mod.lua", confModExistedBefore, confModResult));

            // path.bat : toujours régénéré si les chemins DCS sont configurés, absent ou pas.
            string pathBatFile = Path.Combine(initFolderPath, "path.bat");
            bool pathBatExistedBefore = File.Exists(pathBatFile);

            if (ParamConf.PATH_DCS_Root != "" && ParamConf.PATH_SavedGames_DCS != "")
            {
                string textPathBat = "REM Core or Main DCS ou DCS.beta path, always end the line with \\ \r\n" +
                               "set \"pathDCS=" + ParamConf.PATH_DCS_Root + "\\\"\r\n" +
                               "REM Core or Main DCS ou DCS.beta path, always end the line with \\ \r\n" +
                               "set \"pathSavedGames=" + ParamConf.PATH_SavedGames_DCS + "\\\"\r\n" +
                               "REM DCE ScriptMod version not any / or \\ and no space before and after = \r\n" +
                               "set \"versionPackageICM=" + TestFile.ScriptsMod + "\"\r\n" +
                               "\r\n" +
                               "\r\n" +
                               "REM After each change, You must launch the FirsMission.bat for it to be taken into account.";

                Directory.CreateDirectory(initFolderPath); // au cas où même le dossier Init aurait disparu
                File.WriteAllText(pathBatFile, textPathBat);
                report.Add("path.bat: " + (pathBatExistedBefore ? "regenerated" : "created"));
            }
            else
            {
                report.Add("path.bat: not regenerated (DCS / Saved Games paths not configured in Options)");
            }

            // .cmp : la convention de nommage est fixe (nameCamp_first.miz / nameCamp_ongoing.miz),
            // donc on peut l'écrire même si les .miz manquent encore - requiredMissionFiles continue
            // de les signaler séparément.
            bool cmpExistedBefore = File.Exists(Path.Combine(campaignsRoot, name + ".cmp"));
            CampaignRepair.TryRepairCmpFile(campaignsRoot, name);
            report.Add(".cmp: " + (cmpExistedBefore ? "already present" : "created (references " + name + "_first.miz / " + name + "_ongoing.miz)"));

            // .png : jamais bloquant, juste un visuel de secours.
            bool pngExistedBefore = File.Exists(Path.Combine(campaignsRoot, name + ".png"));
            CampaignRepair.TryRepairPictureFile(campaignsRoot, name);
            report.Add(".png: " + (pngExistedBefore ? "already present" : "placeholder image created"));

            if (campInitResult == ConfUpdateResult.ReferenceMissing || confModResult == ConfUpdateResult.ReferenceMissing)
            {
                report.Add("");
                report.Add("⚠ UTIL_REF_conf_mod.lua and/or UTIL_REF_camp_init.lua could not be found in ScriptsMod - please update ScriptsMod.");
            }

            // .miz manquants : on ne les régénère QUE si base_mission.miz existe (c'est le vrai
            // template, voir discussion précédente). Approche simple, choisie par Miguel : on
            // vide Active/ (First Mission n'en a pas besoin pour tourner, mais il la repeuple
            // lui-même - nécessaire ensuite pour que Skip Mission puisse générer _ongoing.miz),
            // puis on enchaîne First Mission et Skip Mission. Confirmation obligatoire : vider
            // Active/ efface toute progression déjà présente.
            bool hasBaseMission = File.Exists(Path.Combine(initFolderPath, "base_mission.miz"));
            bool firstMizMissing = !File.Exists(Path.Combine(campaignsRoot, name + "_first.miz"));
            bool ongoingMizMissing = !File.Exists(Path.Combine(campaignsRoot, name + "_ongoing.miz"));

            report.Add("");
            report.Add("base_mission.miz: " + (hasBaseMission ? "found" : "not found"));

            if ((firstMizMissing || ongoingMizMissing) && hasBaseMission)
            {
                var confirm = MessageBox.Show(
                    "The mission files for '" + name + "' are missing, but the base mission template (base_mission.miz) is present.\n\n" +
                    "DCE_Manager can generate them now by clearing the Active folder and running First Mission, then Skip Mission.\n\n" +
                    "WARNING: this will erase any saved progress currently in the Active folder for this campaign.\n\n" +
                    "Generate the missing missions now?",
                    "Generate missing missions — " + name,
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning);

                if (confirm == DialogResult.Yes)
                {
                    string activeFolderPath = Path.Combine(folderPath, "Active");

                    if (Directory.Exists(activeFolderPath))
                    {
                        foreach (string file in Directory.GetFiles(activeFolderPath, "*", SearchOption.AllDirectories))
                        {
                            try { File.Delete(file); }
                            catch (IOException) { /* fichier verrouillé, tant pis, on continue */ }
                        }
                    }

                    report.Add("Active folder cleared.");
                    report.Add("Generating missions from base_mission.miz...");

                    Saver_TargetList_Wargame.WriteInitialFormations(name);
                    await RunScriptsModInteractiveAsync(name, folderPath, "FirstMission.bat");

                    WargameEngineLosses.ProcessBeforeMission(name);
                    Saver_TargetList_Wargame.WriteNewActiveFormations(name);
                    await RunScriptsModInteractiveAsync(name, folderPath, "SkipMission.bat");
                }
            }

            _alreadyUpdated.Add(name); // évite de refaire ce travail juste après, au premier clic normal

            await LoadCampaignsAsync(selectCampaignName: name); // recharge la grid ET se repositionne dessus

            // Ce qui reste manquant après coup - fichiers propres à la campagne qu'on ne sait
            // jamais régénérer, ou .miz que tu as refusé de générer.
            var stillMissing = RequiredInitFiles.Where(f => !File.Exists(Path.Combine(initFolderPath, f))).ToList();

            string[] requiredMissionFiles = { name + "_first.miz", name + "_ongoing.miz", name + ".cmp" };
            stillMissing.AddRange(requiredMissionFiles.Where(f => !File.Exists(Path.Combine(campaignsRoot, f))));

            if (stillMissing.Count > 0)
            {
                report.Add("");
                report.Add("⚠ Still missing, cannot be repaired automatically: " + string.Join(", ", stillMissing));
            }

            MessageBox.Show(
                string.Join("\r\n", report),
                (stillMissing.Count > 0 ? "Partial " + contextLabel.ToLowerInvariant() + " — " : contextLabel + " complete — ") + name,
                MessageBoxButtons.OK,
                stillMissing.Count > 0 ? MessageBoxIcon.Warning : MessageBoxIcon.Information);
        }

        private static string DescribeResult(string fileLabel, bool existedBefore, ConfUpdateResult result)
        {
            if (result == ConfUpdateResult.ReferenceMissing)
                return fileLabel + ": not regenerated (reference file missing in ScriptsMod)";

            if (result == ConfUpdateResult.MergeAborted)
                return fileLabel + ": merge failed, local file left unchanged";

            return fileLabel + ": " + (existedBefore ? "checked / updated if needed" : "created from reference template");
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
