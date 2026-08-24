using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using DCE_Manager.Utils;
using NLua;

namespace DCE_Manager
{
    // Lit Init/wargame/wargame_zones_init.lua (écrit par Saver_WargameZoneInit)
    // et reconstruit la liste de WargameZoneData, géométrie comprise.
    internal class WargameZoneInitLoader
    {
        // Etat global relu en même temps que les zones (compteur d'identifiants).
        // Toujours renseigné après un Load, même si le fichier n'existait pas.
        public WargameState State = new WargameState();

        public List<WargameZoneData> Load(string pathFile)
        {
            var result = new List<WargameZoneData>();

            if (!File.Exists(pathFile))
            {
                FormUtils.LogRegister("WargameZoneInitLoader | fichier introuvable : " + pathFile);
                return result;
            }

            using (Lua lua = new Lua())
            {
                lua.DoFile(pathFile);

                LuaTable stateLua = lua["wargame_state"] as LuaTable;
                if (stateLua != null)
                {
                    State.NextFormationId = (int)ToDouble(stateLua["nextFormationId"]);
                    if (State.NextFormationId < 1) State.NextFormationId = 1;
                }

                LuaTable zonesLua = lua["wargame_zones"] as LuaTable;
                if (zonesLua == null)
                {
                    FormUtils.LogRegister("WargameZoneInitLoader | table wargame_zones introuvable dans " + pathFile);
                    return result;
                }

                foreach (object key in zonesLua.Keys)
                {
                    LuaTable zoneLua = zonesLua[key] as LuaTable;
                    if (zoneLua == null) continue;

                    var zone = new WargameZoneData { Id = key.ToString() };

                    string shapeStr = zoneLua["shape"]?.ToString();
                    zone.Shape = shapeStr == "circle" ? WargameZoneShape.Circle : WargameZoneShape.Polygon;

                    LuaTable centerLua = zoneLua["center"] as LuaTable;
                    if (centerLua != null)
                        zone.Center = new PointF((float)ToDouble(centerLua["x"]), (float)ToDouble(centerLua["y"]));

                    zone.Radius = (float)ToDouble(zoneLua["radius"]);

                    string control = zoneLua["control"]?.ToString();
                    zone.Control = string.IsNullOrEmpty(control) ? WargameSide.Contested : control;

                    zone.ResupplyModifier = ToDouble(zoneLua["resupplyModifier"]);
                    zone.SupplySource = ToDouble(zoneLua["supplySource"]);

                    string terrain = zoneLua["terrain"]?.ToString();
                    zone.Terrain = string.IsNullOrEmpty(terrain) ? WargameTerrain.Plain : terrain;

                    string supplyRoute = zoneLua["supplyRoute"]?.ToString();
                    zone.SupplyRoute = string.IsNullOrEmpty(supplyRoute) ? WargameSupplyRoute.Road : supplyRoute;

                    zone.Formations = LoadFormations(zoneLua["formations"] as LuaTable);
                    zone.Irregular = LoadIrregular(zoneLua["irregular"] as LuaTable);
                    zone.Neighbors = LoadStringArray(zoneLua["neighbors"] as LuaTable);
                    zone.DcsPoints = LoadPoints(zoneLua["dcsPoints"] as LuaTable);

                    result.Add(zone);
                }
            }

            return result;
        }

        private List<WargameFormation> LoadFormations(LuaTable table)
        {
            var list = new List<WargameFormation>();
            if (table == null) return list;

            int i = 1;
            while (true)
            {
                LuaTable entry = table[i] as LuaTable;
                if (entry == null) break;

                var f = new WargameFormation
                {
                    FormationId = (int)ToDouble(entry["formationId"]),
                    Name = entry["name"]?.ToString() ?? "",
                    Template = entry["template"]?.ToString() ?? "",
                    Side = entry["side"]?.ToString() ?? "",
                    Multiplier = (int)ToDouble(entry["multiplier"]),
                    ForcePower = ToDouble(entry["forcePower"]),
                };

                // Garde-fou : un multiplicateur à zéro rendrait la formation inutile
                if (f.Multiplier <= 0) f.Multiplier = 1;

                list.Add(f);

                i++;
            }

            return list;
        }

        private WargameIrregularMarker LoadIrregular(LuaTable table)
        {
            if (table == null) return null;

            return new WargameIrregularMarker
            {
                Active = table["active"] is bool b && b,
                ResidualStock = (int)ToDouble(table["residualStock"]),
            };
        }

        private List<string> LoadStringArray(LuaTable table)
        {
            var list = new List<string>();
            if (table == null) return list;

            int i = 1;
            while (true)
            {
                object entry = table[i];
                if (entry == null) break;

                list.Add(entry.ToString());
                i++;
            }

            return list;
        }

        private List<PointF> LoadPoints(LuaTable table)
        {
            var list = new List<PointF>();
            if (table == null) return list;

            int i = 1;
            while (true)
            {
                LuaTable entry = table[i] as LuaTable;
                if (entry == null) break;

                list.Add(new PointF((float)ToDouble(entry["x"]), (float)ToDouble(entry["y"])));
                i++;
            }

            return list;
        }

        private double ToDouble(object value)
        {
            if (value == null) return 0;
            if (value is double d) return d;

            double parsed;
            if (double.TryParse(value.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out parsed))
                return parsed;

            return 0;
        }
    }
}
