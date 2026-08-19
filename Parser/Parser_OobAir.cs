using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using DCE_Manager.Parameters;
using DCE_Manager.Utils;
//using NLua;
using static DCE_Manager.Utils.FormUtils;

namespace DCE_Manager
{
    internal class Parser_OobAir
    {
        private static readonly HashSet<string> ignoredKeys = new HashSet<string>
        {
            "displayReady",
            "displayReserve",
            "isActive",
            "taskscoefpourcent",
            "tasksCoefPourcent",
            "side",
            "helicopter"
        };

        public List<CampaignSquad> LoadCampaignSquads(string campaignName)
        {
            // Ancienne liste conservée pour compatibilité temporaire.
            List_oob_air_Manager.List_oob_air = new List<Squad>();

            // Nouvelle liste logique.
            List_oob_air_Manager.List_campaignSquads = new List<CampaignSquad>();

            LoadFile(campaignName, "Init");
            LoadFile(campaignName, "Active");

            FixSquadIds();

            DetectDuplicateNames(campaignName);

            return List_oob_air_Manager.List_campaignSquads;
        }

        // Assigne des IdSquad uniques et compacts.
        // Pourquoi : garantir des IDs cohérents par campagne.
        private void FixSquadIds()
        {
            var usedIds = new HashSet<int>();

            // 1) Récupère tous les IDs existants valides
            foreach (var squad in List_oob_air_Manager.List_oob_air)
            {
                if (squad.IdSquad > 0)
                {
                    usedIds.Add(squad.IdSquad);
                }
            }

            // 2) Trouve les squads sans ID
            foreach (var squad in List_oob_air_Manager.List_oob_air)
            {
                if (squad.IdSquad > 0)
                    continue;

                int newId = 1;

                while (usedIds.Contains(newId))
                {
                    newId++;
                }

                squad.IdSquad = newId;

                usedIds.Add(newId);

            }
        }

        private void LoadFile(string campaignName, string folderName)
        {
           var time_ParseOobAir = Stopwatch.StartNew();

            //on compte tous les squads differement, init ou active, sinon ça fout le bordel
            //int idSquad = -1;

            //*************************************************************************
            //PARSE LEs FICHIERs ******************************************************
            //oob_air_init et
            //oob_air
            //*************************************************************************

            string pathFileB;

            if (folderName == "Init")
            {
                pathFileB = ParamConf.PATH_SavedGames_DCS + @"\Mods\tech\DCE\Missions\Campaigns\" +
                    campaignName +  @"\Init\oob_air_init.lua";
            }
            else
            {
                pathFileB = ParamConf.PATH_SavedGames_DCS +  @"\Mods\tech\DCE\Missions\Campaigns\" +
                    campaignName +  @"\Active\oob_air.lua";
            }

            if (File.Exists(pathFileB))
            {
                ParamCampaign.NameFileParse = pathFileB;

                var luaObj = LuaParser.ParseFile(pathFileB, "oob_air");

                //temporaire ?
                if (luaObj == null)
                {
                    return;
                }

                if (luaObj.luaobj == null)
                {
                    return;
                }

                var root = luaObj.luaobj as Dictionary<string, LuaObject>;
                if (root == null)
                {
                    FormUtils.LogRegister("OobAirParser.cs:LoadFile(): root == null return ");
                    return;
                }


                foreach (var entry in root) // side
                {
                    string side = entry.Key;
                    int sideInt = side == "blue" ? 1 : 2;

                    var level1 = entry.Value.luaobj as Dictionary<string, LuaObject>;
                    if (level1 == null) continue;

                    foreach (var entry2 in level1) // squad
                    {

                        var squad = new Squad
                        {
                            SideString = side,
                            FolderFile = folderName,

                        };


                        //// REF: OobAirParser - assign squad_id si absent
                        //if (!root.ContainsKey("IdSquad"))
                        //{
                        //    squad.IdSquad = GetNextSquadId();
                        //}
                        //else
                        //{
                        //    // Correction : on s'assure que root["squad_id"].luaobj est bien un int ou string
                        //    object squadIdObj = root["IdSquad"].luaobj;
                        //    if (squadIdObj is int intVal)
                        //    {
                        //        squad.IdSquad = intVal;
                        //    }
                        //    else if (squadIdObj is string strVal)
                        //    {
                        //        squad.IdSquad = Convert.ToInt32(strVal, CultureInfo.InvariantCulture);
                        //    }
                        //    else
                        //    {
                        //        // fallback sécurisé
                        //        squad.IdSquad = GetNextSquadId();
                        //    }
                        //}

                        var level2 = entry2.Value.luaobj as Dictionary<string, LuaObject>;
                        if (level2 == null) continue;

                        foreach (var entry3 in level2) // propriétés squad
                        {

                            //var key = entry3.Key?.Trim();
                            //À faire seulement si tu es sûr que tes clés n'ont pas d'espaces parasites.
                            var rawKey = entry3.Key;
                            if (rawKey == null) continue;

                            if (ignoredKeys.Contains(rawKey))
                                continue;

                            var key = rawKey; // ou Trim si vraiment nécessaire

                            var valObj = entry3.Value.luaobj;

                            //ne pas enregistrer ces key:
                            //displayReady - displayReserve - isActive
                            // SWITCH = beaucoup + rapide que 30 if
                            switch (key)
                            {
                                case "name":
                                    squad.Name = valObj?.ToString();
                                    squad.DisplayName = squad.Name; // valeur par défaut
                                    
                                        break;
                                case "idSquad":
                                    squad.IdSquad = Convert.ToInt32(valObj);
                                    break;

                                case "player": squad.Player = Convert.ToBoolean(valObj); break;
                                    
                                case "humainOnly": squad.HumainOnly = Convert.ToBoolean(valObj); break;
                                case "type": squad.Type = valObj?.ToString(); break;
                                case "country": squad.Country = valObj?.ToString(); break;
                                //case "country": squad.Country = valObj?.ToString(); break;
                                case "skill": squad.Skill = valObj?.ToString(); break;

                                case "callsign": squad.Callsign = valObj?.ToString(); break;
                                case "callsignId": squad.CallsignId = Convert.ToInt32(valObj); break;



                                case "base": squad.Base = valObj?.ToString(); break;

                                case "baseAlternative":
                                    if (valObj is Dictionary<string, LuaObject> baseAlt)
                                    {
                                        squad.BaseAlternative = baseAlt
                                            .Where(e => int.TryParse(e.Key, out _))
                                            .OrderBy(e => int.Parse(e.Key))
                                            .Select(e => e.Value.luaobj.ToString().Trim('"'))
                                            .ToList();
                                    }
                                    break;

                                case "number": squad.Number = Convert.ToInt32(valObj); break;
                                case "InitNumber": squad.InitNumber = Convert.ToInt32(valObj); break;
                                case "reserve": squad.Reserve = Convert.ToInt32(valObj); break;
                                case "InitReserve": squad.InitReserve = Convert.ToInt32(valObj); break;

                                case "tasks":
                                    if (valObj is Dictionary<string, LuaObject> tasks)
                                    {
                                        //squad.Tasks = new Dictionary<string, object>(tasks.Count);
                                        squad.Tasks = new Dictionary<string, bool>(tasks.Count);
                                        foreach (var e in tasks)
                                            squad.Tasks[e.Key] = Convert.ToBoolean(e.Value.luaobj);
                                    }
                                    break;
                                case "tasksCoef":
                                    if (valObj is Dictionary<string, LuaObject> tasksCoef)
                                    {
                                        squad.TasksCoef = new Dictionary<string, double>(tasksCoef.Count);

                                        foreach (var e in tasksCoef)
                                        {
                                            object raw = e.Value.luaobj;
                                            double v;

                                            // Conversion directe, sans passer par ToString()
                                            try
                                            {
                                                v = Convert.ToDouble(raw, CultureInfo.InvariantCulture);
                                            }
                                            catch
                                            {
                                                // fallback si jamais c'est une string bizarre
                                                double.TryParse(raw?.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out v);
                                            }

                                            squad.TasksCoef[e.Key] = v;
                                        }
                                    }
                                    break;

                                case "sidenumber":
                                    var dict = valObj as Dictionary<string, LuaObject>;

                                    if (dict != null)
                                    {
                                        // Lua [1]
                                        if (dict.ContainsKey("1"))
                                        {
                                            int.TryParse(
                                                dict["1"].luaobj.ToString(),
                                                out int min
                                            );

                                            squad.SideNumberMin = min;
                                        }

                                        // Lua [2]
                                        if (dict.ContainsKey("2"))
                                        {
                                            int.TryParse(
                                                dict["2"].luaobj.ToString(),
                                                out int max
                                            );

                                            squad.SideNumberMax = max;
                                        }
                                    }

                                    break;

                                case "livery":
                                    // Toujours convertir en Dictionary<int,string>
                                    // Pourquoi : uniformiser le modèle et éviter les cas spéciaux partout

                                    squad.Livery = new Dictionary<int, string>();

                                    // Cas 1 : table Lua
                                    if (valObj is Dictionary<string, LuaObject> liveryDict)
                                    {
                                        foreach (var e in liveryDict)
                                        {
                                            if (int.TryParse(e.Key, out int index))
                                            {
                                                squad.Livery[index] = e.Value.luaobj?.ToString();
                                            }
                                        }
                                    }
                                    // Cas 2 : string simple
                                    else if (valObj != null)
                                    {
                                        squad.Livery[1] = valObj.ToString();
                                    }

                                    break;


                                case "liveryModex":
                                    // Toujours convertir en Dictionary<int,string>
                                    // Pourquoi : uniformiser le modèle et éviter les cas spéciaux partout

                                    squad.LiveryModex = new Dictionary<int, string>();

                                    // Cas 1 : table Lua
                                    if (valObj is Dictionary<string, LuaObject> modexDict)
                                    {
                                        foreach (var e in modexDict)
                                        {
                                            if (int.TryParse(e.Key, out int index))
                                            {
                                                squad.LiveryModex[index] = e.Value.luaobj?.ToString();
                                            }
                                        }
                                    }
                                    // Cas 2 : string simple
                                    else if (valObj != null)
                                    {
                                        squad.LiveryModex[1] = valObj.ToString();
                                    }

                                    break;

                                case "parking_id":
                                    if (valObj is Dictionary<string, LuaObject> parkingDict)
                                    {
                                        squad.parking_id = new Dictionary<string, object>();

                                        foreach (var e in parkingDict)
                                        {
                                            // conversion du sous-niveau
                                            if (e.Value.luaobj is Dictionary<string, LuaObject> subDict)
                                            {
                                                var list = new List<int>();

                                                 foreach (var _sub in subDict)
                                                {
                                                    if (int.TryParse(_sub.Value.luaobj.ToString(), out int v))
                                                    {
                                                        list.Add(v);
                                                     }

                                                }

                                                squad.parking_id[e.Key] = list;

                                            }
                                        }
                                    }
                                    break;


                                case "inactive":
                                    squad.Squad_Inactive = Convert.ToBoolean(valObj);
                                    break;

                                case "roster":
                                    if (valObj is Dictionary<string, LuaObject> rosterDict)
                                    {
                                        squad.Roster = new Dictionary<string, object>();

                                        foreach (var rosterEntry in rosterDict)
                                        {
                                            object rosterValue = rosterEntry.Value != null
                                                ? rosterEntry.Value.luaobj
                                                : null;

                                            if (rosterValue is LuaObject luaRoster)
                                                rosterValue = luaRoster.luaobj;

                                            int intValue;
                                            if (rosterValue != null &&
                                                int.TryParse(rosterValue.ToString(), out intValue))
                                            {
                                                squad.Roster[rosterEntry.Key] = intValue;
                                            }
                                            else
                                            {
                                                squad.Roster[rosterEntry.Key] = rosterValue;
                                            }
                                        }
                                    }
                                    else
                                    {
                                        FormUtils.LogRegister("Roster introuvable ou mauvais type pour squad.Name " + squad.Name +
                                            " ; type = " + (valObj != null ? valObj.GetType().ToString() : "null"));
                                    }
                                    break;

                                case "score":
                                    if (valObj is Dictionary<string, LuaObject> scoreDict)
                                    {
                                        squad.Score = new Dictionary<string, object>();

                                        foreach (var scoreEntry in scoreDict)
                                        {
                                            object scoreValue = scoreEntry.Value != null
                                                ? scoreEntry.Value.luaobj
                                                : null;

                                            if (scoreValue is LuaObject luaScore)
                                                scoreValue = luaScore.luaobj;

                                            int intValue;
                                            if (scoreValue != null &&
                                                int.TryParse(scoreValue.ToString(), out intValue))
                                            {
                                                squad.Score[scoreEntry.Key] = intValue;
                                            }
                                            else
                                            {
                                                squad.Score[scoreEntry.Key] = scoreValue;
                                            }
                                        }

                                    }
                                    break;


                                default:
                                    //  ultra important : éviter GetType() (lent)

                                    if (valObj is Dictionary<string, LuaObject> sub)
                                    {
                                         var dictDef = new Dictionary<string, object>(sub.Count);

                                        foreach (var e in sub)
                                        {
                                            dictDef[e.Key] = ConvertLuaValue(e.Value);
                                        }

                                        squad.AdditionalProperties[key.Trim()] = dictDef;

                                    }
                                    else
                                    {
                                        squad.AdditionalProperties[key.Trim()] = valObj;
                                    }
                                    break;
                            }
                        }

                        string squadNameKey = squad.Name != null
                        ? squad.Name.Trim().ToLowerInvariant()
                        : "";

                        string squadKey = side.Trim().ToLowerInvariant() + "|" + squadNameKey;

                        CampaignSquad campaignSquad = List_oob_air_Manager.List_campaignSquads.FirstOrDefault(x => x.Key == squadKey);

                        if (campaignSquad == null)
                        {
                            campaignSquad = new CampaignSquad
                            {
                                Key = squadKey,
                                SideString = side
                            };

                            List_oob_air_Manager.List_campaignSquads.Add(campaignSquad);
                        }

                        if (folderName == "Init")
                        {
                            if (campaignSquad.Init != null)
                            {
                                FormUtils.LogRegister(" INIT ECRASÉ: " + squadKey);
                            }

                            campaignSquad.Init = squad;
                        }
                        else
                        {
                            if (campaignSquad.Active != null)
                            {
                                FormUtils.LogRegister(" ACTIVE ECRASÉ: " + squadKey);
                            }

                            campaignSquad.Active = squad;
                        }

                        if (folderName == "Init")
                        {
                            campaignSquad.Init = squad;
                        }
                        else
                        {
                            campaignSquad.Active = squad;
                         }


                        // Ancienne liste conservée pour compatibilité temporaire.
                        List_oob_air_Manager.List_oob_air.Add(squad);



                    }
                }

            }// fileExist()
            
            time_ParseOobAir.Stop();
            //LogRegister($"Time time_ParseOobAir: {time_ParseOobAir.ElapsedMilliseconds} ms");

            var time_UI = Stopwatch.StartNew();

        }


        // Recherche FIABLE basée sur clé unique
        // Pourquoi : éviter collisions et écrasements silencieux
        public static CampaignSquad FindCampaignSquad(string key)
        {
            if (List_oob_air_Manager.List_campaignSquads == null)
                return null;

            return List_oob_air_Manager.List_campaignSquads
                .FirstOrDefault(campaignSquad =>
                    campaignSquad != null &&
                    campaignSquad.Key == key);
        }

        // Convertit récursivement les LuaObject
        // Pourquoi : gérer les tables imbriquées (cas parking_id, etc.)
        private object ConvertLuaValue(object value)
        {
            if (value is LuaObject luaObj)
                value = luaObj.luaobj;

            // dictionnaire
            if (value is Dictionary<string, LuaObject> dict)
            {
                var result = new Dictionary<string, object>();

                foreach (var kv in dict)
                {
                    result[kv.Key] = ConvertLuaValue(kv.Value);
                }

                return result;
            }

            // liste (table indexée)
            if (value is IEnumerable<object> list)
            {
                return list.Select(v => ConvertLuaValue(v)).ToList();
            }

            return value;
        }

        public static void ShowClassAndProperty(object obj)
        {
            ShowObject(obj, 0);
        }

        private static void ShowObject(object obj, int indent)
        {
            if (obj == null)
            {
                //Log(indent, "null");
                return;
            }

            Type type = obj.GetType();
            //Log(indent, "Type: " + type.Name);

            // Types simples
            if (IsSimple(type))
            {
                //Log(indent, obj.ToString());
                return;
            }

            // Listes / IEnumerable
            if (obj is System.Collections.IEnumerable enumerable && !(obj is string))
            {
                //Log(indent, "(IEnumerable)");
                foreach (var item in enumerable)
                {
                    ShowObject(item, indent + 2);
                }
                return;
            }

            // Objets complexes → propriétés
            var props = type.GetProperties();
            foreach (var prop in props)
            {
                object value = null;
                try { value = prop.GetValue(obj); }
                catch { }

                //Log(indent, prop.Name + " = " + (value == null ? "null" : ""));

                if (value != null)
                    ShowObject(value, indent + 2);
            }
        }

        private static bool IsSimple(Type type)
        {
            return type.IsPrimitive
                || type.IsEnum
                || type == typeof(string)
                || type == typeof(decimal)
                || type == typeof(DateTime)
                || type == typeof(Guid);
        }

        //private static void Log(int indent, string msg)
        //{
        //    FormUtils.LogRegister(new string(' ', indent) + msg);
        //}

        // Détecte les doublons de noms de squad et alerte
        // Pourquoi : informer sans casser le parsing ni la logique
        private void DetectDuplicateNames(string campaignName)
        {
            var duplicates = List_oob_air_Manager.List_oob_air
                .GroupBy(s => (s.SideString + "|" + s.Name + "|" + s.FolderFile).ToLowerInvariant())
                .Where(g => g.Count() > 1);

            List<string> messages = new List<string>();

            foreach (var group in duplicates)
            {
                var squads = group.ToList();

                // Marquage UI
                foreach (var s in squads)
                {
                    s.DisplayName = "DOUBLON_" + s.Name;
                }

                // Message détaillé
                string detail = string.Join(" | ",
                    squads.Select(s =>
                        $"{s.Name} ({s.FolderFile} / base={s.Base} / type={s.Type})"
                    )
                );

                messages.Add(detail);

                FormUtils.LogRegister(" DOUBLON DETECTE: " + detail);
            }

            // Popup UNIQUE
            if (messages.Count > 0)
            {
                string msg =
                    "ATTENTION : doublons détectés dans la campagne '" + campaignName + "'\n\n" +
                    string.Join("\n", messages) +
                    "\n\nLes données peuvent être incohérentes.";

                MessageBox.Show(msg, "Doublons détectés", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

    }
}
