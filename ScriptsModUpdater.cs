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
using DCE_Manager.Utils;
using Newtonsoft.Json.Linq;


public class ScriptsModUpdater
{
    private readonly Form1 form;

    public ScriptsModUpdater(Form1 form)
    {
        this.form = form;

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

    // Lit la dernière version publiée sur GitHub.
    // Pourquoi : déterminer si une mise à jour ScriptsMod est disponible.
    public async Task<string> GetGithubScriptsModVersion()
    {
        try
        {
            using (HttpClient client = new HttpClient())
            {
                client.DefaultRequestHeaders.Add(
                    "User-Agent",
                    "DCE_Manager");

                string json =
                    await client.GetStringAsync(
                        "https://api.github.com/repos/Miguel21bis/DCE/releases/latest");

                JObject release = JObject.Parse(json);

                string tagName =
                    release["tag_name"]?.ToString();

                if (string.IsNullOrWhiteSpace(tagName))
                    return "";

                return tagName.TrimStart('v', 'V');
            }
        }
        catch (Exception ex)
        {
            FormUtils.ErrorGeneral_BoxOrLog(
                ex,
                "GetGithubScriptsModVersion",
                "",
                false,
                true);
        }

        return "";
    }

    // Lit la dernière release GitHub et récupère les informations du ZIP.
    // Pourquoi : préparer le téléchargement automatique du ScriptsMod.
    public async Task<bool> GetLatestGithubRelease()
    {
        try
        {
            using (HttpClient client = new HttpClient())
            {
                client.DefaultRequestHeaders.Add(
                    "User-Agent",
                    "DCE_Manager");

                string json =
                    await client.GetStringAsync(
                        "https://api.github.com/repos/Miguel21bis/DCE/releases/latest");

                JObject release = JObject.Parse(json);

                ParamGithub.LastVersion = "";
                ParamGithub.AssetName = "";
                ParamGithub.DownloadUrl = "";

                ParamGithub.LastVersion =
                    release["tag_name"]
                    ?.ToString()
                    .Replace("v", "");

                JArray assets =
                    (JArray)release["assets"];

                if (assets != null && assets.Count > 0)
                {
                    foreach (JToken asset in assets)
                    {
                        string assetName =
                            asset["name"]?.ToString();

                        if (!string.IsNullOrEmpty(assetName) &&
                            assetName.Contains("DCE_scriptsMod_") &&
                            assetName.EndsWith(".zip"))
                        {
                            ParamGithub.AssetName = assetName;

                            ParamGithub.DownloadUrl =
                                asset["browser_download_url"]?.ToString();

                            break;
                        }
                    }
                }

                if (string.IsNullOrWhiteSpace(ParamGithub.DownloadUrl))
                {
                    return false;
                }

                return true;
            }
        }
        catch (Exception ex)
        {
            FormUtils.ErrorGeneral_BoxOrLog(
                ex,
                "GetLatestGithubRelease",
                "",
                false,
                true);
        }

        return false;
    }

    public async Task<bool> GetLatestGithubManagerRelease()
    {
        try
        {
            using (HttpClient client = new HttpClient())
            {
                client.DefaultRequestHeaders.Add(
                    "User-Agent",
                    "DCE_Manager");

                string json =
                    await client.GetStringAsync(
                        "https://api.github.com/repos/Miguel21bis/DCE_Manager/releases/latest");

                JObject release = JObject.Parse(json);

                ParamGithubManager.LastVersion = "";
                ParamGithubManager.AssetName = "";
                ParamGithubManager.DownloadUrl = "";

                ParamGithubManager.LastVersion =
                    release["tag_name"]?
                    .ToString()
                    .Replace("v", "");

                JArray assets =
                    (JArray)release["assets"];

                foreach (JToken asset in assets)
                {
                    string name =
                        asset["name"]?.ToString();

                    if (!string.IsNullOrEmpty(name) &&
                        name.EndsWith(".exe"))
                    {
                        ParamGithubManager.AssetName = name;

                        ParamGithubManager.DownloadUrl =
                            asset["browser_download_url"]?.ToString();

                        return true;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            FormUtils.ErrorGeneral_BoxOrLog(
                ex,
                "GetLatestGithubManagerRelease",
                "",
                false,
                true);
        }

        return false;
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
            bool success =
                await GetLatestGithubRelease();

            if (!success)
                return;

            form.ScriptsModStatusLabel.Text =
                "Status : Downloading...";

            await Task.Delay(300);

            string destinationFile =
                Path.Combine(
                    ParamManager.pathManager,
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
                "Status : Download completed";

            await Task.Delay(400);

            form.ScriptsModStatusLabel.Text =
                "Status : Installing...";

            await Task.Delay(300);

            await InstallLatestScriptsMod(destinationFile);

            form.ScriptsModStatusLabel.Text =
                "Status : Checking installation...";

            await Task.Delay(300);

            await CheckGithubScriptsModVersionAsync();

            form.ScriptsModStatusLabel.Text =
                "Status : Updated successfully";

            updateSucceeded = true;
        }
        catch (IOException ex)
        {
            form.ScriptsModStatusLabel.Text =
                "Status : Installation failed";

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
                "Status : Update failed";

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
            "Status : Extracting package...";

        string tempFolder =
            Path.Combine(
                ParamManager.pathManager,
                "TempScriptsMod");

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
        form.ScriptsModStatusLabel.Text = "Status : Verifying package...";

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
            "Status : Installing...";

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
            "Status : Checking installation...";

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
                "Status : Updated successfully";

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
                "Status : Installation failed";

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



    // Compare deux versions DCE.
    // Pourquoi : déterminer si GitHub propose une version plus récente.
    public bool IsVersionNewer(string githubVersion, string localVersion)
    {
        try
        {
            Version github =
                new Version(githubVersion);

            Version local =
                new Version(localVersion);

            return github > local;
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
            form.ScriptsModStatusLabel.Text =
                "Status : Not installed";

            return;
        }

        FormUtils.LogRegister("CheckGithubScriptsModVersionAsync ScriptsMod Local Version : " + localVersion);

        string githubVersion = await GetGithubScriptsModVersion();

        //ScriptsModVersion.Text = localVersion;
        form.ScriptModInstalledVersion.Text = localVersion;

        form.ScriptsModAvailableVersion.Text = githubVersion;

        FormUtils.LogRegister("CheckGithubScriptsModVersionAsync ScriptsMod GitHub Version : " + githubVersion);


        bool updateAvailable = IsVersionNewer(githubVersion, localVersion);

        if (updateAvailable)
        {
            form.ScriptsModStatusLabel.Text =
                "Status : Update available";
        }
        else
        {
            form.ScriptsModStatusLabel.Text =
                "Status : Up to date";
        }

        form.ScriptsModUpdateButton.Visible = updateAvailable;

        ParamUpdate.NbUpdateAvailable = 0;

        if (IsVersionNewer(githubVersion, localVersion))
        {
            ParamUpdate.NbUpdateAvailable++;
        }

        if (ParamUpdate.NbUpdateAvailable > 0)
        {
            form.tabControl.TabPages[2].Text =
                "Update (" +
                ParamUpdate.NbUpdateAvailable +
                ")";
        }
        else
        {
            form.tabControl.TabPages[2].Text =
                "Update";
        }

    }



    void client_DownloadFileCompleted(object se, System.ComponentModel.AsyncCompletedEventArgs ea)
    {
        string contentState = "DownLoad Success!!!";

        if (ea.Error != null)
        {
            contentState = ea.Error.Message;
        }
        else
        {
            //CheckVersionScriptsModLocal();
        }
    }


    // Lit les versions locale et GitHub de DCE_Manager.
    // Pourquoi : afficher si une mise à jour est disponible.
    public async Task CheckGithubDCEManagerVersionAsync()
    {
        string localVersion =
            form.GetVersionDceManager();

        bool success =
            await GetLatestGithubManagerRelease();

        if (!success)
            return;

        form.DCEManagerInstalledVersion.Text =
            localVersion;

        form.DCEManagerAvailableVersion.Text =
            ParamGithubManager.LastVersion;

        bool updateAvailable =
            IsVersionNewer(
                ParamGithubManager.LastVersion,
                localVersion);

        form.DCEManagerUpdateButton.Visible =
            updateAvailable;

        if (updateAvailable)
        {
            form.DCEManagerStatusLabel.Text =
                "Status : Update available";
        }
        else
        {
            form.DCEManagerStatusLabel.Text =
                "Status : Up to date";
        }
    }

    // Télécharge le dernier Setup de DCE_Manager.
    // Pourquoi : préparer l'installation de la nouvelle version.
    public async Task DownloadLatestDCEManager()
    {
        bool success =
            await GetLatestGithubManagerRelease();

        if (!success)
            return;

        DialogResult result =
            MessageBox.Show(
                "A new version of DCE_Manager is available.\r\n\r\n" +
                "Current : " +
                form.GetVersionDceManager() +
                "\r\nAvailable : " +
                ParamGithubManager.LastVersion +
                "\r\n\r\nDownload and install now ?",
                "DCE_Manager Update",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);

        if (result != DialogResult.Yes)
            return;

        form.DCEManagerStatusLabel.Text =
            "Status : Downloading...";

        string destinationFile =
            Path.Combine(
                ParamManager.pathManager,
                ParamGithubManager.AssetName);

        using (HttpClient client = new HttpClient())
        {
            client.DefaultRequestHeaders.Add(
                "User-Agent",
                "DCE_Manager");

            using (HttpResponseMessage response =
                await client.GetAsync(
                    ParamGithubManager.DownloadUrl,
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

        form.DCEManagerStatusLabel.Text =
            "Status : Download completed";

        MessageBox.Show(
            "Download completed.\r\n\r\n" +
            destinationFile,
            "DCE_Manager");
    }


}
