using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using DCE_Manager.Parameters;
using DCE_Manager.Utils;
using Newtonsoft.Json.Linq;

namespace DCE_Manager
{

    internal class CampaignSaver
    {

        private static readonly Dictionary<string, string> LuaPropertyNames = new Dictionary<string, string>()
        {
            { "IdSquad", "idSquad" },
            { "Squad_Inactive", "inactive" },
        };

        private static CampaignContext _campaignContext;

        //// 3. constructeur
        //public CampaignSaver()
        //{

        //    _campaignContext = new CampaignContext();

        //}

        public static void Save(string pathFile, string pathBackup, string folderName)
        {
            if (pathBackup != null && !File.Exists(pathBackup))
            {
                File.Copy(pathFile, pathBackup, true);
            }

            foreach (var squad in List_oob_air_Manager.List_oob_air)
            {
                if (squad.Player)
                    squad.Squad_Inactive = false;
            }

            WriteListClassSquadsToFile(pathFile, folderName);
        }


        //*******************NEW Write Class *******************
        public static void WriteListClassSquadsToFile(string path, string folderFile)
        {
            // Définir la liste des mots interdits
            var motInterdit = new HashSet<string>
            {
                "SideString",
                "FolderFile",
                "Helicopter",
                "DisplayName",
                "Squad_Active",
                "SideNumberMax",
                "SideNumberMin",
            };

            // Liste des caractères interdits
            char[] forbiddenChars = new char[] { ' ', '-' };

            var sb = new StringBuilder();

            sb.AppendLine("oob_air = ");
            sb.AppendLine("{");

            string accKey1 = "";
            string accKey2 = "";

            string accValue1 = "";
            string accValue2 = "";

            string arg = "";
            int nbTab = 0;

            accKey1 = "[\"";
            accKey2 = "\"]";
            accValue1 = "\"";
            accValue2 = "\"";

            // Itérer sur chaque camp
            foreach (var side in PublicTable.SideList)
            {
                sb.Append(
                 "\t" + "[\"" + side + "\"] = " + "\r\n" +
                   "\t" + "{" + "\r\n");

                if (List_oob_air_Manager.List_oob_air.Count == 0)
                {
                    MessageBox.Show("Le fichier oob_air est vide, rien à sauvegarder.", "WriteChanedSquad Error A");
                    FormUtils.LogRegister("Le fichier oob_air est vide, rien à sauvegarder.List_oob_air.Count:" + List_oob_air_Manager.List_oob_air.Count);

                    return;
                }


                var filteredSquads = List_oob_air_Manager.List_oob_air
                    .Where(squad => squad.FolderFile == folderFile && squad.SideString == side);

                // Itérer sur les squads du camp actuel
                foreach (var squad in filteredSquads)
                {

                    sb.Append("\t\t{" + "\r\n");

                    // Obtenir le type de l'objet
                    Type type = squad.GetType();

                    // Obtenir toutes les propriétés de l'objet
                    PropertyInfo[] properties = type.GetProperties();

                    // Itérer sur chaque propriété Class Normal
                    foreach (PropertyInfo property in properties)
                    {
                        // AJOUTER AU DEBUT DU foreach PropertyInfo
                        var value = property.GetValue(squad, null);

                        if (property.Name == "Livery" && value is IDictionary dictLivery)
                        {
                            if (dictLivery.Count == 0)
                                continue;

                            sb.Append("\t\t\tlivery = {\r\n");

                            foreach (DictionaryEntry entry in dictLivery)
                            {
                                sb.Append($"\t\t\t\t[{entry.Key}] = \"{entry.Value}\",\r\n");
                            }

                            sb.Append("\t\t\t},\r\n");
                            continue;
                        }

                        if (property.Name == "SideNumberMin")
                        {
                            int min = squad.SideNumberMin;
                            int max = squad.SideNumberMax;

                            // Ignore si vide
                            if (min == 0 && max == 0)
                                continue;

                            sb.Append("\t\t\tsidenumber = {\r\n");
                            sb.Append($"\t\t\t\t[1] = {min},\r\n");
                            sb.Append($"\t\t\t\t[2] = {max},\r\n");
                            sb.Append("\t\t\t},\r\n");

                            continue;
                        }

                        if (property.Name == "parking_id" && value is Dictionary<string, object> parkingDict)
                        {
                            if (parkingDict.Count == 0)
                                continue;

                            sb.Append("\t\t\tparking_id = {\r\n");

                            foreach (var entry in parkingDict)
                            {
                                if (entry.Value is IEnumerable enumerable)
                                {
                                    var values = new List<string>();

                                    foreach (var v in enumerable)
                                        values.Add(v.ToString());

                                    sb.Append($"\t\t\t\t[\"{entry.Key}\"] = {{{string.Join(",", values)}}},\r\n");
                                }
                            }

                            sb.Append("\t\t\t},\r\n");
                            continue;
                        }

                        accKey1 = "[\""; accKey2 = "\"]";
                        accValue1 = "\""; accValue2 = "\"";

                        // Obtenir la valeur de la propriété

                        // Si le nom de la propriété est dans la liste des mots interdits, passer à la propriété suivante
                        if (motInterdit.Contains(property.Name))
                        {
                            continue;
                        }
                        //if (folderFile == "Init" && (property.Name == "InitNumber" || property.Name == "InitReserve"))
                        //{
                        //    continue;
                        //}

                        // Si la propriété est un Dictionary
                        if (value is IDictionary dictionary && property.Name != "AdditionalProperties")
                        {
                            if (dictionary.Count == 0)
                                continue;


                            string dictionaryName = property.Name;
                            dictionaryName = FormUtils.ToTitleCase(dictionaryName);
                            if (dictionaryName.IndexOfAny(forbiddenChars) == -1)
                            { accKey1 = ""; accKey2 = ""; }

                            sb.Append("\t\t\t" + accKey1 + dictionaryName + accKey2 + " = " + "{" + "\r\n");

                            foreach (DictionaryEntry entry in dictionary)
                            {
                                string keyName = entry.Key.ToString();

                                // Si la clé est numérique -> [102]
                                if (int.TryParse(keyName, out _))
                                {
                                    accKey1 = "[";
                                    accKey2 = "]";
                                }
                                else
                                {
                                    accKey1 = "[\"";
                                    accKey2 = "\"]";

                                    if (keyName.IndexOfAny(forbiddenChars) == -1)
                                    {
                                        accKey1 = "";
                                        accKey2 = "";
                                    }
                                }
                                //********************

                                if (int.TryParse(entry.Value.ToString(), out int intValue))
                                {
                                    accValue1 = ""; accValue2 = "";
                                    sb.Append("\t\t\t\t" + accKey1 + keyName + accKey2 + " = " + accValue1 + intValue + accValue2 + ",\r\n");
                                }
                                else if (double.TryParse(entry.Value.ToString(), out double doubleValue))
                                {
                                    accValue1 = ""; accValue2 = "";
                                    string doubleStr = doubleValue.ToString(System.Globalization.CultureInfo.InvariantCulture);
                                    sb.Append("\t\t\t\t" + accKey1 + keyName + accKey2 + " = " + accValue1 + doubleStr + accValue2 + ",\r\n");
                                }
                                else if (bool.TryParse(entry.Value.ToString(), out bool boolValue))
                                {
                                    accValue1 = ""; accValue2 = "";
                                    sb.Append("\t\t\t\t" + accKey1 + keyName + accKey2 + " = " + accValue1 + boolValue.ToString().ToLower() + accValue2 + ",\r\n");
                                }

                                else
                                {
                                    accValue1 = "\"";
                                    accValue2 = "\"";

                                    sb.Append("\t\t\t\t" +
                                        accKey1 + keyName + accKey2 +
                                        " = " +
                                        accValue1 + entry.Value.ToString() + accValue2 +
                                        ",\r\n");
                                }

                            }
                            sb.Append("\t\t\t}," + "\r\n");

                            // Important : empêcher la réécriture du Dictionary
                            // Pourquoi : sinon fallback ToString() invalide en Lua
                            continue;
                        }

                        else if (value is List<int> intList)
                        {
                            if (intList.Count == 0)
                                continue;

                            string listName = FormUtils.ToTitleCase(property.Name);

                            sb.Append($"\t\t\t{listName} = {{");

                            sb.Append(string.Join(",", intList));

                            sb.Append("},\r\n");

                            continue;
                        }

                        // Si la propriété est une List
                        else if (value is List<string> list)
                        {
                            // AJOUTER JUSTE APRES
                            if (list.Count == 0)
                                continue;

                            string listName = property.Name;
                            listName = FormUtils.ToTitleCase(listName);
                            if (listName.IndexOfAny(forbiddenChars) == -1)
                            { accKey1 = ""; accKey2 = ""; }

                            sb.Append("\t\t\t" + accKey1 + listName + accKey2 + " = " + "{" + "\r\n");

                            int i = 1;
                            foreach (var item in list)
                            {
                                accValue1 = "\""; accValue2 = "\"";
                                accKey1 = "["; accKey2 = "]";
                                sb.Append("\t\t\t\t" + accKey1 + i.ToString() + accKey2 + " = " + accValue1 + item + accValue2 + ",\r\n");
                                i++;
                            }

                            sb.Append("\t\t\t}," + "\r\n");
                            // Important : empêcher la réécriture du ***
                            // Pourquoi : sinon fallback ToString() invalide en Lua
                            continue;
                        }

                        //**
                        // Callsign géré manuellement
                        // Pourquoi : écrire callsign + callsignId ensemble
                        if (property.Name == "Callsign")
                        {
                            string callsign = value?.ToString();

                            // Automatic = on n'écrit rien
                            if (string.IsNullOrWhiteSpace(callsign) ||
                                callsign == "Automatic")
                            {
                                continue;
                            }

                            // Recalcule l'ID Lua réel
                            // Pourquoi : éviter les désynchronisations UI/C#
                            int callsignId = CampaignLuaLoader.FindCallsignId( CampaignLuaData.Current, squad.Type, squad.Country,  callsign);

                            sb.Append("\t\t\t[\"callsign\"] = \"" + callsign + "\",\r\n");
                            sb.Append("\t\t\t[\"callsignId\"] = " + callsignId + ",\r\n");

                            continue;
                        }

                        // Empêche double écriture automatique
                        // Pourquoi : déjà écrit avec Callsign
                        if (property.Name == "CallsignId")
                        {
                            continue;
                        }
                        //**
                        else if (value != null && property.Name != "AdditionalProperties")
                        {

                            string keyName = property.Name;

                            // Garde les noms Lua exacts quand nécessaire
                            // Pourquoi : compatibilité DCE/Lua
                            if (LuaPropertyNames.ContainsKey(property.Name))
                            {
                                keyName = LuaPropertyNames[property.Name];
                            }
                            else
                            {
                                keyName = FormUtils.ToTitleCase(property.Name);
                            }

                            //keyName = FormUtils.ToTitleCase(keyName);
                            string valueName = value.ToString();

                            if (keyName.IndexOfAny(forbiddenChars) == -1)
                            { accKey1 = ""; accKey2 = ""; }


                            if (int.TryParse(value.ToString(), out int intValue))
                            {
                                accValue1 = ""; accValue2 = "";
                                sb.Append("\t\t\t" + accKey1 + keyName + accKey2 + " = " + accValue1 + intValue + accValue2 + ",\r\n");
                            }
                            else if (double.TryParse(value.ToString(), out double doubleValue))
                            {
                                accValue1 = ""; accValue2 = "";
                                string doubleStr = doubleValue.ToString(System.Globalization.CultureInfo.InvariantCulture);
                                sb.Append("\t\t\t" + accKey1 + keyName + accKey2 + " = " + accValue1 + doubleStr + accValue2 + ",\r\n");
                            }
                            else if (bool.TryParse(value.ToString(), out bool boolValue))
                            {
                                accValue1 = ""; accValue2 = "";
                                sb.Append("\t\t\t" + accKey1 + keyName + accKey2 + " = " + accValue1 + boolValue.ToString().ToLower() + accValue2 + ",\r\n");
                            }
                            else
                            {
                                sb.Append("\t\t\t" + accKey1 + keyName + accKey2 + " = " + accValue1 + valueName + accValue2 + ",\r\n");
                            }

                            // Important : empêcher la réécriture du ***
                            // Pourquoi : sinon fallback ToString() invalide en Lua
                            continue;
                        }
                    }

                    // Itérer sur chaque propriété de AdditionalProperties
                    if (squad.AdditionalProperties != null)
                    {
                        //texteFinal = texteFinal + "\t\t\t"  + " = " + "{--Y" + "\r\n";

                        foreach (var addProp in squad.AdditionalProperties)
                        {
                            accKey1 = "[\"";
                            accKey2 = "\"]";
                            accValue1 = "\"";
                            accValue2 = "\"";

                            if (addProp.Key == "liveryModex")
                                continue;

                            if (motInterdit.Contains(addProp.Key.ToString()))
                            {
                                continue;
                            }

                            //si la key n'est pas un objet/table
                            //ps: je n'arrive pas a detecter correctement que c'est un objet, donc je regarde si c'est un int string bool
                            if (
                                addProp.Value.GetType() == typeof(int) ||
                                addProp.Value.GetType() == typeof(string) ||
                                addProp.Value.GetType() == typeof(bool)
                                )
                            {
                                accKey1 = "[\""; accKey2 = "\"]";

                                string keyName = addProp.Key.ToString();
                                keyName = FormUtils.ToTitleCase(keyName);
                                string valueName = addProp.Value.ToString();

                                arg = "-addProp No Objet-";
                                nbTab = 3;
                                //texteFinal = ProcessEntries(keyName, valueName, forbiddenChars, texteFinal, nbTab, arg);
                                FormUtils.ProcessEntries(keyName, valueName, forbiddenChars, sb, nbTab, arg);

                            }
                            else
                            {
                                accKey1 = "[\""; accKey2 = "\"]";

                                string dictionaryName = addProp.Key;

                                if (dictionaryName.IndexOfAny(forbiddenChars) == -1)
                                { accKey1 = ""; accKey2 = ""; }

                                sb.Append("\t\t\t" + accKey1 + dictionaryName + accKey2 + " = " + "{" + "\r\n");

                                if (addProp.Value is Dictionary<string, object> dict)
                                {
                                    if (dict.Count == 0)
                                        continue;

                                    foreach (var dictEntry in dict)
                                    {
                                        int addA = 0;

                                        if (dict.Keys.First() == "_0" || dict.Keys.First() == "0")
                                        {
                                            addA = 1;
                                        }

                                        var keyA = dictEntry.Key;
                                        keyA = keyA.Replace("_", "");

                                        if (addA == 1 && int.TryParse(keyA.ToString(), out int intValueA))
                                        {
                                            intValueA = intValueA + addA;
                                            keyA = intValueA.ToString();
                                        }


                                        if (dictEntry.Value is Dictionary<string, LuaObject> luaDict4)
                                        {
                                            accKey1 = "[\""; accKey2 = "\"]";
                                            sb.Append("\t\t\t\t" + accKey1 + dictEntry.Key + accKey2 + " = " + "{" + "\r\n");

                                            // Obtenir la première clé de luaDict4
                                            int addB = 0;
                                            if (luaDict4.Keys.First() == "0")
                                            {
                                                addB = 1;
                                            }
                                            foreach (var entry4 in luaDict4)
                                            {
                                                var keyB = entry4.Key;
                                                if (addB == 1 && int.TryParse(entry4.Key.ToString(), out int intValue))
                                                {
                                                    intValue = intValue + addB;
                                                    keyB = intValue.ToString();
                                                }

                                                //  if (entry4.Value.luaobj == "AdA Chasse 1-2 EF")
                                                //{ }
                                                arg = "-addProp Obj luaObj 5-";
                                                nbTab = 5;
                                                //texteFinal = ProcessEntries(keyB, entry4.Value.luaobj, forbiddenChars, texteFinal, nbTab, arg);
                                                FormUtils.ProcessEntries(keyB, entry4.Value.luaobj, forbiddenChars, sb, nbTab, arg);
                                            }

                                            sb.Append("\t\t\t\t}," + "\r\n");
                                        }
                                        else
                                        {

                                            arg = "-addProp Obj else 4-";
                                            nbTab = 4;
                                            //texteFinal = ProcessEntries(keyA, dictEntry.Value, forbiddenChars, texteFinal, nbTab, arg);
                                            FormUtils.ProcessEntries(keyA, dictEntry.Value, forbiddenChars, sb, nbTab, arg);
                                        }
                                    }
                                }
                                sb.Append("\t\t\t}," + "\r\n");

                            }
                        }
                    }

                    sb.Append("\t\t}," + "\r\n");
                }
                sb.Append("\t}," + "\r\n");
            }

            sb.Append("}" + "\r\n");

            string texteFinal = sb.ToString();

            // Vérifier si la liste de squads est vide
            if (!List_oob_air_Manager.List_oob_air.Any())
            {
                MessageBox.Show("Aucune donnée détectée. Le fichier ne sera pas écrasé.", "WriteChanedSquad Error B");
                return;
            }

            // Vérifier si texteFinal contient au moins un bloc de squad
            if (!texteFinal.Contains("\t\t{"))
            {
                MessageBox.Show("Le fichier généré est vide. Écriture annulée.", "WriteChanedSquad Error C");
                return;
            }

            int nbOpen = texteFinal.Count(c => c == '{');
            int nbClose = texteFinal.Count(c => c == '}');

            FormUtils.LogRegister($"{{={nbOpen} }}={nbClose}");

            if (nbClose != nbOpen)
            {
                MessageBox.Show("Nb d'accolades {} différent.", "WriteChanedSquad Error D");
                return;
            }

            using (StreamWriter sr2 = new StreamWriter(path))
            {
                sr2.Write(texteFinal);
            }
        }

    }
}
