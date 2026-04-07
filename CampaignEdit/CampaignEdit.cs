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
using DCE_Manager.Utils;
using NLua;
using static System.Windows.Forms.AxHost;
using static DCE_Manager.Utils.FormUtils;
//using static DCE_Manager.OobAirParser;
//using static DCE_Manager.CampaignLuaLoader;
//using static DCE_Manager.FormSquadEdit;

//namespace DCE_Manager
//{
//    public partial class CampaignEdit : Form
//    {
//        // 1. champs privés

//        // 2. propriétés publiques

//        // 3. constructeur

//        // 4. méthodes d'initialisation

//        // 5. méthodes de chargement des données

//        // 6. méthodes DataGridView

//        // 7. méthodes utilitaires
//    }
//}

namespace DCE_Manager
{

    public partial class CampaignEdit : Form
    {
        private readonly Form1 _form1;
        private readonly string _campaignName;

        private static object _cachedLuaResult = null;
        private static List<string> _playableList = new List<string>();

        private Dictionary<string, Dictionary<string, bool>> _taskByPlane =
            new Dictionary<string, Dictionary<string, bool>>();

        private void ResetUi() { }
        private void LoadAirbases() { }
        private void DisplayErrors() { }


        public List<Squad> CurrentSquads { get; private set; } = new List<Squad>();
        public string BriefingCampaign { get; set; }


        // 3. constructeur
        public CampaignEdit(Form1 form1, string campaignName)
        {
            _form1 = form1;
            _campaignName = campaignName;

            InitializeGridEvents();
            this.StartPosition = FormStartPosition.CenterParent;
            this.WindowState = FormWindowState.Normal;
            this.Size = new Size(1200, 850);

            InitializeCampaign();
        }


        // 4. méthodes d'initialisation
        private void InitializeCampaign()
        {
            _form1.UpdateSharedData();

            ResetUi();
            LoadLuaData();
            LoadAirbases();
            LoadSquads();
            LoadTrigger();
            SetCampaignImage();
            DisplayErrors();


        }

        // 4. méthodes d'initialisation
        private void InitializeGridEvents()
        {
            
            _form1.CampaignTab.Text = "";
            _form1.groupBoxCampEdit.Text = _campaignName;
            _form1.CampaignTab.Visible = true;
            RegisterGrid(_form1.dataGridViewBlue);
            RegisterGrid(_form1.dataGridViewRed);
        }

        private void RegisterGrid(DataGridView grid)
        {
            //grid.CellContentClick += Grid_CellClick;
            grid.CellMouseDown += Grid_CellMouseDown;
            grid.RowHeaderMouseClick += Grid_RowHeaderMouseClick;
            grid.CellValueChanged += Grid_CellValueChanged;
            grid.CurrentCellDirtyStateChanged += Grid_CurrentCellDirtyStateChanged;
            grid.DataError += Grid_DataError;
            grid.CellFormatting += Grid_CellFormatting;

        }

        private void LoadLuaData()
        {
            var loader = new CampaignLuaLoader();
            loader.Load(_campaignName);

            _playableList.Clear();
            _playableList.AddRange(loader.PlayableAircraft);

            _taskByPlane = loader.TaskByPlane;
        }

        private void LoadSquads()
        {

            var parser = new OobAirParser();

            //remplissage de la grille squad
            CurrentSquads = parser.LoadCampaignSquads(_campaignName);

            _form1.currentSquads = CurrentSquads;
            _form1.RefreshGrids();
        }


        // Cette fonction charge le briefing depuis camp_triggers_init.lua.
        // Pourquoi : le parser retourne maintenant simplement une chaîne.
        private void LoadTrigger()
        {
            var parser = new TriggerParser();

            BriefingCampaign = parser.LoadBriefingCampaign(_campaignName);

            SetBriefingText(BriefingCampaign);
        }

        // Cette fonction affiche le briefing dans la textbox.
        // Pourquoi : on force un texte vide plutôt qu'un null.
        private void SetBriefingText(string txt)
        {
            _form1.textBoxCampBriefing.Text = txt ?? "";
        }

        private void SetCampaignImage()
        {
            string basePath = SharedData.textBox_SavedGames + @"\Mods\tech\DCE\Missions\Campaigns\" + _campaignName;

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
                    _form1.pictureBoxCampImage.Image?.Dispose();
                    _form1.pictureBoxCampImage.Image = new Bitmap(image);
                }
            }
            else
            {
                // aucune image → on nettoie
                _form1.pictureBoxCampImage.Image?.Dispose();
                _form1.pictureBoxCampImage.Image = null;
            }

        }

 
        public static void LoadGridStatic(DataGridView grid, List<Squad> squads, string side, string state)
        {

            var filtered = squads
                .Where(s => s.SideString == side && s.FolderFile == state)
                .ToList();

            grid.Columns.Clear();
            grid.AutoGenerateColumns = false;
            grid.DataSource = null;
            grid.ScrollBars = ScrollBars.Both;

            grid.Columns.Add(new DataGridViewCheckBoxColumn()
            {
                HeaderText = "Actif",
                DataPropertyName = "IsActive",
                Width = 50,
                Name = "ActifColumn"
            });

            // Colonne Player
            var playerColumn = new DataGridViewCheckBoxColumn()
            {
                HeaderText = "Player",
                DataPropertyName = "Player",
                Width = 50,
                Name = "PlayerColumn"
            };
            grid.Columns.Add(playerColumn);

            grid.Columns.Add(new DataGridViewTextBoxColumn()
            {
                HeaderText = "Name",
                DataPropertyName = "Name",
                Width = 140
            });
            grid.Columns.Add(new DataGridViewTextBoxColumn()
            {
                HeaderText = "Type",
                DataPropertyName = "Type",
                Width = 80
            });
            grid.Columns.Add(new DataGridViewTextBoxColumn()
            {
                HeaderText = "Base",
                DataPropertyName = "Base",
                Width = 120
            });
            grid.Columns.Add(new DataGridViewTextBoxColumn()
            {
                HeaderText = "Ready",
                DataPropertyName = "DisplayReady",
                Width = 60
            });

            grid.Columns.Add(new DataGridViewButtonColumn()
            {
                Name = "CloneColumn",
                HeaderText = "Clone(+)",
                Text = "+",
                UseColumnTextForButtonValue = true,
                Width = 35
            });
            grid.Columns.Add(new DataGridViewButtonColumn()
            {
                Name = "DeleteColumn",
                HeaderText = "Delete(X)",
                Text = "X",
                UseColumnTextForButtonValue = true,
                Width = 35
            });

            grid.DataSource = filtered;

            grid.DataBindingComplete -= Grid_DataBindingComplete; // sécurité pour ne pas doubler
            grid.DataBindingComplete += Grid_DataBindingComplete;

            foreach (DataGridViewColumn column in grid.Columns)
            {
                bool editable =
                    column.Name == "ActifColumn" ||
                    column.Name == "PlayerColumn" ||
                    column.Name == "CloneColumn" ||
                    column.Name == "DeleteColumn";

                column.ReadOnly = !editable;
            }

        }

        //méthode
        private void Grid_CellMouseDown(object sender, DataGridViewCellMouseEventArgs e)
        {
            if (e.RowIndex < 0)
                return;

            DataGridView grid = (DataGridView)sender;

            DataGridViewRow row = grid.Rows[e.RowIndex];
            Squad squad = row.DataBoundItem as Squad;

            if (squad == null)
                return;

            string columnName = e.ColumnIndex >= 0
            ? grid.Columns[e.ColumnIndex].Name
            : "";

            // Actif + Player : on laisse juste la checkbox fonctionner
            if (columnName == "PlayerColumn" || columnName == "ActifColumn")
                return;


            // Clone
            if (columnName == "CloneColumn")
            {
                var frm = new FormSquadEdit(squad, true);

                frm.FormClosed += (s, args) =>
                {
                    if (frm.DialogResult == DialogResult.OK)
                    {
                        _form1.currentSquads.Add(frm.EditedSquad);
                        _form1.RefreshGrids();
                    }
                };

                frm.Show();
                return;
            }

            // Delete
            if (columnName == "DeleteColumn")
            {
                if (MessageBox.Show(
                    "Delete squad \"" + squad.Name + "\" ?",
                    "Confirm delete",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question) == DialogResult.Yes)
                {
                    _form1.currentSquads.Remove(squad);
                    _form1.RefreshGrids();
                }

                return;
            }

            // Toute autre colonne => édition
            var editFrm = new FormSquadEdit(squad, false);

            editFrm.FormClosed += (s, args) =>
            {
                if (editFrm.DialogResult == DialogResult.OK)
                {
                    _form1.RefreshGrids();
                }
            };

            editFrm.Show();
        }
        //private void Grid_CellClick(object sender, DataGridViewCellEventArgs e)
        //{
        //    var grid = sender as DataGridView;
        //    if (e.RowIndex < 0)
        //        return;

        //    var squad = grid.Rows[e.RowIndex].DataBoundItem as Squad;
        //    if (squad == null)
        //        return;


        //    //// EDIT
        //    string columnName = grid.Columns[e.ColumnIndex].Name;

        //    // Si ce n'est ni Clone ni Delete, on ouvre directement l'éditeur
        //    if (columnName != "CloneColumn" && columnName != "DeleteColumn")
        //    {
        //        var frm = new FormSquadEdit(squad, false);

        //        frm.FormClosed += (s, args) =>
        //        {
        //            if (frm.DialogResult == DialogResult.OK)
        //            {
        //                _form1.RefreshGrids();
        //            }
        //        };

        //        //frm.Show(_form1);
        //        frm.Show();
        //        return;
        //    }

        //    // CLONE
        //    if (columnName == "CloneColumn")
        //    {
        //        var frm = new FormSquadEdit(squad, true);

        //        frm.FormClosed += (s, args) =>
        //        {
        //            if (frm.DialogResult == DialogResult.OK)
        //            {
        //                _form1.currentSquads.Add(frm.EditedSquad);
        //                _form1.RefreshGrids();
        //            }
        //        };

        //        //frm.Show(_form1);
        //        frm.Show();
        //    }

        //    // DELETE
        //    else if (columnName == "DeleteColumn")
        //    {
        //        var result = MessageBox.Show(
        //            $"Delete squad '{squad.Name}' ?",
        //            "Delete",
        //            MessageBoxButtons.YesNo,
        //            MessageBoxIcon.Warning);

        //        if (result == DialogResult.Yes)
        //        {
        //            _form1.currentSquads.Remove(squad);
        //            _form1.RefreshGrids();
        //        }
        //    }
        //}
        private void Grid_RowHeaderMouseClick(object sender, DataGridViewCellMouseEventArgs e)
        {
            if (e.RowIndex < 0)
                return;

            DataGridView grid = (DataGridView)sender;

            DataGridViewRow row = grid.Rows[e.RowIndex];
            Squad squad = row.DataBoundItem as Squad;

            if (squad == null)
                return;

            var frm = new FormSquadEdit(squad, false);

            frm.FormClosed += (s, args) =>
            {
                if (frm.DialogResult == DialogResult.OK)
                {
                    _form1.RefreshGrids();
                }
            };

            frm.Show();
        }

        //méthode
        private void Grid_CurrentCellDirtyStateChanged(object sender, EventArgs e)
        {
            var grid = sender as DataGridView;

            if (grid.CurrentCell is DataGridViewCheckBoxCell)
            {
                grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
            }
        }
        //méthode
        private void Grid_CellValueChanged(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0)
                return;

            var grid = sender as DataGridView;

            // colonne "Player"ow.Cells["PlayerColumn"] = new DataGridViewTextBoxCell();
            if (e.ColumnIndex != 1)
                return;

            var squadTest = grid.Rows[e.RowIndex].DataBoundItem as Squad;

            if (squadTest == null || !_playableList.Contains(squadTest.Type))
                return;

            var selectedSquad = grid.Rows[e.RowIndex].DataBoundItem as Squad;

            if (selectedSquad == null)
                return;

            // si on coche ce squad, on décoche tous les autres
            if (selectedSquad.Player)
            {
                foreach (var squad in _form1.currentSquads)
                {
                    if (!ReferenceEquals(squad, selectedSquad))
                    {
                        squad.Player = false;
                    }
                }

                // recharge les 2 grilles
                LoadGridStatic(_form1.dataGridViewBlue, _form1.currentSquads, "blue", selectedSquad.FolderFile);
                LoadGridStatic(_form1.dataGridViewRed, _form1.currentSquads, "red", selectedSquad.FolderFile);
            }
        }
        //méthode
        // ⚡ Méthode pour désactiver la case Player pour les avions non jouables
        private static void Grid_DataBindingComplete(object sender, DataGridViewBindingCompleteEventArgs e)
        {
            var grid = sender as DataGridView;
            if (grid == null) return;

            foreach (DataGridViewRow row in grid.Rows)
            {
                if (row.DataBoundItem is Squad squad)
                {
                    bool playable = _playableList.Contains(squad.Type);
                    if (!playable)
                    {
                        // Remplacer la checkbox Player par une cellule texte
                        row.Cells["PlayerColumn"] = new DataGridViewTextBoxCell();
                        row.Cells["PlayerColumn"].ReadOnly = true;
                        row.Cells["PlayerColumn"].Style.BackColor = grid.DefaultCellStyle.BackColor;
                        row.Cells["PlayerColumn"].Style.SelectionBackColor = grid.DefaultCellStyle.BackColor;
                        row.Cells["PlayerColumn"].Value = null;
                    }
                }
            }

            grid.Refresh(); // force le rendu
        }

        private void Grid_DataError(object sender, DataGridViewDataErrorEventArgs e)
        {
            e.ThrowException = false;
        }

        private void Grid_CellFormatting(object sender, DataGridViewCellFormattingEventArgs e)
        {
            var grid = sender as DataGridView;

            // colonne Player
            if (e.ColumnIndex != 1 || e.RowIndex < 0)
                return;

            var squad = grid.Rows[e.RowIndex].DataBoundItem as Squad;
            if (squad == null)
                return;

            bool playable = _playableList.Contains(squad.Type);

            if (!playable)
            {
                grid.Rows[e.RowIndex].Cells[e.ColumnIndex].ReadOnly = true;
                grid.Rows[e.RowIndex].Cells[e.ColumnIndex].Style.ForeColor =
                    grid.Rows[e.RowIndex].Cells[e.ColumnIndex].Style.BackColor;
                grid.Rows[e.RowIndex].Cells[e.ColumnIndex].Style.SelectionForeColor =
                    grid.Rows[e.RowIndex].Cells[e.ColumnIndex].Style.SelectionBackColor;
            }
        }


    }
}








            ////internal class CampaignEdit
            //public partial class CampaignEdit_OLD : Form
            //{
            //    Form1 _form1;


            //    static object cachedLuaResult = null;
            //    private static List<string> playableList = new List<string>();

            //    public List<Squad> currentSquads = new List<Squad>();

            //    // 🔹 2. CONSTRUCTEUR
            //    public CampaignEdit(Form1 form1, string nameCamp)
            //    {
            //        _form1 = form1;

            //        var time_LuaPart = Stopwatch.StartNew();


            //        form1.dataGridViewBlue.CellContentClick += Grid_CellClick;
            //        form1.dataGridViewRed.CellContentClick += Grid_CellClick;

            //        form1.dataGridViewBlue.CellValueChanged += Grid_CellValueChanged;
            //        form1.dataGridViewRed.CellValueChanged += Grid_CellValueChanged;

            //        form1.dataGridViewBlue.CurrentCellDirtyStateChanged += Grid_CurrentCellDirtyStateChanged;
            //        form1.dataGridViewRed.CurrentCellDirtyStateChanged += Grid_CurrentCellDirtyStateChanged;

            //        form1.dataGridViewBlue.DataError += Grid_DataError;
            //        form1.dataGridViewRed.DataError += Grid_DataError;

            //        form1.dataGridViewBlue.CellFormatting += Grid_CellFormatting;
            //        form1.dataGridViewRed.CellFormatting += Grid_CellFormatting;

            //        PublicTable.errorTable.Clear();
            //        form1.textBox_Bugs.Text = "";
            //        form1.tabPage12.Text = "Bugs";

            //        //Pour représenter la table Lua donnée en C#, il est généralement préférable d'utiliser une structure de données 
            //        //qui offre la flexibilité de stocker des paires clé-valeur, similaire aux tables en Lua. 
            //        //    En C#, le type de données le plus approprié pour cela est le Dictionary<TKey, TValue>.

            //        // Assurez-vous d'appeler UpdateSharedData avant d'ouvrir Form3_Clonage
            //        form1.UpdateSharedData();

            //        Lua lua = new Lua();

            //        lua["versionPackageICM"] = "NG";
            //        lua["pathScriptsMod"] = SharedData.textBox_SavedGames + @"\Mods\tech\DCE\ScriptsMod.NG";
            //        lua["pathCampaign"] = SharedData.textBox_SavedGames + @"\Mods\tech\DCE\Missions\Campaigns\" + nameCamp;
            //        lua["generator"] = "DCE_Manager"; 
            //        lua["pathSavedGames"] = SharedData.textBox_SavedGames;
            //        // Crée la table Debug avec la clé "debug" à false
            //        var debugTable = new Dictionary<string, object>
            //        {
            //            { "debug", false }
            //        };
            //        lua["Debug"] = debugTable;


            //        //lua.DoFile(SharedData.textBox_SavedGames + @"\Mods\tech\DCE\ScriptsMod.NG\DCEM_Function.lua");

            //        //var result = lua.DoFile(SharedData.textBox_SavedGames + @"\Mods\tech\DCE\ScriptsMod.NG\DCEM_Function.lua");
            //        object[] result;

            //        if (cachedLuaResult == null)
            //        {
            //            cachedLuaResult = lua.DoFile(SharedData.textBox_SavedGames + @"\Mods\tech\DCE\ScriptsMod.NG\DCEM_Function.lua");
            //        }

            //        result = (object[])cachedLuaResult;

            //        LuaTable luaTable = (LuaTable)result[0];
            //        LuaTable taskByPlaneLua = (LuaTable)luaTable["taskByPlane"];

            //        // Créer le dictionnaire pour stocker les données
            //        var taskByPlane = new Dictionary<string, Dictionary<string, bool>>();

            //        // Parcourir la table Lua
            //        foreach (var key in taskByPlaneLua.Keys)
            //        {
            //            string planeName = key.ToString();
            //            LuaTable taskTable = (LuaTable)taskByPlaneLua[key];

            //            var taskDictionary = new Dictionary<string, bool>();
            //            foreach (var taskKey in taskTable.Keys)
            //            {
            //                string taskName = taskKey.ToString();
            //                bool taskValue = (bool)taskTable[taskKey];
            //                taskDictionary[taskName] = taskValue;
            //            }

            //            taskByPlane[planeName] = taskDictionary;
            //        }

            //        // Charger le tableau playable_m
            //        LuaTable playable_mLua = (LuaTable)luaTable["Playable_m"];
            //        //List<string> playableList = new List<string>();
            //        playableList.Clear();

            //        // Parcourir la table Lua
            //        foreach (var plane in playable_mLua.Keys)
            //        {
            //            playableList.Add(plane.ToString());
            //        }

            //        time_LuaPart.Stop();
            //        LogRegister($"time_LuaPart : {time_LuaPart.ElapsedMilliseconds} ms");

            //        //*************************************************************************
            //        // CREATION DES TABLES BASE (VERSION NLua)
            //        //*************************************************************************
            //        var time_airbase = Stopwatch.StartNew();
            //        string savedGamesPath = SharedData.textBox_SavedGames;

            //        // Reset
            //        PublicTable.Airdrome = new Dictionary<string, Dictionary<string, List<string>>>()
            //        {
            //            { "Init", new Dictionary<string, List<string>> { { "blue", new List<string>() }, { "red", new List<string>() } } },
            //            { "Active", new Dictionary<string, List<string>> { { "blue", new List<string>() }, { "red", new List<string>() } } }
            //        };


            //        time_airbase.Stop();
            //        LogRegister($"time_airbase : {time_airbase.ElapsedMilliseconds} ms");


            //        if (ParamDivers.NewParseOobAir)
            //        {
            //            var sw = Stopwatch.StartNew();
            //            var time_ParseOobAir = Stopwatch.StartNew();

            //            IDictionary<int, string> path_oob_air = new Dictionary<int, string>();
            //            string path_oob_airFile = SharedData.textBox_SavedGames + @"\Mods\tech\DCE\Missions\Campaigns\" + nameCamp + @"\Init\oob_air_init.lua";
            //            path_oob_air.Add(1, path_oob_airFile); //adding a key/value using the Add() method

            //            path_oob_airFile = SharedData.textBox_SavedGames + @"\Mods\tech\DCE\Missions\Campaigns\" + nameCamp + @"\Active\oob_air.lua";
            //            path_oob_air.Add(2, path_oob_airFile);


            //            //***************************------------------


            //            List_oob_air_Manager.List_oob_air = new List<Squad>();


            //            //on compte tous les squads differement, init ou active, sinon ça fout le bordel
            //            int idSquad = -1;
            //            var uiBatch = new UIBatchBuilder();

            //            var allSquads = new List<Squad>();

            //            for (int d = 1; d <= 2; d++)
            //            {

            //                //*************************************************************************
            //                //PARSE LEs FICHIERs ******************************************************
            //                //oob_air_init et
            //                //oob_air
            //                //*************************************************************************

            //                string folderFile = "Init";
            //                if (d == 2)
            //                    folderFile = "Active";

            //                //LuaParse C_oobAir = new LuaParse();
            //                string pathFileB = path_oob_air[d];

            //                if (File.Exists(pathFileB))
            //                {
            //                    ParamCampaign.NameFileParse = pathFileB;

            //                    //C_oobAir.Parse(File.ReadAllText(pathFileB));
            //                    var luaObj = LuaParser.ParseFile(pathFileB, "oob_air"); // "oob_air" ou "oob_air_init"
            //                   //var root = luaObj.luaobj as Dictionary<string, LuaObject>;

            //                    var root = luaObj.luaobj as Dictionary<string, LuaObject>;
            //                    if (root == null) continue;

            //                    foreach (var entry in root) // side
            //                    {
            //                        string side = entry.Key;
            //                        int sideInt = side == "blue" ? 1 : 2;

            //                        var level1 = entry.Value.luaobj as Dictionary<string, LuaObject>;
            //                        if (level1 == null) continue;

            //                        foreach (var entry2 in level1) // squad
            //                        {
            //                            idSquad++;

            //                            var squad = new Squad
            //                            {
            //                                SideString = side,
            //                                IdSquad = idSquad,
            //                                FolderFile = folderFile,
            //                            };

            //                            List_oob_air_Manager.List_oob_air.Add(squad);
            //                            allSquads.Add(squad);


            //                            var level2 = entry2.Value.luaobj as Dictionary<string, LuaObject>;
            //                            if (level2 == null) continue;

            //                            foreach (var entry3 in level2) // propriétés squad
            //                            {
            //                                var key = entry3.Key;
            //                                var valObj = entry3.Value.luaobj;

            //                                // ⚡ SWITCH = beaucoup + rapide que 30 if
            //                                switch (key)
            //                                {
            //                                    case "name": squad.Name = valObj?.ToString(); break;
            //                                    case "player": squad.Player = Convert.ToBoolean(valObj); break;
            //                                    case "type": squad.Type = valObj?.ToString(); break;
            //                                    case "country": squad.Country = valObj?.ToString(); break;
            //                                    case "skill": squad.Skill = valObj?.ToString(); break;
            //                                    case "base": squad.Base = valObj?.ToString(); break;

            //                                    case "baseAlternative":
            //                                        if (valObj is Dictionary<string, LuaObject> baseAlt)
            //                                        {
            //                                            squad.BaseAlternative = new List<string>(baseAlt.Count);
            //                                            foreach (var e in baseAlt)
            //                                                squad.BaseAlternative.Add(e.Value.luaobj.ToString().Trim('"'));
            //                                        }
            //                                        break;

            //                                    case "number": squad.Number = Convert.ToInt32(valObj); break;
            //                                    case "InitNumber": squad.InitNumber = Convert.ToInt32(valObj); break;
            //                                    case "reserve": squad.Reserve = Convert.ToInt32(valObj); break;
            //                                    case "InitReserve": squad.InitReserve = Convert.ToInt32(valObj); break;

            //                                    case "tasks":
            //                                        if (valObj is Dictionary<string, LuaObject> tasks)
            //                                        {
            //                                            squad.Tasks = new Dictionary<string, object>(tasks.Count);
            //                                            foreach (var e in tasks)
            //                                                squad.Tasks[e.Key] = Convert.ToBoolean(e.Value.luaobj);
            //                                        }
            //                                        break;

            //                                    case "tasksCoef":
            //                                        if (valObj is Dictionary<string, LuaObject> tasksCoef)
            //                                        {
            //                                            squad.TasksCoef = new Dictionary<string, object>(tasksCoef.Count);
            //                                            foreach (var e in tasksCoef)
            //                                            {
            //                                                if (double.TryParse(e.Value.luaobj.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out double v))
            //                                                    squad.TasksCoef[e.Key] = v;
            //                                            }
            //                                        }
            //                                        break;

            //                                    case "inactive":
            //                                        squad.Inactive = Convert.ToBoolean(valObj);
            //                                        break;

            //                                    case "roster":
            //                                        if (valObj is Dictionary<string, LuaObject> roster)
            //                                        {
            //                                            squad.Roster = new Dictionary<string, object>(roster.Count);
            //                                            foreach (var e in roster)
            //                                                squad.Roster[e.Key] = Convert.ToInt32(e.Value.luaobj);
            //                                        }
            //                                        break;

            //                                    case "score":
            //                                        if (valObj is Dictionary<string, LuaObject> score)
            //                                        {
            //                                            squad.Score = new Dictionary<string, object>(score.Count);
            //                                            foreach (var e in score)
            //                                                squad.Score[e.Key] = Convert.ToInt32(e.Value.luaobj);
            //                                        }
            //                                        break;

            //                                    default:
            //                                        // ⚡ ultra important : éviter GetType() (lent)
            //                                        if (valObj is Dictionary<string, LuaObject> sub)
            //                                        {
            //                                            var dict = new Dictionary<string, object>(sub.Count);
            //                                            foreach (var e in sub)
            //                                                dict[e.Key] = e.Value.luaobj;

            //                                            squad.AdditionalProperties[key] = dict;
            //                                        }
            //                                        else
            //                                        {
            //                                            squad.AdditionalProperties[key] = valObj;
            //                                        }
            //                                        break;
            //                                }
            //                            }
            //                        }
            //                    }

            //                }// fileExist()
            //            }
            //            time_ParseOobAir.Stop();
            //            LogRegister($"Time time_ParseOobAir: {time_ParseOobAir.ElapsedMilliseconds} ms");

            //            var time_UI = Stopwatch.StartNew();

            //            form1.currentSquads = allSquads;
            //            form1.RefreshGrids();


            //            time_UI.Stop();
            //            LogRegister($"Time UI: {time_UI.ElapsedMilliseconds} ms");


            //            IDictionary<int, string> pathOobAir = new Dictionary<int, string>();
            //            string pathFile = SharedData.textBox_SavedGames + @"\Mods\tech\DCE\Missions\Campaigns\" + nameCamp + @"\Init\oob_air_init.lua";
            //            pathOobAir.Add(1, pathFile); //adding a key/value using the Add() method

            //            pathFile = SharedData.textBox_SavedGames + @"\Mods\tech\DCE\Missions\Campaigns\" + nameCamp + @"\Active\oob_air.lua";
            //            pathOobAir.Add(2, pathFile);


            //            form1.CampaignTab.Text = "";
            //            form1.groupBoxCampEdit.Text = nameCamp;
            //            form1.CampaignTab.Visible = true;

            //            for (int d = 1; d <= 2; d++)
            //            {
            //                pathFile = pathOobAir[d];

            //                string[,,,] TEMPtableOobAirAAA = new string[3, 100, 100, 4];



            //                if (ParamDivers.NewParseOobAir == false)
            //                {

            //                }

            //                //*************************************************************************
            //                // RECUPERATION Briefing Campaign (VERSION NLua)
            //                //*************************************************************************

            //                string campTrigger = SharedData.textBox_SavedGames + @"\Mods\tech\DCE\Missions\Campaigns\" + nameCamp + @"\Init\camp_triggers_init.lua";

            //                ParamCampaign.NameFileParse = campTrigger;

            //                string BriefingCampaign = "";

            //                var luaObj = LuaParser.ParseFile(campTrigger, "camp_triggers");

            //                var root = luaObj.luaobj as Dictionary<string, LuaObject>;

            //                if (root != null)
            //                {
            //                    foreach (var entry1 in root)
            //                    {
            //                        Dictionary<string, LuaObject> level1 = null;

            //                        // ✅ CAS 1 : clé = "Campaign Briefing"
            //                        if (entry1.Key.Equals("Campaign Briefing", StringComparison.OrdinalIgnoreCase))
            //                        {
            //                            level1 = entry1.Value.luaobj as Dictionary<string, LuaObject>;
            //                        }
            //                        else
            //                        {
            //                            // ✅ CAS 2 : table avec champ name = "Campaign Briefing"
            //                            var tmp = entry1.Value.luaobj as Dictionary<string, LuaObject>;

            //                            if (tmp != null && tmp.ContainsKey("name"))
            //                            {
            //                                var nameVal = tmp["name"].luaobj?.ToString();

            //                                if (nameVal != null &&
            //                                    nameVal.Equals("Campaign Briefing", StringComparison.OrdinalIgnoreCase))
            //                                {
            //                                    level1 = tmp;
            //                                }
            //                            }
            //                        }

            //                        // Si pas le bon trigger → skip
            //                        if (level1 == null)
            //                            continue;

            //                        // 🔽 Traitement identique ensuite
            //                        if (level1.ContainsKey("action"))
            //                        {
            //                            var actions = level1["action"].luaobj as Dictionary<string, LuaObject>;

            //                            if (actions == null)
            //                                continue;

            //                            foreach (var entry3 in actions)
            //                            {
            //                                string val = entry3.Value.luaobj?.ToString();

            //                                if (string.IsNullOrEmpty(val))
            //                                    continue;

            //                                if (val.Contains("Action.AddImage"))
            //                                    break;

            //                                // 🔥 Extraction propre
            //                                if (val.Contains("Action.Text"))
            //                                {
            //                                    int start = val.IndexOf("Action.Text(");
            //                                    if (start >= 0)
            //                                    {
            //                                        start += "Action.Text(".Length;

            //                                        int end = val.LastIndexOf(")");
            //                                        if (end > start)
            //                                        {
            //                                            string content = val.Substring(start, end - start).Trim();

            //                                            if ((content.StartsWith("\"") && content.EndsWith("\"")) ||
            //                                                (content.StartsWith("'") && content.EndsWith("'")))
            //                                            {
            //                                                content = content.Substring(1, content.Length - 2);
            //                                            }

            //                                            BriefingCampaign += content + "\r\n";
            //                                        }
            //                                    }
            //                                }
            //                            }
            //                        }
            //                    }
            //                }

            //                // Nettoyage (identique à ton code)
            //                BriefingCampaign = BriefingCampaign
            //                    .Replace("'Action.Text(\"", "")
            //                    .Replace("\")'", "\r\n")
            //                    .Replace("\"", "\r\n");

            //                BriefingCampaign = Regex.Replace(BriefingCampaign, @"\[.\]=", "");

            //                form1.textBoxCampBriefing.Text = BriefingCampaign;


            //                //*************************************************************************
            //                //IMAGE Campagne **********************************************************
            //                // ************************************************************************
            //                //*************************************************************************

            //                string basePath = SharedData.textBox_SavedGames + @"\Mods\tech\DCE\Missions\Campaigns\" + nameCamp;

            //                string bmpPath = basePath + ".bmp";
            //                string pngPath = basePath + ".png";

            //                string imagePath = null;

            //                // priorité au PNG (plus moderne)
            //                if (File.Exists(pngPath))
            //                {
            //                    imagePath = pngPath;
            //                }
            //                else if (File.Exists(bmpPath))
            //                {
            //                    imagePath = bmpPath;
            //                }

            //                // si une image existe
            //                if (imagePath != null)
            //                {
            //                    using (Image image = Image.FromFile(imagePath))
            //                    {
            //                        form1.pictureBoxCampImage.Image?.Dispose();
            //                        form1.pictureBoxCampImage.Image = new Bitmap(image);
            //                    }
            //                }
            //                else
            //                {
            //                    // aucune image → on nettoie
            //                    form1.pictureBoxCampImage.Image?.Dispose();
            //                    form1.pictureBoxCampImage.Image = null;
            //                }
            //                //**


            //                //*************************************************************************
            //                //AFFICHAGE DU RESULTAT OOB AIR *******************************************
            //                //*************************************************************************
            //                if (ParamDivers.NewParseOobAir == false)
            //                {

            //                }

            //            }

            //            sw.Stop();
            //            //MessageBox.Show($"Temps d'exécution : {sw.ElapsedMilliseconds} ms", "Performance");
            //            LogRegister($"TimePARSE LEs FICHIERs //oob_air_init et //oob_air +++ : {sw.ElapsedMilliseconds} ms");

            //        }   ////__FIN__PARSE LEs FICHIERs //oob_air_init et //oob_air

            //        var time_errorTable = Stopwatch.StartNew();
            //        string msg = "";
            //        foreach (KeyValuePair<string, string> kvp in PublicTable.errorTable)
            //        {
            //            msg = msg + kvp.Value + "\r\n";
            //            //Console.WriteLine("Key: {0}, Value: {1}", kvp.Key, kvp.Value);
            //        }
            //        if (msg != "")
            //        {
            //            int count1 = PublicTable.errorTable.Count;
            //            form1.textBox_Bugs.Text = msg;
            //            form1.tabPage12.Text = form1.tabPage12.Text + count1.ToString();
            //        }
            //        time_errorTable.Stop();
            //        LogRegister($"time_errorTable : {time_errorTable.ElapsedMilliseconds} ms");
            //    }


            //    public static void LoadGridStatic(DataGridView grid, List<Squad> squads, string side, string state)
            //    {

            //        var filtered = squads
            //            .Where(s => s.SideString == side && s.FolderFile == state)
            //            .ToList();

            //        grid.Columns.Clear();
            //        grid.AutoGenerateColumns = false;
            //        grid.DataSource = null;
            //        grid.ScrollBars = ScrollBars.Both;

            //        grid.Columns.Add(new DataGridViewCheckBoxColumn()
            //        {
            //            HeaderText = "Actif",
            //            DataPropertyName = "IsActive",
            //            Width = 50
            //        });

            //        // Colonne Player
            //        var playerColumn = new DataGridViewCheckBoxColumn()
            //        {
            //            HeaderText = "Player",
            //            DataPropertyName = "Player",
            //            Width = 50,
            //            Name = "PlayerColumn"
            //        };
            //        grid.Columns.Add(playerColumn);

            //        grid.Columns.Add(new DataGridViewTextBoxColumn()
            //        {
            //            HeaderText = "Name",
            //            DataPropertyName = "Name",
            //            Width = 140
            //        });
            //        grid.Columns.Add(new DataGridViewTextBoxColumn()
            //        {
            //            HeaderText = "Type",
            //            DataPropertyName = "Type",
            //            Width = 80
            //        });
            //        grid.Columns.Add(new DataGridViewTextBoxColumn()
            //        {
            //            HeaderText = "Base",
            //            DataPropertyName = "Base",
            //            Width = 120
            //        });
            //        grid.Columns.Add(new DataGridViewTextBoxColumn()
            //        {
            //            HeaderText = "Ready",
            //            DataPropertyName = "DisplayReady",
            //            Width = 60
            //        });
            //        //grid.Columns.Add(new DataGridViewTextBoxColumn()
            //        //{
            //        //    HeaderText = "Reserve",
            //        //    DataPropertyName = "DisplayReserve",
            //        //    Width = 60
            //        //});

            //        grid.Columns.Add(new DataGridViewButtonColumn()
            //        {
            //            HeaderText = "Edit",
            //            Text = "✎",
            //            UseColumnTextForButtonValue = true,
            //            Width = 35
            //        });

            //        grid.Columns.Add(new DataGridViewButtonColumn()
            //        {
            //            HeaderText = "Clone(+)",
            //            Text = "+",
            //            UseColumnTextForButtonValue = true,
            //            Width = 35
            //        });
            //        grid.Columns.Add(new DataGridViewButtonColumn()
            //        {
            //            HeaderText = "Delete(X)",
            //            Text = "X",
            //            UseColumnTextForButtonValue = true,
            //            Width = 35
            //        });

            //        grid.DataSource = filtered;

            //        grid.DataBindingComplete -= Grid_DataBindingComplete; // sécurité pour ne pas doubler
            //        grid.DataBindingComplete += Grid_DataBindingComplete;

            //    }

            //    //méthode
            //    private void Grid_CellClick(object sender, DataGridViewCellEventArgs e)
            //    {
            //        var grid = sender as DataGridView;
            //        if (e.RowIndex < 0)
            //            return;

            //        var squad = grid.Rows[e.RowIndex].DataBoundItem as Squad;
            //        if (squad == null)
            //            return;

            //        // EDIT
            //        if (e.ColumnIndex == 6)
            //        {
            //            using (var frm = new FormSquadEdit(squad, false))
            //            {
            //                if (frm.ShowDialog(_form1) == DialogResult.OK)
            //                {
            //                    _form1.RefreshGrids();
            //                }
            //            }
            //        }

            //        // CLONE
            //        else if (e.ColumnIndex == 7)
            //        {
            //            using (var frm = new FormSquadEdit(squad, true))
            //            {
            //                if (frm.ShowDialog(_form1) == DialogResult.OK)
            //                {
            //                    _form1.currentSquads.Add(frm.EditedSquad);
            //                    _form1.RefreshGrids();
            //                }
            //            }
            //        }

            //        // DELETE
            //        else if (e.ColumnIndex == 8)
            //        {
            //            var result = MessageBox.Show(
            //                $"Delete squad '{squad.Name}' ?",
            //                "Delete",
            //                MessageBoxButtons.YesNo,
            //                MessageBoxIcon.Warning);

            //            if (result == DialogResult.Yes)
            //            {
            //                _form1.currentSquads.Remove(squad);
            //                _form1.RefreshGrids();
            //            }
            //        }
            //    }
            //    //private void Grid_CellClick(object sender, DataGridViewCellEventArgs e)
            //    //{
            //    //    var grid = sender as DataGridView;
            //    //    if (e.RowIndex < 0) return;

            //    //    var squad = grid.Rows[e.RowIndex].DataBoundItem as Squad;
            //    //    if (squad == null) return;

            //    //    // colonne clone
            //    //    if (e.ColumnIndex == 7)
            //    //    {
            //    //        MessageBox.Show($"Clone {squad.Name}");
            //    //    }

            //    //    // colonne delete
            //    //    if (e.ColumnIndex == 8)
            //    //    {
            //    //        MessageBox.Show($"Delete {squad.Name}");
            //    //    }
            //    //}
            //    //méthode
            //    private void Grid_CurrentCellDirtyStateChanged(object sender, EventArgs e)
            //    {
            //        var grid = sender as DataGridView;

            //        if (grid.CurrentCell is DataGridViewCheckBoxCell)
            //        {
            //            grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
            //        }
            //    }
            //    //méthode
            //    private void Grid_CellValueChanged(object sender, DataGridViewCellEventArgs e)
            //    {
            //        if (e.RowIndex < 0)
            //            return;

            //        var grid = sender as DataGridView;

            //        // colonne "Player"ow.Cells["PlayerColumn"] = new DataGridViewTextBoxCell();
            //        if (e.ColumnIndex != 1)
            //            return;

            //        var squadTest = grid.Rows[e.RowIndex].DataBoundItem as Squad;

            //        if (squadTest == null || !playableList.Contains(squadTest.Type))
            //            return;

            //        var selectedSquad = grid.Rows[e.RowIndex].DataBoundItem as Squad;

            //        if (selectedSquad == null)
            //            return;

            //        // si on coche ce squad, on décoche tous les autres
            //        if (selectedSquad.Player)
            //        {
            //            foreach (var squad in _form1.currentSquads)
            //            {
            //                if (!ReferenceEquals(squad, selectedSquad))
            //                {
            //                    squad.Player = false;
            //                }
            //            }

            //            // recharge les 2 grilles
            //            LoadGridStatic(_form1.dataGridViewBlue, _form1.currentSquads, "blue", selectedSquad.FolderFile);
            //            LoadGridStatic(_form1.dataGridViewRed, _form1.currentSquads, "red", selectedSquad.FolderFile);
            //        }
            //    }
            //    //méthode
            //    // ⚡ Méthode pour désactiver la case Player pour les avions non jouables
            //    private static void Grid_DataBindingComplete(object sender, DataGridViewBindingCompleteEventArgs e)
            //    {
            //        var grid = sender as DataGridView;
            //        if (grid == null) return;

            //        foreach (DataGridViewRow row in grid.Rows)
            //        {
            //            if (row.DataBoundItem is Squad squad)
            //            {
            //                bool playable = playableList.Contains(squad.Type);
            //                if (!playable)
            //                {
            //                    // Remplacer la checkbox Player par une cellule texte
            //                    row.Cells["PlayerColumn"] = new DataGridViewTextBoxCell();
            //                    row.Cells["PlayerColumn"].ReadOnly = true;
            //                    row.Cells["PlayerColumn"].Style.BackColor = grid.DefaultCellStyle.BackColor;
            //                    row.Cells["PlayerColumn"].Style.SelectionBackColor = grid.DefaultCellStyle.BackColor;
            //                    row.Cells["PlayerColumn"].Value = null;
            //                }
            //            }
            //        }

            //        grid.Refresh(); // force le rendu
            //    }

            //    private void Grid_DataError(object sender, DataGridViewDataErrorEventArgs e)
            //    {
            //        e.ThrowException = false;
            //    }

            //    private void Grid_CellFormatting(object sender, DataGridViewCellFormattingEventArgs e)
            //    {
            //        var grid = sender as DataGridView;

            //        // colonne Player
            //        if (e.ColumnIndex != 1 || e.RowIndex < 0)
            //            return;

            //        var squad = grid.Rows[e.RowIndex].DataBoundItem as Squad;
            //        if (squad == null)
            //            return;

            //        bool playable = playableList.Contains(squad.Type);

            //        if (!playable)
            //        {
            //            grid.Rows[e.RowIndex].Cells[e.ColumnIndex].ReadOnly = true;
            //            grid.Rows[e.RowIndex].Cells[e.ColumnIndex].Style.ForeColor =
            //                grid.Rows[e.RowIndex].Cells[e.ColumnIndex].Style.BackColor;
            //            grid.Rows[e.RowIndex].Cells[e.ColumnIndex].Style.SelectionForeColor =
            //                grid.Rows[e.RowIndex].Cells[e.ColumnIndex].Style.SelectionBackColor;
            //        }
            //    }


            //}
        //}