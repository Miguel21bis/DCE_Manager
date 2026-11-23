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
    // Partie de Map_Web (voir Map_Web.cs) : mise en correspondance avec les vraies pistes
    internal static partial class Map_Web
    {
        // Pour chaque base, choisit comme référence réelle le point de piste le plus proche du point DCS (milieu ou bout d'une piste).
        // matches : bases avec ICAO (on ne regarde que les pistes de leur aéroport, sinon on garde le point de l'aéroport).
        // noIcao : bases sans ICAO (on regarde toutes les pistes à moins de 1 km) ; celles qui trouvent une piste sont ajoutées à matches.
        // Renvoie aussi la mesure de la règle de position des pistes de db_airbases (texte, vide si impossible)
        private static string RefineWithRunways(string campaign, List<Map_Match> matches, List<Map_Match> noIcao, IProgress<string> progress, out string anchorRule)
        {
            anchorRule = "";
            if (matches.Count == 0 && noIcao.Count == 0) return "";
            if (!Map_RunwayData.EnsureFile(progress)) return "";

            List<Map_RunwayData.PubRunway> all = Map_RunwayData.All();
            if (all.Count == 0) return "";

            // Première projection : celle du théâtre si elle existe et colle, sinon le calage sur les points d'aéroport
            string theatre;
            Map_Projection known = KnownProjection(campaign, out theatre);
            Map_Projection p = known;
            bool fitted = false;
            if (matches.Count >= 3)
            {
                Map_Projection fit = FitRobust(matches, new List<Map_Match>());
                if (known == null) { p = fit; fitted = true; }
                else
                {
                    double eFit = Median(matches.Select(m => ErrorOf(fit, m)));
                    double eKnown = Median(matches.Select(m => ErrorOf(known, m)));
                    if (eKnown > eFit * 1.25 + 150) { p = fit; fitted = true; }
                }
            }
            if (p == null) return "";

            progress.Report("Matching the airbases to the real runways...");
            var byIdent = new Dictionary<string, List<Map_RunwayData.PubRunway>>(StringComparer.OrdinalIgnoreCase);
            foreach (Map_RunwayData.PubRunway r in all)
            {
                if (r.Ident == "") continue;
                List<Map_RunwayData.PubRunway> l;
                if (!byIdent.TryGetValue(r.Ident, out l)) byIdent[r.Ident] = l = new List<Map_RunwayData.PubRunway>();
                l.Add(r);
            }

            var extra = new List<Map_Match>();
            for (int pass = 0; pass < (fitted ? 2 : 1); pass++)
            {
                foreach (Map_Match m in matches)
                {
                    List<Map_RunwayData.PubRunway> l;
                    byIdent.TryGetValue(m.Icao, out l);
                    if (!PickReference(p, m, l, 1500))
                    {
                        m.Lat = m.ArpLat; m.Lon = m.ArpLon; m.Ref = "airport point";
                    }
                }

                extra.Clear();
                foreach (Map_Match m in noIcao)
                {
                    double lat, lon;
                    DcsToLatLon(p, m.X, m.Y, out lat, out lon);
                    var near = new List<Map_RunwayData.PubRunway>();
                    foreach (Map_RunwayData.PubRunway r in all)
                        if (Math.Abs((r.LatA + r.LatB) / 2 - lat) < 0.05 && Math.Abs((r.LonA + r.LonB) / 2 - lon) < 0.07) near.Add(r);
                    if (near.Count > 0 && PickReference(p, m, near, 1000)) extra.Add(m);
                }

                if (fitted)
                {
                    var pool = new List<Map_Match>(matches);
                    pool.AddRange(extra);
                    if (pool.Count >= 3) p = FitRobust(pool, new List<Map_Match>());
                }
            }
            matches.AddRange(extra);

            return MeasureAnchor(p, matches, byIdent, out anchorRule);
        }

        // Où se trouve le point (x, y) des pistes de db_airbases sur la vraie piste ? (milieu, bout est, bout ouest, début de chaque piste)
        // Pour chaque point (piste de db_airbases ; à défaut le point de la base), on cherche la vraie piste de même cap la plus proche (moins de 3 km),
        // et on mesure la position le long de la piste : -1 = bout ouest, 0 = milieu, +1 = bout est (nord / sud si la piste va du nord au sud).
        // 2e mesure : dans le sens du cap de la piste de db_airbases : -1 = le point est au début de la piste (seuil), +1 = à la fin.
        private static string MeasureAnchor(Map_Projection p, List<Map_Match> matches, Dictionary<string, List<Map_RunwayData.PubRunway>> byIdent, out string rule)
        {
            rule = "";
            var east = new List<double>();
            var own = new List<double>();

            foreach (Map_Match m in matches)
            {
                List<Map_RunwayData.PubRunway> real;
                if (string.IsNullOrEmpty(m.Icao) || !byIdent.TryGetValue(m.Icao, out real)) continue;

                // Vraies pistes en coordonnées DCS : milieu (x, y), direction unitaire vers l'est (nord, est), demi-longueur
                var rr = new List<double[]>();
                foreach (Map_RunwayData.PubRunway r in real)
                {
                    double ax, ay, bx, by;
                    LatLonToDcs(p, r.LatA, r.LonA, out ax, out ay);
                    LatLonToDcs(p, r.LatB, r.LonB, out bx, out by);
                    double len = Math.Sqrt((bx - ax) * (bx - ax) + (by - ay) * (by - ay));
                    if (len < 200) continue;

                    double un = (bx - ax) / len, ue = (by - ay) / len;
                    if (ue < 0 || (Math.Abs(ue) < 1e-9 && un < 0)) { un = -un; ue = -ue; }
                    rr.Add(new[] { (ax + bx) / 2, (ay + by) / 2, un, ue, len / 2 });
                }
                if (rr.Count == 0) continue;

                // Points à mesurer : x, y, cap dans db_airbases (NaN = c'est le point de la base)
                var pts = new List<double[]>();
                if (m.DbRunways != null)
                    foreach (Map_Runway e in m.DbRunways)
                        pts.Add(new[] { (e.X == 0 && e.Y == 0) ? m.X : e.X, (e.X == 0 && e.Y == 0) ? m.Y : e.Y, e.Hdg });
                if (pts.Count == 0) pts.Add(new[] { m.X, m.Y, double.NaN });

                foreach (double[] pt in pts)
                {
                    double[] best = null;
                    double bestD = 3000;
                    foreach (double[] r in rr)
                    {
                        double d = Math.Sqrt((pt[0] - r[0]) * (pt[0] - r[0]) + (pt[1] - r[1]) * (pt[1] - r[1]));
                        if (d >= bestD) continue;

                        if (!double.IsNaN(pt[2]))
                        {
                            double hr = Math.Atan2(r[3], r[2]) * 180.0 / Math.PI;           // cap de la vraie piste (0 a 180)
                            double dh = Math.Abs((((pt[2] - hr) % 180) + 180) % 180);
                            if (dh > 90) dh = 180 - dh;
                            if (dh > 20) continue;
                        }
                        best = r;
                        bestD = d;
                    }
                    if (best == null) continue;

                    double dn = pt[0] - best[0], de = pt[1] - best[1];
                    east.Add((dn * best[2] + de * best[3]) / best[4]);
                    if (!double.IsNaN(pt[2]))
                    {
                        double a = pt[2] * Math.PI / 180.0;
                        own.Add((dn * Math.Cos(a) + de * Math.Sin(a)) / best[4]);
                    }
                }
            }
            if (east.Count < 3) return "";

            double mE = Median(east);
            double mO = own.Count >= 3 ? Median(own) : double.NaN;

            string verdict;
            if (Math.Abs(mE) < 0.3 && (double.IsNaN(mO) || Math.Abs(mO) < 0.3)) { verdict = "in the middle of the runway"; rule = "middle"; }
            else if (!double.IsNaN(mO) && mO < -0.7) { verdict = "at the start of each runway (threshold)"; rule = "start"; }
            else if (mE > 0.7) { verdict = "at the east end of the runway"; rule = "east"; }
            else if (mE < -0.7) { verdict = "at the west end of the runway"; rule = "west"; }
            else verdict = "no clear rule (mixed)";

            CultureInfo c = CultureInfo.InvariantCulture;
            return "Measured on " + east.Count + " points of db_airbases against the real runways: median position " + (mE * 100).ToString("+0;-0;0", c)
                + " % (-100 = west end, 0 = middle, +100 = east end)"
                + (double.IsNaN(mO) ? "" : "; along the heading of the runway " + (mO * 100).ToString("+0;-0;0", c) + " % (-100 = start of the runway)")
                + ". It looks like the point is " + verdict + ".";
        }

        // Milieu ou bout de piste le plus proche du point DCS de la base (dans la limite de maxDist mètres) ; met à jour m.Lat / m.Lon / m.Ref
        private static bool PickReference(Map_Projection p, Map_Match m, List<Map_RunwayData.PubRunway> runways, double maxDist)
        {
            if (runways == null) return false;

            double best = double.MaxValue, bLat = 0, bLon = 0;
            string kind = "";
            foreach (Map_RunwayData.PubRunway r in runways)
            {
                double[,] pts =
                {
                    { r.LatA, r.LonA, 1 },
                    { r.LatB, r.LonB, 1 },
                    { (r.LatA + r.LatB) / 2, (r.LonA + r.LonB) / 2, 0 }
                };
                for (int i = 0; i < 3; i++)
                {
                    double x, y;
                    LatLonToDcs(p, pts[i, 0], pts[i, 1], out x, out y);
                    double d = Math.Sqrt((x - m.X) * (x - m.X) + (y - m.Y) * (y - m.Y));
                    if (d < best) { best = d; bLat = pts[i, 0]; bLon = pts[i, 1]; kind = pts[i, 2] > 0 ? "runway end" : "runway middle"; }
                }
            }
            if (best > maxDist) return false;

            m.Lat = bLat; m.Lon = bLon; m.Ref = kind;
            return true;
        }

        private static double Median(IEnumerable<double> values)
        {
            List<double> l = values.OrderBy(v => v).ToList();
            return l.Count == 0 ? 0 : l[l.Count / 2];
        }
    }
}
