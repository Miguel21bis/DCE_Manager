using System;
using System.IO;
using System.Windows.Forms;
using NLua;
using System.Collections.Generic;
using DCE_Manager.Utils;
using DCE_Manager.Parameters;
using System.Threading;
using System.Globalization;
using System.Linq;

namespace DCE_Manager
{
    public partial class FormClonage : Form
    {
        Form1 _form1;

        public LuaTable TabSquad;

        //static class CloneCampaign
        //{
        //    public static string OldNameCamp = "";
        //    public static string path = "";
        //    public static string SquadName = "";
        //}

        public FormClonage(Form1 form1, string path, string OldNameCamp)
        {

            OldNameCamp = OldNameCamp.TrimStart();
            OldNameCamp = OldNameCamp.TrimEnd();

            InitializeComponent();

            _form1 = form1;

            if (form1 == null)
            {
                MessageBox.Show("Le Form1 passé au constructeur est NULL !");
            }

            Lua lua = new Lua();

            lua["versionPackageICM"] = "NG";
            lua["pathScriptsMod"] = SharedData.textBox_SavedGames + @"\Mods\tech\DCE\ScriptsMod.NG"; 
            lua["pathCampaign"] = SharedData.textBox_SavedGames + @"\Mods\tech\DCE\Missions\Campaigns\" + OldNameCamp;
            lua["generator"] = "DCE_Manager";
            lua["pathSavedGames"] = SharedData.textBox_SavedGames;
            // Crée la table Debug avec la clé "debug" à false
            var debugTable = new Dictionary<string, object>
            {
                { "debug", false }
            };
            lua["Debug"] = debugTable;

            lua.DoFile(SharedData.textBox_SavedGames + @"\Mods\tech\DCE\ScriptsMod.NG\DCEM_Function.lua");

            var result = lua.DoFile(SharedData.textBox_SavedGames + @"\Mods\tech\DCE\ScriptsMod.NG\DCEM_Function.lua");

            //LuaTable luaTable = (LuaTable)result[0];
            //LuaTable tabSquad = (LuaTable)luaTable["tabSquad"];

            if (result == null || result.Length == 0 || result[0] == null)
            {
                MessageBox.Show("Le script Lua n'a pas retourné de résultat valide.");
                return;
            }

            LuaTable luaTable = result[0] as LuaTable;
            if (luaTable == null)
            {
                MessageBox.Show("luaTable est NULL !");
                return;
            }

            if (!luaTable.Keys.Cast<object>().Contains("tabSquad"))
            {
                MessageBox.Show("tabSquad n'existe pas dans luaTable !");
                return;
            }

            LuaTable tabSquad = luaTable["tabSquad"] as LuaTable;
            if (tabSquad == null)
            {
                MessageBox.Show("tabSquad est NULL !");
                return;
            }




            var enumerator = tabSquad.GetEnumerator();
            int i = 1;
            while (enumerator.MoveNext())
            {
                comboPlaneChoice.Items.Add(enumerator.Value.ToString());
                comboPlaneChoice.SelectedItem = enumerator.Value.ToString();
                i++;
            }

            string tempTXT = (string)comboPlaneChoice.SelectedItem;
            string[] words = tempTXT.Split('|');
            planeFIX.Text = words[0].Replace(" ", "");
            SquadName.Text = words[1];

            SquadName.Text = SquadName.Text.TrimStart();
            SquadName.Text = SquadName.Text.TrimEnd();

            CloneCampaign.SquadName = SquadName.Text;
            BaseName.Text = words[2];
            CampaignName.Text = OldNameCamp + "-" + planeFIX.Text;
            string NewdNameCamp = CampaignName.Text;

            CloneCampaign.path = path;
            CloneCampaign.OldNameCamp = OldNameCamp;
        }


        public void CreateDicoClassSquad(Form1 form1, string path, string nameCamp)
        {
            Form1
              Form1 = form1;

            PublicTable.errorTable.Clear();
            Form1.textBox_Bugs.Text = "";
            form1.tabPage12.Text = "Bugs";

            //Pour représenter la table Lua donnée en C#, il est généralement préférable d'utiliser une structure de données 
            //qui offre la flexibilité de stocker des paires clé-valeur, similaire aux tables en Lua. 
            //    En C#, le type de données le plus approprié pour cela est le Dictionary<TKey, TValue>.

            Lua lua = new Lua();

            lua["versionPackageICM"] = "NG";                                                                          //Create lua variables
            lua["pathScriptsMod"] = Form1.textBox_SavedGames.Text + @"\Mods\tech\DCE\ScriptsMod.NG";                    //Create lua variables
            lua["pathCampaign"] = Form1.textBox_SavedGames.Text + @"\Mods\tech\DCE\Missions\Campaigns\" + nameCamp;                    //Create lua variables
            lua["generator"] = "DCE_Manager";                                                                         //Create lua variables
            lua["pathSavedGames"] = Form1.textBox_SavedGames.Text;                                                                            //Create lua variables
                                                                                                                                              // Crée la table Debug avec la clé "debug" à false
            var debugTable = new Dictionary<string, object>
            {
                { "debug", false }
            };
            lua["Debug"] = debugTable;

            lua.DoFile(Form1.textBox_SavedGames.Text + @"\Mods\tech\DCE\ScriptsMod.NG\DCEM_Function.lua");

            var result = lua.DoFile(Form1.textBox_SavedGames.Text + @"\Mods\tech\DCE\ScriptsMod.NG\DCEM_Function.lua");

            LuaTable luaTable = (LuaTable)result[0];
            LuaTable taskByPlaneLua = (LuaTable)luaTable["taskByPlane"];

            // Créer le dictionnaire pour stocker les données
            var taskByPlane = new Dictionary<string, Dictionary<string, bool>>();

            // Parcourir la table Lua
            foreach (var key in taskByPlaneLua.Keys)
            {
                string planeName = key.ToString();
                LuaTable taskTable = (LuaTable)taskByPlaneLua[key];

                var taskDictionary = new Dictionary<string, bool>();
                foreach (var taskKey in taskTable.Keys)
                {
                    string taskName = taskKey.ToString();
                    bool taskValue = (bool)taskTable[taskKey];
                    taskDictionary[taskName] = taskValue;
                }

                taskByPlane[planeName] = taskDictionary;
            }

            //// Charger le tableau playable_m
            //LuaTable playable_mLua = (LuaTable)luaTable["playable_m"];
            //List<string> playableList = new List<string>();

            //// Parcourir la table Lua
            //foreach (var plane in playable_mLua.Keys)
            //{
            //    playableList.Add(plane.ToString());
            //}


            //*************************************************************************
            //CREATION DES TABLES BASE ********************************************************
            //PARSE les fichiers BASE  (db_airbases.lua) ********************************************************
            //*************************************************************************

            //string nameCamp = "YourCampaignName";  // Remplacez par le nom de votre campagne
            string savedGamesPath = Form1.textBox_SavedGames.Text;


            // Réinitialiser Airdrome
            PublicTable.Airdrome = new Dictionary<string, Dictionary<string, List<string>>>()
            {
                { "Init", new Dictionary<string, List<string>> { { "blue", new List<string>() }, { "red", new List<string>() } } },
                { "Active", new Dictionary<string, List<string>> { { "blue", new List<string>() }, { "red", new List<string>() } } }
            };


            for (int d = 1; d <= 2; d++)
            {
                string pathAirbase = "";

                if (d == 1)
                    pathAirbase = Path.Combine(savedGamesPath, @"Mods\tech\DCE\Missions\Campaigns\", nameCamp, @"Init\db_airbases.lua");
                else if (d == 2)
                    pathAirbase = Path.Combine(savedGamesPath, @"Mods\tech\DCE\Missions\Campaigns\", nameCamp, @"Active\db_airbases.lua");

                if (File.Exists(pathAirbase))
                {
                    LuaParse C_db_airbases = new LuaParse();
                    ParamCampaign.NameFileParse = pathAirbase;

                    C_db_airbases.Parse(File.ReadAllText(pathAirbase));

                    string state = d == 1 ? "Init" : "Active";

                    foreach (KeyValuePair<string, LuaObject> entry in C_db_airbases.Val)
                    {
                        foreach (KeyValuePair<string, LuaObject> entry2 in entry.Value)
                        {
                            if ((entry2.Key == "side" || entry2.Key == "coalition"))
                            {
                                if (entry2.Value.luaobj.ToString() == "red")
                                {
                                    PublicTable.Airdrome[state]["red"].Add(entry.Key);
                                }
                                else if (entry2.Value.luaobj.ToString() == "blue")
                                {
                                    PublicTable.Airdrome[state]["blue"].Add(entry.Key);
                                }
                            }
                        }
                    }
                }
            }


            if (ParamDivers.NewParseOobAir)
            {
                IDictionary<int, string> path_oob_air = new Dictionary<int, string>();
                string path_oob_airFile = Form1.textBox_SavedGames.Text + @"\Mods\tech\DCE\Missions\Campaigns\" + nameCamp + @"\Init\oob_air_init.lua";
                path_oob_air.Add(1, path_oob_airFile); //adding a key/value using the Add() method

                path_oob_airFile = Form1.textBox_SavedGames.Text + @"\Mods\tech\DCE\Missions\Campaigns\" + nameCamp + @"\Active\oob_air.lua";
                path_oob_air.Add(2, path_oob_airFile);


                //***************************------------------


                List_oob_air_Manager.List_oob_air = new List<Squad>();


                //on compte tous les squads differement, init ou active, sinon ça fout le bordel
                int idSquad = -1;

                for (int d = 1; d <= 2; d++)
                {

                    //*************************************************************************
                    //PARSE LEs FICHIERs ******************************************************
                    //oob_air_init et
                    //oob_air
                    //*************************************************************************

                    string folderFile = "Init";
                    if (d == 2)
                        folderFile = "Active";


                    LuaParse C_oobAir = new LuaParse();

                    string pathFileB = path_oob_air[d];

                    if (File.Exists(pathFileB))
                    {
                        ParamCampaign.NameFileParse = pathFileB;

                        C_oobAir.Parse(File.ReadAllText(pathFileB));

                        //List<Squad> List_oob_air = new List<Squad>();

                        //int tableN = 0;

                        //int sideInt = 0;

                        foreach (KeyValuePair<string, LuaObject> entry in C_oobAir.Val)
                        {
                            //int nbLigne = 0;
                            string side = entry.Key;


                            //if (side == "blue")
                            //{
                            //    sideInt = 1;
                            //}
                            //else if (side == "red")
                            //{
                            //    sideInt = 2;
                            //}

                            //tableN = 1;

                            foreach (KeyValuePair<string, LuaObject> entry2 in entry.Value)
                            {
                                //Boolean beShown = true;
                                int[] firstPos = new int[50];

                                int idElement = 0;
                                //tableN = 2;
                                idSquad++;

                                var squad = new Squad
                                {
                                    SideString = entry.Key.ToString(),
                                    IdSquad = idSquad,
                                    FolderFile = folderFile,
                                };

                                List_oob_air_Manager.List_oob_air.Add(squad);

                                foreach (KeyValuePair<string, LuaObject> entry3 in entry2.Value)
                                {

                                    idElement++;
                                    //tableN = 3;

                                    for (int r = 0; r < firstPos.Length; r++)
                                    {
                                        firstPos[r] = -1;
                                    }

                                    if (entry3.Key == "name")
                                    {
                                        squad.Name = entry3.Value.luaobj.ToString();
                                    }

                                    else if (entry3.Key == "player")
                                    {
                                        squad.Player = Convert.ToBoolean(entry3.Value.luaobj);
                                    }

                                    else if (entry3.Key == "type")
                                    {
                                        squad.Type = entry3.Value.luaobj.ToString();
                                    }

                                    else if (entry3.Key == "country")
                                    {
                                        squad.Country = entry3.Value.luaobj.ToString();

                                    }

                                    else if (entry3.Key == "skill")
                                    {
                                        squad.Skill = entry3.Value.luaobj.ToString();
                                    }
                                    else if (entry3.Key == "base")
                                    {
                                        squad.Base = entry3.Value.luaobj.ToString();
                                    }
                                    else if (entry3.Key == "baseAlternative")
                                    {

                                        squad.BaseAlternative = new List<string>();

                                        foreach (KeyValuePair<string, LuaObject> entry4 in entry3.Value)
                                        {
                                            idElement++;
                                            //tableN = 4;
                                            string baseName = entry4.Value.luaobj.ToString().Trim('\"');
                                            squad.BaseAlternative.Add(baseName);

                                        }

                                    }
                                    else if (entry3.Key == "number" && d == 1)
                                    {
                                        squad.Number = Convert.ToInt32(entry3.Value.luaobj);
                                    }
                                    else if (entry3.Key == "InitNumber" && d == 2)
                                    {
                                        squad.InitNumber = Convert.ToInt32(entry3.Value.luaobj);
                                    }
                                    else if (entry3.Key == "reserve" && d == 1)
                                    {
                                        squad.Reserve = Convert.ToInt32(entry3.Value.luaobj);
                                    }
                                    else if (entry3.Key == "InitReserve" && d == 2)
                                    {
                                        squad.InitReserve = Convert.ToInt32(entry3.Value.luaobj);
                                    }
                                    else if (entry3.Key == "tasks")
                                    {

                                        //squad.Tasks = new Dictionary<string, object>();
                                        squad.Tasks = new Dictionary<string, bool>();

                                        foreach (KeyValuePair<string, LuaObject> entry4 in entry3.Value)
                                        {
                                            idElement++;
                                            //tableN = 4;

                                            squad.Tasks.Add(entry4.Key, Convert.ToBoolean(entry4.Value.luaobj));

                                        }
                                    }
                                    else if (entry3.Key == "tasksCoef")
                                    {
                                        //squad.TasksCoef = new Dictionary<string, object>();
                                        squad.TasksCoef = new Dictionary<string, double>();

                                        foreach (KeyValuePair<string, LuaObject> entry4 in entry3.Value)
                                        {
                                            idElement++;
                                            //tableN = 4;

                                            //squad.TasksCoef.Add(entry4.Key, Convert.ToDouble(entry4.Value.luaobj));

                                            double valeur;
                                            if (Double.TryParse(entry4.Value.luaobj.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out valeur))
                                            {
                                                squad.TasksCoef.Add(entry4.Key, valeur);
                                            }
                                            else
                                            {
                                                // Gestion de l'erreur : log, valeur par défaut, message utilisateur, etc.
                                                MessageBox.Show("It is impossible to transform a digital format into a ‘double’. Perhaps a problem with the decimal separator.", "Retex");
                                            }

                                        }
                                    }
                                    else if (entry3.Key == "inactive")
                                    {
                                        squad.Inactive = Convert.ToBoolean(entry3.Value.luaobj);
                                    }
                                    else if (entry3.Key == "roster")
                                    {

                                        squad.Roster = new Dictionary<string, object>();

                                        foreach (KeyValuePair<string, LuaObject> entry4 in entry3.Value)
                                        {
                                            idElement++;
                                            //tableN = 4;
                                            squad.Roster.Add(entry4.Key, Convert.ToInt32(entry4.Value.luaobj));
                                        }
                                    }
                                    else if (entry3.Key == "score")
                                    {

                                        squad.Score = new Dictionary<string, object>();

                                        foreach (KeyValuePair<string, LuaObject> entry4 in entry3.Value)
                                        {
                                            idElement++;
                                            //tableN = 4;
                                            squad.Score.Add(entry4.Key, Convert.ToInt32(entry4.Value.luaobj));
                                        }
                                    }
                                    else //si ce n'est pas prévu dans la Class, on ajoute dans AdditionalProperties
                                    {

                                        // Vérifier si la valeur est une sous-table
                                        //var value = entry3.Value;
                                        //if (entry3.Value is LuaObject luaDict)
                                        if (
                                            entry3.Value.luaobj.GetType() != typeof(int) &&
                                            entry3.Value.luaobj.GetType() != typeof(string) &&
                                            entry3.Value.luaobj.GetType() != typeof(bool)
                                            )
                                        {
                                            var dict = new Dictionary<string, object>();

                                            foreach (KeyValuePair<string, LuaObject> entry4 in entry3.Value)
                                            {
                                                dict.Add(entry4.Key.ToString(), entry4.Value.luaobj);
                                            }
                                            squad.AdditionalProperties[entry3.Key] = dict;
                                        }
                                        else
                                        {
                                            squad.AdditionalProperties[entry3.Key] = entry3.Value.luaobj;
                                        }
                                    }

                                } // fin foreach 3
                                
                            }
                        }// fileExist()
                    }
                }



                //IDictionary<int, string> pathOobAir = new Dictionary<int, string>();
                //string pathFile = Form1.textBox_SavedGames.Text + @"\Mods\tech\DCE\Missions\Campaigns\" + nameCamp + @"\Init\oob_air_init.lua";
                //pathOobAir.Add(1, pathFile); //adding a key/value using the Add() method

                //pathFile = Form1.textBox_SavedGames.Text + @"\Mods\tech\DCE\Missions\Campaigns\" + nameCamp + @"\Active\oob_air.lua";
                //pathOobAir.Add(2, pathFile);


                //Form1.CampaignTab.Text = "";
                //Form1.groupBoxCampEdit.Text = nameCamp;
                //Form1.CampaignTab.Visible = true;


                //for (int d = 1; d <= 2; d++)
                //{
                    
                    ////*************************************************************************
                    ////RECUPERATION Briefing Campaign ********************************************************
                    ////PARSE camp_trigger_init ********************************************************
                    ////*************************************************************************

                    //string campTrigger = Form1.textBox_SavedGames.Text + @"\Mods\tech\DCE\Missions\Campaigns\" + nameCamp + @"\Init\camp_triggers_init.lua";

                    //LuaParse C_campTrigger = new LuaParse();

                    //Form1.ParamCampaign.NameFileParse = campTrigger;

                    //C_campTrigger.Parse(File.ReadAllText(campTrigger));

                    //string BriefingCampaign = "";

                    //foreach (KeyValuePair<string, LuaObject> entry1 in C_campTrigger.Val)
                    //{
                    //    foreach (KeyValuePair<string, LuaObject> entry2 in entry1.Value)
                    //    {
                    //        if ((entry1.Key == "Campaign Briefing"))
                    //        {
                    //            //Form1.LogRegister("entry2.Key "+ entry2.Key);
                    //        }

                    //        if ((entry1.Key == "Campaign Briefing" & entry2.Key == "action"))
                    //        {
                    //            foreach (KeyValuePair<string, LuaObject> entry3 in entry2.Value)
                    //            {
                    //                 if (entry3.Value.luaobj.ToString().IndexOf("Action.AddImage") > -1)
                    //                {
                    //                     break;
                    //                }
                    //                BriefingCampaign = BriefingCampaign + entry3.Value.luaobj.ToString();

                    //            }
                    //        }
                    //    }
                    //}

                    
                    //BriefingCampaign = BriefingCampaign.Replace("'Action.Text(\"", "").Replace("\")'", "\r\n").Replace("\"", "\r\n");
                    
                    //BriefingCampaign = Regex.Replace(BriefingCampaign, @"\[.\]=", "");

                    //Form1.textBoxCampBriefing.Text = BriefingCampaign;


                    ////*************************************************************************
                    ////IMAGE Campagne **********************************************************
                    //// ************************************************************************
                    ////*************************************************************************

                    //string campPath = Form1.textBox_SavedGames.Text + @"\Mods\tech\DCE\Missions\Campaigns\" + nameCamp;
                    ////Form1.pictureBoxCampImage.Image = Image.FromFile(campPath + ".bmp");

                    //string imagePath = campPath + ".bmp";
                    //using (Image image = Image.FromFile(imagePath, true))
                    //{
                    //    Form1.pictureBoxCampImage.Image?.Dispose();
                    //    Form1.pictureBoxCampImage.Image = new Bitmap(image);
                    //}


                   

                //}

            }   ////PARSE LEs FICHIERs //oob_air_init et //oob_air
        
        }

        private void Modifier_camp_init(string newCampaignPath)
        {          
            string pathFile = newCampaignPath + @"\Init\camp_init.lua";
            string pathFileTemp = newCampaignPath + @"\Init\camp_init_TEMP.lua";

            if (File.Exists(pathFile))
            {
                File.Copy(pathFile, pathFileTemp, true);
                using (StreamReader reader = new StreamReader(pathFileTemp))
                {
                    string line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        if (line.Contains(","))
                        {
                            string[] words = line.Split(',');
                            if (words[0].Contains("title"))
                            {
                                string ligneModifiee = "	title = \"" + CampaignName.Text + "\",		--Title of campaign (name of missions)";
                                FormUtils.ModifierLigneBis(pathFile, line, ligneModifiee);
                            }
                            else if (words[0].Contains("CampaignOriginal"))
                            {
                                string ligneModifiee = "	CampaignOriginal = false,		--initial campaign, prepared to make a cloning";
                                FormUtils.ModifierLigneBis(pathFile, line, ligneModifiee);
                            }
                        }
                    }
                }
                File.Delete(pathFileTemp);
            }
        }


        private void Modifier_ClassSquad()
        {
             // Parcourir le dictionnaire pour trouver le squad avec le nom spécifié
            foreach (var squad in List_oob_air_Manager.List_oob_air)
            {
                 if (squad.Name == CloneCampaign.SquadName)
                {
                    // Mettre à jour les propriétés Player et Inactive
                    squad.Player = true;
                    squad.Inactive = false;
                    
                }
                else
                {
                    squad.Player = false;
                }
            }
        }

        private void Modifier_oob_air_init_PlayerFalse(string newCampaignPath)
        {
            //, CloneCampaign.SquadName
            string pathFile = newCampaignPath + @"\Init\oob_air_init.lua";
            string pathFileTemp = newCampaignPath + @"\Init\oob_air_init_TEMP.lua";

            if (File.Exists(pathFile))
            {
                File.Copy(pathFile, pathFileTemp, true);
                using (StreamReader reader = new StreamReader(pathFileTemp))
                {
                    bool commentaireON = false;
                    string[] NameChapter = new string[10];
                    string line;
                    int i = 1;
                    while ((line = reader.ReadLine()) != null)
                    {                     
                        if (line.Contains("--[["))
                        {
                            commentaireON = true;
                        }
                        if (line.Contains("]]--"))
                        {
                            commentaireON = false;
                        }

                        if (!commentaireON && line.Contains("=") && line.Contains(","))
                        {
                            //cherche l'ancien player=true
                            string[] words = line.Split(',');
                            if (words[0].Contains("player") && words[0].Contains("true"))
                            {
                                string ligneModifiee = "			player = false,									--player unit TESTING";


                                FormUtils.LogRegister("Form3_Creation_Campagne Fichier oob_air_init numLigneAModier: " + i +  " (ligneModifiee): |" + ligneModifiee + "|");

                                FormUtils.ModifierLigneByNumber(pathFile, i, ligneModifiee);
                            }
                        }

                        i++;
                    }
                }
                File.Delete(pathFileTemp);
            }
        }

        private void Modifier_oob_air_init_Squad(string newCampaignPath)
        {
            //, CloneCampaign.SquadName
            string pathFile = newCampaignPath + @"\Init\oob_air_init.lua";
            string pathFileTemp = newCampaignPath + @"\Init\oob_air_init_TEMP.lua";

            if (File.Exists(pathFile))
            {
                File.Copy(pathFile, pathFileTemp, true);
                using (StreamReader reader = new StreamReader(pathFileTemp))
                {

                    bool commentaireON = false;
                    string[] NameChapter = new string[1000];
                    string line;
                    string foundSquadChapter = "";
                    string foundSPlayerChapter = "";
                    string lineSquadName = "";
                    int ChapitreProf = 0;
                    int foundSquadProf = 0;
                    int i = 1;
                    int lastPlayerLigneId = 0;
                    int squadNameLigneId = 0;
                    while ((line = reader.ReadLine()) != null)
                    {

                        bool commentaireLigne = false;

                        //if (line.IndexOf("EC 1/12") > -1)
                        //{
                        //    //bla 
                        //    commentaireLigne = commentaireLigne;
                        //}


                        
                        if (line.Contains("--[["))
                        {
                            commentaireON = true;
                            line = "";
                        }
                        if (line.Contains("]]--"))
                        {
                            commentaireON = false;
                            line = "";
                        }

                        //cherche si c'est une ligne commenté (en début de ligne ou plus loin)
                        
                        int pos = line.IndexOf("--");

                        if (pos == 0)
                        {
                            commentaireLigne = true;
                            line = "";
                        }
                        else if (pos > -1)
                        {
                            string CopyLine = line;
                            CopyLine = CopyLine.Substring(0, pos);
                            CopyLine = CopyLine.Replace("\t", "");
                            
                            line = CopyLine;
                        if (CopyLine.Length <= 0)
                            {
                                commentaireLigne = true;
                                //MessageBox.Show("Passe found commentaire " + line, "|" + CopyLine + "|");
                            }
                        }
                        
                        //debut d'une TABLE
                        //if (!commentaireLigne && !commentaireON && line.Contains("{") && line.Contains("="))
                        if ( !commentaireON && line.Contains("{") )
                        {
                            
                            ChapitreProf++;
                            if (line.Contains("="))
                            {
                                string[] words = line.Split('=');
                                NameChapter[ChapitreProf] = words[0];
                                FormUtils.LogRegister("Form3_Creation_Campagne E NameChapter[ChapitreProf]: |" + NameChapter[ChapitreProf] + "|ChapitreProf:|" + ChapitreProf + "|");

                            }
                            else
                            {
                                //NameChapter[ChapitreProf] = "inc";

                                if (NameChapter[ChapitreProf] == null || NameChapter[ChapitreProf] == "")
                                {
                                    NameChapter[ChapitreProf] = "0";

                                    FormUtils.LogRegister("Form3_Creation_Campagne F |ChapitreProf:| " + ChapitreProf + " |  NameChapter[ChapitreProf]: |" + NameChapter[ChapitreProf]);

                                }

                                //NameChapter[ChapitreProf] = NameChapter[ChapitreProf] + 1;

                                int tempNameIdChapter = Int32.Parse(NameChapter[ChapitreProf]) + 1;

                                NameChapter[ChapitreProf] = tempNameIdChapter.ToString();

                                FormUtils.LogRegister("Form3_Creation_Campagne G  |ChapitreProf:| " + ChapitreProf + " |  NameChapter[ChapitreProf]: |" + NameChapter[ChapitreProf]);

                            }
                        }


                        //Fin d'une TABLE
                        //if (!commentaireLigne && !commentaireON && line.Contains("},") )
                        if (!commentaireLigne && !commentaireON &&  line.Contains("}")) // if ( line.Contains("},"))
                        {
                            //NameChapter[ChapitreProf] = "";
                            ChapitreProf--;
                            FormUtils.LogRegister("Form3_Creation_Campagne H (ChapitreProf): |" + ChapitreProf + "| <? foundSquadProf |" + foundSquadProf);

                            //quand la fin de la table du squad est passé (si squad trouvé) on lance la modif
                            if (ChapitreProf < foundSquadProf )
                            {
                                FormUtils.LogRegister("Form3_Creation_Campagne I (foundSPlayerChapter): |" + foundSPlayerChapter + " foundSquadChapter " + foundSquadChapter);

                                if (foundSPlayerChapter == foundSquadChapter)
                                {
                                    string ligneModifiee = "			player = true,									--player unit TESTINGForm3_Creation_Campagne";

                                    FormUtils.LogRegister("Form3_Creation_Campagne K1 (lastPlayerLigneId): |" + lastPlayerLigneId + "|foundSPlayerChapter:|" + foundSPlayerChapter + "|");
                                    FormUtils.LogRegister("Form3_Creation_Campagne K2 (ligneModifiee): |" + ligneModifiee + "|");
                                    FormUtils.ModifierLigneByNumber(pathFile, lastPlayerLigneId, ligneModifiee);
                                    //createCampaign = true;
                                    break;
                                }
                                else
                                {
                                    string ligneModifiee = lineSquadName + "\r\n			player = true,									--player unit TESTINGForm3_Creation_Campagne";

                                    FormUtils.LogRegister("Form3_Creation_Campagne L1 (lastPlayerLigneId): |" + lastPlayerLigneId + "|foundSPlayerChapter:|" + foundSPlayerChapter + "|");
                                    FormUtils.LogRegister("Form3_Creation_Campagne L2 (ligneModifiee): |" + ligneModifiee + "|");
                                    FormUtils.ModifierLigneByNumber(pathFile, squadNameLigneId, ligneModifiee);
                                    //createCampaign = true;
                                    break;
                                }

                            }
                        }

                        //if (!commentaireLigne && !commentaireON && line.Contains("=") && line.Contains(","))
                        if (line.Contains("=") && line.Contains(","))
                        {
                            string[] words = line.Split(',');

                            //garde en reserve le dernier player, s'il arrive avant le nom
                            if (words[0].Contains("player") )
                            {
                                lastPlayerLigneId = i;
                                foundSPlayerChapter = NameChapter[ChapitreProf];
                                FormUtils.LogRegister("Form3_Creation_Campagne trouve PLAYER: |" + lastPlayerLigneId + "|foundSPlayerChapter:|" + foundSPlayerChapter + "|");
                            }

                            //cherche le squad
                            if (words[0].IndexOf("name") > -1 )
                            {
                                string[] wordSquad = words[0].Split('=');
                                wordSquad[1] = wordSquad[1].Replace("\"", "");
                                wordSquad[1] = wordSquad[1].Replace(",", "");
                                wordSquad[1] = wordSquad[1].TrimStart();
                                wordSquad[1] = wordSquad[1].TrimEnd();

                                FormUtils.LogRegister("Form3_Creation_Campagne cherche le squad ((wordSquad[1]): |" + wordSquad[1] + "| " + " |" + CloneCampaign.SquadName + "|");

                                if (wordSquad[1] == CloneCampaign.SquadName)
                                { 
                                    foundSquadChapter = NameChapter[ChapitreProf];
                                    squadNameLigneId = i;
                                    lineSquadName = line;
                                    foundSquadProf = ChapitreProf;
                                    FormUtils.LogRegister("Form3_Creation_Campagne trouve le squadn ((wordSquad[1]): |" + wordSquad[1] + "|");
                                    //MessageBox.Show("Passe  SQUAD trouvé \r" + line, ChapitreProf.ToString() + " " + NameChapter[ChapitreProf].ToString());
                                }
                            }
                        }
                        i++;
                    }
                }
                File.Delete(pathFileTemp);
            }
        }

        private void Modifier_camp_triggers_init(string newCampaignPath)
        {
            // CloneCampaign.SquadName
            string pathFile = newCampaignPath + @"\Init\camp_triggers_init.lua";
            string pathFileTemp = newCampaignPath + @"\Init\camp_triggers_init_TEMP.lua";

            if (File.Exists(pathFile))
            {
                File.Copy(pathFile, pathFileTemp, true);
                using (StreamReader reader = new StreamReader(pathFileTemp))
                {
                    bool commentaireON = false;
                    bool flagAction = false;
                    string[] NameChapter = new string[200];
                    string[,] ChapterActionLbL = new string[200,2];
                    string line;
                    string lasStringCondition = "";
                    string foundActionCampaignEnd = "";
                    string foundSConditionChapter = "";
                    int ChapitreProf = 0;
                    int foundCampaignEndProf = 0;
                    int foundActionProf = 0;
                    int i = 1;
                    int lastConditionLigneId = 0;
                    int[] NumTabByProfondeur = new int[400];
                    //int iDebutTable = 0;
                    int m = 2;
                    string ligneConditionVictoire = "";
                    int IdligneConditionVictoire = 0;
                    while ((line = reader.ReadLine()) != null)
                    {
                        bool commentaireLigne = false;
                        
                        if (line.Contains("--[["))
                        {
                            commentaireON = true;
                        }
                        if (line.Contains("]]--"))
                        {
                            commentaireON = false;
                            commentaireLigne = true;    //on invente un commentaireLigne pour eviter que des elements soient pris en compte, comme {, par ex
                        }


                        //cherche si c'est une ligne commenté (en début de ligne ou plus loin)
                        if (!commentaireON && !String.IsNullOrEmpty(line) && line.Length >= 2)
                        {

                            int pos = line.IndexOf("--");

                            if (pos == 0)
                            {
                                commentaireLigne = true;
                            }
                            else if (pos > -1)
                            {
                                string CopyLine = line;
                                CopyLine = CopyLine.Substring(0, pos);
                                CopyLine = CopyLine.Replace(" ", "");
                                CopyLine = CopyLine.Replace("\t", "");

                                if (CopyLine.Length <= 0)
                                {
                                    commentaireLigne = true;
                                }
                            }
                        }

                        if (!commentaireLigne && !commentaireON)
                        {

                            //Table action
                            if (!commentaireLigne && !commentaireON && flagAction == true)
                            {
                                ChapterActionLbL[m, 0] = line;
                                ChapterActionLbL[m, 1] = i.ToString();
                                m++;
                            }


                            //debut d'une TABLE
                            //if (!commentaireLigne && !commentaireON && line.Contains("{") && line.Contains("="))
                            if ( line.Contains("{"))
                            {
                                ChapitreProf++;
                                NumTabByProfondeur[ChapitreProf] = NumTabByProfondeur[ChapitreProf] + 1;
                                if (line.Contains("="))
                                {
                                    string[] words = line.Split('=');
                                    NameChapter[ChapitreProf] = words[0];
                                }
                                else
                                {
                                    NameChapter[ChapitreProf] = Convert.ToString(NumTabByProfondeur[ChapitreProf]);
                                }


                                if (line.IndexOf("action") > -1)
                                {
                                    Array.Clear(ChapterActionLbL, 0, ChapterActionLbL.Length);
                                    m = 2;

                                    flagAction = true;
                                    ChapterActionLbL[1, 0] = line;
                                    ChapterActionLbL[1, 1] = i.ToString();

                                    foundActionProf = ChapitreProf;
                                }
                            }
                            //Fin d'une TABLE
                            if ( line.Contains("}"))
                            {
                                //NameChapter[ChapitreProf] = "";
                                ChapitreProf--;

                                //quand la fin de la table CampaignEnd est passé (si CampaignEnd trouvé) on lance la modif
                                if (ChapitreProf < foundCampaignEndProf && foundSConditionChapter != "" && foundSConditionChapter == foundActionCampaignEnd)
                                {
                                    foundSConditionChapter = "";
                                    foundActionCampaignEnd = "";

                                    //condition = 'Return.AirUnitAlive("VFA-106") + Return.AirUnitReady("R/VFA-106") < 2',
                                    string[] wordsA = lasStringCondition.Split('=');
                                    string oldSquad = wordsA[1];
                                    if (oldSquad.IndexOf("+") > -1)
                                    {
                                        string[] wordsB = oldSquad.Split('+');
                                        if (wordsB[0].IndexOf("AirUnitAlive") > -1)
                                        {
                                            oldSquad = wordsB[0].Replace("Return.AirUnitAlive(", "");
                                        }
                                        else if (wordsB[1].IndexOf("AirUnitAlive") > -1)
                                        {
                                            oldSquad = wordsB[1].Replace("Return.AirUnitAlive(", "");
                                        }
                                    }

                                    if (oldSquad.IndexOf(">") > -1)
                                    {
                                        string[] wordsB = oldSquad.Split('>');
                                        oldSquad = wordsB[0];
                                    }
                                    if (oldSquad.IndexOf("<") > -1)
                                    {
                                        string[] wordsB = oldSquad.Split('<');
                                        oldSquad = wordsB[0];
                                    }


                                    oldSquad = oldSquad.Replace("Return.AirUnitAlive(", "");

                                    oldSquad = oldSquad.Replace(")", "");
                                    oldSquad = oldSquad.Replace("\"", "");
                                    oldSquad = oldSquad.Replace("'", "");

                                    oldSquad = oldSquad.TrimStart();
                                    oldSquad = oldSquad.TrimEnd();

                                    CloneCampaign.SquadName = CloneCampaign.SquadName.TrimStart();
                                    CloneCampaign.SquadName = CloneCampaign.SquadName.TrimEnd();

                                    string ligneModifiee = lasStringCondition.Replace(oldSquad, CloneCampaign.SquadName);

                                    //_form1.LogRegister("Form3 Creation Campagne Fichier triggerInit Condition(ligneModifiee): |" + ligneModifiee + "|");

                                    FormUtils.ModifierLigneByNumber(pathFile, lastConditionLigneId, ligneModifiee);

                                    ligneConditionVictoire = ligneModifiee;
                                    IdligneConditionVictoire = lastConditionLigneId;

                                    //recherche dans cette table des references texte a l'ancien squad
                                    for (int k = 1; k < (ChapterActionLbL.Length / 2) - 1; k++)
                                    {
                                        if (ChapterActionLbL[k, 0] != null && ChapterActionLbL[k, 0].IndexOf(oldSquad) > -1)
                                        {

                                            ligneModifiee = ChapterActionLbL[k, 0].Replace(oldSquad, CloneCampaign.SquadName);
                                            int LigneId = Int32.Parse(ChapterActionLbL[k, 1]);

                                            //_form1.LogRegister("Form3 Creation Campagne Fichier triggerInit Action (ligneModifiee): |" + ligneModifiee + "|");

                                            FormUtils.ModifierLigneByNumber(pathFile, LigneId, ligneModifiee);
                                        }
                                    }
                                    flagAction = false;
                                    //break;
                                }
                                // a revoir
                                //quand la fin de la table action est passé 
                                if (ChapitreProf < foundActionProf)
                                {
                                    foundActionProf = 0;
                                    flagAction = false;
                                }

                                if (flagAction)
                                {
                                    flagAction = false;
                                }
                            }
                            //cherche la ligne "condition" ou "CampaignEnd"
                            if ( line.IndexOf(",") > -1)
                            {
                                if (line.IndexOf("=") > -1)
                                {
                                    string[] words = line.Split('=');
                                    //condition = 'Return.AirUnitAlive("VFA-106") + Return.AirUnitReady("R/VFA-106") < 2',
                                    if (words[0].IndexOf("condition") > -1 && words[1].IndexOf("AirUnitAlive") > -1)
                                    {
                                        lastConditionLigneId = i;
                                        lasStringCondition = line;
                                        foundSConditionChapter = NameChapter[ChapitreProf];
                                    }
                                }


                                //[3] = 'Action.Text("Les forces alliées ont infligé des pertes énormes...
                                if (line.IndexOf("Action.") > -1)
                                {
                                    //'Action.CampaignEnd("loss")',
                                    if (line.IndexOf("CampaignEnd(\"loss") > -1)
                                    {
                                        foundActionCampaignEnd = NameChapter[ChapitreProf - 1];
                                        foundCampaignEndProf = ChapitreProf;    //ChapitreProf - 1;
                                    }
                                    ////[2] = 'Action.Text("Ongoing combat operations have exhausted 81st TFS. Loss
                                    //else if (foundActionCampaignEnd && (line.IndexOf("Action.Text") > -1))
                                    //{

                                    //}
                                }
                            }

                            //cherche la ligne Reserve pour ajouter le bon squad de reserve dans les conditions de defaite
                            //action = 'Action.AirUnitReinforce("82 TFS", "81 TFS", 4)',
                            if (line.IndexOf(CloneCampaign.SquadName) > -1 &&  line.IndexOf("AirUnitReinforce") > -1)
                            {
                                string[] wordsA = line.Split('(');
                                string[] wordsB = wordsA[1].Split(')');
                                string[] wordsC = wordsB[0].Split(',');
                                string reserveSquad = wordsC[0];
                                
                                reserveSquad = reserveSquad.Replace("\"", "");

                                reserveSquad = reserveSquad.TrimStart();
                                reserveSquad = reserveSquad.TrimEnd();

                                //condition = 'Return.AirUnitAlive("81 TFS") + Return.AirUnitReady("82 TFS") < 4',
                                //ou
                                //condition = 'Return.AirUnitAlive("81 TFS") < 2',

                                if (line.IndexOf("+") > -1)
                                {
                                    string[] victoryA = ligneConditionVictoire.Split('+');
                                    string[] victoryB = victoryA[1].Split(')');

                                    ligneConditionVictoire = victoryA[0] + "+ Return.AirUnitReady(\"" + reserveSquad + "\")" + victoryB[1];

                                    if (ligneConditionVictoire != "" && IdligneConditionVictoire != 0)
                                    {
                                        FormUtils.ModifierLigneByNumber(pathFile, IdligneConditionVictoire, ligneConditionVictoire);
                                    }
                                }
                                else
                                {

                                    //ligneConditionVictoire = ligneConditionVictoire;
                                    //condition = 'Return.AirUnitAlive("81 TFS") < 2',
                                    if (ligneConditionVictoire != "" && IdligneConditionVictoire != 0)
                                    {
                                        FormUtils.ModifierLigneByNumber(pathFile, IdligneConditionVictoire, ligneConditionVictoire);
                                    }
                                }


                            }

                        }
                        i++;
                    }
                }
                File.Delete(pathFileTemp);
            }
        }

        private void Modifier_CMD(string newCampaignPath)
        {
            //MessageBox.Show("newCampaignPath: "+ newCampaignPath.ToString());

            //, CloneCampaign.SquadName
            string pathFile = newCampaignPath + ".cmp";
            string pathFileTemp = newCampaignPath + "_TMP.cmp";

            if (File.Exists(pathFile))
            {
                File.Copy(pathFile, pathFileTemp, true);
                using (StreamReader reader = new StreamReader(pathFileTemp))
                {
                    bool commentaireON = false;
                    string[] NameChapter = new string[20];
                    string line;
                    bool foundModuleChapter = false;
                    bool foundModuleChapterTotal = false;
                    int i = 1;
                    while ((line = reader.ReadLine()) != null)
                    {

                        bool commentaireLigne = false;
                        if (line.Contains("--[["))
                        {
                            commentaireON = true;
                        }
                        if (line.Contains("]]--"))
                        {
                            commentaireON = false;
                        }

                        //cherche si c'est une ligne commenté (en début de ligne ou plus loin)
                        if (!String.IsNullOrEmpty(line) && line.Length >= 2)
                        {
                            int pos = line.IndexOf("--");

                            if (pos == 0)
                            {
                                commentaireLigne = true;
                            }
                            else if (pos > -1)
                            {
                                string CopyLine = line;
                                CopyLine = CopyLine.Substring(0, pos);
                                CopyLine = CopyLine.Replace(" ", "");
                                CopyLine = CopyLine.Replace("\t", "");

                                if (CopyLine.Length <= 0)
                                {
                                    commentaireLigne = true;
                                }
                                else //supprime la partie de la ligne commenté, pour qu'elle ne pollue pas les variables
                                {
                                    string first = line.Substring(0, pos);
                                    line = first;
                                }
                            }
                        }

                        if (!commentaireLigne && !commentaireON)
                        {
                            //cherche le core module avion 
                            if (foundModuleChapterTotal)
                            {
                                //string ligneModifiee = "        [\"FA-18C\"] = \"FA-18C\",";
                                //string ligneModifiee = "        [\"FA-14\"] = \"FA-14\",";

                                string ligneModifiee = "";


                                //MessageBox.Show("Trouve module avion et change ligne");
                                FormUtils.ModifierLigneByNumber(pathFile, i, ligneModifiee);
                                foundModuleChapter = false;
                                foundModuleChapterTotal = false;
                            }
                            //cherche fin de chapitre module avion
                            if (foundModuleChapterTotal && (line.IndexOf("},") > -1))
                            {
                                foundModuleChapter = false;
                                foundModuleChapterTotal = false;
                            }
                            //cherche fin de chapitre module avion
                            if (foundModuleChapter && (line.IndexOf("{") > -1))
                            {
                                foundModuleChapterTotal = true;
                            }
                            //cherche le module avion
                            if (line.Contains("necessaryUnits"))
                            {
                                //TODO enlever le texte commentaire, il pollue s'il a necessaryUnits
                                foundModuleChapter = true;
                            }

                            //cherche l'ancien name of campaign
                            if (line.IndexOf(CloneCampaign.OldNameCamp) > -1)
                            {
                                string ligneModifiee = line.Replace(CloneCampaign.OldNameCamp, CampaignName.Text);

                                //_form1.LogRegister("Form3 Creation Campagne Fichier.cmp (ligneModifiee): |" + ligneModifiee + "|");
                                FormUtils.ModifierLigneByNumber(pathFile, i, ligneModifiee);
                            }
                        }
                        i++;
                    }
                }
                File.Delete(pathFileTemp);
            }
        }

        private void button_clone_Click(object sender, EventArgs e)
        {
            
            if (System.IO.Directory.Exists(CloneCampaign.path))
            {
                string path = CloneCampaign.path;

                string OldNameCamp = CloneCampaign.OldNameCamp;
                CampaignName.Text = CampaignName.Text.TrimStart();
                CampaignName.Text = CampaignName.Text.TrimEnd();
                string NewdNameCamp = CampaignName.Text;
                NewdNameCamp = NewdNameCamp.Replace("/", "_");

                string sourcePath = path + @"\" + OldNameCamp;
                string targetPath = path + @"\" + NewdNameCamp;
               
                //evite l'écrasement d'une campagne déjà existante:
                if (System.IO.Directory.Exists(targetPath))
                {
                    MessageBox.Show("Already existing campaign", "Attention");
                }
                else
                {
                    _form1.CopyFilesRecursively(sourcePath, targetPath);
                    //Crisis in PG-Hornet-CVN.cmp
                    sourcePath = path + @"\" + OldNameCamp + ".cmp";
                    targetPath = path + @"\" + NewdNameCamp + ".cmp";

                    //try
                    //{
                    //    File.Copy(SourcePath, targetPath, true); ;
                    //}
                    //catch (Exception ex)
                    //{
                    //    //listbox1.Items.Add("Unable to Copy file. Error : " + ex);
                    //    MessageBox.Show(ex.StackTrace.ToString());
                    //}

                    try
                    {
                        // Copier le fichier
                        File.Copy(sourcePath, targetPath, true);

                        // Attendre un moment pour s'assurer que l'opération de copie est terminée
                        Thread.Sleep(500); // Vous pouvez ajuster cette valeur en fonction de vos besoins

                        // Vérifier l'accès au fichier avant de l'éditer
                        if (FormUtils.IsFileReady(targetPath))
                        {
                            // Éditer le fichier ici
                            //MessageBox.Show("File is ready for editing.");
                            // Par exemple, vous pouvez ouvrir et modifier le fichier
                            //using (StreamWriter writer = new StreamWriter(targetPath, true))
                            //{
                            //    writer.WriteLine("New content added.");
                            //}
                        }
                        else
                        {
                            MessageBox.Show("File is not ready for editing.");
                        }
                    }
                    catch (Exception ex)
                    {
                        // Le fichier n'est pas prêt
                        Console.WriteLine("File is not ready: " + ex.Message);
                        //return false;
                    }

                    //Crisis in PG-Hornet-CVN_first.miz
                    sourcePath = path + @"\" + OldNameCamp + "_first.miz";
                    targetPath = path + @"\" + NewdNameCamp + "_first.miz";
                    File.Copy(sourcePath, targetPath, true);

                    //Crisis in PG - Hornet - CVN_ongoing.miz
                    sourcePath = path + @"\" + OldNameCamp + "_ongoing.miz";
                    targetPath = path + @"\" + NewdNameCamp + "_ongoing.miz";
                    File.Copy(sourcePath, targetPath, true);

                    //Crisis in PG-Hornet-CVN.png
                    sourcePath = path + @"\" + OldNameCamp + ".png";
                    targetPath = path + @"\" + NewdNameCamp + ".png";
                    File.Copy(sourcePath, targetPath, true);

                    //creation du Dic Class squad***
                    CreateDicoClassSquad(_form1, path + @"\" + OldNameCamp, OldNameCamp);

                    //Modifier camp_init***
                    Modifier_camp_init(path + @"\" + NewdNameCamp);

                    //modifie dans Class Squad : Player, Squad, Active_Squad
                    Modifier_ClassSquad();

                    ////modifie player dans oob_air_init
                    //Modifier_oob_air_init_PlayerFalse(path + @"\" + NewdNameCamp);

                    ////modifie le squad dans oob_air_init
                    //Modifier_oob_air_init_Squad(path + @"\" + NewdNameCamp);

                    //modifie le squad dans les conditions de defaite dans camp_triggers_init
                    Modifier_camp_triggers_init(path + @"\" + NewdNameCamp);

                    //modifie le name campaign dans le fichier .cmd
                    Modifier_CMD(path + @"\" + NewdNameCamp);

                    //ecrit oob_air_init, a partir du DicClassSquad ***
                    FormUtils.WriteListClassSquadsToFile(path + @"\" + NewdNameCamp + @"\Init\oob_air_init.lua", "Init" );

                    //Suppression des fichiers dans Active:
                    FormUtils.DeleteAllFilesInDirectory(path + @"\" + NewdNameCamp + @"\Active", false);
                    //Suppression des fichiers dans Debriefing:
                    FormUtils.DeleteAllFilesInDirectory(path + @"\" + NewdNameCamp + @"\Debriefing", false);
                    //Suppression des fichiers dans Debug:
                    FormUtils.DeleteAllFilesInDirectory(path + @"\" + NewdNameCamp + @"\Debug", false);
                    
                    this.Close();
                }              
            }
        }

        private void comboPlaneChoice_SelectedIndexChanged(object sender, EventArgs e)
        {
            string tempTXT = comboPlaneChoice.SelectedItem.ToString();
            string[] words = tempTXT.Split('|');
            planeFIX.Text = words[0].Replace(" ", "");

            //_form1.LogRegister("debut C words ( words[1]): |" + words[1] + "|");

            words[1] = words[1].TrimStart();
            words[1] = words[1].TrimEnd();

            SquadName.Text = words[1];

            CloneCampaign.SquadName = SquadName.Text;

            BaseName.Text = words[2];

            CampaignName.Text = CloneCampaign.OldNameCamp + "-" + planeFIX.Text + "-" + SquadName.Text;
        }

        private void Form3_Load(object sender, EventArgs e)
        {

        }

        private void CampaignName_TextChanged(object sender, EventArgs e)
        {
        }

        private void planeFIX_TextChanged(object sender, EventArgs e)
        {

        }
    }
}
