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
        //private static List<string> _playableList = new List<string>();


        //private Dictionary<string, Dictionary<string, bool>> _taskByPlane =
        //    new Dictionary<string, Dictionary<string, bool>>();

        private static CampaignLuaData _luaData;

        private void ResetUi() { }
        private void LoadAirbases() { }
        private void DisplayErrors() { }


        public List<Squad> CurrentSquads { get; private set; } = new List<Squad>();
        // Nouvelle liste logique regroupant Init + Active.
        // Pourquoi : retrouver facilement les deux versions d'un squad.
        public List<CampaignSquad> CurrentCampaignSquads { get; private set; } = new List<CampaignSquad>();
        public string BriefingCampaign { get; set; }

        //public List<string> AllPlaneHeliList = new List<string>();


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

        //public void LoadLuaData()
        //{
        //    var loader = new CampaignLuaLoader();
        //    loader.Load(_campaignName);

        //    _playableList.Clear();
        //    _playableList.AddRange(loader.PlayableAircraft);
        //    AllPlaneHeliList.AddRange(loader.AllPlaneHeli);

        //    _taskByPlane = loader.TaskByPlane;
        //}

        public void LoadLuaData()
        {
            var loader = new CampaignLuaLoader();
            _luaData = loader.Load(_campaignName);
        }

        private void LoadSquads()
        {

            var parser = new OobAirParser();

            CurrentCampaignSquads = parser.LoadCampaignSquads(_campaignName);

            // Ancienne liste plate reconstruite pour ne pas casser les grilles.
            CurrentSquads = List_oob_air_Manager.List_oob_air;

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
                CampaignSquad campaignSquad_A = OobAirParser.FindCampaignSquad(
                squad.SideString,
                squad.Name);

                Squad squadToEdit_A;

                if (_form1.radioButton_OOB_ACTIVE.Checked)
                {
                    squadToEdit_A = campaignSquad_A.Active ?? campaignSquad_A.Init;
                }
                else
                {
                    squadToEdit_A = campaignSquad_A.Init ?? campaignSquad_A.Active;
                }

                var frm = new FormSquadEdit(squadToEdit_A, _luaData, true);

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

            CampaignSquad campaignSquad = OobAirParser.FindCampaignSquad(
                squad.SideString,
                squad.Name);

            Squad squadToEdit_B;

            if (_form1.radioButton_OOB_ACTIVE.Checked)
            {
                squadToEdit_B = campaignSquad.Active ?? campaignSquad.Init;
            }
            else
            {
                squadToEdit_B = campaignSquad.Init ?? campaignSquad.Active;
            }

            // Toute autre colonne => édition

            // Si on édite le fichier Active, on recopie explicitement roster/score
            // Pourquoi : certains champs ne sont pas recopiés automatiquement lors du merge Init/Active.
            if (_form1.radioButton_OOB_ACTIVE.Checked &&
                campaignSquad != null &&
                campaignSquad.Active != null)
            {
                squadToEdit_B.Roster = campaignSquad.Active.Roster;
                squadToEdit_B.Score = campaignSquad.Active.Score;
            }
            var editFrm = new FormSquadEdit(squadToEdit_B, _luaData, false);



            editFrm.FormClosed += (s, args) =>
            {
                if (editFrm.DialogResult == DialogResult.OK)
                {
                    _form1.RefreshGrids();
                }
            };

            editFrm.Show();
        }
       
        private void Grid_RowHeaderMouseClick(object sender, DataGridViewCellMouseEventArgs e)
        {
            if (e.RowIndex < 0)
                return;

            DataGridView grid = (DataGridView)sender;

            DataGridViewRow row = grid.Rows[e.RowIndex];
            Squad squad = row.DataBoundItem as Squad;

            if (squad == null)
                return;

            CampaignSquad campaignSquad = OobAirParser.FindCampaignSquad(
            squad.SideString,
            squad.Name);

            Squad squadToEdit_C;

            if (_form1.radioButton_OOB_ACTIVE.Checked)
            {
                squadToEdit_C = campaignSquad.Active ?? campaignSquad.Init;
            }
            else
            {
                squadToEdit_C = campaignSquad.Init ?? campaignSquad.Active;
            }

            var frm = new FormSquadEdit(squadToEdit_C, _luaData, false);

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

            //if (squadTest == null || !_playableList.Contains(squadTest.Type))
            if (squadTest == null || !_luaData.PlayableAircraft.Contains(squadTest.Type))
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
                    //bool playable = _playableList.Contains(squad.Type);
                    bool playable = _luaData.PlayableAircraft.Contains(squad.Type);
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

            //bool playable = _playableList.Contains(squad.Type);
            bool playable = _luaData.PlayableAircraft.Contains(squad.Type);

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

