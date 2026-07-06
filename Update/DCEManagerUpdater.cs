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

    public class DCEManagerUpdater
    {
        private readonly Form1 form;

        private readonly GithubHelper github;

        private const string Repository = "DCE_Manager";

        public static string LastVersion = "";
        public static string DownloadUrl = "";
        public static string AssetName = "";

        public DCEManagerUpdater(Form1 form)
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
                    "Miguel21bis",
                    Repository,
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

            form.DCEManagerInstalledVersion.Text =
                localVersion;

            form.DCEManagerAvailableVersion.Text =
                LastVersion;

            bool updateAvailable =
                UpdateUtils.IsVersionNewer(
                    LastVersion,
                    localVersion);

            form.DCEManagerUpdateButton.Visible =
                updateAvailable;

            UpdateUtils.RefreshUpdateTab(form);

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

            ParamUpdater.DCEManagerUpdateAvailable =
                 updateAvailable;

            form.DCEManagerUpdateButton.Visible =
                updateAvailable;

            UpdateUtils.RefreshUpdateTab(form);

        }


        // Télécharge le dernier Setup de DCE_Manager.
        // Pourquoi : préparer l'installation de la nouvelle version.
        public async Task DownloadLatestDCEManager()
        {
            //bool success =
            //    await github.GetLatestGithubManagerRelease();

            bool success = await github.GetLatestRelease(
                "Miguel21bis",
                Repository,
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
                ParamUpdater.PathDownloadManager,
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

            ParamUpdater.SetupDownloaded = true;
            ParamUpdater.SetupFile = destinationFile;

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
                     FileName = ParamUpdater.SetupFile,
                     UseShellExecute = true
                 });

            Application.Exit();
        }
    }

}
