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
    // Chargement de la liste des campagnes dans la grille (partie de CampaignGridLeft, voir Campaigns_List_Grid_Left.cs)
    public partial class CampaignGridLeft
    {
        // Charge toutes les campagnes (code existant déplacé ici)
        // selectCampaignName : après rechargement, sélectionne/scroll sur la ligne portant ce
        // nom (ex: la campagne qui vient d'être clonée). Prioritaire sur restoreRowIndex.
        // restoreRowIndex : si selectCampaignName est vide, restaure la vue à peu près là où
        // elle était (ex: ligne 34 après suppression de la ligne 35), borné à la nouvelle taille
        // de la grid.
        // Pas de async : tout le corps est synchrone (accès disque local, remplissage de grid).
        // On renvoie quand même un Task pour ne rien changer aux appelants qui font "await".
        public Task LoadCampaignsAsync(string selectCampaignName = null, int? restoreRowIndex = null, bool keepRightPanel = false)
        {
            // Different configurations (DCSA/DCSB...) can contain campaigns with the
            // same folder name; the ConfMod cache is only keyed by that name, so it
            // must be cleared whenever the campaign list is (re)loaded.
            ConfModLoader.ClearCache();

            RefreshScriptsModState();

            if (keepRightPanel && CurrentCampaignEdit != null)
            {
                // On garde la campagne affichée à droite. Sans ligne précise demandée, on
                // re-sélectionne la ligne de la campagne affichée (Rows.Clear() vide la sélection).
                if (string.IsNullOrEmpty(selectCampaignName))
                    selectCampaignName = ParamCampaignSelected.NameCampaign;
            }
            else
            {
                ResetCurrentCampaign();
            }

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

                        if (folderLocExists)
                        {

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

                            // Icône Wargame : Tag = la campagne a un wargame (sert au dessin et à l'infobulle)
                            bool hasWargame = DCE_Manager.UserControls.ucCampaign.CampaignHasWargame(NameCamp);
                            var wargameCell = _mainForm.dataGridViewCampaigns.Rows[rowIndex].Cells["Wargame"];
                            wargameCell.Tag = hasWargame;
                            wargameCell.ToolTipText = hasWargame ? "Edit the wargame of this campaign" : "Create a wargame for this campaign";
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
            foreach (string colName in new[] { "Clone", "Folder", "Export", "QuickActions", "Parameters", "CampaignSetup", "Select", "Rename", "Triggers" })
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

    }
}
