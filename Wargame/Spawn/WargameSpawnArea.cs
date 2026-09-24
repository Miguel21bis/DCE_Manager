using System;
using System.Collections.Generic;
using System.Drawing;

namespace DCE_Manager
{
    internal static class WargameSpawnPolicy
    {
        public const string Free = "free";
        public const string RoadOnly = "roadOnly";   // gardé pour compatibilité, plus utilisé par le solveur
        public const string Forbidden = "forbidden";
        public const string Exception = "exception"; // purement informatif (rapport de test) - le solveur teste le tag directement
    }

    internal static class WargameSpawnTag
    {
        public const string Town = "town";
        public const string City = "city";
        public const string Forest = "forest";
        public const string Mountain = "mountain";
        public const string Base = "base";
        public const string Exception = "exception";

        public const string Default = Town;

        // Casse indifférente (StringComparer.OrdinalIgnoreCase) et pluriel accepté -
        // pas de dérivation automatique (city -> cities est irrégulier), chaque forme
        // est listée explicitement.
        internal static readonly Dictionary<string, string> Aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "town",       Town },
            { "towns",      Town },
            { "village",    Town },
            { "villages",   Town },
            { "ville",      Town },
            { "villes",     Town },

            { "city",       City },
            { "cities",     City },

            { "forest",     Forest },
            { "forests",    Forest },
            { "foret",      Forest },
            { "forets",     Forest },
            { "wood",       Forest },
            { "woods",      Forest },

            { "mountain",   Mountain },
            { "mountains",  Mountain },
            { "montain",    Mountain },   // faute courante
            { "montains",   Mountain },
            { "montagne",   Mountain },
            { "montagnes",  Mountain },

            { "base",       Base },
            { "bases",      Base },

            // Polygone d'exception : autorise le spawn à l'intérieur d'une zone
            // interdite (town/mountain/city/base) qu'il chevauche.
            { "exception",  Exception },
            { "exceptions", Exception },
            { "spawn",      Exception },
            { "spawns",     Exception },
        };

        // town/city/mountain/base sont interdits de base (sauf route ou exception
        // à l'intérieur). forest reste libre (mis de côté pour plus tard - un
        // trigger de nettoyage arbres/bâtiments le rendra vraiment praticable).
        public static readonly Dictionary<string, string> Policy = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { Town,      WargameSpawnPolicy.Forbidden },
            { City,      WargameSpawnPolicy.Forbidden },
            { Mountain,  WargameSpawnPolicy.Forbidden },
            { Base,      WargameSpawnPolicy.Forbidden },
            { Forest,    WargameSpawnPolicy.Free },
            { Exception, WargameSpawnPolicy.Exception },
        };

        public static string FromObjectName(string objectName)
        {
            if (string.IsNullOrWhiteSpace(objectName))
                return Default;

            foreach (string part in objectName.Split('-', '_', ' '))
            {
                if (Aliases.TryGetValue(part, out string tag))
                    return tag;
            }

            return Default;
        }

        public static bool HasKnownTag(string objectName)
        {
            if (string.IsNullOrWhiteSpace(objectName))
                return false;

            foreach (string part in objectName.Split('-', '_', ' '))
            {
                if (Aliases.ContainsKey(part))
                    return true;
            }

            return false;
        }

        public static string GetPolicy(string tag)
        {
            if (!string.IsNullOrEmpty(tag) && Policy.TryGetValue(tag, out string policy))
                return policy;

            return WargameSpawnPolicy.Forbidden;
        }
    }

    // Une rue ou une route : polyligne ouverte, coordonnées DCS absolues.
    // Aucun tag : dans ce fichier, toute Line est une voie carrossable.
    internal class WargameSpawnLine
    {
        public string Name;
        public List<PointF> Points = new List<PointF>();

        public double GetLength()
        {
            double total = 0;

            for (int i = 0; i < Points.Count - 1; i++)
            {
                double dx = Points[i + 1].X - Points[i].X;
                double dy = Points[i + 1].Y - Points[i].Y;
                total += Math.Sqrt(dx * dx + dy * dy);
            }

            return total;
        }
    }

    // Une surface à traiter à part au moment du spawn.
    //
    // Les quatre modes de polygone de DCS (free, circle, oval, rect) sont tous
    // ramenés à une simple liste de points par le parser : un seul test
    // d'appartenance à écrire, un seul chemin de rendu quand ces polygones
    // s'afficheront sur ucWargameMapView. Un cercle approché par 24 segments
    // fait 1 % d'erreur, négligeable devant un tracé à la souris.
    internal class WargameSpawnPolygon
    {
        public string Name;
        public string Tag = WargameSpawnTag.Default;
        public string Policy = WargameSpawnPolicy.RoadOnly;

        // Coordonnées DCS absolues, dans l'ordre du contour
        public List<PointF> Points = new List<PointF>();

        // Rectangle englobant, calculé une fois au parsing. Un test de spawn
        // interroge tous les polygones de la campagne : sur une carte bien
        // annotée il y en a facilement quelques centaines, autant écarter
        // 99 % d'entre eux par une comparaison de bornes.
        public RectangleF Bounds;

        public void ComputeBounds()
        {
            if (Points.Count == 0)
            {
                Bounds = RectangleF.Empty;
                return;
            }

            float minX = Points[0].X, maxX = Points[0].X;
            float minY = Points[0].Y, maxY = Points[0].Y;

            foreach (PointF p in Points)
            {
                if (p.X < minX) minX = p.X;
                if (p.X > maxX) maxX = p.X;
                if (p.Y < minY) minY = p.Y;
                if (p.Y > maxY) maxY = p.Y;
            }

            Bounds = new RectangleF(minX, minY, maxX - minX, maxY - minY);
        }

        // Lancer de rayon classique. Le comportement exactement sur l'arête n'est
        // pas garanti, sans importance ici : on parle de véhicules posés au mètre
        // près sur un contour dessiné à la main.
        public bool Contains(PointF p)
        {
            if (Points.Count < 3) return false;
            if (!Bounds.Contains(p)) return false;

            bool inside = false;
            int j = Points.Count - 1;

            for (int i = 0; i < Points.Count; i++)
            {
                PointF a = Points[i];
                PointF b = Points[j];

                if ((a.Y > p.Y) != (b.Y > p.Y))
                {
                    float xCross = (b.X - a.X) * (p.Y - a.Y) / (b.Y - a.Y) + a.X;
                    if (p.X < xCross)
                        inside = !inside;
                }

                j = i;
            }

            return inside;
        }
    }

    // Tout ce que le fichier wargame_spawn.miz contient, une fois parsé.
    internal class WargameSpawnAreas
    {
        public List<WargameSpawnLine> Lines = new List<WargameSpawnLine>();
        public List<WargameSpawnPolygon> Polygons = new List<WargameSpawnPolygon>();

        public bool IsEmpty
        {
            get { return Lines.Count == 0 && Polygons.Count == 0; }
        }

        // Premier polygone contenant le point, ou null. Sert surtout à vérifier
        // le parsing pour l'instant ; les règles de placement viendront ensuite.
        public WargameSpawnPolygon FindPolygonAt(PointF dcsPoint)
        {
            foreach (WargameSpawnPolygon poly in Polygons)
            {
                if (poly.Contains(dcsPoint))
                    return poly;
            }

            return null;
        }

        // Contrairement à FindPolygonAt (le premier trouvé), renvoie TOUS les
        // polygones qui contiennent ce point - indispensable maintenant qu'un
        // point peut être à la fois dans un polygone interdit ET dans un
        // polygone exception qui le chevauche.
        public List<WargameSpawnPolygon> FindAllPolygonsAt(PointF dcsPoint)
        {
            var result = new List<WargameSpawnPolygon>();

            foreach (WargameSpawnPolygon poly in Polygons)
            {
                if (poly.Contains(dcsPoint))
                    result.Add(poly);
            }

            return result;
        }


    }
}
