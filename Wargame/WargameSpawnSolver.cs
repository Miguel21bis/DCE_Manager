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
    // Règle (voir échange avec Miguel, plusieurs itérations) :
    //
    //   1. tirer un point P au hasard DANS LA ZONE assignée
    //   2. P tombe-t-il dans un polygone INTERDIT (town/city/mountain/base) ?
    //        NON -> libre, le template garde sa forme d'origine, juste posé sur P
    //        OUI -> comparer deux candidats, garder le plus proche de P :
    //                 - le point de ROUTE le plus proche, À L'INTÉRIEUR de
    //                   (ce polygone interdit ∩ la zone)
    //                 - le point le plus proche À L'INTÉRIEUR de
    //                   (ce polygone interdit ∩ un polygone EXCEPTION qui le
    //                   chevauche ∩ la zone)
    //               aucun des deux -> tentative ratée, on retente un autre point
    //   3. dans TOUS les cas : si une seule unité placée tombe hors de la zone,
    //      toute la tentative est invalidée (un polygone route/exception peut
    //      déborder la zone assignée, le résultat final jamais).
    //
    // Les unités à moins de ClusterDistanceMeters les unes des autres (ex: une
    // DCA et son remblai) sont regroupées en paquets rigides. Un paquet ne se
    // déforme JAMAIS en interne - seule la distance ENTRE paquets peut être
    // resserrée (cas "polygone d'exception trop petit", voir PlaceInException).
    //
    // Pas de rotation pour l'instant.
    internal static class WargameSpawnSolver
    {
        public const double ClusterDistanceMeters = 10.0;
        private const int MaxAttempts = 50;

        // Combien de points on teste le long de chaque segment de rue pour
        // vérifier s'il traverse un polygone donné.
        private const int RoadSamplesPerSegment = 20;

        // Tolérance pour considérer qu'un point est "sur" une route (les routes
        // sont des lignes, pas des surfaces - une route à quelques mètres reste
        // praticable pour un véhicule).
        private const double RoadProximityMeters = 8.0;

        // Paliers de compression de la distance ENTRE paquets, quand ils ne
        // tiennent pas tels quels dans un polygone d'exception. Jamais en dessous
        // du plancher : mieux vaut échouer proprement (et le signaler) que produire
        // un entassement absurde en jeu.
        private static readonly double[] CompressionScales = { 1.0, 0.9, 0.8, 0.7, 0.6, 0.5, 0.4, 0.3 };

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
            spawnAreas = spawnAreas ?? new WargameSpawnAreas();

            for (int attempt = 0; attempt < MaxAttempts; attempt++)
            {
                PointF candidate = RandomPointInPolygon(zone.DcsPoints, rng);

                WargameSpawnPolygon forbidden = spawnAreas.FindAllPolygonsAt(candidate)
                    .FirstOrDefault(p => p.Policy == WargameSpawnPolicy.Forbidden);

                List<WargamePlacedUnit> result;

                if (forbidden == null)
                {
                    result = PlaceAsIs(templateUnits, candidate);
                }
                else
                {
                    result = PlaceInsideForbidden(clusters, candidate, forbidden, zone, spawnAreas);
                }

                if (result != null && AllInsideZone(result, zone.DcsPoints))
                    return result;
            }

            FormUtils.LogRegister("WargameSpawnSolver | aucune position valide trouvée pour la zone '"
                + zone.Id + "' après " + MaxAttempts + " tentatives");
            return null;
        }

        // Point dans un polygone interdit : compare route et exception, prend le
        // plus proche de candidate. Ne connaît rien d'autre que ce seul polygone
        // interdit - le point de départ est dedans, c'est lui qu'on traite.
        private static List<WargamePlacedUnit> PlaceInsideForbidden(List<List<WargameTemplateUnit>> clusters,
            PointF candidate, WargameSpawnPolygon forbidden, WargameZoneData zone, WargameSpawnAreas spawnAreas)
        {
            double searchCap = Math.Max(forbidden.Bounds.Width, forbidden.Bounds.Height);
            List<PointF> zonePoly = zone.DcsPoints;

            List<PointF> roadPoints; int roadSegmentIndex;
            PointF? roadPoint = FindNearestRoadPoint(candidate, forbidden, zonePoly, spawnAreas, searchCap,
                out roadPoints, out roadSegmentIndex);
            double roadDist = roadPoint.HasValue ? Distance(candidate, roadPoint.Value) : double.MaxValue;

            List<WargameSpawnPolygon> overlappingExceptions = spawnAreas.Polygons
                .Where(p => p.Tag == WargameSpawnTag.Exception && PolygonsOverlapApprox(p, forbidden))
                .ToList();

            WargameSpawnPolygon exceptionPoly;
            PointF? exceptionPoint = FindNearestExceptionPoint(candidate, forbidden, zonePoly, overlappingExceptions,
                searchCap, out exceptionPoly);
            double exceptionDist = exceptionPoint.HasValue ? Distance(candidate, exceptionPoint.Value) : double.MaxValue;

            if (roadPoint == null && exceptionPoint == null)
                return null;

            bool useRoad = roadPoint.HasValue && (!exceptionPoint.HasValue || roadDist <= exceptionDist);

            return useRoad
                ? PlaceAlongRoad(clusters, roadPoints, roadSegmentIndex, roadPoint.Value)
                : PlaceInException(clusters, exceptionPoly, exceptionPoint.Value);
        }

        // -------------------- Cas libre : translation simple --------------------

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

        // -------------------- Cas route : déroulé le long du tracé --------------------

        private static List<WargamePlacedUnit> PlaceAlongRoad(
            List<List<WargameTemplateUnit>> clusters, List<PointF> roadPoints, int startSegmentIndex, PointF startPoint)
        {
            List<(List<WargameTemplateUnit> units, PointF centroid)> ordered = OrderClustersAlongMainAxis(clusters);

            var result = new List<WargamePlacedUnit>();
            double walked = 0;

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

        // -------------------- Cas exception : rigide, sinon resserré --------------------

        // Essaie de poser les paquets tels quels (mêmes distances qu'à la
        // conception) autour de "anchor". S'ils ne tiennent pas tous dans le
        // polygone d'exception, resserre la distance ENTRE paquets par paliers -
        // jamais la disposition interne d'un paquet, qui doit rester identique
        // (ex: un véhicule et ses bidons de carburant gardent le même écart).
        private static List<WargamePlacedUnit> PlaceInException(
            List<List<WargameTemplateUnit>> clusters, WargameSpawnPolygon exceptionPoly, PointF anchor)
        {
            if (exceptionPoly == null) return null;

            PointF templateCentroid = GetCentroid(clusters.SelectMany(c => c).Select(u => u.Position));

            foreach (double scale in CompressionScales)
            {
                var placed = new List<WargamePlacedUnit>();
                bool allFit = true;

                foreach (List<WargameTemplateUnit> cluster in clusters)
                {
                    PointF clusterCentroid = GetCentroid(cluster.Select(u => u.Position));

                    float offsetX = (clusterCentroid.X - templateCentroid.X) * (float)scale;
                    float offsetY = (clusterCentroid.Y - templateCentroid.Y) * (float)scale;
                    var clusterAnchor = new PointF(anchor.X + offsetX, anchor.Y + offsetY);

                    float dx = clusterAnchor.X - clusterCentroid.X;
                    float dy = clusterAnchor.Y - clusterCentroid.Y;

                    foreach (WargameTemplateUnit u in cluster)
                    {
                        var pos = new PointF(u.Position.X + dx, u.Position.Y + dy);

                        if (!exceptionPoly.Contains(pos))
                            allFit = false;

                        placed.Add(new WargamePlacedUnit
                        {
                            Name = u.Name,
                            GroupName = u.GroupName,
                            Category = u.Category,
                            Position = pos,
                        });
                    }

                    if (!allFit) break;
                }

                if (allFit)
                    return placed;
            }

            return null; // même au plancher de compression, ça ne rentre pas
        }

        // -------------------- Recherche du point le plus proche --------------------

        // Recherche par cercles concentriques croissants autour de "center" : le
        // premier point qui satisfait le prédicat, au plus petit rayon, est
        // retenu comme "le plus proche" (à la résolution du pas près). Plafonnée
        // à maxRadius pour ne jamais scanner indéfiniment un polygone sans route
        // ni exception dedans.
        private static PointF? FindNearestPointSatisfying(PointF center, double maxRadius, Func<PointF, bool> predicate)
        {
            if (predicate(center))
                return center;

            double step = Math.Max(15.0, maxRadius / 60.0);

            for (double r = step; r <= maxRadius; r += step)
            {
                int samples = Math.Max(12, (int)(2 * Math.PI * r / step));

                for (int i = 0; i < samples; i++)
                {
                    double theta = 2 * Math.PI * i / samples;
                    var p = new PointF(
                        (float)(center.X + r * Math.Cos(theta)),
                        (float)(center.Y + r * Math.Sin(theta)));

                    if (predicate(p))
                        return p;
                }
            }

            return null;
        }

        private static PointF? FindNearestRoadPoint(PointF candidate, WargameSpawnPolygon forbidden, List<PointF> zonePoly,
            WargameSpawnAreas spawnAreas, double maxRadius, out List<PointF> roadPoints, out int segmentIndex)
        {
            List<PointF> foundRoadPoints = null;
            int foundSegmentIndex = -1;

            PointF? result = FindNearestPointSatisfying(candidate, maxRadius, p =>
            {
                if (!forbidden.Contains(p) || !IsPointInPolygon(p, zonePoly))
                    return false;

                foreach (WargameSpawnLine road in spawnAreas.Lines)
                {
                    for (int i = 0; i < road.Points.Count - 1; i++)
                    {
                        if (DistancePointToSegment(p, road.Points[i], road.Points[i + 1]) <= RoadProximityMeters)
                        {
                            foundRoadPoints = road.Points;
                            foundSegmentIndex = i;
                            return true;
                        }
                    }
                }

                return false;
            });

            roadPoints = foundRoadPoints;
            segmentIndex = foundSegmentIndex;
            return result;
        }

        private static PointF? FindNearestExceptionPoint(PointF candidate, WargameSpawnPolygon forbidden, List<PointF> zonePoly,
            List<WargameSpawnPolygon> candidates, double maxRadius, out WargameSpawnPolygon foundPolygon)
        {
            WargameSpawnPolygon matched = null;

            PointF? result = FindNearestPointSatisfying(candidate, maxRadius, p =>
            {
                if (!forbidden.Contains(p) || !IsPointInPolygon(p, zonePoly))
                    return false;

                foreach (WargameSpawnPolygon exc in candidates)
                {
                    if (exc.Contains(p))
                    {
                        matched = exc;
                        return true;
                    }
                }

                return false;
            });

            foundPolygon = matched;
            return result;
        }

        // Test d'approximation, pas une intersection géométrique exacte : suffisant
        // pour savoir si un polygone exception "sert à quelque chose" par rapport à
        // un polygone interdit donné.
        private static bool PolygonsOverlapApprox(WargameSpawnPolygon a, WargameSpawnPolygon b)
        {
            if (!a.Bounds.IntersectsWith(b.Bounds))
                return false;

            foreach (PointF p in a.Points) if (b.Contains(p)) return true;
            foreach (PointF p in b.Points) if (a.Contains(p)) return true;

            if (b.Contains(GetCentroid(a.Points))) return true;
            if (a.Contains(GetCentroid(b.Points))) return true;

            return false;
        }

        private static bool AllInsideZone(List<WargamePlacedUnit> placed, List<PointF> zonePoly)
        {
            foreach (WargamePlacedUnit u in placed)
            {
                if (!IsPointInPolygon(u.Position, zonePoly))
                    return false;
            }

            return true;
        }

        // -------------------- Regroupement des éléments proches --------------------

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

        private static double DistancePointToSegment(PointF p, PointF segA, PointF segB)
        {
            double dx = segB.X - segA.X, dy = segB.Y - segA.Y;

            if (dx == 0 && dy == 0)
                return Distance(p, segA);

            double t = ((p.X - segA.X) * dx + (p.Y - segA.Y) * dy) / (dx * dx + dy * dy);
            t = Math.Max(0, Math.Min(1, t));

            var closest = new PointF((float)(segA.X + t * dx), (float)(segA.Y + t * dy));
            return Distance(p, closest);
        }

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
