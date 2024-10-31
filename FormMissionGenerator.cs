using System;
using System.IO;
using System.IO.Compression;
using System.Windows.Forms;
using DCE_Manager;
using NLua;
using DCE_Manager.Utils;
using DCE_Manager.Parameters;





namespace DCE_Manager
{
 

    public partial class FormMissionGenerator : Form
    {
        //Form1 _form1;

        private string missionName;
        private Form1 mainForm;

        // Constructeur personnalisé pour accepter des paramètres
        public FormMissionGenerator(string missionName, Form1 callingForm)
        {
            InitializeComponent();

            // Utiliser le paramètre passé dans le formulaire
            this.missionName = missionName;

            // Accéder au formulaire parent Form1
            this.mainForm = callingForm;
        }

        private void Form5_MissionGenerator_Load(object sender, EventArgs e)
        {
            // Assurez-vous d'appeler UpdateSharedData avant d'ouvrir Form3_Clonage
            mainForm.UpdateSharedData();

            // Vous pouvez accéder à la variable missionName ici ou interagir avec le formulaire parent (Form1)
            MessageBox.Show("Mission Name: " + missionName);
        }


        public void ExtractZipFileToDirectory(string sourceZipFilePath, string destinationDirectoryName, bool overwrite)
        {
            using (var archive = ZipFile.Open(sourceZipFilePath, ZipArchiveMode.Read))
            {
                if (!overwrite)
                {
                    archive.ExtractToDirectory(destinationDirectoryName);
                    return;
                }

                DirectoryInfo di = Directory.CreateDirectory(destinationDirectoryName);
                string destinationDirectoryFullPath = di.FullName;

                foreach (ZipArchiveEntry file in archive.Entries)
                {
                    string completeFileName = Path.Combine(destinationDirectoryName, file.FullName);
                    if (file.Name == "")
                    {// Assuming Empty for Directory
                        Directory.CreateDirectory(Path.GetDirectoryName(completeFileName));
                        continue;
                    }
                    // create dirs
                    var dirToCreate = destinationDirectoryName;
                    for (var i = 0; i < file.FullName.Split('/').Length - 1; i++)
                    {
                        var s = file.FullName.Split('/')[i];
                        dirToCreate = Path.Combine(dirToCreate, s);
                        if (!Directory.Exists(dirToCreate))
                            Directory.CreateDirectory(dirToCreate);
                    }
                    file.ExtractToFile(completeFileName, true);
                }
            }
        }


        //public Form5_MissionGenerator(Form1 form1)
        //{
        //    InitializeComponent();
        //    _form1 = form1;
        //}

        static class SetPath
        {

            public static string campaignName = "";
        }


        private void checkBox2_CheckedChanged(object sender, EventArgs e)
        {


        }

        private void missionGenerator_Load(object sender, EventArgs e)
        {

        }


        public void button1_Click(object sender, EventArgs aa)
        {


            Lua lua2 = new Lua();

            // Assurez-vous d'appeler UpdateSharedData avant d'ouvrir Form3_Clonage
            mainForm.UpdateSharedData();

            // Enregistrer la fonction de débogage C#
            lua2.RegisterFunction("print", typeof(Form1).GetMethod("LuaPrint", new Type[] { typeof(string) }));

            // Enregistrer la fonction de confirmation C#
            lua2.RegisterFunction("ShowConfirmationDialog", typeof(Form1).GetMethod("ShowConfirmationDialog"));



            //dezip le baseMission pour utiliser directement les fichiers dezippé
            Environment.CurrentDirectory = SharedData.textBox_SavedGames + @"\Mods\tech\DCE\Missions\Campaigns\Crisis in PG-Blue\";

            //ExtractZipFileToDirectory(Environment.CurrentDirectory + @"\Init\base_mission.miz", Environment.CurrentDirectory + @"\Init\base_missionFile", true);


            string InputTypeGame = "";

            if (radioButton1.Checked)
                InputTypeGame = "s";
            if (radioButton2.Checked)
                InputTypeGame = "d";
            if (radioButton3.Checked)
                InputTypeGame = "df";

            if (radioButton4.Checked)
                InputTypeGame = "i";
            if (radioButton5.Checked)
                InputTypeGame = "n";





            lua2["versionPackageICM"] = "NG";                                                                          //Create lua variables
            lua2["pathScriptsMod"] = SharedData.textBox_SavedGames + @"\Mods\tech\DCE\ScriptsMod";                    //Create lua variables
            lua2["pathCampaign"] = SharedData.textBox_SavedGames + @"\Mods\tech\DCE\Missions\Campaigns\Hornet over Caucasus-CVN\";                    //Create lua variables
            lua2["choix1"] = InputTypeGame;                                                                            //Create lua variables
            lua2["generator"] = "DCE_Manager";                                                                         //Create lua variables
            lua2["pathSavedGames"] = SharedData.textBox_SavedGames;                                                                            //Create lua variables


            //lua2.DoFile(SharedData.textBox_SavedGames + @"\Mods\tech\DCE\Missions\Campaigns\" + @"Hornet over Caucasus-CVN\" + @"Init\conf_mod.lua");
            //lua2.DoFile(SharedData.textBox_SavedGames + @"\Mods\tech\DCE\Missions\Campaigns\" + @"Hornet over Caucasus-CVN\" + @"Init\camp_init.lua");
            //lua2.DoFile(SharedData.textBox_SavedGames + @"\Mods\tech\DCE\Missions\Campaigns\" + @"Hornet over Caucasus-CVN\" + @"Init\oob_air_init.lua");

            //lua2.DoFile(SharedData.textBox_SavedGames + @"\Mods\tech\DCE\ScriptsMod.NG\UTIL_Functions.lua");

            //lua2.DoFile(SharedData.textBox_SavedGames + @"\Mods\tech\DCE\Missions\Campaigns\" + @"Hornet over Caucasus-CVN\" + @"Init\targetlist_init.lua");

            //lua2.DoFile(SharedData.textBox_SavedGames + @"\Mods\tech\DCE\ScriptsMod.NG\UTIL_Version.lua");


            //var result = lua.DoFile(filename);


            //var result = lua2.DoFile(SharedData.textBox_SavedGames + @"\Mods\tech\DCE\ScriptsMod.NG\BAT_FirstMission_C.lua");

            var result = lua2.DoFile(SharedData.textBox_SavedGames + @"\Mods\tech\DCE\ScriptsMod.NG\BAT_FirstMission.lua");

            string message = lua2.GetString("s");

            MessageBox.Show(message, "Info");

            LuaFunction on_event = lua2.GetFunction("OnEvent");

          


        }

        private void textBox_MG_TextChanged(object sender, EventArgs e)
        {

        }
    }
}
