using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using DCE_Manager.Parameters;
using NLua;
using static System.Windows.Forms.AxHost;
using static DCE_Manager.Utils.FormUtils;


namespace DCE_Manager
{

    //internal class CampaignEdit
    public partial class CampaignEdit : Form
    {
        Form1 _form1;


        static object cachedLuaResult = null;


        private void LoadGrid(DataGridView grid, List<Squad> squads, string side, string state)
        {
            var filtered = squads
                .Where(s => s.SideString == side && s.FolderFile == state)
                .ToList();

            grid.Columns.Clear();
            grid.AutoGenerateColumns = false;
            grid.DataSource = null;

            // --- ACTIVE ---
            grid.Columns.Add(new DataGridViewCheckBoxColumn()
            {
                HeaderText = "Actif",
                DataPropertyName = "IsActive",
                Width = 50
            });

            // --- PLAYER ---
            grid.Columns.Add(new DataGridViewCheckBoxColumn()
            {
                HeaderText = "Player",
                DataPropertyName = "Player",
                Width = 50
            });

            // --- NAME ---
            grid.Columns.Add(new DataGridViewTextBoxColumn()
            {
                HeaderText = "Name",
                DataPropertyName = "Name",
                Width = 140
            });

            // --- TYPE ---
            grid.Columns.Add(new DataGridViewTextBoxColumn()
            {
                HeaderText = "Type",
                DataPropertyName = "Type",
                Width = 80
            });

            // --- BASE ---
            grid.Columns.Add(new DataGridViewTextBoxColumn()
            {
                HeaderText = "Base",
                DataPropertyName = "Base",
                Width = 120
            });

            // --- READY ---
            grid.Columns.Add(new DataGridViewTextBoxColumn()
            {
                HeaderText = "Ready",
                DataPropertyName = "DisplayReady",
                Width = 60
            });

            // --- RESERVE ---
            grid.Columns.Add(new DataGridViewTextBoxColumn()
            {
                HeaderText = "Reserve",
                DataPropertyName = "DisplayReserve",
                Width = 60
            });

            // --- CLONE BUTTON ---
            grid.Columns.Add(new DataGridViewButtonColumn()
            {
                HeaderText = "+",
                Text = "+",
                UseColumnTextForButtonValue = true,
                Width = 40
            });

            // --- DELETE BUTTON ---
            grid.Columns.Add(new DataGridViewButtonColumn()
            {
                HeaderText = "X",
                Text = "X",
                UseColumnTextForButtonValue = true,
                Width = 40
            });

            grid.DataSource = filtered;
        }

        // 🔹 2. CONSTRUCTEUR
        public CampaignEdit(Form1 form1, string nameCamp)
        {
            //InitializeComponent();

            var time_LuaPart = Stopwatch.StartNew();

            // 👉 ton parsing ici

            form1.tabPage7.Controls.Clear();
            form1.tabPage8.Controls.Clear();
            form1.tabPage9.Controls.Clear();
            form1.tabPage10.Controls.Clear();

            form1.dataGridViewBlue.CellContentClick += Grid_CellClick;
            form1.dataGridViewRed.CellContentClick += Grid_CellClick;

            PublicTable.errorTable.Clear();
            form1.textBox_Bugs.Text = "";
            form1.tabPage12.Text = "Bugs";

            //Pour représenter la table Lua donnée en C#, il est généralement préférable d'utiliser une structure de données 
            //qui offre la flexibilité de stocker des paires clé-valeur, similaire aux tables en Lua. 
            //    En C#, le type de données le plus approprié pour cela est le Dictionary<TKey, TValue>.

            // Assurez-vous d'appeler UpdateSharedData avant d'ouvrir Form3_Clonage
            form1.UpdateSharedData();

            Lua lua = new Lua();

            lua["versionPackageICM"] = "NG";
            lua["pathScriptsMod"] = SharedData.textBox_SavedGames + @"\Mods\tech\DCE\ScriptsMod.NG";
            lua["pathCampaign"] = SharedData.textBox_SavedGames + @"\Mods\tech\DCE\Missions\Campaigns\" + nameCamp;
            lua["generator"] = "DCE_Manager"; 
            lua["pathSavedGames"] = SharedData.textBox_SavedGames;
            // Crée la table Debug avec la clé "debug" à false
            var debugTable = new Dictionary<string, object>
            {
                { "debug", false }
            };
            lua["Debug"] = debugTable;


            //lua.DoFile(SharedData.textBox_SavedGames + @"\Mods\tech\DCE\ScriptsMod.NG\DCEM_Function.lua");

            //var result = lua.DoFile(SharedData.textBox_SavedGames + @"\Mods\tech\DCE\ScriptsMod.NG\DCEM_Function.lua");
            object[] result;

            if (cachedLuaResult == null)
            {
                cachedLuaResult = lua.DoFile(SharedData.textBox_SavedGames + @"\Mods\tech\DCE\ScriptsMod.NG\DCEM_Function.lua");
            }

            result = (object[])cachedLuaResult;

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

            // Charger le tableau playable_m
            LuaTable playable_mLua = (LuaTable)luaTable["Playable_m"];
            List<string> playableList = new List<string>();

            // Parcourir la table Lua
            foreach (var plane in playable_mLua.Keys)
            {
                playableList.Add(plane.ToString());
            }

            time_LuaPart.Stop();
            LogRegister($"time_LuaPart : {time_LuaPart.ElapsedMilliseconds} ms");

            //*************************************************************************
            // CREATION DES TABLES BASE (VERSION NLua)
            //*************************************************************************
            var time_airbase = Stopwatch.StartNew();
            string savedGamesPath = SharedData.textBox_SavedGames;

            // Reset
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
                else
                    pathAirbase = Path.Combine(savedGamesPath, @"Mods\tech\DCE\Missions\Campaigns\", nameCamp, @"Active\db_airbases.lua");

                if (!File.Exists(pathAirbase))
                    continue;

                ParamCampaign.NameFileParse = pathAirbase;

                var luaObj = LuaParser.ParseFile(pathAirbase, "db_airbases");

                string state = d == 1 ? "Init" : "Active";

                foreach (var entry in (Dictionary<string, LuaObject>)luaObj.luaobj)
                {
                    var airbase = entry.Value.luaobj as Dictionary<string, LuaObject>;

                    foreach (var sub in airbase)
                    {
                        if (sub.Key == "side" || sub.Key == "coalition")
                        {
                            var val = sub.Value.luaobj?.ToString();

                            if (val == "red")
                                PublicTable.Airdrome[state]["red"].Add(entry.Key);
                            else if (val == "blue")
                                PublicTable.Airdrome[state]["blue"].Add(entry.Key);
                        }
                    }
                }
            }
            time_airbase.Stop();
            LogRegister($"time_airbase : {time_airbase.ElapsedMilliseconds} ms");


            if (ParamDivers.NewParseOobAir)
            {
                var sw = Stopwatch.StartNew();
                var time_ParseOobAir = Stopwatch.StartNew();

                IDictionary<int, string> path_oob_air = new Dictionary<int, string>();
                string path_oob_airFile = SharedData.textBox_SavedGames + @"\Mods\tech\DCE\Missions\Campaigns\" + nameCamp + @"\Init\oob_air_init.lua";
                path_oob_air.Add(1, path_oob_airFile); //adding a key/value using the Add() method

                path_oob_airFile = SharedData.textBox_SavedGames + @"\Mods\tech\DCE\Missions\Campaigns\" + nameCamp + @"\Active\oob_air.lua";
                path_oob_air.Add(2, path_oob_airFile);


                //***************************------------------


                List_oob_air_Manager.List_oob_air = new List<Squad>();


                //on compte tous les squads differement, init ou active, sinon ça fout le bordel
                int idSquad = -1;
                var uiBatch = new UIBatchBuilder();

                var allSquads = new List<Squad>();

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

                    //LuaParse C_oobAir = new LuaParse();
                    string pathFileB = path_oob_air[d];
                    
                    if (File.Exists(pathFileB))
                    {
                        ParamCampaign.NameFileParse = pathFileB;

                        //C_oobAir.Parse(File.ReadAllText(pathFileB));
                        var luaObj = LuaParser.ParseFile(pathFileB, "oob_air"); // "oob_air" ou "oob_air_init"
                       //var root = luaObj.luaobj as Dictionary<string, LuaObject>;

                        var root = luaObj.luaobj as Dictionary<string, LuaObject>;
                        if (root == null) continue;

                        foreach (var entry in root) // side
                        {
                            string side = entry.Key;
                            int sideInt = side == "blue" ? 1 : 2;

                            var level1 = entry.Value.luaobj as Dictionary<string, LuaObject>;
                            if (level1 == null) continue;

                            foreach (var entry2 in level1) // squad
                            {
                                idSquad++;

                                var squad = new Squad
                                {
                                    SideString = side,
                                    IdSquad = idSquad,
                                    FolderFile = folderFile,
                                };

                                List_oob_air_Manager.List_oob_air.Add(squad);
                                allSquads.Add(squad);


                                var level2 = entry2.Value.luaobj as Dictionary<string, LuaObject>;
                                if (level2 == null) continue;

                                foreach (var entry3 in level2) // propriétés squad
                                {
                                    var key = entry3.Key;
                                    var valObj = entry3.Value.luaobj;

                                    // ⚡ SWITCH = beaucoup + rapide que 30 if
                                    switch (key)
                                    {
                                        case "name": squad.Name = valObj?.ToString(); break;
                                        case "player": squad.Player = Convert.ToBoolean(valObj); break;
                                        case "type": squad.Type = valObj?.ToString(); break;
                                        case "country": squad.Country = valObj?.ToString(); break;
                                        case "skill": squad.Skill = valObj?.ToString(); break;
                                        case "base": squad.Base = valObj?.ToString(); break;

                                        case "baseAlternative":
                                            if (valObj is Dictionary<string, LuaObject> baseAlt)
                                            {
                                                squad.BaseAlternative = new List<string>(baseAlt.Count);
                                                foreach (var e in baseAlt)
                                                    squad.BaseAlternative.Add(e.Value.luaobj.ToString().Trim('"'));
                                            }
                                            break;

                                        case "number": squad.Number = Convert.ToInt32(valObj); break;
                                        case "InitNumber": squad.InitNumber = Convert.ToInt32(valObj); break;
                                        case "reserve": squad.Reserve = Convert.ToInt32(valObj); break;
                                        case "InitReserve": squad.InitReserve = Convert.ToInt32(valObj); break;

                                        case "tasks":
                                            if (valObj is Dictionary<string, LuaObject> tasks)
                                            {
                                                squad.Tasks = new Dictionary<string, object>(tasks.Count);
                                                foreach (var e in tasks)
                                                    squad.Tasks[e.Key] = Convert.ToBoolean(e.Value.luaobj);
                                            }
                                            break;

                                        case "tasksCoef":
                                            if (valObj is Dictionary<string, LuaObject> tasksCoef)
                                            {
                                                squad.TasksCoef = new Dictionary<string, object>(tasksCoef.Count);
                                                foreach (var e in tasksCoef)
                                                {
                                                    if (double.TryParse(e.Value.luaobj.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out double v))
                                                        squad.TasksCoef[e.Key] = v;
                                                }
                                            }
                                            break;

                                        case "inactive":
                                            squad.Inactive = Convert.ToBoolean(valObj);
                                            break;

                                        case "roster":
                                            if (valObj is Dictionary<string, LuaObject> roster)
                                            {
                                                squad.Roster = new Dictionary<string, object>(roster.Count);
                                                foreach (var e in roster)
                                                    squad.Roster[e.Key] = Convert.ToInt32(e.Value.luaobj);
                                            }
                                            break;

                                        case "score":
                                            if (valObj is Dictionary<string, LuaObject> score)
                                            {
                                                squad.Score = new Dictionary<string, object>(score.Count);
                                                foreach (var e in score)
                                                    squad.Score[e.Key] = Convert.ToInt32(e.Value.luaobj);
                                            }
                                            break;

                                        default:
                                            // ⚡ ultra important : éviter GetType() (lent)
                                            if (valObj is Dictionary<string, LuaObject> sub)
                                            {
                                                var dict = new Dictionary<string, object>(sub.Count);
                                                foreach (var e in sub)
                                                    dict[e.Key] = e.Value.luaobj;

                                                squad.AdditionalProperties[key] = dict;
                                            }
                                            else
                                            {
                                                squad.AdditionalProperties[key] = valObj;
                                            }
                                            break;
                                    }
                                }
                            }
                        }

                    }// fileExist()
                }
                time_ParseOobAir.Stop();
                LogRegister($"Time time_ParseOobAir: {time_ParseOobAir.ElapsedMilliseconds} ms");

                var time_UI = Stopwatch.StartNew();

                //// PASS 1 = COLONNE GAUCHE
                //foreach (var squad in allSquads)
                //{
                //    BuildUI(form1, squad, true); // gauche
                //}

                //// PASS 2 = COLONNE DROITE
                //foreach (var squad in allSquads)
                //{
                //    BuildUI(form1, squad, false); // droite
                //}


                this.LoadGrid(form1.dataGridViewBlue, allSquads, "blue", "Init");
                this.LoadGrid(form1.dataGridViewRed, allSquads, "red", "Init");

                time_UI.Stop();
                LogRegister($"Time UI: {time_UI.ElapsedMilliseconds} ms");




                IDictionary<int, string> pathOobAir = new Dictionary<int, string>();
                string pathFile = SharedData.textBox_SavedGames + @"\Mods\tech\DCE\Missions\Campaigns\" + nameCamp + @"\Init\oob_air_init.lua";
                pathOobAir.Add(1, pathFile); //adding a key/value using the Add() method

                pathFile = SharedData.textBox_SavedGames + @"\Mods\tech\DCE\Missions\Campaigns\" + nameCamp + @"\Active\oob_air.lua";
                pathOobAir.Add(2, pathFile);


                form1.CampaignTab.Text = "";
                form1.groupBoxCampEdit.Text = nameCamp;
                form1.CampaignTab.Visible = true;

                for (int d = 1; d <= 2; d++)
                {
                    pathFile = pathOobAir[d];

                    string[,,,] TEMPtableOobAirAAA = new string[3, 100, 100, 4];



                    if (ParamDivers.NewParseOobAir == false)
                    {

                    }

                    //*************************************************************************
                    // RECUPERATION Briefing Campaign (VERSION NLua)
                    //*************************************************************************

                    string campTrigger = SharedData.textBox_SavedGames + @"\Mods\tech\DCE\Missions\Campaigns\" + nameCamp + @"\Init\camp_triggers_init.lua";

                    ParamCampaign.NameFileParse = campTrigger;

                    string BriefingCampaign = "";

                    var luaObj = LuaParser.ParseFile(campTrigger, "camp_triggers");

                    var root = luaObj.luaobj as Dictionary<string, LuaObject>;

                    if (root != null)
                    {
                        foreach (var entry1 in root)
                        {
                            Dictionary<string, LuaObject> level1 = null;

                            // ✅ CAS 1 : clé = "Campaign Briefing"
                            if (entry1.Key.Equals("Campaign Briefing", StringComparison.OrdinalIgnoreCase))
                            {
                                level1 = entry1.Value.luaobj as Dictionary<string, LuaObject>;
                            }
                            else
                            {
                                // ✅ CAS 2 : table avec champ name = "Campaign Briefing"
                                var tmp = entry1.Value.luaobj as Dictionary<string, LuaObject>;

                                if (tmp != null && tmp.ContainsKey("name"))
                                {
                                    var nameVal = tmp["name"].luaobj?.ToString();

                                    if (nameVal != null &&
                                        nameVal.Equals("Campaign Briefing", StringComparison.OrdinalIgnoreCase))
                                    {
                                        level1 = tmp;
                                    }
                                }
                            }

                            // Si pas le bon trigger → skip
                            if (level1 == null)
                                continue;

                            // 🔽 Traitement identique ensuite
                            if (level1.ContainsKey("action"))
                            {
                                var actions = level1["action"].luaobj as Dictionary<string, LuaObject>;

                                if (actions == null)
                                    continue;

                                foreach (var entry3 in actions)
                                {
                                    string val = entry3.Value.luaobj?.ToString();

                                    if (string.IsNullOrEmpty(val))
                                        continue;

                                    if (val.Contains("Action.AddImage"))
                                        break;

                                    // 🔥 Extraction propre
                                    if (val.Contains("Action.Text"))
                                    {
                                        int start = val.IndexOf("Action.Text(");
                                        if (start >= 0)
                                        {
                                            start += "Action.Text(".Length;

                                            int end = val.LastIndexOf(")");
                                            if (end > start)
                                            {
                                                string content = val.Substring(start, end - start).Trim();

                                                if ((content.StartsWith("\"") && content.EndsWith("\"")) ||
                                                    (content.StartsWith("'") && content.EndsWith("'")))
                                                {
                                                    content = content.Substring(1, content.Length - 2);
                                                }

                                                BriefingCampaign += content + "\r\n";
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }

                    // Nettoyage (identique à ton code)
                    BriefingCampaign = BriefingCampaign
                        .Replace("'Action.Text(\"", "")
                        .Replace("\")'", "\r\n")
                        .Replace("\"", "\r\n");

                    BriefingCampaign = Regex.Replace(BriefingCampaign, @"\[.\]=", "");

                    form1.textBoxCampBriefing.Text = BriefingCampaign;


                    //*************************************************************************
                    //IMAGE Campagne **********************************************************
                    // ************************************************************************
                    //*************************************************************************

                    //string campPath = SharedData.textBox_SavedGames + @"\Mods\tech\DCE\Missions\Campaigns\" + nameCamp;
                   
                    //string imagePath = campPath + ".bmp";
                    //using (Image image = Image.FromFile(imagePath, true))
                    //{
                    //    form1.pictureBoxCampImage.Image?.Dispose();
                    //    form1.pictureBoxCampImage.Image = new Bitmap(image);
                    //}

                    //**
                    string basePath = SharedData.textBox_SavedGames + @"\Mods\tech\DCE\Missions\Campaigns\" + nameCamp;

                    string bmpPath = basePath + ".bmp";
                    string pngPath = basePath + ".png";

                    string imagePath = null;

                    // priorité au PNG (plus moderne)
                    if (File.Exists(pngPath))
                    {
                        imagePath = pngPath;
                    }
                    else if (File.Exists(bmpPath))
                    {
                        imagePath = bmpPath;
                    }

                    // si une image existe
                    if (imagePath != null)
                    {
                        using (Image image = Image.FromFile(imagePath))
                        {
                            form1.pictureBoxCampImage.Image?.Dispose();
                            form1.pictureBoxCampImage.Image = new Bitmap(image);
                        }
                    }
                    else
                    {
                        // aucune image → on nettoie
                        form1.pictureBoxCampImage.Image?.Dispose();
                        form1.pictureBoxCampImage.Image = null;
                    }
                    //**


                    //*************************************************************************
                    //AFFICHAGE DU RESULTAT OOB AIR *******************************************
                    //*************************************************************************
                    if (ParamDivers.NewParseOobAir == false)
                    {

                    }

                }

                sw.Stop();
                //MessageBox.Show($"Temps d'exécution : {sw.ElapsedMilliseconds} ms", "Performance");
                LogRegister($"TimePARSE LEs FICHIERs //oob_air_init et //oob_air +++ : {sw.ElapsedMilliseconds} ms");

            }   ////__FIN__PARSE LEs FICHIERs //oob_air_init et //oob_air

            var time_errorTable = Stopwatch.StartNew();
            string msg = "";
            foreach (KeyValuePair<string, string> kvp in PublicTable.errorTable)
            {
                msg = msg + kvp.Value + "\r\n";
                //Console.WriteLine("Key: {0}, Value: {1}", kvp.Key, kvp.Value);
            }
            if (msg != "")
            {
                int count1 = PublicTable.errorTable.Count;
                form1.textBox_Bugs.Text = msg;
                form1.tabPage12.Text = form1.tabPage12.Text + count1.ToString();
            }
            time_errorTable.Stop();
            LogRegister($"time_errorTable : {time_errorTable.ElapsedMilliseconds} ms");
        }

        private void Grid_CellClick(object sender, DataGridViewCellEventArgs e)
        {
            var grid = sender as DataGridView;
            if (e.RowIndex < 0) return;

            var squad = grid.Rows[e.RowIndex].DataBoundItem as Squad;
            if (squad == null) return;

            // colonne clone
            if (e.ColumnIndex == 7)
            {
                MessageBox.Show($"Clone {squad.Name}");
            }

            // colonne delete
            if (e.ColumnIndex == 8)
            {
                MessageBox.Show($"Delete {squad.Name}");
            }
        }

    }
}