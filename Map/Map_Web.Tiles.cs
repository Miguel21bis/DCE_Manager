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
    // Partie de Map_Web (voir Map_Web.cs) : tuiles, mosaïque
    internal static partial class Map_Web
    {
        internal static int TileX(double lon, int z)
        {
            return (int)Math.Floor((lon + 180.0) / 360.0 * (1 << z));
        }

        internal static int TileY(double lat, int z)
        {
            double r = Math.Max(-85, Math.Min(85, lat)) * Math.PI / 180;
            return (int)Math.Floor((1 - Math.Log(Math.Tan(r) + 1 / Math.Cos(r)) / Math.PI) / 2 * (1 << z));
        }

        internal static double TileLon(double tx, int z)
        {
            return tx / (double)(1 << z) * 360.0 - 180.0;
        }

        internal static double TileLat(double ty, int z)
        {
            return Math.Atan(Math.Sinh(Math.PI * (1 - 2.0 * ty / (1 << z)))) * 180.0 / Math.PI;
        }

        private static string TileFile(int z, int x, int y)
        {
            return TileFile(CurrentStyle, z, x, y);
        }

        internal static string TileFile(Map_Style st, int z, int x, int y)
        {
            string root = st.Id == "osm" ? Path.Combine(CacheRoot, "Tiles") : Path.Combine(CacheRoot, "Tiles_" + st.Id);
            return Path.Combine(root, z.ToString(), x.ToString(), y + ".png");
        }

        // Charge les tuiles déjà présentes sur le disque et calcule leurs coins en coordonnées DCS
        // Image lue en mémoire : le fichier n'est pas verrouillé
        internal static Bitmap LoadBitmap(string file)
        {
            using (var fs = File.OpenRead(file))
            using (var tmp = Image.FromStream(fs))
                return new Bitmap(tmp);
        }

        // Une tuile OSM est carrée en Web Mercator, mais en coordonnées DCS c'est un trapèze
        // (convergence des méridiens, surtout au nord). 3 coins ne suffisent donc pas : on la découpe
        // en cut x cut morceaux, chacun avec ses vrais coins. Les voisins partagent les mêmes coins,
        // donc plus de bandes noires entre les tuiles.
        internal static List<Map_Tile> MakePieces(Map_Projection p, int z, int tx, int ty, Bitmap img, int cut)
        {
            var list = new List<Map_Tile>();
            float w = img.Width / (float)cut, h = img.Height / (float)cut;
            for (int i = 0; i < cut; i++)
            {
                for (int j = 0; j < cut; j++)
                {
                    var t = new Map_Tile { Image = img, Src = new RectangleF(i * w, j * h, w, h) };
                    double a0 = tx + i / (double)cut, a1 = tx + (i + 1) / (double)cut;
                    double b0 = ty + j / (double)cut, b1 = ty + (j + 1) / (double)cut;
                    LatLonToDcs(p, TileLat(b0, z), TileLon(a0, z), out t.TlX, out t.TlY);
                    LatLonToDcs(p, TileLat(b0, z), TileLon(a1, z), out t.TrX, out t.TrY);
                    LatLonToDcs(p, TileLat(b1, z), TileLon(a0, z), out t.BlX, out t.BlY);
                    list.Add(t);
                }
            }
            return list;
        }

        // Colle toutes les tuiles du fond dans une seule image (thread de fond : relit les fichiers lui-même,
        // pour ne jamais toucher aux images que l'interface est en train de dessiner).
        internal static Bitmap BuildMosaic(Map_Background bg, out double minX, out double maxX, out double minY, out double maxY, out double pix)
        {
            var tiles = new List<int[]>();
            minX = minY = double.MaxValue;
            maxX = maxY = double.MinValue;
            double tileMeters = double.MaxValue;

            for (int tx = bg.X0; tx <= bg.X1; tx++)
            {
                for (int ty = bg.Y0; ty <= bg.Y1; ty++)
                {
                    if (!File.Exists(TileFile(bg.Style, bg.Z, tx, ty))) continue;
                    tiles.Add(new int[] { tx, ty });

                    double[] lat = { TileLat(ty, bg.Z), TileLat(ty + 1, bg.Z) };
                    double[] lon = { TileLon(tx, bg.Z), TileLon(tx + 1, bg.Z) };
                    double tlX = 0, tlY = 0, trX = 0, trY = 0;
                    for (int a = 0; a < 2; a++)
                    {
                        for (int b = 0; b < 2; b++)
                        {
                            double x, y;
                            LatLonToDcs(bg.Proj, lat[a], lon[b], out x, out y);
                            if (x < minX) minX = x;
                            if (x > maxX) maxX = x;
                            if (y < minY) minY = y;
                            if (y > maxY) maxY = y;
                            if (a == 0 && b == 0) { tlX = x; tlY = y; }
                            if (a == 0 && b == 1) { trX = x; trY = y; }
                        }
                    }
                    // taille d'un pixel de tuile, en mètres (côté haut de la tuile / 256)
                    double side = Math.Sqrt((tlX - trX) * (tlX - trX) + (tlY - trY) * (tlY - trY)) / 256.0;
                    if (side > 0 && side < tileMeters) tileMeters = side;
                }
            }
            if (tiles.Count == 0) throw new Exception("no tile");

            // Finesse : celle des tuiles, mais pas plus de 3600 pixels de côté (environ 50 Mo)
            double extent = Math.Max(maxX - minX, maxY - minY);
            pix = Math.Max(tileMeters, extent / 3600.0);
            int w = (int)Math.Ceiling((maxY - minY) / pix) + 1;
            int h = (int)Math.Ceiling((maxX - minX) / pix) + 1;

            var mosaic = new Bitmap(w, h, PixelFormat.Format32bppPArgb);
            using (Graphics g = Graphics.FromImage(mosaic))
            using (var ia = new ImageAttributes())
            {
                ia.SetWrapMode(WrapMode.TileFlipXY);
                g.InterpolationMode = InterpolationMode.HighQualityBilinear;
                g.PixelOffsetMode = PixelOffsetMode.Half;

                double mnX = minX, mxX = maxX, mnY = minY, p = pix;
                Func<double, double, PointF> P = (x, y) => new PointF((float)((y - mnY) / p), (float)((mxX - x) / p));

                foreach (int[] t in tiles)
                {
                    try
                    {
                        using (Bitmap img = LoadBitmap(TileFile(bg.Style, bg.Z, t[0], t[1])))
                        {
                            foreach (Map_Tile piece in MakePieces(bg.Proj, bg.Z, t[0], t[1], img, Cut))
                                g.DrawImage(img, new PointF[] { P(piece.TlX, piece.TlY), P(piece.TrX, piece.TrY), P(piece.BlX, piece.BlY) },
                                    piece.Src, GraphicsUnit.Pixel, ia);
                        }
                    }
                    catch (Exception ex)
                    {
                        FormUtils.LogRegister("Map_Web | mosaïque, tuile illisible : " + ex.Message);
                    }
                }
            }
            return mosaic;
        }

        private static Map_Background LoadTiles(Map_Projection p, int z, int x0, int x1, int y0, int y1)
        {
            var bg = new Map_Background { Attribution = CurrentStyle.Attribution, Proj = p, Style = CurrentStyle, Z = z, X0 = x0, X1 = x1, Y0 = y0, Y1 = y1 };

            for (int tx = x0; tx <= x1; tx++)
            {
                for (int ty = y0; ty <= y1; ty++)
                {
                    string file = TileFile(z, tx, ty);
                    if (!File.Exists(file)) continue;

                    try
                    {
                        Bitmap img = LoadBitmap(file);
                        bg.Tiles.AddRange(MakePieces(p, z, tx, ty, img, Cut));
                    }
                    catch (Exception ex)
                    {
                        FormUtils.LogRegister("Map_Web | tile illisible " + file + " : " + ex.Message);
                        try { File.Delete(file); } catch { }    // tuile abîmée : elle sera retéléchargée
                    }
                }
            }

            bg.Detail = new Map_Detail(p, z, x0, x1, y0, y1, CurrentStyle);
            return bg;
        }
    }
}
