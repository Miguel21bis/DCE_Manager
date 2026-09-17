using System;
using System.Collections.Generic;
using System.Drawing;

namespace DCE_Manager
{
    // Propose les voisins d'une zone en mesurant la LONGUEUR DE FRONTIÈRE PARTAGÉE
    // entre deux contours, pas juste "est-ce qu'ils se touchent quelque part".
    //
    // Un simple test de proximité (un point de A à moins de 500 m d'un point de B)
    // laisse passer deux zones qui ne se touchent qu'à un coin - un carrefour à 4
    // zones, par exemple. Pour compter comme voisines, il faut au contraire
    // accumuler une vraie longueur de contour commun.
    //
    // Méthode : le contour de A est échantillonné tous les SampleStepMeters ; pour
    // chaque point d'échantillon, on regarde s'il est à moins de MaxGapMeters du
    // contour de B (la tolérance de tracé à la souris), et on additionne la portion
    // de contour correspondante. On fait le test dans les deux sens (A vers B et B
    // vers A, les deux polygones n'ayant pas forcément la même densité de points)
    // et on garde le plus grand des deux résultats.
    //
    // Seuils calibrés sur un jeu de 23 zones réel : les 47 paires qui se touchent
    // vraiment partagent toutes AU MOINS 600 m de frontière (la plus courte étant
    // CQ85_BASE / CQ97 à 620 m), la plupart largement plus. 300 m laisse une marge
    // confortable sous cette valeur tout en filtrant un contact de coin, qui
    // n'accumule que quelques dizaines de mètres.
    internal static class WargameNeighborDetector
    {
        // Tolérance de tracé : distance sous laquelle deux points de contours
        // différents sont considérés comme "le même endroit".
        public const double DefaultMaxGapMeters = 500.0;

        // Longueur de frontière commune minimale pour compter comme voisines.
        public const double DefaultMinSharedBorderMeters = 300.0;

        // Résolution de l'échantillonnage le long des contours. Plus petit = plus
        // précis mais plus de calcul ; 25 m est largement assez fin pour des zones
        // qui font plusieurs km de côté.
        private const double SampleStepMeters = 25.0;

        public static Dictionary<string, List<string>> DetectNeighbors(
            List<WargameZoneData> zones,
            double maxGapMeters = DefaultMaxGapMeters,
            double minSharedBorderMeters = DefaultMinSharedBorderMeters)
        {
            var result = new Dictionary<string, List<string>>();

            foreach (WargameZoneData zone in zones)
                result[zone.Id] = new List<string>();

            // Rectangle englobant de chaque zone, élargi de la tolérance de tracé :
            // sert à écarter la grande majorité des paires avant le test détaillé,
            // bien plus coûteux avec l'échantillonnage.
            var bounds = new Dictionary<string, RectangleF>();
            foreach (WargameZoneData zone in zones)
                bounds[zone.Id] = GetExpandedBounds(zone.DcsPoints, maxGapMeters);

            for (int i = 0; i < zones.Count; i++)
            {
                WargameZoneData zoneA = zones[i];
                if (zoneA.DcsPoints == null || zoneA.DcsPoints.Count < 3) continue;

                for (int j = i + 1; j < zones.Count; j++)
                {
                    WargameZoneData zoneB = zones[j];
                    if (zoneB.DcsPoints == null || zoneB.DcsPoints.Count < 3) continue;

                    if (!bounds[zoneA.Id].IntersectsWith(bounds[zoneB.Id]))
                        continue;

                    double sharedFromA = GetSharedBorderLength(zoneA.DcsPoints, zoneB.DcsPoints, maxGapMeters);
                    double sharedFromB = GetSharedBorderLength(zoneB.DcsPoints, zoneA.DcsPoints, maxGapMeters);
                    double shared = Math.Max(sharedFromA, sharedFromB);

                    if (shared >= minSharedBorderMeters)
                    {
                        result[zoneA.Id].Add(zoneB.Id);
                        result[zoneB.Id].Add(zoneA.Id);
                    }
                }
            }

            return result;
        }

        // Longueur du contour de "a" qui se trouve à moins de maxGap du contour de
        // "b". Echantillonnage au milieu de petits segments plutôt qu'aux sommets
        // seuls : un polygone à peu de points ne serait sinon quasiment jamais
        // détecté comme voisin (peu de sommets = peu de chances qu'un sommet tombe
        // près de l'autre contour).
        private static double GetSharedBorderLength(List<PointF> a, List<PointF> b, double maxGap)
        {
            double total = 0;
            int na = a.Count;

            for (int i = 0; i < na; i++)
            {
                PointF p1 = a[i];
                PointF p2 = a[(i + 1) % na];

                double segLength = Distance(p1, p2);
                if (segLength < 0.01) continue;

                int steps = Math.Max(1, (int)(segLength / SampleStepMeters));
                double stepLength = segLength / steps;

                for (int s = 0; s < steps; s++)
                {
                    double t = (s + 0.5) / steps;

                    var midPoint = new PointF(
                        p1.X + (p2.X - p1.X) * (float)t,
                        p1.Y + (p2.Y - p1.Y) * (float)t);

                    if (PointToPolygonDistance(midPoint, b) <= maxGap)
                        total += stepLength;
                }
            }

            return total;
        }

        // Distance minimale entre deux zones (bord à bord). Sert au panneau
        // d'édition pour filtrer la liste des candidats proposés au campaignMaker -
        // un usage différent du calcul de frontière partagée ci-dessus : ici on
        // veut juste "est-ce que ça vaut le coup de le montrer", pas trancher si
        // c'est un vrai voisin.
        public static double GetMinDistance(List<PointF> a, List<PointF> b)
        {
            if (a == null || b == null || a.Count < 2 || b.Count < 2)
                return double.MaxValue;

            double best = double.MaxValue;

            foreach (PointF p in a)
            {
                double d = PointToPolygonDistance(p, b);
                if (d < best) best = d;
            }

            foreach (PointF p in b)
            {
                double d = PointToPolygonDistance(p, a);
                if (d < best) best = d;
            }

            return best;
        }

        private static double PointToPolygonDistance(PointF p, List<PointF> polygon)
        {
            double best = double.MaxValue;
            int n = polygon.Count;

            for (int i = 0; i < n; i++)
            {
                double d = PointToSegmentDistance(p, polygon[i], polygon[(i + 1) % n]);
                if (d < best) best = d;
            }

            return best;
        }

        private static double PointToSegmentDistance(PointF p, PointF segA, PointF segB)
        {
            double dx = segB.X - segA.X;
            double dy = segB.Y - segA.Y;

            if (dx == 0 && dy == 0)
                return Distance(p, segA);

            double t = ((p.X - segA.X) * dx + (p.Y - segA.Y) * dy) / (dx * dx + dy * dy);
            if (t < 0) t = 0;
            if (t > 1) t = 1;

            double closestX = segA.X + t * dx;
            double closestY = segA.Y + t * dy;

            double resDx = p.X - closestX, resDy = p.Y - closestY;
            return Math.Sqrt(resDx * resDx + resDy * resDy);
        }

        private static double Distance(PointF a, PointF b)
        {
            double dx = a.X - b.X, dy = a.Y - b.Y;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        private static RectangleF GetExpandedBounds(List<PointF> points, double margin)
        {
            if (points == null || points.Count == 0)
                return RectangleF.Empty;

            float minX = points[0].X, maxX = points[0].X;
            float minY = points[0].Y, maxY = points[0].Y;

            foreach (PointF p in points)
            {
                if (p.X < minX) minX = p.X;
                if (p.X > maxX) maxX = p.X;
                if (p.Y < minY) minY = p.Y;
                if (p.Y > maxY) maxY = p.Y;
            }

            float m = (float)margin;
            return new RectangleF(minX - m, minY - m, (maxX - minX) + 2 * m, (maxY - minY) + 2 * m);
        }
    }
}
