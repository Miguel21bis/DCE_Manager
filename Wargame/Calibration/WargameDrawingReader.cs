using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using DCE_Manager.Utils;
using NLua;

namespace DCE_Manager
{
    // Une forme dessinée dans l'éditeur DCS, ramenée à ce qui nous intéresse :
    // un nom, et une liste de points en coordonnées DCS absolues.
    //
    // Les quatre modes de polygone de l'éditeur (free, circle, oval, rect) sont
    // tous convertis en liste de points par le reader : un seul test
    // d'appartenance à écrire, un seul chemin de rendu sur la carte. Un cercle
    // approché par 24 segments fait 1 % d'erreur, négligeable devant un tracé
    // à la souris.
    internal class WargameDrawing
    {
        public string Name = "";
        public string LayerName = "";

        // true = surface fermée (primitiveType "Polygon"), false = polyligne
        // ouverte (primitiveType "Line"). C'est ce qui distingue une zone ou une
        // ville d'une rue.
        public bool IsSurface;

        // Coordonnées DCS absolues, dans l'ordre du tracé
        public List<PointF> Points = new List<PointF>();

        public double GetLength()
        {
            double total = 0;

            for (int i = 0; i < Points.Count - 1; i++)
            {
                double dx = Points[i + 1].X - Points[i].X;
                double dy = Points[i + 1].Y - Points[i].Y;
                total += Math.Sqrt(dx * dx + dy * dy);
            }

            return total;
        }

        // Centroïde de surface (et non moyenne des sommets) : sur un contour
        // tracé à la main, les points sont serrés dans les virages et espacés
        // sur les lignes droites, une simple moyenne serait tirée vers les
        // virages. Sur une forme très concave (en U), le centroïde peut tomber
        // hors du polygone — vérifié sur les 23 zones de l'échantillon, aucune
        // n'est dans ce cas.
        public PointF GetCentroid()
        {
            int n = Points.Count;

            if (n == 0) return PointF.Empty;
            if (n < 3) return Points[0];

            double area = 0, cx = 0, cy = 0;

            for (int i = 0; i < n; i++)
            {
                PointF a = Points[i];
                PointF b = Points[(i + 1) % n];

                double cross = a.X * (double)b.Y - b.X * (double)a.Y;

                area += cross;
                cx += (a.X + b.X) * cross;
                cy += (a.Y + b.Y) * cross;
            }

            area *= 0.5;

            // Polygone dégénéré (points alignés) : on se rabat sur la moyenne
            if (Math.Abs(area) < 0.000001)
            {
                double sx = 0, sy = 0;

                foreach (PointF p in Points)
                {
                    sx += p.X;
                    sy += p.Y;
                }

                return new PointF((float)(sx / n), (float)(sy / n));
            }

            return new PointF((float)(cx / (6 * area)), (float)(cy / (6 * area)));
        }
    }

    // Lecture brute des dessins d'un .miz, partagée par le parser de zones et
    // celui des spawn areas : les deux lisent exactement la même structure
    // (mission.drawings.layers[].objects[]) et n'en font qu'une interprétation
    // différente ensuite.
    //
    // Conventions DCS relevées sur des fichiers réels :
    //  - les coordonnées des points sont RELATIVES à mapX/mapY de l'objet,
    //    l'absolu vaut donc (mapX + point.x, mapY + point.y), dans le même
    //    repère que les anciennes trigger zones -> compatible WargameMapCalibration
    //  - les points [1] et [2] sont systématiquement identiques (0,0) : doublon
    //    en tête, dédupliqué ici sous peine de premier segment de longueur nulle
    //  - la table "points" est sérialisée dans le désordre ([7], [1], [2], [4]...),
    //    les index restent contigus de 1 à n : on itère donc par index, jamais
    //    dans l'ordre du texte. C'est aussi ce qui interdit toute approche regex.
    internal static class WargameDrawingReader
    {
        private const int CircleSegments = 24;

        public static List<WargameDrawing> ReadFromMiz(string mizPath)
        {
            var result = new List<WargameDrawing>();
            string fileName = Path.GetFileName(mizPath);

            if (!File.Exists(mizPath))
            {
                FormUtils.LogRegister("WargameDrawingReader | fichier introuvable : " + mizPath);
                return result;
            }

            string missionText = ReadMissionEntry(mizPath);
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
                    FormUtils.LogRegister("WargameDrawingReader | erreur parsing de " + fileName + " : " + ex.Message);
                    return result;
                }

                LuaTable mission = lua["mission"] as LuaTable;
                LuaTable drawings = mission?["drawings"] as LuaTable;
                LuaTable layers = drawings?["layers"] as LuaTable;

                if (layers == null)
                {
                    FormUtils.LogRegister("WargameDrawingReader | pas de mission.drawings.layers dans " + fileName);
                    return result;
                }

                // Tous les calques sont lus sans distinction : le campaignMaker
                // dessine où il veut, seuls le type d'objet et son nom comptent.
                int layerIndex = 1;
                while (true)
                {
                    LuaTable layer = layers[layerIndex] as LuaTable;
                    if (layer == null) break;

                    string layerName = layer["name"]?.ToString() ?? "";
                    LuaTable objects = layer["objects"] as LuaTable;

                    if (objects != null)
                    {
                        int objIndex = 1;
                        while (true)
                        {
                            LuaTable obj = objects[objIndex] as LuaTable;
                            if (obj == null) break;

                            WargameDrawing drawing = ParseObject(obj, layerName);
                            if (drawing != null)
                                result.Add(drawing);

                            objIndex++;
                        }
                    }

                    layerIndex++;
                }
            }

            return result;
        }

        // Lit le texte Lua de l'entrée "mission" à l'intérieur du .miz (un zip).
        // Rien n'est extrait sur disque.
        public static string ReadMissionEntry(string mizPath)
        {
            using (ZipArchive archive = ZipFile.OpenRead(mizPath))
            {
                ZipArchiveEntry entry = archive.GetEntry("mission");
                if (entry == null)
                {
                    FormUtils.LogRegister("WargameDrawingReader | entrée 'mission' introuvable dans " + mizPath);
                    return null;
                }

                using (var reader = new StreamReader(entry.Open(), Encoding.UTF8))
                {
                    return reader.ReadToEnd();
                }
            }
        }

        private static WargameDrawing ParseObject(LuaTable obj, string layerName)
        {
            string name = obj["name"]?.ToString() ?? "";
            string primitiveType = obj["primitiveType"]?.ToString();

            double mapX = ToDouble(obj["mapX"]);
            double mapY = ToDouble(obj["mapY"]);

            List<PointF> points;
            bool isSurface;

            if (primitiveType == "Line")
            {
                // Une flèche est une annotation de briefing, pas un tracé utile.
                if (obj["lineMode"]?.ToString() == "arrow")
                {
                    FormUtils.LogRegister("WargameDrawingReader | '" + name + "' ignoré (ligne en mode flèche)");
                    return null;
                }

                points = ReadRelativePoints(obj["points"] as LuaTable, mapX, mapY);
                isSurface = false;

                if (points.Count < 2)
                {
                    FormUtils.LogRegister("WargameDrawingReader | '" + name + "' ignoré (ligne de moins de 2 points)");
                    return null;
                }
            }
            else if (primitiveType == "Polygon")
            {
                points = BuildPolygonPoints(obj, name, mapX, mapY);
                isSurface = true;

                if (points == null)
                    return null;

                if (points.Count < 3)
                {
                    FormUtils.LogRegister("WargameDrawingReader | '" + name + "' ignoré (polygone de moins de 3 points)");
                    return null;
                }
            }
            else
            {
                // TextBox, Icon et compagnie : annotations, aucune géométrie exploitable.
                return null;
            }

            return new WargameDrawing
            {
                Name = name,
                LayerName = layerName,
                IsSurface = isSurface,
                Points = points,
            };
        }

        // Ramène les quatre modes de polygone de DCS à une liste de points.
        // Retourne null si l'objet n'est pas exploitable (le cas est logué ici).
        private static List<PointF> BuildPolygonPoints(LuaTable obj, string name, double mapX, double mapY)
        {
            string mode = obj["polygonMode"]?.ToString();

            switch (mode)
            {
                case "free":
                    return ReadRelativePoints(obj["points"] as LuaTable, mapX, mapY);

                case "circle":
                    return BuildEllipse(mapX, mapY, ToDouble(obj["radius"]), ToDouble(obj["radius"]), 0);

                case "oval":
                    return BuildEllipse(mapX, mapY, ToDouble(obj["r1"]), ToDouble(obj["r2"]), ToDouble(obj["angle"]));

                case "rect":
                    return BuildRect(mapX, mapY, ToDouble(obj["width"]), ToDouble(obj["height"]), ToDouble(obj["angle"]));

                default:
                    // "arrow" surtout : une flèche pleine reste une annotation.
                    FormUtils.LogRegister("WargameDrawingReader | '" + name + "' ignoré (polygonMode '" + mode + "')");
                    return null;
            }
        }

        // Cercle et ellipse. L'angle DCS est en degrés, la rotation est appliquée
        // dans le plan X/Y du moteur. A confronter visuellement à la carte le jour
        // où ces formes s'afficheront : sur des ellipses proches de 0°, une erreur
        // de signe se verrait à peine.
        private static List<PointF> BuildEllipse(double centerX, double centerY, double r1, double r2, double angleDeg)
        {
            var points = new List<PointF>();

            if (r1 <= 0 || r2 <= 0)
                return points;

            double angle = angleDeg * Math.PI / 180.0;
            double cos = Math.Cos(angle);
            double sin = Math.Sin(angle);

            for (int i = 0; i < CircleSegments; i++)
            {
                double t = 2 * Math.PI * i / CircleSegments;
                double localX = r1 * Math.Cos(t);
                double localY = r2 * Math.Sin(t);

                points.Add(new PointF(
                    (float)(centerX + localX * cos - localY * sin),
                    (float)(centerY + localX * sin + localY * cos)));
            }

            return points;
        }

        private static List<PointF> BuildRect(double centerX, double centerY, double width, double height, double angleDeg)
        {
            var points = new List<PointF>();

            if (width <= 0 || height <= 0)
                return points;

            double angle = angleDeg * Math.PI / 180.0;
            double cos = Math.Cos(angle);
            double sin = Math.Sin(angle);

            double halfW = width / 2.0;
            double halfH = height / 2.0;

            double[,] corners = { { -halfW, -halfH }, { halfW, -halfH }, { halfW, halfH }, { -halfW, halfH } };

            for (int i = 0; i < 4; i++)
            {
                double localX = corners[i, 0];
                double localY = corners[i, 1];

                points.Add(new PointF(
                    (float)(centerX + localX * cos - localY * sin),
                    (float)(centerY + localX * sin + localY * cos)));
            }

            return points;
        }

        // Les points d'un dessin sont des décalages par rapport à mapX/mapY.
        // Itération par index et non dans l'ordre du fichier (voir en-tête),
        // puis suppression des points consécutifs identiques.
        private static List<PointF> ReadRelativePoints(LuaTable pointsLua, double mapX, double mapY)
        {
            var list = new List<PointF>();

            if (pointsLua == null)
                return list;

            int i = 1;
            while (true)
            {
                LuaTable p = pointsLua[i] as LuaTable;
                if (p == null) break;

                var point = new PointF(
                    (float)(mapX + ToDouble(p["x"])),
                    (float)(mapY + ToDouble(p["y"])));

                if (list.Count == 0 || !IsSamePoint(list[list.Count - 1], point))
                    list.Add(point);

                i++;
            }

            return list;
        }

        private static bool IsSamePoint(PointF a, PointF b)
        {
            return Math.Abs(a.X - b.X) < 0.01f && Math.Abs(a.Y - b.Y) < 0.01f;
        }

        private static double ToDouble(object value)
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
