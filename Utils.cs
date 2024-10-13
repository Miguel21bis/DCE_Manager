using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Windows.Forms;
using DCE_Manager;
using DCE_Manager.Parameters;
using Microsoft.Win32;


// Utils.cs
namespace DCE_Manager.Utils
{
    public static class FormUtils
    {
        public static void ShowErrorMessage(string message, [System.Runtime.CompilerServices.CallerLineNumber] int lineNumber = 0)
        {
            MessageBox.Show($"Line error {lineNumber}: {message}", "Erreur");
        }

        public static void CommonFunction()
        {
            // Implémentation de la fonction commune
            MessageBox.Show("Fonction commune appelée !");
        }

        private static readonly object lockObj = new object();

        public static void LogRegister(string log)
        {
            string pathOptionInstaller = System.Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments) + @"\DCE_Manager";
            bool exists = System.IO.Directory.Exists(pathOptionInstaller);

            if (!exists)
            {
                try
                {
                    System.IO.Directory.CreateDirectory(pathOptionInstaller);
                }
                catch (Exception e)
                {
                    MessageBox.Show($"Failed to create directory: {e.Message}", "Directory Error");
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
                        // Create the file if it doesn't exist
                        using (FileStream fs = File.Open(path, FileMode.Create, FileAccess.Write, FileShare.None))
                        {
                            Byte[] info = new UTF8Encoding(true).GetBytes(log + "\r\n");
                            fs.Write(info, 0, info.Length);
                        }
                    }
                    else
                    {
                        // Append to the file
                        using (FileStream fs = File.Open(path, FileMode.OpenOrCreate, FileAccess.Write, FileShare.None))
                        {
                            fs.Seek(0, SeekOrigin.End); // Move to the end of the file
                            Byte[] info = new UTF8Encoding(true).GetBytes(log + "\r\n");
                            fs.Write(info, 0, info.Length);
                        }
                    }
                }
                catch (Exception ex)
                {
                    // Obtenir les détails supplémentaires dans la pile (StackTrace)
                    var stackTrace = new System.Diagnostics.StackTrace(ex, true);
                    var frame = stackTrace.GetFrame(0);

                    // Récupérer la ligne exacte et le fichier source (si disponibles)
                    var lineNumber = frame?.GetFileLineNumber() ?? 0;
                    var fileName = frame?.GetFileName() ?? "Unknown File";

                    // Construire le message d'erreur
                    string errorDetails = $"Error: {ex.Message}, StackTrace: {ex.StackTrace}, Line: {lineNumber}, File: {fileName}, Path: {path}";

                    // Afficher un message d'erreur plus synthétique
                    MessageBox.Show($"The file is locked or access is denied: {path}\n\nMore details: {errorDetails}", "Access error");

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
                using (FileStream inputStream = File.Open(filename, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
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

        public static void addLigne(string StringTxt, string path, bool check) //addLigne
        {
            //bool find = false;
            //if (check)
            //{
            //    string ligneRecherche = StringTxt;
            //    string ligneEnCoursDeLecture = null;
            //    StreamReader sr = new StreamReader(path);
            //    while ((sr.Peek() != -1))
            //    {
            //        ligneEnCoursDeLecture = sr.ReadLine();
            //        if (ligneEnCoursDeLecture.IndexOf(ligneRecherche) > -1)
            //        {
            //            find = true;
            //            return;
            //        }
            //    }
            //    sr.Close();
            //}

            //if (find)
            //    return;

            //bool fileExistPath = File.Exists(path);
            //if (!fileExistPath)
            //{
            //    return;
            //}

            // Create the file, or overwrite if the file exists.
            try
            {
                //ajoute du texte
                //FormUtils.LogRegister("LogRegister util 174 ---");
                //FormUtils.LogRegister(ParamManager.pathManager + @"options.txt");
                using (FileStream fs = File.Open(ParamManager.pathManager + "options.txt", FileMode.Append))
                {
                    Byte[] info = new UTF8Encoding(true).GetBytes(StringTxt + "\r\n");
                    fs.Write(info, 0, StringTxt.Length);
                    fs.Close();
                }
            }
            catch (Exception ex)
            {
                FormUtils.LogRegister("LogRegister UTIL 185 ---");
                FormUtils.LogRegister(path);
                FormUtils.LogRegister("---");
                FormUtils.LogRegister(StringTxt);
                FormUtils.LogRegister("---");
                FormUtils.LogRegister(ex.StackTrace);
                FormUtils.LogRegister("---");
                MessageBox.Show("Check Log", "Error");
            }

        }

        public static void ModifierLigneByNumber(string path, int numberLine, string ligneModifiee)
        {
            int iLine = 1;
            int iLineModif = 0;
            string retourLine = "";
            string texteFinal = null;
            StreamReader sr = new StreamReader(path);
            string ligneEnCoursDeLecture = null;
            while ((sr.Peek() != -1))
            {
                ligneEnCoursDeLecture = sr.ReadLine();
                if (iLine == numberLine)
                {
                    texteFinal = (texteFinal
                                + (ligneModifiee + "\r\n"));
                    iLineModif++;
                }
                else
                {
                    if (ligneEnCoursDeLecture.Length > 0)
                        retourLine = "\r\n";

                    texteFinal = (texteFinal
                                + (ligneEnCoursDeLecture + retourLine));
                }
                iLine++;
            }
            sr.Close();
            // Ré-écriture du fichier
            try
            {
                StreamWriter sr2 = new StreamWriter(path);
                sr2.WriteLine(texteFinal);
                sr2.Close();
                FormUtils.LogRegister("LogRegister ModifierLigneByNumber() numberLine: " + numberLine + "  nbLigneModified: " + iLineModif.ToString() + "\r\n");
                FormUtils.LogRegister("LogRegister ModifierLigneByNumber() ligneModifiee: " + ligneModifiee + "\r\n");
            }
            catch (Exception ex)
            {
                string errorMsg = string.Format("Exception File : {0}, Error : {1}", path, ex.Message);
                //MessageBox.Show(errorMsg.ToString(), "Error");
                //Console.WriteLine(errorMsg);
                FormUtils.LogRegister("LogRegister ERROR ModifierLigneByNumber() 1243 " + errorMsg + "\r\n");
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

        public static int ModifierLigneBis(string path, string ligneRecherche, string ligneModifiee)
        {
            int Ufind = 0;
            string retourLine = "";
            string texteFinal = null;
            StreamReader sr = new StreamReader(path);
            string ligneEnCoursDeLecture = null;
            while ((sr.Peek() != -1))
            {
                ligneEnCoursDeLecture = sr.ReadLine();
                if (ligneEnCoursDeLecture.Length >= 2 && ligneEnCoursDeLecture.Substring(0, 2) != "--" && ligneEnCoursDeLecture.IndexOf(ligneRecherche) > -1)     // && ligneEnCoursDeLecture.IndexOf(ligneModifiee) <= -1
                {
                    texteFinal = (texteFinal
                                + (ligneModifiee + "\r\n"));
                    Ufind++;
                }
                else
                {
                    if (ligneEnCoursDeLecture.Length > 0)
                        retourLine = "\r\n";

                    texteFinal = (texteFinal
                                + (ligneEnCoursDeLecture + retourLine));
                }
            }
            sr.Close();
            // Ré-écriture du fichier
            StreamWriter sr2 = new StreamWriter(path);
            sr2.WriteLine(texteFinal);
            sr2.Close();

            return Ufind;
        }

        public static int ModifLigneOrAdd(string path, string ligneRecherche, string ligneModifiee)
        {
            int Ufind = 0;
            string retourLine = "";
            string texteFinal = null;

            try
            {
                StreamReader sr = new StreamReader(path);
                string ligneEnCoursDeLecture = null;
                while ((sr.Peek() != -1))
                {
                    ligneEnCoursDeLecture = sr.ReadLine();
                    if (ligneEnCoursDeLecture.Length >= 2 && ligneEnCoursDeLecture.Substring(0, 2) != "--" && ligneEnCoursDeLecture.IndexOf(ligneRecherche) > -1)     // && ligneEnCoursDeLecture.IndexOf(ligneModifiee) <= -1
                    {
                        texteFinal = (texteFinal
                                    + (ligneModifiee + "\r\n"));
                        Ufind++;
                    }
                    else
                    {
                        if (ligneEnCoursDeLecture.Length > 0)
                            retourLine = "\r\n";

                        texteFinal = (texteFinal
                                    + (ligneEnCoursDeLecture + retourLine));
                    }
                }
                sr.Close();
                // Ré-écriture du fichier
                StreamWriter sr2 = new StreamWriter(path);
                sr2.WriteLine(texteFinal);
                sr2.Close();

                if (Ufind == 0)
                {
                    addLigne(ligneModifiee, path, true);
                    //addLigne(path, ligneModifiee, true);
                }
            }
            catch (Exception ex)
            {
            }


            return Ufind;

        }


        public static bool IsFileLocked(FileInfo file)
        {
            try
            {
                using (FileStream stream = file.Open(FileMode.Open, FileAccess.Read, FileShare.None))
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


        public static void CreatFolder(string subFolderPath)
        {
            // Chemin des sous-dossiers à créer
            //string subFolderPath = @"C:\ExampleFolder\SubFolder1\SubFolder2";

            try
            {
                // Créer les sous-dossiers
                Directory.CreateDirectory(subFolderPath);

            }
            catch (UnauthorizedAccessException e)
            {
                Console.WriteLine("Erreur : Accès non autorisé.");
                //Console.WriteLine(e.Message);
                MessageBox.Show(e.Message, "Error");
            }
            catch (IOException e)
            {
                Console.WriteLine("Erreur : Problème d'entrée/sortie.");
                //Console.WriteLine(e.Message);
                MessageBox.Show(e.Message, "Error");
            }
            catch (Exception e)
            {
                Console.WriteLine("Erreur : Une erreur est survenue.");
                //Console.WriteLine(e.Message);
                MessageBox.Show(e.Message, "Error");
            }
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

        public static string ProcessEntries(object key, object value, char[] forbiddenChars, string texteFinal, int nbTab, string arg)
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

        //*******************NEW Write Class *******************
        public static void WriteListClassSquadsToFile(string path, string folderFile)
        {
            // Définir la liste des mots interdits
            var motInterdit = new HashSet<string> { "SideString", "FolderFile", "Helicopter", "IdSquad" };

            // Liste des caractères interdits
            char[] forbiddenChars = new char[] { ' ', '-' };

            string texteFinal = "oob_air = " + "\r\n" +
                                "{ " + "\r\n"; ;

            string accKey1 = "";
            string accKey2 = "";

            string accValue1 = "";
            string accValue2 = "";

            string arg = "";
            int nbTab = 0;

            accKey1 = "[\"";
            accKey2 = "\"]";
            accValue1 = "\"";
            accValue2 = "\"";

            // Itérer sur chaque camp
            foreach (var side in PublicTable.SideList)
            {
                texteFinal = texteFinal +
                 "\t" + "[\"" + side + "\"] = " + "\r\n" +
                   "\t" + "{" + "\r\n";

                var filteredSquads = List_oob_air_Manager.List_oob_air
                    .Where(squad => squad.FolderFile == folderFile && squad.SideString == side);

                // Itérer sur les squads du camp actuel
                foreach (var squad in filteredSquads)
                {

                    texteFinal = texteFinal + "\t\t{" + "\r\n";

                    // Obtenir le type de l'objet
                    Type type = squad.GetType();

                    // Obtenir toutes les propriétés de l'objet
                    PropertyInfo[] properties = type.GetProperties();

                    // Itérer sur chaque propriété Class Normal
                    foreach (PropertyInfo property in properties)
                    {
                        accKey1 = "[\""; accKey2 = "\"]";
                        accValue1 = "\""; accValue2 = "\"";

                        if (property.Name != "baseAlternative")
                        { }

                        // Obtenir la valeur de la propriété
                        var value = property.GetValue(squad, null);

                        // Si le nom de la propriété est dans la liste des mots interdits, passer à la propriété suivante
                        if (motInterdit.Contains(property.Name))
                        {
                            continue;
                        }
                        if(folderFile == "Init" && (property.Name == "InitNumber" || property.Name == "InitReserve"))
                        {
                            continue;
                        }

                        // Si la propriété est un Dictionary
                        if (value is IDictionary dictionary && property.Name != "AdditionalProperties")
                        {

                            string dictionaryName = property.Name;
                            dictionaryName = ToTitleCase(dictionaryName);
                            if (dictionaryName.IndexOfAny(forbiddenChars) == -1)
                            { accKey1 = ""; accKey2 = ""; }

                            texteFinal = texteFinal + "\t\t\t" + accKey1 + dictionaryName + accKey2 + " = " + "{" + "\r\n";

                            foreach (DictionaryEntry entry in dictionary)
                            {
                                accKey1 = "[\""; accKey2 = "\"]";

                                string keyName = entry.Key.ToString();
                                if (keyName.IndexOfAny(forbiddenChars) == -1)
                                { accKey1 = ""; accKey2 = ""; }

                                if (int.TryParse(entry.Value.ToString(), out int intValue))
                                {
                                    accValue1 = ""; accValue2 = "";
                                    texteFinal = texteFinal + "\t\t\t\t" + accKey1 + keyName + accKey2 + " = " + accValue1 + intValue + accValue2 + ",\r\n";
                                }
                                else if (double.TryParse(entry.Value.ToString(), out double doubleValue))
                                {
                                    accValue1 = ""; accValue2 = "";
                                    texteFinal = texteFinal + "\t\t\t\t" + accKey1 + keyName + accKey2 + " = " + accValue1 + doubleValue + accValue2 + ",\r\n";
                                }
                                else if (bool.TryParse(entry.Value.ToString(), out bool boolValue))
                                {
                                    accValue1 = ""; accValue2 = "";
                                    texteFinal = texteFinal + "\t\t\t\t" + accKey1 + keyName + accKey2 + " = " + accValue1 + boolValue.ToString().ToLower() + accValue2 + ",\r\n";
                                }

                            }
                            texteFinal = texteFinal + "\t\t\t}," + "\r\n";
                        }

                        // Si la propriété est une List
                        else if (value is List<string> list)
                        {
                            string listName = property.Name;
                            listName = ToTitleCase(listName);
                            if (listName.IndexOfAny(forbiddenChars) == -1)
                            { accKey1 = ""; accKey2 = ""; }

                            texteFinal = texteFinal + "\t\t\t" + accKey1 + listName + accKey2 + " = " + "{" + "\r\n";

                            int i = 1;
                            foreach (var item in list)
                            {
                                accValue1 = "\""; accValue2 = "\"";
                                accKey1 = "["; accKey2 = "]";
                                texteFinal = texteFinal + "\t\t\t\t" + accKey1 + i.ToString() + accKey2 + " = " + accValue1 + item + accValue2 + ",\r\n";
                                i++;
                            }

                            texteFinal = texteFinal + "\t\t\t}," + "\r\n";
                        }
                        else if (value != null && property.Name != "AdditionalProperties")
                        {

                            string keyName = property.Name;
                            keyName = ToTitleCase(keyName);
                            string valueName = value.ToString();

                            if (keyName.IndexOfAny(forbiddenChars) == -1)
                            { accKey1 = ""; accKey2 = ""; }


                            if (int.TryParse(value.ToString(), out int intValue))
                            {
                                accValue1 = ""; accValue2 = "";
                                texteFinal = texteFinal + "\t\t\t" + accKey1 + keyName + accKey2 + " = " + accValue1 + intValue + accValue2 + ",\r\n";
                            }
                            else if (double.TryParse(value.ToString(), out double doubleValue))
                            {
                                accValue1 = ""; accValue2 = "";
                                texteFinal = texteFinal + "\t\t\t" + accKey1 + keyName + accKey2 + " = " + accValue1 + doubleValue + accValue2 + ",\r\n";
                            }
                            else if (bool.TryParse(value.ToString(), out bool boolValue))
                            {
                                accValue1 = ""; accValue2 = "";
                                texteFinal = texteFinal + "\t\t\t" + accKey1 + keyName + accKey2 + " = " + accValue1 + boolValue.ToString().ToLower() + accValue2 + ",\r\n";
                            }
                            else
                            {
                                texteFinal = texteFinal + "\t\t\t" + accKey1 + keyName + accKey2 + " = " + accValue1 + valueName + accValue2 + ",\r\n";
                            }
                        }
                    }

                    // Itérer sur chaque propriété de AdditionalProperties
                    if (squad.AdditionalProperties != null)
                    {
                        //texteFinal = texteFinal + "\t\t\t"  + " = " + "{--Y" + "\r\n";

                        foreach (var addProp in squad.AdditionalProperties)
                        {
                            accKey1 = "[\"";
                            accKey2 = "\"]";
                            accValue1 = "\"";
                            accValue2 = "\"";

                            if (addProp.Key.ToString().IndexOf("liveryModex") > -1)
                            { }

                            if (motInterdit.Contains(addProp.Key.ToString()))
                            {
                                continue;
                            }

                            //si la key n'est pas un objet/table
                            //ps: je n'arrive pas a detecter correctement que c'est un objet, donc je regarde si c'est un int string bool
                            if (
                                addProp.Value.GetType() == typeof(int) ||
                                addProp.Value.GetType() == typeof(string) ||
                                addProp.Value.GetType() == typeof(bool)
                                )
                            {
                                accKey1 = "[\""; accKey2 = "\"]";

                                string keyName = addProp.Key.ToString();
                                keyName = ToTitleCase(keyName);
                                string valueName = addProp.Value.ToString();

                                arg = "-addProp No Objet-";
                                nbTab = 3;
                                texteFinal = ProcessEntries(keyName, valueName, forbiddenChars, texteFinal, nbTab, arg);

                            }
                            else
                            {
                                accKey1 = "[\""; accKey2 = "\"]";

                                string dictionaryName = addProp.Key;

                                if (dictionaryName.IndexOfAny(forbiddenChars) == -1)
                                { accKey1 = ""; accKey2 = ""; }

                                texteFinal = texteFinal + "\t\t\t" + accKey1 + dictionaryName + accKey2 + " = " + "{" + "\r\n";

                                if (addProp.Value is Dictionary<string, object> dict)
                                {

                                    foreach (var dictEntry in dict)
                                    {
                                        //if (dictEntry.Value.ToString().IndexOf("AdA Chasse 1-2 EF") > -1  )
                                        //{ }

                                        int addA = 0;

                                        if (dict.Keys.First() == "_0" || dict.Keys.First() == "0")
                                        {
                                            addA = 1;
                                        }

                                        var keyA = dictEntry.Key;
                                        keyA = keyA.Replace("_", "");

                                        if (addA == 1 && int.TryParse(keyA.ToString(), out int intValueA))
                                        {
                                            intValueA = intValueA + addA;
                                            keyA = intValueA.ToString();
                                        }


                                        if (dictEntry.Value is Dictionary<string, LuaObject> luaDict4)
                                        {
                                            accKey1 = "[\""; accKey2 = "\"]";
                                            texteFinal = texteFinal + "\t\t\t\t" + accKey1 + dictEntry.Key + accKey2 + " = " + "{" + "\r\n";

                                            // Obtenir la première clé de luaDict4
                                            int addB = 0;
                                            if (luaDict4.Keys.First() == "0")
                                            {
                                                addB = 1;
                                            }
                                            foreach (var entry4 in luaDict4)
                                            {
                                                var keyB = entry4.Key;
                                                if (addB == 1 && int.TryParse(entry4.Key.ToString(), out int intValue))
                                                {
                                                    intValue = intValue + addB;
                                                    keyB = intValue.ToString();
                                                }

                                                //  if (entry4.Value.luaobj == "AdA Chasse 1-2 EF")
                                                //{ }
                                                arg = "-addProp Obj luaObj 5-";
                                                nbTab = 5;
                                                texteFinal = ProcessEntries(keyB, entry4.Value.luaobj, forbiddenChars, texteFinal, nbTab, arg);
                                            }

                                            texteFinal = texteFinal + "\t\t\t\t}," + "\r\n";
                                        }
                                        else
                                        {

                                            arg = "-addProp Obj else 4-";
                                            nbTab = 4;
                                            texteFinal = ProcessEntries(keyA, dictEntry.Value, forbiddenChars, texteFinal, nbTab, arg);
                                        }
                                    }
                                }
                                texteFinal = texteFinal + "\t\t\t}," + "\r\n";

                            }
                        }
                    }

                    texteFinal = texteFinal + "\t\t}," + "\r\n";
                }
                texteFinal = texteFinal + "\t}," + "\r\n";
            }


            // Attendre que l'utilisateur appuie sur Entrée avant de fermer la console
            Console.WriteLine("Appuyez sur Entrée pour fermer la console...");
            Console.ReadLine();

            //texteFinal = texteFinal + "\t}," + "\r\n";
            texteFinal = texteFinal + "}" + "\r\n";

            StreamWriter sr2 = new StreamWriter(path);
            sr2.WriteLine(texteFinal);
            sr2.Close();
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

    }


}

