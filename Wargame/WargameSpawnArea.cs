using System;
using System.Collections.Generic;
using System.Drawing;

namespace DCE_Manager
{
    // Ce que le campaignMaker a le droit de faire dans un polygone donné.
    // Volontairement séparé du tag : le tag dit ce qu'est la surface (une ville,
    // un bois), la politique dit ce qu'on en fait. Le jour où le trigger de
    // nettoyage des arbres est validé en jeu, seule la table Policy bouge.
    internal static class WargameSpawnPolicy
    {
        public const string Free = "free";           // spawn autorisé n'importe où
        public const string RoadOnly = "roadOnly";   // spawn autorisé seulement sur une ligne
        public const string Forbidden = "forbidden"; // jamais, même sur une ligne (réservé à l'eau)
    }

    // Nature d'un polygone, lue dans le nom de l'objet dessiné.
    //
    // Le tag n'est ni au début ni à la fin du nom : DCS suffixe les copies, donc
    // "Polygon-town-7-13-2-5" est une copie de copie d'un polygone "town". On
    // découpe le nom sur -, _ et espace, et on cherche un mot connu dans les
    // morceaux. Le premier trouvé gagne.
    internal static class WargameSpawnTag
    {
        public const string Town = "town";
        public const string City = "city";
        public const string Forest = "forest";
        public const string Mountain = "mountain";
        public const string Base = "base";

        // Un polygone dessiné mais mal nommé ne doit pas silencieusement autoriser
        // le spawn : on le rabat sur le cas le plus contraignant et le plus fréquent.
        public const string Default = Town;

        // Fautes de frappe et synonymes tolérés, pour ne pas obliger à tout
        // renommer dans l'éditeur DCS.
        internal static readonly Dictionary<string, string> Aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "town",     Town },
            { "village",  Town },
            { "city",     City },
            { "ville",    Town },
            { "forest",   Forest },
            { "foret",    Forest },
            { "wood",     Forest },
            { "mountain", Mountain },
            { "montain",  Mountain },   // faute courante
            { "montagne", Mountain },
            { "base",     Base },
        };

        // Politique de spawn associée à chaque tag.
        public static readonly Dictionary<string, string> Policy = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { Town,     WargameSpawnPolicy.RoadOnly },
            { City,     WargameSpawnPolicy.RoadOnly },
            { Mountain, WargameSpawnPolicy.Forbidden },
            { Base,     WargameSpawnPolicy.RoadOnly },

            // En forêt un véhicule peut être posé n'importe où À CONDITION qu'un
            // trigger de nettoyage supprime arbres et bâtiments. Reste à valider
            // en jeu : si le timing d'apparition rend ça impossible, repasser
            // cette seule ligne en RoadOnly.
            { Forest,   WargameSpawnPolicy.Free },
        };

        // Extrait le tag du nom de l'objet dessiné. Retourne Default si rien
        // de connu n'est trouvé (le parser logue le cas).
        public static string FromObjectName(string objectName)
        {
            if (string.IsNullOrWhiteSpace(objectName))
                return Default;

            string[] parts = objectName.Split('-', '_', ' ');

            foreach (string part in parts)
            {
                if (Aliases.TryGetValue(part, out string tag))
                    return tag;
            }

            return Default;
        }

        // Distingue "tag town explicite" de "aucun tag trouvé, rabattu sur town",
        // pour ne loguer que le second cas.
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

            return WargameSpawnPolicy.RoadOnly;
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
    }
}
