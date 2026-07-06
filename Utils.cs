using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using DCE_Manager;
using DCE_Manager.Parameters;
using Microsoft.Win32;
using NLua;


namespace DCE_Manager.Utils
{

    // Extension method to update or add a key-value pair
    public static class DictionaryExtensions
    {
        public static void AddOrUpdate<TKey, TValue>(this Dictionary<TKey, TValue> dictionary, TKey key, TValue value)
        {
            if (dictionary.ContainsKey(key))
            {
                dictionary[key] = value;
            }
            else
            {
                dictionary.Add(key, value);
            }
        }
    }

    public static class FormUtils
    {

        public static async Task<bool> TéléchargerFichierAvecHttpClient(string url, string destinationPath)
        {
            // Vérifier si le lien est un lien Google Drive
            bool isGoogleDrive = url.Contains("drive.google.com");

            if (isGoogleDrive)
            {
                try
                {
                    // Lancer l'ouverture de la page Google Drive dans le navigateur pour le téléchargement manuel
                    FormUtils.LogRegister($"Opening the browser for manual download : {url}");
                    Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });

                    // Afficher un message à l'utilisateur
                    MessageBox.Show("The file requires a download confirmation from Google Drive. " +
                                    "A tab has opened in your browser. Click on 'Download anyway' " +
                                    " to download the file.", "Download confirmation required", MessageBoxButtons.OK, MessageBoxIcon.Information);

                    return true; // Considérer comme succès après avoir informé l'utilisateur
                }
                catch (Exception ex)
                {
                    FormUtils.ErrorGeneral_BoxOrLog(ex, "Error opening confirmation link in browser", url, true, true);
                    MessageBox.Show($"Error : Unable to open the download page.\n{ex.Message}", "Erreur", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return false;
                }
            }
            else
            {
                // Pour les autres serveurs, procéder au téléchargement direct
                using (HttpClient client = new HttpClient())
                {
                    try
                    {
                        client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64)");

                        FormUtils.LogRegister($"Attempt to download directly from {url} to {destinationPath}");

                        // Effectuer le téléchargement du fichier
                        using (HttpResponseMessage response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead))
                        {
                            response.EnsureSuccessStatusCode();

                            // Enregistrer le fichier
                            using (var fileStream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None))
                            {
                                await response.Content.CopyToAsync(fileStream);
                            }
                            FormUtils.LogRegister($"Successful download : {destinationPath}");
                        }
                        return true;
                    }
                    catch (HttpRequestException httpEx)
                    {
                        FormUtils.ErrorGeneral_BoxOrLog(httpEx, "HTTP error during direct download", url, true, true);
                        //MessageBox.Show($"Erreur de requête HTTP : {httpEx.Message}", "Erreur de téléchargement");
                        return false;
                    }
                    catch (Exception ex)
                    {
                        FormUtils.ErrorGeneral_BoxOrLog(ex, "General error during direct download", url, true, true);
                        //MessageBox.Show($"Erreur de téléchargement : {ex.Message}", "Erreur de téléchargement");
                        return false;
                    }
                }
            }
        }





        public static async Task<bool> TéléchargerFichierAvecHttpClient_OLD(string url, string destinationPath)
        {
            int nbErreurs = 0;
            using (HttpClient client = new HttpClient())
            {
                try
                {
                    // Configurer un User-Agent pour simuler un navigateur
                    client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64)");

                    FormUtils.LogRegister($"Tentative de téléchargement depuis {url} vers {destinationPath}");

                    // Effectuer le téléchargement du fichier
                    using (HttpResponseMessage response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead))
                    {
                        response.EnsureSuccessStatusCode();

                        // Enregistrer le fichier
                        using (var fileStream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None))
                        {
                            await response.Content.CopyToAsync(fileStream);
                        }
                        FormUtils.LogRegister($"Téléchargement réussi : {destinationPath}");
                    }
                }
                catch (HttpRequestException httpEx)
                {
                    FormUtils.ErrorGeneral_BoxOrLog(httpEx, "Erreur Http pendant le téléchargement", url, true, true);
                    MessageBox.Show($"Erreur de requête HTTP : {httpEx.Message}", "Erreur de téléchargement");
                    nbErreurs++;
                }
                catch (Exception ex)
                {
                    FormUtils.ErrorGeneral_BoxOrLog(ex, "Erreur générale pendant le téléchargement", url, true, true);
                    MessageBox.Show($"Erreur de téléchargement : {ex.Message}", "Erreur de téléchargement");
                    nbErreurs++;
                }
            }
            return nbErreurs == 0;
        }

        public static void ShowErrorMessage(string message, [System.Runtime.CompilerServices.CallerLineNumber] int lineNumber = 0)
        {
            MessageBox.Show($"Line error {lineNumber}: {message}", "Erreur");
        }

        public static void CommonFunction()
        {
            // Implémentation de la fonction commune
            MessageBox.Show("Fonction commune appelée !");
        }

        public static void ErrorGeneral_BoxOrLog(Exception ex, string txt, string arg, Boolean messageBox, Boolean writeInLog)
        {
            // Obtenir la version du programme
            string programVersion = Assembly.GetExecutingAssembly().GetName().Version.ToString();

            // Obtenir la pile d'appels complète (stack trace), mais filtrer pour inclure uniquement les fonctions de ton code
            var trace = new System.Diagnostics.StackTrace(ex, true);
            string customStackTrace = "";

            foreach (var frame in trace.GetFrames())
            {
                string method = frame.GetMethod().DeclaringType?.FullName ?? "Unknown Method";
                if (method != null && method.StartsWith("DCE_Manager")) // Filtrer pour n'afficher que les fonctions de ton projet
                {
                    int line = frame.GetFileLineNumber();
                    customStackTrace += $"{method} à la ligne {line}\r\n";
                }
            }

            // Obtenir le numéro de la ligne spécifique de l'erreur actuelle
            var lineNumber = trace.GetFrame(0)?.GetFileLineNumber() ?? 0;

            var moreInfo = "";
            if (arg != null)
                moreInfo = arg;

            // Construire le message d'erreur complet
            string errorDetails = $"message: {txt}\r\n" +
                                  $"Error: {ex.Message}\r\n" +
                                  $"StackTrace: {customStackTrace}\r\n" +
                                  $"Line: {lineNumber}\r\n" +
                                  //$"line to be added : {ligneModifiee}\r\n" +
                                  $"Additional information: {moreInfo}\r\n" +
                                  $"Version: {programVersion}";

            // Afficher le message synthétique pour l'utilisateur
            if(messageBox == true)
            { 
                MessageBox.Show($"An error has occurred. More details:\r\n\r\n{errorDetails}", "Error");
            }

            // Si besoin, tu peux également enregistrer l'erreur dans un fichier de log
            if (writeInLog == true);
            {
                LogRegister("An error has occurred. More details " + errorDetails);
            }

        }

        public static void CreateDCE_Folder()
        {
            // Appel à UpdateSharedData avant de récupérer les valeurs de SharedData
            Form1.Instance.UpdateSharedData();

            // Chemin de base
            string baseFolderPath = SharedData.textBox_SavedGames;

            // Structure des sous-dossiers à créer
            //Mods\tech\DCE\Missions\Campaigns
            string[] subFolders = { "Mods", "Mods\\tech", "Mods\\tech\\DCE", "Mods\\tech\\DCE\\Missions", "Mods\\tech\\DCE\\Missions\\Campaigns" };

            // Créer les dossiers et sous-dossiers
            foreach (string subFolder in subFolders)
            {
                string fullPath = Path.Combine(baseFolderPath, subFolder);

                FormUtils.CreatFolder(fullPath);
            }
        }



        public static void CreatFolder(string subFolderPath)
        {
            // Chemin des sous-dossiers à créer
            //string subFolderPath = @"C:\ExampleFolder\SubFolder1\SubFolder2";

            try
            {
                // Créer les sous-dossiers
                Directory.CreateDirectory(subFolderPath);

            }
            catch (UnauthorizedAccessException ex)
            {
                //MessageBox.Show(e.Message, "Error");
                ErrorGeneral_BoxOrLog(ex, "Error: Unauthorised access.", "", true, true);
            }
            catch (IOException ex)
            {
                //MessageBox.Show(e.Message, "Error");
                ErrorGeneral_BoxOrLog(ex, "Error: Input/output problem.", "", true, true);
            }
            catch (Exception ex)
            {
                //MessageBox.Show(e.Message, "Error");
                ErrorGeneral_BoxOrLog(ex, "Error: An error has occurred", "", true, true);
            }
        }

        private static readonly object lockObj = new object();

        public static void LogRegister(string log)
        {
            string pathOptionInstaller = System.Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments) + @"\DCE_Manager";
            bool exists = System.IO.Directory.Exists(pathOptionInstaller);

            // Obtenir la date et l'heure actuelles sous forme de chaîne
            string timestamp = DateTime.Now.ToString("[yyyy-MM-dd HH:mm:ss] ");

            // Ajouter le timestamp au début du log
            string logEntry = timestamp + log + "\r\n";

            if (!exists)
            {
                try
                {
                    System.IO.Directory.CreateDirectory(pathOptionInstaller);
                }
                catch (Exception ex)
                {
                    //MessageBox.Show($"Failed to create directory: {e.Message}", "Directory Error");
                    ErrorGeneral_BoxOrLog(ex, "LogRegister", "", true, false);
                    return;
                }
            }

            string path = pathOptionInstaller + @"\log.txt";

            lock (lockObj)
            {
                try
                {
                    if (!File.Exists(path))
                    {
                        // Créer le fichier s'il n'existe pas et écrire le log avec horodatage
                        using (FileStream fs = File.Open(path, FileMode.Create, FileAccess.Write, FileShare.ReadWrite))
                        {
                            Byte[] info = new UTF8Encoding(true).GetBytes(logEntry);
                            fs.Write(info, 0, info.Length);
                        }

                    }
                    else
                    {
                        // Append to the file
                        using (FileStream fs = File.Open(path, FileMode.OpenOrCreate, FileAccess.Write, FileShare.ReadWrite))
                        {
                            fs.Seek(0, SeekOrigin.End); // Move to the end of the file
                            Byte[] info = new UTF8Encoding(true).GetBytes(logEntry + "\r\n");
                            fs.Write(info, 0, info.Length);
                        }
                    }
                }
                catch (Exception ex)
                {
                    //// Obtenir les détails supplémentaires dans la pile (StackTrace)
                    //var stackTrace = new System.Diagnostics.StackTrace(ex, true);
                    //var frame = stackTrace.GetFrame(0);

                    //// Récupérer la ligne exacte et le fichier source (si disponibles)
                    //var lineNumber = frame?.GetFileLineNumber() ?? 0;
                    //var fileName = frame?.GetFileName() ?? "Unknown File";

                    //// Construire le message d'erreur
                    //string errorDetails = $"Error: {ex.Message}, StackTrace: {ex.StackTrace}, Line: {lineNumber}, File: {fileName}, Path: {path}";

                    //// Afficher un message d'erreur plus synthétique
                    //MessageBox.Show($"The file is locked or access is denied: {path}\n\nMore details: {errorDetails}", "Access error");
                    ErrorGeneral_BoxOrLog(ex, "The file is locked or access is denied", path, true, false );

                }
            }
        }


        public static void DeleteAllFilesInDirectory(string sourcePath, bool FolderDelete)
        {
            System.IO.DirectoryInfo di = new DirectoryInfo(sourcePath);

            foreach (FileInfo file in di.GetFiles())
            {
                file.Delete();
            }
            foreach (DirectoryInfo dir in di.GetDirectories())
            {
                dir.Delete(true);
            }
        }

        public static bool IsFileReady(string filename)
        {
            // Tente d'ouvrir le fichier en lecture/écriture pour vérifier s'il est prêt
            try
            {
                using (FileStream inputStream = File.Open(filename, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite))
                {
                    return inputStream.Length > 0;
                }
            }
            catch (Exception ex)
            {
                // Le fichier n'est pas prêt
                Console.WriteLine("File is not ready: " + ex.Message);
                return false;
            }
        }

        //public static bool IsFileLocked(IOException ex)
        //{
        //    int errorCode = System.Runtime.InteropServices.Marshal.GetHRForException(ex) & 0xFFFF;
        //    return errorCode == 32 || errorCode == 33; // ERROR_SHARING_VIOLATION or ERROR_LOCK_VIOLATION
        //}

        public static void addLigne(string StringTxt, string path, bool check) //addLigne
        {

            // Create the file, or overwrite if the file exists.
            try
            {
                //ajoute du texte
                //using (FileStream fs = File.Open(ParamManager.pathManager + "options.txt", FileMode.Append))
                //{
                //    Byte[] info = new UTF8Encoding(true).GetBytes(StringTxt + "\r\n");
                //    fs.Write(info, 0, StringTxt.Length);
                //    fs.Close();
                //}
                using (FileStream fs = File.Open(ParamManager.pathManager + "options.txt", FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
                {
                    Byte[] info = new UTF8Encoding(true).GetBytes(StringTxt + "\r\n");
                    fs.Write(info, 0, info.Length); // Corrige la taille de l'écriture
                }
            }
            catch (Exception ex)
            {
                ErrorGeneral_BoxOrLog(ex, "", path, true, true);

            }

        }
        public static void ModifierLigneByNumber(string path, int numberLine, string ligneModifiee)
        {
            int iLine = 1;
            int iLineModif = 0;
            string texteFinal = "";

            // Lecture du fichier avec StreamReader en utilisant un bloc using pour éviter de bloquer le fichier
            try
            {
                using (StreamReader sr = new StreamReader(File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)))
                {
                    string ligneEnCoursDeLecture;
                    while ((ligneEnCoursDeLecture = sr.ReadLine()) != null)
                    {
                        if (iLine == numberLine)
                        {
                            texteFinal += ligneModifiee + "\r\n";
                            iLineModif++;
                        }
                        else
                        {
                            texteFinal += ligneEnCoursDeLecture + "\r\n";
                        }
                        iLine++;
                    }
                }
            }
            catch (Exception ex)
            {
                FormUtils.LogRegister($"Erreur lors de la lecture du fichier : {ex.Message}\r\n");
                return;  // Sortir de la méthode si une erreur de lecture survient
            }

            // Ré-écriture du fichier
            try
            {
                using (StreamWriter sw = new StreamWriter(File.Open(path, FileMode.Truncate, FileAccess.Write, FileShare.ReadWrite)))
                {
                    sw.Write(texteFinal);
                }
                FormUtils.LogRegister($"LogRegister ModifierLigneByNumber() numberLine: {numberLine}, nbLigneModified: {iLineModif}\r\n");
                FormUtils.LogRegister($"LogRegister ModifierLigneByNumber() ligneModifiee: {ligneModifiee}\r\n");
            }
            catch (Exception ex)
            {
                string errorMsg = $"Exception File : {path}, Error : {ex.Message}";
                FormUtils.LogRegister($"LogRegister ERROR ModifierLigneByNumber() 201 {errorMsg}\r\n");
            }
        }

        public static void supprimerLigne(string path, string ligne)
        {
            // Path correspond au répertoire de ton fichier!
            string texte = null;
            string ligneActuelle = null;
            StreamReader sr = new StreamReader(path);
            // Ouverture du fichier
            while ((sr.Peek() != -1))
            {
                ligneActuelle = sr.ReadLine();
                if (!(ligneActuelle == ligne))
                {
                    texte += ligneActuelle + "\r\n";
                }
            }
            sr.Close();
            // Ré-écriture du fichier
            StreamWriter sr2 = new StreamWriter(path);
            sr2.Write(texte);
            sr2.Close();
        }

        public static int ModifierLigne(string path, string ligneRecherche, string ligneModifiee, int minor)
        {
            int Ufind = 0;
            string texteFinal = null;
            StreamReader sr = new StreamReader(path);
            string ligneEnCoursDeLecture = null;
            while ((sr.Peek() != -1))
            {
                bool changeTxt = false;
                ligneEnCoursDeLecture = sr.ReadLine();
                if (ligneEnCoursDeLecture.IndexOf(ligneRecherche) > -1 && ligneEnCoursDeLecture.IndexOf(ligneModifiee) <= -1)
                {
                    if (minor > 0)
                    {
                        string[] part = ligneEnCoursDeLecture.Split('=');
                        string[] version = ligneEnCoursDeLecture.Split('.');
                        if (version.Length >= 2)
                        {
                            int LocMinor = Int32.Parse(version[1]);
                            if (LocMinor >= minor)
                            { changeTxt = true; }
                        }
                        else
                        { changeTxt = true; }

                    }
                    else
                    { changeTxt = true; }
                }

                if (changeTxt)
                {
                    texteFinal = (texteFinal
                                + (ligneModifiee + "\r\n"));
                    Ufind++;
                }
                else
                {
                    texteFinal = (texteFinal
                                + (ligneEnCoursDeLecture + "\r\n"));
                }
            }
            sr.Close();
            // Ré-écriture du fichier
            StreamWriter sr2 = new StreamWriter(path);
            sr2.WriteLine(texteFinal);
            sr2.Close();

            return Ufind;
        }

        public static (int NbLignesModifiees, int NbErreurs) ModifierLigneBis(string path, string ligneRecherche, string ligneModifiee)
        {
            int nbLignesModifiees = 0;
            int nbErreurs = 0;
            string texteFinal = "";

            // Lecture du fichier avec un FileStream et partage en lecture
            try
            {
                using (FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (StreamReader sr = new StreamReader(fs))
                {
                    string ligneEnCoursDeLecture;
                    while ((ligneEnCoursDeLecture = sr.ReadLine()) != null)
                    {
                        if (ligneEnCoursDeLecture.Length >= 2 && !ligneEnCoursDeLecture.StartsWith("--") && ligneEnCoursDeLecture.Contains(ligneRecherche))
                        {
                            texteFinal += ligneModifiee + "\r\n";
                            nbLignesModifiees++;
                        }
                        else
                        {
                            texteFinal += ligneEnCoursDeLecture + "\r\n";
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                FormUtils.ErrorGeneral_BoxOrLog(ex, "Erreur lors de la lecture du fichier", path, false, true);
                nbErreurs++; // Incrémente le compteur d'erreurs en cas d'échec de lecture
            }

            // Ré-écriture du fichier avec FileStream et partage en écriture
            try
            {
                using (FileStream fs = new FileStream(path, FileMode.OpenOrCreate, FileAccess.Write, FileShare.ReadWrite))
                using (StreamWriter sr2 = new StreamWriter(fs))
                {
                    fs.SetLength(0);  // Efface le contenu précédent sans verrouiller le fichier
                    sr2.Write(texteFinal);
                }
            }
            catch (Exception ex)
            {
                FormUtils.ErrorGeneral_BoxOrLog(ex, "Erreur lors de la réécriture du fichier", path, false, true);
                nbErreurs++; // Incrémente le compteur d'erreurs en cas d'échec d'écriture
            }

            return (nbLignesModifiees, nbErreurs); // Retourne le nombre de lignes modifiées et d'erreurs
        }



        public static int ModifierLigneBis_OLD(string path, string ligneRecherche, string ligneModifiee)
        {
            int Ufind = 0;
            string texteFinal = "";

            // Lecture du fichier avec un FileStream et partage en lecture
            try
            {
                using (FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (StreamReader sr = new StreamReader(fs))
                {
                    string ligneEnCoursDeLecture;
                    while ((ligneEnCoursDeLecture = sr.ReadLine()) != null)
                    {
                        if (ligneEnCoursDeLecture.Length >= 2 && !ligneEnCoursDeLecture.StartsWith("--") && ligneEnCoursDeLecture.Contains(ligneRecherche))
                        {
                            texteFinal += ligneModifiee + "\r\n";
                            Ufind++;
                        }
                        else
                        {
                            texteFinal += ligneEnCoursDeLecture + "\r\n";
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                //FormUtils.LogRegister($"Erreur lors de la lecture du fichier: {ex.Message}\r\n");
                FormUtils.ErrorGeneral_BoxOrLog(ex, "ModifierLigneBis Erreur lors de la lecture du fichier", path, true, true);
                return Ufind;  // Retourne ce qui a été trouvé jusqu'à l'erreur
            }

            // Ré-écriture du fichier avec FileStream et partage en écriture
            try
            {
                using (FileStream fs = new FileStream(path, FileMode.Truncate, FileAccess.Write, FileShare.ReadWrite))
                using (StreamWriter sr2 = new StreamWriter(fs))
                {
                    sr2.Write(texteFinal);
                }
            }
            catch (Exception ex)
            {
                //FormUtils.LogRegister($"Erreur lors de la ré-écriture du fichier: {ex.Message}\r\n");
                FormUtils.ErrorGeneral_BoxOrLog(ex, "ModifierLigneBis Erreur lors de la ré-écriture du fichier", path, true, true);
            }

            return Ufind;
        }

        public static bool UpdateConfigFileFromDictionary()
        {
            bool result = false;
            string pathOptionInstaller = System.Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments) + @"\DCE_Manager";
            string filePath = pathOptionInstaller + @"\options.txt";

            // Vérifier et créer le fichier s'il n'existe pas
            if (!System.IO.File.Exists(filePath))
            {
                try
                {
                    System.IO.Directory.CreateDirectory(pathOptionInstaller);
                    using (System.IO.FileStream fs = System.IO.File.Create(filePath)) { }
                }
                catch (Exception e)
                {
                    ErrorGeneral_BoxOrLog(e, "Erreur lors de la création du fichier options.txt", filePath, true, true);
                    return false; // Retourner immédiatement false si la création échoue
                }
            }

            // Appel à UpdateSharedData avant de récupérer les valeurs de SharedData
            Form1.Instance.UpdateSharedData();

            // Mettre à jour les valeurs dans configDictionary
            ParamConf.configDictionary.AddOrUpdate("ASTI_MissionFile", SharedData.textBox_ASTI_MissionFile);
            ParamConf.configDictionary.AddOrUpdate("ASTI_importTemplateFolder", SharedData.textBox_ASTI_importTemplateFolder);
            ParamConf.configDictionary.AddOrUpdate("upgradeTxtDownload", ParamDownload.UpgradeTime);
            ParamConf.configDictionary.AddOrUpdate("LastNewsVersion", DceNews.LastNewsVersion);
            //ParamConf.configDictionary.AddOrUpdate("ServerNickNameSelected", ParamServ.ServerNickNameSelected);
            ParamConf.configDictionary.AddOrUpdate("NbLancement", ParamManager.NbLancement.ToString());
            ParamConf.configDictionary.AddOrUpdate("verScriptsMod", ParamScriptsMod.verScriptsMod);
            ParamConf.configDictionary["config_" + ParamConf.NumSelectConfig + "_"] = SharedData.comboBox_Config;
            ParamConf.configDictionary["config_" + ParamConf.NumSelectConfig + "_pathZipCampaign"] = SharedData.textBox_Campaign;
            ParamConf.configDictionary["config_" + ParamConf.NumSelectConfig + "_pathDCS"] = SharedData.textBox_DCS;
            ParamConf.configDictionary["config_" + ParamConf.NumSelectConfig + "_pathSavedGames"] = SharedData.textBox_SavedGames;
            ParamConf.configDictionary["config_" + ParamConf.NumSelectConfig + "_pathOVGME"] = SharedData.textBox_OvGME;

            // Copier les valeurs actuelles de configDictionary
            var configDictionary = new Dictionary<string, string>(ParamConf.configDictionary);

            try
            {
                // Lire et stocker le contenu actuel du fichier dans fileDictionary
                Dictionary<string, string> fileDictionary = new Dictionary<string, string>();

                if (File.Exists(filePath))
                {
                    string[] lines = File.ReadAllLines(filePath);
                    foreach (string line in lines)
                    {
                        if (!string.IsNullOrWhiteSpace(line) && !line.StartsWith("--") && line.Contains('='))
                        {
                            string[] parts = line.Split('=');
                            if (parts.Length == 2)
                            {
                                fileDictionary[parts[0].Trim()] = parts[1].Trim();
                            }
                        }
                    }

                    // Mettre à jour les valeurs du fichier avec les valeurs du dictionnaire configDictionary
                    foreach (var entry in configDictionary)
                    {
                        fileDictionary[entry.Key] = entry.Value;
                    }

                    // Écriture des données mises à jour dans le fichier
                    using (StreamWriter writer = new StreamWriter(filePath))
                    {
                        foreach (var entry in fileDictionary)
                        {
                            if (configDictionary.ContainsKey(entry.Key))
                            {
                                writer.WriteLine($"{entry.Key}={entry.Value}");
                            }
                        }
                    }
                    result = true; // Mise à jour réussie
                }
            }
            catch (Exception ex)
            {
                ErrorGeneral_BoxOrLog(ex, "Error updating the configuration file", filePath, true, true);
                result = false;
            }
            return result;
        }


        public static bool UpdateConfigFileFromDictionary_OLD()
        {
            Boolean result = false;
            string pathOptionInstaller = System.Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments) + @"\DCE_Manager";
            string filePath = pathOptionInstaller + @"\options.txt";

            if (!System.IO.File.Exists(filePath))
            {
                try
                {
                    // Assurez-vous que le répertoire existe avant de créer le fichier
                    System.IO.Directory.CreateDirectory(pathOptionInstaller);

                    // Créez le fichier si nécessaire et libérez le flux
                    using (System.IO.FileStream fs = System.IO.File.Create(filePath))
                    {
                        // Vous pouvez ajouter ici des données initiales si nécessaire
                    }
                }
                catch (Exception ex)
                {
                    ErrorGeneral_BoxOrLog(ex, "Erreur lors de la création du fichier options.txt", filePath, true, true);
                }
            }

            // Assurez-vous d'appeler UpdateSharedData avant d'utiliser SharedData
            Form1.Instance.UpdateSharedData();

            // Utilisation de la méthode générique pour mettre à jour ou ajouter les valeurs
            ParamConf.configDictionary.AddOrUpdate("ASTI_MissionFile", SharedData.textBox_ASTI_MissionFile);
            ParamConf.configDictionary.AddOrUpdate("ASTI_importTemplateFolder", SharedData.textBox_ASTI_importTemplateFolder);
            ParamConf.configDictionary.AddOrUpdate("upgradeTxtDownload", ParamDownload.UpgradeTime);
            ParamConf.configDictionary.AddOrUpdate("LastNewsVersion", DceNews.LastNewsVersion);
            //ParamConf.configDictionary.AddOrUpdate("ServerNickNameSelected", ParamServ.ServerNickNameSelected);
            ParamConf.configDictionary.AddOrUpdate("NbLancement", ParamManager.NbLancement.ToString());
            ParamConf.configDictionary.AddOrUpdate("verScriptsMod", ParamScriptsMod.verScriptsMod);

            ParamConf.configDictionary["config_" + ParamConf.NumSelectConfig + "_"] = SharedData.comboBox_Config;
            ParamConf.configDictionary["config_" + ParamConf.NumSelectConfig + "_pathZipCampaign"] = SharedData.textBox_Campaign;
            ParamConf.configDictionary["config_" + ParamConf.NumSelectConfig + "_pathDCS"] = SharedData.textBox_DCS;
            ParamConf.configDictionary["config_" + ParamConf.NumSelectConfig + "_pathSavedGames"] = SharedData.textBox_SavedGames;
            ParamConf.configDictionary["config_" + ParamConf.NumSelectConfig + "_pathOVGME"] = SharedData.textBox_OvGME;

    
            // Copie superficielle du dictionnaire
            var configDictionary = new Dictionary<string, string>(ParamConf.configDictionary);


            try
            {
                // Créer un dictionnaire temporaire pour stocker les lignes du fichier actuel
                Dictionary<string, string> fileDictionary = new Dictionary<string, string>();

                // Si le fichier existe, on le lit
                if (File.Exists(filePath))
                {
                    // Lire le fichier ligne par ligne et ajouter les paires clé/valeur dans fileDictionary
                    string[] lines = File.ReadAllLines(filePath);
                    foreach (string line in lines)
                    {
                        if (!string.IsNullOrWhiteSpace(line) && !line.StartsWith("--") && line.Contains('='))
                        {
                            string[] parts = line.Split('=');
                            if (parts.Length == 2)
                            {
                                string key = parts[0].Trim();
                                string value = parts[1].Trim();
                                fileDictionary[key] = value; // Ajouter ou mettre à jour la clé/valeur dans le dictionnaire temporaire
                            }
                        }
                    }

                    // Fusionner les valeurs du configDictionary dans fileDictionary (mise à jour des valeurs existantes)
                    foreach (var entry in configDictionary)
                    {
                        fileDictionary[entry.Key] = entry.Value; // Ajouter ou mettre à jour les clés/valeurs du dictionnaire passé en paramètre
                    }

                    // Réécrire le fichier en supprimant les éléments qui ne sont plus présents dans configDictionary
                    using (StreamWriter writer = new StreamWriter(filePath))
                    {
                        foreach (var entry in fileDictionary)
                        {
                            if (configDictionary.ContainsKey(entry.Key))
                            {
                                writer.WriteLine($"{entry.Key}={entry.Value}"); // Écrire uniquement les lignes qui sont dans configDictionary
                            }
                        }
                    }
                    result = true;
                }
            }
            catch (Exception ex)
            {
                ErrorGeneral_BoxOrLog(ex, "Error updating the config file", filePath, true, true);
                 result = false; 
            }
            return result;
        }



        public static int ModifLigneOrAdd(string path, string ligneRecherche, string ligneModifiee)
        {
            int Ufind = 0;
            string texteFinal = "";
            string retourLine = "";

            try
            {
                // Vérifier si le fichier existe, sinon le créer
                if (!File.Exists(path))
                {
                    File.Create(path).Dispose(); // Créer et libérer la ressource immédiatement
                }

                // Ouvrir le fichier en lecture avec FileShare.ReadWrite pour permettre à d'autres processus d'y accéder
                using (FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (StreamReader sr = new StreamReader(fs))
                {
                    string ligneEnCoursDeLecture = null;
                    while ((ligneEnCoursDeLecture = sr.ReadLine()) != null)
                    {
                        if (ligneEnCoursDeLecture.Length >= 2 &&
                            ligneEnCoursDeLecture.Substring(0, 2) != "--" &&
                            ligneEnCoursDeLecture.IndexOf(ligneRecherche) > -1)
                        {
                            texteFinal += ligneModifiee + Environment.NewLine;
                            Ufind++;
                        }
                        else
                        {
                            if (ligneEnCoursDeLecture.Length > 0)
                                retourLine = Environment.NewLine;

                            texteFinal += ligneEnCoursDeLecture + retourLine;
                        }
                    }
                }

                // Ré-écriture du fichier
                using (FileStream fsWrite = new FileStream(path, FileMode.Truncate, FileAccess.Write, FileShare.Read))
                using (StreamWriter sr2 = new StreamWriter(fsWrite))
                {
                    sr2.Write(texteFinal);
                }

                // Si aucune ligne n'a été modifiée, ajouter la nouvelle ligne
                if (Ufind == 0)
                {
                    addLigne(ligneModifiee, path, true);
                }
            }
            catch (Exception ex)
            {
                ErrorGeneral_BoxOrLog(ex, "", path, true, true);
                
                //LogRegister("Error in ModifLigneOrAdd: " + errorDetails);
            }

            return Ufind;
        }


        


        public static bool IsFileLocked(FileInfo file)
        {
            try
            {
                using (FileStream stream = file.Open(FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    stream.Close();
                }
            }
            catch (IOException)
            {
                //the file is unavailable because it is:
                //still being written to
                //or being processed by another thread
                //or does not exist (has already been processed)
                return true;
            }

            //file is not locked
            return false;
        }


        public static void LinkLabel_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            // L'URL à ouvrir
            string url = "http://www.exemple.com";

            // Ouvrir l'URL dans le navigateur web par défaut
            System.Diagnostics.Process.Start(url);
        }

        public static string IsApplicationInstalled(string p_name)
        {
            string displayName;
            string appliPath = "";
            RegistryKey key;

            // search in: CurrentUser
            key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");
            foreach (String keyName in key.GetSubKeyNames())
            {
                RegistryKey subkey = key.OpenSubKey(keyName);
                displayName = subkey.GetValue("DisplayName") as string;
                if (displayName != null && displayName.Contains(p_name))
                {
                    appliPath = subkey.GetValue("Inno Setup: App Path") as string;
                    return appliPath;
                }
            }

            // search in: LocalMachine_32
            key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");
            foreach (String keyName in key.GetSubKeyNames())
            {
                RegistryKey subkey = key.OpenSubKey(keyName);
                displayName = subkey.GetValue("DisplayName") as string;
                if (displayName != null)
                    //MessageBox.Show(displayName, "Passe01 LocalMachine_32");

                    if (displayName != null && displayName.Contains(p_name))
                    {
                        appliPath = subkey.GetValue("Inno Setup: App Path") as string;
                        return appliPath;
                    }
            }


            // search in: LocalMachine_64
            key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Wow6432Node\Microsoft\Windows\CurrentVersion\Uninstall");
            foreach (String keyName in key.GetSubKeyNames())
            {
                RegistryKey subkey = key.OpenSubKey(keyName);
                displayName = subkey.GetValue("DisplayName") as string;
                if (displayName != null && displayName.Contains(p_name))
                {
                    appliPath = subkey.GetValue("Inno Setup: App Path") as string;
                    return appliPath;
                }
            }

            // NOT FOUND
            return appliPath;
        }

        //compare les versions des fichiers

        //Dans cet exemple, version1.CompareTo(version2) retournera :
        //Explication du résultat de CompareTo :

        //Si result est inférieur à 0 : version1 est inférieure à version2.
        //Si result est égal à 0 : version1 est égale à version2.
        //Si result est supérieur à 0 : version1 est supérieure à version2.

        // Source: https://prograide.com/pregunta/76591/comparez-les-numeros-de-version-sans-utiliser-la-fonction-de-division
        public static bool CompareVersion(string v1, string v2)
        {
            //string v1 = "1.23.56.1487";
            //string v2 = "1.24.55.487";
            var version1 = new Version(v1);
            var version2 = new Version(v2);
            var result = version1.CompareTo(version2);

            if (result > 0)
                return true;
            else if (result <= 0)
                return false;
            else
                return false;
        }

        public static string ToTitleCase(string input)
        {

            if (string.IsNullOrEmpty(input))
                return input;

            if (input.IndexOf("InitNumber") > -1 || input.IndexOf("InitReserve") > -1)
            {
                return input;
            }

            // Mettre en minuscule le premier caractère et concaténer avec le reste de la chaîne
            return char.ToLower(input[0]) + input.Substring(1);

        }
        
        public static void ProcessEntries(
            object key,
            object value,
            char[] forbiddenChars,
            StringBuilder sb,
            int nbTab,
            string arg)
        {
            string accKey1 = "[\"";
            string accKey2 = "\"]";
            string accValue1 = "\"";
            string accValue2 = "\"";

            string valueKey = key.ToString();
            string valueName = value.ToString();

            string stringTab = "";

            for (int n = 1; n <= nbTab; n++)
            {
                stringTab += "\t";
            }

            if (valueKey.IndexOfAny(forbiddenChars) == -1)
            {
                accKey1 = "";
                accKey2 = "";
            }

            if (int.TryParse(valueKey, out int intKey))
            {
                accKey1 = "[";
                accKey2 = "]";
                valueKey = intKey.ToString();
            }

            if (int.TryParse(valueName, out int intValue))
            {
                accValue1 = "";
                accValue2 = "";

                sb.AppendLine(
                    $"{stringTab}{accKey1}{valueKey}{accKey2} = {accValue1}{intValue}{accValue2},");    //--{arg} -ProcessEntries int- 
            }
            else if (double.TryParse(valueName, out double doubleValue))
            {
                accValue1 = "";
                accValue2 = "";

                sb.AppendLine(
                    $"{stringTab}{accKey1}{valueKey}{accKey2} = {accValue1}{doubleValue}{accValue2},");
            }
            else if (bool.TryParse(valueName, out bool boolValue))
            {
                accValue1 = "";
                accValue2 = "";

                sb.AppendLine(
                    $"{stringTab}{accKey1}{valueKey}{accKey2} = {accValue1}{boolValue.ToString().ToLower()}{accValue2},");  //--{arg} -ProcessEntries bool- 
            }
            else if (value is string)
            {
                if (valueName.Length >= 1 && valueName[0] == '\"')
                {
                    accValue1 = "";
                    accValue2 = "";
                }

                if (valueKey.IndexOf("_") > -1)
                {
                    string[] word = valueKey.Split('_');

                    if (word.Length > 1 && int.TryParse(word[1], out int intKeyB))
                    {
                        accKey1 = "[";
                        accKey2 = "]";

                        sb.AppendLine(
                            $"{stringTab}{accKey1}{intKeyB}{accKey2} = {accValue1}{valueName}{accValue2},");    //-- {arg} -ProcessEntries stringToInt- 

                        return;
                    }
                }

                sb.AppendLine(
                     $"{stringTab}{accKey1}{valueKey}{accKey2} = {accValue1}{valueName}{accValue2},");    //--{arg} -ProcessEntries global string- 
            }
            else
            {
                sb.AppendLine(
                    $"{stringTab}{accKey1}{valueKey}{accKey2} = {accValue1}{valueName}{accValue2},");   //--{arg} -ProcessEntries global ELSE- 
            }
        }

        public static string ProcessEntries_OLD(object key, object value, char[] forbiddenChars, string texteFinal, int nbTab, string arg)
        {
            string accKey1 = "[\"";
            string accKey2 = "\"]";
            string accValue1 = "\"";
            string accValue2 = "\"";

            string valueKey = key.ToString();
            string valueName = value.ToString();

            string stringTab = "";

            for (int n = 1; n <= nbTab; n++)
            {
                stringTab = stringTab + "\t";
            }

            if (valueKey.IndexOfAny(forbiddenChars) == -1)
            {
                accKey1 = "";
                accKey2 = "";
            }

            if (int.TryParse(valueKey, out int intKey))
            {
                accKey1 = "[";
                accKey2 = "]";
                //intKey = intKey + 1;
                valueKey = intKey.ToString();
            }

            if (int.TryParse(value.ToString(), out int intValue))
            {
                accValue1 = "";
                accValue2 = "";
                texteFinal += $"{stringTab}{accKey1}{valueKey}{accKey2} = {accValue1}{intValue}{accValue2},--{arg} -ProcessEntries int- \r\n";
            }
            else if (double.TryParse(value.ToString(), out double doubleValue))
            {
                accValue1 = "";
                accValue2 = "";
                texteFinal += $"{stringTab}{accKey1}{valueKey}{accKey2} = {accValue1}{doubleValue}{accValue2},--{arg} -ProcessEntries double- \r\n";
            }
            else if (bool.TryParse(value.ToString(), out bool boolValue))
            {
                accValue1 = "";
                accValue2 = "";
                texteFinal += $"{stringTab}{accKey1}{valueKey}{accKey2} = {accValue1}{boolValue.ToString().ToLower()}{accValue2},--{arg} -ProcessEntries bool- \r\n";
            }
            else if (value is string)
            {
                if (valueName.Length >= 1)
                {
                    char testA = valueName[0];
                    if (testA == '\"')
                    {
                        accValue1 = "";
                        accValue2 = "";
                    }
                }

                if (valueKey.IndexOf("_") > -1)
                {
                    string[] word = valueKey.Split('_');
                    if (int.TryParse(word[1], out int intKeyB))
                    {
                        accKey1 = "[";
                        accKey2 = "]";
                        //intKeyB = intKeyB + 1;
                        texteFinal += $"{stringTab}{accKey1}{intKeyB}{accKey2} = {accValue1}{valueName}{accValue2},-- {arg} -ProcessEntries stringToInt- \r\n";
                        return texteFinal;
                    }
                }

                texteFinal += $"{stringTab}{accKey1}{valueKey}{accKey2} = {accValue1}{valueName}{accValue2},--{arg} -ProcessEntries global string- \r\n";
            }
            else
            {
                texteFinal += $"{stringTab}{accKey1}{valueKey}{accKey2} = {accValue1}{valueName}{accValue2},--{arg} -ProcessEntries global ELSE- \r\n";
            }

            return texteFinal;
        }

        ////*******************NEW Write Class *******************
        //public static void WriteListClassSquadsToFile(string path, string folderFile)
        //{
        //    // Définir la liste des mots interdits
        //    //var motInterdit = new HashSet<string> { "SideString", "FolderFile", "Helicopter", "IdSquad" };
        //    var motInterdit = new HashSet<string> { "SideString", "FolderFile", "Helicopter"};

        //    // Liste des caractères interdits
        //    char[] forbiddenChars = new char[] { ' ', '-' };

        //    //string texteFinal = "oob_air = " + "\r\n" +
        //    //                    "{ " + "\r\n"; ;
        //    var sb = new StringBuilder();

        //    sb.AppendLine("oob_air = ");
        //    sb.AppendLine("{");

        //    string accKey1 = "";
        //    string accKey2 = "";

        //    string accValue1 = "";
        //    string accValue2 = "";

        //    string arg = "";
        //    int nbTab = 0;

        //    accKey1 = "[\"";
        //    accKey2 = "\"]";
        //    accValue1 = "\"";
        //    accValue2 = "\"";

        //    // Itérer sur chaque camp
        //    foreach (var side in PublicTable.SideList)
        //    {
        //        sb.Append(
        //         "\t" + "[\"" + side + "\"] = " + "\r\n" +
        //           "\t" + "{" + "\r\n");

        //        if (List_oob_air_Manager.List_oob_air.Count == 0)
        //        {
        //            MessageBox.Show("Le fichier oob_air est vide, rien à sauvegarder.", "WriteChanedSquad Error A");
        //            FormUtils.LogRegister("Le fichier oob_air est vide, rien à sauvegarder.List_oob_air.Count:" + List_oob_air_Manager.List_oob_air.Count);

        //            return;
        //        }


        //        var filteredSquads = List_oob_air_Manager.List_oob_air
        //            .Where(squad => squad.FolderFile == folderFile && squad.SideString == side);

        //        // Itérer sur les squads du camp actuel
        //        foreach (var squad in filteredSquads)
        //        {

        //            sb.Append( "\t\t{" + "\r\n");

        //            // Obtenir le type de l'objet
        //            Type type = squad.GetType();

        //            // Obtenir toutes les propriétés de l'objet
        //            PropertyInfo[] properties = type.GetProperties();

        //            // Itérer sur chaque propriété Class Normal
        //            foreach (PropertyInfo property in properties)
        //            {
        //                accKey1 = "[\""; accKey2 = "\"]";
        //                accValue1 = "\""; accValue2 = "\"";

        //                if (property.Name != "baseAlternative")
        //                { }

        //                // Obtenir la valeur de la propriété
        //                var value = property.GetValue(squad, null);

        //                // Si le nom de la propriété est dans la liste des mots interdits, passer à la propriété suivante
        //                if (motInterdit.Contains(property.Name))
        //                {
        //                    continue;
        //                }
        //                if(folderFile == "Init" && (property.Name == "InitNumber" || property.Name == "InitReserve"))
        //                {
        //                    continue;
        //                }

        //                // Si la propriété est un Dictionary
        //                if (value is IDictionary dictionary && property.Name != "AdditionalProperties")
        //                {

        //                    string dictionaryName = property.Name;
        //                    dictionaryName = ToTitleCase(dictionaryName);
        //                    if (dictionaryName.IndexOfAny(forbiddenChars) == -1)
        //                    { accKey1 = ""; accKey2 = ""; }

        //                    sb.Append( "\t\t\t" + accKey1 + dictionaryName + accKey2 + " = " + "{" + "\r\n");

        //                    foreach (DictionaryEntry entry in dictionary)
        //                    {
        //                        accKey1 = "[\""; accKey2 = "\"]";

        //                        string keyName = entry.Key.ToString();
        //                        if (keyName.IndexOfAny(forbiddenChars) == -1)
        //                        { accKey1 = ""; accKey2 = ""; }

        //                        if (int.TryParse(entry.Value.ToString(), out int intValue))
        //                        {
        //                            accValue1 = ""; accValue2 = "";
        //                            sb.Append( "\t\t\t\t" + accKey1 + keyName + accKey2 + " = " + accValue1 + intValue + accValue2 + ",\r\n");
        //                        }
        //                        else if (double.TryParse(entry.Value.ToString(), out double doubleValue))
        //                        {
        //                            accValue1 = ""; accValue2 = "";
        //                            sb.Append( "\t\t\t\t" + accKey1 + keyName + accKey2 + " = " + accValue1 + doubleValue + accValue2 + ",\r\n");
        //                        }
        //                        else if (bool.TryParse(entry.Value.ToString(), out bool boolValue))
        //                        {
        //                            accValue1 = ""; accValue2 = "";
        //                            sb.Append( "\t\t\t\t" + accKey1 + keyName + accKey2 + " = " + accValue1 + boolValue.ToString().ToLower() + accValue2 + ",\r\n");
        //                        }

        //                    }
        //                    sb.Append( "\t\t\t}," + "\r\n");
        //                }

        //                // Si la propriété est une List
        //                else if (value is List<string> list)
        //                {
        //                    string listName = property.Name;
        //                    listName = ToTitleCase(listName);
        //                    if (listName.IndexOfAny(forbiddenChars) == -1)
        //                    { accKey1 = ""; accKey2 = ""; }

        //                    sb.Append( "\t\t\t" + accKey1 + listName + accKey2 + " = " + "{" + "\r\n");

        //                    int i = 1;
        //                    foreach (var item in list)
        //                    {
        //                        accValue1 = "\""; accValue2 = "\"";
        //                        accKey1 = "["; accKey2 = "]";
        //                        sb.Append( "\t\t\t\t" + accKey1 + i.ToString() + accKey2 + " = " + accValue1 + item + accValue2 + ",\r\n");
        //                        i++;
        //                    }

        //                    sb.Append( "\t\t\t}," + "\r\n");
        //                }
        //                else if (value != null && property.Name != "AdditionalProperties")
        //                {

        //                    string keyName = property.Name;
        //                    keyName = ToTitleCase(keyName);
        //                    string valueName = value.ToString();

        //                    if (keyName.IndexOfAny(forbiddenChars) == -1)
        //                    { accKey1 = ""; accKey2 = ""; }


        //                    if (int.TryParse(value.ToString(), out int intValue))
        //                    {
        //                        accValue1 = ""; accValue2 = "";
        //                        sb.Append( "\t\t\t" + accKey1 + keyName + accKey2 + " = " + accValue1 + intValue + accValue2 + ",\r\n");
        //                    }
        //                    else if (double.TryParse(value.ToString(), out double doubleValue))
        //                    {
        //                        accValue1 = ""; accValue2 = "";
        //                        sb.Append( "\t\t\t" + accKey1 + keyName + accKey2 + " = " + accValue1 + doubleValue + accValue2 + ",\r\n");
        //                    }
        //                    else if (bool.TryParse(value.ToString(), out bool boolValue))
        //                    {
        //                        accValue1 = ""; accValue2 = "";
        //                        sb.Append( "\t\t\t" + accKey1 + keyName + accKey2 + " = " + accValue1 + boolValue.ToString().ToLower() + accValue2 + ",\r\n");
        //                    }
        //                    else
        //                    {
        //                        sb.Append( "\t\t\t" + accKey1 + keyName + accKey2 + " = " + accValue1 + valueName + accValue2 + ",\r\n");
        //                    }
        //                }
        //            }

        //            // Itérer sur chaque propriété de AdditionalProperties
        //            if (squad.AdditionalProperties != null)
        //            {
        //                //texteFinal = texteFinal + "\t\t\t"  + " = " + "{--Y" + "\r\n";

        //                foreach (var addProp in squad.AdditionalProperties)
        //                {
        //                    accKey1 = "[\"";
        //                    accKey2 = "\"]";
        //                    accValue1 = "\"";
        //                    accValue2 = "\"";

        //                    if (addProp.Key.ToString().IndexOf("liveryModex") > -1)
        //                    { }

        //                    if (motInterdit.Contains(addProp.Key.ToString()))
        //                    {
        //                        continue;
        //                    }

        //                    //si la key n'est pas un objet/table
        //                    //ps: je n'arrive pas a detecter correctement que c'est un objet, donc je regarde si c'est un int string bool
        //                    if (
        //                        addProp.Value.GetType() == typeof(int) ||
        //                        addProp.Value.GetType() == typeof(string) ||
        //                        addProp.Value.GetType() == typeof(bool)
        //                        )
        //                    {
        //                        accKey1 = "[\""; accKey2 = "\"]";

        //                        string keyName = addProp.Key.ToString();
        //                        keyName = ToTitleCase(keyName);
        //                        string valueName = addProp.Value.ToString();

        //                        arg = "-addProp No Objet-";
        //                        nbTab = 3;
        //                        //texteFinal = ProcessEntries(keyName, valueName, forbiddenChars, texteFinal, nbTab, arg);
        //                        ProcessEntries(keyName, valueName, forbiddenChars, sb, nbTab, arg);

        //                    }
        //                    else
        //                    {
        //                        accKey1 = "[\""; accKey2 = "\"]";

        //                        string dictionaryName = addProp.Key;

        //                        if (dictionaryName.IndexOfAny(forbiddenChars) == -1)
        //                        { accKey1 = ""; accKey2 = ""; }

        //                        sb.Append( "\t\t\t" + accKey1 + dictionaryName + accKey2 + " = " + "{" + "\r\n");

        //                        if (addProp.Value is Dictionary<string, object> dict)
        //                        {

        //                            foreach (var dictEntry in dict)
        //                            {
        //                                //if (dictEntry.Value.ToString().IndexOf("AdA Chasse 1-2 EF") > -1  )
        //                                //{ }

        //                                int addA = 0;

        //                                if (dict.Keys.First() == "_0" || dict.Keys.First() == "0")
        //                                {
        //                                    addA = 1;
        //                                }

        //                                var keyA = dictEntry.Key;
        //                                keyA = keyA.Replace("_", "");

        //                                if (addA == 1 && int.TryParse(keyA.ToString(), out int intValueA))
        //                                {
        //                                    intValueA = intValueA + addA;
        //                                    keyA = intValueA.ToString();
        //                                }


        //                                if (dictEntry.Value is Dictionary<string, LuaObject> luaDict4)
        //                                {
        //                                    accKey1 = "[\""; accKey2 = "\"]";
        //                                    sb.Append( "\t\t\t\t" + accKey1 + dictEntry.Key + accKey2 + " = " + "{" + "\r\n");

        //                                    // Obtenir la première clé de luaDict4
        //                                    int addB = 0;
        //                                    if (luaDict4.Keys.First() == "0")
        //                                    {
        //                                        addB = 1;
        //                                    }
        //                                    foreach (var entry4 in luaDict4)
        //                                    {
        //                                        var keyB = entry4.Key;
        //                                        if (addB == 1 && int.TryParse(entry4.Key.ToString(), out int intValue))
        //                                        {
        //                                            intValue = intValue + addB;
        //                                            keyB = intValue.ToString();
        //                                        }

        //                                        //  if (entry4.Value.luaobj == "AdA Chasse 1-2 EF")
        //                                        //{ }
        //                                        arg = "-addProp Obj luaObj 5-";
        //                                        nbTab = 5;
        //                                        //texteFinal = ProcessEntries(keyB, entry4.Value.luaobj, forbiddenChars, texteFinal, nbTab, arg);
        //                                        ProcessEntries(keyB, entry4.Value.luaobj, forbiddenChars, sb, nbTab, arg);
        //                                    }

        //                                    sb.Append( "\t\t\t\t}," + "\r\n");
        //                                }
        //                                else
        //                                {

        //                                    arg = "-addProp Obj else 4-";
        //                                    nbTab = 4;
        //                                    //texteFinal = ProcessEntries(keyA, dictEntry.Value, forbiddenChars, texteFinal, nbTab, arg);
        //                                    ProcessEntries(keyA, dictEntry.Value, forbiddenChars, sb, nbTab, arg);
        //                                }
        //                            }
        //                        }
        //                        sb.Append("\t\t\t}," + "\r\n");

        //                    }
        //                }
        //            }

        //            sb.Append( "\t\t}," + "\r\n");
        //        }
        //        sb.Append( "\t}," + "\r\n");
        //    }

        //    sb.Append( "}" + "\r\n");

        //    //// Attendre que l'utilisateur appuie sur Entrée avant de fermer la console
        //    //Console.WriteLine("Appuyez sur Entrée pour fermer la console...");
        //    //Console.ReadLine();



        //    string texteFinal = sb.ToString();

        //    // Vérifier si la liste de squads est vide
        //    if (!List_oob_air_Manager.List_oob_air.Any())
        //    {
        //        MessageBox.Show("Aucune donnée détectée. Le fichier ne sera pas écrasé.", "WriteChanedSquad Error B");
        //        return;
        //    }

        //    // Vérifier si texteFinal contient au moins un bloc de squad
        //    if (!texteFinal.Contains("\t\t{"))
        //    {
        //        MessageBox.Show("Le fichier généré est vide. Écriture annulée.", "WriteChanedSquad Error C");
        //        return;
        //    }

        //    int nbOpen = texteFinal.Count(c => c == '{');
        //    int nbClose = texteFinal.Count(c => c == '}');

        //    FormUtils.LogRegister($"{{={nbOpen} }}={nbClose}");

        //    if (nbClose != nbOpen)
        //    {
        //        MessageBox.Show("Nb d'accolades {} différent.", "WriteChanedSquad Error D");
        //        return;
        //    }

        //    using (StreamWriter sr2 = new StreamWriter(path))
        //    {
        //        sr2.Write(texteFinal);
        //    }
        //}


        public static class LuaParser
        {
            // Fonction principale
            public static LuaObject ParseFile(string path, string tableName = null)
            {
                string luaCode = File.ReadAllText(path);

                using (var lua = new Lua())
                {
                    // 🔒 Sandbox
                    lua.DoString(@"
                    os = nil
                    io = nil
                    file = nil
                    debug = nil
                ");

                    try
                    {
                        lua.DoString(luaCode);
                    }
                    catch (Exception ex)
                    {
                        throw new Exception("Lua parse error in " + path + " : " + ex.Message);
                    }

                    object result = null;

                    // Si tableName fourni → on la récupère
                    if (!string.IsNullOrEmpty(tableName))
                    {
                        result = lua[tableName];
                    }
                    else
                    {
                        //// Sinon on prend la première table trouvée
                        //foreach (var key in lua.Globals.Keys)
                        //{
                        //    var val = lua[key];
                        //    if (val is LuaTable)
                        //    {
                        //        result = val;
                        //        break;
                        //    }
                        //}
                    }

                    if (result == null)
                        throw new Exception("No Lua table found in " + path);

                    return ConvertToLuaObject(result);
                }
            }

            // Conversion Lua → ton LuaObject
            private static LuaObject ConvertToLuaObject(object obj)
            {
                if (obj is LuaTable table)
                {
                    var dict = new Dictionary<string, LuaObject>();

                    foreach (var key in table.Keys)
                    {
                        string k = key.ToString();
                        dict[k] = ConvertToLuaObject(table[key]);
                    }

                    return new LuaObject(dict);
                }

                // Types simples
                if (obj is string || obj is double || obj is bool || obj == null)
                {
                    return new LuaObject(obj);
                }

                // fallback
                return new LuaObject(obj.ToString());
            }
        }

        public static string TableSerialization(LuaObject luaObj, int indentLevel)
        {
            StringBuilder text = new StringBuilder();
            string tab1 = new string('\t', indentLevel); // Indentation pour la ligne courante

            text.Append("\n" + tab1 + "{" + "\n"); // Ajouter un saut de ligne avant l'ouverture de la table

            string tab = new string('\t', indentLevel + 1); // Indentation pour les sous-lignes

            Dictionary<string, LuaObject> dict = luaObj.luaobj as Dictionary<string, LuaObject>;
            if (dict != null)
            {
                foreach (var entry in dict)
                {
                    string key = entry.Key;
                    LuaObject value = entry.Value;

                    // Sérialiser la clé
                    key = key.Replace("\n", "\\\n").Replace("\"", "\\\"").Replace("'", "\\\'");
                    int intTest;
                    if (int.TryParse(key, out intTest))
                    {
                        text.Append(tab + "[" + intTest + "] = ");
                    }
                    else
                    {
                        text.Append(tab + "[\"" + key + "\"] = ");
                    }
                    

                    // Sérialiser la valeur
                    if (value.luaobj is string stringValue)
                    {
                        stringValue = stringValue.Replace("\n", "\\\n").Replace("\"", "\\\"").Replace("'", "\\\'");

                        if(stringValue.IndexOf("true") > -1 | stringValue.IndexOf("false") > -1)
                        {
                            text.AppendLine( stringValue + ",");
                        }
                        else
                        {
                            text.AppendLine("\"" + stringValue + "\",");
                        }
                        
                    }
                    else if (value.luaobj is int intValue)
                    {
                        text.AppendLine(intValue + ",");
                    }
                    else if (value.luaobj is bool boolValue)
                    {
                        text.AppendLine(boolValue.ToString().ToLower() + ","); // Utiliser toLower() pour obtenir "true" ou "false"
                    }
                    else if (value.luaobj is double doubleValue)
                    {
                        text.AppendLine(doubleValue.ToString("G", System.Globalization.CultureInfo.InvariantCulture) + ",");
                    }
                    else if (value.luaobj is Dictionary<string, LuaObject>)
                    {
                        // Récursion pour les tables imbriquées
                        text.AppendLine(TableSerialization(value, indentLevel + 1));
                    }
                    else
                    {
                        text.AppendLine("nil,");
                    }
                }
            }

            bool rtb = false;
            tab = new string('\t', indentLevel); // Réduire l'indentation pour la fermeture
            if (indentLevel == 0)
            {
                text.Append(tab + "}"); // Dernière accolade sans virgule
            }
            else
            {
                text.Append(tab + "},"); // Les accolades imbriquées ont une virgule après
                rtb = true;
            }

            if (rtb != true)
                text.AppendLine(); // Retour à la ligne après la fermeture de la table

            return text.ToString();
        }

        public static void ConvertToBitmap(string campaignPath)
        {
            string bitmapPath = campaignPath + ".bmp";
            string pngPath = campaignPath + ".png";
            try
            {
                using (FileStream pngStream = new FileStream(pngPath, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    using (Image image = Image.FromStream(pngStream))
                    {
                        // Sauvegarder l'image en format BMP
                        image.Save(bitmapPath, System.Drawing.Imaging.ImageFormat.Bmp);
                    }
                }
            }
            catch (Exception ex)
            {
                FormUtils.ErrorGeneral_BoxOrLog(ex, "Erreur lors de la conversion de l'image en bitmap", pngPath, true, true);
            }
        }

        public static void MakeRoundedButton(Button button, int radius)
        {
            GraphicsPath path = new GraphicsPath();

            path.AddArc(0, 0, radius, radius, 180, 90);
            path.AddArc(button.Width - radius, 0, radius, radius, 270, 90);
            path.AddArc(button.Width - radius, button.Height - radius, radius, radius, 0, 90);
            path.AddArc(0, button.Height - radius, radius, radius, 90, 90);

            path.CloseFigure();

            button.Region = new Region(path);
        }

    }
}

