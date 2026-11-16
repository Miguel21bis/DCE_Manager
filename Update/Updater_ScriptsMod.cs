using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;
using DCE_Manager;
using DCE_Manager.Parameters;
using DCE_Manager.Update;
using DCE_Manager.Utils;
using Newtonsoft.Json.Linq;


namespace DCE_Manager.Update
{

    public class Updater_ScriptsMod
    {
        private readonly Main_Form form;
        private readonly GithubHelper github;
        //private const string Repository = "DCE";

        public Updater_ScriptsMod(Main_Form form)
        {
            this.form = form;
            github = new GithubHelper();

        }


        // Lit la version locale du ScriptsMod depuis UTIL_Changelog.lua.
        // Pourquoi : cette version servira de référence pour comparer avec la dernière release GitHub.
        public string GetLocalScriptsModVersion()
        {
            try
            {
                string changelogFile = ParamConf.PATH_SavedGames_DCS +  @"\Mods\tech\DCE\ScriptsMod.NG\UTIL_Changelog.lua";

                if (!File.Exists(changelogFile))
                {
                    FormUtils.LogRegister("UTIL_Changelog.lua introuvable : " + changelogFile);

                    return "";
                }

                foreach (string line in File.ReadLines(changelogFile))
                {
                    FormUtils.LogRegister(" GetLocalScriptsModVersion() SCAN : " + line);

                    Match match = Regex.Match(
                        line,
                        @"versionDCE\[""UTIL_Changelog\.lua""\]\s*=\s*""([^""]+)""");

                    if (match.Success)
                    {
                        FormUtils.LogRegister(
                            "GetLocalScriptsModVersion() VERSION TROUVEE : " +
                            match.Groups[1].Value);

                        return match.Groups[1].Value;
                    }
                }
            }
            catch (Exception ex)
            {
                FormUtils.ErrorGeneral_BoxOrLog(
                    ex,
                    "GetLocalScriptsModVersion",
                    "",
                    false,
                    true);
            }

            return "";
        }


        // Télécharge puis installe automatiquement le dernier ScriptsMod.
        // Pourquoi : gérer proprement les erreurs et toujours remettre l'interface dans un état cohérent.
        public async Task DownloadLatestScriptsMod()
        {
            string scriptsModFolder =
                Path.Combine(
                    form.textBox_SavedGames.Text,
                    @"Mods\tech\DCE\ScriptsMod.NG");

            if (!IsScriptsModUnlocked(scriptsModFolder))
            {
                MessageBox.Show(
                    "ScriptsMod is currently in use.\r\n\r\n" +
                    "Please close DCS and any editor using ScriptsMod files.",
                    "ScriptsMod Update",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);

                return;
            }

            bool updateSucceeded = false;

            form.ScriptsModUpdateButton.Enabled = false;

            try
            {
                bool success = await github.GetLatestRelease(
                    GithubHelper.GithubAccount,
                    GithubHelper.Repository_ScriptsMod,
                    ".zip",
                    "DCE_scriptsMod_",
                    (version, asset, url) =>
                    {
                        ParamGithub.LastVersion = version;
                        ParamGithub.AssetName = asset;
                        ParamGithub.DownloadUrl = url;
                    });


                if (!success)
                {
                    OfferManualDownload(github.GetStatusMessage());
                    return;
                }

                form.ScriptsModStatusLabel.Text = "Downloading...";

                await Task.Delay(300);

                string destinationFile = Path.Combine(
                    Updater_Param.PathDownloadScriptsMod,
                    ParamGithub.AssetName);

                // Le dossier Downloads\ScriptsMod n'existe pas au tout premier lancement : on le crée
                Directory.CreateDirectory(Path.GetDirectoryName(destinationFile));

                using (HttpClient client = new HttpClient())
                {
                    client.DefaultRequestHeaders.Add(
                        "User-Agent",
                        "DCE_Manager");

                    using (HttpResponseMessage response =
                        await client.GetAsync(
                            ParamGithub.DownloadUrl,
                            HttpCompletionOption.ResponseHeadersRead))
                    {
                        response.EnsureSuccessStatusCode();

                        using (Stream contentStream =
                            await response.Content.ReadAsStreamAsync())
                        using (FileStream fileStream =
                            new FileStream(
                                destinationFile,
                                FileMode.Create,
                                FileAccess.Write,
                                FileShare.None))
                        {
                            byte[] buffer = new byte[81920];

                            int read;

                            while ((read =
                                await contentStream.ReadAsync(
                                    buffer,
                                    0,
                                    buffer.Length)) > 0)
                            {
                                await fileStream.WriteAsync(
                                    buffer,
                                    0,
                                    read);
                            }
                        }
                    }
                }

                form.ScriptsModStatusLabel.Text = "Download completed";

                await Task.Delay(400);

                form.ScriptsModStatusLabel.Text = "Installing...";

                await Task.Delay(300);

                await InstallLatestScriptsMod(destinationFile);

                form.ScriptsModStatusLabel.Text = "Checking installation...";

                await Task.Delay(300);

                await CheckGithubScriptsModVersionAsync();

                form.ScriptsModStatusLabel.Text = "Updated successfully";

                updateSucceeded = true;

                //UpdateUtils.RefreshUpdateTab(form);
            }
            catch (IOException ex)
            {
                form.ScriptsModStatusLabel.Text =
                    "Installation failed";

                form.ScriptsModUpdateButton.Enabled = true;

                MessageBox.Show(
                    ex.Message +
                    "\r\n\r\n" +
                    "Please close DCS, Mission Editor, Visual Studio Code, Notepad++, or Windows Explorer if it is opened in ScriptsMod.NG.",
                    "ScriptsMod Update",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);

                FormUtils.ErrorGeneral_BoxOrLog(
                    ex,
                    "DownloadLatestScriptsMod",
                    "",
                    false,
                    true);
            }
            catch (UnauthorizedAccessException ex)
            {
                form.ScriptsModStatusLabel.Text =
                    "Installation failed";

                form.ScriptsModUpdateButton.Enabled = true;

                MessageBox.Show(
                    "Access denied while installing ScriptsMod.\r\n\r\n" +
                    "This is usually caused by an antivirus temporarily locking the files, " +
                    "or a cloud sync tool (OneDrive) accessing the Saved Games folder.\r\n\r\n" +
                    "Please add an exception for your DCS Saved Games folder in your antivirus, " +
                    "or pause OneDrive sync, then try again.\r\n\r\n" +
                    "Technical detail: " + ex.Message,
                    "ScriptsMod Update - Access Denied",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);

                FormUtils.ErrorGeneral_BoxOrLog(
                    ex,
                    "DownloadLatestScriptsMod // UnauthorizedAccess",
                    "",
                    false,
                    true);
            }
            catch (Exception ex)
            {
                form.ScriptsModStatusLabel.Text = "Update failed";

                form.ScriptsModUpdateButton.Enabled = true;

                FormUtils.ErrorGeneral_BoxOrLog(
                    ex,
                    "DownloadLatestScriptsMod",
                    "",
                    true,
                    true);
            }
            finally
            {
                if (!updateSucceeded)
                {
                    form.ScriptsModUpdateButton.Enabled = true;
                }
            }
        }

        // Installe le ScriptsMod téléchargé.
        // Pourquoi : extraire directement dans un dossier définitif ".new",
        // puis ne faire que des bascules de dossier (rapides, peu sensibles à l'antivirus)
        // au lieu de recopier chaque fichier un par un.
        public async Task InstallLatestScriptsMod(string zipFile)
        {
            form.ScriptsModStatusLabel.Text = "Extracting package...";

            string destinationFolder =
                Path.Combine(
                    form.textBox_SavedGames.Text,
                    @"Mods\tech\DCE\ScriptsMod.NG");

            string newFolder = destinationFolder + ".new";

            if (Directory.Exists(newFolder))
            {
                Directory.Delete(newFolder, true);
            }

            ZipFile.ExtractToDirectory(zipFile, newFolder);

            foreach (string dir in Directory.GetDirectories(
                newFolder, "*", SearchOption.AllDirectories))
            {
                FormUtils.LogRegister("DIR : " + dir);
            }

            foreach (string file in Directory.GetFiles(
                newFolder, "*.*", SearchOption.AllDirectories))
            {
                FormUtils.LogRegister("FILE : " + file);
            }

            await Task.Delay(300);

            form.ScriptsModStatusLabel.Text = "Verifying package...";

            await Task.Delay(300);

            // Vérifie simplement que l'archive contient bien UTIL_Changelog.lua
            string changelog = Path.Combine(newFolder, "UTIL_Changelog.lua");

            if (!File.Exists(changelog))
            {
                Directory.Delete(newFolder, true);

                throw new Exception("Invalid ScriptsMod package.");
            }

            await ReplaceScriptsMod(newFolder);
        }

        // Bascule le dossier ScriptsMod vers la nouvelle version.
        // Pourquoi : un renommage de dossier est quasi-instantané et beaucoup moins
        // sensible aux verrous antivirus/OneDrive qu'une copie fichier par fichier.
        public async Task ReplaceScriptsMod(string newFolder)
        {
            form.ScriptsModStatusLabel.Text = "Installing...";

            string destinationFolder =
                Path.Combine(
                    form.textBox_SavedGames.Text,
                    @"Mods\tech\DCE\ScriptsMod.NG");

            string backupFolder = destinationFolder + ".old";

            // Nettoyage d'un éventuel ".old" orphelin laissé par une tentative précédente échouée
            if (Directory.Exists(backupFolder))
            {
                Directory.Delete(backupFolder, true);
            }

            // Étape 1 : ancien dossier -> .old (bascule rapide, avec tentatives en cas de verrou transitoire)
            if (Directory.Exists(destinationFolder))
            {
                Exception moveOldError =
                    await TryMoveWithRetryAsync(destinationFolder, backupFolder);

                if (moveOldError != null)
                {
                    // Rien n'a encore été touché côté destination : on nettoie juste le nouveau dossier extrait
                    if (Directory.Exists(newFolder))
                        Directory.Delete(newFolder, true);

                    throw new IOException(
                        "ScriptsMod is currently in use (or blocked by antivirus/cloud sync).",
                        moveOldError);
                }
            }

            // Étape 2 : nouveau dossier .new -> destination finale (bascule rapide également)
            Exception moveNewError =
                await TryMoveWithRetryAsync(newFolder, destinationFolder);

            if (moveNewError != null)
            {
                // Rollback : on restaure l'ancienne version si elle existe encore en backup
                if (Directory.Exists(backupFolder) && !Directory.Exists(destinationFolder))
                {
                    Directory.Move(backupFolder, destinationFolder);
                }

                throw new IOException(
                    "Could not finalize ScriptsMod installation (antivirus/cloud sync?).",
                    moveNewError);
            }

            await Task.Delay(300);

            form.ScriptsModStatusLabel.Text = "Checking installation...";

            await Task.Delay(300);

            string version = GetLocalScriptsModVersion();

            if (version == ParamGithub.LastVersion)
            {
                if (Directory.Exists(backupFolder))
                {
                    Directory.Delete(backupFolder, true);
                }

                form.ScriptsModStatusLabel.Text = "Updated successfully";

                await CheckGithubScriptsModVersionAsync();
            }
            else
            {
                if (Directory.Exists(destinationFolder))
                {
                    Directory.Delete(destinationFolder, true);
                }

                if (Directory.Exists(backupFolder))
                {
                    Directory.Move(backupFolder, destinationFolder);
                }

                form.ScriptsModStatusLabel.Text = "Installation failed";

                MessageBox.Show("Previous version restored.");
            }
        }

        // Tente un Directory.Move plusieurs fois avant d'abandonner.
        // Pourquoi : absorbe les verrous transitoires (antivirus, OneDrive) sans échouer immédiatement.
        // Retourne null en cas de succès, ou la dernière exception rencontrée en cas d'échec définitif.
        private async Task<Exception> TryMoveWithRetryAsync(
            string source, string destination, int maxAttempts = 3)
        {
            Exception lastError = null;

            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                try
                {
                    Directory.Move(source, destination);
                    return null; // succès
                }
                catch (IOException ex)
                {
                    lastError = ex;
                }
                catch (UnauthorizedAccessException ex)
                {
                    lastError = ex;
                }

                await Task.Delay(500);
            }

            return lastError;
        }

        //// Copie récursivement un dossier.
        //// Pourquoi : installer le nouveau ScriptsMod.
        //public void CopyDirectory(
        //    string source,if (string.IsNullOrWhiteSpace(localVersion))
        //    string destination)
        //{
        //    Directory.CreateDirectory(
        //        destination);

        //    foreach (string file in Directory.GetFiles(source))
        //    {
        //        File.Copy(
        //            file,
        //            Path.Combine(
        //                destination,
        //                Path.GetFileName(file)),
        //            true);
        //    }

        //    foreach (string dir in Directory.GetDirectories(source))
        //    {
        //        CopyDirectory(
        //            dir,
        //            Path.Combine(
        //                destination,
        //                Path.GetFileName(dir)));
        //    }
        //}

        // Vérifie si le dossier ScriptsMod peut être modifié.
        // Pourquoi : éviter de lancer une installation qui échouera immédiatement.
        public bool IsScriptsModUnlocked(string folder)
        {
            try
            {
                string testFile =
                    Path.Combine(folder, "UTIL_Changelog.lua");

                // Pas encore installé : rien à verrouiller (sinon la 1re installation était refusée à tort)
                if (!File.Exists(testFile))
                    return true;

                using (FileStream fs = new FileStream(
                    testFile,
                    FileMode.Open,
                    FileAccess.ReadWrite,
                    FileShare.None))
                {
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        // Téléchargement automatique impossible (pas de réseau, quota GitHub dépassé, aucune release...) :
        // on propose d'ouvrir la page de téléchargement, pour que l'utilisateur ne reste jamais bloqué.
        private void OfferManualDownload(string reason)
        {
            form.ScriptsModStatusLabel.Text = "Download failed";

            string targetFolder = Path.Combine(ParamConf.PATH_SavedGames_DCS, @"Mods\tech\DCE\ScriptsMod.NG");

            DialogResult answer = MessageBox.Show(
                "ScriptsMod could not be downloaded automatically." +
                (string.IsNullOrEmpty(reason) ? "" : "\r\n(" + reason + ")") +
                "\r\n\r\nOpen the download page in your browser?\r\n\r\n" +
                "Then unzip the ZIP file into this folder, so that UTIL_Changelog.lua is directly inside it:\r\n" +
                targetFolder,
                "ScriptsMod",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning);

            if (answer != DialogResult.Yes)
                return;

            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = $"https://github.com/{GithubHelper.GithubAccount}/{GithubHelper.Repository_ScriptsMod}/releases/latest",
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                FormUtils.LogRegister("OfferManualDownload : " + ex.Message);
            }
        }


        // Compare la version locale et GitHub et met à jour les labels.
        // Pourquoi : afficher immédiatement à l'utilisateur si une mise à jour existe.
        // ScriptsMod absent : on interroge quand même GitHub et on affiche le bouton Install
        // (avant, la méthode s'arrêtait avant et l'utilisateur n'avait aucun moyen de l'installer).
        public async Task CheckGithubScriptsModVersionAsync()
        {
            // Sans dossier Saved Games valide, on ne peut ni lire ni installer ScriptsMod : on guide l'utilisateur
            if (string.IsNullOrWhiteSpace(ParamConf.PATH_SavedGames_DCS))
            {
                form.ScriptModInstalledVersion.Text = "...";
                form.ScriptsModAvailableVersion.Text = "...";
                form.ScriptsModStatusLabel.Text = "Set the Saved Games folder first (Install tab)";
                form.pictureBox_ScriptsMod_Status.Image = Properties.Resources.icons8_warning_blue_30;
                form.ScriptsModUpdateButton.Visible = false;

                Updater_Param.ScriptsModUpdateAvailable = false;

                Utils_Update.RefreshUpdateTab(form);

                return;
            }

            string localVersion = GetLocalScriptsModVersion();
            bool notInstalled = string.IsNullOrWhiteSpace(localVersion);

            FormUtils.LogRegister("CheckGithubScriptsModVersionAsync A ScriptsMod Local Version : [" + localVersion + "]");

            bool success =
                 await github.GetLatestRelease(
                    GithubHelper.GithubAccount,
                    GithubHelper.Repository_ScriptsMod,
                    ".zip",
                    "DCE_scriptsMod",
                    (version, asset, url) =>
                    {
                        ParamGithub.LastVersion = version;
                        ParamGithub.AssetName = asset;
                        ParamGithub.DownloadUrl = url;
                    });

            if (!success)
            {
                form.ScriptsModStatusLabel.Text = notInstalled ? "Not installed" : github.GetStatusMessage();
                form.pictureBox_ScriptsMod_Status.Image = Properties.Resources.icons8_warning_blue_30;

                // ScriptsMod absent : le bouton reste là. Au clic, DownloadLatestScriptsMod propose la page de téléchargement.
                form.ScriptsModUpdateButton.Text = "Install";
                form.ScriptsModUpdateButton.Visible = notInstalled;

                Updater_Param.ScriptsModUpdateAvailable = notInstalled;

                Utils_Update.RefreshUpdateTab(form);

                return;
            }

            string githubVersion = ParamGithub.LastVersion;

            Updater_Param.LastGithubCheckUtc = DateTime.UtcNow;

            form.ScriptsModAvailableVersion.Text = githubVersion;

            FormUtils.LogRegister("CheckGithubScriptsModVersionAsync D ScriptsMod GitHub Version : " + githubVersion);

            // ---- ScriptsMod absent : on propose de l'installer ----
            if (notInstalled)
            {
                form.ScriptModInstalledVersion.Text = "...";
                form.ScriptsModStatusLabel.Text = "Not installed";
                form.pictureBox_ScriptsMod_Status.Image = Properties.Resources.icons8_warning_blue_30;

                form.ScriptsModUpdateButton.Text = "Install";
                form.ScriptsModUpdateButton.Visible = true;

                Updater_Param.ScriptsModUpdateAvailable = true;   // l'onglet Update affiche "(1)"

                Utils_Update.RefreshUpdateTab(form);

                return;
            }

            // ---- ScriptsMod installé : comparaison des versions ----
            form.ScriptModInstalledVersion.Text = localVersion;

            ParamConf.DCE_ScriptsMod_LocVer = localVersion;

            bool updateAvailable = Utils_Update.IsVersionNewer(githubVersion, localVersion);

            if (updateAvailable)
            {
                form.ScriptsModStatusLabel.Text = "Update available";
                form.pictureBox_ScriptsMod_Status.Image = Properties.Resources.icons8_download_24;
            }
            else
            {
                form.ScriptsModStatusLabel.Text = "Up to date";
                form.pictureBox_ScriptsMod_Status.Image = Properties.Resources.icons8_ok_24;
            }

            Updater_Param.ScriptsModUpdateAvailable = updateAvailable;

            form.ScriptsModUpdateButton.Text = "Update && Install";
            form.ScriptsModUpdateButton.Visible = updateAvailable;

            Utils_Update.RefreshUpdateTab(form);
        }




    }
}
