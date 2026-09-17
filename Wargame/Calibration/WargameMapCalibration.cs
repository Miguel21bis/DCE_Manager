using System;
using System.Drawing;
using System.IO;
using DCE_Manager.Utils;
using Newtonsoft.Json;

namespace DCE_Manager
{
    // Calibration DCS <-> pixel pour la carte de fond d'une campagne wargame.
    // Basée sur 2 points de repère (ex: 2 bases aériennes) dont on connaît à la
    // fois la position DCS (X/Y moteur) et la position pixel sur l'image collée
    // par le campaignMaker (Init/wargame_map.jpg).
    //
    // MODELE : l'image est supposée NORD EN HAUT, ce qui est le cas de toute
    // capture de carte F10 ou de l'éditeur. L'orientation est donc connue
    // d'avance et n'a pas à être déduite :
    //
    //      X de DCS = nord   -> vers le HAUT de l'image  (pixel Y décroissant)
    //      Y de DCS = est    -> vers la DROITE de l'image (pixel X croissant)
    //
    // Il ne reste qu'à déterminer deux échelles et deux décalages, soit quatre
    // inconnues pour quatre équations : les deux points de repère suffisent
    // exactement.
    //
    // La version précédente déduisait une échelle unique ET une rotation libre.
    // C'était le degré de liberté au mauvais endroit : la moindre imprécision de
    // clic se transformait en rotation de toute la carte, juste aux deux repères
    // et fausse partout ailleurs, sans aucun moyen de s'en apercevoir.
    //
    // Plus de trigonométrie : les coefficients sont recalculés à chaque appel,
    // mais ce ne sont plus que quelques soustractions et divisions.
    internal class WargameMapCalibration
    {
        public PointF DcsRef1;
        public PointF DcsRef2;
        public PointF PixelRef1;
        public PointF PixelRef2;

        // Ecart minimal exigé sur CHAQUE axe entre les deux repères. Deux points
        // alignés plein nord ne donnent aucune information sur l'échelle est-ouest :
        // c'est la contrepartie du modèle à deux échelles, et il vaut mieux le
        // refuser franchement que produire une carte absurde.
        private const double MinAxisSeparation = 1000.0;

        // Séparation en dessous de laquelle la calibration marche mais reste
        // imprécise : l'erreur de clic se divise par la distance entre repères.
        private const double AdvisedAxisSeparation = 10000.0;

        [JsonIgnore]
        public bool IsCalibrated
        {
            get
            {
                return Math.Abs(DcsRef2.X - DcsRef1.X) >= MinAxisSeparation
                    && Math.Abs(DcsRef2.Y - DcsRef1.Y) >= MinAxisSeparation;
            }
        }

        // Pixels par mètre vers l'est (axe Y de DCS -> axe X de l'image)
        [JsonIgnore]
        public double ScaleX
        {
            get { return (PixelRef2.X - PixelRef1.X) / (double)(DcsRef2.Y - DcsRef1.Y); }
        }

        // Pixels par mètre vers le nord (axe X de DCS -> axe Y de l'image, inversé)
        [JsonIgnore]
        public double ScaleY
        {
            get { return -(PixelRef2.Y - PixelRef1.Y) / (double)(DcsRef2.X - DcsRef1.X); }
        }

        // Convertit un point DCS (X/Y moteur) en position pixel sur l'image.
        public PointF DcsToPixel(PointF dcsPoint)
        {
            if (!IsCalibrated)
                return PointF.Empty;

            double pixelX = PixelRef1.X + (dcsPoint.Y - DcsRef1.Y) * ScaleX;
            double pixelY = PixelRef1.Y - (dcsPoint.X - DcsRef1.X) * ScaleY;

            return new PointF((float)pixelX, (float)pixelY);
        }

        // Conversion inverse. Indispensable pour vérifier une calibration :
        // survoler un lieu connu et comparer la coordonnée lue à celle de DCS.
        public PointF PixelToDcs(PointF pixelPoint)
        {
            if (!IsCalibrated)
                return PointF.Empty;

            double scaleX = ScaleX;
            double scaleY = ScaleY;

            if (Math.Abs(scaleX) < 1e-12 || Math.Abs(scaleY) < 1e-12)
                return PointF.Empty;

            double dcsY = DcsRef1.Y + (pixelPoint.X - PixelRef1.X) / scaleX;
            double dcsX = DcsRef1.X - (pixelPoint.Y - PixelRef1.Y) / scaleY;

            return new PointF((float)dcsX, (float)dcsY);
        }

        // Echelle moyenne, pour ce qui n'a pas d'orientation (rayon d'un cercle).
        // Les deux échelles devraient être identiques ; si elles ne le sont pas,
        // GetDiagnostics() le dit.
        public double GetScale()
        {
            if (!IsCalibrated)
                return 1.0;

            return (Math.Abs(ScaleX) + Math.Abs(ScaleY)) / 2.0;
        }

        // Ecart entre les deux échelles, en pourcentage. Devrait être proche de
        // zéro : au-delà de 1 %, un des deux clics est à revoir.
        public double GetScaleMismatchPercent()
        {
            if (!IsCalibrated)
                return 0;

            double a = Math.Abs(ScaleX);
            double b = Math.Abs(ScaleY);

            if (a < 1e-12 || b < 1e-12)
                return 0;

            return Math.Abs(a - b) / Math.Min(a, b) * 100.0;
        }

        // Rotation que les deux repères impliquent si on les interprétait comme
        // une similitude (l'ancien modèle). Sur une carte nord en haut elle vaut
        // -90 degrés ; tout écart mesure l'imprécision des clics. Purement
        // informatif, plus rien ne s'en sert pour convertir.
        public double GetImpliedRotationDeg()
        {
            double dcsDx = DcsRef2.X - DcsRef1.X;
            double dcsDy = DcsRef2.Y - DcsRef1.Y;
            double pixDx = PixelRef2.X - PixelRef1.X;
            double pixDy = PixelRef2.Y - PixelRef1.Y;

            if (Math.Abs(dcsDx) < 1e-9 && Math.Abs(dcsDy) < 1e-9)
                return 0;

            double angle = Math.Atan2(pixDy, pixDx) - Math.Atan2(dcsDy, dcsDx);

            return angle * 180.0 / Math.PI;
        }

        // Texte affichable dans la Form de calibration (UI en anglais).
        public string GetDiagnostics()
        {
            if (!IsCalibrated)
            {
                return "The two reference points must differ by at least "
                    + (int)MinAxisSeparation + " m on BOTH axes (north and east).";
            }

            double scaleX = Math.Abs(ScaleX);
            double scaleY = Math.Abs(ScaleY);
            double mismatch = GetScaleMismatchPercent();
            double rotation = GetImpliedRotationDeg();

            string text =
                "Scale X : " + (1.0 / scaleX).ToString("0.0") + " m/px\r\n" +
                "Scale Y : " + (1.0 / scaleY).ToString("0.0") + " m/px\r\n" +
                "Mismatch: " + mismatch.ToString("0.0") + " %\r\n" +
                "Implied rotation: " + rotation.ToString("0.00") + "\u00b0 (should be -90)";

            if (mismatch > 1.0)
                text += "\r\n\r\nWARNING: both scales should match. Re-check one of the two clicks.";

            if (Math.Abs(DcsRef2.X - DcsRef1.X) < AdvisedAxisSeparation
                || Math.Abs(DcsRef2.Y - DcsRef1.Y) < AdvisedAxisSeparation)
            {
                text += "\r\n\r\nWARNING: reference points are close together. "
                    + "Pick two points far apart on both axes for better accuracy.";
            }

            return text;
        }

        // -------- Sauvegarde / chargement (Init/wargame/wargame_map_calib.json) --------
        // Le format du fichier ne change pas : ce sont toujours les 4 mêmes points,
        // seule leur interprétation évolue. Aucune migration nécessaire, mais une
        // calibration faite avec l'ancien modèle est à refaire, elle contenait
        // la rotation parasite dans ses clics.

        public void Save(string jsonPath)
        {
            string json = JsonConvert.SerializeObject(this, Formatting.Indented);
            File.WriteAllText(jsonPath, json);
        }

        // Renvoie toujours un objet utilisable (vide/non calibré si le fichier
        // n'existe pas encore ou est illisible), jamais null.
        public static WargameMapCalibration Load(string jsonPath)
        {
            if (!File.Exists(jsonPath))
                return new WargameMapCalibration();

            try
            {
                string json = File.ReadAllText(jsonPath);
                return JsonConvert.DeserializeObject<WargameMapCalibration>(json) ?? new WargameMapCalibration();
            }
            catch (Exception ex)
            {
                FormUtils.LogRegister("WargameMapCalibration | erreur lecture " + jsonPath + " : " + ex.Message);
                return new WargameMapCalibration();
            }
        }
    }
}
