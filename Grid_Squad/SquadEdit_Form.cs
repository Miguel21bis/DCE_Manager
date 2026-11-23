// Fichier à remplacer : FormSquadEdit.cs
// But : remplir automatiquement la grande fenêtre avec toutes les données du squad.
// Pourquoi : permettre d'afficher et modifier immédiatement toutes les variables connues et futures.

using System;
using System.Collections;

//using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Reflection;

//using System.Web.UI.WebControls;
using System.Windows.Forms;
using DCE_Manager.Parameters;
using DCE_Manager.Utils;

namespace DCE_Manager
{
    public partial class SquadEdit_Form : Form
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
        // Vrai pendant le remplissage initial des contrôles.
        // Pourquoi : éviter que l'ouverture du form écrase le roster de la campagne.
        private bool _isLoading;

        // On édite une COPIE du squad (EditedSquad). L'original (celui de la campagne en mémoire) n'est modifié qu'au clic sur Save.
        // null en mode clone : le clone est alors le résultat, rendu à l'appelant.
        private Squad _original;

        // Vrai dès que l'utilisateur a changé quelque chose (sert à demander confirmation à la fermeture)
        private bool _dirty;

        // Vrai pendant la construction de la zone des tâches (les NumericUpDown déclenchent ValueChanged en se remplissant)
        private bool _building;

        // Vrai quand on ferme avec Cancel : pas de question
        private bool _discard;

        private Button _btnSave;
        private Button _btnCancel;


        // Event déclenché à chaque modification importante du squad
        public event Action SquadUpdated;



        //CONSTRUCTEUR
        public SquadEdit_Form(Squad squad, CampaignContext campaignContext, bool cloneMode = false, String txt="")
        {
            // Un squad null n'a aucun sens ici.
            // Pourquoi : mieux vaut refuser tout de suite que construire une Form à moitié initialisée.
            if (squad == null)
                throw new ArgumentNullException(nameof(squad),
                    "SquadEdit_Form : squad null (version Init/Active inexistante ?)");

            InitializeComponent();

            // Affichage barré des bases inactives
            // Pourquoi : rendre visuellement cohérentes toutes les listes de bases
            comboBoxBase.DrawMode = DrawMode.OwnerDrawFixed;
            comboBoxBase.DrawItem += ComboBoxBase_DrawItem;

            comboBox_All_bases.DrawMode = DrawMode.OwnerDrawFixed;
            comboBox_All_bases.DrawItem += ComboBoxBase_DrawItem;

            listBoxBasesAlternat.DrawMode = DrawMode.OwnerDrawFixed;
            listBoxBasesAlternat.DrawItem += ListBoxBasesAlternat_DrawItem;

            _campaignContext = campaignContext;

            _isActiveVersion = squad.FolderFile == "Active";

            // On clone le squad pour éviter de modifier l'original avant Save.
            // Pourquoi : l'utilisateur peut encore annuler.
            _original = cloneMode ? null : squad;
            EditedSquad = cloneMode ? CloneSquad(squad) : MakeWorkingCopy(squad);

            // Abonnement APRÈS l'affectation de EditedSquad.
            // Pourquoi : le handler s'en sert, il ne doit jamais tomber sur un null.
            textBoxName.TextChanged += (s, e) =>
            {
                if (EditedSquad == null || _isLoading)
                    return;

                EditedSquad.Name = textBoxName.Text.Trim();
                EditedSquad.DisplayName = textBoxName.Text.Trim();
                MarkChanged();
            };

            Text = cloneMode
            ? "Clone Squad - " + EditedSquad.DisplayName
            : "Edit Squad - " + EditedSquad.DisplayName;

            LoadStaticLists();
            _isLoading = true;
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
            BuildSaveCancelButtons();

            // _isLoading reste vrai pendant toute la construction : rien de ce qui se remplit ici ne compte comme une modification
            _isLoading = false;
           
           
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
            textBoxName.Text = EditedSquad.Name;

            comboBoxType.Text = EditedSquad.Type;
            comboBoxCountry.Text = EditedSquad.Country;
            comboBoxBase.Text = EditedSquad.Base;
            comboBoxSkill.Text = EditedSquad.Skill;

            checkBoxPlayer.Checked = EditedSquad.Player;
            checkBox_HumainOnly.Checked = EditedSquad.HumainOnly;
            checkBoxActive.Checked = EditedSquad.Squad_Active;

            numericNumber.Value = EditedSquad.Number;
            numericReserve.Value = EditedSquad.Reserve;
            
            textBox_ModexNb_Min.Text = EditedSquad.SideNumberMin.ToString();
            textBox_ModexNb_Max.Text = EditedSquad.SideNumberMax.ToString();
        }


        //***********************BASE ALT START

        // Synchronise Base + BaseAlternative depuis la ListBox
        // Pourquoi : garder une logique unique et cohérente partout
        private void SyncBases()
        {
            List<string> bases = listBoxBasesAlternat.Items
                .Cast<object>()
                .Select(x => x.ToString())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            // -------------------------------------------------
            // Base principale = première base ACTIVE
            // -------------------------------------------------

            string activeBase = bases
                .FirstOrDefault(b => !IsAirbaseInactive(b));

            EditedSquad.Base = activeBase;

            // -------------------------------------------------
            // BaseAlternative
            // -------------------------------------------------

            if (bases.Count <= 1)
            {
                EditedSquad.BaseAlternative = new List<string>();
            }
            else
            {
                EditedSquad.BaseAlternative = new List<string>(bases);
            }

            // -------------------------------------------------
            // Refresh combo base
            // -------------------------------------------------

            comboBoxBase.SelectedItem = EditedSquad.Base;

            MarkChanged();
        }

        // Cette fonction remplit la ListBox des bases alternatives.
        // Pourquoi : afficher uniquement les bases de repli (sans la base principale).
        // Cette fonction remplit la ListBox des bases possibles.
        // Pourquoi : afficher la vraie liste priorisée des bases.
        private void BuildBasesAlternative()
        {
            listBoxBasesAlternat.Items.Clear();

            // -------------------------------------------------
            // Cas multi-bases
            // -------------------------------------------------

            if (EditedSquad.BaseAlternative != null
                && EditedSquad.BaseAlternative.Count > 0)
            {
                foreach (string airbase in EditedSquad.BaseAlternative)
                {
                    listBoxBasesAlternat.Items.Add(airbase);
                }

                return;
            }

           
        }
        // Ajoute une base alternative depuis la comboBox_All_bases.
        // Pourquoi : permettre d'ajouter une base de repli sans doublon ni incohérence.
        private void button_base_plus_Click(object sender, EventArgs e)
        {
            string selectedBase = comboBox_All_bases.SelectedItem as string;

            if (string.IsNullOrEmpty(selectedBase))
                return;

            // Interdit : doublon
            foreach (var item in listBoxBasesAlternat.Items)
            {
                if (string.Equals(item.ToString(), selectedBase, StringComparison.OrdinalIgnoreCase))
                    return;
            }

            // -------------------------------------------------
            // Transition mono-base -> multi-bases
            // -------------------------------------------------

            if (listBoxBasesAlternat.Items.Count == 0
                && !string.IsNullOrWhiteSpace(EditedSquad.Base))
            {
                listBoxBasesAlternat.Items.Add(EditedSquad.Base);
            }

            listBoxBasesAlternat.Items.Add(selectedBase);

            SyncBases();

        }
        // Supprime la base alternative sélectionnée.
        // Pourquoi : permettre de retirer une base de repli.
        // Supprime la base sélectionnée.
        // Pourquoi : maintenir automatiquement les priorités cohérentes.
        private void buttonBaseMoins_Click(object sender, EventArgs e)
        {
            int index = listBoxBasesAlternat.SelectedIndex;

            if (index >= 0)
            {
                listBoxBasesAlternat.Items.RemoveAt(index);

                SyncBases();
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
            SyncBases();
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
            SyncBases();
        }
        // Quand la base principale change.
        // Pourquoi : déplacer cette base en priorité 0.
        private void comboBoxBase_SelectedIndexChanged(object sender, EventArgs e)
        {
            string newBase = comboBoxBase.SelectedItem as string;

            if (string.IsNullOrWhiteSpace(newBase))
                return;

            // -------------------------------------------------
            // Cas mono-base
            // -------------------------------------------------

            if (listBoxBasesAlternat.Items.Count == 0)
            {
                EditedSquad.Base = newBase;

                MarkChanged();

                return;
            }

            // -------------------------------------------------
            // Retire ancienne occurrence
            // -------------------------------------------------

            for (int i = listBoxBasesAlternat.Items.Count - 1; i >= 0; i--)
            {
                if (string.Equals(
                    listBoxBasesAlternat.Items[i].ToString(),
                    newBase,
                    StringComparison.OrdinalIgnoreCase))
                {
                    listBoxBasesAlternat.Items.RemoveAt(i);
                }
            }

            // -------------------------------------------------
            // Insère en priorité 0
            // -------------------------------------------------

            listBoxBasesAlternat.Items.Insert(0, newBase);

            listBoxBasesAlternat.SelectedIndex = 0;

            SyncBases();
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

            MarkChanged();
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

        // Cette fonction construit dynamiquement la zone des Tasks.
        // Pourquoi : chaque squad peut avoir des Tasks différentes (selon son type d'avion).
        private void BuildTasksArea()
        {
            _building = true;

            try
            {
                flowLayoutPanelTasks.Controls.Clear();
                _taskRows.Clear();

                if (EditedSquad.Tasks == null)
                    return;

                if (EditedSquad.TasksCoef == null)
                    EditedSquad.TasksCoef = new Dictionary<string, double>();

                foreach (var task in EditedSquad.Tasks.OrderBy(t => t.Key))
                {
                    string taskName = task.Key;

                    System.Windows.Forms.Panel row = new System.Windows.Forms.Panel();
                    row.Width = 400;
                    row.Height = 32;

                    System.Windows.Forms.Label label = new System.Windows.Forms.Label();
                    label.Text = taskName;
                    label.Location = new Point(0, 7);
                    label.Width = 75;

                    System.Windows.Forms.CheckBox checkBox = new System.Windows.Forms.CheckBox();
                    checkBox.Checked = Convert.ToBoolean(task.Value);
                    checkBox.Location = new Point(75, 5);

                    // MAJ de la copie de travail quand on coche/décoche
                    checkBox.CheckedChanged += (s, e) =>
                    {
                        if (_building) return;
                        EditedSquad.Tasks[taskName] = checkBox.Checked;
                        MarkChanged();
                    };

                    System.Windows.Forms.Label labelCoef = new System.Windows.Forms.Label();
                    labelCoef.Text = "Coef";
                    labelCoef.Location = new Point(100, 7);
                    labelCoef.Width = 50;

                    NumericUpDown numericCoef = new NumericUpDown();
                    numericCoef.DecimalPlaces = 2;
                    numericCoef.Maximum = 999;
                    numericCoef.Minimum = -999;
                    numericCoef.Width = 80;
                    numericCoef.Location = new Point(300, 3);

                    if (EditedSquad.TasksCoef.ContainsKey(taskName))
                    {
                        decimal coef = Convert.ToDecimal(EditedSquad.TasksCoef[taskName]);
                        numericCoef.Value = Math.Max(numericCoef.Minimum, Math.Min(numericCoef.Maximum, coef));
                    }

                    // MAJ de la copie de travail du coef (abonné APRÈS le remplissage)
                    numericCoef.ValueChanged += (s, e) =>
                    {
                        if (_building) return;
                        EditedSquad.TasksCoef[taskName] = (double)numericCoef.Value;
                        MarkChanged();
                    };

                    row.Controls.Add(label);
                    row.Controls.Add(checkBox);
                    row.Controls.Add(labelCoef);
                    row.Controls.Add(numericCoef);

                    flowLayoutPanelTasks.Controls.Add(row);

                    _taskRows.Add(new TaskRow
                    {
                        TaskName = taskName,
                        EnabledCheckBox = checkBox,
                        CoefNumeric = numericCoef
                    });
                }
            }
            finally
            {
                _building = false;
            }
        }

        // Tâches possibles pour un type d'avion (sans doublon, triées), d'après DCEM_Function.lua.
        // Type absent de la table : on propose toutes les tâches connues, à l'utilisateur de cocher.
        private List<string> TasksForType(string type)
        {
            Dictionary<string, List<string>> map = _campaignContext?.LuaData?.TaskByPlane;
            if (map == null)
                return null;

            List<string> list;
            if (!string.IsNullOrEmpty(type) && map.TryGetValue(type, out list))
                return list.Distinct().OrderBy(t => t).ToList();

            return map.Values.SelectMany(l => l).Distinct().OrderBy(t => t).ToList();
        }

        // Remplace les tâches du squad par celles du type choisi.
        // Une tâche déjà connue garde son état et son coef ; les nouvelles sont décochées (coef 1) ; les autres disparaissent.
        private void ApplyTasksOfType()
        {
            List<string> wanted = TasksForType(EditedSquad.Type);
            if (wanted == null)
                return;

            Dictionary<string, bool> oldTasks = EditedSquad.Tasks ?? new Dictionary<string, bool>();
            Dictionary<string, double> oldCoef = EditedSquad.TasksCoef ?? new Dictionary<string, double>();

            var newTasks = new Dictionary<string, bool>();
            var newCoef = new Dictionary<string, double>();

            foreach (string t in wanted)
            {
                bool on;
                double coef;
                newTasks[t] = oldTasks.TryGetValue(t, out on) && on;
                newCoef[t] = oldCoef.TryGetValue(t, out coef) ? coef : 1.0;
            }

            EditedSquad.Tasks = newTasks;
            EditedSquad.TasksCoef = newCoef;
        }

        // Changement de type d'avion : tâches et callsigns suivent le nouveau type
        private void ApplyType(string type)
        {
            EditedSquad.Type = type;
            ApplyTasksOfType();
            BuildTasksArea();
            RefreshCallsignList();
            MarkChanged();
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

                    MarkChanged();
                };

                group.Controls.Add(label);
                group.Controls.Add(numeric);

                y += 28;
            }

            flowLayoutPanelScore.Controls.Add(group);
        }


        // ---------------------------------------------------------------
        //  Copie de travail, Save et Cancel
        // ---------------------------------------------------------------

        // Noter qu'un changement a été fait (hors remplissage initial)
        private void MarkChanged()
        {
            if (_isLoading || _building)
                return;

            _dirty = true;
        }

        // Copie de travail : tous les champs (privés compris) + copie profonde des collections.
        // Pourquoi : l'original ne doit pas bouger tant que l'utilisateur n'a pas cliqué sur Save.
        private static Squad MakeWorkingCopy(Squad source)
        {
            Squad copy = source.Clone();
            CopyValues(source, copy);

            copy.BaseAlternative = source.BaseAlternative != null ? new List<string>(source.BaseAlternative) : new List<string>();
            copy.Tasks = source.Tasks != null ? new Dictionary<string, bool>(source.Tasks) : new Dictionary<string, bool>();
            copy.TasksCoef = source.TasksCoef != null ? new Dictionary<string, double>(source.TasksCoef) : new Dictionary<string, double>();
            copy.TasksCoefPourcent = source.TasksCoefPourcent != null ? new Dictionary<string, int>(source.TasksCoefPourcent) : null;
            copy.Roster = source.Roster != null ? new Dictionary<string, object>(source.Roster) : null;
            copy.Score = source.Score != null ? new Dictionary<string, object>(source.Score) : null;
            copy.ScoreLast = source.ScoreLast != null ? new Dictionary<string, object>(source.ScoreLast) : null;
            copy.AdditionalProperties = source.AdditionalProperties != null ? new Dictionary<string, object>(source.AdditionalProperties) : new Dictionary<string, object>();
            copy.parking_id = source.parking_id != null ? new Dictionary<string, object>(source.parking_id) : new Dictionary<string, object>();
            copy.Livery = source.Livery != null ? new Dictionary<int, string>(source.Livery) : new Dictionary<int, string>();
            copy.LiveryModex = source.LiveryModex != null ? new Dictionary<int, string>(source.LiveryModex) : new Dictionary<int, string>();

            return copy;
        }

        // Recopie tous les champs (privés et classes parentes compris) d'un squad vers un autre
        private static void CopyValues(Squad from, Squad to)
        {
            for (Type t = typeof(Squad); t != null && t != typeof(object); t = t.BaseType)
            {
                foreach (FieldInfo f in t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                {
                    if (f.IsInitOnly || f.IsLiteral) continue;
                    f.SetValue(to, f.GetValue(from));
                }
            }
        }

        // Boutons Save / Cancel en bas de la fenêtre
        private void BuildSaveCancelButtons()
        {
            var bar = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                Height = 46,
                FlowDirection = FlowDirection.RightToLeft,
                Padding = new Padding(8)
            };

            _btnCancel = new Button { Text = "Cancel", Width = 100, Height = 30 };
            _btnCancel.Click += buttonCancel_Click;

            _btnSave = new Button { Text = "Save", Width = 100, Height = 30 };
            _btnSave.Click += buttonSave_Click;

            bar.Controls.Add(_btnCancel);   // RightToLeft : le premier ajouté est tout à droite
            bar.Controls.Add(_btnSave);

            Controls.Add(bar);
            bar.SendToBack();               // le panneau prend sa place en bas AVANT les autres contrôles
        }

        // Save : la copie de travail est recopiée dans le squad de la campagne, puis l'appelant est prévenu
        private void buttonSave_Click(object sender, EventArgs e)
        {
            // Type tapé à la main sans être choisi dans la liste
            string typed = comboBoxType.Text.Trim();
            if (typed != EditedSquad.Type)
            {
                if (!comboBoxType.Items.Contains(typed))
                {
                    MessageBox.Show("Unknown aircraft type: " + typed, "Edit Squad", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                ApplyType(typed);
            }

            if (_original != null)
            {
                bool typeChanged = _original.Type != EditedSquad.Type;

                bool stateChanged = _original.Squad_Inactive != EditedSquad.Squad_Inactive;

                bool nameChanged = _original.Name != EditedSquad.Name;

                CopyValues(EditedSquad, _original);

                // Le nom doit rester identique dans Init et Active (DCE les associe par le nom).
                // Au Save de la campagne, les deux fichiers seront écrits.
                if (nameChanged)
                {
                    SyncTwinName(_original);
                    Parser_OobAir.NamesTouched = true;
                }

                // Un squad existe en deux versions (Init et Active). Si le type d'avion change, l'autre version doit suivre,
                // sinon elle reste sur l'ancien type (A-10) et la génération de mission remet le squad à l'ancien type.
                if (typeChanged)
                    SyncTwinType(_original);

                if (stateChanged)
                    SyncTwinState(_original);

                EditedSquad = _original;   // les appelants lisent frm.EditedSquad : ils doivent voir le vrai squad

                // Un seul Player dans la campagne
                if (EditedSquad.Player)
                    _campaignContext.CampaignEditRef.SetSinglePlayerSquad(EditedSquad);
            }

            _dirty = false;
            DialogResult = DialogResult.OK;
            SquadUpdated?.Invoke();
            Close();
        }

        // Nom : l'autre version (Init <-> Active) du même squad prend le même nom
        private void SyncTwinName(Squad edited)
        {
            var list = _campaignContext?.CampaignEditRef?.CurrentCampaignSquads;
            if (list == null)
                return;

            foreach (CampaignSquad cs in list)
            {
                Squad twin = ReferenceEquals(cs.Init, edited) ? cs.Active
                           : ReferenceEquals(cs.Active, edited) ? cs.Init
                           : null;

                if (twin == null || ReferenceEquals(twin, edited))
                    continue;

                twin.Name = edited.Name;
                twin.DisplayName = edited.Name;
                return;
            }
        }

        // Actif / inactif : l'export de la Preview relit la version Init, l'autre version doit donc suivre
        private void SyncTwinState(Squad edited)
        {
            var list = _campaignContext?.CampaignEditRef?.CurrentCampaignSquads;
            if (list == null)
                return;

            foreach (CampaignSquad cs in list)
            {
                Squad twin = ReferenceEquals(cs.Init, edited) ? cs.Active
                           : ReferenceEquals(cs.Active, edited) ? cs.Init
                           : null;

                if (twin == null || ReferenceEquals(twin, edited))
                    continue;

                twin.Squad_Inactive = edited.Squad_Inactive;
                return;
            }
        }

        // Recopie le type et ce qui en dépend (tâches, livrées, callsign) sur l'autre version (Init <-> Active) du même squad
        private void SyncTwinType(Squad edited)
        {
            var list = _campaignContext?.CampaignEditRef?.CurrentCampaignSquads;
            if (list == null)
                return;

            foreach (CampaignSquad cs in list)
            {
                Squad twin = ReferenceEquals(cs.Init, edited) ? cs.Active
                           : ReferenceEquals(cs.Active, edited) ? cs.Init
                           : null;

                if (twin == null || ReferenceEquals(twin, edited))
                    continue;

                twin.Type = edited.Type;
                twin.Tasks = new Dictionary<string, bool>(edited.Tasks);
                twin.TasksCoef = new Dictionary<string, double>(edited.TasksCoef);
                twin.TasksCoefPourcent = edited.TasksCoefPourcent != null ? new Dictionary<string, int>(edited.TasksCoefPourcent) : null;
                twin.Livery = new Dictionary<int, string>(edited.Livery);
                twin.LiveryModex = new Dictionary<int, string>(edited.LiveryModex);
                twin.Callsign = edited.Callsign;
                return;
            }
        }

        // Cancel : on jette la copie de travail, rien n'a été modifié dans la campagne
        private void buttonCancel_Click(object sender, EventArgs e)
        {
            _discard = true;
            DialogResult = DialogResult.Cancel;
            Close();
        }

        // Fermeture par la croix avec des changements non enregistrés : on demande
        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!_discard && _dirty && DialogResult != DialogResult.OK && e.CloseReason == CloseReason.UserClosing)
            {
                DialogResult answer = MessageBox.Show(
                    "Discard your changes?",
                    "Edit Squad",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question);

                if (answer != DialogResult.Yes)
                    e.Cancel = true;
            }

            base.OnFormClosing(e);
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
            // Pendant le remplissage initial, on ne touche à rien (sinon les tâches du fichier seraient remplacées)
            if (_isLoading)
                return;

            string type = comboBoxType.SelectedItem as string ?? comboBoxType.Text;
            if (string.IsNullOrWhiteSpace(type) || type == EditedSquad.Type)
                return;

            ApplyType(type);
        }

        private void comboBoxSkill_SelectedIndexChanged(object sender, EventArgs e)
        {
            EditedSquad.Skill = comboBoxSkill.Text;
            MarkChanged();
        }

        private void checkBoxPlayer_CheckedChanged(object sender, EventArgs e)
        {
            // Remplissage initial : rien à faire
            if (_isLoading)
                return;

            // Le vrai squad (celui de la liste) : on ne compare jamais avec la copie de travail
            Squad self = _original ?? EditedSquad;

            // Empêche de décocher si c'est le dernier Player
            if (!checkBoxPlayer.Checked)
            {
                bool anotherPlayerExists = _campaignContext.CampaignEditRef
                    .CurrentCampaignSquads
                    .Any(cs =>
                        (cs.Active != null && cs.Active != self && cs.Active.Player)
                        ||
                        (cs.Init != null && cs.Init != self && cs.Init.Player)
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

            // Un seul Player dans la campagne : la règle est appliquée au Save (SetSinglePlayerSquad)
            EditedSquad.Player = checkBoxPlayer.Checked;
            MarkChanged();
        }

        private void checkBox_HumainOnly_CheckedChanged(object sender, EventArgs e)
        {
            EditedSquad.HumainOnly = checkBox_HumainOnly.Checked;
            MarkChanged();
        }


        private void checkBoxActive_CheckedChanged(object sender, EventArgs e)
        {
            EditedSquad.Squad_Active = checkBoxActive.Checked;

            MarkChanged();
        }

        private void numericNumber_ValueChanged(object sender, EventArgs e)
        {
            if (_isLoading)
                return;

            EditedSquad.Number = (int)numericNumber.Value;
            EditedSquad.InitNumber = (int)numericNumber.Value;

            // Le roster n'existe que dans Active\oob_air.lua
            // Pourquoi : c'est lui que DCE lit, peu importe que le squad soit actif ou non
            if (_isActiveVersion)
            {
                if (EditedSquad.Roster == null)
                {
                    EditedSquad.Roster = new Dictionary<string, object>();
                }

                EditedSquad.Roster["ready"] = (int)numericNumber.Value;

                // Refresh UI live du roster
                BuildScoreArea();
            }

            MarkChanged();
        }


        private void numericReserve_ValueChanged(object sender, EventArgs e)
        {
            if (_isLoading)
                return;

            EditedSquad.Reserve = (int)numericReserve.Value;
            EditedSquad.InitReserve = (int)numericReserve.Value;

            // Le roster n'existe que dans Active\oob_air.lua
            // Pourquoi : c'est lui que DCE lit, peu importe que le squad soit actif ou non
            if (_isActiveVersion)
            {
                if (EditedSquad.Roster == null)
                {
                    EditedSquad.Roster = new Dictionary<string, object>();
                }

                EditedSquad.Roster["reserve"] = (int)numericReserve.Value;

                // Refresh UI live du roster
                BuildScoreArea();
            }

            MarkChanged();
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
                _isRefreshingCallsign = false;
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
            // ("Automatic" est déjà ajouté tout en haut de la méthode : plus de doublon)

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

        // Retourne true si la base est inactive
        // Pourquoi : centraliser la logique de détection
        //private bool IsAirbaseInactive(string airbaseName)
        //{
        //    if (string.IsNullOrWhiteSpace(airbaseName))
        //        return false;

        //    if (_campaignContext?.Airbases == null)
        //        return false;

        //    foreach (var airbase in _campaignContext.Airbases.Values)
        //    {
        //        if (string.Equals(
        //            airbase.Name,
        //            airbaseName,
        //            StringComparison.OrdinalIgnoreCase))
        //        {
        //            return airbase.Inactive;
        //        }
        //    }

        //    return false;
        //}

        // Vérifie si une base est inactive
        // Pourquoi : centraliser la logique de détection
        private bool IsAirbaseInactive(string airbaseName)
        {
            if (string.IsNullOrWhiteSpace(airbaseName))
                return false;

            if (_campaignContext?.Airbases == null)
                return false;

            foreach (var airbase in _campaignContext.Airbases.Values)
            {
                if (string.Equals(
                    airbase.Name,
                    airbaseName,
                    StringComparison.OrdinalIgnoreCase))
                {
                    return airbase.Inactive;
                }
            }

            return false;
        }

        // Dessine les bases alternatives
        // Pourquoi : afficher les bases inactives en barré
        //private void listBoxBasesAlternat_DrawItem(object sender, DrawItemEventArgs e)
        //{
        //    if (e.Index < 0)
        //        return;

        //    string text = listBoxBasesAlternat.Items[e.Index].ToString();

        //    e.DrawBackground();

        //    bool inactive = IsAirbaseInactive(text);

        //    Font font = inactive
        //        ? new Font(e.Font, FontStyle.Strikeout)
        //        : e.Font;

        //    TextRenderer.DrawText(
        //        e.Graphics,
        //        text,
        //        font,
        //        e.Bounds,
        //        e.ForeColor,
        //        TextFormatFlags.Left);

        //    e.DrawFocusRectangle();

        //    // Important
        //    if (inactive)
        //    {
        //        font.Dispose();
        //    }
        //}

        // Dessine les bases alternatives
        // Pourquoi : barrer les bases inactives
        private void ListBoxBasesAlternat_DrawItem(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0)
                return;

            string text = listBoxBasesAlternat.Items[e.Index]?.ToString() ?? "";

            bool inactive = IsAirbaseInactive(text);

            e.DrawBackground();

            Font font = inactive
                ? new Font(e.Font, FontStyle.Strikeout)
                : e.Font;

            TextRenderer.DrawText(
                e.Graphics,
                text,
                font,
                e.Bounds,
                e.ForeColor,
                TextFormatFlags.Left);

            e.DrawFocusRectangle();

            if (inactive)
            {
                font.Dispose();
            }
        }

        // Dessine les bases inactives en barré
        // Pourquoi : rendre immédiatement visibles les bases inutilisables
        private void ComboBoxBase_DrawItem(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0)
                return;

            ComboBox combo = sender as ComboBox;

            string text = combo.Items[e.Index]?.ToString() ?? "";

            bool inactive = IsAirbaseInactive(text);

            e.DrawBackground();

            Font font = inactive
                ? new Font(e.Font, FontStyle.Strikeout)
                : e.Font;

            TextRenderer.DrawText(
                e.Graphics,
                text,
                font,
                e.Bounds,
                e.ForeColor,
                TextFormatFlags.Left);

            e.DrawFocusRectangle();

            // IMPORTANT
            // Pourquoi : éviter fuite GDI
            if (inactive)
            {
                font.Dispose();
            }
        }

    }
}
