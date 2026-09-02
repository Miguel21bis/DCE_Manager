using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Text;
using DCE_Manager.Utils;

namespace DCE_Manager
{
    // Régénère entièrement Init/wargame/wargame_zones_init.lua à partir de la liste
    // de WargameZoneData en mémoire (géométrie + champs de jeu). Ce fichier est
    // 100% possédé par DCE_Manager, donc pas de risque de perdre des champs
    // "qu'on ne connaît pas" en le régénérant - contrairement à targetlist_init.lua,
    // pas besoin de patch ligne à ligne.
    internal class Saver_WargameZoneInit
    {
        public static void Save(string pathFile, List<WargameZoneData> zones, WargameState state)
        {
            var sb = new StringBuilder();

            // Etat global en tête de fichier : compteur d'identifiants de formation
            sb.AppendLine("wargame_state = ");
            sb.AppendLine("{");
            sb.AppendLine("\t[\"nextFormationId\"] = " + (state?.NextFormationId ?? 1) + ",");
            sb.AppendLine("}");
            sb.AppendLine();

            sb.AppendLine("wargame_zones = ");
            sb.AppendLine("{");

            foreach (WargameZoneData zone in zones)
            {
                WriteZone(sb, zone);
            }

            sb.AppendLine("}");

            File.WriteAllText(pathFile, sb.ToString());

            FormUtils.LogRegister("Saver_WargameZoneInit | " + zones.Count + " zone(s) écrite(s) dans " + pathFile);
        }

        private static void WriteZone(StringBuilder sb, WargameZoneData zone)
        {
            sb.AppendLine("\t[\"" + Escape(zone.Id) + "\"] = ");
            sb.AppendLine("\t{");

            sb.AppendLine("\t\t[\"shape\"] = \"" + (zone.Shape == WargameZoneShape.Circle ? "circle" : "polygon") + "\",");
            sb.AppendLine("\t\t[\"center\"] = { [\"x\"] = " + FormatNumber(zone.Center.X) + ", [\"y\"] = " + FormatNumber(zone.Center.Y) + " },");
            sb.AppendLine("\t\t[\"radius\"] = " + FormatNumber(zone.Radius) + ",");

            sb.AppendLine("\t\t[\"control\"] = \"" + Escape(zone.Control) + "\",");
            sb.AppendLine("\t\t[\"resupplyModifier\"] = " + FormatNumber(zone.ResupplyModifier) + ",");
            sb.AppendLine("\t\t[\"supplySource\"] = " + FormatNumber(zone.SupplySource) + ",");
            sb.AppendLine("\t\t[\"terrain\"] = \"" + Escape(zone.Terrain) + "\",");
            sb.AppendLine("\t\t[\"supplyRoute\"] = \"" + Escape(zone.SupplyRoute) + "\",");

            WriteFormations(sb, zone.Formations);
            WriteIrregular(sb, zone.Irregular);
            WriteNeighbors(sb, zone.Neighbors);
            WritePoints(sb, zone.DcsPoints);

            sb.AppendLine("\t},");
        }

        private static void WriteFormations(StringBuilder sb, List<WargameFormation> formations)
        {
            sb.AppendLine("\t\t[\"formations\"] = ");
            sb.AppendLine("\t\t{");

            int index = 1;
            foreach (WargameFormation f in formations)
            {
                sb.AppendLine("\t\t\t[" + index + "] = ");
                sb.AppendLine("\t\t\t{");
                sb.AppendLine("\t\t\t\t[\"formationId\"] = " + f.FormationId + ",");
                sb.AppendLine("\t\t\t\t[\"name\"] = \"" + Escape(f.Name) + "\",");
                sb.AppendLine("\t\t\t\t[\"template\"] = \"" + Escape(f.Template) + "\",");
                sb.AppendLine("\t\t\t\t[\"side\"] = \"" + Escape(f.Side) + "\",");
                sb.AppendLine("\t\t\t\t[\"multiplier\"] = " + f.Multiplier + ",");
                sb.AppendLine("\t\t\t\t[\"forcePower\"] = " + FormatNumber(f.ForcePower) + ",");
                sb.AppendLine("\t\t\t\t[\"priority\"] = " + f.Priority + ",");
                sb.AppendLine("\t\t\t\t[\"attributes\"] = \"" + Escape(string.Join(",", f.Attributes)) + "\",");
                sb.AppendLine("\t\t\t\t[\"firepowerMin\"] = " + FormatNumber(f.FirepowerMin) + ",");
                sb.AppendLine("\t\t\t\t[\"firepowerMax\"] = " + FormatNumber(f.FirepowerMax) + ",");
                sb.AppendLine("\t\t\t\t[\"priority\"] = " + f.Priority + ",");
                sb.AppendLine("\t\t\t\t[\"attributes\"] = \"" + Escape(string.Join(",", f.Attributes)) + "\",");
                sb.AppendLine("\t\t\t\t[\"firepowerMin\"] = " + FormatNumber(f.FirepowerMin) + ",");
                sb.AppendLine("\t\t\t\t[\"firepowerMax\"] = " + FormatNumber(f.FirepowerMax) + ",");
                sb.AppendLine("\t\t\t},");
                index++;
            }

            sb.AppendLine("\t\t},");
        }

        private static void WriteIrregular(StringBuilder sb, WargameIrregularMarker irregular)
        {
            sb.AppendLine("\t\t[\"irregular\"] = ");
            sb.AppendLine("\t\t{");
            sb.AppendLine("\t\t\t[\"active\"] = " + (irregular != null && irregular.Active ? "true" : "false") + ",");
            sb.AppendLine("\t\t\t[\"residualStock\"] = " + (irregular?.ResidualStock ?? 0) + ",");
            sb.AppendLine("\t\t},");
        }

        private static void WriteNeighbors(StringBuilder sb, List<string> neighbors)
        {
            sb.AppendLine("\t\t[\"neighbors\"] = ");
            sb.AppendLine("\t\t{");

            int index = 1;
            foreach (string n in neighbors)
            {
                sb.AppendLine("\t\t\t[" + index + "] = \"" + Escape(n) + "\",");
                index++;
            }

            sb.AppendLine("\t\t},");
        }

        private static void WritePoints(StringBuilder sb, List<PointF> points)
        {
            sb.AppendLine("\t\t[\"dcsPoints\"] = ");
            sb.AppendLine("\t\t{");

            int index = 1;
            foreach (PointF p in points)
            {
                sb.AppendLine("\t\t\t[" + index + "] = { [\"x\"] = " + FormatNumber(p.X) + ", [\"y\"] = " + FormatNumber(p.Y) + " },");
                index++;
            }

            sb.AppendLine("\t\t},");
        }

        private static string FormatNumber(double value)
        {
            return value.ToString(CultureInfo.InvariantCulture);
        }

        // Echappe juste les guillemets - suffisant pour des id/nom de template,
        // pas prévu pour du texte libre avec sauts de ligne etc.
        private static string Escape(string value)
        {
            return (value ?? "").Replace("\"", "\\\"");
        }
    }
}
