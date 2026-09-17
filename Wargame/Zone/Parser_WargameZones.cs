using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using DCE_Manager.Utils;
using NLua;

namespace DCE_Manager
{
    // Extrait les zones wargame depuis Init/wargame/wargame_zone.miz.
    //
    // Les zones sont désormais des POLYGONES dessinés (mission.drawings), et non
    // plus des trigger zones : le cercle et le quad de l'éditeur de trigger zones
    // étaient trop limitatifs pour épouser un front. Le nom du polygone reste
    // l'identifiant de la zone (ex: "CQ95"), exactement comme avant.
    //
    // Ce fichier n'a pas d'autre rôle : tout ce qui y est dessiné en surface est
    // une zone. Les rues et les surfaces à éviter vivent dans wargame_spawn.miz.
    //
    // Les lignes éventuellement présentes sont ignorées : elles ne peuvent pas
    // délimiter une zone, et les laisser passer produirait des zones fantômes.
    internal class Parser_WargameZones
    {
        public List<WargameZoneData> LoadZonesFromMiz(string mizPath)
        {
            var result = new List<WargameZoneData>();
            var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            List<WargameDrawing> drawings = WargameDrawingReader.ReadFromMiz(mizPath);

            foreach (WargameDrawing drawing in drawings)
            {
                if (!drawing.IsSurface)
                {
                    FormUtils.LogRegister("Parser_WargameZones | '" + drawing.Name + "' ignoré (ligne, une zone doit être un polygone)");
                    continue;
                }

                if (string.IsNullOrWhiteSpace(drawing.Name))
                {
                    FormUtils.LogRegister("Parser_WargameZones | polygone sans nom ignoré");
                    continue;
                }

                // DCS suffixe les copies (-1, -2...) mais rien ne garantit
                // l'unicité si le campaignMaker renomme à la main. Or tout le
                // reste s'indexe sur ce nom : voisins, formations, targetlist.
                // Mieux vaut perdre le doublon bruyamment que l'écraser en silence.
                if (!seenIds.Add(drawing.Name))
                {
                    FormUtils.LogRegister("Parser_WargameZones | nom en double '" + drawing.Name + "', second polygone ignoré");
                    continue;
                }

                result.Add(new WargameZoneData
                {
                    Id = drawing.Name,
                    Shape = WargameZoneShape.Polygon,
                    DcsPoints = drawing.Points,
                    Center = drawing.GetCentroid(),
                });
            }

            // Repli sur l'ancien format : une campagne dont le wargame_zone.miz
            // date d'avant le passage aux polygones garde ses trigger zones.
            // A supprimer le jour où plus aucune campagne n'est dans ce cas.
            if (result.Count == 0)
            {
                result = LoadLegacyTriggerZones(mizPath);

                if (result.Count > 0)
                    FormUtils.LogRegister("Parser_WargameZones | aucun polygone trouvé, repli sur les trigger zones (" + result.Count + " zone(s))");
            }

            return result;
        }

        // ---------- Ancien format : mission.triggers.zones ----------

        private List<WargameZoneData> LoadLegacyTriggerZones(string mizPath)
        {
            var result = new List<WargameZoneData>();

            string missionText = WargameDrawingReader.ReadMissionEntry(mizPath);
            if (string.IsNullOrEmpty(missionText))
                return result;

            using (Lua lua = new Lua())
            {
                lua.DoString(@"
                    os = nil
                    io = nil
                    file = nil
                    debug = nil
                ");

                try
                {
                    lua.DoString(missionText);
                }
                catch (Exception ex)
                {
                    FormUtils.LogRegister("Parser_WargameZones | erreur parsing mission : " + ex.Message);
                    return result;
                }

                LuaTable mission = lua["mission"] as LuaTable;
                LuaTable triggers = mission?["triggers"] as LuaTable;
                LuaTable zonesLua = triggers?["zones"] as LuaTable;

                if (zonesLua == null)
                    return result;

                int index = 1;
                while (true)
                {
                    LuaTable zoneLua = zonesLua[index] as LuaTable;
                    if (zoneLua == null) break;

                    WargameZoneData zone = ParseLegacyZone(zoneLua);
                    if (zone != null)
                        result.Add(zone);

                    index++;
                }
            }

            return result;
        }

        private WargameZoneData ParseLegacyZone(LuaTable zoneLua)
        {
            string name = zoneLua["name"]?.ToString();
            if (string.IsNullOrWhiteSpace(name))
            {
                FormUtils.LogRegister("Parser_WargameZones | trigger zone sans nom ignorée");
                return null;
            }

            var zone = new WargameZoneData
            {
                Id = name,
                Center = new PointF((float)ToDouble(zoneLua["x"]), (float)ToDouble(zoneLua["y"])),
            };

            List<PointF> vertices = LoadVertices(zoneLua["verticies"] as LuaTable);

            if (vertices.Count >= 3)
            {
                zone.Shape = WargameZoneShape.Polygon;
                zone.DcsPoints = vertices;
            }
            else
            {
                zone.Shape = WargameZoneShape.Circle;
                zone.Radius = (float)ToDouble(zoneLua["radius"]);
            }

            return zone;
        }

        private List<PointF> LoadVertices(LuaTable verticiesLua)
        {
            var list = new List<PointF>();
            if (verticiesLua == null)
                return list;

            int i = 1;
            while (true)
            {
                LuaTable v = verticiesLua[i] as LuaTable;
                if (v == null) break;

                list.Add(new PointF((float)ToDouble(v["x"]), (float)ToDouble(v["y"])));
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
