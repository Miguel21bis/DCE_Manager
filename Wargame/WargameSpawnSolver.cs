using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using DCE_Manager.Utils;

namespace DCE_Manager
{
    // Une unité du template, à sa position FINALE une fois le template posé
    // quelque part dans une zone.
    internal class WargamePlacedUnit
    {
        public string Name;
        public string GroupName;
        public string Category;
        public PointF Position;
    }

    // Trouve où poser un template dans une zone, et calcule la position de
    // chaque unité qui le compose.
    //
    // Règle (voir échange avec Miguel) :
    //   1. tirer un point au hasard dans la zone
    //   2. regarder dans quel polygone de wargame_spawn.miz il tombe
    //        - aucun polygone / Free (forêt) -> le template garde sa forme
    //          d'origine, juste translaté sur ce point
    //        - Forbidden (montagne)          -> on rejette, on retente un
    //          autre point
    //        - RoadOnly (ville/town/base)    -> il faut une rue À L'INTÉRIEUR
    //          de ce même polygone ; si oui, le template est déroulé le long
    //          de cette rue ; si non, on retente un autre point
    //
    // Les unités à moins de ClusterDistanceMeters les unes des autres (ex: une
    // DCA et son remblai) sont regroupées en paquets rigides qui se déplacent
    // ensemble, jamais éclatés.
    //
    // Pas de rotation pour l'instant : l'orientation dépendra plus tard de la
    // zone/formation ennemie la plus proche (voir document de reprise).
    internal static class WargameSpawnSolver
    {
        public const double ClusterDistanceMeters = 10.0;
        private const int MaxAttempts = 50;

        // Combien de points on teste le long de chaque segment de rue pour
        // vérifier s'il traverse le polygone visé. Une rue de plusieurs km avec
        // seulement 2 points ne suffirait pas à détecter qu'elle passe par une
        // ville éloignée des extrémités.
        private const int RoadSamplesPerSegment = 20;

        public static List<WargamePlacedUnit> PlaceTemplate(
            WargameZoneData zone, List<WargameTemplateUnit> templateUnits, WargameSpawnAreas spawnAreas, Random rng)
        {
            if (templateUnits == null || templateUnits.Count == 0)
                return null;

            if (zone?.DcsPoints == null || zone.DcsPoints.Count < 3)
            {
                FormUtils.LogRegister("WargameSpawnSolver | zone '" + zone?.Id + "' sans géométrie exploitable");
                return null;
            }

            List<List<WargameTemplateUnit>> clusters = BuildClusters(templateUnits);

            for (int attempt = 0; attempt < MaxAttempts; attempt++)
            {
                PointF candidate = RandomPointInPolygon(zone.DcsPoints, rng);
                WargameSpawnPolygon polygon = spawnAreas?.FindPolygonAt(candidate);

                string policy = polygon != null ? polygon.Policy : WargameSpawnPolicy.Free;

                if (policy == WargameSpawnPolicy.Forbidden)
                    continue; // montagne : on rejette ce point et on en retire un autre

                if (policy == WargameSpawnPolicy.Free)
                    return PlaceAsIs(templateUnits, candidate);

                // RoadOnly : il faut une rue A L'INTERIEUR de ce polygone précis
                PointF? snapped = SnapToRoadInsidePolygon(candidate, polygon, spawnAreas,
                    out List<PointF> roadPoints, out int segmentIndex);

                if (snapped == null)
                    continue; // pas de rue dans cette ville : on retente ailleurs

                return PlaceAlongRoad(clusters, roadPoints, segmentIndex, snapped.Value);
            }

            FormUtils.LogRegister("WargameSpawnSolver | aucune position valide trouvée pour la zone '"
                + zone.Id + "' après " + MaxAttempts + " tentatives");
            return null;
        }

        // -------------------- Cas FREE : translation simple --------------------

        private static List<WargamePlacedUnit> PlaceAsIs(List<WargameTemplateUnit> units, PointF target)
        {
            PointF centroid = GetCentroid(units.Select(u => u.Position));
            float dx = target.X - centroid.X;
            float dy = target.Y - centroid.Y;

            return units.Select(u => new WargamePlacedUnit
            {
                Name = u.Name,
                GroupName = u.GroupName,
                Category = u.Category,
                Position = new PointF(u.Position.X + dx, u.Position.Y + dy),
            }).ToList();
        }

        // -------------------- Cas ROADONLY : déroulé le long de la rue --------------------

        private static List<WargamePlacedUnit> PlaceAlongRoad(
            List<List<WargameTemplateUnit>> clusters, List<PointF> roadPoints, int startSegmentIndex, PointF startPoint)
        {
            // Ordonne les paquets selon l'axe le plus étalé du template (le plus
            // simple des axes principaux, suffisant pour "garder l'ordre d'origine"
            // sans sortir une vraie ACP).
            List<(List<WargameTemplateUnit> units, PointF centroid)> ordered = OrderClustersAlongMainAxis(clusters);

            var result = new List<WargamePlacedUnit>();

            double walked = 0;
            PointF previousCentroid = ordered[0].centroid;

            for (int i = 0; i < ordered.Count; i++)
            {
                if (i > 0)
                    walked += Distance(ordered[i - 1].centroid, ordered[i].centroid);

                PointF anchor = WalkAlongPolyline(roadPoints, startSegmentIndex, startPoint, walked);

                float dx = anchor.X - ordered[i].centroid.X;
                float dy = anchor.Y - ordered[i].centroid.Y;

                foreach (WargameTemplateUnit u in ordered[i].units)
                {
                    result.Add(new WargamePlacedUnit
                    {
                        Name = u.Name,
                        GroupName = u.GroupName,
                        Category = u.Category,
                        Position = new PointF(u.Position.X + dx, u.Position.Y + dy),
                    });
                }
            }

            return result;
        }

        private static List<(List<WargameTemplateUnit> units, PointF centroid)> OrderClustersAlongMainAxis(
            List<List<WargameTemplateUnit>> clusters)
        {
            var withCentroids = clusters.Select(c => (units: c, centroid: GetCentroid(c.Select(u => u.Position)))).ToList();

            float minX = withCentroids.Min(c => c.centroid.X), maxX = withCentroids.Max(c => c.centroid.X);
            float minY = withCentroids.Min(c => c.centroid.Y), maxY = withCentroids.Max(c => c.centroid.Y);

            bool useX = (maxX - minX) >= (maxY - minY);

            return useX
                ? withCentroids.OrderBy(c => c.centroid.X).ToList()
                : withCentroids.OrderBy(c => c.centroid.Y).ToList();
        }

        // Avance de "distance" mètres le long d'une polyligne, à partir d'un point
        // donné sur un segment donné. Si la distance dépasse la fin de la route,
        // le reste est posé à l'extrémité (mieux vaut un empilement visible et
        // logué qu'un plantage ou une position hors de la route).
        private static PointF WalkAlongPolyline(List<PointF> points, int startSegmentIndex, PointF startPoint, double distance)
        {
            double remaining = distance;
            PointF current = startPoint;
            int segmentIndex = startSegmentIndex;

            while (remaining > 0 && segmentIndex < points.Count - 1)
            {
                PointF segEnd = points[segmentIndex + 1];
                double toEnd = Distance(current, segEnd);

                if (toEnd >= remaining)
                {
                    double t = remaining / toEnd;
                    return new PointF(
                        (float)(current.X + (segEnd.X - current.X) * t),
                        (float)(current.Y + (segEnd.Y - current.Y) * t));
                }

                remaining -= toEnd;
                current = segEnd;
                segmentIndex++;
            }

            if (remaining > 0)
            {
                FormUtils.LogRegister("WargameSpawnSolver | formation plus longue que la rue disponible, "
                    + "unités posées en bout de route");
            }

            return current;
        }

        // -------------------- Recherche d'une rue dans le polygone --------------------

        private static PointF? SnapToRoadInsidePolygon(
            PointF candidate, WargameSpawnPolygon polygon, WargameSpawnAreas spawnAreas,
            out List<PointF> bestRoadPoints, out int bestSegmentIndex)
        {
            bestRoadPoints = null;
            bestSegmentIndex = -1;

            if (spawnAreas == null || spawnAreas.Lines.Count == 0)
                return null;

            double bestDistance = double.MaxValue;
            PointF? bestPoint = null;

            foreach (WargameSpawnLine road in spawnAreas.Lines)
            {
                for (int i = 0; i < road.Points.Count - 1; i++)
                {
                    PointF a = road.Points[i];
                    PointF b = road.Points[i + 1];

                    for (int s = 0; s <= RoadSamplesPerSegment; s++)
                    {
                        float t = (float)s / RoadSamplesPerSegment;
                        var p = new PointF(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t);

                        if (!polygon.Contains(p))
                            continue; // ce point de la rue n'est pas DANS ce polygone précis

                        double d = Distance(candidate, p);
                        if (d < bestDistance)
                        {
                            bestDistance = d;
                            bestPoint = p;
                            bestRoadPoints = road.Points;
                            bestSegmentIndex = i;
                        }
                    }
                }
            }

            return bestPoint;
        }

        // -------------------- Regroupement des éléments proches --------------------

        // Union-Find classique, même principe que WargameGeometryCleaner : les
        // unités à moins de ClusterDistanceMeters les unes des autres finissent
        // dans le même paquet, de proche en proche.
        private static List<List<WargameTemplateUnit>> BuildClusters(List<WargameTemplateUnit> units)
        {
            int n = units.Count;
            int[] parent = new int[n];
            for (int i = 0; i < n; i++) parent[i] = i;

            int Find(int i)
            {
                while (parent[i] != i) { parent[i] = parent[parent[i]]; i = parent[i]; }
                return i;
            }

            void Union(int a, int b)
            {
                int ra = Find(a), rb = Find(b);
                if (ra != rb) parent[ra] = rb;
            }

            double thresholdSq = ClusterDistanceMeters * ClusterDistanceMeters;

            for (int i = 0; i < n; i++)
            {
                for (int j = i + 1; j < n; j++)
                {
                    double dx = units[i].Position.X - units[j].Position.X;
                    double dy = units[i].Position.Y - units[j].Position.Y;

                    if (dx * dx + dy * dy <= thresholdSq)
                        Union(i, j);
                }
            }

            var clusters = new Dictionary<int, List<WargameTemplateUnit>>();
            for (int i = 0; i < n; i++)
            {
                int root = Find(i);
                if (!clusters.TryGetValue(root, out List<WargameTemplateUnit> list))
                {
                    list = new List<WargameTemplateUnit>();
                    clusters[root] = list;
                }
                list.Add(units[i]);
            }

            return clusters.Values.ToList();
        }

        // -------------------- Utilitaires géométriques --------------------

        private static PointF GetCentroid(IEnumerable<PointF> points)
        {
            float sumX = 0, sumY = 0;
            int n = 0;

            foreach (PointF p in points)
            {
                sumX += p.X;
                sumY += p.Y;
                n++;
            }

            return n == 0 ? PointF.Empty : new PointF(sumX / n, sumY / n);
        }

        private static double Distance(PointF a, PointF b)
        {
            double dx = a.X - b.X, dy = a.Y - b.Y;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        // Tirage au sort par rejet : point aléatoire dans le rectangle englobant,
        // retenu seulement s'il tombe vraiment dans le polygone. Simple et
        // largement assez rapide pour des zones dont le remplissage n'est jamais
        // extrême (sinon quelques dizaines d'essais suffisent toujours).
        private static PointF RandomPointInPolygon(List<PointF> polygon, Random rng)
        {
            float minX = polygon.Min(p => p.X), maxX = polygon.Max(p => p.X);
            float minY = polygon.Min(p => p.Y), maxY = polygon.Max(p => p.Y);

            for (int i = 0; i < 200; i++)
            {
                var p = new PointF(
                    minX + (float)(rng.NextDouble() * (maxX - minX)),
                    minY + (float)(rng.NextDouble() * (maxY - minY)));

                if (IsPointInPolygon(p, polygon))
                    return p;
            }

            // Repli si le polygone est trop tourmenté pour que le rejet aboutisse
            // (zone en croissant très fin, par exemple) : le centroïde est presque
            // toujours un point valide.
            return GetCentroid(polygon);
        }

        private static bool IsPointInPolygon(PointF p, List<PointF> polygon)
        {
            bool inside = false;
            int j = polygon.Count - 1;

            for (int i = 0; i < polygon.Count; i++)
            {
                PointF a = polygon[i], b = polygon[j];

                if ((a.Y > p.Y) != (b.Y > p.Y))
                {
                    float xCross = (b.X - a.X) * (p.Y - a.Y) / (b.Y - a.Y) + a.X;
                    if (p.X < xCross) inside = !inside;
                }

                j = i;
            }

            return inside;
        }
    }
}
