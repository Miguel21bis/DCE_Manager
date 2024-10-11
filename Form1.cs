using System;
using System.IO;
using System.IO.Compression;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Diagnostics;



namespace WindowsFormsDCE
{

    public partial class Form1 : Form
    {
        public void ExtractZipFileToDirectory(string sourceZipFilePath, bool overwrite)
        {
            using (var archive = ZipFile.Open(sourceZipFilePath, ZipArchiveMode.Read))
            {
                string destinationDirectoryName = "";
                string DestFullName = "";

                foreach (ZipArchiveEntry file in archive.Entries)
                {
                    DestFullName = file.FullName;

                    string[] words = DestFullName.Split('/');
                    string LowerWordZero = words[0].ToLowerInvariant();

                    bool extractAutorise = true;



                    string testZipFilePath = sourceZipFilePath.Replace("_", " ");
                    // s'il existe un repertoir supplémentaire comprenant le nom exacte de la campagne 
                    if (System.Text.RegularExpressions.Regex.IsMatch(testZipFilePath, words[0]))
                    {
                        MessageBox.Show("Incompatible ZIP file structure.", "Error");
                        Close();
                    }

                    //MessageBox.Show(words.Length + " " + DestFullName + " ||words[0]|| " + words[0], file.Name);
                    if (words.Length <= 2 && (Path.GetExtension(words[0]) == ".pdf" || Path.GetExtension(words[0]) == ".exe" || Path.GetExtension(words[0]) == ".txt"))
                    {
                        //MessageBox.Show("Passe" + DestFullName, file.Name);
                        continue;
                    }
                    if (
                        !LowerWordZero.Contains("mod") & !words[0].Contains("tech")
                        & !(System.Text.RegularExpressions.Regex.IsMatch(LowerWordZero, "savedgame")) & !(System.Text.RegularExpressions.Regex.IsMatch(LowerWordZero, "ovgme"))
                        )
                    {
                        destinationDirectoryName = textBox_SavedGames.Text + @"\Liveries";
                    }
                    if (words[0].Contains("tech"))
                    {
                        destinationDirectoryName = textBox_SavedGames.Text + @"\Mods";
                    }
                    if (words[0].Contains("DCE_Missionscript_mod") || words[0].Contains("MOD"))
                    {
                        destinationDirectoryName = textBox_OvGME.Text;
                        //DestFullName = DestFullName.Replace("DCE_Missionscript_mod/", "");
                        //MessageBox.Show(DestFullName);
                    }

                    if (System.Text.RegularExpressions.Regex.IsMatch(LowerWordZero, "ovgme"))
                    {
                        DestFullName = DestFullName.Replace(words[0]+"/", "");
                        destinationDirectoryName = textBox_OvGME.Text;  
                    }
                    //if (LowerWordZero.Contains("dcs_savedgames_path"))
                    if (System.Text.RegularExpressions.Regex.IsMatch(LowerWordZero, "savedgame"))
                    {


                        DestFullName = DestFullName.Replace(words[0] + "/", "");
                        destinationDirectoryName = textBox_SavedGames.Text;

                        //regarde s'il est interdit d'écraser le dossier ScriptsMod
                        if (checkBoxSM.Checked == true)
                        {
                            foreach (var word in words)
                            {
                                if (System.Text.RegularExpressions.Regex.IsMatch(word, "ScriptsMod"))
                                {
                                    //bool fileExistsScriptsMod = File.Exists(DestFullName);
                                    bool fileExistsScriptsMod = File.Exists(destinationDirectoryName + @"\" + DestFullName);
                                    if (fileExistsScriptsMod & checkBoxSM.Checked)
                                     extractAutorise = false;
                                }
                            }
                        }
                        //regarde s'il est interdit d'écraser le dossier Active
                        if (checkBoxActiveFolder.Checked == true)
                        {
                            foreach (var word in words)
                            {
                                if (System.Text.RegularExpressions.Regex.IsMatch(word, "Active"))
                                {
                                    //MessageBox.Show(destinationDirectoryName + @"\" + DestFullName, destinationDirectoryName + @"\" + DestFullName);

                                    bool fileExistsActive = File.Exists(destinationDirectoryName+@"\"+DestFullName);
                                    if (fileExistsActive & checkBoxActiveFolder.Checked)
                                    {
                                        //MessageBox.Show(DestFullName, file.FullName);
                                        extractAutorise = false;
                                    }
                                }
                            }
                        }

                    }

                    if (words[0].Contains("Liveries"))
                    {
                        destinationDirectoryName = textBox_SavedGames.Text;
                    }
                    if (words[0].Contains("Mods"))
                    {
                        destinationDirectoryName = textBox_SavedGames.Text;
                    }
                    if (words[0].Contains("aircraft"))
                    {
                        destinationDirectoryName = textBox_SavedGames.Text;
                    }


                    DirectoryInfo di = Directory.CreateDirectory(destinationDirectoryName);
                    string destinationDirectoryFullPath = di.FullName;

                    string completeFileName = Path.GetFullPath(Path.Combine(destinationDirectoryFullPath, DestFullName));

                    if (!completeFileName.StartsWith(destinationDirectoryFullPath, StringComparison.OrdinalIgnoreCase))
                    {
                        throw new IOException("Trying to extract file outside of destination directory. See this link for more info: https://snyk.io/research/zip-slip-vulnerability");
                    }

                    if (file.Name == "")
                    {// Assuming Empty for Directory
                        Directory.CreateDirectory(Path.GetDirectoryName(completeFileName));
                        continue;
                    }

                    if(extractAutorise)
                     file.ExtractToFile(completeFileName, true);

                }
            }
        }

        private void modifierLigne(string path, string ligneRecherche, string ligneModifiee)
        {
            string texteFinal = null;
            StreamReader sr = new StreamReader(path);
            string ligneEnCoursDeLecture = null;
            while ((sr.Peek() != -1))
            {
                ligneEnCoursDeLecture = sr.ReadLine();

                if (ligneEnCoursDeLecture.IndexOf(ligneRecherche) > -1)
                {
                    texteFinal = (texteFinal
                                + (ligneModifiee + "\r\n"));
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
        }

        public Form1()
        {
            InitializeComponent();

            string pathOptionInstaller = System.Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments) + @"\DCE_Installer";
            bool exists = System.IO.Directory.Exists(pathOptionInstaller);
            bool fileExists = File.Exists(pathOptionInstaller + @"\options.txt");

            if (exists & fileExists)
            {
                string[] lines = System.IO.File.ReadAllLines(pathOptionInstaller + @"\options.txt");

                foreach (string line in lines)
                {
                    if (line.Contains("pathZipCampaign="))
                        textBox_Campaign.Text = line.Replace("pathZipCampaign=", "");

                    if (line.Contains("pathDCS="))
                        textBox_DCS.Text = line.Replace("pathDCS=", "");

                    if (line.Contains("pathSavedGames="))
                        textBox_SavedGames.Text = line.Replace("pathSavedGames=", "");

                    if (line.Contains("pathOVGME="))
                        textBox_OvGME.Text = line.Replace("pathOVGME=", "");
                }
            }


            ToolTip toolTip1 = new ToolTip();
            toolTip1.ShowAlways = true;
            toolTip1.ToolTipTitle = "Example :";
            toolTip1.UseFading = true;
            toolTip1.UseAnimation = true;
            toolTip1.IsBalloon = true;
            toolTip1.ShowAlways = true;
            toolTip1.AutoPopDelay = 20000;
            toolTip1.InitialDelay = 1000;
            toolTip1.ReshowDelay = 500;
            toolTip1.SetToolTip(m_ButtonDcsPath, @"C:\Eagle Dynamics\DCS World or DCS World OpenBeta");
            toolTip1.SetToolTip(textBox_Campaign, @"C:\Eagle Dynamics\DCS World or DCS World OpenBeta");
            toolTip1.SetToolTip(Label_DCS, @"C:\Eagle Dynamics\DCS World or DCS World OpenBeta");

            toolTip1.SetToolTip(m_ButtonSavedGames, @"C:\Users\yourname\Saved Games\DCS World or DCS World OpenBeta");
            toolTip1.SetToolTip(textBox_DCS, @"C:\Users\yourname\Saved Games\DCS World or DCS World OpenBeta");
            toolTip1.SetToolTip(Label_SavedGames, @"C:\Users\yourname\Saved Games\DCS World or DCS World OpenBeta");

        }

        private void listBox1_SelectedIndexChanged(object sender, EventArgs e)
        {

        }

        static class TestFile
        {
            public static bool structureValide = false;
            public static bool presenceMisScript = false;
            public static bool presenceScriptMod = false;
            public static string ScriptsMod;
        }
        static class TestPath
        {
            public static bool DCS_Root = false;
            public static bool DCS_SavedGames = false;
            public static bool OVGME = false;
        }
        static class ParamCampaign
        {
            public static string NameCampaign = "";
            public static string pathCampaign = "";
        }
        

        private void m_ButtonCampaign_Click(object sender, EventArgs e)
        {

            TestFile.structureValide = false;
            TestFile.presenceMisScript = false;
            TestFile.presenceScriptMod = false;
            //MessageBox.Show(textBox_Campaign.Text, "Information");

            OpenFileDialog fdlg = new OpenFileDialog();
            string Downloads = "";
            if (textBox_Campaign.Text == "")
            {
                Downloads = System.Environment.ExpandEnvironmentVariables("%USERPROFILE%\\Downloads");
            }
            Downloads = textBox_Campaign.Text;
            fdlg.InitialDirectory = Downloads;
            fdlg.DefaultExt = ".zip";
            fdlg.Filter = "ZIP  (.ZIP)|*.zip";
            fdlg.FilterIndex = 2;
            fdlg.RestoreDirectory = true;
            if (fdlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                 textBox_Campaign.Text = fdlg.FileName;
                
                //controle si les elements attendu sont dans le fichier zip
                using (var archive = ZipFile.Open(fdlg.FileName, ZipArchiveMode.Read))
                {
                    foreach (ZipArchiveEntry file in archive.Entries)
                    {
                        if (file.FullName.Contains("tech"))
                        {
                            TestFile.structureValide = true;
                            m_buttonNext.Visible = true;
                        }
                        if (file.FullName.Contains("DCE_Missionscript_mod"))
                        {
                            TestFile.presenceMisScript = true;
                        }
                        if (file.FullName.Contains("ScriptsMod"))
                        {
                            TestFile.presenceScriptMod = true;                                                         
                        }
                    }
                }
                if (TestFile.structureValide == false)
                {
                    MessageBox.Show("\"Tech\" path not found.\n Automatic installation canceled", "Error");
                }
                if (TestFile.presenceMisScript == false)
                {
                    MessageBox.Show("Missionscript not found", "Information");
                }
                if (TestFile.presenceScriptMod == false)
                {
                    MessageBox.Show("ScriptsMod not found", "Information");
                }

            }
        }
        private void m_ButtonDcsPath_Click(object sender, EventArgs e)
        {
            FolderBrowserDialog fdb = new FolderBrowserDialog();
            fdb.RootFolder = Environment.SpecialFolder.MyComputer;
            if (fdb.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                string combPath = Path.Combine(fdb.SelectedPath, "bin");
                if (Directory.Exists(combPath))
                {
                    m_buttonNext.Visible = true;
                    textBox_DCS.Text = fdb.SelectedPath;
                    TestPath.DCS_Root = true;
                }
                // string[] words = fdb.SelectedPath.Split('/');
                //string LowerWordEnd = words[words.Length - 1].ToLowerInvariant();
                //if (System.Text.RegularExpressions.Regex.IsMatch(LowerWordEnd, "dcs"))
                //{
                //    textBox_DCS.Text = fdb.SelectedPath;
                //    TestPath.DCS_Root = true;

                //    if (TestFile.structureValide & TestPath.DCS_Root & TestPath.DCS_SavedGames)
                //    {
                //        m_buttonNext.Visible = true;
                //    }
                //}
                else
                {
                    //MessageBox.Show("This folder does not seem to be the one of DCS.", words[words.Length - 1]);
                    MessageBox.Show("This directory does not appear to be the root folder of DCS: \r\n" + fdb.SelectedPath, "Error");
                }
            }
        }
        private void m_ButtonSavedGame_Click(object sender, EventArgs e)
        {
            FolderBrowserDialog fdb = new FolderBrowserDialog();
            string saveFolder = System.Environment.ExpandEnvironmentVariables("%USERPROFILE%\\Saved Games");
            fdb.SelectedPath = saveFolder;

            if (fdb.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                string combPath = Path.Combine(fdb.SelectedPath, "Logs");
                if (Directory.Exists(combPath))
                {
                    textBox_SavedGames.Text = fdb.SelectedPath;
                    TestPath.OVGME = true;
                }

                //string[] words = fdb.SelectedPath.Split('/');
                //string LowerWordEnd = words[words.Length - 1].ToLowerInvariant();
                //if (System.Text.RegularExpressions.Regex.IsMatch(LowerWordEnd, "dcs"))
                //{
                //    textBox_SavedGames.Text = fdb.SelectedPath;
                //    TestPath.OVGME = true;
                //}
                else
                {
                    MessageBox.Show("This directory does not seem to be the DCS Saved Games folder: \r\n"+ fdb.SelectedPath, "Error");

                }
            }

        }


        private void m_buttonOvGME_Click(object sender, EventArgs e)
        {
            FolderBrowserDialog fdb = new FolderBrowserDialog();
            fdb.RootFolder = Environment.SpecialFolder.MyComputer;
            if (fdb.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                textBox_OvGME.Text = fdb.SelectedPath;
                TestPath.DCS_Root = true;

                if (TestFile.structureValide & TestPath.DCS_Root & TestPath.DCS_SavedGames)
                {
                    m_buttonNext.Visible = true;
                }
            }
        }
        //private void label1_Click(object sender, EventArgs e)
        //{

        //}

        private void groupBox1_Enter(object sender, EventArgs e)
        {

        }

        private void textBox2_TextChanged(object sender, EventArgs e)
        {

        }

        private void textBox3_TextChanged(object sender, EventArgs e)
        {

        }

        private void m_buttonInstall_Click(object sender, EventArgs e)
        {

            ExtractZipFileToDirectory(textBox_Campaign.Text, true);

            //cherche le nom de la campagne dans le fichier zip
            bool findNameCampaign = false;
            bool findScriptsMod = false;
            

            string NameCampaign = "";
            string zipPath = textBox_Campaign.Text;
            using (ZipArchive archive = ZipFile.OpenRead(zipPath))
            {
                foreach (ZipArchiveEntry entry in archive.Entries)
                {
                    bool containsSearchResult = entry.FullName.Contains("Campaigns");

                    if (containsSearchResult & findNameCampaign == false)
                    {

                        string[] words = entry.FullName.Split('/');
                        string stringNum = Convert.ToString(words.Length);

                        for (int i = 0; i < words.Length; i++)
                        {
                            string stringMot = Convert.ToString(words[i].Length);
                            if (entry.Name == "" &&  words[i].Contains("Campaigns") & ((i + 1) < words.Length) && (words[i + 1].Length > 0) & findNameCampaign == false)
                            {
                                NameCampaign = words[i + 1];
                                ParamCampaign.NameCampaign = words[i + 1];
                                findNameCampaign = true;
                                break;

                            }
                        }
                    }

                    bool ScriptsModSearchResult = entry.FullName.Contains("ScriptsMod");
                    if (ScriptsModSearchResult & findScriptsMod == false)
                    {

                        string[] words = entry.FullName.Split('/');

                        foreach (var word in words)
                        {
                            if (word.Contains("ScriptsMod"))
                            {
                                //MessageBox.Show(Path.GetExtension(entry.FullName) + entry.FullName, "GetExtension ");
                                if (entry.FullName.EndsWith("/"))
                                {
                                    TestFile.ScriptsMod = word.Replace("ScriptsMod.", "");
                                    //MessageBox.Show(TestFile.ScriptsMod);
                                    findScriptsMod = true;
                                    break;
                                }
                            }
                        }
                    }
                }
            }

            //string pathCampaign = textBox_SavedGames.Text + @"\Mods\tech\DCE\Missions\Campaigns\" + NameCampaign ;
            //string pathFile = textBox_SavedGames.Text + @"\Mods\tech\DCE\Missions\Campaigns\" + NameCampaign + @"\Init\path.bat";

            ParamCampaign.pathCampaign = textBox_SavedGames.Text + @"\Mods\tech\DCE\Missions\Campaigns\" + ParamCampaign.NameCampaign;
            string pathFile = textBox_SavedGames.Text + @"\Mods\tech\DCE\Missions\Campaigns\" + ParamCampaign.NameCampaign + @"\Init\path.bat";


            //ecrit dans le fichier path.bat
            //REM Core or Main DCS ou DCS.beta path, always end the line with \
            //set "pathDCS=D:\___DCS___\"

            //REM DCS or DCS.beta saved game path, always end the line with \
            //set "pathSavedGames=Saved Games\DCS.openbeta\" 

            //REM DCE ScriptMod version not any / or \ and no space before and after =
            //set "versionPackageICM=20.43.59"

            string text = "REM Core or Main DCS ou DCS.beta path, always end the line with \\ \r\n" +
                           "set \"pathDCS=" + textBox_DCS.Text + "\\\"\r\n" +
                           "REM Core or Main DCS ou DCS.beta path, always end the line with \\ \r\n" +
                           "set \"pathSavedGames=" + textBox_SavedGames.Text + "\\\"\r\n" +
                           "REM DCE ScriptMod version not any / or \\ and no space before and after = \r\n" +
                           "set \"versionPackageICM="+ TestFile.ScriptsMod + "\"\r\n" +
                           "\r\n" +
                           "\r\n" +
                           "REM After each change, You must launch the FirsMission.bat for it to be taken into account.";

            System.IO.File.WriteAllText(pathFile, text);


            string pathOptionInstaller = System.Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments) + @"\DCE_Installer";

            //string pathOptionInstaller = @"D:\_D_DDocuments";

            bool exists = System.IO.Directory.Exists(pathOptionInstaller);

            if (!exists)
                System.IO.Directory.CreateDirectory(pathOptionInstaller);

            //textBox_Campaign.Text = textBox_Campaign.Text.Replace(".zip", "");
            string[] wordsOption = textBox_Campaign.Text.Split('\\');

            //MessageBox.Show(textBox_Campaign.Text , Convert.ToString(wordsOption.Length));
            textBox_Campaign.Text = textBox_Campaign.Text.Replace(wordsOption[wordsOption.Length-1], "");

            //Inscrit dans le fichier option, pour ne pas retapper les paths à la prochaine installation
            string textOption =
                "pathZipCampaign=" + textBox_Campaign.Text + "\r\n" +
                "pathDCS=" + textBox_DCS.Text + "\r\n" +
                "pathSavedGames=" + textBox_SavedGames.Text + "\r\n" +
                "pathOVGME=" + textBox_OvGME.Text;


            // Create the file, or overwrite if the file exists.

            try
            {
                using (FileStream fs = File.Create(pathOptionInstaller + @"\options.txt"))
                {
                     byte[] info = new UTF8Encoding(true).GetBytes(textOption);
                    // Add some information to the file.
                    fs.Write(info, 0, info.Length);
                }

            }
            catch (UnauthorizedAccessException)
            {

            }



            string[] PreScripWords = TestFile.ScriptsMod.Split('_');
            //MessageBox.Show(PreScripWords[0]);
            string[] ScripWords = PreScripWords[0].Split('.');
            //MessageBox.Show(ScripWords[0], ScripWords[1]);
            int versionScriptMod = int.Parse(ScripWords[0] + ScripWords[1] + ScripWords[2]);
            //MessageBox.Show(versionScriptMod.ToString(), ScripWords[0]);
            bool changerEventsTrackerFile = false;
            if (versionScriptMod <= 203801)
                changerEventsTrackerFile = true;

            string EventsTrackerPath = textBox_SavedGames.Text + @"\Mods\tech\DCE\ScriptsMod." + TestFile.ScriptsMod + @"\Mission Scripts\EventsTracker.lua";
            if (changerEventsTrackerFile)
            {
                //MessageBox.Show("Passe " +versionScriptMod.ToString(), ScripWords[0]);
                //mise à jour du fichier EventsTracker 203801 qui est buggué
                
                string BadText = "		if  string.sub (camp.path, 2, 1";
                string GoodText = "		if  string.sub (camp.path, 2, 2) ~= \":\" then    -- texte modifie par DCE_Installer.exe";

                modifierLigne(EventsTrackerPath, BadText, GoodText);

            }


            //===================================================================================
            //met à jour le lien dans camp_status.lua pour ensuite l'ajouter dans firtmission.miz
            //string pathCampST = pathCampaign  + @"\Active\camp_status.lua";
            string pathCampST = ParamCampaign.pathCampaign + @"\Active\camp_status.lua";
            bool fileExistsCampST = File.Exists(pathCampST);

            if (fileExistsCampST)
            {
                //mise à jour du fichier camp_status s'il existe
                string pathStatus = textBox_SavedGames.Text.Replace(@"\", "/") + "/";             
                modifierLigne(pathCampST, "['path']", "	['path'] = '" + pathStatus + "',");

                string pathFirstMission = textBox_SavedGames.Text + @"\Mods\tech\DCE\Missions\Campaigns\" + NameCampaign + "_first.miz";
                string pathOngoingMission = textBox_SavedGames.Text + @"\Mods\tech\DCE\Missions\Campaigns\" + NameCampaign + "_ongoing.miz";
               
                //suppression de l'ancien camp_status dans firstmission.miz
                using (ZipArchive archive = ZipFile.Open(pathFirstMission, ZipArchiveMode.Update))
                {
                    foreach (ZipArchiveEntry entry in archive.Entries)
                    {
                        if (entry.Name.Equals("camp_status.lua"))
                        {
                            entry.Delete();
                            break; //needed to break out of the loop
                        }
                    }
                }
                //suppression de l'ancien EventsTracker dans firstmission.miz
                using (ZipArchive archive = ZipFile.Open(pathFirstMission, ZipArchiveMode.Update))
                {
                    foreach (ZipArchiveEntry entry in archive.Entries)
                    {
                        if (changerEventsTrackerFile && entry.Name.Equals("EventsTracker.lua"))
                        {
                            entry.Delete();
                            break; //needed to break out of the loop
                        }
                    }
                }
                //mise à jour de la premiere mission firstmission.miz
                using (var zipArchive = ZipFile.Open(pathFirstMission, ZipArchiveMode.Update))
                {
                    var fileInfo = new FileInfo(pathCampST);
                    zipArchive.CreateEntryFromFile(fileInfo.FullName, @"l10n\DEFAULT\" + fileInfo.Name);

                    if (changerEventsTrackerFile)
                    {
                        var fileInfo2 = new FileInfo(EventsTrackerPath);
                        zipArchive.CreateEntryFromFile(fileInfo2.FullName, @"l10n\DEFAULT\" + fileInfo2.Name);
                    }
                }

                //suppression de l'ancien camp_status dans ongoing.miz
                using (ZipArchive archive = ZipFile.Open(pathOngoingMission, ZipArchiveMode.Update))
                {
                    foreach (ZipArchiveEntry entry in archive.Entries)
                    {
                        if (entry.Name.Equals("camp_status.lua"))
                        {
                            entry.Delete();
                            break; //needed to break out of the loop
                        }
                    }
                }
                using (ZipArchive archive = ZipFile.Open(pathOngoingMission, ZipArchiveMode.Update))
                {
                    foreach (ZipArchiveEntry entry in archive.Entries)
                    {
                        if (changerEventsTrackerFile && entry.Name.Equals("EventsTracker.lua"))
                        {
                            entry.Delete();
                            break; //needed to break out of the loop
                        }
                    }
                }
                //mise à jour de la deuxieme mission ongoing.miz
                using (var zipArchive = ZipFile.Open(pathOngoingMission, ZipArchiveMode.Update))
                {
                    var fileInfo = new FileInfo(pathCampST);
                    zipArchive.CreateEntryFromFile(fileInfo.FullName, @"l10n\DEFAULT\" + fileInfo.Name);

                    if (changerEventsTrackerFile)
                    {
                        var fileInfo2 = new FileInfo(EventsTrackerPath);
                        zipArchive.CreateEntryFromFile(fileInfo2.FullName, @"l10n\DEFAULT\" + fileInfo2.Name);
                    }
                }
                MessageBox.Show(NameCampaign + " successfully installed.\r\n" +
                    "   Activate DCE_Missionscript_mod with OvGME before launching DCS", "Information");

            }
            else
            {

                MessageBox.Show(NameCampaign + " successfully installed.\r\n" +
                    "   Activate DCE_Missionscript_mod with OvGME before launching DCS", "Information");
                MessageBox.Show("But this campaign must ABSOLUTELY be updated with FirstMission.bat...", "Information");
                //System.Diagnostics.Process.Start(pathCampaign);

                Process process = new Process();
                // Configure the process using the StartInfo properties.
                process.StartInfo.FileName = ParamCampaign.pathCampaign + @"\FirstMission.bat";
                process.StartInfo.Arguments = " ";
                process.StartInfo.WindowStyle = ProcessWindowStyle.Maximized;
                process.StartInfo.WorkingDirectory = ParamCampaign.pathCampaign;
                process.Start();
                //process.WaitForExit();// Waits here for the process to exit.
            }

            

            m_buttonNext.Visible = false;

            //Close();

        }

        private void label1_Click(object sender, EventArgs e)
        {

        }

        private void button1_Click(object sender, EventArgs e)
        {

            Close();
        }

        private void label2_Click(object sender, EventArgs e)
        {

        }

        private void Form1_Load(object sender, EventArgs e)
        {

        }

        private void pictureBox1_Click(object sender, EventArgs e)
        {

        }

        private void textBox1_TextChanged(object sender, EventArgs e)
        {

        }

        private void labelInc01_Click(object sender, EventArgs e)
        {
            
        }

        private void label3_Click(object sender, EventArgs e)
        {

        }

        private void label4_Click(object sender, EventArgs e)
        {

        }

        private void textBox4_TextChanged(object sender, EventArgs e)
        {

        }

        private void toolTip1_Popup(object sender, PopupEventArgs e)
        {

        }

        private void linkLabelOvGME_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            try
            {
                VisitLinkOvGME();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Unable to open link that was clicked.");
            }
        }
        private void VisitLinkOvGME()
        {
            // Change the color of the link text by setting LinkVisited
            // to true.
            linkLabelOvGME.LinkVisited = true;
            //Call the Process.Start method to open the default browser
            //with a URL:
            System.Diagnostics.Process.Start("https://wiki.hoggitworld.com/view/OVGME#Download_the_installer");
        }

        private void linkLabelCampaign_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            try
            {
                VisitLinkCampaign();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Unable to open link that was clicked.");
            }
        }
        private void VisitLinkCampaign()
        {
            // Change the color of the link text by setting LinkVisited
            // to true.
            linkLabelCampaign.LinkVisited = true;
            //Call the Process.Start method to open the default browser
            //with a URL:
            System.Diagnostics.Process.Start("https://forums.eagle.ru/topic/162712-dce-campaigns/");
        }

        private void label1_Click_1(object sender, EventArgs e)
        {

        }

        private void label3_Click_1(object sender, EventArgs e)
        {

        }

        private void pictureBox3_Click(object sender, EventArgs e)
        {

        }

        private void checkBoxSM_CheckedChanged(object sender, EventArgs e)
        {

        }
    }
}