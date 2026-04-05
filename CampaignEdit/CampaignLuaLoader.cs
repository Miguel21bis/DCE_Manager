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
        public List<string> PlayableAircraft { get; private set; } = new List<string>();

        public Dictionary<string, Dictionary<string, bool>> TaskByPlane { get; private set; }

        private static object _cachedLuaResult = null;
        private static List<string> _playableList = new List<string>();

        public void Load(string campaignName)
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
                _cachedLuaResult = lua.DoFile(
                    SharedData.textBox_SavedGames +
                    @"\Mods\tech\DCE\ScriptsMod.NG\DCEM_Function.lua");
            }

            result = (object[])_cachedLuaResult;

            LuaTable luaTable = (LuaTable)result[0];

            // -----------------------------------------------------------------
            // Playable aircraft
            // -----------------------------------------------------------------

            PlayableAircraft.Clear();

            LuaTable playableLua = luaTable["Playable_m"] as LuaTable;

            if (playableLua != null)
            {
                foreach (object key in playableLua.Keys)
                {
                    string plane = key.ToString();

                    if (!PlayableAircraft.Contains(plane))
                    {
                        PlayableAircraft.Add(plane);
                    }
                }
            }

            // -----------------------------------------------------------------
            // Tasks by plane
            // -----------------------------------------------------------------

            TaskByPlane = new Dictionary<string, Dictionary<string, bool>>();

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

                    TaskByPlane[planeName] = tasks;
                }
            }

            FormUtils.LogRegister( "CampaignLuaLoader.cs: PlayableAircraft.Count = " +  PlayableAircraft.Count);

            FormUtils.LogRegister(  "CampaignLuaLoader.cs: TaskByPlane.Count = " + TaskByPlane.Count);
        }

       
    }
}
