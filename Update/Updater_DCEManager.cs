using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows.Forms;
using DCE_Manager;
using DCE_Manager.Parameters;
using DCE_Manager.Update;
using DCE_Manager.Utils;
using Newtonsoft.Json.Linq;


namespace DCE_Manager.Update
{

    public class Updater_DCEManager
    {
        private readonly Main_Form form;

        private readonly GithubHelper github;

        //private const string Repository = "DCE_Manager";

        public static string LastVersion = "";
        public static string DownloadUrl = "";
        public static string AssetName = "";

        public Updater_DCEManager(Main_Form form)
        {
            this.form = form;
            github = new GithubHelper();
        }



        // Lit les versions locale et GitHub de DCE_Manager.
        // Pourquoi : afficher si une mise à jour est disponible.
        public async Task CheckGithubDCEManagerVersionAsync()
        {
            string localVersion =
                form.GetVersionDceManager();

            bool success =
                await github.GetLatestRelease(
                    GithubHelper.GithubAccount,
                    GithubHelper.Repository_Manager,
                    //Repository,
                    ".exe",
                    "DCE_Manager_Setup_",
                    (version, asset, url) =>
                    {
                        LastVersion = version;
                        AssetName = asset;
                        DownloadUrl = url;
                    });

            if (!success)
            {
                form.DCEManagerStatusLabel.Text = github.GetStatusMessage();
                form.pictureBox_DCE_Manager_Status.Image = Properties.Resources.icons8_warning_blue_30;

                Utils_Update.RefreshUpdateTab(form);

                return;
            }

            form.DCEManagerInstalledVersion.Text = localVersion;

            form.DCEManagerAvailableVersion.Text = LastVersion;

            Updater_Param.LastGithubCheckUtc = DateTime.UtcNow;

            bool updateAvailable =
                Utils_Update.IsVersionNewer(
                    LastVersion,
                    localVersion);

            form.DCEManagerUpdateButton.Visible =
                updateAvailable;

            //UpdateUtils.RefreshUpdateTab(form);

            if (updateAvailable)
            {
                form.DCEManagerStatusLabel.Text = "Update available";
                form.pictureBox_DCE_Manager_Status.Image = Properties.Resources.icons8_download_24;
            }
            else
            {
                form.DCEManagerStatusLabel.Text = "Up to date";
                form.pictureBox_DCE_Manager_Status.Image = Properties.Resources.icons8_ok_24;
            }

            Updater_Param.DCEManagerUpdateAvailable =
                 updateAvailable;

            form.DCEManagerUpdateButton.Visible =
                updateAvailable;

            Utils_Update.RefreshUpdateTab(form);

        }


        // Télécharge le dernier Setup de DCE_Manager.
        // Pourquoi : préparer l'installation de la nouvelle version.
        public async Task DownloadLatestDCEManager()
        {
            //bool success =
            //    await github.GetLatestGithubManagerRelease();

            bool success = await github.GetLatestRelease(
                GithubHelper.GithubAccount,
                GithubHelper.Repository_Manager,
                ".exe",
                "DCE_Manager_Setup_",
                (version, asset, url) =>
                {
                    LastVersion = version;
                    AssetName = asset;
                    DownloadUrl = url;
                });

            if (!success)
                return;

            form.DCEManagerStatusLabel.Text =
                "Downloading...";

            string destinationFile = Path.Combine(
                Updater_Param.PathDownloadManager,
                AssetName);

            using (HttpClient client = new HttpClient())
            {
                client.DefaultRequestHeaders.Add(
                    "User-Agent",
                    "DCE_Manager");

                using (HttpResponseMessage response =
                    await client.GetAsync(
                        DownloadUrl,
                        HttpCompletionOption.ResponseHeadersRead))
                {
                    response.EnsureSuccessStatusCode();

                    using (Stream input =
                        await response.Content.ReadAsStreamAsync())

                    using (FileStream output =
                        new FileStream(
                            destinationFile,
                            FileMode.Create,
                            FileAccess.Write,
                            FileShare.None))
                    {
                        await input.CopyToAsync(output);
                    }
                }
            }

            Updater_Param.SetupDownloaded = true;
            Updater_Param.SetupFile = destinationFile;

            form.DCEManagerStatusLabel.Text =
             "Installing...";

            LaunchLatestDCEManager();

        }


        // Lance le Setup de DCE_Manager.
        // Pourquoi : terminer la mise à jour après téléchargement.
        public void LaunchLatestDCEManager()
        {
            form.DCEManagerStatusLabel.Text =
                "Launching installer...";

            System.Diagnostics.Process.Start(
                 new System.Diagnostics.ProcessStartInfo()
                 {
                     FileName = Updater_Param.SetupFile,
                     UseShellExecute = true
                 });

            Application.Exit();
        }
    }

}
