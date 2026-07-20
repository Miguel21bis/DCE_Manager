using System;
using DCE_Manager.Utils;
using DCE_Manager.Parameters;
using System.Windows.Forms;
using NLua;
using System.IO;
using Ookii.Dialogs.WinForms;
using System.IO.Compression;
using System.Diagnostics;

namespace DCE_Manager
{
    public partial class ASTI_Form : Form
    {
        public ASTI_Form()
        {
            InitializeComponent();

            // Enregistrement des événements sur les boutons du formulaire ASTI_Form
            //but_ASTI.Click += (sender, e) => but_ASTI_Click(this);
            but_ASTI_Browse_Template.Click += (sender, e) => but_ASTI_Browse_Template_Click(this);
            but_ASTI_Open_templateFolder.Click += (sender, e) => but_ASTI_Open_templateFolder_Click(this);
            but_ASTI_Browse_MissionFile.Click += (sender, e) => but_ASTI_Browse_Mission_Click(this);
            but_ASTI_Process.Click += (sender, e) => but_ASTI_Process_Click(this);
            but_GPS_LL.Click += (sender, e) => GPS_LL_Click(this);
        }

        // --- MÉTHODES STATIQUES DE TRAITEMENT (Toutes utilisent ASTI_Form) ---

        public static void but_ASTI_Click(ASTI_Form form)
        {
            form.textBox_ASTI_MissionFile.Text = ParamConf.AstiMissionFile;
            form.textBox_ASTI_importTemplateFolder.Text = ParamConf.AstiImportTemplateFolder;

            //form.groupBoxDroiteAccueil.Visible = false;
            form.groupBox_staticTemplate.Visible = true;

            if (!string.IsNullOrEmpty(form.textBox_ASTI_importTemplateFolder.Text))
            {
                form.but_ASTI_Open_templateFolder.Visible = true;
            }
            else
            {
                form.but_ASTI_Open_templateFolder.Visible = false;
            }
        }

        public static void but_ASTI_Browse_Template_Click(ASTI_Form form)
        {
            using (VistaFolderBrowserDialog folderBrowserDialog = new VistaFolderBrowserDialog())
            {
                if (!VistaFolderBrowserDialog.IsVistaFolderDialogSupported)
                {
                    MessageBox.Show("This feature is not supported on your version of Windows.");
                    return;
                }

                folderBrowserDialog.Description = "Select a folder";
                folderBrowserDialog.UseDescriptionForTitle = true;
                folderBrowserDialog.ShowNewFolderButton = true;

                string savedGamesPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments) + @"\Saved Games";
                if (Directory.Exists(savedGamesPath))
                {
                    folderBrowserDialog.SelectedPath = savedGamesPath;
                }

                if (folderBrowserDialog.ShowDialog() == DialogResult.OK)
                {
                    string folderPath = folderBrowserDialog.SelectedPath;

                    // On applique bien les modifications sur l'instance d'ASTI_Form passée en paramètre
                    form.textBox_ASTI_importTemplateFolder.Text = folderPath;
                    ParamConf.AstiImportTemplateFolder = folderPath;
                    form.but_ASTI_Open_templateFolder.Visible = true;
                }
            }
        }

        public static void but_ASTI_Browse_Mission_Click(ASTI_Form form)
        {
            using (OpenFileDialog openFileDialog = new OpenFileDialog())
            {
                openFileDialog.Title = "Select a .miz file";
                openFileDialog.InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments) + @"\Saved Games";
                openFileDialog.Filter = "Files .miz (*.miz)|*.miz";
                openFileDialog.FilterIndex = 1;
                openFileDialog.Multiselect = false;

                if (openFileDialog.ShowDialog() == DialogResult.OK)
                {
                    string filePath = openFileDialog.FileName;

                    // On applique bien les modifications sur l'instance d'ASTI_Form passée en paramètre
                    form.textBox_ASTI_MissionFile.Text = filePath;
                    ParamConf.AstiMissionFile = filePath;
                }
            }
        }

        public static void but_ASTI_Open_templateFolder_Click(ASTI_Form form)
        {
            // Votre code pour ouvrir le dossier template
            if (Directory.Exists(form.textBox_ASTI_importTemplateFolder.Text))
            {
                Process.Start("explorer.exe", form.textBox_ASTI_importTemplateFolder.Text);
            }
        }


        public static void GPS_LL_Click(ASTI_Form form)
        {
            // Votre code pour le clic GPS LL
        }




        // Fonction pour charger le template en fonction du nom
        static Lua LoadTemplateByName(string templateFilePath, string marqueurName, Lua lua)
        {
            Boolean findFile = false;
            string testFile = ParamConf.AstiImportTemplateFolder + @"\" + templateFilePath + ".stm";

            // Remplacer les doubles barres obliques inverses par une seule
            testFile = testFile.Replace(@"\\", @"\");

            if (File.Exists(testFile))
            {
                lua.DoFile(testFile);
                AddTemplateInMission(marqueurName, lua);
                findFile = true;
            }

            if (findFile == false)
            {
                testFile = ParamConf.AstiImportTemplateFolder + @"\" + templateFilePath + ".miz";
                // Remplacer les doubles barres obliques inverses par une seule
                testFile = testFile.Replace(@"\\", @"\");

                if (File.Exists(testFile))
                {
                    //lua.DoFile(templateFilePath);
                    //AddTemplateInMission(marqueurName, lua);
                    findFile = true;

                    try
                    {

                        string fileMissionAsTemplateStr = null;

                        // Ouvrir le fichier ZIP en lecture seule
                        using (FileStream zipFileStream = new FileStream(testFile, FileMode.Open, FileAccess.Read))
                        {
                            using (ZipArchive archive = new ZipArchive(zipFileStream, ZipArchiveMode.Read))
                            {
                                // Rechercher le fichier "mission" dans le fichier ZIP
                                ZipArchiveEntry fileMission = archive.GetEntry("mission");

                                if (fileMission != null)
                                {
                                    // Lire le contenu du fichier "mission"
                                    using (StreamReader reader = new StreamReader(fileMission.Open()))
                                    {
                                        fileMissionAsTemplateStr = reader.ReadToEnd();
                                    }

                                    // Remplacer le nom de la table de "mission =" à "staticTemplate ="
                                    fileMissionAsTemplateStr = fileMissionAsTemplateStr.Replace("mission =", "staticTemplate =");

                                    //*****************************dictionary***********************************
                                    // Rechercher le fichier "dictionary" dans le sous-répertoire "l10n/DEFAULT"
                                    ZipArchiveEntry dictionary = archive.GetEntry("l10n/DEFAULT/dictionary");

                                    if (dictionary != null)
                                    {
                                        //MessageBox.Show("passe A dictionary non nul");
                                        // Lire le contenu du fichier "fileMapResource"
                                        using (Stream entryStream = dictionary.Open())
                                        {
                                            using (StreamReader reader = new StreamReader(entryStream))
                                            {
                                                string dictionaryStr = reader.ReadToEnd();
                                                lua.DoString(dictionaryStr); // Charger le fichier Lua
                                            }
                                        }
                                    }

                                    lua.DoString(fileMissionAsTemplateStr);
                                    AddTemplateInMission(marqueurName, lua);
                                }
                                else
                                {
                                    //MessageBox.Show($"Le fichier '{testFile}' n'a pas été trouvé dans l'archive ZIP.", "Erreur");
                                    FormUtils.ShowErrorMessage($"The file ‘{testFile}’ was not found in the ZIP archive. ");
                                }
                            }
                        }

                    }
                    catch (Exception ex)
                    {
                        FormUtils.ErrorGeneral_BoxOrLog(ex, "LoadTemplateByName", testFile, true, true);
                    }
                }
            }

            if (findFile == false)
            {
                //MessageBox.Show($"Le fichier template '{templateFilePath}' n'existe pas.", "ERROR LoadTemplateByName");
                FormUtils.ShowErrorMessage($"The template file ‘{templateFilePath}’ does not exist.");
                //Console.WriteLine($"Le fichier template '{templateFilePath}' n'existe pas.");
            }
            return lua;
        }

        // Méthode de débogage C#
        public static void LuaPrint(string message)
        {
            MessageBox.Show(message, "Debug Lua");
        }

        // Fonction C# pour afficher un message avec OK/Cancel
        public static bool ShowConfirmationDialog(string message)
        {
            DialogResult result = MessageBox.Show(message, "Confirmation", MessageBoxButtons.OKCancel, MessageBoxIcon.Information);
            return result == DialogResult.OK;
        }



        static Tuple<string, string, string, string, Boolean> ReadMission(string fileMissionStr, Lua lua)
        {

            // Enregistrer la fonction de débogage C# depuis ASTI
            lua.RegisterFunction("print", typeof(ASTI_Form).GetMethod("LuaPrint", new Type[] { typeof(string) }));
            // Enregistrer la fonction de confirmation C#
            lua.RegisterFunction("ShowConfirmationDialog", typeof(ASTI_Form).GetMethod("ShowConfirmationDialog"));


            if (fileMissionStr != null)
            {

                lua.DoString(fileMissionStr); // Charger le fichier Lua Mission

                string luaFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Scripts", "lua_function.lua");
                lua.DoFile(luaFilePath);

                string luaTemplateFunctionFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Scripts", "lua_template.lua");
                lua.DoFile(luaTemplateFunctionFilePath);

                // Appeler la fonction Lua getReperesInMission
                LuaFunction getReperesInMission = lua.GetFunction("getReperesInMission");
                var result = getReperesInMission.Call();  // Appeler la fonction

                // Récupérer le premier résultat retourné (repereToCallTemplate)
                var reperesInMission = result[0] as LuaTable;

                // Vérifier si la table est récupérée avec succès
                if (reperesInMission != null)
                {
                    // Parcourir les entrées de la table Lua
                    foreach (var key in reperesInMission.Keys)
                    {
                        var entry = reperesInMission[key] as LuaTable;
                        if (entry != null)
                        {
                            // Récupérer les valeurs de chaque entrée
                            double x = (double)entry["x"];
                            double y = (double)entry["y"];
                            string templateName = entry["templateName"].ToString();
                            string sideName = entry["sideName"].ToString();
                            string marqueurName = entry["name"].ToString();
                            //int countryId = (int)entry["countryId"];

                            // Convertir en entier en toute sécurité
                            int countryId = Convert.ToInt32(entry["countryId"]);


                            //// Charger le template en fonction du nom du fichier
                            LoadTemplateByName(templateName, marqueurName, lua);
                        }
                    }
                }
                else
                {
                    FormUtils.ShowErrorMessage("repereToCallTemplate Vide ");
                }
            }

            // accéder aux données et fonctions Lua dans le script C#...
            var missionTable = lua["mission"] as LuaTable;
            var staticTemplateTable = lua["staticTemplate"] as LuaTable;
            var ASTI_refTable = lua["ASTI_ref"] as LuaTable;
            var templateGroupId_refId_Table = lua["templateGroupId_refId"] as LuaTable;
            var mapResourceTable = lua["mapResource"] as LuaTable;
            Boolean cancelVar = Convert.ToBoolean(lua["cancelVar"]);

            //var cancelVarTest = lua["cancelVar"] as LuaTable;
            //MessageBox.Show("cancelVar " + cancelVarTest.ToString(), "Info");
            //Boolean cancelVar = Convert.ToBoolean(cancelVarTest);

            string missionString = lua.GetFunction("tableToString").Call(missionTable)[0] as string;
            missionString = "mission = \r" + missionString;

            string ASTI_refString = lua.GetFunction("tableToString").Call(ASTI_refTable)[0] as string;
            ASTI_refString = "ASTI_ref = \r" + ASTI_refString;

            string templateGroupId_refId_String = lua.GetFunction("tableToString").Call(templateGroupId_refId_Table)[0] as string;
            templateGroupId_refId_String = "templateGroupId_refId = \r" + templateGroupId_refId_String;

            string mapResourceString = lua.GetFunction("tableToString").Call(mapResourceTable)[0] as string;
            mapResourceString = "mapResource = \r" + mapResourceString;

            // Retourner un Tuple contenant les deux chaînes
            return Tuple.Create(missionString, ASTI_refString, templateGroupId_refId_String, mapResourceString, cancelVar);

        }

        // Fonction AddTemplateInMission
        static Lua AddTemplateInMission(string marqueurName, Lua lua)
        {

            // Enregistrer la fonction de débogage C#
            lua.RegisterFunction("print", typeof(ASTI_Form).GetMethod("LuaPrint", new Type[] { typeof(string) }));

            // Appeler la fonction Lua getReperesInMission
            LuaFunction addTemplateInMission = lua.GetFunction("addTemplateInMission");
            addTemplateInMission.Call(marqueurName);  // Appeler la fonction

            return lua;

        }

        public static void but_ASTI_Process_Click(ASTI_Form form)
        {
            string zipFilePath = form.textBox_ASTI_MissionFile.Text;
            string fileNameToRead = "mission";
            string fileASTIRefPath = "l10n/DEFAULT/ASTI_ref"; // Chemin relatif dans le ZIP
            string fileMapResourcePath = "l10n/DEFAULT/mapResource";
            ZipArchiveEntry fileMission = null;
            ZipArchiveEntry fileASTIRef = null;
            ZipArchiveEntry fileMapResource = null;
            string fileMissionStr = null;
            string newMission = null;
            string fileASTIRefStr = null;
            string fileMapResourceStr = null;

            Lua lua = new Lua();

            //M*****************************************************************
            //load .miz et fichier mission
            //M*****************************************************************


            if (form.textBox_ASTI_MissionFile.Text != null && form.textBox_ASTI_importTemplateFolder.Text != "")
            {

                ParamConf.AstiMissionFile = form.textBox_ASTI_MissionFile.Text;

                ParamConf.AstiImportTemplateFolder = form.textBox_ASTI_importTemplateFolder.Text;

                try
                {
                    // Ouvrir le fichier ZIP en mode mise à jour
                    using (FileStream zipFileStream = new FileStream(zipFilePath, FileMode.Open, FileAccess.ReadWrite))
                    {
                        using (ZipArchive archive = new ZipArchive(zipFileStream, ZipArchiveMode.Update))
                        {
                            // Rechercher le fichier "mission" dans le fichier ZIP
                            foreach (var en in archive.Entries)
                            {
                                if (en.FullName.EndsWith(fileNameToRead, StringComparison.OrdinalIgnoreCase))
                                {
                                    fileMission = en;
                                    break;
                                }
                            }


                            //****************************ASTI_ref**************************************
                            // Rechercher le fichier "ASTI_ref" dans le sous-répertoire "l10n/DEFAULT"
                            fileASTIRef = archive.GetEntry(fileASTIRefPath);

                            if (fileASTIRef != null)
                            {
                                // Lire le contenu du fichier "ASTI_ref"
                                using (Stream entryStream = fileASTIRef.Open())
                                {
                                    using (StreamReader reader = new StreamReader(entryStream))
                                    {
                                        fileASTIRefStr = reader.ReadToEnd();
                                        lua.DoString(fileASTIRefStr); // Charger le fichier Lua
                                                                      //FormUtils.LogRegister("A fileASTIRefStr " + fileASTIRefStr);
                                    }
                                }
                            }
                            else
                            {
                                // Si le fichier n'existe pas, on le crée
                                fileASTIRefStr = ""; // Créer un contenu vide ou autre contenu par défaut

                                //FormUtils.ShowErrorMessage("A ASTI_ref non trouvé ");
                            }

                            //*****************************mapResource***********************************
                            // Rechercher le fichier "mapResource" dans le sous-répertoire "l10n/DEFAULT"
                            fileMapResource = archive.GetEntry(fileMapResourcePath);

                            if (fileMapResource != null)
                            {
                                // Lire le contenu du fichier "fileMapResource"
                                using (Stream entryStream = fileMapResource.Open())
                                {
                                    using (StreamReader reader = new StreamReader(entryStream))
                                    {
                                        fileMapResourceStr = reader.ReadToEnd();
                                        //MessageBox.Show("fileMapResourceStr " + fileMapResourceStr, "debug");

                                        lua.DoString(fileMapResourceStr); // Charger le fichier Lua
                                    }
                                }
                            }
                            else
                            {
                                foreach (var en in archive.Entries)
                                {
                                    // Afficher tous les noms des fichiers dans l'archive
                                    FormUtils.LogRegister("ZIP Entry: " + en.FullName);
                                }


                                FormUtils.LogRegister(fileMapResourcePath);
                                // Si le fichier n'existe pas
                                FormUtils.ShowErrorMessage("the MapResource file cannot be found in the .miz file "
                                    + "/r"
                                    + fileMapResourcePath.ToString());
                                //MessageBox.Show("the MapResource file cannot be found in the .miz file "
                                //    + "/r"
                                //    + fileMapResourcePath.ToString(), "Error");
                            }

                            if (fileMission != null)
                            {
                                // Lire le contenu du fichier "mission" dans le ZIP
                                using (Stream entryStream = fileMission.Open())
                                {
                                    using (StreamReader reader = new StreamReader(entryStream))
                                    {
                                        fileMissionStr = reader.ReadToEnd();
                                    }
                                }

                                // Appel de la méthode ReadMission
                                var result = ReadMission(fileMissionStr, lua);

                                if (result == null)
                                {
                                    MessageBox.Show("result est null", "Error");
                                    return;  // Sortir de la méthode si result est null
                                }

                                // Récupérer les valeurs
                                newMission = result.Item1;
                                string newASTI_ref = result.Item2;
                                string templateGroupId_refId = result.Item3;
                                string newMapResource = result.Item4;
                                Boolean cancelVar = result.Item5;

                                //FormUtils.LogRegister("C newASTI_ref " + newASTI_ref);

                                //MessageBox.Show("cancelVar "+cancelVar, "Info");
                                if (cancelVar == true)
                                {
                                    return;
                                }


                                // Supprimer l'entrée existante pour la mission
                                fileMission.Delete();

                                // Ajouter la nouvelle entrée "mission" avec les modifications
                                ZipArchiveEntry newEntry = archive.CreateEntry(fileNameToRead);
                                using (StreamWriter writer = new StreamWriter(newEntry.Open()))
                                {
                                    writer.Write(newMission);
                                }


                                // Supprimer l'entrée existante de "ASTI_ref"
                                if (fileASTIRef != null)
                                {
                                    fileASTIRef.Delete();  // Supprimer l'entrée ASTI_ref existante
                                }


                                // Créer et ajouter la nouvelle entrée "ASTI_ref"
                                ZipArchiveEntry newEntryASTI = archive.CreateEntry(fileASTIRefPath);
                                using (StreamWriter writer = new StreamWriter(newEntryASTI.Open()))
                                {
                                    //writer.Write(newASTI_ref);

                                    // Concaténer les deux tables
                                    string combinedString = newASTI_ref + templateGroupId_refId;

                                    // Écrire les deux tables concaténées dans le fichier
                                    writer.Write(combinedString);
                                }


                                // Supprimer l'entrée existante de "fileMapResource"
                                if (fileMapResource != null)
                                {
                                    fileMapResource.Delete();  // Supprimer l'entrée fileMapResource existante
                                }


                                // Créer et ajouter la nouvelle entrée "MapResource"
                                ZipArchiveEntry newEntryMapResource = archive.CreateEntry(fileMapResourcePath);
                                using (StreamWriter writer = new StreamWriter(newEntryMapResource.Open()))
                                {
                                    //FormUtils.LogRegister("D newASTI_ref " + newASTI_ref);
                                    writer.Write(newMapResource);
                                }



                            }
                            else
                            {
                                //MessageBox.Show($"Le fichier '{fileNameToRead}' n'a pas été trouvé dans l'archive ZIP.", "error 7606");
                                FormUtils.ShowErrorMessage($"The file ‘{fileNameToRead}’ was not found in the ZIP archive.");
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    FormUtils.ErrorGeneral_BoxOrLog(ex, "but_ASTI_Process_Click", zipFilePath, true, true);
                }

                if (newMission != null)
                {
                    MessageBox.Show("Process OK : ", "Info");
                }

            }

        }

        public static void but_ASTI_Open_templateFolder_Click(Main_Form forme)
        {

            // Remplacez "C:\Votre\Chemin\Vers\Le\Dossier" par le chemin réel de votre dossier
            string dossierAouvrir = ParamConf.AstiImportTemplateFolder;

            // Vérifiez si le dossier existe avant de l'ouvrir
            if (Directory.Exists(dossierAouvrir))
            {
                Process.Start(dossierAouvrir);
            }
            else
            {
                // Afficher un message d'erreur si le dossier n'existe pas
                MessageBox.Show("The specified folder does not exist.", "Erreur", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }

        }

        public static void GPS_LL_Click(Main_Form forme)
        {
            //Proj_NET.TestPositionLL(new string[] { "500000", "450000" });


            //MessageBox.Show("Zlib version2: " + version2, "");

            Proj_NET.TestProg(0, 0);
            Proj_NET.TestProg(-560000, 380000);

            Proj_NET.TestProg(1129937, 379982);

            Proj_NET.TestProg(1125256, -595066);

            Proj_NET.TestProg(-559999, -559999);


            //new string[] { "500000", "450000" };
            //
        }
    }

}
