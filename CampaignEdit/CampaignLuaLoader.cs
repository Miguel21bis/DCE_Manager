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

            if (_cachedLuaResult == null)
            {
                _cachedLuaResult = lua.DoFile( SharedData.textBox_SavedGames + @"\Mods\tech\DCE\ScriptsMod.NG\DCEM_Function.lua");
            }

            result = (object[])_cachedLuaResult;

            LuaTable luaTable = (LuaTable)result[0];

            _data.PlayableAircraft = new HashSet<string>();
            _data.AllPlaneHeli = new HashSet<string>();
            //_data.TaskByPlane = new Dictionary<string, Dictionary<string, bool>>();
            _data.TaskByPlane = new Dictionary<string, List<string>>();

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

          
            //FormUtils.LogRegister("CampaignLuaLoader.cs: PlayableAircraft.Count = " + _data.PlayableAircraft.Count);

            //FormUtils.LogRegister("CampaignLuaLoader.cs: TaskByPlane.Count = " +  _data.TaskByPlane.Count);

            return _data;
        }


    }


}
