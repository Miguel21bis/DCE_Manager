using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using DCE_Manager.Parameters;
using DCE_Manager.Utils;
using NLua;

namespace DCE_Manager
{
    internal class db_airbasesParser
    {
        public Dictionary<string, AirbaseInfo> Load_db_airbases(string campaignName)
        {
            var result = new Dictionary<string, AirbaseInfo>();

            LoadFile(campaignName, "Init", result);
            LoadFile(campaignName, "Active", result);

            return result;
        }

        private void LoadFile(string campaignName, string folderName, Dictionary<string, AirbaseInfo> result)
        {
            var timer = Stopwatch.StartNew();

            string pathFile = Path.Combine( SharedData.textBox_SavedGames, @"Mods\tech\DCE\Missions\Campaigns",
                campaignName, folderName, "db_airbases.lua"
            );

            if (!File.Exists(pathFile))
            {
                FormUtils.LogRegister($"db_airbases.lua not found: {pathFile}");
                return;
            }

            // Charger le fichier Lua
            using (Lua lua = new Lua())
            {
                lua.DoFile(pathFile);

                LuaTable dbAirbasesLua = lua["db_airbases"] as LuaTable;
                if (dbAirbasesLua == null)
                {
                    FormUtils.LogRegister("db_airbases table not found in Lua file.");
                    return;
                }

                foreach (object key in dbAirbasesLua.Keys)
                {
                    string baseName = key.ToString();
                    LuaTable baseLua = dbAirbasesLua[key] as LuaTable;

                    var info = new AirbaseInfo();
                    info.Name = baseName;

                    foreach (object subKey in baseLua.Keys)
                    {
                        string field = subKey.ToString();
                        object value = baseLua[subKey];

                        switch (field)
                        {
                            case "aliasName":
                                info.AliasName = value.ToString();
                                break;

                            case "side":
                                info.Side = value.ToString();
                                break;

                            case "elevation":
                                info.Elevation = Convert.ToInt32(value);
                                break;

                            case "airdromeId":
                                info.AirdromeId = Convert.ToInt32(value);
                                break;

                            case "divert":
                                info.Divert = Convert.ToBoolean(value);
                                break;

                            case "inactive":
                                info.Inactive = Convert.ToBoolean(value);
                                break;

                            case "x":
                                info.X = Convert.ToDouble(value);
                                break;

                            case "y":
                                info.Y = Convert.ToDouble(value);
                                break;

                            case "code":
                                info.Code = ParseStringTable(value as LuaTable);
                                break;

                            case "ATC_frequency":
                                info.ATCFrequencies = ParseDoubleList(value as LuaTable);
                                break;

                            case "runways":
                                info.Runways = ParseRunways(value as LuaTable);
                                break;

                            case "parkAlertSAR":
                                info.ParkAlertSAR = ParseParkSpots(value as LuaTable);
                                break;
                        }
                    }

                    result[baseName] = info;
                }
            }

            timer.Stop();
            FormUtils.LogRegister($"Parsed db_airbases ({folderName}) in {timer.ElapsedMilliseconds} ms");
        }

        // -------------------------
        //  Parsing helpers
        // -------------------------

        private Dictionary<string, string> ParseStringTable(LuaTable t)
        {
            var dict = new Dictionary<string, string>();
            if (t == null) return dict;

            foreach (object k in t.Keys)
                dict[k.ToString()] = t[k]?.ToString() ?? "";

            return dict;
        }

        private List<double> ParseDoubleList(LuaTable t)
        {
            var list = new List<double>();
            if (t == null) return list;

            foreach (object v in t.Values)
            {
                if (v == null)
                    continue;

                double parsed;

                // Si c'est déjà un nombre Lua → direct
                if (v is double d)
                {
                    list.Add(d);
                    continue;
                }

                // Sinon on tente de parser la chaîne
                var s = v.ToString().Trim();

                // Ignore les chaînes vides ou non numériques
                if (string.IsNullOrEmpty(s))
                    continue;

                // Essaye avec culture US (Lua utilise le point)
                if (double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out parsed))
                {
                    list.Add(parsed);
                    continue;
                }

                // Essaye avec culture FR (au cas où)
                if (double.TryParse(s, NumberStyles.Any, CultureInfo.GetCultureInfo("fr-FR"), out parsed))
                {
                    list.Add(parsed);
                    continue;
                }

                // Sinon → on ignore la valeur
                FormUtils.LogRegister($"ATC_frequency ignorée (non numérique) : '{s}'");
            }

            return list;
        }

        private List<RunwayInfo> ParseRunways(LuaTable t)
        {
            var list = new List<RunwayInfo>();
            if (t == null) return list;

            foreach (object k in t.Keys)
            {
                LuaTable r = t[k] as LuaTable;
                if (r == null) continue;

                var rw = new RunwayInfo
                {
                    Name = r["name"]?.ToString(),
                    Hdg = Convert.ToDouble(r["hdg"]),
                    TrueHdg = Convert.ToBoolean(r["true_hdg"]),
                    Length = Convert.ToInt32(r["length"]),
                    X = Convert.ToDouble(r["x"]),
                    Y = Convert.ToDouble(r["y"])
                };

                list.Add(rw);
            }

            return list;
        }

        private List<ParkSpot> ParseParkSpots(LuaTable t)
        {
            var list = new List<ParkSpot>();
            if (t == null) return list;

            foreach (object k in t.Keys)
            {
                LuaTable p = t[k] as LuaTable;
                if (p == null) continue;

                var spot = new ParkSpot
                {
                    X = Convert.ToDouble(p["x"]),
                    Y = Convert.ToDouble(p["y"]),
                    ReservedAR = Convert.ToBoolean(p["reservedAR"]),
                    ReservedSAR = Convert.ToBoolean(p["reservedSAR"]),
                    Occupied = Convert.ToBoolean(p["occupied"])
                };

                list.Add(spot);
            }

            return list;
        }
    }
}