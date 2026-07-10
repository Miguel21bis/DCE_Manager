using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Security.Policy;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using DCE_Manager.Parameters;
using DCE_Manager.Properties;
using DCE_Manager.Utils;
using DCE_Manager.Clone;
using static System.Windows.Forms.VisualStyles.VisualStyleElement;
using static DCE_Manager.Utils.FormUtils;

namespace DCE_Manager.Update
{
    public partial class CampaignUpdater
    {
        private readonly Form1 form;

        private CancellationTokenSource downloadCancellation;

        private readonly Dictionary<int, CampaignInfo> displayedCampaigns = new Dictionary<int, CampaignInfo>();

        private readonly GithubHelper github;

        public CampaignUpdater(Form1 form)
        {
            this.form = form;
            github = new GithubHelper();

        }


        // Charge uniquement les informations nécessaires aux mises à jour.
        // Pourquoi : éviter le scan complet des campagnes.
        public List<CampaignInfo> LoadCampaignsForUpdate( string savedGamesPath)
        {
            List<CampaignInfo> campaignList = new List<CampaignInfo>();

            string campaignRoot = Path.Combine( savedGamesPath, @"Mods\tech\DCE\Missions\Campaigns");

            if (!Directory.Exists(campaignRoot)) return campaignList;

            foreach (string campaignFolder in Directory.GetDirectories(campaignRoot))
            {
                string campInit = Path.Combine( campaignFolder, @"Init\camp_init.lua");

                 if (!File.Exists(campInit))
                    continue;

                CampaignInfo campaign = new CampaignInfo();

                campaign.Name = Path.GetFileName(campaignFolder);

                campaign.Folder = campaignFolder;

                int valueFound = 0;

                foreach (string line in File.ReadLines(campInit))
                {
                    string trim = line.Trim();

                    if (trim.StartsWith("version"))
                    {
                        campaign.LocalVersion = GetLuaStringValue(trim);

                        valueFound++;
                    }
                    else if (trim.StartsWith("campaignId"))
                    {
                        campaign.CampaignId = GetLuaStringValue(trim);

                       
                        valueFound++;
                    }
                    else if (trim.StartsWith("repositoryUrl"))
                    {
                        campaign.RepositoryUrl = GetLuaStringValue(trim);

                        valueFound++;
                    }

                    if (valueFound == 3)
                        break;
                }

                if (!string.IsNullOrEmpty(campaign.CampaignId))
                    campaignList.Add(campaign);
            }

            return campaignList;
        }

        // Met à jour la grille des campagnes.
        // Pourquoi : vérifier les releases et afficher le résultat.
        public async Task RefreshCampaignUpdates( DataGridView campaignGrid, string savedGamesPath)
        {
            displayedCampaigns.Clear();

            ParamUpdater.NbUpdateAvailable = 0;

            campaignGrid.Rows.Clear();

            List<CampaignInfo> campaigns = LoadCampaignsForUpdate(savedGamesPath);

            Dictionary<string, CampaignInfo> uniqueCampaigns = new Dictionary<string, CampaignInfo>();

            foreach (CampaignInfo campaign in campaigns)
            {
                if (!uniqueCampaigns.ContainsKey(campaign.CampaignId))
                {
                    campaign.InstalledCampaigns.Add(campaign.Name + " (" + campaign.LocalVersion + ")");
                    campaign.InstalledVersions.Add(campaign.LocalVersion);

                    uniqueCampaigns.Add(campaign.CampaignId, campaign);
                }
                else
                {
                    uniqueCampaigns[campaign.CampaignId].InstalledCampaigns.Add(campaign.Name + " (" + campaign.LocalVersion + ")");
                    uniqueCampaigns[campaign.CampaignId].InstalledVersions.Add(campaign.LocalVersion);
                }
            }


            // 1. Initialiser le constructeur de texte
            StringBuilder logBuilder = new StringBuilder();
            logBuilder.AppendLine("=== Début de l'inventaire uniqueCampaigns ===");

            // 2. Parcourir le dictionnaire
            foreach (KeyValuePair<string, CampaignInfo> kvp in uniqueCampaigns)
            {
                string campaignId = kvp.Key;
                CampaignInfo info = kvp.Value;

                // Joindre les listes internes (versions et noms installés) pour les afficher proprement
                string listCampaigns = string.Join(", ", info.InstalledCampaigns);
                string listVersions = string.Join(", ", info.InstalledVersions);

                // Construire la ligne pour cette campagne
                logBuilder.AppendLine($"- ID: {campaignId} | Nom principal: {info.Name} | Version Locale: {info.LocalVersion}");
                logBuilder.AppendLine($"  Campagnes combinées: [{listCampaigns}]");
                logBuilder.AppendLine($"  Versions combinées: [{listVersions}]");
                logBuilder.AppendLine(new string('-', 40)); // Petite ligne de séparation interne
            }

            logBuilder.AppendLine("=== Fin de l'inventaire uniqueCampaigns ===");

            // 3. Envoyer le tout d'un coup dans votre méthode de Log
            FormUtils.LogRegister(logBuilder.ToString());


            foreach (CampaignInfo campaign in uniqueCampaigns.Values)
            {
                FormUtils.LogRegister("A await github.GetLatestReleaseFromUrl() ");

                bool success =
                    await github.GetLatestReleaseFromUrl(
                        campaign.RepositoryUrl,
                        ".zip",
                        "",
                        (version, asset, url) =>
                        {
                            campaign.LatestVersion = version;
                            campaign.AssetName = asset;
                            campaign.DownloadUrl = url;
                        });

                FormUtils.LogRegister("B await github.GetLatestReleaseFromUrl() ");

                if (success)
                {
                    FormUtils.LogRegister(
                        "RefreshCampaignUpdates() success: " +
                        " RepositoryUrl " + campaign.RepositoryUrl +
                        " AssetName: " + campaign.AssetName +
                        " LatestVersion: " + campaign.LatestVersion);

                    campaign.AlreadyInstalledLatestVersion =
                        campaign.InstalledVersions.Contains(campaign.LatestVersion);

                    campaign.UpdateAvailable =
                        !campaign.AlreadyInstalledLatestVersion;

                    if (campaign.UpdateAvailable)
                        ParamUpdater.NbUpdateAvailable++;

                    Form1.Instance.tabPageLeft_Update.Text =
                        ParamUpdater.NbUpdateAvailable > 0
                            ? $"Update ({ParamUpdater.NbUpdateAvailable})"
                            : "Update";

                }
                else
                {
                    campaign.LatestVersion = "?";
                    campaign.UpdateAvailable = false;

                    FormUtils.LogRegister(
                    "RefreshCampaignUpdates() else ECHEC: ");
                }

                FormUtils.LogRegister("C await github.GetLatestReleaseFromUrl() ");

                AddCampaignToGrid( campaignGrid, campaign);


                //campaign.InstalledCampaigns.Add(campaign.Name + " (" + campaign.LocalVersion + ")");

                UpdateUtils.RefreshUpdateTab(form);
            }
        }


        // Initialise la grille des mises à jour des campagnes.
        // Pourquoi : centraliser toute la configuration de l'onglet Campaign Update.
        public static void InitCampaignUpdateGrid(DataGridView campaignDataGridView)
        {
            campaignDataGridView.Columns.Clear();

            campaignDataGridView.AutoGenerateColumns = false;
            campaignDataGridView.AllowUserToAddRows = false;
            campaignDataGridView.AllowUserToDeleteRows = false;
            campaignDataGridView.AllowUserToResizeRows = false;
            campaignDataGridView.RowHeadersVisible = false;

            campaignDataGridView.SelectionMode = DataGridViewSelectionMode.FullRowSelect;

            campaignDataGridView.MultiSelect = false;

            campaignDataGridView.RowTemplate.Height = 32;
            campaignDataGridView.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleLeft;

            campaignDataGridView.Columns.Add(
                "Campaign",
                "Campaign");

            campaignDataGridView.Columns.Add(
                "Installed",
                "Installed");

            campaignDataGridView.Columns.Add(
                "Latest",
                "Latest");



            DataGridViewImageColumn iconColumn =
                new DataGridViewImageColumn();

            iconColumn.Name = "Icon";
            iconColumn.HeaderText = "";
            iconColumn.Width = 28;
            iconColumn.ImageLayout =
                DataGridViewImageCellLayout.Zoom;

            campaignDataGridView.Columns.Add(iconColumn);



            campaignDataGridView.Columns.Add(
                "Status",
                "Status");



            DataGridViewButtonColumn actionColumn =
                new DataGridViewButtonColumn();

            actionColumn.Name = "Action";
            actionColumn.HeaderText = "Action";
            actionColumn.UseColumnTextForButtonValue = false;

            campaignDataGridView.Columns.Add(actionColumn);



            campaignDataGridView.Columns["Campaign"].Width = 180;

            campaignDataGridView.Columns["Installed"].Width = 120;

            campaignDataGridView.Columns["Latest"].Width = 90;

            campaignDataGridView.Columns["Icon"].Width = 30;
            campaignDataGridView.Columns["Icon"].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;

            campaignDataGridView.Columns["Status"].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;

            campaignDataGridView.Columns["Action"].Width = 130;

            

           
        }


        // Extrait une chaîne Lua.
        // Pourquoi : factoriser la lecture des variables.
        private string GetLuaStringValue(
            string line)
        {
            int first = line.IndexOf('"');
            int last = line.LastIndexOf('"');

            if (first < 0 || last <= first)
                return "";

            return line.Substring(
                first + 1,
                last - first - 1);
        }



        private void AddCampaignToGrid( DataGridView campaignGrid, CampaignInfo campaign)
        {
            string status;
            string action;

            Image icon;



            if (string.IsNullOrEmpty(campaign.LatestVersion))
            {
                status = "No release";
                action = null;

                icon = Properties.Resources.icons8_warning_blue_30;
            }
            else if (campaign.UpdateAvailable)
            {
                status = "New version available";
                action = "Install";

                icon = Properties.Resources.icons8_download_24;
            }
            else
            {
                status = "Up to date";
                action = null;

                icon = Properties.Resources.icons8_ok_24;
            }



            int row =
                campaignGrid.Rows.Add(
                    campaign.Name,
                    campaign.InstalledCampaigns.Count + " installed",
                    campaign.LatestVersion,
                    icon,
                    status,
                    action);



            displayedCampaigns[row] = campaign;



            campaignGrid.Rows[row]
                .Cells["Installed"]
                .ToolTipText =
                string.Join(
                    Environment.NewLine,
                    campaign.InstalledCampaigns);



            DataGridViewButtonCell button = (DataGridViewButtonCell) campaignGrid.Rows[row].Cells["Action"];

            button.FlatStyle = FlatStyle.Flat;

            button.Style.BackColor = Color.White;

            button.Style.SelectionBackColor = Color.White;

            button.Style.SelectionForeColor = Color.Black;

            button.Style.Padding = new Padding(4, 1, 4, 1);

        }

        // Retourne la campagne correspondant à une ligne.
        // Pourquoi : retrouver les informations lors d'un clic sur Install.
        public CampaignInfo GetCampaignFromRow(int row)
        {
            if (displayedCampaigns.ContainsKey(row))
                return displayedCampaigns[row];

            return null;
        }

        // Retourne le nombre de campagnes ayant une mise à jour.
        // Pourquoi : permettre à l'onglet Update d'afficher le nombre total de mises à jour.
        public int GetUpdateCount()
        {
            FormUtils.LogRegister("GetUpdateCount STARTING ");
            int count = 0;

            foreach (CampaignInfo campaign in displayedCampaigns.Values)
            {
                if (campaign.UpdateAvailable)
                {
                    count++;
                    FormUtils.LogRegister( "GetUpdateCount: " + count);
                }
            }

            FormUtils.LogRegister("GetUpdateCount END return count " + count);
            return count;
        }

        // Télécharge l'archive de la campagne.
        // Pourquoi : récupérer la dernière release avant installation.
        public async Task<string> DownloadCampaign( CampaignInfo campaign, System.Windows.Forms.ProgressBar progressBar, 
            Label label, Label labelCampaignDld_Pct, Label labelTitle)
        {

            downloadCancellation = new CancellationTokenSource();

            CancellationToken token = downloadCancellation.Token;

            //string downloadFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "DCE_Manager");
            string downloadFolder = ParamUpdater.PathDownloadCampaigns;

            progressBar.Visible = true;
            progressBar.Value = 0;

            label.Visible = true;
            labelCampaignDld_Pct.Visible = true;
            label.Text = "Connecting...";

           //string destinationFile = Path.Combine( ParamUpdater.PathDownloadManager, ParamUpdater.AssetName);
            string destinationFile = Path.Combine( ParamUpdater.PathDownloadCampaigns, campaign.AssetName);

            try
            {

                labelTitle.Visible = true;

                labelTitle.Text = "Downloading " + campaign.Name +
                    " " +  campaign.LatestVersion +  "...";

                using (HttpClient client = new HttpClient())
                {
                    client.DefaultRequestHeaders.Add("User-Agent", "DCE_Manager");

                    FormUtils.LogRegister("Start download");
                    FormUtils.LogRegister(campaign.DownloadUrl);

                    using (HttpResponseMessage response = await client.GetAsync(
                        campaign.DownloadUrl,
                        HttpCompletionOption.ResponseHeadersRead,
                        token))
                    //using (HttpResponseMessage response = await client.GetAsync(campaign.DownloadUrl))
                    {
                        response.EnsureSuccessStatusCode();

                        FormUtils.LogRegister("Headers received");

                        using (Stream input = await response.Content.ReadAsStreamAsync())
                        using (FileStream output = new FileStream(destinationFile, FileMode.Create, FileAccess.Write, FileShare.None))
                        {
                            FormUtils.LogRegister(response.Content.Headers.ContentLength.ToString());
                             byte[] buffer = new byte[81920];

                            long totalRead = 0;
                            long totalSize = response.Content.Headers.ContentLength ?? 0;

                            DateTime startTime = DateTime.Now;

                            int lastPercent = -1;

                            int read;

                            //while ((read = await input.ReadAsync(buffer, 0, buffer.Length)) > 0)
                            while ((read = await input.ReadAsync(buffer, 0, buffer.Length, token)) > 0)
                            {

                                token.ThrowIfCancellationRequested();

                                await output.WriteAsync(buffer, 0, read, token);

                                totalRead += read;

                                if (totalSize > 0)
                                {
                                    int percent = (int)(100 * totalRead / totalSize);

                                    if (percent != lastPercent)
                                    {
                                        lastPercent = percent;

                                        progressBar.Value = percent;

                                        double elapsed = (DateTime.Now - startTime).TotalSeconds;

                                        double speed = elapsed > 0 ? totalRead / elapsed : 0;

                                        double remaining = speed > 0 ? (totalSize - totalRead) / speed : 0;

                                        labelCampaignDld_Pct.Text = string.Format( "{0}% ", percent);

                                        label.Text = string.Format(
                                            "{0:0.0}/{1:0.0} MB   {2:0.00} MB/s   ETA {3:mm\\:ss}",
                                            totalRead / 1024d / 1024d,
                                            totalSize / 1024d / 1024d,
                                            speed / 1024d / 1024d,
                                            TimeSpan.FromSeconds(remaining)
                                        );

                                        //label.Text =
                                        //    string.Format(
                                        //        "{0}%   {1:0.0}/{2:0.0} MB   {3:0.00} MB/s   ETA {4:mm\\:ss}",
                                        //        percent,
                                        //        totalRead / 1024d / 1024d,
                                        //        totalSize / 1024d / 1024d,
                                        //        speed / 1024d / 1024d,
                                        //        TimeSpan.FromSeconds(remaining)
                                        //     );

                                    }
                                }
                            }


                            FormUtils.LogRegister("Copy finished");
                        }
                    }
                }

                progressBar.Value = 100;

                label.Text = "Download completed";

                FormUtils.LogRegister("Campaign download completed : " + destinationFile);

                labelTitle.Visible = false;

                return destinationFile;
            }
            catch (OperationCanceledException)
            {
                FormUtils.LogRegister("Campaign download cancelled.");

                if (File.Exists(destinationFile))
                    File.Delete(destinationFile);

                return "";
            }
        }

        public void CancelDownload()
        {
            FormUtils.LogRegister("CancelDownload()");

            if (downloadCancellation != null)
            {
                FormUtils.LogRegister("CTS found");

                downloadCancellation.Cancel();
            }
            else
            {
                FormUtils.LogRegister("CTS NULL");
            }
        }

        // Lit les informations d'une campagne contenue dans un ZIP.
        // Pourquoi : vérifier le contenu avant installation.
        private CampaignPackageInfo ReadCampaignPackage(
            string zipFile)
        {
            using (ZipArchive archive =
                ZipFile.OpenRead(zipFile))
            {
                ZipArchiveEntry campInit =
                    archive.Entries.FirstOrDefault(e =>
                        e.FullName.EndsWith("Init/camp_init.lua", StringComparison.OrdinalIgnoreCase));

                if (campInit == null)
                    return null;

                CampaignPackageInfo info =
                    new CampaignPackageInfo();

                using (StreamReader reader =
                    new StreamReader(campInit.Open()))
                {
                    while (!reader.EndOfStream)
                    {
                        string line = reader.ReadLine().Trim();

                        if (line.StartsWith("campaignId"))
                            info.CampaignId = GetLuaStringValue(line);

                        else if (line.StartsWith("version"))
                            info.Version = GetLuaStringValue(line);

                        else if (line.StartsWith("title"))
                            info.Title = GetLuaStringValue(line);

                        else if (line.StartsWith("repositoryUrl"))
                            info.RepositoryUrl = GetLuaStringValue(line);
                    }
                }

                return info;
            }
        }

        // Installe une campagne téléchargée.
        // Pourquoi : permettre plusieurs versions d'une même campagne sans écraser les anciennes.
        public void ExtractCampaignZip(
            string zipFile,
            string savedGamesPath,
            CampaignInfo campaign)
        {

            //string newNameCamp = null;
            string oldNameCamp = campaign.Name;
            //string campaignPathNewName = null;

            string newNameCamp = campaign.Name + " " + campaign.LatestVersion;
            string campaignRootRelative = Path.Combine( "Mods", "tech", "DCE", "Missions", "Campaigns", newNameCamp);
            string campaignMainPath = Path.Combine(savedGamesPath,"Mods", "tech", "DCE", "Missions", "Campaigns");

            string campaignPathNewName = Path.Combine(savedGamesPath, campaignRootRelative);

            using (ZipArchive archive = ZipFile.OpenRead(zipFile))
            {
                foreach (ZipArchiveEntry entry in archive.Entries)
                {
                    string zipPath = entry.FullName.Replace('\\', '/');

                    if (string.IsNullOrWhiteSpace(zipPath))
                        continue;

                    //----------------------------------------------------
                    // On ignore complètement ScriptsMod
                    //----------------------------------------------------

                    if (zipPath.Contains("/ScriptsMod.NG/"))
                        continue;

                    //----------------------------------------------------
                    // On ignore Active / Debug / Debriefing
                    //----------------------------------------------------

                    bool isSpecialFolder =
                        zipPath.Contains("/Active/") ||
                        zipPath.Contains("/Debug/") ||
                        zipPath.Contains("/Debriefing/");

                    //----------------------------------------------------
                    // On ne garde que ce qui est dans DCS_SavedGames_Path
                    //----------------------------------------------------

                    if (!zipPath.StartsWith("DCS_SavedGames_Path/"))
                        continue;

                    string relativePath = zipPath.Substring("DCS_SavedGames_Path/".Length);

                    //----------------------------------------------------
                    // Renommage automatique du dossier campagne
                    //----------------------------------------------------

                    string campaignPrefix = "Mods/tech/DCE/Missions/Campaigns/" + campaign.Name + "/";

                    if (relativePath.StartsWith(campaignPrefix))
                    {
                        relativePath = "Mods/tech/DCE/Missions/Campaigns/" +
                            campaign.Name + " " + campaign.LatestVersion +
                            "/" + relativePath.Substring(campaignPrefix.Length);

                    }

                    if (relativePath.Equals(
                        $@"Mods/tech/DCE/Missions/Campaigns/{oldNameCamp}_first.miz",
                        StringComparison.OrdinalIgnoreCase))
                    {
                        relativePath = $@"Mods/tech/DCE/Missions/Campaigns/{newNameCamp}_first.miz";
                    }
                    else if (relativePath.Equals(
                                 $@"Mods/tech/DCE/Missions/Campaigns/{oldNameCamp}_ongoing.miz",
                                 StringComparison.OrdinalIgnoreCase))
                    {
                        relativePath = $@"Mods/tech/DCE/Missions/Campaigns/{newNameCamp}_ongoing.miz";
                    }
                    else if (relativePath.Equals(
                                 $@"Mods/tech/DCE/Missions/Campaigns/{oldNameCamp}.cmp",
                                 StringComparison.OrdinalIgnoreCase))
                    {
                        relativePath = $@"Mods/tech/DCE/Missions/Campaigns/{newNameCamp}.cmp";
                    }
                    else if (relativePath.Equals(
                                 $@"Mods/tech/DCE/Missions/Campaigns/{oldNameCamp}.png",
                                 StringComparison.OrdinalIgnoreCase))
                    {
                        relativePath = $@"Mods/tech/DCE/Missions/Campaigns/{newNameCamp}.png";
                    }

                    string destinationFile = Path.Combine(savedGamesPath, relativePath);

                    
                    //----------------------------------------------------
                    // Dossier
                    //----------------------------------------------------

                    //if (string.IsNullOrEmpty(entry.Name))
                    //{
                    //    Directory.CreateDirectory(destinationFile);
                    //    continue;
                    //}

                    if (string.IsNullOrEmpty(entry.Name))
                    {
                        // On crée toujours les dossiers, même Active, Debug ou Debriefing.
                        Directory.CreateDirectory(destinationFile);
                        continue;
                    }

                    //----------------------------------------------------
                    // Création du dossier parent
                    //----------------------------------------------------

                    if (isSpecialFolder)
                        continue;

                    Directory.CreateDirectory(
                        Path.GetDirectoryName(destinationFile));

                    //----------------------------------------------------
                    // Extraction
                    //----------------------------------------------------

                    entry.ExtractToFile(
                        destinationFile,
                        true);
                }
            }

            if (Directory.Exists(campaignPathNewName))
            {
                CloneHelper.UpdateCampInit(campaignPathNewName, oldNameCamp, newNameCamp);
                string fileCmdPath = Path.Combine(campaignMainPath, newNameCamp + ".cmp");
                CloneHelper.UpdateCmpFile(fileCmdPath, oldNameCamp, newNameCamp);
                //CloneHelper.RenameMissionFiles(campaignPathNewName, oldNameCamp, newNameCamp);

            }
            else
            {
                MessageBox.Show(
                    $"Installation failed: The campaign folder could not be found.\nPath: {campaignPathNewName}",
                    "Installation Error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error
                );
            }

        }

        // Installe une campagne téléchargée.
        // Pourquoi : réutiliser le système d'installation déjà présent dans DCE_Manager.
        //public async Task InstallCampaign(
        //    Form1 form,
        //    CampaignInfo campaign,
        //    string zipFile,
        //    DataGridView campaignGrid)
        //{
        //    if (!File.Exists(zipFile))
        //        throw new FileNotFoundException(zipFile);

        //    //FormUtils.LogRegister("Installing campaign : " + campaign.Name);

        //    form.labelCampaignDownload.Text = "Checking package...";

        //    CampaignPackageInfo package = ReadCampaignPackage(zipFile);

        //    if (package == null)
        //        throw new Exception("Invalid DCE campaign package.");

        //    //FormUtils.LogRegister("CampaignId : " + package.CampaignId);
        //    //FormUtils.LogRegister("Version    : " + package.Version);

        //    form.labelCampaignDownload.Text = "Installing...";

        //    form.ExtractZipFileToDirectoryLight(
        //        zipFile,
        //        true);

        //    //FormUtils.LogRegister("Campaign installed.");

        //    form.labelCampaignDownload.Text = "Refreshing...";
        //    //FormUtils.LogRegister("Refreshing...");

        //    //await RefreshCampaignUpdates( campaignGrid, form.textBox_SavedGames.Text);

        //    await form.LoadCampaignsAsync();

        //    form.labelCampaignDownload.Text = "Completed";
        //    //FormUtils.LogRegister("Completed");
        //}



    }
}
