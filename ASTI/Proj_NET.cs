using System;
using System.IO;
using System.Windows.Forms;
using DCE_Manager;
using NLua;
using System.Collections.Generic;
using DCE_Manager.Utils;
using DCE_Manager.Parameters;
using ProjNet.CoordinateSystems;
using ProjNet.CoordinateSystems.Transformations;

namespace DCE_Manager
{
    class Proj_NET
    {
        
        // Fonction pour configurer la transformation géodésique XY -> Lat/Lon avec UTM 36N et Proj.NET
        static (double, double) ConvertXYToLatLon(double x, double y, double originLat, double originLon)
        {
            // Système de coordonnées géographiques (WGS84 pour Latitude/Longitude)
            var geographicCS = GeographicCoordinateSystem.WGS84;

            // Créer une instance de l'usine de systèmes de coordonnées
            var csFactory = new CoordinateSystemFactory();

            // Créer un système de projection UTM pour la zone 36N (hémisphère nord)
            var projectedCS = ProjectedCoordinateSystem.WGS84_UTM(36, true); // true pour l'hémisphère nord

            // Créer une transformation des coordonnées géographiques (lat/lon) en coordonnées projetées (XY)
            var transformFactory = new CoordinateTransformationFactory();
            var transformationToXY = transformFactory.CreateFromCoordinateSystems(geographicCS, projectedCS);

            // 1. Convertir les coordonnées d'origine (latitude/longitude) en UTM (XY)
            double[] originLatLon = { originLon, originLat }; // lon, lat pour la transformation
            double[] originXY = transformationToXY.MathTransform.Transform(originLatLon);

            // equivalent_x et equivalent_y représentent les coordonnées projetées de l'origine
            double equivalent_x = originXY[0]; // Easting
            double equivalent_y = originXY[1]; // Northing

            // Calcul de l'offset pour ajuster la position
            double Offset_x = x + equivalent_x;
            double Offset_y = y + equivalent_y;

            // 2. Créer une transformation des coordonnées projetées (XY) en coordonnées géographiques (lat/lon)
            var transformationToLatLon = transformFactory.CreateFromCoordinateSystems(projectedCS, geographicCS);

            // Convertir les coordonnées XY ajustées en latitude/longitude
            double[] xyCoordinates = { Offset_x, Offset_y };
            double[] latLonCoordinates = transformationToLatLon.MathTransform.Transform(xyCoordinates);

            // Retourner la latitude et la longitude
            return (latLonCoordinates[1], latLonCoordinates[0]); // lat, lon
        }

        public static void TestProg(double x, double y)
        {
            // Point d'origine (coordonnées connues de la carte DCS Caucasus)
            double originLat = 45.129497;
            double originLon = 34.265514;

            // Conversion XY en latitude/longitude dans la projection UTM 36N
            var (lat, lon) = ConvertXYToLatLon(x, y, originLat, originLon);

            // Afficher les résultats
            MessageBox.Show($"Latitude: {lat}, Longitude: {lon}", $"x: {x}, y: {y}");
        }
  

    }
}




