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
        public double Heading;
    }

    // Trouve où poser un template dans une zone, et calcule la position de
    // chaque unité qui le compose.
    //
    // Un template EST une formation dans son intégralité, même s'il est composé
    // de plusieurs groupes DCS hétérogènes (nations, véhicules, statiques...) -
    // c'est un artefact de l'architecture DCS, pas une indication qu'il faudrait
    // le scinder. Un template peut donc être très étalé (parfois plus d'1 km).
    // Au script de s'adapter pour qu'il rentre dans la zone assignée, ou le long
    // d'une route : c'est ce que fait la compression décrite plus bas.
    //
    // Règle de placement :
    //
    //   1. tirer un point P au hasard DANS LA ZONE assignée (ou dans le secteur
    //      angulaire assigné à cette formation si plusieurs se partagent la zone)
    //   2. P tombe-t-il dans un polygone INTERDIT (town/city/mountain/base) ?
    //        NON -> "libre" : le template est posé sur P, comprimé si besoin
    //               pour tenir entièrement dans la zone (voir PlaceCompressed)
    //        OUI -> comparer deux candidats, garder le plus proche de P :
    //                 - le point de ROUTE le plus proche, à l'intérieur de
    //                   (ce polygone interdit ∩ la zone), déroulé le long du
    //                   tracé, comprimé si la route est trop courte
    //                 - le point le plus proche à l'intérieur de
    //                   (ce polygone interdit ∩ un polygone EXCEPTION qui le
    //                   chevauche ∩ la zone), comprimé si le polygone est trop
    //                   petit
    //               aucun des deux -> tentative ratée, on retente un autre point
    //   3. dans TOUS les cas : si une seule unité placée tombe hors de la zone,
    //      ou trop près de son bord, toute la tentative est invalidée.
    //   4. une fois la position acceptée, tout le paquet est orienté en bloc
    //      (position ET heading de chaque unité) : le long de la route si le
    //      placement s'est fait sur une route, sinon face à la zone ennemie la
    //      plus proche (voir ApplyRotation).
    //
    // La compression ne resserre JAMAIS la disposition interne d'un paquet -
    // seule la distance ENTRE paquets peut être réduite, par paliers, jusqu'à un
    // plancher. Au-delà, la tentative est abandonnée plutôt que de produire un
    // entassement.
    //
    // Les unités à moins de ClusterDistanceMeters les unes des autres (ex: une
    // DCA et son remblai, ou une batterie entière très compacte) sont regroupées
    // en paquets rigides.
    internal static class WargameSpawnSolver
    {
        // Calibré sur un template réel : toutes les vraies paires "protection
        // rapprochée" (véhicule + sac de sable, unités d'un même petit groupe)
        // sont à moins de 6 m ; la paire indépendante suivante la plus proche
        // saute directement à 40 m. 8 m laisse une marge confortable sans
        // risquer d'attraper autre chose.
        public const double ClusterDistanceMeters = 8.0;

        // Calibré large : le seuil de distance ci-dessus suffit déjà à séparer
        // les vraies paires "protection rapprochée" du reste - un vrai groupe
        // compact (ex: une batterie Hawk à 7 membres, un peloton à 5 chars) doit
        // pouvoir rester un seul bloc rigide. Ce plafond n'est plus qu'un filet
        // de sécurité contre un enchaînement vraiment anormal, pas un premier
        // recours - il ne devrait quasiment jamais se déclencher en pratique.
        private const int MaxClusterSize = 20;

        private const int MaxAttempts = 50;
        private const int RoadSamplesPerSegment = 20;
        private const double RoadProximityMeters = 8.0;

        // Marge de sécurité vis-à-vis du bord de la zone : une unité peut être
        // techniquement "dedans" tout en semblant déborder sur la carte à
        // cause de la taille de son icône. À ajuster selon le rendu réel.
        private const double ZoneEdgeMarginMeters = 50.0;

        // Paliers de compression de la distance ENTRE paquets (jamais à
        // l'intérieur d'un paquet), utilisés pour les trois cas de placement.
        private static readonly double[] CompressionScales = { 1.0, 0.9, 0.8, 0.7, 0.6, 0.5, 0.4, 0.3 };

        // threatPoint : point vers lequel orienter la formation si elle n'est
        // pas posée sur une route (typiquement le centre de la zone ennemie la
        // plus proche - voir Saver_TargetList_Wargame.FindNearestEnemyZoneCenter).
        // null = pas de rotation appliquée dans ce cas (heading d'origine du
        // template conservé).
        //
        // sectorIndex/sectorCount : voir la Phase 1 ci-dessous.
        public static List<WargamePlacedUnit> PlaceTemplate(
            WargameZoneData zone, List<WargameTemplateUnit> templateUnits, WargameSpawnAreas spawnAreas, Random rng,
            PointF? threatPoint = null, int sectorIndex = 0, int sectorCount = 1)
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

            // Phase 1 (optionnelle) : si plusieurs formations se partagent la
            // zone, on tente d'abord de rester dans le secteur angulaire qui
            // lui est assigné (la zone est divisée en "parts de tarte" autour
            // de son centroïde, une part par formation), pour éviter qu'elles
            // ne s'agglutinent toutes au hasard au même endroit.
            if (sectorCount > 1)
            {
                PointF centroid = GetCentroid(zone.DcsPoints);
                double sectorSpan = 2 * Math.PI / sectorCount;
                double sectorStart = sectorIndex * sectorSpan;
                double sectorEnd = sectorStart + sectorSpan;

                for (int attempt = 0; attempt < MaxAttempts; attempt++)
                {
                    PointF? candidate = RandomPointInPolygonSector(zone.DcsPoints, rng, centroid, sectorStart, sectorEnd);
                    if (candidate == null) continue;

                    List<WargamePlacedUnit> sectorResult = TryPlaceAt(candidate.Value, clusters, zone, spawnAreas, threatPoint);
                    if (sectorResult != null)
                        return sectorResult;
                }
            }

            // Phase 2 : secteur infructueux (ou une seule formation dans la
            // zone) -> on cherche n'importe où dans la zone entière, comme avant.
            for (int attempt = 0; attempt < MaxAttempts; attempt++)
            {
                PointF candidate = RandomPointInPolygon(zone.DcsPoints, rng);

                List<WargamePlacedUnit> result = TryPlaceAt(candidate, clusters, zone, spawnAreas, threatPoint);
                if (result != null)
                    return result;
            }

            int attemptsTried = sectorCount > 1 ? MaxAttempts * 2 : MaxAttempts;
            FormUtils.LogRegister("WargameSpawnSolver | aucune position valide trouvée pour la zone '"
                + zone.Id + "' après " + attemptsTried + " tentatives");
            return null;
        }

        // Factorise ce que faisait le corps de la boucle d'origine : résout un
        // point candidat (libre ou dans une zone interdite), applique
        // l'orientation qui va avec, et valide que tout tombe bien dans la zone.
        private static List<WargamePlacedUnit> TryPlaceAt(PointF candidate, List<List<WargameTemplateUnit>> clusters,
            WargameZoneData zone, WargameSpawnAreas spawnAreas, PointF? threatPoint)
        {
            WargameSpawnPolygon forbidden = spawnAreas.FindAllPolygonsAt(candidate)
                .FirstOrDefault(p => p.Policy == WargameSpawnPolicy.Forbidden);

            List<WargamePlacedUnit> result;

            if (forbidden == null)
            {
                result = PlaceCompressed(clusters, candidate, p => IsPointInPolygon(p, zone.DcsPoints));
                double desiredHeading = threatPoint.HasValue ? Bearing(candidate, threatPoint.Value) : 0;
                result = ApplyRotation(result, desiredHeading);
            }
            else
            {
                result = PlaceInsideForbidden(clusters, candidate, forbidden, zone, spawnAreas, threatPoint);
            }

            return (result != null && AllInsideZone(result, zone.DcsPoints)) ? result : null;
        }

        private static List<WargamePlacedUnit> PlaceInsideForbidden(List<List<WargameTemplateUnit>> clusters,
            PointF candidate, WargameSpawnPolygon forbidden, WargameZoneData zone, WargameSpawnAreas spawnAreas,
            PointF? threatPoint)
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

            if (useRoad)
            {
                List<WargamePlacedUnit> result = PlaceAlongRoad(clusters, roadPoints, roadSegmentIndex, roadPoint.Value);
                double roadHeading = Bearing(roadPoints[roadSegmentIndex], roadPoints[roadSegmentIndex + 1]);
                return ApplyRotation(result, roadHeading);
            }
            else
            {
                List<WargamePlacedUnit> result = PlaceCompressed(clusters, exceptionPoint.Value, p => exceptionPoly.Contains(p));
                double desiredHeading = threatPoint.HasValue ? Bearing(exceptionPoint.Value, threatPoint.Value) : 0;
                return ApplyRotation(result, desiredHeading);
            }
        }

        // -------------------- Placement radial comprimé (libre / exception) --------------------

        // Essaie de poser les paquets tels quels (mêmes distances qu'à la
        // conception) autour de "anchor". Si tous ne tiennent pas dans la zone
        // autorisée (isInsideAllowedArea), resserre la distance ENTRE paquets par
        // paliers - jamais la disposition interne d'un paquet.
        private static List<WargamePlacedUnit> PlaceCompressed(
            List<List<WargameTemplateUnit>> clusters, PointF anchor, Func<PointF, bool> isInsideAllowedArea)
        {
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

                        if (!isInsideAllowedArea(pos))
                            allFit = false;

                        placed.Add(new WargamePlacedUnit
                        {
                            Name = u.Name,
                            GroupName = u.GroupName,
                            Category = u.Category,
                            Position = pos,
                            Heading = u.Heading,
                        });
                    }

                    if (!allFit) break;
                }

                if (allFit)
                    return placed;
            }

            return null; // même comprimé au plancher, ça ne rentre pas
        }

        // -------------------- Cas route : déroulé le long du tracé --------------------

        // Comme PlaceCompressed, mais le long d'un chemin 1D plutôt qu'autour d'un
        // point : la distance parcourue entre paquets consécutifs est comprimée
        // par le même jeu de paliers si la route disponible est trop courte pour
        // accueillir le template à sa taille d'origine.
        private static List<WargamePlacedUnit> PlaceAlongRoad(
            List<List<WargameTemplateUnit>> clusters, List<PointF> roadPoints, int startSegmentIndex, PointF startPoint)
        {
            List<(List<WargameTemplateUnit> units, PointF centroid)> ordered = OrderClustersAlongMainAxis(clusters);

            foreach (double scale in CompressionScales)
            {
                var result = new List<WargamePlacedUnit>();
                double walked = 0;
                bool ranOutOfRoad = false;

                for (int i = 0; i < ordered.Count; i++)
                {
                    if (i > 0)
                        walked += Distance(ordered[i - 1].centroid, ordered[i].centroid) * scale;

                    bool truncated;
                    PointF anchor = WalkAlongPolyline(roadPoints, startSegmentIndex, startPoint, walked, out truncated);
                    if (truncated) ranOutOfRoad = true;

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
                            Heading = u.Heading,
                        });
                    }
                }

                if (!ranOutOfRoad)
                    return result;
            }

            FormUtils.LogRegister("WargameSpawnSolver | route trop courte pour ce template même comprimé au plancher");
            return null;
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

        // truncated = true si la distance demandée dépasse la fin de la
        // polyligne - l'appelant décide quoi en faire (ici : essayer une
        // compression plus forte plutôt que d'accepter un empilement en bout de
        // route).
        private static PointF WalkAlongPolyline(List<PointF> points, int startSegmentIndex, PointF startPoint,
            double distance, out bool truncated)
        {
            truncated = false;
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
                truncated = true;

            return current;
        }

        // -------------------- Orientation (menace / route) --------------------

        // Tourne tout le paquet d'unités déjà placé (positions ET headings) en
        // bloc autour de son propre centroïde, de "rotationDeltaDegrees" degrés.
        // On part du principe que le template a été construit face au nord
        // (heading 0) - le delta demandé EST donc directement le cap final visé.
        private static List<WargamePlacedUnit> ApplyRotation(List<WargamePlacedUnit> placed, double rotationDeltaDegrees)
        {
            if (placed == null || rotationDeltaDegrees == 0)
                return placed;

            PointF pivot = GetCentroid(placed.Select(u => u.Position));
            double rad = rotationDeltaDegrees * Math.PI / 180.0;
            double cos = Math.Cos(rad), sin = Math.Sin(rad);

            foreach (WargamePlacedUnit u in placed)
            {
                float dx = u.Position.X - pivot.X;
                float dy = u.Position.Y - pivot.Y;

                u.Position = new PointF(
                    pivot.X + (float)(dx * cos - dy * sin),
                    pivot.Y + (float)(dx * sin + dy * cos));

                u.Heading = NormalizeAngle(u.Heading + rotationDeltaDegrees);
            }

            return placed;
        }

        // Cap depuis "from" vers "to", même convention que côté ScriptsMod
        // (UTIL_Functions.GetHeadingDegre : 0° = +X, 90° = +Y).
        private static double Bearing(PointF from, PointF to)
        {
            double dx = to.X - from.X, dy = to.Y - from.Y;
            return NormalizeAngle(Math.Atan2(dy, dx) * 180.0 / Math.PI);
        }

        private static double NormalizeAngle(double degrees)
        {
            degrees %= 360;
            if (degrees < 0) degrees += 360;
            return degrees;
        }

        // -------------------- Recherche du point le plus proche --------------------

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
                if (!IsPointInPolygonWithMargin(u.Position, zonePoly, ZoneEdgeMarginMeters))
                    return false;
            }

            return true;
        }

        // Comme IsPointInPolygon, mais rejette aussi un point trop proche du
        // bord (même techniquement "dedans") : évite qu'une unité affichée
        // sur la carte ne semble déborder visuellement de la zone.
        private static bool IsPointInPolygonWithMargin(PointF p, List<PointF> polygon, double marginMeters)
        {
            if (!IsPointInPolygon(p, polygon))
                return false;

            int j = polygon.Count - 1;
            for (int i = 0; i < polygon.Count; i++)
            {
                if (DistancePointToSegment(p, polygon[i], polygon[j]) < marginMeters)
                    return false;

                j = i;
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

            List<List<WargameTemplateUnit>> result = clusters.Values.ToList();

            var final = new List<List<WargameTemplateUnit>>();
            int exploded = 0;

            foreach (List<WargameTemplateUnit> cluster in result)
            {
                if (cluster.Count <= MaxClusterSize)
                {
                    final.Add(cluster);
                }
                else
                {
                    foreach (WargameTemplateUnit u in cluster)
                        final.Add(new List<WargameTemplateUnit> { u });

                    exploded++;
                }
            }

            if (exploded > 0)
            {
                FormUtils.LogRegister("WargameSpawnSolver | " + exploded + " paquet(s) anormalement grand(s) "
                    + "(> " + MaxClusterSize + " unités, tracé du template à vérifier) éclaté(s) en unités isolées");
            }

            return final;
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

        // Comme RandomPointInPolygon, mais ne garde le point tiré que si son
        // angle depuis "centroid" tombe dans le secteur assigné à la formation.
        // Ne garantit pas des parts de surface rigoureusement égales si la zone
        // est très irrégulière ou allongée, mais suffit à répartir les
        // formations sans avoir à découper réellement le polygone. Retourne
        // null si le point tiré ne tombe pas dans le secteur (l'appelant retente).
        private static PointF? RandomPointInPolygonSector(List<PointF> polygon, Random rng, PointF centroid,
            double sectorStart, double sectorEnd)
        {
            float minX = polygon.Min(p => p.X), maxX = polygon.Max(p => p.X);
            float minY = polygon.Min(p => p.Y), maxY = polygon.Max(p => p.Y);

            var pt = new PointF(
                minX + (float)(rng.NextDouble() * (maxX - minX)),
                minY + (float)(rng.NextDouble() * (maxY - minY)));

            if (!IsPointInPolygon(pt, polygon))
                return null;

            double angle = Math.Atan2(pt.Y - centroid.Y, pt.X - centroid.X);
            if (angle < 0) angle += 2 * Math.PI;

            return (angle >= sectorStart && angle < sectorEnd) ? (PointF?)pt : null;
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
