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
    // Une base utilisée pour caler la carte (nom + code ICAO + position DCS)
    internal class Map_Match
    {
        public string Name;
        public string Icao;
        public double X;        // DCS x (nord)
        public double Y;        // DCS y (est)
        public double Lat;      // position réelle de référence (OurAirports : point de l'aéroport, ou milieu / bout d'une piste)
        public double Lon;
        public double ArpLat, ArpLon;   // point de l'aéroport (OurAirports), pour revenir dessus si aucune piste ne colle
        public List<Map_Runway> DbRunways;      // pistes de db_airbases (position absolue, 0 / 0 = pas de position) : sert à mesurer la règle de position des pistes
        public string Ref = "";         // ce que représente le point de la base : "runway middle" / "runway end" / "airport point"
        public double ErrorM;   // écart après calage, en mètres
    }

    // Une tuile de fond, avec ses 3 coins exprimés en coordonnées DCS
    // (haut-gauche, haut-droite, bas-gauche) : la vue n'a plus qu'à les passer à DrawImage.
    internal class Map_Tile
    {
        public Bitmap Image;
        public double TlX, TlY, TrX, TrY, BlX, BlY;
        public RectangleF Src;      // partie de l'image utilisée (une tuile est découpée en petits morceaux)
    }

    // Un style de fond de carte (serveur de tuiles). {z} {x} {y} sont remplacés dans l'adresse.
    internal class Map_Style
    {
        public string Id;
        public string Label;
        public string Url;
        public string Attribution;
        public int MaxZoom;
    }

    internal class Map_Background : IDisposable
    {
        public List<Map_Tile> Tiles = new List<Map_Tile>();
        public string Report = "";
        public string AnchorRule = "";      // règle déduite de la mesure : middle / start / east / west ("" = pas de règle claire)
        public string AnchorInfo = "";      // mesure : où se trouve le point de db_airbases sur les vraies pistes (voir Map_Web.MeasureAnchor)
        public string Attribution = "";
        public Map_Detail Detail;       // tuiles plus fines, chargées selon le zoom de l'utilisateur

        // Données pour construire la mosaïque (voir Map_Web.BuildMosaic)
        public Map_Projection Proj;
        public Map_Style Style;
        public int Z, X0, X1, Y0, Y1;

        // Mosaïque : toutes les tuiles du fond collées en UNE image alignée sur le nord DCS.
        // Dessiner une seule image est bien plus rapide que des centaines de petits morceaux inclinés.
        public Bitmap Mosaic;
        public double MosaicMinX, MosaicMaxX, MosaicMinY, MosaicMaxY, MosaicPix;
        public bool Disposed;

        // Appelé dans le thread de l'interface : la mosaïque est prête, on libère les tuiles d'origine
        public void AttachMosaic(Bitmap img, double minX, double maxX, double minY, double maxY, double pix)
        {
            if (Disposed) { img.Dispose(); return; }

            Mosaic = img;
            MosaicMinX = minX; MosaicMaxX = maxX; MosaicMinY = minY; MosaicMaxY = maxY; MosaicPix = pix;

            var done = new HashSet<Bitmap>();
            foreach (Map_Tile t in Tiles)
                if (t.Image != null && done.Add(t.Image)) t.Image.Dispose();
            Tiles.Clear();
        }

        public void Dispose()
        {
            Disposed = true;
            if (Detail != null) Detail.Dispose();
            if (Mosaic != null) { Mosaic.Dispose(); Mosaic = null; }
            // Les morceaux d'une même tuile partagent la même image : on ne la libère qu'une fois
            var done = new HashSet<Bitmap>();
            foreach (Map_Tile t in Tiles)
                if (t.Image != null && done.Add(t.Image)) t.Image.Dispose();
            Tiles.Clear();
        }
    }

    // Passage latitude/longitude -> coordonnées DCS : projection Transverse Mercator,
    // avec méridien central (Cm) et décalages (Fe, Fn) retrouvés par le calage.
    //   DCS y (est)  = Easting  + Fe
    //   DCS x (nord) = Northing + Fn
    internal class Map_Projection
    {
        public double Cm, Fe, Fn;
    }
}
