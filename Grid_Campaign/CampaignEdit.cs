using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using DCE_Manager.Parameters;
using DCE_Manager.Utils;

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
        private readonly Main_Form _form1;
        private readonly string _campaignName;
        private static CampaignContext _campaignContext;
        private void ResetUi() { }
        private void DisplayErrors() { }
        private readonly CampaignGridLeft _campGridLft;


        public List<Squad> CurrentSquads { get; private set; } = new List<Squad>();
        // Nouvelle liste logique regroupant Init + Active.
        // Pourquoi : retrouver facilement les deux versions d'un squad.
        public List<CampaignSquad> CurrentCampaignSquads { get; private set; } = new List<CampaignSquad>();
        public string BriefingCampaign { get; set; }


        // 3. constructeur
        public CampaignEdit(Main_Form form1, CampaignGridLeft campGridLft, string campaignName)
        {
            this.FormClosed += CampaignEdit_FormClosed;

            _form1 = form1;
            _campGridLft = campGridLft; // <-- On enregistre la référence
            _campaignName = campaignName;
            Main_Form.Instance.CampaignView.label_Right_Campaign_Name.Text = _campaignName;
            

            InitializeGridEvents();
            this.StartPosition = FormStartPosition.CenterParent;
            this.WindowState = FormWindowState.Normal;
            this.Size = new Size(1200, 850);

            InitializeCampaign();


        }


        // 4. méthodes d'initialisation
        private void InitializeCampaign()
        {
            _campaignContext = new CampaignContext();
            _campaignContext.CampaignEditRef = this;
            _campaignContext.CampaignName = _campaignName;

            ResetUi();
            LoadLuaData();
            LoadAirbases();
            LoadTrigger();
            SetCampaignImage();
            LoadSquads();
            DisplayErrors();

        }

        // 4. méthodes d'initialisation
        private void InitializeGridEvents()
        {

            Main_Form.Instance.CampaignView.CampaignTab.Text = "";
            //_form1.groupBoxCampEdit.Text = _campaignName;
            ParamCampaignSelected.NameCampaign = _campaignName;
            //_form1.CampaignTab.Visible = true;
            Main_Form.Instance.ShowCampaign();
            RegisterGrid(Main_Form.Instance.CampaignView.DataGridViewBlue);
            RegisterGrid(Main_Form.Instance.CampaignView.DataGridViewRed);
        }

        private void RegisterGrid(DataGridView grid)
        {

            // 🔧 Ligne à modifier dans RegisterGrid
            grid.CellMouseDown -= Grid_CellMouseDown; // évite doublons
            grid.CellMouseDown += Grid_CellMouseDown;

            grid.CellValueChanged -= Grid_CellValueChanged;
            grid.CellValueChanged += Grid_CellValueChanged;

            grid.CurrentCellDirtyStateChanged -= Grid_CurrentCellDirtyStateChanged;
            grid.CurrentCellDirtyStateChanged += Grid_CurrentCellDirtyStateChanged;

            grid.DataError -= Grid_DataError;
            grid.DataError += Grid_DataError;

            grid.CellFormatting -= Grid_CellFormatting;
            grid.CellFormatting += Grid_CellFormatting;

        }

        public void LoadLuaData()
        {
            var loader = new CampaignLuaLoader();
            _campaignContext.LuaData = loader.Load(_campaignName);
        }

        public void LoadSquads()
        {

            var parser = new Parser_OobAir();

            CurrentCampaignSquads = parser.LoadCampaignSquads(_campaignName);

            // Ancienne liste plate reconstruite pour ne pas casser les grilles.
            CurrentSquads = List_oob_air_Manager.List_oob_air;

            _form1.currentSquads = CurrentSquads;

            _campGridLft.RefreshGrids();
        }


        // Cette fonction charge le briefing depuis camp_triggers_init.lua.
        // Pourquoi : le parser retourne maintenant simplement une chaîne.
        private void LoadTrigger()
        {
            var parser = new TriggerParser();

            BriefingCampaign = parser.LoadBriefingCampaign(_campaignName);

            SetBriefingText(BriefingCampaign);
        }

        private void LoadAirbases()
        {
            var parser = new Parser_db_airbases();
            _campaignContext.Airbases = parser.Load_db_airbases(_campaignName);
        }

        // Cette fonction affiche le briefing dans la textbox.
        // Pourquoi : on force un texte vide plutôt qu'un null.
        private void SetBriefingText(string txt)
        {
            Main_Form.Instance.CampaignView.textBoxCampBriefing.Text = txt ?? "";
        }

        private void SetCampaignImage()
        {
            string basePath = ParamConf.PATH_SavedGames_DCS + @"\Mods\tech\DCE\Missions\Campaigns\" + _campaignName;

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
                    Main_Form.Instance.CampaignView.pictureBoxCampImage.Image?.Dispose();
                    Main_Form.Instance.CampaignView.pictureBoxCampImage.Image = new Bitmap(image);
                }
            }
            else
            {
                // aucune image → on nettoie
                Main_Form.Instance.CampaignView.pictureBoxCampImage.Image?.Dispose();
                Main_Form.Instance.CampaignView.pictureBoxCampImage.Image = null;
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
            grid.RowHeadersVisible = false;

            // Colonne Edit
            grid.Columns.Add(new DataGridViewButtonColumn()
            {
                Name = "EditColumn",
                HeaderText = "Edit",
                Text = "✏",//🔧
                UseColumnTextForButtonValue = true,
                Width = 30
            });

            grid.Columns.Add(new DataGridViewButtonColumn()
            {
                Name = "CloneColumn",
                HeaderText = "Clone",
                Text = "+",
                UseColumnTextForButtonValue = true,
                Width = 30
            });
            grid.Columns.Add(new DataGridViewButtonColumn()
            {
                Name = "DeleteColumn",
                HeaderText = "Delete",
                Text = "X",
                UseColumnTextForButtonValue = true,
                Width = 30
            });

            grid.Columns.Add(new DataGridViewCheckBoxColumn()
            {
                HeaderText = "Actif",
                DataPropertyName = "Squad_Active",
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
                //HeaderText = "Name",
                //DataPropertyName = "Name",
                HeaderText = "DisplayName",
                DataPropertyName = "DisplayName",
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
                //DataPropertyName = "DisplayReady",
                DataPropertyName = "DisplayReady",
                Width = 60
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

            if (e.ColumnIndex < 0)
                return;

            bool isActive = Main_Form.Instance.CampaignView.IsOobInit;

            CampaignSquad campaignSquad;

            Squad squadToEdit;

            DataGridView grid = (DataGridView)sender;

            DataGridViewRow row = grid.Rows[e.RowIndex];
            Squad squad = row.DataBoundItem as Squad;

            if (squad == null)
                return;

            if (e.ColumnIndex < 0)
            {
                // comportement header (ou return)
                return;
            }

            string columnName = e.ColumnIndex >= 0
            ? grid.Columns[e.ColumnIndex].Name
            : "";


            if (columnName != "EditColumn" &&
                columnName != "CloneColumn" &&
                columnName != "DeleteColumn")
            {
                return; // ← plus d'ouverture automatique
            }

            // Actif + Player : on laisse juste la checkbox fonctionner
            if (columnName == "PlayerColumn" || columnName == "ActifColumn")
                return;

            if (columnName == "EditColumn")
            {
                //string squadKey = squad.SideString.Trim().ToLowerInvariant() + "|" + squad.Name.Trim().ToLowerInvariant();

                //campaignSquad = OobAirParser.FindCampaignSquad(squadKey);

                campaignSquad = CurrentCampaignSquads
                .FirstOrDefault(cs =>
                    (cs.Init != null && cs.Init.IdSquad == squad.IdSquad)
                    ||
                    (cs.Active != null && cs.Active.IdSquad == squad.IdSquad)
                );

                squadToEdit = isActive
                    ? campaignSquad.Active
                    : campaignSquad.Init;

                var frm = new SquadEdit_Form(squadToEdit, _campaignContext, false, "EditColumn");

                frm.SquadUpdated += () =>
                {
                    Main_Form.Instance.CampaignView.DataGridViewBlue.Refresh();
                    Main_Form.Instance.CampaignView.DataGridViewRed.Refresh();
                };


                frm.FormClosed += (s, args) =>
                {
                    if (frm.DialogResult == DialogResult.OK)
                    {
                        _campGridLft.RefreshGrids();
                    }
                };

                frm.SquadUpdated += () =>
                {
                    if (frm.EditedSquad.Player)
                    {
                        SetSinglePlayerSquad(frm.EditedSquad);
                    }
                    else
                    {
                        _campGridLft.RefreshGrids();
                    }
                };

                frm.Show();
                return;
            }


            // Clone
            if (columnName == "CloneColumn")
            {
                if (columnName == "CloneColumn")
                {

                    campaignSquad = CurrentCampaignSquads
                    .FirstOrDefault(cs =>
                        (cs.Init != null && cs.Init.IdSquad == squad.IdSquad)
                        ||
                        (cs.Active != null && cs.Active.IdSquad == squad.IdSquad)
                    );

                    squadToEdit = isActive
                        ? campaignSquad.Active
                        : campaignSquad.Init;

                    // -------------------------------------------------
                    // Création clone DIRECTE
                    // -------------------------------------------------

                    Squad clonedSquad = squadToEdit.Clone();

                    clonedSquad.GenerateCloneIdentity(CurrentSquads);

                    // -------------------------------------------------
                    // Création CampaignSquad complet
                    // -------------------------------------------------

                    CampaignSquad newCampaignSquad = new CampaignSquad();

                    if (isActive)
                    {
                        newCampaignSquad.Active = clonedSquad;

                        Squad initClone = clonedSquad.Clone();
                        initClone.FolderFile = "Init";

                        newCampaignSquad.Init = initClone;
                    }
                    else
                    {
                        newCampaignSquad.Init = clonedSquad;

                        Squad activeClone = clonedSquad.Clone();
                        activeClone.FolderFile = "Active";

                        newCampaignSquad.Active = activeClone;
                    }

                    // -------------------------------------------------
                    // Ajout IMMÉDIAT
                    // -------------------------------------------------

                    CurrentCampaignSquads.Add(newCampaignSquad);

                    if (newCampaignSquad.Init != null)
                        CurrentSquads.Add(newCampaignSquad.Init);

                    if (newCampaignSquad.Active != null)
                        CurrentSquads.Add(newCampaignSquad.Active);

                    _form1.currentSquads = CurrentSquads;

                    // -------------------------------------------------
                    // REFRESH IMMÉDIAT
                    // -------------------------------------------------

                    _campGridLft.RefreshGrids();

                    // -------------------------------------------------
                    // OUVERTURE ÉDITION SUR LE CLONE
                    // -------------------------------------------------

                    var frm = new SquadEdit_Form(
                        clonedSquad,
                        _campaignContext,
                        false,
                        "CloneLive"
                    );

                    frm.SquadUpdated += () =>
                    {
                        Main_Form.Instance.CampaignView.DataGridViewBlue.Refresh();
                        Main_Form.Instance.CampaignView.DataGridViewRed.Refresh();
                    };

                    frm.Show();

                    return;
                }

                //frm.Show();
                //return;
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
                    _campGridLft.RefreshGrids();
                }

                return;
            }

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
            if (grid.Columns[e.ColumnIndex].Name != "PlayerColumn")
                return;

            var squadTest = grid.Rows[e.RowIndex].DataBoundItem as Squad;

            //if (squadTest == null || !_playableList.Contains(squadTest.Type))
            if (squadTest == null || !_campaignContext.LuaData.PlayableAircraft.Contains(squadTest.Type))
                return;

            var selectedSquad = grid.Rows[e.RowIndex].DataBoundItem as Squad;

            if (selectedSquad == null)
                return;

            //éviter de recalculer quand on décoche
            if (!selectedSquad.Player)
                return;
            // si on coche ce squad, on décoche tous les autres
            selectedSquad = grid.Rows[e.RowIndex].DataBoundItem as Squad;

            if (selectedSquad == null)
                return;

            // 🔒 Empêche suppression du dernier Player
            if (!selectedSquad.Player)
            {
                bool anotherPlayerExists = CurrentCampaignSquads.Any(cs =>
                    (cs.Active != selectedSquad && cs.Active.Player) ||
                    (cs.Init != selectedSquad && cs.Init.Player)
                );

                if (!anotherPlayerExists)
                {
                    MessageBox.Show(
                        "You must select another Player squad first.",
                        "Player required",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information
                    );

                    selectedSquad.Player = true;
                    grid.Refresh();
                    return;
                }

                return;
            }

            // colonne Player
            if (grid.Columns[e.ColumnIndex].Name != "PlayerColumn")
                return;

            selectedSquad = grid.Rows[e.RowIndex].DataBoundItem as Squad;

            if (selectedSquad == null)
                return;

            // uniquement si on coche
            if (selectedSquad.Player)
            {
                SetSinglePlayerSquad(selectedSquad);
            }
            else
            {
                // protection dernier player (optionnel, tu peux garder ton code actuel)
                bool anotherPlayerExists = CurrentCampaignSquads.Any(cs =>
                    (cs.Init != selectedSquad && cs.Init.Player) ||
                    (cs.Active != selectedSquad && cs.Active.Player)
                );

                if (!anotherPlayerExists)
                {
                    MessageBox.Show("You must select another Player squad first.");
                    selectedSquad.Player = true;
                    grid.Refresh();
                }
            }

            // refresh
            _campGridLft.RefreshGrids();
           
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
                    bool playable = _campaignContext.LuaData.PlayableAircraft.Contains(squad.Type);
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
            //if (e.ColumnIndex != 1 || e.RowIndex < 0)
            if (grid.Columns[e.ColumnIndex].Name != "PlayerColumn" || e.RowIndex < 0)
                return;

            var squad = grid.Rows[e.RowIndex].DataBoundItem as Squad;
            if (squad == null)
                return;

            bool playable = _campaignContext.LuaData.PlayableAircraft.Contains(squad.Type);

            if (!playable)
            {
                grid.Rows[e.RowIndex].Cells[e.ColumnIndex].ReadOnly = true;
                grid.Rows[e.RowIndex].Cells[e.ColumnIndex].Style.ForeColor =
                    grid.Rows[e.RowIndex].Cells[e.ColumnIndex].Style.BackColor;
                grid.Rows[e.RowIndex].Cells[e.ColumnIndex].Style.SelectionForeColor =
                    grid.Rows[e.RowIndex].Cells[e.ColumnIndex].Style.SelectionBackColor;
            }
        }

        // 🔧 Nettoie les events pour éviter les duplications
        // Pourquoi : sinon les anciennes instances restent abonnées aux grids du Main_Form
        private void UnregisterGrid(DataGridView grid)
        {
            grid.CellMouseDown -= Grid_CellMouseDown;
            grid.CellValueChanged -= Grid_CellValueChanged;
            grid.CurrentCellDirtyStateChanged -= Grid_CurrentCellDirtyStateChanged;
            grid.DataError -= Grid_DataError;
            grid.CellFormatting -= Grid_CellFormatting;
        }
        // 🔧 Se désabonne quand la fenêtre est fermée
        // Pourquoi : empêcher les handlers fantômes
        private void CampaignEdit_FormClosed(object sender, FormClosedEventArgs e)
        {
            UnregisterGrid(Main_Form.Instance.CampaignView.DataGridViewBlue);
            UnregisterGrid(Main_Form.Instance.CampaignView.DataGridViewRed);
        }

        // 🔧 Nettoyage manuel des events
        // Pourquoi : éviter accumulation des handlers sur Main_Form
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                UnregisterGrid(Main_Form.Instance.CampaignView.DataGridViewBlue);
                UnregisterGrid(Main_Form.Instance.CampaignView.DataGridViewRed);
            }

            base.Dispose(disposing);
        }

        // Garantit qu'il n'y a qu'un seul squad Player dans la campagne
        // Pourquoi : centraliser toute la logique (Init + Active)
        public void SetSinglePlayerSquad(Squad selectedSquad)
        {
            if (selectedSquad == null)
                return;

            foreach (var cs in CurrentCampaignSquads)
            {
                if (cs.Init != null)
                    cs.Init.Player = false;

                if (cs.Active != null)
                    cs.Active.Player = false;
            }

            // Active uniquement celui sélectionné
            selectedSquad.Player = true;

            _campGridLft.RefreshGrids();
        }


        public void modifiedCampaign(string pathFile, string pathFileBackup, string folderName)
        {
            //sauvegarde la fichier oob_air_init pour éviter de l'écraser et le réutiliser si pb
            if (pathFileBackup != null && !File.Exists(pathFileBackup))
            {
                try
                {
                    File.Copy(pathFile, pathFileBackup, true);
                }
                catch (IOException iox)
                {
                    FormUtils.ShowErrorMessage(iox.Message);
                    //MessageBox.Show(iox.Message, "Info");
                }
            }
            //Main_Form.groupBoxCampEdit.Text = nameCamp;

            string[,,,] TEMPtableOobAirBBB = new string[3, 100, 100, 4];

            TEMPtableOobAirBBB = PublicTable.tableOobAiriNIT;


            //active l'unité du squad sélectionné
            foreach (var squad in List_oob_air_Manager.List_oob_air)
            {

                if (squad.Player)
                {
                    squad.Squad_Inactive = false;
                }
            }

            ////ecrit les Class de tous les squad dans le fichier oob_air
            //FormUtils.WriteListClassSquadsToFile(pathFile, folderName);

            if (!List_oob_air_Manager.List_oob_air.Any())
            {
                MessageBox.Show("No squads to save.");
                return;
            }
            // REF: FormMain - remplacer modifiedCampaign
            Saver_Campaign.Save(pathFile, pathFileBackup, folderName);

            MessageBox.Show("Changes saved.", "Report");
        }

        public void buttonSaveChgtCampaign_Click(object sender, EventArgs e)
        {
            string pathFileBackup = ParamConf.PATH_SavedGames_DCS + @"\Mods\tech\DCE\Missions\Campaigns\" + ParamCampaignSelected.NameCampaign + @"\Init\oob_air_init_backup_DTT.lua";

            string pathFile = "";
            string FolderName = "";

            if (Main_Form.Instance.CampaignView.IsOobInit)
            {
                pathFile = ParamConf.PATH_SavedGames_DCS + @"\Mods\tech\DCE\Missions\Campaigns\" + ParamCampaignSelected.NameCampaign + @"\Init\oob_air_init.lua";
                FolderName = "Init";
            }
            else if (Main_Form.Instance.CampaignView.IsOobActive)
            {
                pathFile = ParamConf.PATH_SavedGames_DCS + @"\Mods\tech\DCE\Missions\Campaigns\" + ParamCampaignSelected.NameCampaign + @"\Active\oob_air.lua";

                FolderName = "Active";
            }

            FormUtils.LogRegister("buttonSaveChgtCampaign_Click pathFile: " + pathFile + ", pathFileBackup: " + pathFileBackup + ", FolderName: " + FolderName);

            modifiedCampaign(pathFile, pathFileBackup, FolderName);

            // Recherche le squad actuellement marqué Player = true
            string newType = null;

            foreach (var squad in _form1.currentSquads)
            {
                if (squad.Player)
                {
                    newType = squad.Type;
                    break;
                }
            }

            // Si aucun squad player trouvé, on ne fait rien
            if (!string.IsNullOrEmpty(newType))
            {
                for (int i = 0; i < _form1.dataGridViewCampaigns.Rows.Count; i++)
                {
                    string rowName = _form1.dataGridViewCampaigns.Rows[i].Cells["Name"].Value?.ToString();

                    if (rowName == ParamCampaignSelected.NameCampaign)
                    {
                        // Met à jour la colonne Aircraft
                        _form1.dataGridViewCampaigns.Rows[i].Cells["Aircraft"].Value = newType;

                        // Met à jour aussi l'image si elle dépend du type
                        string imagePath = Path.Combine(
                            ParamConf.PATH_SavedGames_DCS,
                            "Mods", "tech", "DCE", "Missions", "Campaigns",
                            ParamCampaignSelected.NameCampaign,
                            "Images",
                            "planescreen_" + newType + ".png");

                        if (File.Exists(imagePath))
                        {
                            using (var temp = Image.FromFile(imagePath))
                            {
                                _form1.dataGridViewCampaigns.Rows[i].Cells["Image"].Value = new Bitmap(temp);
                            }
                        }

                        break;
                    }
                }
            }

            PublicTable.errorTable.Clear();
            Main_Form.Instance.CampaignView.textBox_Bugs.Text = "";
            Main_Form.Instance.CampaignView.tabPage12.Text = "Bugs";

        }

        //public void buttonSaveActive_Click(object sender, EventArgs e)
        //{
        //    string pathFile = _form1.textBox_SavedGames.Text + @"\Mods\tech\DCE\Missions\Campaigns\" + ParamCampaignSelected.NameCampaign + @"\Active\oob_air.lua";

        //    modifiedCampaign(pathFile, null, "Active");

        //    PublicTable.errorTable.Clear();
        //    _form1.textBox_Bugs.Text = "";
        //    _form1.tabPage12.Text = "Bugs";

        //    //CampaignEdit1(sender, e, pathFile, groupBoxCampEdit.Text);
        //}

        public void buttonResetBackup_Click(object sender, EventArgs e)
        {
            // Initializes the variables to pass to the MessageBox.Show method.
            string message = "Do you really want to go back to the original values?";
            string caption = "Caution";
            MessageBoxButtons buttons = MessageBoxButtons.YesNo;
            DialogResult result;

            // Displays the MessageBox.
            result = MessageBox.Show(message, caption, buttons);
            if (result == System.Windows.Forms.DialogResult.Yes)
            {
                string pathFileBackup = ParamConf.PATH_SavedGames_DCS + @"\Mods\tech\DCE\Missions\Campaigns\" + ParamCampaignSelected.NameCampaign + @"\Init\oob_air_init_backup_DTT.lua";
                string pathFile = ParamConf.PATH_SavedGames_DCS + @"\Mods\tech\DCE\Missions\Campaigns\" + ParamCampaignSelected.NameCampaign + @"\Init\oob_air_init.lua";
                //string pathFile = _form1.textBox_SavedGames.Text + @"\Mods\tech\DCE\Missions\Campaigns\" + ParamCampaignSelected.NameCampaign + @"\Init\oob_air_initCLONE.lua";

                //sauvegarde la fichier oob_air_init pour éviter de l'écraser et le réutiliser si pb
                if (File.Exists(pathFileBackup))
                {
                    try
                    {
                        File.Copy(pathFileBackup, pathFile, true);
                    }
                    catch (IOException iox)
                    {
                        //MessageBox.Show(iox.Message, "Info");
                        FormUtils.ShowErrorMessage(iox.Message);
                    }
                }
            }

            // Remplacez l'appel statique CampaignEdit.LoadSquads(); par un appel sur l'instance CurrentCampaignEdit
            if (this != null)
            {
                this.LoadSquads();
            }


            string path = ParamConf.PATH_SavedGames_DCS + @"\Mods\tech\DCE\Missions\Campaigns\" + ParamCampaignSelected.NameCampaign;

        }

    }
}

