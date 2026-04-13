using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Windows.Input;
using DCE_Manager.Parameters;
using DCE_Manager.Utils;
using static DCE_Manager.Utils.FormUtils;

namespace DCE_Manager
{
    internal class OobAirParser
    {
        public List<CampaignSquad> LoadCampaignSquads(string campaignName)
        {
            // Ancienne liste conservée pour compatibilité temporaire.
            List_oob_air_Manager.List_oob_air = new List<Squad>();

            // Nouvelle liste logique.
            List_oob_air_Manager.List_campaignSquads = new List<CampaignSquad>();

            LoadFile(campaignName, "Init");
            LoadFile(campaignName, "Active");

            return List_oob_air_Manager.List_campaignSquads;
        }

        private void LoadFile(string campaignName, string folderName)
        {
           var time_ParseOobAir = Stopwatch.StartNew();

            //on compte tous les squads differement, init ou active, sinon ça fout le bordel
            int idSquad = -1;




            //*************************************************************************
            //PARSE LEs FICHIERs ******************************************************
            //oob_air_init et
            //oob_air
            //*************************************************************************


            string pathFileB;

            if (folderName == "Init")
            {
                pathFileB = SharedData.textBox_SavedGames + @"\Mods\tech\DCE\Missions\Campaigns\" +
                    campaignName +  @"\Init\oob_air_init.lua";
            }
            else
            {
                pathFileB = SharedData.textBox_SavedGames +  @"\Mods\tech\DCE\Missions\Campaigns\" +
                    campaignName +  @"\Active\oob_air.lua";
            }

            if (File.Exists(pathFileB))
            {
                ParamCampaign.NameFileParse = pathFileB;

                FormUtils.LogRegister("OobAirParser.cs:LoadFile(): pathFileB: " + pathFileB);

                var luaObj = LuaParser.ParseFile(pathFileB, "oob_air");

                //temporaire ?
                if (luaObj == null)
                {
                    FormUtils.LogRegister("luaObj == null");
                    return;
                }

                if (luaObj.luaobj == null)
                {
                    FormUtils.LogRegister("luaObj.luaobj == null");
                    return;
                }

                var root = luaObj.luaobj as Dictionary<string, LuaObject>;
                if (root == null)
                {
                    FormUtils.LogRegister("OobAirParser.cs:LoadFile(): root == null return ");
                    MessageBox.Show("root == null return ", "OobAirParser");
                    return;
                }

                FormUtils.LogRegister("START Parser campaignName " + campaignName);


                foreach (var entry in root) // side
                {
                    //FormUtils.LogRegister("OobAirParser.cs: B foreach  (var entry in root)// side ");
                    string side = entry.Key;
                    int sideInt = side == "blue" ? 1 : 2;

                    var level1 = entry.Value.luaobj as Dictionary<string, LuaObject>;
                    if (level1 == null) continue;

                    foreach (var entry2 in level1) // squad
                    {
                        //FormUtils.LogRegister("OobAirParser.cs: C foreach  (foreach (var entry2 in level1) // squad) ");
                        idSquad++;

                        FormUtils.LogRegister("new Squad: " + idSquad );

                        var squad = new Squad
                        {
                            SideString = side,
                            IdSquad = idSquad,
                            FolderFile = folderName,

                        };
                        

                        var level2 = entry2.Value.luaobj as Dictionary<string, LuaObject>;
                        if (level2 == null) continue;

                        foreach (var entry3 in level2) // propriétés squad
                        {
                            
                            //FormUtils.LogRegister("OobAirParser.cs: D foreach   foreach (var entry3 in level2) // propriétés squad ");
                            //var key = entry3.Key;
                            var key = entry3.Key?.Trim().ToLowerInvariant();
                            var valObj = entry3.Value.luaobj;

                            //FormUtils.LogRegister("KEY DETECTED: [" + key + "]"); // 👈 AJOUT

                            // ⚡ SWITCH = beaucoup + rapide que 30 if
                            switch (key)
                            {
                                case "name":
                                    squad.Name = valObj?.ToString();

                                    FormUtils.LogRegister("Found squad Name: [" + squad.Name + "]");

                                    if (squad.Name == "469th TFS")
                                    { }
                                    break;
                                case "player": squad.Player = Convert.ToBoolean(valObj); break;
                                case "type": squad.Type = valObj?.ToString(); break;
                                case "country": squad.Country = valObj?.ToString(); break;
                                case "skill": squad.Skill = valObj?.ToString(); break;
                                case "base": squad.Base = valObj?.ToString(); break;

                                case "baseAlternative":
                                    if (valObj is Dictionary<string, LuaObject> baseAlt)
                                    {
                                        squad.BaseAlternative = new List<string>(baseAlt.Count);
                                        foreach (var e in baseAlt)
                                            squad.BaseAlternative.Add(e.Value.luaobj.ToString().Trim('"'));
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
                                        //squad.TasksCoef = new Dictionary<string, object>(tasksCoef.Count);
                                        squad.TasksCoef = new Dictionary<string, double>(tasksCoef.Count);
                                        foreach (var e in tasksCoef)
                                        {
                                            if (double.TryParse(e.Value.luaobj.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out double v))
                                                squad.TasksCoef[e.Key] = v;
                                        }
                                    }
                                    break;

                                case "inactive":
                                    squad.Inactive = Convert.ToBoolean(valObj);
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

                                        //FormUtils.LogRegister("Roster chargé pour squad.Name " + squad.Name +
                                        //    " : " + squad.Roster.Count + " éléments");
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

                                        //FormUtils.LogRegister(
                                        //    "Score chargé pour squad " + squad.Name +
                                        //    " : " + squad.Score.Count + " éléments");
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

                                                //FormUtils.LogRegister("CASE parking_id D");

                                                foreach (var _sub in subDict)
                                                {
                                                    //FormUtils.LogRegister("CASE parking_id E");
                                                    if (int.TryParse(_sub.Value.luaobj.ToString(), out int v))
                                                    {
                                                        list.Add(v);
                                                        //FormUtils.LogRegister("CASE parking_id F");
                                                    }

                                                }

                                                squad.parking_id[e.Key] = list;
                                                //FormUtils.LogRegister($"CASE parking_id G {e.Key}: [{string.Join(", ", list)}]");

                                                //var dumpA = string.Join(" | ",
                                                //        squad.parking_id.Select(kv =>
                                                //            $"{kv.Key}: [{string.Join(", ", (kv.Value as IEnumerable<int>) ?? new List<int>())}]"
                                                //        )
                                                //    );

                                                //FormUtils.LogRegister("AA_parking_id  = " + dumpA);

                                            }
                                        }
                                    }
                                    break;

                                //case "parking_id":
                                //    squad.parking_id = ConvertLuaValue(valObj) as Dictionary<string, object>;
                                //    break;

                                default:
                                    // ⚡ ultra important : éviter GetType() (lent)

                                    if (valObj is Dictionary<string, LuaObject> sub)
                                    {
                                        //var dict = new Dictionary<string, object>(sub.Count);
                                        //foreach (var e in sub)
                                        //    dict[e.Key] = e.Value.luaobj;

                                        var dict = new Dictionary<string, object>(sub.Count);

                                        foreach (var e in sub)
                                        {
                                            dict[e.Key] = ConvertLuaValue(e.Value);
                                        }

                                        squad.AdditionalProperties[key.Trim().ToLowerInvariant()] = dict;

                                        //squad.AdditionalProperties[key] = dict;
                                    }
                                    else
                                    {
                                        //squad.AdditionalProperties[key] = valObj;
                                        squad.AdditionalProperties[key.Trim().ToLowerInvariant()] = valObj;
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
                                FormUtils.LogRegister("⚠️ INIT ECRASÉ: " + squadKey);
                            }

                            campaignSquad.Init = squad;
                        }
                        else
                        {
                            if (campaignSquad.Active != null)
                            {
                                FormUtils.LogRegister("⚠️ ACTIVE ECRASÉ: " + squadKey);
                            }

                            campaignSquad.Active = squad;
                        }

                        if (folderName == "Init")
                        {
                            campaignSquad.Init = squad;
                            //FormUtils.LogRegister("OobAirParser.cs:LoadCampaignSquads(): campaignSquad.Init = squad ");
                        }
                        else
                        {
                            campaignSquad.Active = squad;
                            //FormUtils.LogRegister("OobAirParser.cs:LoadCampaignSquads(): campaignSquad.Active = squad ");
                        }


                        // Ancienne liste conservée pour compatibilité temporaire.
                        List_oob_air_Manager.List_oob_air.Add(squad);

                        LogRegister("squad.Name |" + squad.Name+"|");
                        LogRegister("squadNameKey |" + squadNameKey + "|");

                        if (squad.Name == "73 TFS")
                        {
                            LogRegister("ShowClassAndProperty START " + squad.Name);

                            ShowClassAndProperty(squad);

                            LogRegister("ShowClassAndProperty END " + squad.Name);

                        }



                        //FormUtils.LogRegister("OobAirParser.cs:LoadCampaignSquads(): List_oob_air.Count: " + List_oob_air_Manager.List_oob_air.Count);
                    }
                }

            }// fileExist()
            
            time_ParseOobAir.Stop();
            LogRegister($"Time time_ParseOobAir: {time_ParseOobAir.ElapsedMilliseconds} ms");

            var time_UI = Stopwatch.StartNew();

        }

        // Recherche un squad logique à partir du nom + camp.
        // Pourquoi : retrouver facilement Init et Active du même squad.
        //public static CampaignSquad FindCampaignSquad(string side, string squadName)
        //{
        //    if (List_oob_air_Manager.List_campaignSquads == null)
        //        return null;

        //    return List_oob_air_Manager.List_campaignSquads.FirstOrDefault(campaignSquad =>
        //        campaignSquad != null &&
        //        campaignSquad.SideString == side &&
        //        (
        //            (campaignSquad.Init != null && campaignSquad.Init.Name == squadName) ||
        //            (campaignSquad.Active != null && campaignSquad.Active.Name == squadName)
        //        ));
        //}

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
                Log(indent, "null");
                return;
            }

            Type type = obj.GetType();
            Log(indent, "Type: " + type.Name);

            // Types simples
            if (IsSimple(type))
            {
                Log(indent, obj.ToString());
                return;
            }

            // Listes / IEnumerable
            if (obj is System.Collections.IEnumerable enumerable && !(obj is string))
            {
                Log(indent, "(IEnumerable)");
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

                Log(indent, prop.Name + " = " + (value == null ? "null" : ""));

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

        private static void Log(int indent, string msg)
        {
            FormUtils.LogRegister(new string(' ', indent) + msg);
        }

    }
}
