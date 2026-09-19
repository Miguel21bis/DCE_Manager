using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using DCE_Manager.Utils;

namespace DCE_Manager
{
    // Régénère entièrement Active/wargame_zones.lua : uniquement les champs qui
    // évoluent en cours de campagne (control, resupplyModifier, forceGroups,
    // irregular). La géométrie et les neighbors sont statiques et restent dans
    // wargame_zones_init.lua, pas la peine de les dupliquer ici.
    internal class Saver_WargameZoneActive
    {
        public static void Save(string pathFile, List<WargameZoneData> zones)
        {
            var sb = new StringBuilder();

            sb.AppendLine("wargame_zones_active = ");
            sb.AppendLine("{");

            foreach (WargameZoneData zone in zones)
            {
                WriteZone(sb, zone);
            }

            sb.AppendLine("}");

            File.WriteAllText(pathFile, sb.ToString());

            FormUtils.LogRegister("Saver_WargameZoneActive | " + zones.Count + " zone(s) écrite(s) dans " + pathFile);
        }

        private static void WriteZone(StringBuilder sb, WargameZoneData zone)
        {
            sb.AppendLine("\t[\"" + Escape(zone.Id) + "\"] = ");
            sb.AppendLine("\t{");

            sb.AppendLine("\t\t[\"control\"] = \"" + Escape(zone.Control) + "\",");
            sb.AppendLine("\t\t[\"resupplyModifier\"] = " + zone.ResupplyModifier.ToString(CultureInfo.InvariantCulture) + ",");

            WriteFormations(sb, zone.Formations);
            WriteIrregular(sb, zone.Irregular);

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
                sb.AppendLine("\t\t\t\t[\"spawnGeneration\"] = " + f.SpawnGeneration + ",");
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

        private static string FormatNumber(double value)
        {
            return value.ToString(CultureInfo.InvariantCulture);
        }

        // Echappe juste les guillemets - suffisant pour des id/nom de template.
        private static string Escape(string value)
        {
            return (value ?? "").Replace("\"", "\\\"");
        }
    }
}
