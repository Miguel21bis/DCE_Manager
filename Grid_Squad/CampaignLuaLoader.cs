using System.Collections.Generic;
using DCE_Manager.Parameters;
using DCE_Manager.Utils;
using NLua;
using System.Globalization;
using System.Linq;
using static DCE_Manager.Parameters.CampaignLuaData;
using System;

namespace DCE_Manager
{
    internal class CampaignLuaLoader
    {
        private CampaignLuaData _data = new CampaignLuaData();

        private static object _cachedLuaResult = null;

        private static Dictionary<string, object> _cache = new Dictionary<string, object>();

        public CampaignLuaData Load(string campaignName)
        {
            if (_cache.TryGetValue(campaignName, out object cachedData))
            {
                CampaignLuaData.Current = (CampaignLuaData)cachedData;
                return (CampaignLuaData)cachedData;
            }

            // GARDE-FOU (défense en profondeur) : si le dossier de la campagne a disparu
            // (supprimée, dossier déplacé, clonage interrompu...), on ne tente même pas le
            // DoFile Lua, qui lèverait une LuaScriptException brute difficile à gérer
            // proprement pour l'appelant. Ça protège tout appelant de Load(), pas seulement
            // le clic dans la grid (déjà filtré en amont côté Campaigns_List_Grid_Left).
            string campaignPath = ParamConf.PATH_SavedGames_DCS + @"\Mods\tech\DCE\Missions\Campaigns\" + campaignName;
            if (!System.IO.Directory.Exists(campaignPath))
            {
                FormUtils.LogRegister($"CampaignLuaLoader.Load('{campaignName}') : dossier de campagne introuvable ({campaignPath}), abandon.");
                return null;
            }

            // Vérification ciblée AVANT le DoFile : quand DCEM_Function.lua plante, le message
            // Lua ("attempt to index a nil value (field 'type')") ne dit jamais QUEL fichier est
            // en cause côté utilisateur. On vérifie nous-mêmes en amont pour remonter un message
            // clair au lieu de laisser l'erreur Lua brute remonter telle quelle.
            string initFolder = campaignPath + @"\Init\";
            string[] requiredForLuaLoad = { "conf_mod.lua", "camp_init.lua", "db_airbases.lua", "targetlist_init.lua", "oob_air_init.lua" };

            foreach (string fileName in requiredForLuaLoad)
            {
                string filePath = initFolder + fileName;

                if (!System.IO.File.Exists(filePath))
                    throw new System.IO.FileNotFoundException(
                        "Required file '" + fileName + "' is missing for campaign '" + campaignName + "'.", filePath);

                if (new System.IO.FileInfo(filePath).Length == 0)
                    throw new System.InvalidOperationException(
                        "Required file '" + fileName + "' is empty for campaign '" + campaignName + "'.");
            }


            // Un objet neuf par campagne. Avant, _data (champ d'instance) était
            // réutilisé tel quel : deux campagnes chargées par le même loader
            // finissaient dans _cache en pointant sur le MÊME objet, et TabSquad,
            // qui n'était jamais vidé, accumulait les squads de toutes les
            // campagnes ouvertes depuis le démarrage.
            _data = new CampaignLuaData();

            _data.PlayableAircraft = new HashSet<string>();
            _data.AllPlaneHeli = new HashSet<string>();
            _data.TaskByPlane = new Dictionary<string, List<string>>();
            _data.CallsignWest = new Dictionary<string, List<string>>();
            _data.SpecificCallnames = new Dictionary<string, Dictionary<string, Dictionary<string, string>>>();

            // Globales, bootstrap et compatibilité : tout est dans DcemLua.
            using (Lua lua = DcemLua.NewCampaignState(campaignName))
            {
                object[] result = lua.DoFile(DcemLua.Resolve("DCEM_Function.lua"));

                LuaTable luaTable = (LuaTable)result[0];

                // ------- tout ton bloc d'extraction existant reste identique, -------
                // ------- il est juste maintenant DANS le using, donc encore -------
                // ------- valide au moment où on lit chaque LuaTable -------

                LuaTable playableLua = luaTable["Playable_m"] as LuaTable;
                if (playableLua != null)
                {
                    foreach (object value in playableLua.Values)
                        _data.PlayableAircraft.Add(value.ToString());
                }

                LuaTable all_PlaneHeliLua = luaTable["all_PlaneHeli"] as LuaTable;
                if (all_PlaneHeliLua != null)
                {
                    foreach (object value in all_PlaneHeliLua.Values)
                        _data.AllPlaneHeli.Add(value.ToString());
                }

                LuaTable taskByPlaneLua = luaTable["taskByPlane"] as LuaTable;
                if (taskByPlaneLua != null)
                {
                    foreach (object planeKey in taskByPlaneLua.Keys)
                    {
                        string planeName = planeKey.ToString();
                        LuaTable tasksLua = taskByPlaneLua[planeKey] as LuaTable;
                        if (tasksLua == null) continue;

                        var tasks = new List<string>();
                        foreach (object value in tasksLua.Values)
                            tasks.Add(value.ToString());

                        _data.TaskByPlane[planeName] = tasks;
                    }
                }

                LuaTable callsignWestLua = luaTable["CallsignWest"] as LuaTable;
                if (callsignWestLua != null)
                {
                    foreach (object key in callsignWestLua.Keys)
                    {
                        string type = key.ToString();
                        LuaTable subTable = callsignWestLua[key] as LuaTable;
                        if (subTable == null) continue;

                        List<string> list = new List<string>();
                        foreach (object subKey in subTable.Keys)
                        {
                            object val = subTable[subKey];
                            if (val != null) list.Add(val.ToString());
                        }

                        _data.CallsignWest[type] = list;
                    }
                }

                LuaTable specificLua = luaTable["SpecificCallnames"] as LuaTable;
                if (specificLua != null)
                {
                    foreach (object aircraftKey in specificLua.Keys)
                    {
                        string aircraft = aircraftKey.ToString();
                        LuaTable countriesTable = specificLua[aircraftKey] as LuaTable;
                        if (countriesTable == null) continue;

                        var countryDict = new Dictionary<string, Dictionary<string, string>>();
                        foreach (object countryKey in countriesTable.Keys)
                        {
                            string country = countryKey.ToString();
                            LuaTable callSignTable = countriesTable[countryKey] as LuaTable;
                            if (callSignTable == null) continue;

                            Dictionary<string, string> list = new Dictionary<string, string>();
                            foreach (object subKey in callSignTable.Keys)
                            {
                                object val = callSignTable[subKey];
                                if (val != null) list[subKey.ToString()] = val.ToString();
                            }

                            countryDict[country] = list;
                        }

                        _data.SpecificCallnames[aircraft] = countryDict;
                    }
                }

                _data.RadioByModule = new Dictionary<string, List<double[]>>();
                LuaTable radioLua = luaTable["RadioRanges"] as LuaTable;
                if (radioLua != null)
                {
                    foreach (object moduleKey in radioLua.Keys)
                    {
                        LuaTable mod = radioLua[moduleKey] as LuaTable;
                        if (mod == null) continue;

                        // default = true : aucune donnée radio pour ce module, la plage 100-156 n'est qu'un repli : on ne contrôle rien
                        if (mod["default"] is bool && (bool)mod["default"]) continue;

                        LuaTable radios = mod["radios"] as LuaTable;
                        if (radios == null) continue;

                        var ranges = new List<double[]>();
                        foreach (object rk in radios.Keys)
                        {
                            LuaTable radio = radios[rk] as LuaTable;
                            LuaTable rl = radio == null ? null : radio["ranges"] as LuaTable;
                            if (rl == null) continue;

                            foreach (object ek in rl.Keys)
                            {
                                LuaTable r = rl[ek] as LuaTable;
                                if (r == null || r["min"] == null || r["max"] == null) continue;
                                ranges.Add(new double[]
                                {
                    System.Convert.ToDouble(r["min"], System.Globalization.CultureInfo.InvariantCulture),
                    System.Convert.ToDouble(r["max"], System.Globalization.CultureInfo.InvariantCulture)
                                });
                            }
                        }
                        if (ranges.Count > 0) _data.RadioByModule[moduleKey.ToString()] = ranges;
                    }
                }

                LuaTable radioInfoLua = luaTable["RadioRangesInfo"] as LuaTable;
                if (radioInfoLua != null)
                    FormUtils.LogRegister("Radio ranges (scriptsMod) | dcsRoot=" + radioInfoLua["dcsRoot"] + " (" + radioInfoLua["dcsRootSource"] + ") | modules=" + radioInfoLua["modules"] + " | withData=" + radioInfoLua["withData"] + " | missing=" + radioInfoLua["missing"] + " | seconds=" + radioInfoLua["seconds"] + " | error=" + radioInfoLua["error"]);
                else
                    FormUtils.LogRegister("Radio ranges (scriptsMod) | RadioRangesInfo missing in the DCEM_Function.lua result");

                // radios séparées par module (nom, nombre de canaux, plages) : sert aux presets radio de la carte
                _data.RadioDetail = new Dictionary<string, List<ModuleRadio>>();
                LuaTable radioAll = luaTable["RadioRanges"] as LuaTable;
                if (radioAll != null)
                {
                    foreach (object mk in radioAll.Keys)
                    {
                        LuaTable mod = radioAll[mk] as LuaTable;
                        LuaTable radios = mod == null ? null : mod["radios"] as LuaTable;
                        if (radios == null) continue;

                        bool isDefault = mod["default"] is bool && (bool)mod["default"];
                        var list = new List<ModuleRadio>();
                        foreach (object rk in radios.Keys.Cast<object>().OrderBy(k => Convert.ToDouble(k, CultureInfo.InvariantCulture)))
                        {
                            LuaTable r = radios[rk] as LuaTable;
                            LuaTable rr = r == null ? null : r["ranges"] as LuaTable;
                            if (rr == null) continue;

                            var mr = new ModuleRadio { Name = Convert.ToString(r["name"], CultureInfo.InvariantCulture), IsDefault = isDefault };
                            if (r["nbCanal"] != null) mr.NbCanal = Convert.ToInt32(r["nbCanal"], CultureInfo.InvariantCulture);
                            foreach (object gk in rr.Keys)
                            {
                                LuaTable g = rr[gk] as LuaTable;
                                if (g != null && g["min"] != null && g["max"] != null)
                                    mr.Ranges.Add(new[] { Convert.ToDouble(g["min"], CultureInfo.InvariantCulture), Convert.ToDouble(g["max"], CultureInfo.InvariantCulture) });
                            }
                            list.Add(mr);
                        }
                        if (list.Count > 0) _data.RadioDetail[Convert.ToString(mk, CultureInfo.InvariantCulture)] = list;
                    }
                }
                FormUtils.LogRegister("Radio ranges (loader) | modules kept in RadioByModule : " + _data.RadioByModule.Count + " | with detail : " + _data.RadioDetail.Count);


                LuaTable TabSquad = luaTable["TabSquad"] as LuaTable;
                if (TabSquad != null)
                {
                    foreach (object value in TabSquad.Values)
                        _data.TabSquad.Add(value.ToString());
                }

                LuaTable countryLua = luaTable["Country"] as LuaTable;
                if (countryLua != null)
                {
                    _data.Country = new List<string>();
                    foreach (object value in countryLua.Values)
                    {
                        if (value != null)
                        {
                            string name = value.ToString();
                            if (!string.IsNullOrWhiteSpace(name))
                                _data.Country.Add(name);
                        }
                    }
                }
            } // ← lua.Dispose() ici. Tout ce dont on a besoin est déjà copié dans _data (types C# purs).

            _cache[campaignName] = _data;
            CampaignLuaData.Current = _data;

            return _data;
        }

        //public CampaignLuaData Load(string campaignName)
        //{

        //    Lua lua = new Lua();

        //    lua["versionPackageICM"] = "NG";
        //    lua["pathScriptsMod"] = ParamConf.PATH_SavedGames_DCS + @"\Mods\tech\DCE\ScriptsMod.NG";
        //    lua["pathCampaign"] = ParamConf.PATH_SavedGames_DCS + @"\Mods\tech\DCE\Missions\Campaigns\" + campaignName;
        //    lua["generator"] = "DCE_Manager";
        //    lua["PATH_SavedGames_DCS"] = ParamConf.PATH_SavedGames_DCS;

        //    lua["Debug"] = new Dictionary<string, object>
        //    {
        //        { "debug", false }
        //    };

        //    object[] result;

        //    if (!_cache.ContainsKey(campaignName))
        //    {
        //        _cache[campaignName] = lua.DoFile(
        //            ParamConf.PATH_SavedGames_DCS + @"\Mods\tech\DCE\ScriptsMod.NG\DCEM_Function.lua"
        //        );
        //    }

        //    result = (object[])_cache[campaignName];

        //    LuaTable luaTable = (LuaTable)result[0];

        //    _data.PlayableAircraft = new HashSet<string>();
        //    _data.AllPlaneHeli = new HashSet<string>();
        //    _data.TaskByPlane = new Dictionary<string, List<string>>();
        //    _data.CallsignWest = new Dictionary<string, List<string>>();
        //    _data.SpecificCallnames = new Dictionary<string, Dictionary<string, Dictionary<string, string>>>();


        //    // -----------------------------------------------------------------
        //    // Playable aircraft
        //    // -----------------------------------------------------------------

        //    LuaTable playableLua = luaTable["Playable_m"] as LuaTable;

        //    if (playableLua != null)
        //    {
        //        foreach (object value in playableLua.Values)
        //        {
        //            _data.PlayableAircraft.Add(value.ToString());
        //        }
        //    }

        //    // -----------------------------------------------------------------
        //    // all_PlaneHeli
        //    // -----------------------------------------------------------------

        //    LuaTable all_PlaneHeliLua = luaTable["all_PlaneHeli"] as LuaTable;

        //    if (all_PlaneHeliLua != null)
        //    {
        //        foreach (object value in all_PlaneHeliLua.Values)
        //        {
        //            _data.AllPlaneHeli.Add(value.ToString());
        //        }
        //    }


        //    // -----------------------------------------------------------------
        //    // Tasks by plane
        //    // -----------------------------------------------------------------

        //     LuaTable taskByPlaneLua = luaTable["taskByPlane"] as LuaTable;

        //    if (taskByPlaneLua != null)
        //    {
        //        foreach (object planeKey in taskByPlaneLua.Keys)
        //        {
        //            string planeName = planeKey.ToString();

        //            LuaTable tasksLua = taskByPlaneLua[planeKey] as LuaTable;
        //            if (tasksLua == null)
        //                continue;

        //            var tasks = new List<string>();

        //            foreach (object value in tasksLua.Values)
        //            {
        //                tasks.Add(value.ToString());
        //            }

        //            _data.TaskByPlane[planeName] = tasks;
        //        }
        //    }

        //    // -----------------------------------------------------------------
        //    // CallsignWest
        //    // -----------------------------------------------------------------
        //    LuaTable callsignWestLua = luaTable["CallsignWest"] as LuaTable;

        //    if (callsignWestLua != null)
        //    {
        //        foreach (object key in callsignWestLua.Keys)
        //        {
        //            string type = key.ToString();

        //            LuaTable subTable = callsignWestLua[key] as LuaTable;
        //            if (subTable == null)
        //                continue;

        //            List<string> list = new List<string>();

        //            foreach (object subKey in subTable.Keys)
        //            {
        //                object val = subTable[subKey];
        //                if (val != null)
        //                {
        //                    list.Add(val.ToString());
        //                }
        //            }

        //            _data.CallsignWest[type] = list;
        //        }
        //    }

        //    // -----------------------------------------------------------------
        //    // SpecificCallnames
        //    // -----------------------------------------------------------------

        //    LuaTable specificLua = luaTable["SpecificCallnames"] as LuaTable;

        //    if (specificLua != null)
        //    {
        //        foreach (object aircraftKey in specificLua.Keys)
        //        {
        //            string aircraft = aircraftKey.ToString();

        //            LuaTable countriesTable = specificLua[aircraftKey] as LuaTable;
        //            if (countriesTable == null)
        //                continue;

        //            var countryDict = new Dictionary<string, Dictionary<string, string>>();

        //            foreach (object countryKey in countriesTable.Keys)
        //            {
        //                string country = countryKey.ToString();

        //                LuaTable callSignTable = countriesTable[countryKey] as LuaTable;
        //                if (callSignTable == null)
        //                    continue;

        //                Dictionary<string, string> list = new Dictionary<string, string>();

        //                 foreach (object subKey in callSignTable.Keys)
        //                {
        //                    object val = callSignTable[subKey];

        //                    if (val != null)
        //                    {
        //                        list[subKey.ToString()] = val.ToString();
        //                    }
        //                }

        //                countryDict[country] = list;
        //            }

        //            _data.SpecificCallnames[aircraft] = countryDict;
        //        }
        //    }


        //    // -----------------------------------------------------------------
        //    // tabSquad
        //    // -----------------------------------------------------------------

        //    LuaTable TabSquad = luaTable["TabSquad"] as LuaTable;

        //    if (TabSquad != null)
        //    {
        //        foreach (object value in TabSquad.Values)
        //        {
        //            _data.TabSquad.Add(value.ToString());
        //        }
        //    }

        //    // -----------------------------------------------------------------
        //    // Country
        //    // -----------------------------------------------------------------

        //    LuaTable countryLua = luaTable["Country"] as LuaTable;

        //    if (countryLua != null)
        //    {
        //        _data.Country = new List<string>();

        //        foreach (object value in countryLua.Values)
        //        {
        //            if (value != null)
        //            {
        //                string name = value.ToString();

        //                // évite les entrées vides
        //                if (!string.IsNullOrWhiteSpace(name))
        //                {
        //                    _data.Country.Add(name);
        //                }
        //            }
        //        }
        //    }


        //    // Rend les données accessibles globalement
        //    // Pourquoi : utilisées par le save des callsigns
        //    CampaignLuaData.Current = _data;

        //    return _data;
        //}

        // Retourne l'ID Lua correspondant à un callsign texte
        // Pourquoi : DCE stocke un entier dans oob_air.lua
        public static int FindCallsignId( CampaignLuaData data, string aircraft, string country, string callsign)
        {
            if (string.IsNullOrWhiteSpace(callsign))
                return 0;

            // -------------------------------------------------
            // SpecificCallnames prioritaires
            // -------------------------------------------------

            FormUtils.LogRegister(
                "FindCallsignId | aircraft=" + aircraft +
                " | country=" + country +
                " | callsign=" + callsign
            );



            if (data.SpecificCallnames.TryGetValue(aircraft, out var countryDict))
            {
                foreach (var kv in countryDict)
                {
                    FormUtils.LogRegister(
                            "SpecificCallnames country Lua = |" + kv.Key + "|"
                        );

                    string luaCountry = kv.Key?.Trim().ToLowerInvariant();
                    string squadCountry = country?.Trim().ToLowerInvariant();

                    if (luaCountry != squadCountry)
                        continue;

                    var list = kv.Value;

                    foreach (var k2 in list)
                    {
                        int luaId = int.Parse(k2.Key);
                        string luaCallsign = k2.Value;

                        FormUtils.LogRegister( "Equals? k2.Key| " + k2.Key + " |k2.Value| " + k2.Value );

                        if (string.Equals(luaCallsign, callsign,  System.StringComparison.OrdinalIgnoreCase))
                        {
                            FormUtils.LogRegister("return k2.Key| " + k2.Key + " |k2.Value| " + k2.Value);
                            return luaId;
                        }
                    }
                }
            }

            // -------------------------------------------------
            // Generic west callsigns
            // -------------------------------------------------

            if (data.CallsignWest.TryGetValue("generic", out var genericList))
            {
                for (int i = 0; i < genericList.Count; i++)
                {
                    if (genericList[i] == callsign)
                    {
                        // IDs commencent à 1
                        return i + 1;
                    }
                }
            }

            return 0;
        }

        // Retourne le texte du callsign à partir de son ID Lua
        // Pourquoi : l'UI travaille avec le texte lisible
        public static string FindCallsignName(
            CampaignLuaData data,
            string aircraft,
            string country,
            int callsignId)
        {
            // -------------------------------------------------
            // SpecificCallnames
            // -------------------------------------------------

            if (callsignId >= 9)
            {
                if (data.SpecificCallnames.TryGetValue(aircraft, out var countryDict))
                {
                    if (countryDict.TryGetValue(country, out var list))
                    {
                        string key = callsignId.ToString();

                        if (list.ContainsKey(key))
                        {
                            return list[key];
                        }
                    }
                }
            }

            // -------------------------------------------------
            // Generic
            // -------------------------------------------------

            if (callsignId >= 1)
            {
                if (data.CallsignWest.TryGetValue("generic", out var genericList))
                {
                    int index = callsignId - 1;

                    if (index >= 0 && index < genericList.Count)
                    {
                        return genericList[index];
                    }
                }
            }

            return "Automatic";
        }

    }

}
