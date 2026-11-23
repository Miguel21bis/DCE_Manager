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
    // Partie de Map_Web (voir Map_Web.cs) : calage, projection Transverse Mercator
    internal static partial class Map_Web
    {
        // Easting / Northing (sans décalage) d'un point lat/lon pour un méridien central donné
        private static void Tm(double latDeg, double lonDeg, double cmDeg, out double easting, out double northing)
        {
            const double a = 6378137.0;
            const double f = 1 / 298.257223563;
            double e2 = f * (2 - f);
            double ep2 = e2 / (1 - e2);

            double phi = latDeg * Math.PI / 180;
            double lam = (lonDeg - cmDeg) * Math.PI / 180;

            double sin = Math.Sin(phi), cos = Math.Cos(phi), tan = Math.Tan(phi);
            double N = a / Math.Sqrt(1 - e2 * sin * sin);
            double T = tan * tan;
            double C = ep2 * cos * cos;
            double A = lam * cos;

            double e4 = e2 * e2, e6 = e4 * e2;
            double M = a * ((1 - e2 / 4 - 3 * e4 / 64 - 5 * e6 / 256) * phi
                          - (3 * e2 / 8 + 3 * e4 / 32 + 45 * e6 / 1024) * Math.Sin(2 * phi)
                          + (15 * e4 / 256 + 45 * e6 / 1024) * Math.Sin(4 * phi)
                          - (35 * e6 / 3072) * Math.Sin(6 * phi));

            easting = K0 * N * (A + (1 - T + C) * Math.Pow(A, 3) / 6
                              + (5 - 18 * T + T * T + 72 * C - 58 * ep2) * Math.Pow(A, 5) / 120);
            northing = K0 * (M + N * tan * (A * A / 2 + (5 - T + 9 * C + 4 * C * C) * Math.Pow(A, 4) / 24
                              + (61 - 58 * T + T * T + 600 * C - 330 * ep2) * Math.Pow(A, 6) / 720));
        }

        public static void LatLonToDcs(Map_Projection p, double lat, double lon, out double x, out double y)
        {
            double e, n;
            Tm(lat, lon, p.Cm, out e, out n);
            y = e + p.Fe;
            x = n + p.Fn;
        }

        // Coût d'un méridien central donné : somme des écarts carrés, après avoir calé Fe / Fn sur la moyenne
        private static double Cost(List<Map_Match> list, double cm, out double fe, out double fn)
        {
            int n = list.Count;
            var e = new double[n];
            var nn = new double[n];
            double sumY = 0, sumX = 0;

            for (int i = 0; i < n; i++)
            {
                Tm(list[i].Lat, list[i].Lon, cm, out e[i], out nn[i]);
                sumY += list[i].Y - e[i];
                sumX += list[i].X - nn[i];
            }
            fe = sumY / n;
            fn = sumX / n;

            double cost = 0;
            for (int i = 0; i < n; i++)
            {
                double dy = list[i].Y - e[i] - fe;
                double dx = list[i].X - nn[i] - fn;
                cost += dx * dx + dy * dy;
            }
            return cost;
        }

        private static double BestCm(List<Map_Match> list, double from, double to, double step)
        {
            double best = from, bestCost = double.MaxValue;
            for (double cm = from; cm <= to; cm += step)
            {
                double fe, fn;
                double c = Cost(list, cm, out fe, out fn);
                if (c < bestCost) { bestCost = c; best = cm; }
            }
            return best;
        }

        private static Map_Projection Fit(List<Map_Match> list)
        {
            double lonMean = list.Average(m => m.Lon);
            double cm = BestCm(list, lonMean - 9, lonMean + 9, 0.5);
            cm = BestCm(list, cm - 0.5, cm + 0.5, 0.05);
            cm = BestCm(list, cm - 0.05, cm + 0.05, 0.005);

            double fe, fn;
            Cost(list, cm, out fe, out fn);
            return new Map_Projection { Cm = cm, Fe = fe, Fn = fn };
        }

        // Projections des cartes DCS (Transverse Mercator, k0 = 0.9996) : méridien central, décalages est / nord.
        // DCS y = Easting + Fe ; DCS x = Northing + Fn
        // Les valeurs viennent de la section [theatres] de Data\dce_data.ini (+ %AppData%\DCE_Manager\dce_data_user.ini) : voir Map_Data.

        // Nom du théâtre (fichier "theatre" d'un .miz de la campagne : Init\base_mission.miz, sinon <campagne>_first.miz)
        private static string DetectTheatre(string campaign)
        {
            string root = Path.Combine(ParamConf.PATH_SavedGames_DCS, @"Mods\tech\DCE\Missions\Campaigns");
            string[] candidates =
            {
                Path.Combine(root, campaign, "Init", "base_mission.miz"),
                Path.Combine(root, campaign + "_first.miz"),
                Path.Combine(root, campaign + "_ongoing.miz")
            };

            foreach (string miz in candidates)
            {
                try
                {
                    if (!File.Exists(miz)) continue;
                    using (ZipArchive zip = ZipFile.OpenRead(miz))
                    {
                        ZipArchiveEntry e = zip.GetEntry("theatre");
                        if (e == null) continue;
                        using (var sr = new StreamReader(e.Open(), Encoding.UTF8))
                        {
                            string name = sr.ReadToEnd().Trim();
                            if (name != "") return name;
                        }
                    }
                }
                catch (Exception ex)
                {
                    FormUtils.LogRegister("Map_Web | théâtre illisible dans " + miz + " : " + ex.Message);
                }
            }
            return "";
        }

        internal static Map_Projection KnownProjection(string campaign, out string theatre)
        {
            theatre = DetectTheatre(campaign);
            double cm, fe, fn;
            if (theatre != "" && Map_Data.TheatreProjection(theatre, out cm, out fe, out fn))
                return new Map_Projection { Cm = cm, Fe = fe, Fn = fn };
            return null;
        }

        // Inverse de LatLonToDcs (Newton sur la projection directe) : position DCS -> latitude / longitude
        internal static void DcsToLatLon(Map_Projection p, double x, double y, out double lat, out double lon)
        {
            lat = (x - p.Fn) / 111000.0;
            lon = p.Cm + (y - p.Fe) / (111320.0 * Math.Max(0.1, Math.Cos(lat * Math.PI / 180.0)));

            const double h = 1e-4;
            for (int i = 0; i < 12; i++)
            {
                double fx, fy, ax, ay, bx, by;
                LatLonToDcs(p, lat, lon, out fx, out fy);
                double ex = x - fx, ey = y - fy;
                if (Math.Abs(ex) < 1 && Math.Abs(ey) < 1) break;

                LatLonToDcs(p, lat + h, lon, out ax, out ay);
                LatLonToDcs(p, lat, lon + h, out bx, out by);
                double a = (ax - fx) / h, b = (bx - fx) / h, c = (ay - fy) / h, d = (by - fy) / h;
                double det = a * d - b * c;
                if (Math.Abs(det) < 1e-9) break;

                lat += (ex * d - b * ey) / det;
                lon += (a * ey - c * ex) / det;
            }
        }

        private static double ErrorOf(Map_Projection p, Map_Match m)
        {
            double x, y;
            LatLonToDcs(p, m.Lat, m.Lon, out x, out y);
            double dx = m.X - x, dy = m.Y - y;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        // Calage qui écarte les bases trop éloignées du reste (mauvais code ICAO, point DCS décalé...).
        // "used" reçoit les bases gardées ; ErrorM est rempli pour toutes.
        private static Map_Projection FitRobust(List<Map_Match> all, List<Map_Match> used)
        {
            used.Clear();
            used.AddRange(all);

            for (int pass = 0; pass < 3; pass++)
            {
                Map_Projection p = Fit(used);
                foreach (Map_Match m in all) m.ErrorM = ErrorOf(p, m);

                List<double> errs = used.Select(m => m.ErrorM).OrderBy(e => e).ToList();
                double median = errs[errs.Count / 2];
                double limit = Math.Max(3000, 3 * median);

                List<Map_Match> next = all.Where(m => m.ErrorM <= limit).ToList();
                if (next.Count < 3 || next.Count == used.Count) break;

                used.Clear();
                used.AddRange(next);
            }

            Map_Projection final = Fit(used);
            foreach (Map_Match m in all) m.ErrorM = ErrorOf(final, m);
            return final;
        }

        // ------------------------------------------------------------------
        //  Tuiles
        // ------------------------------------------------------------------
    }
}
