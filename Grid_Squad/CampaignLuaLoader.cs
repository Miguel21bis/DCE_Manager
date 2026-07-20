using System.Collections.Generic;
using DCE_Manager.Parameters;
using DCE_Manager.Utils;
using NLua;

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

            _data.PlayableAircraft = new HashSet<string>();
            _data.AllPlaneHeli = new HashSet<string>();
            _data.TaskByPlane = new Dictionary<string, List<string>>();
            _data.CallsignWest = new Dictionary<string, List<string>>();
            _data.SpecificCallnames = new Dictionary<string, Dictionary<string, Dictionary<string, string>>>();

            using (Lua lua = new Lua())
            {
                lua["versionPackageICM"] = "NG";
                lua["pathScriptsMod"] = ParamConf.PATH_SavedGames_DCS + @"\Mods\tech\DCE\ScriptsMod.NG";
                lua["pathCampaign"] = ParamConf.PATH_SavedGames_DCS + @"\Mods\tech\DCE\Missions\Campaigns\" + campaignName;
                lua["generator"] = "DCE_Manager";
                lua["PATH_SavedGames_DCS"] = ParamConf.PATH_SavedGames_DCS;
                lua["Debug"] = new Dictionary<string, object> { { "debug", false } };

                object[] result = lua.DoFile(
                    ParamConf.PATH_SavedGames_DCS + @"\Mods\tech\DCE\ScriptsMod.NG\DCEM_Function.lua"
                );

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
