using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using DCE_Manager.Parameters;
using DCE_Manager.Utils;

namespace DCE_Manager
{
    // Lecture des dessins de l'éditeur de mission (drawings/layers) dans Init\base_mission.miz.
    // Un .miz est un zip : le fichier "mission" est une table Lua, lue ici par un petit lecteur maison.
    internal static partial class Map_Drawings
    {
        public static List<Map_Drawing> Load(string campaign)
        {
            var list = new List<Map_Drawing>();
            try
            {
                string miz = Path.Combine(ParamConf.PATH_SavedGames_DCS, @"Mods\tech\DCE\Missions\Campaigns", campaign, "Init", "base_mission.miz");
                if (!File.Exists(miz)) return list;

                string text;
                using (ZipArchive zip = ZipFile.OpenRead(miz))
                {
                    ZipArchiveEntry entry = zip.GetEntry("mission");
                    if (entry == null) return list;
                    using (var sr = new StreamReader(entry.Open(), Encoding.UTF8))
                        text = sr.ReadToEnd();
                }

                int i = text.IndexOf("[\"drawings\"]", StringComparison.Ordinal);
                if (i < 0) return list;
                i = text.IndexOf('{', i);
                if (i < 0) return list;

                var reader = new LuaReader(text, i);
                var drawings = reader.ReadValue() as Dictionary<string, object>;
                if (drawings == null) return list;

                var layers = Sub(drawings, "layers");
                if (layers == null) return list;

                foreach (object layerObj in layers.Values)
                {
                    var layer = layerObj as Dictionary<string, object>;
                    if (layer == null || !IsVisible(layer)) continue;

                    var objects = Sub(layer, "objects");
                    if (objects == null) continue;

                    foreach (object o in objects.Values)
                    {
                        var d = o as Dictionary<string, object>;
                        if (d == null || !IsVisible(d)) continue;
                        Map_Drawing md = Convert(d);
                        if (md != null) list.Add(md);
                    }
                }
            }
            catch (Exception ex)
            {
                FormUtils.LogRegister("Map_Drawings | " + ex.Message);
            }
            return list;
        }

        // Zones du wargame : Init\Wargame\DCE_Generated\wargame_zones_init.lua (déjà lu par l'éditeur wargame).
        // Couleur = camp qui tient la zone. Les plans de débarquement (LANDING...) sont en pointillés.
        //   active : true = on applique par-dessus l'état de Active\wargame_zones.lua (camp actuel de chaque zone)
        public static List<WargameZoneData> LoadWargameZoneData(string campaign, bool active)
        {
            try
            {
                string file = WargameZoneRepository.GetInitLuaPath(campaign);
                if (!File.Exists(file)) return new List<WargameZoneData>();

                List<WargameZoneData> zones = new WargameZoneInitLoader().Load(file);

                if (active)
                {
                    string activePath = WargameZoneRepository.GetActiveWargameZonesPath(campaign);
                    if (File.Exists(activePath))
                        new WargameZoneActiveLoader().ApplyActiveState(activePath, zones);
                }
                return zones;
            }
            catch (Exception ex)
            {
                FormUtils.LogRegister("Map_Drawings | wargame zone data : " + ex.Message);
                return new List<WargameZoneData>();
            }
        }

        public static List<Map_Drawing> LoadWargameZones(string campaign, bool active)
        {
            var list = new List<Map_Drawing>();
            try
            {
                List<WargameZoneData> zones = LoadWargameZoneData(campaign, active);

                foreach (WargameZoneData z in zones)
                {
                    Color c = z.Control == WargameSide.Blue ? Color.FromArgb(70, 140, 255)
                            : z.Control == WargameSide.Red ? Color.FromArgb(235, 70, 70)
                            : Color.FromArgb(240, 170, 40);

                    var md = new Map_Drawing { Name = z.Id, Label = z.Id, Group = "zones", Line = c, Fill = Color.FromArgb(45, c), Thickness = 2f };

                    if (z.Id.StartsWith("LANDING", StringComparison.OrdinalIgnoreCase))
                    {
                        md.Line = Color.Cyan;
                        md.Fill = Color.FromArgb(40, Color.Cyan);
                        md.Dashed = true;
                    }

                    if (z.Shape == WargameZoneShape.Circle)
                    {
                        md.Kind = "Circle";
                        md.Radius = z.Radius;
                        md.Points.Add(new double[] { z.Center.X, z.Center.Y });
                    }
                    else
                    {
                        md.Kind = "Polygon";
                        md.Closed = true;
                        foreach (PointF pt in z.DcsPoints)
                            md.Points.Add(new double[] { pt.X, pt.Y });
                        if (md.Points.Count < 3) continue;
                    }
                    list.Add(md);
                }
            }
            catch (Exception ex)
            {
                FormUtils.LogRegister("Map_Drawings | wargame zones : " + ex.Message);
            }
            return list;
        }

        // Zones de spawn du wargame : Init\Wargame\wargame_spawn.miz (routes = lignes, surfaces tagguées town / forest / mountain...)
        public static List<Map_Drawing> LoadWargameSpawn(string campaign)
        {
            var list = new List<Map_Drawing>();
            try
            {
                string file = WargameZoneRepository.GetSpawnMizPath(campaign);
                if (!File.Exists(file)) return list;

                WargameSpawnAreas areas = new Parser_WargameSpawnAreas().LoadFromMiz(file);

                foreach (WargameSpawnLine l in areas.Lines)
                {
                    var md = new Map_Drawing { Name = l.Name, Kind = "Line", Group = "spawn", Line = Color.FromArgb(230, 225, 200, 60), Thickness = 1.5f };
                    foreach (PointF pt in l.Points) md.Points.Add(new double[] { pt.X, pt.Y });
                    if (md.Points.Count >= 2) list.Add(md);
                }

                foreach (WargameSpawnPolygon p in areas.Polygons)
                {
                    string tag = (p.Tag ?? "").ToLowerInvariant();
                    Color c = tag.Contains("forest") ? Color.FromArgb(80, 190, 90)
                            : tag.Contains("mountain") ? Color.FromArgb(190, 130, 80)
                            : Color.FromArgb(190, 190, 190);

                    var md = new Map_Drawing { Name = p.Name, Label = p.Tag ?? "", Kind = "Polygon", Group = "spawn", Closed = true,
                                               Line = c, Fill = Color.FromArgb(50, c), Thickness = 1.5f };
                    foreach (PointF pt in p.Points) md.Points.Add(new double[] { pt.X, pt.Y });
                    if (md.Points.Count >= 3) list.Add(md);
                }
            }
            catch (Exception ex)
            {
                FormUtils.LogRegister("Map_Drawings | wargame spawn : " + ex.Message);
            }
            return list;
        }

        private static Map_Drawing Convert(Dictionary<string, object> d)
        {
            string type = Str(d, "primitiveType");
            double mx = Num(d, "mapX"), my = Num(d, "mapY");

            var md = new Map_Drawing { Name = Str(d, "name"), Kind = type };
            md.Closed = Str(d, "closed") == "true";
            md.Dashed = Str(d, "style") != "solid" && Str(d, "style") != "";
            md.Thickness = (float)Math.Max(1, Math.Min(5, Num(d, "thickness") / 4));
            md.Line = Col(Str(d, "colorString"), Color.White);
            md.Fill = Col(Str(d, "fillColorString"), Color.Transparent);

            if (type == "Line" || type == "Polygon")
            {
                var pts = Sub(d, "points");
                string mode = Str(d, "polygonMode");

                if (type == "Polygon" && mode == "circle")
                {
                    md.Kind = "Circle";
                    md.Radius = Num(d, "radius");
                    md.Points.Add(new double[] { mx, my });
                    return md;
                }
                if (pts == null) return null;

                // les clés sont des numéros (parfois dans le désordre) : on trie
                var keys = new List<int>();
                foreach (string k in pts.Keys) { int n; if (int.TryParse(k, out n)) keys.Add(n); }
                keys.Sort();
                foreach (int k in keys)
                {
                    var p = pts[k.ToString()] as Dictionary<string, object>;
                    if (p == null) continue;
                    md.Points.Add(new double[] { mx + Num(p, "x"), my + Num(p, "y") });
                }
                if (type == "Polygon") md.Closed = true;
                return md.Points.Count >= 2 ? md : null;
            }

            if (type == "TextBox")
            {
                md.Text = Str(d, "text");
                md.Points.Add(new double[] { mx, my });
                return md.Text != "" ? md : null;
            }
            return null;        // icônes, flèches... : pas gérées
        }

        // ------------------------------------------------------------------
        //  Mission en cours : plans de vol des joueurs (skill Client / Player) et rayons de menace des SAM
        // ------------------------------------------------------------------

        // Portée maximale (m) par lanceur, d'après les données connues de DCS (valeurs arrondies, indicatives).
        // Le motif est cherché dans le type de l'unité ; le premier qui correspond gagne (les plus précis d'abord).
        private static readonly object[][] SamTable =
        {
            new object[] { "5P85",            "SA-10 / S-300PS",   75000.0 },
            new object[] { "9A82",            "SA-12 / S-300V",    75000.0 },
            new object[] { "9A83",            "SA-12 / S-300V",    75000.0 },
            new object[] { "Patriot ln",      "Patriot",           80000.0 },
            new object[] { "Hawk ln",         "Hawk",              40000.0 },
            new object[] { "S_75M",           "SA-2",              45000.0 },
            new object[] { "5p73",            "SA-3",              25000.0 },
            new object[] { "9A310",           "SA-11 Buk",         32000.0 },
            new object[] { "2P25",            "SA-6 Kub",          24000.0 },
            new object[] { "NASAMS_LN",       "NASAMS",            30000.0 },
            new object[] { "IRIST",           "IRIS-T SLM",        40000.0 },
            new object[] { "Tor 9A331",       "SA-15 Tor",         12000.0 },
            new object[] { "HQ-7",            "HQ-7",              12000.0 },
            new object[] { "Osa 9A33",        "SA-8 Osa",          10000.0 },
            new object[] { "Roland ADS",      "Roland",             8000.0 },
            new object[] { "2S6",             "SA-19 Tunguska",     8000.0 },
            new object[] { "rapier",          "Rapier",             7000.0 },
            new object[] { "M1097",           "Avenger",            5500.0 },
            new object[] { "Strela-10M3",     "SA-13 Strela-10",    5000.0 },
            new object[] { "Igla",            "SA-18 Igla",         5000.0 },
            new object[] { "M6 Linebacker",   "Linebacker",         4800.0 },
            new object[] { "Stinger",         "Stinger",            4800.0 },
            new object[] { "Strela-1 9P31",   "SA-9 Strela-1",      4200.0 },
        };

        private static bool SamRange(string type, out string name, out double range)
        {
            name = "";
            range = 0;
            if (string.IsNullOrEmpty(type)) return false;

            foreach (object[] row in SamTable)
            {
                if (type.IndexOf((string)row[0], StringComparison.OrdinalIgnoreCase) < 0) continue;
                name = (string)row[1];
                range = (double)row[2];
                return true;
            }
            return false;
        }
    }
}
