using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using DCE_Manager.Parameters;
using DCE_Manager.Utils;

namespace DCE_Manager
{
    public partial class Configuration_Form : Form
    {
        private TextBox inlineTextBox;
        private bool isNewConfig = false;

        public Configuration_Form()
        {
            InitializeComponent();
            InitializeInlineTextBox();
            LoadConfigsIntoListBox();

            //// Initialisation par défaut basée sur la campagne sélectionnée si nécessaire
            //if (ParamConf.NumSelectConfig >= 0 && ParamCampaignSelected.NameCampaign != null)
            //{
            //    ParamConf.configDictionary["config_" + ParamConf.NumSelectConfig + "_"] = ParamCampaignSelected.NameCampaign;
            //}
        }

        // Charge les données de la map globale dans la ListBox
        private void LoadConfigsIntoListBox()
        {
            listBox_Config.Items.Clear();

            foreach (var configName in ParamConf.configMap.Keys)
            {
                if (!string.IsNullOrWhiteSpace(configName))
                {
                    listBox_Config.Items.Add(configName);
                }
            }

            // Sélectionner l'élément actif actuel si possible
            if (!string.IsNullOrEmpty(ParamConf.CurrentConfigName) && listBox_Config.Items.Contains(ParamConf.CurrentConfigName))
            {
                listBox_Config.SelectedItem = ParamConf.CurrentConfigName;
            }
            else if (listBox_Config.Items.Count > 0)
            {
                listBox_Config.SelectedIndex = 0;
            }
        }

        private void InitializeInlineTextBox()
        {
            inlineTextBox = new TextBox
            {
                Visible = false,
                BorderStyle = BorderStyle.FixedSingle
            };

            inlineTextBox.KeyDown += InlineTextBox_KeyDown;
            inlineTextBox.Leave += (s, e) => CancelInlineEdit();
            listBox_Config.Controls.Add(inlineTextBox);
        }

        private void But_AddConfig_Click(object sender, EventArgs e)
        {
            isNewConfig = true;
            string tempName = "[Nouvelle_Configuration]";

            listBox_Config.Items.Add(tempName);
            int index = listBox_Config.Items.Count - 1;
            listBox_Config.SelectedIndex = index;

            Rectangle itemRect = listBox_Config.GetItemRectangle(index);
            inlineTextBox.Bounds = new Rectangle(itemRect.X + 2, itemRect.Y, itemRect.Width - 4, itemRect.Height);
            inlineTextBox.Text = "";
            inlineTextBox.Visible = true;
            inlineTextBox.BringToFront();
            inlineTextBox.Focus();
        }

        private void But_Config_Rename_Click(object sender, EventArgs e)
        {
            if (listBox_Config.SelectedIndex == -1) return;

            isNewConfig = false;
            int index = listBox_Config.SelectedIndex;
            Rectangle itemRect = listBox_Config.GetItemRectangle(index);

            inlineTextBox.Bounds = new Rectangle(itemRect.X + 2, itemRect.Y, itemRect.Width - 4, itemRect.Height);
            inlineTextBox.Text = listBox_Config.SelectedItem.ToString();
            inlineTextBox.Visible = true;
            inlineTextBox.BringToFront();
            inlineTextBox.Focus();
            inlineTextBox.SelectAll();
        }

        private void InlineTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                ValidateInlineEdit();
            }
            else if (e.KeyCode == Keys.Escape)
            {
                e.SuppressKeyPress = true;
                CancelInlineEdit();
            }
        }



        private void ValidateInlineEdit()
        {
            string newName = inlineTextBox.Text.Trim();

            if (string.IsNullOrEmpty(newName))
            {
                MessageBox.Show("Please enter a name for this configuration", "Error");
                inlineTextBox.Focus();
                return;
            }

            // Éviter les conflits d'événements pendant la sauvegarde
            inlineTextBox.Visible = false;

            int index = listBox_Config.SelectedIndex;

            if (isNewConfig)
            {
                if (index == -1)
                {
                    listBox_Config.Items.Add(newName);
                    index = listBox_Config.Items.Count - 1;
                }
                else
                {
                    listBox_Config.Items[index] = newName;
                }

                // 1. Attribution d'un nouvel ID incrémental unique
                ParamConf.NumMaxConfig++;
                ParamConf.NumSelectConfig = ParamConf.NumMaxConfig;

                // 2. Enregistrement du nom de la nouvelle config
                string prefix = "config_" + ParamConf.NumSelectConfig + "_";
                ParamConf.configDictionary[prefix] = newName;
                ParamConf.configDictionary["display"] = newName;
                ParamConf.CurrentConfigName = newName;   // ← LA LIGNE QUI MANQUAIT

                // 3. SÉCURITÉ : chemins vides pour la nouvelle config
                ParamConf.configDictionary[prefix + "pathDCS"] = "";
                ParamConf.configDictionary[prefix + "pathSavedGames"] = "";
                ParamConf.configDictionary[prefix + "pathOVGME"] = "";

                ParamConf.PATH_DCS_Root = "";
                ParamConf.PATH_SavedGames_DCS = "";
                ParamConf.PATH_OVGME_MOD = "";

                if (!ParamConf.configMap.ContainsKey(newName))
                {
                    ParamConf.configMap.Add(newName, ParamConf.NumSelectConfig);
                }

                // 4. Sauvegarde physique propre
                Save_Config();

                listBox_Config.SelectedIndex = index;
            }
            else
            {
                // Mode RENAME strict
                if (index != -1)
                {
                    string oldName = listBox_Config.Items[index].ToString();

                    if (oldName != newName)
                    {
                        listBox_Config.Items[index] = newName;

                        // On récupère l'ID actuel lié à l'ancien nom
                        if (ParamConf.configMap.TryGetValue(oldName, out int currentId))
                        {
                            // 1. On met à jour la Map (on remplace juste la clé du nom)
                            ParamConf.configMap.Remove(oldName);
                            ParamConf.configMap[newName] = currentId;

                            // 2. On met à jour le dictionnaire avec le même ID, mais le nouveau nom
                            ParamConf.configDictionary["config_" + currentId + "_"] = newName;

                            // FORCE l'ID actif sur l'ID en cours de renommage pour que Save_Config ne se trompe pas
                            ParamConf.NumSelectConfig = currentId;
                        }

                        ParamConf.configDictionary["display"] = newName;
                        ParamConf.CurrentConfigName = newName;

                        // 3. On sauvegarde : les lignes config_N_path conservent le même N, seul le nom a changé !
                        Save_Config();


                    }
                }
            }

            // Mise à jour de l'UI principale (FormMain) si elle est ouverte
            if (Main_Form.Instance != null)
            {
                Main_Form.Instance.UpdateComboBoxConfigList();
            }
        }

        private void CancelInlineEdit()
        {
            if (!inlineTextBox.Visible) return;
            inlineTextBox.Visible = false;

            if (isNewConfig && listBox_Config.SelectedIndex != -1)
            {
                listBox_Config.Items.RemoveAt(listBox_Config.SelectedIndex);
            }
        }

        private void but_Configuration_Close_Click(object sender, EventArgs e)
        {
            DialogResult = DialogResult.OK;
            this.Close();
        }




        public static bool Save_Config()
        {
            string folderPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "DCE_Manager");
            string filePath = Path.Combine(folderPath, "options.txt");

            try
            {
                if (!Directory.Exists(folderPath)) Directory.CreateDirectory(folderPath);

                // 1. Synchroniser le dictionnaire avec les valeurs actuelles
                ParamConf.configDictionary["ASTI_MissionFile"] = ParamConf.AstiMissionFile ?? string.Empty;
                ParamConf.configDictionary["ASTI_importTemplateFolder"] = ParamConf.AstiImportTemplateFolder ?? string.Empty;
                ParamConf.configDictionary["upgradeTxtDownload"] = ParamConf.UpgradeTime ?? string.Empty;
                ParamConf.configDictionary["LastNewsVersion"] = ParamConf.LastNewsVersion ?? string.Empty;
                ParamConf.configDictionary["LastGithubCheckUtc"] = Updater_Param.LastGithubCheckUtc.ToString("O");
                ParamConf.configDictionary["NbLancement"] = ParamManager.NbLancement.ToString();
                ParamConf.configDictionary["verScriptsMod"] = ParamScriptsMod.verScriptsMod ?? string.Empty;

                string prefix = $"config_{ParamConf.NumSelectConfig}_";
                ParamConf.configDictionary[prefix + "pathDCS"] = ParamConf.PATH_DCS_Root ?? string.Empty;
                ParamConf.configDictionary[prefix + "pathSavedGames"] = ParamConf.PATH_SavedGames_DCS ?? string.Empty;
                ParamConf.configDictionary[prefix + "pathOVGME"] = ParamConf.PATH_OVGME_MOD ?? string.Empty;

                // 2. Charger le fichier actuel pour la fusion (en ignorant les clés corrompues ou vides)
                Dictionary<string, string> fileDictionary = new Dictionary<string, string>();
                if (File.Exists(filePath))
                {
                    foreach (var line in File.ReadAllLines(filePath))
                    {
                        if (string.IsNullOrWhiteSpace(line) || line.StartsWith("--")) continue;

                        int index = line.IndexOf('=');
                        if (index > 0)
                        {
                            string key = line.Substring(0, index).Trim();
                            string value = line.Substring(index + 1).Trim();

                            // PURGE : On ignore les anciennes lignes de config vides stockées par erreur
                            if (key.StartsWith("config_") && key.EndsWith("_") && string.IsNullOrEmpty(value))
                                continue;

                            fileDictionary[key] = value;
                        }
                    }
                }

                // 3. Fusionner avec les données fraîches
                foreach (var entry in ParamConf.configDictionary)
                {
                    fileDictionary[entry.Key] = entry.Value;
                }


                // 4. Écriture propre filtrée sans résidus ni orphelins
                using (StreamWriter writer = new StreamWriter(filePath))
                {
                    foreach (var entry in fileDictionary)
                    {
                        // --- SÉCURITÉ ANTI-FANTÔMES ---
                        if (entry.Key.StartsWith("config_"))
                        {
                            // On extrait l'ID (ex: extrait "0" de "config_0_pathDCS")
                            int nextUnderscore = entry.Key.IndexOf('_', 7);
                            if (nextUnderscore > 7)
                            {
                                string idStr = entry.Key.Substring(7, nextUnderscore - 7);

                                // Si la config_N_ correspondante n'existe nulle part dans notre dictionnaire actuel,
                                // ou si la valeur du nom est vide, on ignore TOUTES les clés de cet ID (chemins inclus).
                                string nameKey = $"config_{idStr}_";
                                if (!ParamConf.configDictionary.TryGetValue(nameKey, out string currentName) || string.IsNullOrWhiteSpace(currentName))
                                {
                                    continue; // Purge immédiate de la ligne, elle ne sera pas écrite !
                                }
                            }
                        }
                        // ------------------------------

                        writer.WriteLine($"{entry.Key}={entry.Value}");
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                FormUtils.ErrorGeneral_BoxOrLog(ex, "Erreur lors de la mise à jour du fichier de config", filePath, true, true);
                return false;
            }
        }

        private void But_Config_Delete_Click(object sender, EventArgs e)
        {
            // 1. Vérifier qu'une configuration est sélectionnée
            if (listBox_Config.SelectedIndex == -1)
            {
                MessageBox.Show("Please select a configuration to delete.", "Warning", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            string configToDelete = listBox_Config.SelectedItem.ToString();

            // Sécurité : Éviter de tout supprimer s'il ne reste qu'une seule config
            if (listBox_Config.Items.Count <= 1)
            {
                MessageBox.Show("You must keep at least one configuration.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            // 2. Demander confirmation à l'utilisateur
            var confirmResult = MessageBox.Show(
                $"Are you sure you want to delete the configuration '{configToDelete}'?\nThis will clear all its associated paths.",
                "Confirm Delete",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question
            );

            if (confirmResult == DialogResult.Yes)
            {
                // 3. Récupérer l'ID unique de la config pour nettoyer le dictionnaire
                if (ParamConf.configMap.TryGetValue(configToDelete, out int idToDelete))
                {
                    string prefix = $"config_{idToDelete}_";

                    // Liste de toutes les clés possibles à nettoyer pour cet ID
                    List<string> keysToRemove = new List<string>
            {
                prefix,                          // Le nom de la config (ex: config_1_)
                prefix + "pathDCS",              // Le chemin DCS
                prefix + "pathSavedGames",       // Le chemin Saved Games alternatif
                prefix + "PATH_SavedGames_DCS",  // L'autre variante de clé Saved Games
                prefix + "pathOVGME",            // Le chemin OvGME
                prefix + "pathZipCampaign"       // Le chemin Zip
            };

                    // Suppression physique dans le dictionnaire global
                    foreach (var key in keysToRemove)
                    {
                        if (ParamConf.configDictionary.ContainsKey(key))
                        {
                            ParamConf.configDictionary.Remove(key);
                        }
                    }

                    // Supprimer également de la Map d'indexation
                    ParamConf.configMap.Remove(configToDelete);
                }

                // 4. Supprimer l'élément de la ListBox visuelle
                int currentIndex = listBox_Config.SelectedIndex;
                listBox_Config.Items.RemoveAt(currentIndex);

                // 5. Gérer le repli si la config supprimée était celle active ("display")
                if (ParamConf.CurrentConfigName == configToDelete)
                {
                    // On sélectionne la première config restante dans la liste
                    listBox_Config.SelectedIndex = 0;
                    string newActiveConfig = listBox_Config.SelectedItem.ToString();

                    ParamConf.CurrentConfigName = newActiveConfig;
                    ParamConf.configDictionary["display"] = newActiveConfig;


                    if (ParamConf.configMap.TryGetValue(newActiveConfig, out int newId))
                    {
                        ParamConf.NumSelectConfig = newId;
                            
                        // On recharge les VRAIS chemins de la nouvelle config active depuis le
                        // dictionnaire, sinon Save_Config() va les écraser avec les anciennes
                        // valeurs globales (celles de la config qu'on vient de supprimer)
                        string newPrefix = $"config_{newId}_";
                        ParamConf.PATH_DCS_Root = ParamConf.configDictionary.TryGetValue(newPrefix + "pathDCS", out var pDcs) ? pDcs : "";
                        ParamConf.PATH_SavedGames_DCS = ParamConf.configDictionary.TryGetValue(newPrefix + "pathSavedGames", out var pSaved) ? pSaved : "";
                        ParamConf.PATH_OVGME_MOD = ParamConf.configDictionary.TryGetValue(newPrefix + "pathOVGME", out var pOvgme) ? pOvgme : "";
                    }
                    
                }
                else
                {
                    // Si on a supprimé une autre config que l'active, on garde la sélection sur l'active
                    if (listBox_Config.Items.Contains(ParamConf.CurrentConfigName))
                    {
                        listBox_Config.SelectedItem = ParamConf.CurrentConfigName;
                    }
                    else
                    {
                        listBox_Config.SelectedIndex = 0;
                    }
                }

                // 6. Sauvegarder immédiatement les changements sur le disque (le fichier options.txt sera purgé)
                Save_Config();

                // 7. Mettre à jour la ComboBox du formulaire principal si l'instance est accessible
                if (Main_Form.Instance != null)
                {
                    Main_Form.Instance.UpdateComboBoxConfigList();
                }
            }
        }
    }
}


