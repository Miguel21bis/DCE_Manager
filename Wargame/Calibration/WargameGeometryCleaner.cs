using System.Collections.Generic;
using System.Drawing;
using DCE_Manager.Utils;

namespace DCE_Manager
{
    // Rapproche les sommets de zones DIFFÉRENTES qui sont à moins de weldDistance
    // les uns des autres, et leur donne à tous la même position (leur moyenne).
    //
    // Sur un tracé à main levée dans l'éditeur DCS, un coin où 3 ou 4 zones se
    // touchent existe en réalité en 3 ou 4 exemplaires légèrement différents, un
    // par zone. Ça complique tout calcul qui suppose des frontières qui coïncident
    // vraiment (voisinage, rendu, plus tard une éventuelle carte de contrôle) et ça
    // se voit à l'écran comme un petit interstice entre deux zones qui devraient
    // se toucher.
    //
    // Ne fusionne JAMAIS deux sommets de la MÊME zone : son propre tracé, même fin,
    // doit rester tel quel. Sans cette règle, une frontière longue et finement
    // tracée (beaucoup de sommets rapprochés le long d'une même ligne) risquerait
    // de s'effondrer en chaîne en un seul point - exactement ce qu'on veut éviter.
    internal static class WargameGeometryCleaner
    {
        public const double DefaultWeldDistanceMeters = 100.0;

        // Au-delà de cette taille, un groupe de sommets à souder n'est
        // vraisemblablement plus un coin (2 à 4 zones qui se touchent) mais un
        // enchaînement le long d'une frontière commune mal détecté. On préfère
        // ignorer et loguer plutôt que déformer une zone en silence.
        private const int MaxClusterSize = 6;

        public static void WeldNearbyVertices(List<WargameZoneData> zones, double weldDistanceMeters = DefaultWeldDistanceMeters)
        {
            // Tous les sommets de toutes les zones, à plat, avec de quoi les
            // retrouver pour les réécrire ensuite.
            var allPoints = new List<(int zoneIndex, int pointIndex, PointF position)>();

            for (int z = 0; z < zones.Count; z++)
            {
                List<PointF> pts = zones[z].DcsPoints;
                if (pts == null) continue;

                for (int p = 0; p < pts.Count; p++)
                    allPoints.Add((z, p, pts[p]));
            }

            int n = allPoints.Count;
            if (n == 0) return;

            // Union-Find classique : regroupe tous les sommets qui se touchent de
            // proche en proche (transitif A-B et B-C -> A,B,C dans le même groupe).
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

            double weldSq = weldDistanceMeters * weldDistanceMeters;

            for (int i = 0; i < n; i++)
            {
                for (int j = i + 1; j < n; j++)
                {
                    if (allPoints[i].zoneIndex == allPoints[j].zoneIndex)
                        continue; // jamais 2 sommets de la même zone

                    double dx = allPoints[i].position.X - allPoints[j].position.X;
                    double dy = allPoints[i].position.Y - allPoints[j].position.Y;

                    if (dx * dx + dy * dy <= weldSq)
                        Union(i, j);
                }
            }

            // Regroupe les sommets par cluster (racine Union-Find commune)
            var clusters = new Dictionary<int, List<int>>();
            for (int i = 0; i < n; i++)
            {
                int root = Find(i);
                if (!clusters.TryGetValue(root, out List<int> list))
                {
                    list = new List<int>();
                    clusters[root] = list;
                }
                list.Add(i);
            }

            int weldedCount = 0;
            int skippedCount = 0;

            foreach (List<int> cluster in clusters.Values)
            {
                if (cluster.Count < 2)
                    continue; // sommet isolé, rien à souder

                if (cluster.Count > MaxClusterSize)
                {
                    skippedCount++;
                    continue;
                }

                float sumX = 0, sumY = 0;
                foreach (int idx in cluster)
                {
                    sumX += allPoints[idx].position.X;
                    sumY += allPoints[idx].position.Y;
                }

                var average = new PointF(sumX / cluster.Count, sumY / cluster.Count);

                foreach (int idx in cluster)
                {
                    (int zoneIndex, int pointIndex, PointF _) = allPoints[idx];
                    zones[zoneIndex].DcsPoints[pointIndex] = average;
                }

                weldedCount += cluster.Count;
            }

            if (weldedCount > 0)
            {
                FormUtils.LogRegister("WargameGeometryCleaner | " + weldedCount + " sommet(s) soudé(s) (tolérance "
                    + (int)weldDistanceMeters + " m)");
            }

            if (skippedCount > 0)
            {
                FormUtils.LogRegister("WargameGeometryCleaner | " + skippedCount + " groupe(s) de sommets ignoré(s) "
                    + "(plus de " + MaxClusterSize + " sommets rapprochés - probablement un enchaînement le long "
                    + "d'une frontière plutôt qu'un coin, tracé à vérifier si besoin)");
            }
        }
    }
}
