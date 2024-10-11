using System;
using System.Collections.Generic;
using System.Drawing;
using System.Dynamic;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using NLua;
using DCE_Manager;
using DCE_Manager.Utils;
using DCE_Manager.Parameters;
using System.Globalization;

namespace DCE_Manager
{
    
    //internal class CampaignEdit
    public partial class CampaignEdit : Form
    {
        
        // Liste pour stocker les RadioButtons des onglets 1 et 2 (campagne INIT)
        private List<RadioButton> radioButtonGroupInit = new List<RadioButton>();

        // Liste pour stocker les RadioButtons des onglets 3 et 4 (campagne Active)
        private List<RadioButton> radioButtonGroupActive = new List<RadioButton>();

        // Méthode pour ajouter un RadioButton au groupe INIT et gérer l'événement CheckedChanged
        public void AddRadioButtonToInitGroup(RadioButton rBut)
        {
            radioButtonGroupInit.Add(rBut);
            rBut.CheckedChanged += RadioButtonInit_CheckedChanged;
        }

        // Méthode pour ajouter un RadioButton au groupe Active et gérer l'événement CheckedChanged
        public void AddRadioButtonToActiveGroup(RadioButton rBut)
        {
            radioButtonGroupActive.Add(rBut);
            rBut.CheckedChanged += RadioButtonActive_CheckedChanged;
        }

        // Gestionnaire d'événements pour gérer la sélection unique dans le groupe INIT
        private void RadioButtonInit_CheckedChanged(object sender, EventArgs e)
        {
            RadioButton selectedRadioButton = sender as RadioButton;

            if (selectedRadioButton != null && selectedRadioButton.Checked)
            {
                foreach (var radioButton in radioButtonGroupInit)
                {
                    if (radioButton != selectedRadioButton)
                    {
                        radioButton.Checked = false;
                    }
                }
            }
        }

        // Gestionnaire d'événements pour gérer la sélection unique dans le groupe Active
        private void RadioButtonActive_CheckedChanged(object sender, EventArgs e)
        {
            RadioButton selectedRadioButton = sender as RadioButton;

            if (selectedRadioButton != null && selectedRadioButton.Checked)
            {
                foreach (var radioButton in radioButtonGroupActive)
                {
                    if (radioButton != selectedRadioButton)
                    {
                        radioButton.Checked = false;
                    }
                }
            }
        }

        //public static Form1 from;

        private void anycontrol_MouseEnter(object sender, System.EventArgs e)
        {
            var senderControl = sender as System.Windows.Forms.Control;
            if (senderControl == null)
                return;
            senderControl.Focus();
        }


        private void usersCombobox_MouseWheel(object sender, MouseEventArgs e)
        {
            //if (!AllowUsersScrolling)
                ((HandledMouseEventArgs)e).Handled = true;
        }

        
        public void campaignTab_AddNameVar(Form1 form1, Dictionary<string, int> args, Dictionary<string, string> argsString, Squad squad)  //, string[] baseList
        { 
            //Form1
            //    Form1 = form1;
            
            string Name = "";
            string NameAffiche = "";

            int sizeLong = 200;
            if (args["col"] == 1)
            { 
                sizeLong = 75;
                Name = argsString["key"];
                NameAffiche = argsString["key"];

                if(argsString["key"] == "Base")
                {
                    NameAffiche = "Current Base";
                }
                else if (argsString["key"] == "BaseAlternative")
                {
                    NameAffiche = "Base priority" + args["item"].ToString();
                }
            }
            else if(args["col"] == 2)
            {
                Name = argsString["value"];
                NameAffiche = argsString["value"];
            }
            else if (args["col"] == 100)
            { 
                sizeLong = 600;
                Name = argsString["value"];
                NameAffiche = argsString["value"];
            }

            
            foreach (Control cnt in form1.tabPage7.Controls)
            {
                string testName = args["idSquad"].ToString() + "_" + args["sousTable"].ToString() + "_" + Name;
                if (cnt is Label && cnt.Name ==  testName)
                {
                    MessageBox.Show("error207");
                    return;
                }
            }

            //commence ici
            if (((argsString["key"] == "Base" || argsString["key"] == "BaseAlternative") && args["col"] == 2))
            {
                ComboBox label1 = new ComboBox();

                // Obtient l'état et la coalition à partir des arguments
                string state = argsString["state"].ToString();
                string side = argsString["side"].ToString();
                string nameArg1 = args["idSquad"].ToString();
                string nameArg2 = Name;
                string nameArg3 = argsString["tablSup"];

                if (argsString["key"] == "BaseAlternative")
                {
                    nameArg3 = args["item"].ToString();
                }

                // Itérer directement sur les bases aériennes de l'état et de la coalition donnés
                if (PublicTable.Airdrome.ContainsKey(state) && PublicTable.Airdrome[state].ContainsKey(side))
                {
                    foreach (var baseName in PublicTable.Airdrome[state][side])
                    {
                        label1.Items.Add(baseName); // Ajoute chaque nom de base à la ComboBox
                    }
                }

                // Trouve l'index de l'élément qui correspond au nom
                int index = label1.FindString(Name);

                // Si l'index est négatif, l'élément n'a pas été trouvé
                if (index < 0)
                {
                    label1.Text = Name + "***";

                    string infoError = "Please note that this base was not found in the side." + "\r"
                        + Name + "\r"
                        + " not part of this side: " + "\r"
                        + args["sideInt"] + " " + PublicTable.sideToString[args["sideInt"]];

                    if (Name != null && !PublicTable.errorTable.ContainsKey(Name))
                    {
                        PublicTable.errorTable.Add(Name, infoError);
                    }
                    if (Name == null)
                    {
                        // MessageBox.Show("error191, base or baseAlternative : " + argsString["squadName"].ToString());
                        return;
                    }
                }
                else
                {
                    label1.SelectedIndex = index;
                }
                

                // Initialize the controls and their bounds.
                label1.Tag = args["idElement"].ToString() + "|" + argsString["key"];
                label1.Top = args["posV"] * 20;
                label1.Left = args["posH"];

                //label1.Name = args["idSquad"].ToString() + "_" + Name + "_" + argsString["tablSup"];
                label1.Name = nameArg1 + "|" + nameArg2 + "|" + nameArg3;
                label1.Size = new Size(sizeLong, 20);
                label1.AutoSize = false;

                label1.MouseWheel += usersCombobox_MouseWheel;

                //interdire ou pas la modification des valeurs
                if(args["col"] == 2 && form1.LabelStatut.Text == "User")
                { 
                    if (argsString["canBeModified"].IndexOf("all") > -1)
                    {
                        label1.Enabled = false;
                    }
                    else if (argsString["canBeModified"].IndexOf(argsString["key"]) > -1)
                    {
                        label1.Enabled = false;
                    }
                }

                if(argsString["key"] == "Base" && squad.BaseAlternative != null && squad.BaseAlternative.Count > 0)
                {
                    label1.Enabled = false;
                }

                // Add the Label control to the form's control collection.
                if (args["sideInt"] == 1 && args["idPath"] == 1)
                {
                    form1.tabPage7.Controls.Add(label1);
                }
                else if (args["sideInt"] == 2 && args["idPath"] == 1)
                {
                    form1.tabPage8.Controls.Add(label1);
                }
                else if (args["sideInt"] == 1 && args["idPath"] == 2)
                {
                    form1.tabPage9.Controls.Add(label1);
                }
                else if (args["sideInt"] == 2 && args["idPath"] == 2)
                {
                    form1.tabPage10.Controls.Add(label1);
                }

            }
            else
            {
                //TextBox textBox1 = new TextBox();
                Label label1 = new Label();

                // Initialize the controls and their bounds.
                label1.Tag = args["idElement"].ToString() + "|" + argsString["key"];
                label1.Top = args["posV"] * 20;
                label1.Left = args["posH"];

                label1.Text = NameAffiche;//pour debug + args["idSquad"].ToString()
                label1.Name = args["idSquad"].ToString() + "|" + Name + "|" + argsString["tablSup"];
                label1.Size = new Size(sizeLong, 20);
                label1.AutoSize = false;

                //interdire ou pas la modification des valeurs
                if (args["col"] == 2 && form1.LabelStatut.Text == "User")
                {
                    if (argsString["canBeModified"].IndexOf("all") > -1)
                    {
                        label1.Enabled = false;
                    }
                    else if (argsString["canBeModified"].IndexOf(argsString["key"]) > -1)
                    {
                        label1.Enabled = false;
                    }
                }

                // Add the Label control to the form's control collection.
                if (args["sideInt"] == 1 && args["idPath"] == 1)
                {
                    form1.tabPage7.Controls.Add(label1);
                }
                else if (args["sideInt"] == 2 && args["idPath"] == 1)
                {
                    form1.tabPage8.Controls.Add(label1);
                }
                else if (args["sideInt"] == 1 && args["idPath"] == 2)
                {
                    form1.tabPage9.Controls.Add(label1);
                }
                else if (args["sideInt"] == 2 && args["idPath"] == 2)
                {
                    form1.tabPage10.Controls.Add(label1);
                }
            }     
        }

        
        public void campaignTab_AddNumericVar(Form1 form1, Dictionary<string, int> args, Dictionary<string, string> argsString, string[] baseList)
        {

            Form1
                Form1 = form1;
            string Name = "";


            if (args["col"] == 1)
            {
                Name = argsString["key"];
            }
            else if (args["col"] == 2)
            {
                Name = argsString["value"];
            }
            else if (args["col"] == 100)
            {
                Name = argsString["value"];
            }
            foreach (Control cnt in Form1.tabPage7.Controls)
            {
                string testName = args["idSquad"].ToString() + "_" + args["sousTable"].ToString() + "_" + Name;
                if (cnt is Label && cnt.Name == testName)
                {
                    return;
                }
            }

            NumericUpDown control = new NumericUpDown();

            // Initialize the controls and their bounds.
            control.Tag = args["idElement"].ToString() + "|" + argsString["key"];
            control.Top = args["posV"] * 20;
            control.Left = args["posH"];


            if (argsString["value"] != null && argsString["value"] != "")
            {
                control.Value = Int32.Parse(argsString["value"]);
            }
            else if (args["value"] != null)
            {
                control.Value = args["value"];
            }


            control.Name = args["idSquad"].ToString() + "|" + Name + "|" + args["sousTable"].ToString();
            //control.Name = idSquad.ToString() + "|" + idElement.ToString();
            control.Size = new Size(40, 15);
            //control.AutoSize = true;
            control.AutoSize = false;

            control.MouseWheel += usersCombobox_MouseWheel;

            //interdire ou pas la modification des valeurs
            if (args["col"] == 2 && Form1.LabelStatut.Text == "User")
            {
                if (argsString["canBeModified"].IndexOf("all") > -1)
                {
                    control.Enabled = false;
                }
                else if (argsString["canBeModified"].IndexOf(argsString["key"]) > -1)
                {
                    control.Enabled = false;
                }
            }

            // Add the Label control to the form's control collection.
            if (args["sideInt"] == 1 && args["idPath"] == 1)
            {
                Form1.tabPage7.Controls.Add(control);
            }
            else if (args["sideInt"] == 2 && args["idPath"] == 1)
            {
                Form1.tabPage8.Controls.Add(control);
            }
            else if (args["sideInt"] == 1 && args["idPath"] == 2)
            {
                Form1.tabPage9.Controls.Add(control);
            }
            else if (args["sideInt"] == 2 && args["idPath"] == 2)
            {
                Form1.tabPage10.Controls.Add(control);
            }
        }

        public void campaignTab_AddRoster(Form1 form1, Dictionary<string, int> args, Dictionary<string, string> argsString, string[] baseList)
        {

            Form1
                Form1 = form1;

            string Name = "";

            int placeH = 0;
            if (argsString["key"] == "ready")
            {
                placeH = 40;
                Name = "ready";
            }
            else if (argsString["key"] == "damaged")
            { 
                placeH = 130;
                Name = "damaged";
            }
            else if (argsString["key"] == "lost")
            {
                placeH = 220;
                Name = "lost";
            }
            else if (argsString["key"] == "reserve")
            {
                placeH = 325;
                Name = "reserve";
            }
            foreach (Control cnt in Form1.tabPage7.Controls)
            {
                string testName = args["idSquad"].ToString() + "_" + args["sousTable"].ToString() + "_" + Name;
                if (cnt is Label && cnt.Name == testName)
                {
                    return;
                }
            }

            Label control = new Label();

            // Initialize the controls and their bounds.
            control.Tag = args["idElement"].ToString() + "|" + argsString["key"];
            control.Top = args["posV"] * 20;
            control.Left = placeH;
            control.Size = new System.Drawing.Size(80, 20);
            control.Text = Name;
            control.Name = args["idSquad"].ToString() + "|" + Name + "|" + argsString["tablSup"];
            control.AutoSize = false;

            //control.MouseWheel += usersCombobox_MouseWheel;


            Label controlValue = new Label();

            // Initialize the controls and their bounds.
            controlValue.Tag = args["idElement"].ToString() + "|" + argsString["key"];
            controlValue.Top = control.Top + 25;
            controlValue.Left = placeH;
            controlValue.Size = new System.Drawing.Size(80, 20);
            controlValue.Text = argsString["value"];
            controlValue.Name = args["idSquad"].ToString() + "|" + Name + "_Value" + "|" + argsString["tablSup"];
            controlValue.AutoSize = false;

            //controlValue.MouseWheel += usersCombobox_MouseWheel;


            // Add the Label control to the form's control collection.
            if (args["sideInt"] == 1 && args["idPath"] == 1)
            {
                Form1.tabPage7.Controls.Add(control);
                Form1.tabPage7.Controls.Add(controlValue);
            }
            else if (args["sideInt"] == 2 && args["idPath"] == 1)
            {
                Form1.tabPage8.Controls.Add(control);
                Form1.tabPage8.Controls.Add(controlValue);
            }
            else if (args["sideInt"] == 1 && args["idPath"] == 2)
            {
                Form1.tabPage9.Controls.Add(control);
                Form1.tabPage9.Controls.Add(controlValue);
            }
            else if (args["sideInt"] == 2 && args["idPath"] == 2)
            {
                Form1.tabPage10.Controls.Add(control);
                Form1.tabPage10.Controls.Add(controlValue);
            }
        }


        public void campaignTab_AddScore(Form1 form1, Dictionary<string, int> args, Dictionary<string, string> argsString, string[] baseList)
        {

            Form1
                Form1 = form1;

            string Name = "";

            int placeH = 0;
            if (argsString["key"] == "kills_air")
            {
                placeH = 50;
                Name = "Kills air";
            }
            else if (argsString["key"] == "kills_ground")
            {
                placeH = 150;
                Name = "Kills ground";
            }
            else if (argsString["key"] == "kills_ship")
            {
                placeH = 250;
                Name = "Kills ship";
            }

            foreach (Control cnt in Form1.tabPage7.Controls)
            {
                string testName = args["idSquad"].ToString() + "_" + args["sousTable"].ToString() + "_" + Name;
                if (cnt is Label && cnt.Name == testName)
                {
                    return;
                }
            }

            Label control = new Label();

            // Initialize the controls and their bounds.
            control.Tag = args["idElement"].ToString() + "|" + argsString["key"];
            control.Top = args["posV"] * 20;
            control.Left = placeH;
            control.Size = new System.Drawing.Size(80, 20);
            control.Text = Name;
            control.Name = args["idSquad"].ToString() + "|" + Name + "|" + argsString["tablSup"];
            control.AutoSize = false;
            //control.MouseWheel += usersCombobox_MouseWheel;

            Label controlValue = new Label();

            // Initialize the controls and their bounds.
            controlValue.Tag = args["idElement"].ToString() + "|" + argsString["key"];
            controlValue.Top = control.Top + 25;
            controlValue.Left = placeH;
            controlValue.Size = new System.Drawing.Size(80, 20);
            controlValue.Text = argsString["value"];
            controlValue.Name = args["idSquad"].ToString() + "|" + Name + "_Value" + "|" + argsString["tablSup"];
            controlValue.AutoSize = false;
            //controlValue.MouseWheel += usersCombobox_MouseWheel;


            // Add the Label control to the form's control collection.
            if (args["sideInt"] == 1 && args["idPath"] == 1)
            {
                Form1.tabPage7.Controls.Add(control);
                Form1.tabPage7.Controls.Add(controlValue);
            }
            else if (args["sideInt"] == 2 && args["idPath"] == 1)
            {
                Form1.tabPage8.Controls.Add(control);
                Form1.tabPage8.Controls.Add(controlValue);
            }
            else if (args["sideInt"] == 1 && args["idPath"] == 2)
            {
                Form1.tabPage9.Controls.Add(control);
                Form1.tabPage9.Controls.Add(controlValue);
            }
            else if (args["sideInt"] == 2 && args["idPath"] == 2)
            {
                Form1.tabPage10.Controls.Add(control);
                Form1.tabPage10.Controls.Add(controlValue);
            }
        }

        //ajoute AddCheckBox
        public void campaignTab_AddCheckBox(Form1 form1, Dictionary<string, int> args, Dictionary<string, string> argsString, string[] baseList)
        {
            Form1
             Form1 = form1;
            string Name = "";

            foreach (Control cnt in Form1.tabPage7.Controls)
            {
                if (cnt is CheckBox && cnt.Name == args["idSquad"] + "_" + args["sousTable"] + "_" +  Name)
                {
                    return;
                }
            }

            CheckBox box = new CheckBox();
            box.Tag = args["idElement"].ToString() + "|" + argsString["key"];
            box.Name = args["idSquad"].ToString() + "|" + argsString["key"] + "|" + argsString["tablSup"];
            //box.Text = args["idSquad"].ToString() + "_" + args["idElement"].ToString();
            box.AutoSize = false;
            //box.AutoSize = true;
            box.Size = new Size(15, 15);
            box.Location = new Point(args["posH"], args["posV"] * 20);

            if (argsString["value"] != null && argsString["value"] != "")
            {
                box.Checked = Convert.ToBoolean(argsString["value"]);
            }
            else
            {
                box.Checked = args["value"] != 0 ;
            }

            if (args["col"] == 2 && Form1.LabelStatut.Text == "User")
            {
                if (argsString["canBeModified"].IndexOf("all") > -1)
                {
                    box.Enabled = false;
                    //Form1.toolTip1.SetToolTip(box, "Locked by campaign creator(*)");
   
                    Form1.toolTip1.Show("Locked by campaign creator(*)", box, 0, 0, 3000);
                    
                }
                else if (argsString["canBeModified"].IndexOf(argsString["key"]) > -1)
                {
                    box.Enabled = false;
                    //Form1.toolTip1.SetToolTip(box, "Locked by campaign creator(*)");
                    Form1.toolTip1.Show("Locked by campaign creator(*)", box, 0, 0, 3000);
                }

                if (argsString["key"] == "active" || argsString["key"] == "inactive")
                {
                    if (argsString["canBeModified"].IndexOf("active") > -1)
                    {
                        //Form1.toolTip1.SetToolTip(box, "Locked by campaign creator(*)");
                        Form1.toolTip1.Show("Locked by campaign creator(*)", box, 0, 0, 3000);
                    }
                }
                //tablSup
                if (argsString["tablSup"] != null && argsString["tablSup"] != "" && argsString["canBeModified"].IndexOf(argsString["tablSup"]) > -1)
                {
                    box.Enabled = false;
                    //Form1.toolTip1.SetToolTip(box, "Locked by campaign creator(*)");
                    Form1.toolTip1.Show("Locked by campaign creator(*)", box, 0, 0, 3000);
                }
            }


            if (args["sideInt"] == 1 && args["idPath"] == 1)
            {
                Form1.tabPage7.Controls.Add(box);
            }
            else if (args["sideInt"] == 2 && args["idPath"] == 1)
            {
                Form1.tabPage8.Controls.Add(box);
            }
            else if (args["sideInt"] == 1 && args["idPath"] == 2)
            {
                Form1.tabPage9.Controls.Add(box);
            }
            else if (args["sideInt"] == 2 && args["idPath"] == 2)
            {
                Form1.tabPage10.Controls.Add(box);
            }

        }

        //ajoute RadioButton
        public void campaignTab_AddRadioButton(Form1 form1, Dictionary<string, int> args, Dictionary<string, string> argsString, int idElement, int posH, int posV, bool checkValue, int sousTable, int idPath)
        {
            Form1
             Form1 = form1;
            
            int sideInt = args["sideInt"];

            RadioButton rBut = new RadioButton();
            rBut.Tag = idElement.ToString() + "|" + argsString["key"];
            rBut.Checked = checkValue;
            rBut.Name = args["idSquad"].ToString() + "|" + argsString["key"] + "|" + argsString["tablSup"];
            //rBut.Text = args["idSquad"].ToString() + "|" + idElement.ToString();
            rBut.AutoSize = false;
            //box.AutoSize = true;
            rBut.Size = new Size(15, 15);
            rBut.Location = new Point(posH, posV * 20);

        
         
            if (sideInt == 1 && idPath == 1)
            {
                Form1.tabPage7.Controls.Add(rBut);
                AddRadioButtonToInitGroup(rBut);
            }
            else if (sideInt == 2 && idPath == 1)
            {
                Form1.tabPage8.Controls.Add(rBut);
                AddRadioButtonToInitGroup(rBut);
            }
            else if (sideInt == 1 && idPath == 2)
            {
                Form1.tabPage9.Controls.Add(rBut);
                AddRadioButtonToActiveGroup(rBut);
            }
            else if (sideInt == 2 && idPath == 2)
            {
                Form1.tabPage10.Controls.Add(rBut);
                AddRadioButtonToActiveGroup(rBut);
            }

        }


        public CampaignEdit(Form1 form1, string nameCamp)
        {

            form1.tabPage7.Controls.Clear();
            form1.tabPage8.Controls.Clear();
            form1.tabPage9.Controls.Clear();
            form1.tabPage10.Controls.Clear();

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

            lua.DoFile(SharedData.textBox_SavedGames + @"\Mods\tech\DCE\ScriptsMod.NG\DCEM_Function.lua");

            var result = lua.DoFile(SharedData.textBox_SavedGames + @"\Mods\tech\DCE\ScriptsMod.NG\DCEM_Function.lua");

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
            LuaTable playable_mLua = (LuaTable)luaTable["playable_m"];
            List<string> playableList = new List<string>();

            // Parcourir la table Lua
            foreach (var plane in playable_mLua.Keys)
            {
                playableList.Add(plane.ToString());
            }



            //*************************************************************************
            //CREATION DES TABLES BASE ********************************************************
            //PARSE les fichiers BASE ********************************************************
            //*************************************************************************

            //string nameCamp = "YourCampaignName";  // Remplacez par le nom de votre campagne
            string savedGamesPath = SharedData.textBox_SavedGames;


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
                string path_oob_airFile = SharedData.textBox_SavedGames + @"\Mods\tech\DCE\Missions\Campaigns\" + nameCamp + @"\Init\oob_air_init.lua";
                path_oob_air.Add(1, path_oob_airFile); //adding a key/value using the Add() method

                path_oob_airFile = SharedData.textBox_SavedGames + @"\Mods\tech\DCE\Missions\Campaigns\" + nameCamp + @"\Active\oob_air.lua";
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

                    if(File.Exists(pathFileB))
                    {
                        ParamCampaign.NameFileParse = pathFileB;

                        C_oobAir.Parse(File.ReadAllText(pathFileB));

                        //List<Squad> List_oob_air = new List<Squad>();
                        
                        int tableN = 0;
                        
                        int sideInt = 0;
                        //int idSquad = -1;
                        foreach (KeyValuePair<string, LuaObject> entry in C_oobAir.Val)
                        {
                            int nbLigne = 0;
                            string side = entry.Key;


                            if (side == "blue")
                            {
                                sideInt = 1;
                            }
                            else if (side == "red")
                            {
                                sideInt = 2;
                            }

                            tableN = 1;

                            foreach (KeyValuePair<string, LuaObject> entry2 in entry.Value)
                            {
                                //Boolean beShown = true;
                                int[] firstPos = new int[50];

                                int idElement = 0;
                                tableN = 2;
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
                                    tableN = 3;

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
                                            tableN = 4;
                                            //string cleanWord = ;

                                            string baseName = entry4.Value.luaobj.ToString().Trim('\"');
                                            squad.BaseAlternative.Add(baseName);

                                        }

                                    }
                                    else if (entry3.Key == "number")// && d == 1
                                    {
                                        squad.Number = Convert.ToInt32(entry3.Value.luaobj);
                                    }
                                    else if (entry3.Key == "InitNumber")// && d == 2
                                    {
                                        squad.InitNumber = Convert.ToInt32(entry3.Value.luaobj);
                                    }
                                    else if (entry3.Key == "reserve")//&& d == 1
                                    {
                                        squad.Reserve = Convert.ToInt32(entry3.Value.luaobj);
                                    }
                                    else if (entry3.Key == "InitReserve")// && d == 2
                                    {
                                        squad.InitReserve = Convert.ToInt32(entry3.Value.luaobj);
                                    }
                                    else if (entry3.Key == "tasks")
                                    {

                                        squad.Tasks = new Dictionary<string, object>();
                                        
                                        foreach (KeyValuePair<string, LuaObject> entry4 in entry3.Value)
                                        {
                                            idElement++;
                                            tableN = 4;

                                            squad.Tasks.Add(entry4.Key, Convert.ToBoolean(entry4.Value.luaobj));

                                        }
                                    }
                                    else if (entry3.Key == "tasksCoef")
                                    {
                                        squad.TasksCoef = new Dictionary<string, object>();
                                        
                                        foreach (KeyValuePair<string, LuaObject> entry4 in entry3.Value)
                                        {
                                            idElement++;
                                            tableN = 4;
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
                                            //squad.TasksCoef.Add(entry4.Key, Convert.ToDouble(entry4.Value.luaobj));

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
                                            tableN = 4;
                                            squad.Roster.Add(entry4.Key, Convert.ToInt32(entry4.Value.luaobj));
                                        }
                                    }
                                    else if (entry3.Key == "score")
                                    {
                                     
                                        squad.Score = new Dictionary<string, object>();

                                        foreach (KeyValuePair<string, LuaObject> entry4 in entry3.Value)
                                        {
                                            idElement++;
                                            tableN = 4;
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

                                
                                //**********************************************************************************
                                //**************** ici on troll la class squad *************************************
                                //**************** *****************************************************************

                                nbLigne++;
                                idElement++;
                                tableN = 3;

                                for (int r = 0; r < firstPos.Length; r++)
                                {
                                    firstPos[r] = -1;
                                }
                                
                                var args = new Dictionary<string, int>();
                                var argsString = new Dictionary<string, string>();

                                bool checkValue = false;

                                args["sideInt"] = sideInt;
                                args["col"] = 1;
                                args["idSquad"] = squad.IdSquad;
                                args["idElement"] = idElement;
                                args["idPath"] = d;
                                args["sousTable"] = tableN;

                                //argsString["key"] = entry3.Key;
                                //argsString["value"] = entry3.Value.luaobj.ToString(); //entry2.Value.luaobj.ToString()
                                argsString["canBeModified"] = "";
                                argsString["tablSup"] = entry2.Key;

                                int posHCol1 = 20 * (tableN - 3);
                                int posHCol2 = posHCol1 + 150 ;


                                //if (entry3.Key == "name")

                                argsString["key"] = "Name";
                                argsString["value"] = squad.Name;
                                argsString["squadName"] = squad.Name;

                                args["col"] = 1; args["posH"] = posHCol1; args["posV"] = nbLigne;
                                campaignTab_AddNameVar(form1, args, argsString, null);
                                args["col"] = 2; args["posH"] = posHCol2;
                                campaignTab_AddNameVar(form1, args, argsString, null);
                                nbLigne++;

                                //if (entry3.Key == "player")

                                //if (playableList(squad.Type))
                                if (playableList.Contains(squad.Type))
                                {
                                    argsString["key"] = "Player";
                                    checkValue = squad.Player;

                                    args["col"] = 1; args["posH"] = posHCol1; args["posV"] = nbLigne;
                                    campaignTab_AddNameVar(form1, args, argsString, null);
                                    args["col"] = 2; args["posH"] = posHCol2;
                                    campaignTab_AddRadioButton(form1, args, argsString, idElement, posHCol2, nbLigne, checkValue, tableN, d);
                                    nbLigne++;
                                }



                                //if (entry3.Key == "type")
                                argsString["key"] = "Type";
                                argsString["value"] = squad.Type;

                                args["col"] = 1; args["posH"] = posHCol1; args["posV"] = nbLigne;
                                campaignTab_AddNameVar(form1, args, argsString, null);
                                args["col"] = 2; args["posH"] = posHCol2;
                                campaignTab_AddNameVar(form1, args, argsString, null);
                                nbLigne++;


                                //if (entry3.Key == "country")

                                argsString["key"] = "Country";
                                argsString["value"] = squad.Country;

                                args["col"] = 1; args["posH"] = posHCol1; args["posV"] = nbLigne;
                                campaignTab_AddNameVar(form1, args, argsString, null);
                                args["col"] = 2; args["posH"] = posHCol2;
                                campaignTab_AddNameVar(form1, args, argsString, null);
                                nbLigne++;


                                //if (entry3.Key == "skill")

                                argsString["key"] = "Skill";
                                argsString["value"] = squad.Skill;

                                args["col"] = 1; args["posH"] = posHCol1; args["posV"] = nbLigne;
                                campaignTab_AddNameVar(form1, args, argsString, null);
                                args["col"] = 2; args["posH"] = posHCol2;
                                campaignTab_AddNameVar(form1, args, argsString, null);
                                nbLigne++;

                                //if (entry3.Key == "base")
                                argsString["key"] = "Base";
                                argsString["value"] = squad.Base;
                                argsString["state"] = folderFile;
                                argsString["side"] = side;

                                args["col"] = 1; args["posH"] = posHCol1; args["posV"] = nbLigne;
                                campaignTab_AddNameVar(form1, args, argsString, null);
                                args["col"] = 2; args["posH"] = posHCol2;
                                campaignTab_AddNameVar(form1, args, argsString, squad);
                                nbLigne++;


                                //if (entry3.Key == "baseAlternative")
                                if (squad.BaseAlternative != null && squad.BaseAlternative.Count > 0)
                                {
                                    int item = 0;
                                    foreach (var baseAlt in squad.BaseAlternative)
                                    {
                                        argsString["key"] = "BaseAlternative";
                                        argsString["value"] = baseAlt;
                                        argsString["state"] = folderFile;
                                        argsString["side"] = side;
                                        args["item"] = item;

                                        args["col"] = 1; args["posH"] = posHCol1; args["posV"] = nbLigne;
                                        campaignTab_AddNameVar(form1, args, argsString, squad);
                                        args["col"] = 2; args["posH"] = posHCol2;
                                        campaignTab_AddNameVar(form1, args, argsString, squad);

                                        item++;
                                        nbLigne++;
                                    }
                                }

                                if (d == 1)
                                {
                                    argsString["key"] = "Number";
                                    argsString["value"] = "";
                                    args["value"] = squad.Number;

                                    args["col"] = 1; args["posH"] = posHCol1; args["posV"] = nbLigne;
                                    campaignTab_AddNameVar(form1, args, argsString, null);
                                    args["col"] = 2; args["posH"] = posHCol2;
                                    campaignTab_AddNumericVar(form1, args, argsString, null);
                                    nbLigne++;
                                }

                                //if (entry3.Key == "InitNumber" && d == 2)
                                if (d == 2)
                                { 
                                    argsString["key"] = "InitNumber";
                                    argsString["value"] = "";
                                    args["value"] = squad.InitNumber;

                                    args["col"] = 1; args["posH"] = posHCol1; args["posV"] = nbLigne;
                                    campaignTab_AddNameVar(form1, args, argsString, null);
                                    args["col"] = 2; args["posH"] = posHCol2;
                                    campaignTab_AddNumericVar(form1, args, argsString, null);
                                    nbLigne++;
                                }

                                //if (entry3.Key == "reserve" && d == 1)
                                if (d == 1)
                                {
                                    argsString["key"] = "Reserve";
                                    argsString["value"] = "";
                                    args["value"] = squad.Reserve;

                                    args["col"] = 1; args["posH"] = posHCol1; args["posV"] = nbLigne;
                                    campaignTab_AddNameVar(form1, args, argsString, null);
                                    args["col"] = 2; args["posH"] = posHCol2;
                                    campaignTab_AddNumericVar(form1, args, argsString, null);
                                    nbLigne++;
                                }

                                //if (entry3.Key == "InitReserve" && d == 2)
                                if (d == 2)
                                {
                                    argsString["key"] = "InitReserve";
                                    argsString["value"] = "";
                                    args["value"] = squad.InitReserve;

                                    args["col"] = 1; args["posH"] = posHCol1; args["posV"] = nbLigne;
                                    campaignTab_AddNameVar(form1, args, argsString, null);
                                    args["col"] = 2; args["posH"] = posHCol2;
                                    campaignTab_AddNumericVar(form1, args, argsString, null);
                                    nbLigne++;
                                }



                                idElement++;
                                tableN = 4;

                                //var dicoTasks = new Dictionary<string, int>
                                //{
                                //    { "Intercept", 9 },
                                //    { "CAP", 10 },
                                //    { "Escort", 11 },
                                //    { "Fighter Sweep", 12 },
                                //    { "Strike", 13 },

                                //    { "Anti-ship Strike", 14 },
                                //    { "Runway Attack", 15 },
                                //    { "SEAD", 16 },
                                //    { "Laser Illumination", 17 },
                                //    { "Escort Jammer", 18 },
                                //    { "AWACS", 19 },

                                //    { "Refueling", 20 },
                                //    { "Transport", 21 },
                                //    { "SAR", 22 },
                                //    { "CSAR", 23 },

                                //};
                                

                                //if (entry3.Key == "tasks")

                                var tasksForPlane = taskByPlane[squad.Type];

                                args["value"] = 0;
                                if (squad.Tasks != null)
                                {
                                  
                                    //foreach (var task in dicoTasks)
                                    foreach (var task in tasksForPlane)
                                    {
                                        idElement++;
                                        tableN = 4;

                                        args["idElement"] = idElement;
                                        args["sousTable"] = tableN;
                                        argsString["key"] = task.Key;

                                        if (squad.Tasks.ContainsKey(task.Key))
                                        {
                                             argsString["value"] = squad.Tasks[task.Key].ToString();                                       
                                        }
                                        else
                                        {
                                            argsString["value"] = "false";
                                            squad.Tasks.Add(task.Key, false);
                                        }
                                        
                                        argsString["canBeModified"] = "";
                                        argsString["tablSup"] = "Tasks";

                                        args["col"] = 1; args["posH"] = posHCol1 + 20; args["posV"] = nbLigne;
                                        campaignTab_AddNameVar(form1, args, argsString, null);
                                        args["col"] = 2; args["posH"] = posHCol2 + 20;
                                        campaignTab_AddCheckBox(form1, args, argsString, null);
                                        nbLigne++;
                                       
                                    }
                                }


                                ////if (entry3.Key == "TasksCoef")

                                //args["value"] = 0;
                                //if (squad.TasksCoef != null)
                                //{
                                //    foreach (var task in dicoTasks)
                                //    {
                                //        if (squad.TasksCoef.ContainsKey(task.Key))
                                //        {
                                //            idElement++;
                                //            tableN = 4;

                                //            args["idElement"] = idElement;
                                //            args["sousTable"] = tableN;
                                //            argsString["key"] = task.Key;
                                //            argsString["value"] = squad.TasksCoef[task.Key].ToString();


                                //            argsString["canBeModified"] = "";
                                //            argsString["tablSup"] = "TasksCoef";

                                //            args["col"] = 1; args["posH"] = posHCol1; args["posV"] = nbLigne;
                                //            campaignTab_AddNameVar(form1, args, argsString, null);
                                //            args["col"] = 2; args["posH"] = posHCol2;
                                //            campaignTab_AddNumericVar(form1, args, argsString, null);
                                //            nbLigne++;
                                //        }

                                //    }
                                //}


                                //if (entry3.Key == "inactive")

                                if(squad.Name == "47th FS")
                                { }

                                argsString["key"] = "Active";
                                 
                                args["col"] = 1; args["posH"] = posHCol1; args["posV"] = nbLigne;
                                campaignTab_AddNameVar(form1, args, argsString, null);
                                args["col"] = 2; args["posH"] = posHCol2;
                                if (squad.Inactive == false)
                                {
                                    argsString["value"] = "true";
                                }
                                else if (squad.Inactive == true)
                                {
                                    argsString["value"] = "false";
                                }
                                   
                                campaignTab_AddCheckBox(form1, args, argsString, null);
                                nbLigne++;

                                
                                //if (entry3.Key == "roster")
                                var dicoRoster = new Dictionary<string, int>
                                {
                                    { "ready", 26 },
                                    { "damaged", 27 },
                                    { "lost", 28 },
                                    { "reserve", 29 },

                                     { "kills_air", 30 },
                                     { "kills_ground", 31 },
                                     { "kills_ship", 32 },
                                     
                                };
                                args["value"] = 0;
                                foreach (var roster in dicoRoster)
                                {
                                    if (squad.Roster != null && squad.Roster.ContainsKey(roster.Key))
                                    {
                                        idElement++;
                                        tableN = 4;

                                        args["idElement"] = idElement;
                                        args["sousTable"] = tableN;
                                        argsString["key"] = roster.Key;
                                        argsString["value"] = squad.Roster[roster.Key].ToString();
                                        
                                        argsString["canBeModified"] = "";
                                        argsString["tablSup"] = "Roster";
                                        
                                        args["col"] = 1; args["posH"] = posHCol1; args["posV"] = nbLigne;
                                        campaignTab_AddRoster(form1, args, argsString, null);
                                        //nbLigne++;
                                    }
                                }

                                if (squad.Roster != null)
                                    nbLigne++;


                                //if (entry3.Key == "score")
                                var dicoScore = new Dictionary<string, int>
                                {
                                    { "kills_air", 30 },
                                    { "kills_ground", 31 },
                                    { "kills_ship", 32 },
                                    
                                };
                                args["value"] = 0;
                                foreach (var score in dicoScore)
                                {
                                    if (squad.Roster != null && squad.Roster.ContainsKey(score.Key))
                                    {
                                        idElement++;
                                        tableN = 4;

                                        args["idElement"] = idElement;
                                        args["sousTable"] = tableN;
                                        argsString["key"] = score.Key;
                                        argsString["value"] = squad.Roster[score.Key].ToString();

                                        argsString["canBeModified"] = "";
                                        argsString["tablSup"] = "Score";

                                        args["col"] = 1; args["posH"] = posHCol1; args["posV"] = nbLigne;
                                        campaignTab_AddRoster(form1, args, argsString, null);
                                        //nbLigne++;
                                    }
                                }

                                if (squad.Score != null)
                                    nbLigne++;
                                
                            }
                        }// fileExist()
                    }
                }

                
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

                    //autorise la parsing du fichier en fonction du nb de mission joué
                    // si c'est le début, on autorise que le Init/etc
                    //si la campagne a commencé,  on n'autorise que le Active/etc
                    //bool autoriseParse = false;

                    //if (Form1.ParamCampaign.NbMission == 0 && d == 1)
                    //{
                    //    autoriseParse = true;

                    //}
                    //else if (Form1.ParamCampaign.NbMission >= 1 && d == 2)
                    //{
                    //    autoriseParse = true;
                    //}

                    if (ParamDivers.NewParseOobAir == false)
                    {
                       
                    }
                   

                    //*************************************************************************
                    //RECUPERATION Briefing Campaign ********************************************************
                    //PARSE camp_trigger_init ********************************************************
                    //*************************************************************************

                    string campTrigger = SharedData.textBox_SavedGames + @"\Mods\tech\DCE\Missions\Campaigns\" + nameCamp + @"\Init\camp_triggers_init.lua";

                    LuaParse C_campTrigger = new LuaParse();

                    ParamCampaign.NameFileParse = campTrigger;

                    C_campTrigger.Parse(File.ReadAllText(campTrigger));

                    string BriefingCampaign = "";

                    foreach (KeyValuePair<string, LuaObject> entry1 in C_campTrigger.Val)
                    {
                        foreach (KeyValuePair<string, LuaObject> entry2 in entry1.Value)
                        {
                            if ((entry1.Key == "Campaign Briefing"))
                            {
                                //Form1.LogRegister("entry2.Key "+ entry2.Key);
                            }

                            if ((entry1.Key == "Campaign Briefing" & entry2.Key == "action"))
                            {
                                //Form1.LogRegister("passe  Campaign Briefing & entry2.Key == action " + entry2.Key);

                                foreach (KeyValuePair<string, LuaObject> entry3 in entry2.Value)
                                {
                                    //Form1.LogRegister("foreach entry3 " + entry3.Value.luaobj.ToString());

                                    if (entry3.Value.luaobj.ToString().IndexOf("Action.AddImage") > -1)
                                    {
                                        //Form1.LogRegister("passe  break " + entry3.Value.luaobj.ToString());
                                        break;
                                    }
                                    BriefingCampaign = BriefingCampaign + entry3.Value.luaobj.ToString();

                                    //Form1.LogRegister("BriefingCampaign " + BriefingCampaign);

                                }
                            }
                        }
                    }

                    //Form1.LogRegister("BriefingCampaign "+BriefingCampaign);

                    BriefingCampaign = BriefingCampaign.Replace("'Action.Text(\"", "").Replace("\")'", "\r\n").Replace("\"", "\r\n");

                    //BriefingCampaign = BriefingCampaign.Replace(" 'Action.Text(\"", "").Replace("\")'", "\r\n").Replace("\"", "\r\n");


                    BriefingCampaign = Regex.Replace(BriefingCampaign, @"\[.\]=", "");

                    form1.textBoxCampBriefing.Text = BriefingCampaign;


                    //*************************************************************************
                    //IMAGE Campagne **********************************************************
                    // ************************************************************************
                    //*************************************************************************

                    string campPath = SharedData.textBox_SavedGames + @"\Mods\tech\DCE\Missions\Campaigns\" + nameCamp;
                    //Form1.pictureBoxCampImage.Image = Image.FromFile(campPath + ".bmp");

                    string imagePath = campPath + ".bmp";
                    using (Image image = Image.FromFile(imagePath, true))
                    {
                        form1.pictureBoxCampImage.Image?.Dispose();
                        form1.pictureBoxCampImage.Image = new Bitmap(image);
                    }


                    //*************************************************************************
                    //AFFICHAGE DU RESULTAT OOB AIR *******************************************
                    //*************************************************************************
                    if (ParamDivers.NewParseOobAir == false)
                    {
                        
                    }
                    
                }
                   
            }   ////PARSE LEs FICHIERs //oob_air_init et //oob_air
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
        }

    }
}