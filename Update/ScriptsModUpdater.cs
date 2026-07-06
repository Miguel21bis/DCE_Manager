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

    public class ScriptsModUpdater
    {
        private readonly Form1 form;
        private readonly GithubHelper github;
        private const string Repository = "DCE";

        public ScriptsModUpdater(Form1 form)
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
                string changelogFile =
                    form.textBox_SavedGames.Text +
                    @"\Mods\tech\DCE\ScriptsMod.NG\UTIL_Changelog.lua";

                if (!File.Exists(changelogFile))
                {
                    FormUtils.LogRegister(
                        "UTIL_Changelog.lua introuvable : " +
                        changelogFile);

                    return "";
                }

                foreach (string line in File.ReadLines(changelogFile))
                {
                    //FormUtils.LogRegister("SCAN : " + line);

                    Match match = Regex.Match(
                        line,
                        @"versionDCE\[""UTIL_Changelog\.lua""\]\s*=\s*""([^""]+)""");

                    if (match.Success)
                    {
                        //FormUtils.LogRegister(
                        //    "VERSION TROUVEE : " +
                        //    match.Groups[1].Value);

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
                // 1. On prépare le message textuel pour le log (sans la fonction dedans)
                string messageDeLog = $"[GitHub Request] Owner: Miguel21bis | Repo: {Repository} | Extension: .zip | Prefix: DCE_scriptsMod_";

                // 2. On appelle ta méthode de log avec ce string
                FormUtils.LogRegister(messageDeLog);

                bool success = await github.GetLatestRelease(
                    "Miguel21bis",
                    "DCE",
                    ".zip",
                    "DCE_scriptsMod_",
                    (version, asset, url) =>
                    {
                        ParamGithub.LastVersion = version;
                        ParamGithub.AssetName = asset;
                        ParamGithub.DownloadUrl = url;
                    });


                if (!success)
                    return;

                form.ScriptsModStatusLabel.Text =
                    "Downloading...";

                await Task.Delay(300);

                string destinationFile = Path.Combine(
                    ParamUpdater.PathDownloadScriptsMod,
                    ParamGithub.AssetName);


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

                form.ScriptsModStatusLabel.Text =
                    "Download completed";

                await Task.Delay(400);

                form.ScriptsModStatusLabel.Text =
                    "Installing...";

                await Task.Delay(300);

                await InstallLatestScriptsMod(destinationFile);

                form.ScriptsModStatusLabel.Text =
                    "Checking installation...";

                await Task.Delay(300);

                await CheckGithubScriptsModVersionAsync();

                form.ScriptsModStatusLabel.Text =
                    "Updated successfully";

                updateSucceeded = true;
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
            catch (Exception ex)
            {
                form.ScriptsModStatusLabel.Text =
                    "Update failed";

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
        // Pourquoi : remplacer automatiquement l'ancienne version.
        public async Task InstallLatestScriptsMod(string zipFile)
        {
            form.ScriptsModStatusLabel.Text =
                "Extracting package...";


            string tempFolder = Path.Combine(
                ParamUpdater.PathTemp,
                "ScriptsMod");

            if (Directory.Exists(tempFolder))
            {
                Directory.Delete(
                    tempFolder,
                    true);
            }

            Directory.CreateDirectory(tempFolder);

            ZipFile.ExtractToDirectory(
                zipFile,
                tempFolder);

            foreach (string dir in Directory.GetDirectories(
                tempFolder,
                "*",
                SearchOption.AllDirectories))
            {
                FormUtils.LogRegister("DIR : " + dir);
            }

            foreach (string file in Directory.GetFiles(
                tempFolder,
                "*.*",
                SearchOption.AllDirectories))
            {
                FormUtils.LogRegister("FILE : " + file);
            }

            await Task.Delay(300);

            //********************
            form.ScriptsModStatusLabel.Text = "Verifying package...";

            await Task.Delay(300);

            // Vérifie simplement que l'archive contient bien UTIL_Changelog.lua
            string changelog =
                Path.Combine(
                    tempFolder,
                    "UTIL_Changelog.lua");
            if (!File.Exists(changelog))
            {
                throw new Exception(
                    "Invalid ScriptsMod package.");
            }
            //if (!File.Exists(changelog))
            //{
            //    MessageBox.Show(
            //        "Invalid ScriptsMod package.");

            //    return;
            //}

            await ReplaceScriptsMod(tempFolder);


            Directory.Delete(
                tempFolder,
                true);
        }

        // Remplace le ScriptsMod installé.
        // Pourquoi : installer la nouvelle version en conservant un rollback.
        public async Task ReplaceScriptsMod(string newFolder)
        {
            form.ScriptsModStatusLabel.Text =
                "Installing...";

            string destinationFolder =
                Path.Combine(
                    form.textBox_SavedGames.Text,
                    @"Mods\tech\DCE\ScriptsMod.NG");

            string backupFolder =
                destinationFolder + ".old";

            if (Directory.Exists(backupFolder))
            {
                Directory.Delete(
                    backupFolder,
                    true);
            }

            if (Directory.Exists(destinationFolder))
            {
                try
                {
                    Directory.Move(
                        destinationFolder,
                        backupFolder);
                }
                catch (IOException ex)
                {
                    throw new IOException(
                        "ScriptsMod is currently in use.",
                        ex);
                }
                //catch (IOException)
                //{
                //    MessageBox.Show(
                //        "Unable to update ScriptsMod.\r\n\r\n" +
                //        "Some files are currently in use.\r\n\r\n" +
                //        "Please close:\r\n" +
                //        "- DCS World\r\n" +
                //        "- Mission Editor\r\n" +
                //        "- Lua editors\r\n" +
                //        "- Windows Explorer if it is open in ScriptsMod.NG\r\n\r\n" +
                //        "Then try again.",
                //        "ScriptsMod Update",
                //        MessageBoxButtons.OK,
                //        MessageBoxIcon.Warning);

                //    return;
                //}
            }

            CopyDirectory(
                newFolder,
                destinationFolder);

            await Task.Delay(300);

            form.ScriptsModStatusLabel.Text =
                "Checking installation...";

            await Task.Delay(300);

            string version =
                GetLocalScriptsModVersion();

            if (version == ParamGithub.LastVersion)
            {
                if (Directory.Exists(backupFolder))
                {
                    Directory.Delete(
                        backupFolder,
                        true);
                }

                form.ScriptsModStatusLabel.Text =
                    "Updated successfully";

                await CheckGithubScriptsModVersionAsync();
            }
            else
            {
                Directory.Delete(
                    destinationFolder,
                    true);

                Directory.Move(
                    backupFolder,
                    destinationFolder);

                form.ScriptsModStatusLabel.Text =
                    "Installation failed";

                MessageBox.Show(
                    "Previous version restored.");
            }
        }

        // Copie récursivement un dossier.
        // Pourquoi : installer le nouveau ScriptsMod.
        public void CopyDirectory(
            string source,
            string destination)
        {
            Directory.CreateDirectory(
                destination);

            foreach (string file in Directory.GetFiles(source))
            {
                File.Copy(
                    file,
                    Path.Combine(
                        destination,
                        Path.GetFileName(file)),
                    true);
            }

            foreach (string dir in Directory.GetDirectories(source))
            {
                CopyDirectory(
                    dir,
                    Path.Combine(
                        destination,
                        Path.GetFileName(dir)));
            }
        }

        // Vérifie si le dossier ScriptsMod peut être modifié.
        // Pourquoi : éviter de lancer une installation qui échouera immédiatement.
        public bool IsScriptsModUnlocked(string folder)
        {
            try
            {
                string testFile =
                    Path.Combine(folder, "UTIL_Changelog.lua");

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


        // Compare la version locale et GitHub et met à jour les labels.
        // Pourquoi : afficher immédiatement à l'utilisateur si une mise à jour existe.
        public async Task CheckGithubScriptsModVersionAsync()
        {
            string localVersion = GetLocalScriptsModVersion();

            if (string.IsNullOrWhiteSpace(localVersion))
            {
                form.ScriptsModStatusLabel.Text = "Not installed";
                form.pictureBox_ScriptsMod_Status.Image = Properties.Resources.icons8_warning_blue_30;

                return;
            }

            FormUtils.LogRegister("CheckGithubScriptsModVersionAsync ScriptsMod Local Version : " + localVersion);

            bool success =
                 await github.GetLatestRelease(
                     "Miguel21bis",
                    ParamGithub.Repository,
                    ".zip",
                    "DCE_scriptsMod_",
                    (version, asset, url) =>
                    {
                        ParamGithub.LastVersion = version;
                        ParamGithub.AssetName = asset;
                        ParamGithub.DownloadUrl = url;
                    });

            if (!success)
                return;

            string githubVersion =
                ParamGithub.LastVersion;

            //ScriptsModVersion.Text = localVersion;
            form.ScriptModInstalledVersion.Text = localVersion;

            form.ScriptsModAvailableVersion.Text = githubVersion;

            FormUtils.LogRegister("CheckGithubScriptsModVersionAsync ScriptsMod GitHub Version : " + githubVersion);


            bool updateAvailable = UpdateUtils.IsVersionNewer(githubVersion, localVersion);

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

            form.ScriptsModUpdateButton.Visible = updateAvailable;

            UpdateUtils.RefreshUpdateTab(form);


            ParamUpdater.ScriptsModUpdateAvailable =
                updateAvailable;

            form.ScriptsModUpdateButton.Visible =
                updateAvailable;

            UpdateUtils.RefreshUpdateTab(form);

        }




    }
}
