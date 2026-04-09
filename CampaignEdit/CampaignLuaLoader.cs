using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NLua;
using DCE_Manager.Parameters;
using DCE_Manager.Utils;

namespace DCE_Manager
{
    internal class CampaignLuaLoader
    {
        //public List<string> PlayableAircraft { get; private set; } = new List<string>();

        //public List<string> AllPlaneHeli { get; private set; } = new List<string>();

        //public Dictionary<string, Dictionary<string, bool>> TaskByPlane { get; private set; }

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
            _data.TaskByPlane = new Dictionary<string, Dictionary<string, bool>>();

            // -----------------------------------------------------------------
            // Playable aircraft
            // -----------------------------------------------------------------

            

            LuaTable playableLua = luaTable["Playable_m"] as LuaTable;

            if (playableLua != null)
            {
                foreach (object key in playableLua.Keys)
                {
                    _data.PlayableAircraft.Add(key.ToString());
                }
            }

            //PlayableAircraft.Clear();

            //LuaTable playableLua = luaTable["Playable_m"] as LuaTable;

            //if (playableLua != null)
            //{
            //    foreach (object key in playableLua.Keys)
            //    {
            //        string plane = key.ToString();

            //        if (!PlayableAircraft.Contains(plane))
            //        {
            //            PlayableAircraft.Add(plane);
            //        }
            //    }
            //}


            // -----------------------------------------------------------------
            // all_PlaneHeli
            // -----------------------------------------------------------------

            LuaTable all_PlaneHeliLua = luaTable["all_PlaneHeli"] as LuaTable;

            if (all_PlaneHeliLua != null)
            {
                foreach (object key in all_PlaneHeliLua.Keys)
                {
                    _data.AllPlaneHeli.Add(key.ToString());
                }
            }

            //AllPlaneHeli.Clear();

            //LuaTable all_PlaneHeliLua = luaTable["all_PlaneHeli"] as LuaTable;

            //if (all_PlaneHeliLua != null)
            //{
            //    foreach (object key in all_PlaneHeliLua.Keys)
            //    {
            //        string plane = key.ToString();

            //        if (!AllPlaneHeli.Contains(plane))
            //        {
            //            AllPlaneHeli.Add(plane);
            //        }
            //    }
            //}

            // -----------------------------------------------------------------
            // Tasks by plane
            // -----------------------------------------------------------------

            _data.TaskByPlane = new Dictionary<string, Dictionary<string, bool>>();

            LuaTable taskByPlaneLua = luaTable["taskByPlane"] as LuaTable;

            if (taskByPlaneLua != null)
            {
                foreach (object planeKey in taskByPlaneLua.Keys)
                {
                    string planeName = planeKey.ToString();

                    LuaTable planeTasksLua = taskByPlaneLua[planeKey] as LuaTable;

                    if (planeTasksLua == null)
                        continue;

                    var tasks = new Dictionary<string, bool>();

                    foreach (object taskKey in planeTasksLua.Keys)
                    {
                        string taskName = taskKey.ToString();

                        bool enabled = false;

                        object rawValue = planeTasksLua[taskKey];

                        if (rawValue is bool)
                        {
                            enabled = (bool)rawValue;
                        }
                        else if (rawValue != null)
                        {
                            bool.TryParse(rawValue.ToString(), out enabled);
                        }

                        tasks[taskName] = enabled;
                    }

                    _data.TaskByPlane[planeName] = tasks;
                }
            }

            FormUtils.LogRegister( "CampaignLuaLoader.cs: PlayableAircraft.Count = " +
            _data.PlayableAircraft.Count);

            FormUtils.LogRegister( "CampaignLuaLoader.cs: TaskByPlane.Count = " +
                _data.TaskByPlane.Count);

            return _data;
        }

       
    }


}
