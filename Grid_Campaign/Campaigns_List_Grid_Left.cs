using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
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
                AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill
            });

            // Colonne pour ouvrir le dossier
            GridCampaigns_AddButtonColumn("Folder", "📂", 55);


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

            GridCampaigns_AddButtonColumn("First", "▶", 55);
            GridCampaigns_AddButtonColumn("Skip", "⏭", 55, useColumnTextForButtonValue: false);
            GridCampaigns_AddButtonColumn("Parameters", "⚙", 55);
            GridCampaigns_AddButtonColumn("CampaignSetup", "🛠", 55);
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


        private void GridCampaigns_AddButtonColumn(string name, string text, int width, bool useColumnTextForButtonValue = true)
        {
            _mainForm.dataGridViewCampaigns.Columns.Add(new DataGridViewButtonColumn()
            {
                Name = name,
                Text = text,
                UseColumnTextForButtonValue = useColumnTextForButtonValue,
                Width = width,
                FlatStyle = FlatStyle.Flat
            });
        }

        public void UpdateCampaignSetupColumnVisibility()
        {
            if (_mainForm.dataGridViewCampaigns.Columns.Contains("CampaignSetup"))
            {
                _mainForm.dataGridViewCampaigns.Columns["CampaignSetup"].Visible =
                    ParamConf.UserLevel == UserLevel.CampaignMaker;
            }
        }

        private async void GridCampaigns_CellClick(object sender, DataGridViewCellEventArgs e)
        {
            // Ignore header
            if (e.RowIndex < 0 || e.ColumnIndex < 0)
            {
                return; 
                FormUtils.LogRegister("GridCampaigns_CellClick RETURN A");
            }
                

            if (e.ColumnIndex >= _mainForm.dataGridViewCampaigns.Columns.Count)
            {
                return; 
                FormUtils.LogRegister("GridCampaigns_CellClick RETURN B");
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
                return; 
                FormUtils.LogRegister("GridCampaigns_CellClick RETURN C");
            }
            

            if (string.IsNullOrEmpty(name))
            {
                return; 
                FormUtils.LogRegister("GridCampaigns_CellClick RETURN D");
            }

            // Ligne "problème" (dossier incomplet ou fichier orphelin) : seuls Delete et Folder
            // ont un sens ici. Les autres colonnes (First/Skip/Parameters/CampaignSetup, ou
            // l'ouverture normale du panneau de droite en fin de méthode) sont ignorées, plutôt
            // que de tenter d'agir sur des fichiers qui peuvent ne pas exister.
            if (_incompleteOrOrphanNames.Contains(name) && columnName != "Delete" && columnName != "Folder")
            {
                return;
            }

            // Recale camp_init.lua puis conf_mod.lua sur leurs fichiers de référence avant
            // d'afficher quoi que ce soit pour cette campagne. Une seule fois par campagne
            // pour la session (voir _alreadyUpdated en haut du fichier).
            if (_alreadyUpdated.Add(name))
            {
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

            // Recale camp_init.lua puis conf_mod.lua sur leurs fichiers de référence avant
            // d'afficher quoi que ce soit pour cette campagne. Une seule fois par campagne
            // pour la session (voir _alreadyUpdated en haut du fichier).
            //if (_alreadyUpdated.Add(name))
            //{
            //    FormUtils.LogRegister("GridCampaigns_CellClick E Updating campaign '" + name + "' with reference files.");

            //    new CampInitUpdater().UpdateCampaign(name);
            //    new ConfModTemplateUpdater().UpdateCampaign(name); // dans cet ordre

            //    FormUtils.LogRegister("GridCampaigns_CellClick F Update complete for campaign '" + name + "'.");
            //}


            string basePath = ParamConf.PATH_SavedGames_DCS + @"\Mods\tech\DCE\Missions\Campaigns\";

            string folderPath = Path.Combine(basePath, name);

            Utils.FormUtils.LogRegister($"Clicked on column '{columnName}' for campaign '{name}' folderPath '{folderPath}'");

            if (columnName == "First")
            {
                string batPath = Path.Combine(folderPath, "FirstMission.bat");

                if (File.Exists(batPath))
                {
                    System.Diagnostics.Process.Start(new ProcessStartInfo()
                    {
                        FileName = batPath,
                        WorkingDirectory = folderPath,
                        UseShellExecute = true
                    });
                }

                return;
            }
            else if (columnName == "Skip")
            {
                string nbMissionTextSkip = _mainForm.dataGridViewCampaigns.Rows[e.RowIndex].Cells["Missions"].Value?.ToString();

                int nbMissionSkip;
                int.TryParse(nbMissionTextSkip, out nbMissionSkip);

                if (nbMissionSkip <= 0)
                    return; // bouton grisé : aucune mission jouée, on ignore le clic

                string batPath = Path.Combine(folderPath, "SkipMission.bat");

                if (File.Exists(batPath))
                {
                    System.Diagnostics.Process.Start(new ProcessStartInfo()
                    {
                        FileName = batPath,
                        WorkingDirectory = folderPath,
                        UseShellExecute = true
                    });
                }
            }
            else if (columnName == "Parameters")
            {
                //string filePath = Path.Combine(folderPath, @"Init\conf_mod.lua");

                //if (File.Exists(filePath))
                //{
                //    System.Diagnostics.Process.Start(new ProcessStartInfo()
                //    {
                //        FileName = filePath,
                //        UseShellExecute = true
                //    });
                //}

                Utils.FormUtils.LogRegister( Utils.FormUtils.ToTitleCase("Open Parameters for campaign '" + name + "'"));

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
        public async Task LoadCampaignsAsync(string selectCampaignName = null, int? restoreRowIndex = null)
        {
            // Different configurations (DCSA/DCSB...) can contain campaigns with the
            // same folder name; the ConfMod cache is only keyed by that name, so it
            // must be cleared whenever the campaign list is (re)loaded.
            ConfModLoader.ClearCache();

            ResetCurrentCampaign();

            _mainForm.dataGridViewCampaigns.Rows.Clear();
            _incompleteOrOrphanNames.Clear();

            List<CampaignInfo> campaignUpdateList = new List<CampaignInfo>();

            var LoadCampaigns = Stopwatch.StartNew();

            int nbCampaign = 0;

            string campaignsRoot = ParamConf.PATH_SavedGames_DCS + @"\Mods\tech\DCE\Missions\Campaigns";

            bool folderCampExists = System.IO.Directory.Exists(campaignsRoot);

            if (folderCampExists)
            {
                foreach (string subFolder in Directory.GetDirectories(campaignsRoot))
                {

                    // 🔥 cache local des fichiers (1 lecture max)
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


                    //  COPIE ICI TOUT TON CODE ACTUEL DE LA BOUCLE
                    string[] NameCampTab = subFolder.Split('\\');
                    string NameCamp = NameCampTab[NameCampTab.Count() - 1];

                    bool folderLocExists = System.IO.Directory.Exists(subFolder);

                    // Complétude du dossier : les 6 fichiers Init attendus + les 2 .miz + le .cmp.
                    // Pourquoi : un dossier créé par un clonage ou une mise à jour interrompue
                    // peut exister sans être exploitable ; mieux vaut le signaler à l'utilisateur
                    // (ligne "problème" + bouton Delete) que de le planter silencieusement plus
                    // tard, ou de le montrer comme une campagne normale et fonctionnelle.
                    string initFolder = subFolder + @"\Init\";
                    string[] requiredInitFiles = { "camp_init.lua", "camp_triggers_init.lua", "conf_mod.lua", "db_airbases.lua", "targetlist_init.lua", "path.bat" };

                    var missingFiles = requiredInitFiles.Where(f => !File.Exists(initFolder + f)).ToList();

                    string[] requiredMissionFiles = { NameCamp + "_first.miz", NameCamp + "_ongoing.miz", NameCamp + ".cmp" };
                    missingFiles.AddRange(requiredMissionFiles.Where(f => !File.Exists(campaignsRoot + @"\" + f)));

                    if (missingFiles.Count > 0)
                    {
                        _incompleteOrOrphanNames.Add(NameCamp);
                        AddProblemRow(NameCamp, "Dossier incomplet — manque : " + string.Join(", ", missingFiles));
                        continue;
                    }

                    //cherche la version inscrite dans path.bat
                    string PathBatFile = subFolder + @"\Init\path.bat";
                    bool fileExistPathBat = File.Exists(PathBatFile);

                    if (fileExistPathBat)
                    {
                        if (ParamConf.PATH_DCS_Root != "" & ParamConf.PATH_SavedGames_DCS != "")
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

                            System.IO.File.WriteAllText(PathBatFile, textPathBat);
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

                        string colorFM = "";
                        string colorSM = "";
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
                                //File.Copy(filePNGbyePlane, filePNG, true);
                                try
                                {
                                    File.Copy(filePNGbyePlane, filePNG, true);
                                }
                                catch (IOException)
                                {
                                    // ignore si en cours d'utilisation
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

                            // Le bouton Skip ne doit être visible que si au moins une mission a été jouée
                            int nbMissionParsed;
                            int.TryParse(NbMission, out nbMissionParsed);
                            bool skipVisible = nbMissionParsed > 0;

                            _mainForm.dataGridViewCampaigns.Rows.Add(
                                null,       // Clone (bouton)
                                img,        // Image
                                NameCamp,   // Name
                                null,       // Folder
                                VerCamp,    // Version
                                NbMission,  // Missions
                                type,       // Aircraft
                                null,       // First
                                skipVisible ? "⏭" : "",   // Skip (vide = invisible)
                                null,       // Config
                                null        // Delete
                            );

                            int rowIndex = _mainForm.dataGridViewCampaigns.Rows.Count - 1;

                            // Case à cocher disponible sur toutes les lignes, campagnes normales
                            // comprises (et pas seulement les lignes "problème").
                            _mainForm.dataGridViewCampaigns.Rows[rowIndex].Cells["Select"].Value = false;

                            // Aucune mission jouée : le bouton reste vide (pas d'icône) et non cliquable
                            _mainForm.dataGridViewCampaigns.Rows[rowIndex].Cells["Skip"].ReadOnly = !skipVisible;

                            // Exemple : bouton Skip rouge si besoin (uniquement si visible)
                            if (skipVisible && colorSM == "red")
                            {
                                _mainForm.dataGridViewCampaigns.Rows[rowIndex].Cells["Skip"].Style.BackColor = Color.DarkRed;
                            }

                            // Exemple : bouton First rouge
                            if (colorFM == "red")
                            {
                                _mainForm.dataGridViewCampaigns.Rows[rowIndex].Cells["First"].Style.BackColor = Color.DarkRed;
                            }

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
                "",             // Version
                "",             // Missions
                "⚠ " + reason,  // Aircraft (utilisée ici comme colonne de statut)
                null,           // First
                "",             // Skip (masqué)
                null,           // CampaignSetup
                null            // Delete
            );

            int rowIndex = _mainForm.dataGridViewCampaigns.Rows.Count - 1;
            _mainForm.dataGridViewCampaigns.Rows[rowIndex].Cells["Skip"].ReadOnly = true;
            _mainForm.dataGridViewCampaigns.Rows[rowIndex].Cells["Select"].Value = false;
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
            foreach (string colName in new[] { "Clone", "Folder", "First", "Skip", "Parameters", "CampaignSetup", "Select" })
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

        



    }
}
