// Fichier à remplacer : FormSquadEdit.cs
// But : remplir automatiquement la grande fenêtre avec toutes les données du squad.
// Pourquoi : permettre d'afficher et modifier immédiatement toutes les variables connues et futures.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using DCE_Manager.Parameters;
using DCE_Manager.Utils;

namespace DCE_Manager
{
    public partial class FormSquadEdit : Form
    {
        public Squad EditedSquad { get; private set; }

        // Déclaration membre de classe
        // Pourquoi : garder une référence sur la ComboBox pour pouvoir la mettre à jour.
        private ComboBox _comboBoxLivery;

        // Cette liste mémorise les contrôles dynamiques des tâches.
        // Pourquoi : récupérer facilement les valeurs lors du Save.
        private readonly List<TaskRow> _taskRows = new List<TaskRow>();

        // Cette liste mémorise les contrôles dynamiques des variables additionnelles.
        // Pourquoi : permettre plus tard d'éditer également les nouvelles variables Lua.
        private readonly List<AdditionalRow> _additionalRows = new List<AdditionalRow>();

        public FormSquadEdit(Squad squad, bool cloneMode = false)
        {
            InitializeComponent();

            // On clone le squad pour éviter de modifier l'original avant Save.
            // Pourquoi : l'utilisateur peut encore annuler.
            EditedSquad = cloneMode ? CloneSquad(squad) : squad;

            Text = cloneMode
                ? "Clone Squad - " + EditedSquad.Name
                : "Edit Squad - " + EditedSquad.Name;

            LoadStaticLists();
            FillControls();
            BuildBasesAlternative();
            BuildLiveryArea();
            BuildTasksArea();
            BuildScoreArea();
            BuildAdditionalArea();
        }

        // Cette fonction crée une copie manuelle du squad.
        // Pourquoi : éviter que le mode clone modifie le squad original.
        private Squad CloneSquad(Squad source)
        {
            return new Squad
            {
                Name = source.Name + "_Copy",
                SideString = source.SideString,
                FolderFile = source.FolderFile,
                IdSquad = source.IdSquad,
                Inactive = source.Inactive,
                Player = source.Player,
                Type = source.Type,
                Country = source.Country,
                Base = source.Base,
                Skill = source.Skill,
                Number = source.Number,
                InitNumber = source.InitNumber,
                Reserve = source.Reserve,
                InitReserve = source.InitReserve,
                BaseAlternative = source.BaseAlternative != null ? new List<string>(source.BaseAlternative) : new List<string>(),
                Tasks = source.Tasks != null ? new Dictionary<string, bool>(source.Tasks) : new Dictionary<string, bool>(),
                TasksCoef = source.TasksCoef != null ? new Dictionary<string, double>(source.TasksCoef) : new Dictionary<string, double>(),
                Roster = source.Roster != null ? new Dictionary<string, object>(source.Roster) : new Dictionary<string, object>(),
                Score = source.Score != null ? new Dictionary<string, object>(source.Score) : new Dictionary<string, object>(),
                Livery = source.Livery,
                AdditionalProperties = source.AdditionalProperties != null
                    ? new Dictionary<string, object>(source.AdditionalProperties)
                    : new Dictionary<string, object>()
            };
        }

        // Cette fonction remplit les listes connues des ComboBox.
        // Pourquoi : proposer directement les choix possibles à l'utilisateur.
        private void LoadStaticLists()
        {
            comboBoxCountry.Items.AddRange(new object[]
            {
                "USA", "Russia", "France", "UK", "Germany", "Israel", "Turkey", "Georgia"
            });

            // A remplacer plus tard par ta vraie liste d'avions.
            comboBoxType.Items.AddRange(new object[]
            {
                EditedSquad.Type,
                "F-4E-45MC",
                "MiG-23MLD",
                "F-14B",
                "F-16C_50",
                "FA-18C_hornet",
                "Mirage-F1CE"
            });

            // A remplacer plus tard par ta vraie liste de bases.
            comboBoxBase.Items.Add(EditedSquad.Base);

            if (EditedSquad.BaseAlternative != null)
            {
                foreach (string baseAlt in EditedSquad.BaseAlternative)
                {
                    if (!comboBoxBase.Items.Contains(baseAlt))
                        comboBoxBase.Items.Add(baseAlt);
                }
            }
        }

        // Cette fonction charge les propriétés simples du squad dans les contrôles.
        // Pourquoi : préremplir immédiatement la fenêtre d'édition.
        private void FillControls()
        {
            textBoxName.Text = EditedSquad.Name;

            comboBoxType.Text = EditedSquad.Type;
            comboBoxCountry.Text = EditedSquad.Country;
            comboBoxBase.Text = EditedSquad.Base;
            comboBoxSkill.Text = EditedSquad.Skill;

            checkBoxPlayer.Checked = EditedSquad.Player;
            checkBoxInactive.Checked = EditedSquad.Inactive;

            numericNumber.Value = EditedSquad.Number;
            numericInitNumber.Value = EditedSquad.InitNumber;
            numericReserve.Value = EditedSquad.Reserve;
            numericInitReserve.Value = EditedSquad.InitReserve;

            
        }

        private void BuildBasesAlternative()
        {
            comboBoxBasesAdd.Items.Clear();

            var baseAlterList = EditedSquad.BaseAlternative;

            if (baseAlterList != null && baseAlterList.Count > 0)
            {
                foreach (var entry in baseAlterList)
                {
                    comboBoxBasesAdd.Items.Add(entry);
                }

                // Afficher immédiatement la première base
                comboBoxBasesAdd.SelectedIndex = 0;
            }
        }

        // Cette fonction remplit la zone Livery.
        // Pourquoi : afficher immédiatement la première skin et permettre d'en ajouter.
        private void BuildLiveryArea()
        {
            comboBox_Livery.Items.Clear();
            comboBox_Livery.DropDownStyle = ComboBoxStyle.DropDownList;

            // Cas 1 : aucune livery connue
            if (EditedSquad.Livery == null)
            {
                comboBox_Livery.Items.Add("(aucune skin)");
                comboBox_Livery.SelectedIndex = 0;

                // On prépare malgré tout un dictionnaire vide pour les futurs ajouts.
                EditedSquad.Livery = new Dictionary<int, string>();
            }

            // Cas 2 : une seule livery sous forme de string
            else if (EditedSquad.Livery is string liveryString)
            {
                comboBox_Livery.Items.Add(liveryString);

                // On affiche la première skin directement sans ouvrir la liste.
                comboBox_Livery.SelectedIndex = 0;
            }

            // Cas 3 : plusieurs liveries dans un dictionnaire
            else if (EditedSquad.Livery is Dictionary<int, string> liveryDict)
            {
                foreach (var entry in liveryDict.OrderBy(x => x.Key))
                {
                    comboBox_Livery.Items.Add(entry.Value);
                }

                // Important : afficher immédiatement la première skin.
                if (comboBox_Livery.Items.Count > 0)
                {
                    comboBox_Livery.SelectedIndex = 0;
                }
            }

            // Cette fonction ajoute une nouvelle skin depuis textBox_AddSkin.
            // Pourquoi : permettre au campaignMaker d'ajouter librement des liveries.
            button_AddSkin.Click -= Button_AddSkin_Click;
            button_AddSkin.Click += Button_AddSkin_Click;
        }

        // Cette fonction ajoute une skin dans la ComboBox et dans EditedSquad.Livery.
        // Pourquoi : conserver les nouvelles skins dans les données du squad.
        private void Button_AddSkin_Click(object sender, EventArgs e)
        {
            string newSkin = textBox_AddSkin.Text.Trim();

            if (string.IsNullOrEmpty(newSkin))
                return;

            // Évite les doublons visuels
            foreach (var item in comboBox_Livery.Items)
            {
                if (string.Equals(item.ToString(), newSkin, StringComparison.OrdinalIgnoreCase))
                {
                    comboBox_Livery.SelectedItem = item;
                    textBox_AddSkin.Clear();
                    return;
                }
            }

            comboBox_Livery.Items.Add(newSkin);
            comboBox_Livery.SelectedItem = newSkin;

            // Cas : aucune livery existante ou simple string
            if (!(EditedSquad.Livery is Dictionary<int, string> dict))
            {
                dict = new Dictionary<int, string>();

                // Si on avait déjà une livery string, on la conserve comme entrée 1
                if (EditedSquad.Livery is string existingString &&
                    !string.IsNullOrWhiteSpace(existingString))
                {
                    dict[1] = existingString;
                }

                EditedSquad.Livery = dict;
            }

            // Cherche le prochain index disponible
            int newIndex = 1;

            while (((Dictionary<int, string>)EditedSquad.Livery).ContainsKey(newIndex))
            {
                newIndex++;
            }

            ((Dictionary<int, string>)EditedSquad.Livery)[newIndex] = newSkin;

            textBox_AddSkin.Clear();
            textBox_AddSkin.Focus();
        }

        

        // Cette fonction construit dynamiquement la zone des tâches.
        // Pourquoi : chaque squad peut avoir des tâches différentes.
        private void BuildTasksArea()
        {
            flowLayoutPanelTasks.Controls.Clear();
            _taskRows.Clear();

            if (EditedSquad.Tasks == null)
                return;

            foreach (var task in EditedSquad.Tasks.OrderBy(t => t.Key))
            {
                Panel row = new Panel();
                row.Width = 1350;
                row.Height = 32;

                Label label = new Label();
                label.Text = task.Key;
                label.Location = new Point(0, 7);
                label.Width = 220;

                CheckBox checkBox = new CheckBox();
                checkBox.Checked = Convert.ToBoolean(task.Value);
                checkBox.Location = new Point(240, 5);

                Label labelCoef = new Label();
                labelCoef.Text = "Coef";
                labelCoef.Location = new Point(320, 7);
                labelCoef.Width = 40;

                NumericUpDown numericCoef = new NumericUpDown();
                numericCoef.DecimalPlaces = 2;
                numericCoef.Maximum = 999;
                numericCoef.Minimum = -999;
                numericCoef.Width = 80;
                numericCoef.Location = new Point(370, 3);

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

            FormUtils.LogRegister("BuildScoreArea() squad.Name " + EditedSquad.Name  );

            if (EditedSquad.Roster == null)
                return;

            //if ((EditedSquad.Roster == null || EditedSquad.Roster.Count == 0) &&
            //    EditedSquad.AdditionalProperties != null &&
            //    EditedSquad.AdditionalProperties.ContainsKey("roster"))
            if (EditedSquad.Roster.Count > 0)
            {
                //var rawRoster = EditedSquad.AdditionalProperties["roster"] as System.Collections.IDictionary;
                var rawRoster = EditedSquad.Roster as System.Collections.IDictionary;

                if (rawRoster != null)
                {
                    EditedSquad.Roster = new Dictionary<string, object>();

                    foreach (System.Collections.DictionaryEntry entry in rawRoster)
                    {
                        object value = entry.Value;

                        if (value is LuaObject luaObj)
                            value = luaObj.luaobj;

                        EditedSquad.Roster[entry.Key.ToString()] = value;
                    }
                }
            }

            //if ((EditedSquad.Score == null || EditedSquad.Score.Count == 0) &&
            //    EditedSquad.AdditionalProperties != null &&
            //    EditedSquad.AdditionalProperties.ContainsKey("score"))
            if (EditedSquad.Score.Count > 0)
            {
                //var rawScore = EditedSquad.AdditionalProperties["score"] as System.Collections.IDictionary;
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


            AddDictionarySection("Roster", EditedSquad.Roster);
            AddDictionarySection("Score", EditedSquad.Score);

            if (EditedSquad.AdditionalProperties != null &&
                EditedSquad.AdditionalProperties.ContainsKey("score_last"))
            {
                var dict = EditedSquad.AdditionalProperties["score_last"] as Dictionary<string, object>;
                AddDictionarySection("Score Last Mission", dict);
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
                Label label = new Label();
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

                group.Controls.Add(label);
                group.Controls.Add(numeric);

                y += 28;
            }

            flowLayoutPanelScore.Controls.Add(group);
        }

        // Cette fonction construit la zone des propriétés inconnues.
        // Pourquoi : afficher automatiquement toute nouvelle variable Lua future.
        private void BuildAdditionalArea()
        {
            flowLayoutPanelAdditional.Controls.Clear();
            _additionalRows.Clear();

            if (EditedSquad.AdditionalProperties == null)
                return;


            //if (EditedSquad.Livery != null)
            //{
            //    Panel row = new Panel();
            //    row.Width = 1400;
            //    row.Height = 120;

            //    Label label = new Label();
            //    label.Text = "livery";
            //    label.Location = new Point(0, 10);
            //    label.Width = 220;

            //    TextBox textBox = new TextBox();
            //    textBox.Location = new Point(230, 10);
            //    textBox.Width = 1100;
            //    textBox.Height = 90;
            //    textBox.Multiline = true;
            //    textBox.ScrollBars = ScrollBars.Vertical;

            //    if (EditedSquad.Livery is Dictionary<int, string> dict)
            //    {
            //        textBox.Text = string.Join(
            //            Environment.NewLine,
            //            dict.OrderBy(x => x.Key)
            //                .Select(x => "[" + x.Key + "] = " + x.Value));
            //    }
            //    else
            //    {
            //        textBox.Text = EditedSquad.Livery.ToString();
            //    }

            //    row.Controls.Add(label);
            //    row.Controls.Add(textBox);

            //    flowLayoutPanelAdditional.Controls.Add(row);
            //}

            foreach (var property in EditedSquad.AdditionalProperties.OrderBy(p => p.Key))
            {
                //if (property.Key == "score_last")
                //    continue;

                if (property.Key == "score_last" ||
                    property.Key == "roster" ||
                    property.Key == "score")
                {
                    continue;
                }

                Panel row = new Panel();
                row.Width = 1400;
                row.Height = 55;

                Label label = new Label();
                label.Text = property.Key;
                label.Location = new Point(0, 18);
                label.Width = 220;

                TextBox textBox = new TextBox();
                textBox.Location = new Point(230, 15);
                textBox.Width = 1100;
                textBox.Text = ConvertPropertyToText(property.Value);

                row.Controls.Add(label);
                row.Controls.Add(textBox);

                flowLayoutPanelAdditional.Controls.Add(row);

                _additionalRows.Add(new AdditionalRow
                {
                    PropertyName = property.Key,
                    ValueTextBox = textBox
                });
            }
        }


        private string ConvertPropertyToText(object value)
        {
            if (value == null)
                return "";

            // Table Lua quelconque : Dictionary<string,...>, Dictionary<int,...>, etc.
            if (value is System.Collections.IDictionary dictionary)
            {
                List<string> items = new List<string>();

                foreach (System.Collections.DictionaryEntry entry in dictionary)
                {
                    items.Add(entry.Key + "=" + entry.Value);
                }

                return string.Join(" ; ", items);
            }

            // Liste
            if (value is System.Collections.IEnumerable enumerable && !(value is string))
            {
                List<string> items = new List<string>();

                foreach (object item in enumerable)
                {
                    items.Add(item != null ? item.ToString() : "");
                }

                return string.Join(" ; ", items);
            }

            return value.ToString();
        }

        // Cette fonction sauvegarde les valeurs simples du formulaire.
        // Pourquoi : recopier les modifications de l'utilisateur dans le squad.
        private void buttonOK_Click(object sender, EventArgs e)
        {
            EditedSquad.Name = textBoxName.Text.Trim();
            EditedSquad.Type = comboBoxType.Text;
            EditedSquad.Country = comboBoxCountry.Text;
            EditedSquad.Base = comboBoxBase.Text;
            EditedSquad.Skill = comboBoxSkill.Text;

            EditedSquad.Player = checkBoxPlayer.Checked;
            EditedSquad.Inactive = checkBoxInactive.Checked;

            EditedSquad.Number = (int)numericNumber.Value;
            EditedSquad.InitNumber = (int)numericInitNumber.Value;
            EditedSquad.Reserve = (int)numericReserve.Value;
            EditedSquad.InitReserve = (int)numericInitReserve.Value;

            // On sauvegarde les tâches dynamiques.
            // Pourquoi : prendre en compte les checkbox et coefficients modifiés.
            if (EditedSquad.Tasks == null)
            {
                //EditedSquad.Tasks = new Dictionary<string, object>();
                EditedSquad.Tasks = new Dictionary<string, bool>();
            }

            if (EditedSquad.TasksCoef == null)
            {
                //EditedSquad.TasksCoef = new Dictionary<string, object>();
                EditedSquad.TasksCoef = new Dictionary<string, double>();
            }

            foreach (TaskRow row in _taskRows)
            {
                EditedSquad.Tasks[row.TaskName] = row.EnabledCheckBox.Checked;
                EditedSquad.TasksCoef[row.TaskName] = (double)row.CoefNumeric.Value;
            }

            // Pour le moment, les AdditionalProperties sont réécrites comme texte.
            // Pourquoi : cela permet déjà de conserver toutes les nouvelles variables visibles.
            if (EditedSquad.AdditionalProperties == null)
            {
                EditedSquad.AdditionalProperties = new Dictionary<string, object>();
            }

            foreach (AdditionalRow row in _additionalRows)
            {
                EditedSquad.AdditionalProperties[row.PropertyName] = row.ValueTextBox.Text;
            }

            DialogResult = DialogResult.OK;
            Close();
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
            public CheckBox EnabledCheckBox { get; set; }
            public NumericUpDown CoefNumeric { get; set; }
        }

        // Cette classe mémorise une ligne de propriété additionnelle.
        private class AdditionalRow
        {
            public string PropertyName { get; set; }
            public TextBox ValueTextBox { get; set; }
        }

        private void comboBox1_SelectedIndexChanged(object sender, EventArgs e)
        {

        }
    }
}
