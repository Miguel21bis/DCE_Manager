using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using DCE_Manager.Utils;
using NLua;

namespace DCE_Manager
{
    // Un axe d'attaque : une zone de départ (A) et une zone objectif (B), posées
    // par le campaignMaker. Le chemin entre les deux est CALCULE (jamais saisi à
    // la main, jamais sauvegardé) : on ne sauvegarde que Name / Start / End, et
    // le chemin est recalculé au chargement à partir des voisins des zones.
    //
    // Path[0] = zone de départ, Path[Count-1] = zone objectif. L'index d'une zone
    // dans Path (AxisIndex) sert de référence de progression pour le combat :
    // plus l'index est grand, plus on est avancé vers l'objectif.
    internal class WargameAxis
    {
        public string Name = "";
        public string StartZoneId;
        public string EndZoneId;

        // Nom du flag de campagne qui déclenche l'axe. Le campaignMaker le pose avec un
        // trigger normal dans camp_triggers_init.lua : Action.SetCampFlag("nom", true).
        // Vide = l'axe n'est jamais actif (pas de démarrage caché).
        public string ActivationFlag = "";

        // Flag posé par le moteur quand l'objectif de l'axe est atteint (lu ensuite par
        // les triggers de scriptsMod). Dérivé du flag d'activation : aucune saisie en plus.
        public string ObjectiveFlag
        {
            get { return ActivationFlag + "_reached"; }
        }

        // Zones de passage obligées, dans l'ordre voulu par le campaignMaker
        // (départ -> via[0] -> via[1] -> ... -> arrivée). Vide = axe direct, comme avant.
        public List<string> ViaZoneIds = new List<string>();

        // Calculé par WargameAxisPathFinder, pas sauvegardé
        public List<string> Path = new List<string>();

        public string AttackerSide;   // camp qui tenait la zone de départ au chargement (mémorisé)

        // Valide = un chemin d'au moins 2 zones a été trouvé entre A et B
        public bool IsValid
        {
            get { return Path.Count >= 2; }
        }

        // Actif = flag renseigné ET vrai dans camp.flag de Active\camp_status.lua
        public bool IsActive(HashSet<string> activeFlags)
        {
            return IsValid
                && !string.IsNullOrEmpty(ActivationFlag)
                && activeFlags != null
                && activeFlags.Contains(ActivationFlag);
        }

        // Position de la zone sur l'axe (0 = départ), -1 si elle n'est pas sur l'axe
        public int GetIndex(string zoneId)
        {
            return Path.FindIndex(id => string.Equals(id, zoneId, StringComparison.OrdinalIgnoreCase));
        }
    }

    internal static class WargameAxisPathFinder
    {
        // Chemin A -> B le plus court en DISTANCE (Dijkstra) en suivant les voisins des
        // zones. La distance entre deux zones voisines = distance entre leurs centres.
        // (Un simple "moins de zones possible" donnait des chemins qui sautent d'une
        // grande zone à l'autre en traversant tout le reste de la carte.)
        // Retourne une liste vide si A ou B n'existent pas, ou si aucun chemin.
        // Ancienne signature gardée : un axe direct, sans zone de passage.
        public static List<string> FindPath(List<WargameZoneData> zones, string startId, string endId)
        {
            return FindPath(zones, startId, null, endId);
        }

        // Chemin départ -> zones de passage (dans l'ordre) -> arrivée, calculé morceau par morceau
        // (Dijkstra en distance, comme avant). Un morceau n'a pas le droit de repasser par une
        // zone déjà utilisée : le chemin reste simple, chaque zone n'y figure qu'une fois, ce qui
        // garde un index de progression clair pour le combat.
        // Retourne une liste vide si une zone n'existe plus, ou si un morceau n'a pas de chemin
        // (par exemple si l'ordre des zones de passage force à repasser par une zone déjà prise).
        public static List<string> FindPath(List<WargameZoneData> zones, string startId, List<string> viaIds, string endId)
        {
            var path = new List<string>();

            if (zones == null || string.IsNullOrEmpty(startId) || string.IsNullOrEmpty(endId))
                return path;

            Dictionary<string, List<string>> links = BuildLinks(zones);

            // Les étapes dans l'ordre : départ, zones de passage, arrivée
            var stops = new List<string> { startId };

            if (viaIds != null)
            {
                foreach (string via in viaIds)
                {
                    if (!string.IsNullOrEmpty(via)) stops.Add(via);
                }
            }

            stops.Add(endId);

            foreach (string stop in stops)
            {
                if (!links.ContainsKey(stop))
                    return path;   // zone supprimée du .miz
            }

            var centers = new Dictionary<string, PointF>(StringComparer.OrdinalIgnoreCase);
            foreach (WargameZoneData zone in zones)
                centers[zone.Id] = GetZoneCenter(zone);

            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            path.Add(startId);
            used.Add(startId);

            for (int i = 0; i < stops.Count - 1; i++)
            {
                string from = stops[i];
                string to = stops[i + 1];

                if (string.Equals(from, to, StringComparison.OrdinalIgnoreCase))
                    continue;   // même zone deux fois de suite : rien à parcourir

                // Zones déjà prises par le chemin, sauf celle d'où l'on part
                var blocked = new HashSet<string>(used, StringComparer.OrdinalIgnoreCase);
                blocked.Remove(from);

                List<string> segment = FindSegment(links, centers, from, to, blocked);
                if (segment.Count < 2)
                    return new List<string>();   // pas de chemin

                for (int j = 1; j < segment.Count; j++)
                {
                    path.Add(segment[j]);
                    used.Add(segment[j]);
                }
            }

            return path;
        }

        // Un morceau de chemin (Dijkstra en distance entre centres de zones), sans jamais
        // passer par les zones "blocked". Retourne une liste vide si aucun chemin.
        private static List<string> FindSegment(
            Dictionary<string, List<string>> links,
            Dictionary<string, PointF> centers,
            string startId, string endId,
            HashSet<string> blocked)
        {
            var path = new List<string>();

            var distance = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            var cameFrom = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var done = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (string id in links.Keys)
                distance[id] = double.MaxValue;

            distance[startId] = 0;

            while (true)
            {
                // Zone non traitée la plus proche du départ (quelques dizaines de zones :
                // une simple boucle suffit, pas besoin d'une file de priorité)
                string current = null;
                double best = double.MaxValue;

                foreach (KeyValuePair<string, double> kvp in distance)
                {
                    if (!done.Contains(kvp.Key) && kvp.Value < best)
                    {
                        best = kvp.Value;
                        current = kvp.Key;
                    }
                }

                if (current == null) break;   // plus rien d'atteignable
                if (string.Equals(current, endId, StringComparison.OrdinalIgnoreCase)) break;

                done.Add(current);

                foreach (string next in links[current])
                {
                    if (done.Contains(next) || blocked.Contains(next)) continue;

                    double dx = centers[current].X - centers[next].X;
                    double dy = centers[current].Y - centers[next].Y;
                    double candidate = best + Math.Sqrt(dx * dx + dy * dy);

                    if (candidate < distance[next])
                    {
                        distance[next] = candidate;
                        cameFrom[next] = current;
                    }
                }
            }

            if (!cameFrom.ContainsKey(endId))
                return path;   // pas de chemin

            // On remonte de l'arrivée vers le départ, puis on retourne la liste
            string step = endId;
            while (step != null)
            {
                path.Add(step);
                step = cameFrom.ContainsKey(step) ? cameFrom[step] : null;
            }

            path.Reverse();
            return path;
        }

        // Centre d'une zone en coordonnées DCS : centre de gravité du polygone
        // (pas la moyenne des sommets, qui est tirée vers les côtes très détaillées)
        private static PointF GetZoneCenter(WargameZoneData zone)
        {
            if (zone.Shape != WargameZoneShape.Polygon || zone.DcsPoints == null || zone.DcsPoints.Count < 3)
                return zone.Center;

            return PolygonCentroid(zone.DcsPoints);
        }

        // Centre de gravité d'un polygone. Aussi utilisé par la carte (ucWargameMapView)
        // pour que le tracé des axes et le chemin calculé parlent des mêmes points.
        public static PointF PolygonCentroid(IList<PointF> points)
        {
            if (points == null || points.Count == 0)
                return PointF.Empty;

            double area2 = 0, cx = 0, cy = 0;
            int n = points.Count;

            for (int i = 0; i < n; i++)
            {
                PointF a = points[i];
                PointF b = points[(i + 1) % n];
                double cross = (double)a.X * b.Y - (double)b.X * a.Y;

                area2 += cross;
                cx += (a.X + b.X) * cross;
                cy += (a.Y + b.Y) * cross;
            }

            if (Math.Abs(area2) < 1e-6)
            {
                // Polygone plat : retour à la moyenne des sommets
                double sx = 0, sy = 0;
                foreach (PointF p in points) { sx += p.X; sy += p.Y; }
                return new PointF((float)(sx / n), (float)(sy / n));
            }

            return new PointF((float)(cx / (3 * area2)), (float)(cy / (3 * area2)));
        }

        // Mémorise, pour chaque axe, le camp qui tient sa zone de départ. À appeler une fois, au chargement
        // (après RecomputeAll, avant tout combat) : le camp attaquant ne change plus ensuite.
        public static void RememberAttackers(List<WargameAxis> axes, List<WargameZoneData> zones)
        {
            if (axes == null || zones == null) return;

            foreach (WargameAxis axis in axes)
            {
                if (axis.Path == null || axis.Path.Count == 0) continue;

                WargameZoneData start = zones.Find(z => string.Equals(z.Id, axis.Path[0], StringComparison.OrdinalIgnoreCase));
                if (start != null)
                    axis.AttackerSide = start.Control;
            }
        }

        // A appeler après chargement des zones, et à chaque fois que les voisins
        // ou les extrémités d'un axe changent.
        public static void RecomputeAll(List<WargameAxis> axes, List<WargameZoneData> zones)
        {
            if (axes == null) return;

            foreach (WargameAxis axis in axes)
            {
                axis.Path = FindPath(zones, axis.StartZoneId, axis.ViaZoneIds, axis.EndZoneId);

                FormUtils.LogRegister("WargameAxis | '" + axis.Name + "' " + axis.StartZoneId + " -> " + axis.EndZoneId
                    + (axis.IsValid ? " : " + axis.Path.Count + " zone(s)" : " : AUCUN CHEMIN"));
            }
        }

        // Liens dans les deux sens : si le campaignMaker a coché B comme voisine
        // de A mais pas l'inverse, le chemin doit quand même pouvoir passer.
        private static Dictionary<string, List<string>> BuildLinks(List<WargameZoneData> zones)
        {
            var links = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

            foreach (WargameZoneData zone in zones)
                links[zone.Id] = new List<string>();

            foreach (WargameZoneData zone in zones)
            {
                foreach (string n in zone.Neighbors)
                {
                    if (!links.ContainsKey(n)) continue;   // voisine supprimée du .miz

                    if (!links[zone.Id].Contains(n)) links[zone.Id].Add(n);
                    if (!links[n].Contains(zone.Id)) links[n].Add(zone.Id);
                }
            }

            return links;
        }
    }

    internal static class WargameCampFlags
    {
        // Lit camp.flag dans Active\camp_status.lua et retourne les noms des flags vrais
        // (true, ou nombre différent de 0). Les clés numériques ([801] = 2) sont
        // retournées en texte ("801"). Fichier absent (campagne pas encore jouée) = aucun flag.
        public static HashSet<string> LoadActiveFlags(string campStatusPath)
        {
            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (string.IsNullOrEmpty(campStatusPath) || !File.Exists(campStatusPath))
                return result;

            try
            {
                using (Lua lua = new Lua())
                {
                    lua.DoString(@"
                        os = nil
                        io = nil
                        file = nil
                        debug = nil
                    ");

                    lua.DoString(File.ReadAllText(campStatusPath));

                    // La table racine s'appelle "camp" ou "campL" selon qui a écrit le fichier
                    LuaTable root = lua["camp"] as LuaTable;
                    if (root == null) root = lua["campL"] as LuaTable;

                    LuaTable flags = root == null ? null : root["flag"] as LuaTable;
                    if (flags == null) return result;

                    foreach (object key in flags.Keys)
                    {
                        object value = flags[key];
                        bool on = false;

                        if (value is bool) on = (bool)value;
                        else if (value is double) on = (double)value != 0;
                        else if (value is long) on = (long)value != 0;

                        if (on)
                            result.Add(Convert.ToString(key, CultureInfo.InvariantCulture));
                    }
                }
            }
            catch (Exception ex)
            {
                FormUtils.LogRegister("WargameCampFlags | erreur lecture " + campStatusPath + " : " + ex.Message);
            }

            return result;
        }
    }
}
