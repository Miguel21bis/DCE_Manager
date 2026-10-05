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
    // chaque point d'échantillon P, on cherche le point Q le plus proche du contour
    // de B. P compte comme "frontière partagée" seulement si :
    //   1. Q est à moins de MaxGapMeters (la tolérance de tracé à la souris) ;
    //   2. le contact est RÉCIPROQUE : le point de A le plus proche de Q doit être
    //      (à peu près) P lui-même. Deux bords qui longent l'un l'autre se
    //      répondent point à point. A un contact de coin, ce n'est pas le cas : tous
    //      les points des deux côtés du coin ont le même Q (le coin), dont le point
    //      le plus proche sur A est le coin de A, pas P.
    // La condition 2 est indispensable : sans elle, avec 500 m de tolérance, un
    // simple coin accumule ~500 m sur CHAQUE côté et passait pour une frontière.
    //
    // On fait le test dans les deux sens (A vers B et B vers A, les deux polygones
    // n'ayant pas forcément la même densité de points) et on garde le plus grand
    // des deux résultats.
    //
    // Seuils calibrés sur un jeu de 23 zones réel : les 47 paires qui se touchent
    // vraiment partagent toutes AU MOINS 600 m de frontière (la plus courte étant
    // CQ85_BASE / CQ97 à 620 m), la plupart largement plus. 300 m laisse une marge
    // confortable sous cette valeur ; un contact de coin n'accumule plus que ~150 m
    // au pire (voir MutualSlackMeters).
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

        // Marge du test de réciprocité : le point de A le plus proche de Q peut être à
        // la moitié de la distance P-Q, plus cette marge, de P. La marge absorbe les
        // contours très détaillés (côtes), où le "plus proche" saute de quelques mètres
        // d'un sommet à l'autre. Plus elle est grande, plus un coin accumule de longueur
        // (environ 2 x 2 x marge) : 40 m donne ~160 m au pire, sous le seuil de 300 m.
        private const double MutualSlackMeters = 40.0;

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

        // Longueur du contour de "a" qui longe réellement le contour de "b" (voir le
        // commentaire de la classe : proximité ET contact réciproque). Echantillonnage
        // au milieu de petits segments plutôt qu'aux sommets seuls : un polygone à peu
        // de points ne serait sinon quasiment jamais détecté comme voisin.
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

                    double distToB;
                    PointF q = NearestPointOnPolygon(midPoint, b, out distToB);

                    if (distToB > maxGap)
                        continue;

                    // Contact réciproque : en revenant de Q vers le contour de A, on doit
                    // retomber près du point de départ. Au coin de deux zones, on tombe
                    // sur le coin de A, loin de P.
                    double backDist;
                    PointF back = NearestPointOnPolygon(q, a, out backDist);

                    if (Distance(midPoint, back) <= 0.5 * distToB + MutualSlackMeters)
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
            double distance;
            NearestPointOnPolygon(p, polygon, out distance);
            return distance;
        }

        // Point du contour du polygone le plus proche de p, et la distance correspondante.
        private static PointF NearestPointOnPolygon(PointF p, List<PointF> polygon, out double bestDistance)
        {
            bestDistance = double.MaxValue;
            PointF bestPoint = p;
            int n = polygon.Count;

            for (int i = 0; i < n; i++)
            {
                PointF segA = polygon[i];
                PointF segB = polygon[(i + 1) % n];

                PointF closest = ClosestPointOnSegment(p, segA, segB);
                double d = Distance(p, closest);

                if (d < bestDistance)
                {
                    bestDistance = d;
                    bestPoint = closest;
                }
            }

            return bestPoint;
        }

        private static PointF ClosestPointOnSegment(PointF p, PointF segA, PointF segB)
        {
            double dx = segB.X - segA.X;
            double dy = segB.Y - segA.Y;

            if (dx == 0 && dy == 0)
                return segA;

            double t = ((p.X - segA.X) * dx + (p.Y - segA.Y) * dy) / (dx * dx + dy * dy);
            if (t < 0) t = 0;
            if (t > 1) t = 1;

            return new PointF((float)(segA.X + t * dx), (float)(segA.Y + t * dy));
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
