// Fichier à remplacer : FormSquadEdit.cs
// But : remplir automatiquement la grande fenêtre avec toutes les données du squad.
// Pourquoi : permettre d'afficher et modifier immédiatement toutes les variables connues et futures.

using System;
using System.Collections;

//using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

//using System.Web.UI.WebControls;
using System.Windows.Forms;
using DCE_Manager.Parameters;
using DCE_Manager.Utils;

namespace DCE_Manager
{
    public partial class FormSquadEdit : Form
    {
        public Squad EditedSquad { get; private set; }

        private readonly CampaignContext _campaignContext;

        // Cette liste mémorise les contrôles dynamiques des tâches.
        // Pourquoi : récupérer facilement les valeurs lors du Save.
        private readonly List<TaskRow> _taskRows = new List<TaskRow>();

        // Cette liste mémorise les contrôles dynamiques des variables additionnelles.
        // Pourquoi : permettre plus tard d'éditer également les nouvelles variables Lua.
        private readonly List<AdditionalRow> _additionalRows = new List<AdditionalRow>();

        private bool _isRefreshingCallsign;

        private readonly bool _isActiveVersion;


        // Event déclenché à chaque modification importante du squad
        public event Action SquadUpdated;


        //CONSTRUCTEUR
        public FormSquadEdit(Squad squad, CampaignContext campaignContext, bool cloneMode = false, String txt="")
        {
            InitializeComponent();

            textBoxName.TextChanged += (s, e) =>
            {
                EditedSquad.Name = textBoxName.Text.Trim();
                EditedSquad.DisplayName = textBoxName.Text.Trim();
                SquadUpdated?.Invoke();
            };

            _campaignContext = campaignContext;

            _isActiveVersion = squad.FolderFile == "Active";

            if (squad == null)
            {
                MessageBox.Show("Squad NULL lors de l'ouverture", "Erreur");
                return;
            }

            // On clone le squad pour éviter de modifier l'original avant Save.
            // Pourquoi : l'utilisateur peut encore annuler.
            EditedSquad = cloneMode ? CloneSquad(squad) : squad;

            Text = cloneMode
            ? "Clone Squad - " + EditedSquad.DisplayName
            : "Edit Squad - " + EditedSquad.DisplayName;

            LoadStaticLists();
            FillControls();
            BuildGenericTables();
            BuildBase();
            BuildBasesAlternative();
            BuildCallSign();
            RefreshCallsignList();
            comboBoxCountry.SelectedIndexChanged += comboBoxCountry_SelectedIndexChanged;
            BuildParkingId();
            BuildLiveryArea();
            BuildLiveryModex();
            BuildTasksArea();
            BuildScoreArea();
           
           
        }

        // Cette fonction crée une copie manuelle du squad.
        // Pourquoi : éviter que le mode clone modifie le squad original.
        private Squad CloneSquad(Squad source)
        {
            // Clone de base automatique
            // Pourquoi : éviter de recopier chaque propriété manuellement
            Squad clone = source.Clone();

            // -------------------------------------------------
            // Copie profonde des collections
            // Pourquoi : éviter que clone et original partagent les mêmes références
            // -------------------------------------------------

            clone.BaseAlternative = source.BaseAlternative != null
                ? new List<string>(source.BaseAlternative)
                : new List<string>();

            clone.Tasks = source.Tasks != null
                ? new Dictionary<string, bool>(source.Tasks)
                : new Dictionary<string, bool>();

            clone.TasksCoef = source.TasksCoef != null
                ? new Dictionary<string, double>(source.TasksCoef)
                : new Dictionary<string, double>();

            clone.Roster = source.Roster != null
                ? new Dictionary<string, object>(source.Roster)
                : new Dictionary<string, object>();

            clone.Score = source.Score != null
                ? new Dictionary<string, object>(source.Score)
                : new Dictionary<string, object>();

            clone.AdditionalProperties = source.AdditionalProperties != null
                ? new Dictionary<string, object>(source.AdditionalProperties)
                : new Dictionary<string, object>();

            clone.parking_id = source.parking_id != null
                ? new Dictionary<string, object>(source.parking_id)
                : new Dictionary<string, object>();

            clone.Livery = source.Livery != null
                ? new Dictionary<int, string>(source.Livery)
                : new Dictionary<int, string>();

            clone.LiveryModex = source.LiveryModex != null
                ? new Dictionary<int, string>(source.LiveryModex)
                : new Dictionary<int, string>();

            // -------------------------------------------------
            // Nouveau nom unique
            // Pourquoi : éviter conflits Lua/DCE
            // -------------------------------------------------

            clone.Name = GenerateCloneName(source.Name);
            clone.DisplayName = clone.Name;

            // -------------------------------------------------
            // Nouvel ID unique
            // Pourquoi : DCE utilise IdSquad comme identifiant interne
            // -------------------------------------------------

            clone.IdSquad = GenerateNewSquadId();

            // -------------------------------------------------
            // Le clone ne doit pas être Player par défaut
            // Pourquoi : éviter plusieurs Player squads
            // -------------------------------------------------

            clone.Player = false;

            return clone;
        }

        // Génère un nom de clone unique
        // Pourquoi : éviter les doublons dans la campagne
        private string GenerateCloneName(string baseName)
        {
            string candidate = baseName + "_Copy";
            int index = 1;

            var existingNames = _campaignContext.CampaignEditRef
                .CurrentCampaignSquads
                .SelectMany(s => new[] { s.Active, s.Init })
                .Where(s => s != null)
                .Select(s => s.Name)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            while (existingNames.Contains(candidate))
            {
                candidate = baseName + "_Copy" + index;
                index++;
            }

            return candidate;
        }

        // Génère un nouvel IdSquad unique
        // Pourquoi : éviter collisions entre squads
        private int GenerateNewSquadId()
        {
            var usedIds = _campaignContext.CampaignEditRef
                .CurrentCampaignSquads
                .SelectMany(s => new[] { s.Active, s.Init })
                .Where(s => s != null)
                .Select(s => s.IdSquad);

            if (!usedIds.Any())
                return 1;

            return usedIds.Max() + 1;
        }

        // Cette fonction remplit les listes connues des ComboBox.
        // Pourquoi : proposer directement les choix possibles à l'utilisateur.
        private void LoadStaticLists()
        {

            comboBoxCountry.Items.Clear();

            if (_campaignContext != null &&
                _campaignContext.LuaData != null &&
                _campaignContext.LuaData.Country != null)
            {
                comboBoxCountry.Items.AddRange(
                    _campaignContext.LuaData.Country.ToArray()
                );
            }

            comboBoxType.Items.Clear();

            if (_campaignContext != null &&
                _campaignContext.LuaData != null &&
                _campaignContext.LuaData.AllPlaneHeli != null)
            {
                comboBoxType.Items.AddRange(_campaignContext.LuaData.AllPlaneHeli.ToArray());
            }

        }



        private void BuildBase()
        {
            comboBoxBase.BeginUpdate();


            comboBoxBase.Items.Clear();

            if (_campaignContext != null &&
                _campaignContext.Airbases != null)
            {
                IEnumerable<AirbaseInfo> bases = _campaignContext.Airbases.Values;

                // Si le squad a un side connu, on filtre la liste.
                if (!string.IsNullOrEmpty(EditedSquad.SideString))
                {
                    bases = bases.Where(a =>
                        string.Equals(a.Side, EditedSquad.SideString,
                            StringComparison.OrdinalIgnoreCase));
                }

                foreach (AirbaseInfo airbase in bases.OrderBy(a => a.Name))
                {
                    comboBoxBase.Items.Add(airbase.Name);
                    comboBox_All_bases.Items.Add(airbase.Name);
                }
            }

            // On garde la base actuelle visible même si elle n'est plus dans la liste.
            if (!string.IsNullOrEmpty(EditedSquad.Base))
            {
                if (!comboBoxBase.Items.Contains(EditedSquad.Base))
                {
                    comboBoxBase.Items.Add(EditedSquad.Base);
                }

                comboBoxBase.SelectedItem = EditedSquad.Base;
            }

            comboBoxBase.EndUpdate();
        }


        // Cette fonction charge les propriétés simples du squad dans les contrôles.
        // Pourquoi : préremplir immédiatement la fenêtre d'édition.
        private void FillControls()
        {
            //textBoxName.Text = EditedSquad.Name;
            textBoxName.Text = EditedSquad.DisplayName;

            comboBoxType.Text = EditedSquad.Type;
            comboBoxCountry.Text = EditedSquad.Country;
            comboBoxBase.Text = EditedSquad.Base;
            comboBoxSkill.Text = EditedSquad.Skill;

            checkBoxPlayer.Checked = EditedSquad.Player;
            checkBox_HumainOnly.Checked = EditedSquad.HumainOnly;
            //checkBoxInactive.Checked = EditedSquad.Squad_Inactive;
            checkBoxActive.Checked = EditedSquad.Squad_Active;

            numericNumber.Value = EditedSquad.Number;
            //numericInitNumber.Value = EditedSquad.InitNumber;
            numericReserve.Value = EditedSquad.Reserve;
            //numericInitReserve.Value = EditedSquad.InitReserve;

            textBox_ModexNb_Min.Text = EditedSquad.SideNumberMin.ToString();
            textBox_ModexNb_Max.Text = EditedSquad.SideNumberMax.ToString();
        }


        //***********************BASE ALT START

        // Cette fonction remplit la ListBox des bases alternatives.
        // Pourquoi : afficher uniquement les bases de repli (sans la base principale).
        private void BuildBasesAlternative()
        {
            listBoxBasesAlternat.Items.Clear();

            var baseAlterList = EditedSquad.BaseAlternative;

            if (baseAlterList == null)
                return;

            foreach (var entry in baseAlterList)
            {
                // Sécurité : ne jamais afficher la base principale
                if (!string.Equals(entry, EditedSquad.Base, StringComparison.OrdinalIgnoreCase))
                {
                    listBoxBasesAlternat.Items.Add(entry);
                }
            }
        }
        // Ajoute une base alternative depuis la comboBox_All_bases.
        // Pourquoi : permettre d'ajouter une base de repli sans doublon ni incohérence.
        private void button_base_plus_Click(object sender, EventArgs e)
        {
            string selectedBase = comboBox_All_bases.SelectedItem as string;

            if (string.IsNullOrEmpty(selectedBase))
                return;

            // Interdit : identique à la base principale
            if (string.Equals(selectedBase, EditedSquad.Base, StringComparison.OrdinalIgnoreCase))
                return;

            // Interdit : doublon
            foreach (var item in listBoxBasesAlternat.Items)
            {
                if (string.Equals(item.ToString(), selectedBase, StringComparison.OrdinalIgnoreCase))
                    return;
            }

            listBoxBasesAlternat.Items.Add(selectedBase);
        }
        // Supprime la base alternative sélectionnée.
        // Pourquoi : permettre de retirer une base de repli.
        private void buttonBaseMoins_Click(object sender, EventArgs e)
        {
            int index = listBoxBasesAlternat.SelectedIndex;

            if (index >= 0)
            {
                listBoxBasesAlternat.Items.RemoveAt(index);
            }
        }
        // Remonte la base sélectionnée d'un cran.
        // Pourquoi : modifier la priorité des bases de repli.
        private void button_Base_Haut_Click(object sender, EventArgs e)
        {
            int index = listBoxBasesAlternat.SelectedIndex;

            if (index > 0)
            {
                object item = listBoxBasesAlternat.Items[index];

                listBoxBasesAlternat.Items.RemoveAt(index);
                listBoxBasesAlternat.Items.Insert(index - 1, item);

                listBoxBasesAlternat.SelectedIndex = index - 1;
            }
        }
        // Descend la base sélectionnée d'un cran.
        // Pourquoi : modifier la priorité des bases de repli.
        private void button_Base_Down_Click(object sender, EventArgs e)
        {
            int index = listBoxBasesAlternat.SelectedIndex;

            if (index >= 0 && index < listBoxBasesAlternat.Items.Count - 1)
            {
                object item = listBoxBasesAlternat.Items[index];

                listBoxBasesAlternat.Items.RemoveAt(index);
                listBoxBasesAlternat.Items.Insert(index + 1, item);

                listBoxBasesAlternat.SelectedIndex = index + 1;
            }
        }
        // Quand la base principale change.
        // Pourquoi : éviter qu'une base alternative soit identique à la base actuelle.
        private void comboBoxBase_SelectedIndexChanged(object sender, EventArgs e)
        {
            string newBase = comboBoxBase.SelectedItem as string;

            if (string.IsNullOrEmpty(newBase))
                return;

            // Supprime si présente dans les alternatives
            for (int i = listBoxBasesAlternat.Items.Count - 1; i >= 0; i--)
            {
                if (string.Equals(listBoxBasesAlternat.Items[i].ToString(), newBase, StringComparison.OrdinalIgnoreCase))
                {
                    listBoxBasesAlternat.Items.RemoveAt(i);
                }
            }

            EditedSquad.Base = newBase;

            SquadUpdated?.Invoke();


        }

        //***********************
        //***********************BASE ALT FIN



        //***********************
        //***********************ParkingId START

        private void BuildParkingId()
        {
            listBox_ParkingId.Items.Clear();

            if (EditedSquad.parking_id == null)
                return;

            foreach (var kvp in EditedSquad.parking_id)
            {
                string key = kvp.Key;
                List<int> values = ConvertToIntList(kvp.Value);

                listBox_ParkingId.Items.Add(FormatParkingDisplay(key, values));
            }
        }
        private List<int> ConvertToIntList(object value)
        {
            var list = new List<int>();

            if (value is IEnumerable<object> objList)
            {
                foreach (var v in objList)
                    list.Add(Convert.ToInt32(v));
            }
            else if (value is IEnumerable enumerable)
            {
                foreach (var v in enumerable)
                    list.Add(Convert.ToInt32(v));
            }

            return list;
        }
        private string FormatParkingDisplay(string key, List<int> values)
        {
            string prefix = string.IsNullOrEmpty(key) ? "_" : key;

            if (values.Count == 2)
            {
                int a = values[0];
                int b = values[1];
                return $"{prefix} : {a} to {b}";
            }

            return $"{prefix} : " + string.Join(",", values);
        }      

        private void button_ParkingId_Add_Click(object sender, EventArgs e)
        {
            string prefix = textBox_ParkingId_Prefix.Text.Trim();
            string intText = textBox_ParkingId_Int.Text.Trim();

            if (string.IsNullOrEmpty(intText))
                return;

            // Parse les valeurs séparées par virgule
            List<int> values = intText
                .Split(new[] { '.',',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(v => Convert.ToInt32(v))
                .ToList();

            if (EditedSquad.parking_id == null)
                EditedSquad.parking_id = new Dictionary<string, object>();

            // Si la clé existe déjà → on fusionne
            if (EditedSquad.parking_id.ContainsKey(prefix))
            {
                var existing = ConvertToIntList(EditedSquad.parking_id[prefix]);
                existing.AddRange(values);
                existing = existing.Distinct().OrderBy(v => v).ToList();
                EditedSquad.parking_id[prefix] = existing;
            }
            else
            {
                EditedSquad.parking_id[prefix] = values;
            }

            BuildParkingId();

            textBox_ParkingId_Prefix.Clear();
            textBox_ParkingId_Int.Clear();
        }

        // Supprime la ParkingId sélectionnée.
        // Pourquoi : permettre de retirer un ParkingId
        private void button_ParkingId_Remove_Click(object sender, EventArgs e)
        {
            int index = listBox_ParkingId.SelectedIndex;
            if (index < 0)
                return;

            string line = listBox_ParkingId.Items[index].ToString();

            string key = line.Split(':')[0].Trim();
            if (key == "_") key = "";

            if (EditedSquad.parking_id.ContainsKey(key))
                EditedSquad.parking_id.Remove(key);

            BuildParkingId();
        }

        //***********************
        //***********************ParkingId END


        private void BuildCallSign()
        {
            comboBox_Callsign.Items.Clear();

            // Toujours ajouter "Automatic" en premier
            comboBox_Callsign.Items.Add("Automatic");

            string selectedCallsign = EditedSquad.Callsign; // string ou null ?

            if (EditedSquad.SideString == "blue")
            {
                var aircraft = EditedSquad.Type;

                if (_campaignContext?.LuaData?.SpecificCallnames != null)
                {
                    bool callsignAdded = false;

                    if (_campaignContext?.LuaData?.SpecificCallnames != null)
                    {
                        if (_campaignContext.LuaData.SpecificCallnames.ContainsKey(aircraft))
                        {
                            var dict = _campaignContext.LuaData.SpecificCallnames[aircraft];

                            if (dict.ContainsKey(EditedSquad.Country))
                            {
                                comboBox_Callsign.Items.AddRange(
                                    dict[EditedSquad.Country]
                                    .OrderBy(k => int.Parse(k.Key))
                                    .Select(k => k.Value)
                                    .ToArray()
                                );

                                callsignAdded = true;
                            }
                        }

                        // -------------------------------------------------
                        // fallback generic
                        // -------------------------------------------------

                        if (!callsignAdded)
                        {
                            string typeFCT = "generic";

                            if (EditedSquad.Tasks != null)
                            {
                                foreach (var task in EditedSquad.Tasks)
                                {
                                    if (task.Key == "AWACS" && task.Value)
                                    {
                                        typeFCT = "AWACS";
                                        break;
                                    }

                                    if (task.Key == "Refueling" && task.Value)
                                    {
                                        typeFCT = "tanker";
                                        break;
                                    }
                                }
                            }

                            if (_campaignContext.LuaData.CallsignWest.ContainsKey(typeFCT))
                            {
                                comboBox_Callsign.Items.AddRange(
                                    _campaignContext.LuaData.CallsignWest[typeFCT].ToArray()
                                );
                            }
                        }
                    }
                    else
                    {
                        comboBox_Callsign.Enabled = false;
                        return;
                    }
                   
                }
            }
            else
            {
                comboBox_Callsign.Enabled = false;
                return;
            }

            // Ajouter le callsign déjà sélectionné s'il n'est pas dans la liste
            if (!string.IsNullOrEmpty(selectedCallsign) &&
                !comboBox_Callsign.Items.Contains(selectedCallsign))
            {
                comboBox_Callsign.Items.Add(selectedCallsign);
            }

            // Sélectionner le callsign actuel ou "Automatic"
            if (!string.IsNullOrEmpty(selectedCallsign) &&
                comboBox_Callsign.Items.Contains(selectedCallsign))
            {
                comboBox_Callsign.SelectedItem = selectedCallsign;
            }
            else
            {
                comboBox_Callsign.SelectedItem = "Automatic";
            }
        }

        private void comboBox_Callsign_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (_isRefreshingCallsign)
                return;

            var selected = comboBox_Callsign.SelectedItem as string;

            if (selected == "Automatic")
            {
                EditedSquad.Callsign = null;
            }
            else
            {
                EditedSquad.Callsign = selected;
            }
        }


        // Construit la liste des liveries.
        // Pourquoi : affichage simple + cohérent avec les autres listes.
        private void BuildLiveryArea()
        {
            listBox_Livery.Items.Clear();
            comboBox_LiveryM.Items.Clear();

            var dict = NormalizeLivery();

            foreach (var entry in dict.OrderBy(x => x.Key))
            {
                listBox_Livery.Items.Add(entry.Value);
                comboBox_LiveryM.Items.Add(entry.Value);
            }
        }

        // Convertit Livery en Dictionary<int,string> sûr.
        // Pourquoi : garantir un format unique partout.
        private Dictionary<int, string> NormalizeLivery()
        {
            if (EditedSquad.Livery == null)
            {
                EditedSquad.Livery = new Dictionary<int, string>();
            }

            return EditedSquad.Livery;
        }


        // Supprime la skin sélectionnée.
        // Pourquoi : permettre la gestion de la liste.
        private void button_RemoveSkin_Click(object sender, EventArgs e)
        {
            int index = listBox_Livery.SelectedIndex;

            if (index < 0)
                return;

            var dict = NormalizeLivery();

            // On récupère la clé réelle (important)
            var orderedKeys = dict.OrderBy(k => k.Key).Select(k => k.Key).ToList();

            if (index >= orderedKeys.Count)
                return;

            int keyToRemove = orderedKeys[index];

            // SUPPRESSION DANS LA CLASS
            dict.Remove(keyToRemove);

            // Reindex propre (évite trous 1,3,5)
            EditedSquad.Livery = dict
                .OrderBy(k => k.Key)
                .Select((kvp, i) => new { Key = i + 1, kvp.Value })
                .ToDictionary(x => x.Key, x => x.Value);

            // Refresh UI
            BuildLiveryArea();
        }

        // Ajoute une nouvelle skin.
        // Pourquoi : éviter doublons + garder cohérence UI.
        private void button_AddSkin_Click(object sender, EventArgs e)
        {
            string newSkin = textBox_AddSkin.Text.Trim();

            if (string.IsNullOrEmpty(newSkin))
                return;

            var dict = NormalizeLivery();

            // Vérifie doublon
            if (dict.Values.Any(v => v.Equals(newSkin, StringComparison.OrdinalIgnoreCase)))
                return;

            int newIndex = dict.Count > 0 ? dict.Keys.Max() + 1 : 1;

            // AJOUT DANS LA CLASS Squad (IMPORTANT)
            dict[newIndex] = newSkin;

            // Refresh complet UI
            BuildLiveryArea();

            textBox_AddSkin.Clear();
            textBox_AddSkin.Focus();
        }

        // Construit la liste des liveries.
        // Pourquoi : affichage simple + cohérent avec les autres listes.
        private void BuildLiveryModex()
        {
            listBox_LiveryModex.Items.Clear();

            var dict = NormalizeModex();

            foreach (var entry in dict.OrderBy(x => x.Key))
            {
                listBox_LiveryModex.Items.Add(new ModexItem
                {
                    Key = entry.Key,
                    Value = entry.Value
                });
            }
        }

        // Convertit Livery en Dictionary<int,string> sûr.
        // Pourquoi : garantir un format unique partout.
        private Dictionary<int, string> NormalizeModex()
        {
            if (EditedSquad.LiveryModex == null)
            {
                EditedSquad.LiveryModex = new Dictionary<int, string>();
            }
            return EditedSquad.LiveryModex;
        }

        // Supprime la skin sélectionnée.
        // Pourquoi : permettre la gestion de la liste.
        private void button_LiveryModex_Moins_Click(object sender, EventArgs e)
        {
            int index = listBox_LiveryModex.SelectedIndex;
            if (index < 0)
                return;

            if (listBox_LiveryModex.Items[index] is ModexItem item)
            {
                // Supprime dans la classe Squad
                EditedSquad.LiveryModex.Remove(item.Key);
            }

            // Supprime dans la ListBox
            listBox_LiveryModex.Items.RemoveAt(index);
        }

        // Ajoute une nouvelle skin.
        // Pourquoi : éviter doublons + garder cohérence UI.
        private void button_LiveryModex_Plus_Click(object sender, EventArgs e)
        {
            if (!int.TryParse(textBox_Modex.Text.Trim(), out int modex))
                return;

            string skin = comboBox_LiveryM.SelectedItem?.ToString();
            if (string.IsNullOrEmpty(skin))
                return;

            // Vérifie doublon dans le dictionnaire
            if (EditedSquad.LiveryModex.ContainsKey(modex))
                return;

            // Ajoute dans la classe Squad
            EditedSquad.LiveryModex[modex] = skin;

            // Ajoute dans la ListBox
            listBox_LiveryModex.Items.Add(new ModexItem
            {
                Key = modex,
                Value = skin
            });

            textBox_Modex.Clear();
            textBox_Modex.Focus();
        }

        // Cette fonction construit dynamiquement la zone des Taks.
        // Pourquoi : chaque squad peut avoir des Tasks différentes.
        private void BuildTasksArea()
        {
            flowLayoutPanelTasks.Controls.Clear();
            _taskRows.Clear();

            if (EditedSquad.Tasks == null)
                return;

            foreach (var task in EditedSquad.Tasks.OrderBy(t => t.Key))
            {
                System.Windows.Forms.Panel  row = new System.Windows.Forms.Panel ();
                row.Width = 400;
                row.Height = 32;

                System.Windows.Forms.Label label = new System.Windows.Forms.Label ();
                label.Text = task.Key;
                label.Location = new Point(0, 7);
                label.Width = 75;

                System.Windows.Forms.CheckBox checkBox = new System.Windows.Forms.CheckBox();
                checkBox.Checked = Convert.ToBoolean(task.Value);
                checkBox.Location = new Point(75, 5);

                // MAJ live du squad quand on coche/décoche
                checkBox.CheckedChanged += (s, e) =>
                {
                    EditedSquad.Tasks[task.Key] = checkBox.Checked;
                    SquadUpdated?.Invoke();
                };

                System.Windows.Forms.Label labelCoef = new System.Windows.Forms.Label ();
                labelCoef.Text = "Coef";
                labelCoef.Location = new Point(100, 7);
                labelCoef.Width = 50;

                NumericUpDown numericCoef = new NumericUpDown();
                numericCoef.DecimalPlaces = 2;
                numericCoef.Maximum = 999;
                numericCoef.Minimum = -999;
                numericCoef.Width = 80;
                numericCoef.Location = new Point(300, 3);

                // MAJ live du coef
                numericCoef.ValueChanged += (s, e) =>
                {
                    EditedSquad.TasksCoef[task.Key] = (double)numericCoef.Value;
                    SquadUpdated?.Invoke();
                };

                if (EditedSquad.TasksCoef != null && EditedSquad.TasksCoef.ContainsKey(task.Key))
                {
                    numericCoef.Value = Convert.ToDecimal(EditedSquad.TasksCoef[task.Key]);
                }

                row.Controls.Add(label);
                row.Controls.Add(checkBox);
                row.Controls.Add(labelCoef);
                row.Controls.Add(numericCoef);

                flowLayoutPanelTasks.Controls.Add(row);

                _taskRows.Add(new TaskRow
                {
                    TaskName = task.Key,
                    EnabledCheckBox = checkBox,
                    CoefNumeric = numericCoef
                });
            }
        }

        private void BuildScoreArea()
        {
            flowLayoutPanelScore.Controls.Clear();

            //FormUtils.LogRegister("BuildScoreArea() squad.Name " + EditedSquad.Name  );

            if (EditedSquad.Roster != null && EditedSquad.Roster.Count > 0)
            {
                var rawRoster = EditedSquad.Roster as System.Collections.IDictionary;

                if (rawRoster != null)
                {
                    EditedSquad.Roster = new Dictionary<string, object>();

                    // sécurité si null
                    if (EditedSquad.TasksCoef == null)
                        EditedSquad.TasksCoef = new Dictionary<string, double>();

                    foreach (System.Collections.DictionaryEntry entry in rawRoster)
                    {
                        object value = entry.Value;

                        if (value is LuaObject luaObj)
                            value = luaObj.luaobj;

                        EditedSquad.Roster[entry.Key.ToString()] = value;
                    }
                }
            }

            if (EditedSquad.Score != null && EditedSquad.Score.Count > 0)
            {
                var rawScore = EditedSquad.Score as System.Collections.IDictionary;

                if (rawScore != null)
                {
                    EditedSquad.Score = new Dictionary<string, object>();

                    foreach (System.Collections.DictionaryEntry entry in rawScore)
                    {
                        object value = entry.Value;

                        if (value is LuaObject luaObj)
                            value = luaObj.luaobj;

                        EditedSquad.Score[entry.Key.ToString()] = value;
                    }
                }
            }


            if (_isActiveVersion)
            {
                AddDictionarySection("Roster", EditedSquad.Roster);
                AddDictionarySection("Score", EditedSquad.Score);
            }

        }



        // Cette fonction ajoute une sous-section issue d'un dictionnaire.
        // Pourquoi : mutualiser l'affichage des roster, score et autres futurs blocs.
        private void AddDictionarySection(string title, Dictionary<string, object> dict)
        {
            if (dict == null)
                return;


            GroupBox group = new GroupBox();
            group.Text = title;
            group.Width = 430;
            //group.Height = 120;
            group.Height = Math.Max(120, 35 + (dict.Count * 28));
            group.Margin = new Padding(10);

            int y = 20;

            foreach (var item in dict)
            {
                System.Windows.Forms.Label label = new System.Windows.Forms.Label ();
                label.Text = item.Key;
                label.Location = new Point(10, y + 3);
                label.Width = 180;

                NumericUpDown numeric = new NumericUpDown();
                numeric.Maximum = 999999;
                numeric.Minimum = -999999;
                numeric.Location = new Point(200, y);
                numeric.Width = 100;

                int value;
                int.TryParse(item.Value?.ToString(), out value);
                numeric.Value = value;

                // Sauvegarde live dans le dictionnaire
                // Pourquoi : permettre au CampaignSaver d'écrire les nouvelles valeurs
                numeric.ValueChanged += (s, e) =>
                {
                    dict[item.Key] = (int)numeric.Value;

                    SquadUpdated?.Invoke();
                };

                group.Controls.Add(label);
                group.Controls.Add(numeric);

                y += 28;
            }

            flowLayoutPanelScore.Controls.Add(group);
        }


        // Cette fonction ferme la fenêtre sans rien modifier.
        // Pourquoi : laisser l'utilisateur annuler ses changements.
        private void buttonCancel_Click(object sender, EventArgs e)
        {
            DialogResult = DialogResult.Cancel;
            Close();
        }

        // Cette classe mémorise une ligne de tâche dynamique.
        private class TaskRow
        {
            public string TaskName { get; set; }
            public System.Windows.Forms.CheckBox EnabledCheckBox { get; set; }
            public NumericUpDown CoefNumeric { get; set; }
        }

        // Cette classe mémorise une ligne de propriété additionnelle.
        private class AdditionalRow
        {
            public string PropertyName { get; set; }
            public System.Windows.Forms.TextBox ValueTextBox { get; set; }
        }

        // Représente une entrée LiveryModex (clé + valeur)
        // Pourquoi : conserver le modex tout en affichant proprement dans la UI
        private class ModexItem
        {
            public int Key { get; set; }
            public string Value { get; set; }

            public override string ToString()
            {
                return Key + " - " + Value;
            }
        }

        private void comboBox1_SelectedIndexChanged(object sender, EventArgs e)
        {

        }

        private void labelSkill_Click(object sender, EventArgs e)
        {

        }

        private void labelBasesAdd_Click(object sender, EventArgs e)
        {

        }
        // Construit un éditeur générique pour table Lua
        // Pourquoi : éviter de coder 10 UI différents
        private void AddGenericTable(string title, object data)
        {
            if (data == null)
                return;

            GroupBox group = new GroupBox();
            //group.Text = title;
            group.Text = $"{title} ({data.GetType().Name})";
            group.Width = 520;
            group.Height = 220;

            System.Windows.Forms.ListBox list = new System.Windows.Forms.ListBox();
            list.Width = 480;
            list.Height = 120;
            list.Location = new Point(10, 20);

            System.Windows.Forms.TextBox textKey = new System.Windows.Forms.TextBox();
            textKey.Width = 100;
            textKey.Location = new Point(10, 150);

            System.Windows.Forms.TextBox textValue = new System.Windows.Forms.TextBox();
            textValue.Width = 200;
            textValue.Location = new Point(120, 150);

            System.Windows.Forms.Button btnAdd = new System.Windows.Forms.Button();
            btnAdd.Text = "+";
            btnAdd.Size = new Size(30, 25);
            btnAdd.Location = new Point(330, 148);

            System.Windows.Forms.Button btnRemove = new System.Windows.Forms.Button();
            btnRemove.Text = "-";
            btnRemove.Size = new Size(30, 25);
            btnRemove.Location = new Point(370, 148);

            // Type détecté
            bool hasKey = true;

            // Détection universelle
            // Pourquoi : supporter toutes les structures Lua sans ajouter de code
            if (data is System.Collections.IDictionary dict)
            {
                foreach (System.Collections.DictionaryEntry entry in dict)
                {
                    string key = entry.Key.ToString();

                    if (ReferenceEquals(entry.Value, data))
                    {
                        list.Items.Add($"{key} = (self reference)");
                        continue;
                    }

                    
                    if (entry.Value is System.Collections.IEnumerable enumerable && !(entry.Value is string))
                    {
                        List<string> values = new List<string>();

                        foreach (var v in enumerable)
                        {
                            values.Add(v != null ? v.ToString() : "null");
                        }

                        // affichage simple demandé
                        list.Items.Add($"{key} = {string.Join(",", values)}");
                    }
                    else
                    {
                        list.Items.Add($"{key} = {entry.Value}");
                    }
                }
            }
            else if (data is System.Collections.IEnumerable enumerable && !(data is string))
            {
                foreach (var v in enumerable)
                {
                    list.Items.Add(v.ToString());
                }

                hasKey = false;
            }

           

            // Si pas de clé → on désactive le champ key
            //textKey.Enabled = hasKey;

            // ---------------------------
            // ➕ ADD
            // ---------------------------
            btnAdd.Click += (s, e) =>
            {
                string key = textKey.Text.Trim();
                string val = textValue.Text.Trim();

                if (string.IsNullOrEmpty(val))
                    return;

                if (hasKey)
                    list.Items.Add($"{key} = {val}");
                else
                    list.Items.Add(val);

                textKey.Clear();
                textValue.Clear();
            };

            // ---------------------------
            // ➖ REMOVE
            // ---------------------------
            btnRemove.Click += (s, e) =>
            {
                if (list.SelectedIndex >= 0)
                    list.Items.RemoveAt(list.SelectedIndex);
            };

            group.Controls.Add(list);
            group.Controls.Add(textKey);
            group.Controls.Add(textValue);
            group.Controls.Add(btnAdd);
            group.Controls.Add(btnRemove);

            //flowLayoutPanelAdditional.Controls.Add(group);
            flowLayoutPanelTables.Controls.Add(group);
        }

        // Construit automatiquement les tables typées du Squad
        // Pourquoi : éviter de déclarer chaque table à la main et rendre l'UI scalable
        private void BuildGenericTables()
        {
            flowLayoutPanelTables.Controls.Clear();

            var properties = typeof(Squad).GetProperties();

            foreach (var prop in properties)
            {
                if ( prop.Name == "AdditionalProperties")
                { 
                }
                

                // EXCLUSIONS (UI spécifique déjà existante)
                if (
                    prop.Name == "Livery"
                    || prop.Name == "LiveryModex"
                    || prop.Name == "Tasks"
                    || prop.Name == "TasksCoef" 
                    || prop.Name == "Roster" 
                    || prop.Name == "Score"
                    || prop.Name == "BaseAlternative"
                    || prop.Name == "ScoreLast"
                    || prop.Name == "TasksCoefPourcent"
                    || prop.Name == "parking_id"
                    || prop.Name == "SideNumber"
                    || prop.Name == "score_last"
                    || prop.Name == "CallsignId"
                    || prop.Name == "InitNumber"
                    || prop.Name == "InitReserve"
                    // ||prop.Name == "AdditionalProperties"
                    )

                {
                    continue;
                }

                object value = prop.GetValue(EditedSquad);

                //    continue;
                if (value == null)
                {
                    // afficher quand même une table vide
                    if (prop.PropertyType != typeof(string))
                        AddGenericTable(prop.Name, new List<string>());
                    continue;
                }

                // Détection automatique table
                bool isDictionary = value is System.Collections.IDictionary;
                bool isList = value is System.Collections.IEnumerable && !(value is string);

                if (isDictionary || isList)
                {
                    //FormUtils.LogRegister($"TABLE ADDED: {prop.Name}");
                    AddGenericTable(prop.Name, value);
                }
            }
        }

        private void label_ModexNb_Min_Click(object sender, EventArgs e)
        {

        }

        private void groupBoxAdditional_Enter(object sender, EventArgs e)
        {

        }

        private void listBox1_SelectedIndexChanged(object sender, EventArgs e)
        {

        }

        private void textBox2_TextChanged(object sender, EventArgs e)
        {

        }

        private void comboBoxType_SelectedIndexChanged(object sender, EventArgs e)
        {
            EditedSquad.Type = comboBoxType.Text;
            BuildTasksArea();
            SquadUpdated?.Invoke();
        }

        private void comboBoxSkill_SelectedIndexChanged(object sender, EventArgs e)
        {
            EditedSquad.Skill = comboBoxSkill.Text;
        }

        private void checkBoxPlayer_CheckedChanged(object sender, EventArgs e)
        {
            //  Empêche de décocher si c'est le dernier Player
            if (!checkBoxPlayer.Checked)
            {
               

                bool anotherPlayerExists = _campaignContext.CampaignEditRef
                    .CurrentCampaignSquads
                    .Any(cs =>
                        (cs.Active != EditedSquad && cs.Active?.Player == true)
                        ||
                        (cs.Init != EditedSquad && cs.Init?.Player == true)
                    );


                if (!anotherPlayerExists)
                {
                    MessageBox.Show(
                        "You must select another Player squad first.",
                        "Player required",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information
                    );

                    checkBoxPlayer.Checked = true;
                    return;
                }
            }

            EditedSquad.Player = checkBoxPlayer.Checked;

            // Applique règle globale (un seul Player)
            if (EditedSquad.Player)
            {
                EditedSquad.Player = checkBoxPlayer.Checked;

                if (EditedSquad.Player)
                {
                    _campaignContext.CampaignEditRef.SetSinglePlayerSquad(EditedSquad);
                }
            }
        }

        private void checkBox_HumainOnly_CheckedChanged(object sender, EventArgs e)
        {
            if (checkBox_HumainOnly.Checked)
            {
                EditedSquad.HumainOnly = true;
            }
            else
            {
                EditedSquad.HumainOnly = false;
            }
        }


        private void checkBoxActive_CheckedChanged(object sender, EventArgs e)
        {
            EditedSquad.Squad_Active = checkBoxActive.Checked;

            SquadUpdated?.Invoke();
        }

        private void numericNumber_ValueChanged(object sender, EventArgs e)
        {
            EditedSquad.Number = (int)numericNumber.Value;
            EditedSquad.InitNumber = (int)numericNumber.Value;

            if (checkBoxActive.Checked)
            {
                if (EditedSquad.Roster == null)
                {
                    EditedSquad.Roster = new Dictionary<string, object>();
                }

                EditedSquad.Roster["ready"] = (int)numericNumber.Value;

                // Refresh UI live du roster
                // Pourquoi : afficher immédiatement la nouvelle valeur
                BuildScoreArea();
            }
        }


        private void numericReserve_ValueChanged(object sender, EventArgs e)
        {
            EditedSquad.Reserve = (int)numericReserve.Value;
            EditedSquad.InitReserve = (int)numericReserve.Value;

            if (checkBoxActive.Checked)
            {
                if (EditedSquad.Roster == null)
                {
                    EditedSquad.Roster = new Dictionary<string, object>();
                }

                EditedSquad.Roster["reserve"] = (int)numericReserve.Value;

                // Refresh UI live du roster
                // Pourquoi : afficher immédiatement la nouvelle valeur
                BuildScoreArea();
            }
        }


        private void textBox_ModexNb_Min_TextChanged(object sender, EventArgs e)
        {
            if (int.TryParse(textBox_ModexNb_Min.Text, out int value))
            {
                EditedSquad.SideNumberMin = value;
            }
            else
            {
                EditedSquad.SideNumberMin = 0;
            }
        }

        private void textBox_ModexNb_Max_TextChanged(object sender, EventArgs e)
        {
            if (int.TryParse(textBox_ModexNb_Max.Text, out int value))
            {
                EditedSquad.SideNumberMax = value;
            }
            else
            {
                EditedSquad.SideNumberMax = 0;
            }
        }

        // Recharge les callsigns selon avion/pays/tasks
        // Pourquoi : mettre à jour automatiquement la liste quand le pays change
        private void RefreshCallsignList()
        {
            _isRefreshingCallsign = true;

            comboBox_Callsign.Items.Clear();

            // Toujours proposer Automatic
            // Pourquoi : permettre retour au mode auto DCE
            comboBox_Callsign.Items.Add("Automatic");

            string selectedCallsign = EditedSquad.Callsign;

            if (_campaignContext?.LuaData == null)
            {
                comboBox_Callsign.Enabled = false;
                return;
            }

            string aircraft = EditedSquad.Type;

            bool callsignAdded = false;

            // -------------------------------------------------
            // SpecificCallnames
            // -------------------------------------------------

            if (_campaignContext.LuaData.SpecificCallnames != null)
            {
                if (_campaignContext.LuaData.SpecificCallnames.ContainsKey(aircraft))
                {
                    var dict = _campaignContext.LuaData.SpecificCallnames[aircraft];

                    if (dict.ContainsKey(EditedSquad.Country))
                    {
                        comboBox_Callsign.Items.AddRange(
                             dict[EditedSquad.Country]
                             .Select(x => x.Value)
                             .ToArray()
                         );

                        callsignAdded = true;
                    }
                }
            }

            // -------------------------------------------------
            // Generic fallback
            // -------------------------------------------------

            if (!callsignAdded)
            {
                string typeFCT = "generic";

                if (EditedSquad.Tasks != null)
                {
                    foreach (var task in EditedSquad.Tasks)
                    {
                        if (task.Key == "AWACS" && task.Value)
                        {
                            typeFCT = "AWACS";
                            break;
                        }

                        if (task.Key == "Refueling" && task.Value)
                        {
                            typeFCT = "tanker";
                            break;
                        }
                    }
                }

                if (_campaignContext.LuaData.CallsignWest.ContainsKey(typeFCT))
                {
                    comboBox_Callsign.Items.AddRange(
                        _campaignContext.LuaData.CallsignWest[typeFCT].ToArray()
                    );
                }
            }

            //comboBox_Callsign.Enabled = comboBox_Callsign.Items.Count > 0;

            //comboBox_Callsign.Enabled = comboBox_Callsign.Items.Count > 0;

            // Toujours ajouter "Automatic" en premier
            comboBox_Callsign.Items.Insert(0, "Automatic");

            // Vérifie si le callsign actuel existe encore
            string currentCallsign = EditedSquad.Callsign;

            if (!string.IsNullOrEmpty(currentCallsign)
                && comboBox_Callsign.Items.Contains(currentCallsign))
            {
                // On garde le callsign actuel
                comboBox_Callsign.SelectedItem = currentCallsign;
            }
            else
            {
                // Callsign invalide pour ce pays → retour automatique
                EditedSquad.Callsign = null;
                comboBox_Callsign.SelectedItem = "Automatic";
            }

            comboBox_Callsign.Enabled = comboBox_Callsign.Items.Count > 0;

            //// Ajouter le callsign actuel s'il n'existe pas
            //// Pourquoi : éviter de perdre une valeur exotique/non prévue
            //if (!string.IsNullOrEmpty(selectedCallsign) &&
            //    !comboBox_Callsign.Items.Contains(selectedCallsign))
            //{
            //    comboBox_Callsign.Items.Add(selectedCallsign);
            //}

            //// Sélection automatique
            //// Pourquoi : garder la cohérence UI après refresh
            //if (!string.IsNullOrEmpty(selectedCallsign) &&
            //    comboBox_Callsign.Items.Contains(selectedCallsign))
            //{
            //    comboBox_Callsign.SelectedItem = selectedCallsign;
            //}
            //else
            //{
            //    comboBox_Callsign.SelectedItem = "Automatic";
            //}

            _isRefreshingCallsign = false;
        }

        // Recharge les callsigns quand le pays change
        // Pourquoi : afficher immédiatement les bons callsigns
        private void comboBoxCountry_SelectedIndexChanged(object sender, EventArgs e)
        {
            //EditedSquad.Country = comboBoxCountry.Text;

            if (comboBoxCountry.SelectedItem != null)
            {
                EditedSquad.Country = comboBoxCountry.SelectedItem.ToString();

                RefreshCallsignList();
            }
        }

    }
}
