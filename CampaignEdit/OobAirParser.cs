using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using DCE_Manager.Parameters;
using DCE_Manager.Utils;
using static DCE_Manager.Utils.FormUtils;

namespace DCE_Manager
{
    internal class OobAirParser
    {
        public List<Squad> LoadCampaignSquads(string campaignName)
        {
            var squads = new List<Squad>();

            LoadFile(campaignName, "Init", squads);
            LoadFile(campaignName, "Active", squads);

            return squads;
        }

        private void LoadFile(string campaignName, string folderName, List<Squad> squads)
        {
            List_oob_air_Manager.List_oob_air = new List<Squad>();

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

                        var squad = new Squad
                        {
                            SideString = side,
                            IdSquad = idSquad,
                            FolderFile = folderName,
                        };

                        List_oob_air_Manager.List_oob_air.Add(squad);
                        squads.Add(squad);


                        var level2 = entry2.Value.luaobj as Dictionary<string, LuaObject>;
                        if (level2 == null) continue;

                        foreach (var entry3 in level2) // propriétés squad
                        {
                            //FormUtils.LogRegister("OobAirParser.cs: D foreach   foreach (var entry3 in level2) // propriétés squad ");
                            var key = entry3.Key;
                            var valObj = entry3.Value.luaobj;

                            // ⚡ SWITCH = beaucoup + rapide que 30 if
                            switch (key)
                            {
                                case "name": squad.Name = valObj?.ToString(); break;
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
                                        squad.Tasks = new Dictionary<string, object>(tasks.Count);
                                        foreach (var e in tasks)
                                            squad.Tasks[e.Key] = Convert.ToBoolean(e.Value.luaobj);
                                    }
                                    break;

                                case "tasksCoef":
                                    if (valObj is Dictionary<string, LuaObject> tasksCoef)
                                    {
                                        squad.TasksCoef = new Dictionary<string, object>(tasksCoef.Count);
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
                                    if (valObj is Dictionary<string, LuaObject> roster)
                                    {
                                        squad.Roster = new Dictionary<string, object>(roster.Count);
                                        foreach (var e in roster)
                                            squad.Roster[e.Key] = Convert.ToInt32(e.Value.luaobj);
                                    }
                                    break;

                                case "score":
                                    if (valObj is Dictionary<string, LuaObject> score)
                                    {
                                        squad.Score = new Dictionary<string, object>(score.Count);
                                        foreach (var e in score)
                                            squad.Score[e.Key] = Convert.ToInt32(e.Value.luaobj);
                                    }
                                    break;

                                default:
                                    // ⚡ ultra important : éviter GetType() (lent)
                                    if (valObj is Dictionary<string, LuaObject> sub)
                                    {
                                        var dict = new Dictionary<string, object>(sub.Count);
                                        foreach (var e in sub)
                                            dict[e.Key] = e.Value.luaobj;

                                        squad.AdditionalProperties[key] = dict;
                                    }
                                    else
                                    {
                                        squad.AdditionalProperties[key] = valObj;
                                    }
                                    break;
                            }
                        }
                    }
                }

            }// fileExist()
            
            time_ParseOobAir.Stop();
            LogRegister($"Time time_ParseOobAir: {time_ParseOobAir.ElapsedMilliseconds} ms");

            var time_UI = Stopwatch.StartNew();

        }
    }
}
