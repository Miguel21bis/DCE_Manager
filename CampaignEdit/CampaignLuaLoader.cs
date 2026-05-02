using System.Collections.Generic;
using NLua;
using DCE_Manager.Parameters;

namespace DCE_Manager
{
    internal class CampaignLuaLoader
    {
        private CampaignLuaData _data = new CampaignLuaData();

        private static object _cachedLuaResult = null;
        //private static List<string> _playableList = new List<string>();


        private static Dictionary<string, object> _cache = new Dictionary<string, object>();

        public CampaignLuaData Load(string campaignName)
        {

            Lua lua = new Lua();

            lua["versionPackageICM"] = "NG";
            lua["pathScriptsMod"] = SharedData.textBox_SavedGames + @"\Mods\tech\DCE\ScriptsMod.NG";
            lua["pathCampaign"] = SharedData.textBox_SavedGames + @"\Mods\tech\DCE\Missions\Campaigns\" + campaignName;
            lua["generator"] = "DCE_Manager";
            lua["pathSavedGames"] = SharedData.textBox_SavedGames;

            lua["Debug"] = new Dictionary<string, object>
            {
                { "debug", false }
            };

            object[] result;

            //if (_cachedLuaResult == null)
            //{
            //    _cachedLuaResult = lua.DoFile( SharedData.textBox_SavedGames + @"\Mods\tech\DCE\ScriptsMod.NG\DCEM_Function.lua");
            //}

            //result = (object[])_cachedLuaResult;

            if (!_cache.ContainsKey(campaignName))
            {
                _cache[campaignName] = lua.DoFile(
                    SharedData.textBox_SavedGames + @"\Mods\tech\DCE\ScriptsMod.NG\DCEM_Function.lua"
                );
            }

            result = (object[])_cache[campaignName];

            LuaTable luaTable = (LuaTable)result[0];

            _data.PlayableAircraft = new HashSet<string>();
            _data.AllPlaneHeli = new HashSet<string>();
            _data.TaskByPlane = new Dictionary<string, List<string>>();
            //_data.TaskByPlane = new Dictionary<string, Dictionary<string, bool>>();
            _data.CallsignWest = new Dictionary<string, List<string>>();
            _data.SpecificCallnames = new Dictionary<string, Dictionary<string, List<string>>>();
            //_data.Country = new Dictionary<string, List<string>>();

            // -----------------------------------------------------------------
            // Playable aircraft
            // -----------------------------------------------------------------

            LuaTable playableLua = luaTable["Playable_m"] as LuaTable;

            if (playableLua != null)
            {
                foreach (object value in playableLua.Values)
                {
                    _data.PlayableAircraft.Add(value.ToString());
                }
            }

            // -----------------------------------------------------------------
            // all_PlaneHeli
            // -----------------------------------------------------------------

            LuaTable all_PlaneHeliLua = luaTable["all_PlaneHeli"] as LuaTable;

            if (all_PlaneHeliLua != null)
            {
                foreach (object value in all_PlaneHeliLua.Values)
                {
                    _data.AllPlaneHeli.Add(value.ToString());
                }
            }


            // -----------------------------------------------------------------
            // Tasks by plane
            // -----------------------------------------------------------------

             LuaTable taskByPlaneLua = luaTable["taskByPlane"] as LuaTable;

            if (taskByPlaneLua != null)
            {
                foreach (object planeKey in taskByPlaneLua.Keys)
                {
                    string planeName = planeKey.ToString();

                    LuaTable tasksLua = taskByPlaneLua[planeKey] as LuaTable;
                    if (tasksLua == null)
                        continue;

                    var tasks = new List<string>();

                    foreach (object value in tasksLua.Values)
                    {
                        tasks.Add(value.ToString());
                    }

                    _data.TaskByPlane[planeName] = tasks;
                }
            }

            // -----------------------------------------------------------------
            // CallsignWest
            // -----------------------------------------------------------------
            LuaTable callsignWestLua = luaTable["CallsignWest"] as LuaTable;

            if (callsignWestLua != null)
            {
                foreach (object key in callsignWestLua.Keys)
                {
                    string type = key.ToString();

                    LuaTable subTable = callsignWestLua[key] as LuaTable;
                    if (subTable == null)
                        continue;

                    List<string> list = new List<string>();

                    foreach (object subKey in subTable.Keys)
                    {
                        object val = subTable[subKey];
                        if (val != null)
                        {
                            list.Add(val.ToString());
                        }
                    }

                    _data.CallsignWest[type] = list;
                }
            }

            // -----------------------------------------------------------------
            // SpecificCallnames
            // -----------------------------------------------------------------

            _data.SpecificCallnames = new Dictionary<string, Dictionary<string, List<string>>>();

            LuaTable specificLua = luaTable["SpecificCallnames"] as LuaTable;

            if (specificLua != null)
            {
                foreach (object aircraftKey in specificLua.Keys)
                {
                    string aircraft = aircraftKey.ToString();

                    LuaTable countriesTable = specificLua[aircraftKey] as LuaTable;
                    if (countriesTable == null)
                        continue;

                    var countryDict = new Dictionary<string, List<string>>();

                    foreach (object countryKey in countriesTable.Keys)
                    {
                        string country = countryKey.ToString();

                        LuaTable callSignTable = countriesTable[countryKey] as LuaTable;
                        if (callSignTable == null)
                            continue;

                        List<string> list = new List<string>();

                        foreach (object subKey in callSignTable.Keys)
                        {
                            object val = callSignTable[subKey];
                            if (val != null)
                            {
                                list.Add(val.ToString());
                            }
                        }

                        countryDict[country] = list;
                    }

                    _data.SpecificCallnames[aircraft] = countryDict;
                }
            }


            // -----------------------------------------------------------------
            // tabSquad
            // -----------------------------------------------------------------

            LuaTable TabSquad = luaTable["TabSquad"] as LuaTable;

            if (TabSquad != null)
            {
                foreach (object value in TabSquad.Values)
                {
                    _data.TabSquad.Add(value.ToString());
                }
            }

            // -----------------------------------------------------------------
            // Country
            // -----------------------------------------------------------------

            LuaTable countryLua = luaTable["Country"] as LuaTable;

            if (countryLua != null)
            {
                _data.Country = new List<string>();

                foreach (object value in countryLua.Values)
                {
                    if (value != null)
                    {
                        string name = value.ToString();

                        // évite les entrées vides
                        if (!string.IsNullOrWhiteSpace(name))
                        {
                            _data.Country.Add(name);
                        }
                    }
                }
            }


            //FormUtils.LogRegister("CampaignLuaLoader.cs: PlayableAircraft.Count = " + _data.PlayableAircraft.Count);

            //FormUtils.LogRegister("CampaignLuaLoader.cs: TaskByPlane.Count = " +  _data.TaskByPlane.Count);

            return _data;
        }


    }


}
