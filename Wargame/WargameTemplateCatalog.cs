using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using DCE_Manager.Utils;
using NLua;

namespace DCE_Manager
{
    // Type d'unité. Sert surtout de repère pour le campaignMaker et de base aux
    // valeurs par défaut : les coefficients réels sont saisis par template, pas
    // déduits du type (sinon deux blindés ne pourraient jamais différer).
    internal static class WargameUnitType
    {
        public const string Armor = "armor";
        public const string Mech = "mech";
        public const string Infantry = "infantry";
        public const string Artillery = "artillery";
        public const string AirDefense = "airdefense";
        public const string Logistics = "logistics";

        public static readonly string[] All = { Armor, Mech, Infantry, Artillery, AirDefense, Logistics };

        // Valeurs proposées à la création d'une entrée de catalogue, pour éviter
        // au campaignMaker de partir de zéro.
        public static void GetDefaults(string type, out double power, out double attack, out double defense, out double supplyCost)
        {
            switch (type)
            {
                case Armor: power = 20; attack = 3.0; defense = 2.5; supplyCost = 2.0; break;
                case Mech: power = 15; attack = 2.2; defense = 2.0; supplyCost = 1.5; break;
                case Infantry: power = 10; attack = 1.0; defense = 1.8; supplyCost = 0.8; break;
                case Artillery: power = 12; attack = 3.5; defense = 0.8; supplyCost = 2.5; break;
                case AirDefense: power = 8; attack = 0.5; defense = 1.2; supplyCost = 1.2; break;
                case Logistics: power = 4; attack = 0.1; defense = 0.5; supplyCost = 0.5; break;
                default: power = 10; attack = 1.0; defense = 1.0; supplyCost = 1.0; break;
            }
        }
    }

    // Caractéristiques d'un template .stm. Elles appartiennent au template, pas à
    // la zone : un même .stm réutilisé dans 10 zones a toujours les mêmes capacités.
    internal class WargameTemplateEntry
    {
        public string Template;   // nom du fichier .stm, sans extension
        public string Side;       // WargameSide.Blue / Red (déduit du dossier)
        public string Type = WargameUnitType.Infantry;

        public double Attack = 1.0;
        public double Defense = 1.0;
        public double SupplyCost = 1.0;

        // Ce que vaut UN exemplaire de ce template dans le wargame.
        // La ForcePower d'une formation vaut xN x Power.
        public double Power = 10.0;

        // Taux de représentation par défaut (1 groupe posé = N unités système).
        // Surchargeable zone par zone via la colonne xN du panneau d'édition.
        public int DefaultMultiplier = 1;

        // Nombre d'unités retenu pour les calculs de pertes. Pré-rempli par le
        // parser du .stm, mais MODIFIABLE : le comptage automatique inclut les
        // objets statiques (bâtiments, décor) qui ne valent pas forcément une
        // unité de combat, donc le campaignMaker doit pouvoir corriger.
        public int VehicleCount;

        // Comptages bruts issus du .stm, pour information seulement (non éditables).
        public int DetectedDynamic;
        public int DetectedStatic;
    }

    // Catalogue par campagne : Init/wargame/wargame_templates.lua
    //
    // Le catalogue est synchronisé avec les .stm réellement présents dans
    // Templates/wargame_blue et wargame_red : les nouveaux fichiers apparaissent
    // avec des valeurs par défaut, les entrées orphelines sont écartées.
    internal class WargameTemplateCatalog
    {
        public List<WargameTemplateEntry> Entries = new List<WargameTemplateEntry>();

        public WargameTemplateEntry Find(string template)
        {
            if (string.IsNullOrEmpty(template))
                return null;

            return Entries.Find(e => e.Template.Equals(template, StringComparison.OrdinalIgnoreCase));
        }

        public int GetDefaultMultiplier(string template)
        {
            WargameTemplateEntry entry = Find(template);
            return entry != null ? entry.DefaultMultiplier : 1;
        }

        public double GetPower(string template)
        {
            WargameTemplateEntry entry = Find(template);
            return entry != null ? entry.Power : 10.0;
        }

        // Ce que coûte en ForcePower la destruction d'une unité de ce template.
        // = Power / nombre d'unités : détruire tout le template coûte exactement Power.
        public double GetPowerPerVehicle(string template)
        {
            WargameTemplateEntry entry = Find(template);
            if (entry == null || entry.VehicleCount <= 0)
                return 0;

            return entry.Power / entry.VehicleCount;
        }

        // Charge le catalogue et le recale sur les .stm réellement présents.
        public static WargameTemplateCatalog LoadAndSync(string campaignName, WargameCampaignInfo campaignInfo)
        {
            string path = WargameZoneRepository.GetTemplateCatalogPath(campaignName);

            WargameTemplateCatalog catalog = Load(path);
            catalog.SyncWithFolders(campaignInfo);

            return catalog;
        }

        // Ajoute les templates trouvés sur disque et absents du catalogue, retire
        // ceux dont le .stm n'existe plus. Les valeurs déjà saisies sont conservées.
        // Recompte les unités d'un template depuis son .stm et met à jour l'entrée.
        // VehicleCount n'est écrasé que s'il n'a jamais été renseigné, pour ne pas
        // perdre une correction manuelle du campaignMaker.
        public void RecountTemplate(WargameTemplateEntry entry, WargameCampaignInfo campaignInfo, bool forceOverwrite)
        {
            if (entry == null || campaignInfo == null) return;

            string stmPath = campaignInfo.GetTemplateFilePath(entry.Template);
            if (string.IsNullOrEmpty(stmPath)) return;

            WargameTemplateCount count = new Parser_WargameTemplate().CountUnits(stmPath);

            entry.DetectedDynamic = count.Dynamic;
            entry.DetectedStatic = count.Static;

            if (forceOverwrite || entry.VehicleCount <= 0)
                entry.VehicleCount = count.Total;
        }

        public void SyncWithFolders(WargameCampaignInfo campaignInfo)
        {
            if (campaignInfo == null)
                return;

            var onDisk = new List<KeyValuePair<string, string>>(); // template -> side

            foreach (string t in campaignInfo.BlueTemplates)
                onDisk.Add(new KeyValuePair<string, string>(t, WargameSide.Blue));

            foreach (string t in campaignInfo.RedTemplates)
                onDisk.Add(new KeyValuePair<string, string>(t, WargameSide.Red));

            // Ajouts
            foreach (var kvp in onDisk)
            {
                if (Find(kvp.Key) != null)
                    continue;

                double power, attack, defense, supplyCost;
                WargameUnitType.GetDefaults(WargameUnitType.Infantry, out power, out attack, out defense, out supplyCost);

                var newEntry = new WargameTemplateEntry
                {
                    Template = kvp.Key,
                    Side = kvp.Value,
                    Type = WargameUnitType.Infantry,
                    Power = power,
                    Attack = attack,
                    Defense = defense,
                    SupplyCost = supplyCost,
                    DefaultMultiplier = 1,
                };

                // Comptage initial des unités depuis le .stm
                RecountTemplate(newEntry, campaignInfo, true);

                Entries.Add(newEntry);
            }

            // Retraits : un .stm supprimé ne doit pas continuer à polluer le catalogue.
            // Attention, une zone peut encore référencer ce template : le panneau
            // d'édition gère déjà ce cas en gardant la valeur affichée.
            Entries.RemoveAll(e => !onDisk.Exists(k => k.Key.Equals(e.Template, StringComparison.OrdinalIgnoreCase)));

            // Entrées déjà présentes mais jamais comptées (catalogue créé avant que
            // le parser de .stm n'existe) : on les compte maintenant.
            foreach (WargameTemplateEntry entry in Entries)
            {
                if (entry.DetectedDynamic == 0 && entry.DetectedStatic == 0)
                    RecountTemplate(entry, campaignInfo, entry.VehicleCount <= 0);
            }

            Entries.Sort((a, b) => string.Compare(a.Template, b.Template, StringComparison.OrdinalIgnoreCase));
        }

        // -------------------- Lecture --------------------

        public static WargameTemplateCatalog Load(string pathFile)
        {
            var catalog = new WargameTemplateCatalog();

            if (!File.Exists(pathFile))
            {
                FormUtils.LogRegister("WargameTemplateCatalog | pas encore de catalogue : " + pathFile);
                return catalog;
            }

            try
            {
                using (Lua lua = new Lua())
                {
                    lua.DoFile(pathFile);

                    LuaTable root = lua["wargame_templates"] as LuaTable;
                    if (root == null)
                    {
                        FormUtils.LogRegister("WargameTemplateCatalog | table wargame_templates introuvable dans " + pathFile);
                        return catalog;
                    }

                    foreach (object key in root.Keys)
                    {
                        LuaTable entryLua = root[key] as LuaTable;
                        if (entryLua == null) continue;

                        var entry = new WargameTemplateEntry
                        {
                            Template = key.ToString(),
                            Side = entryLua["side"]?.ToString() ?? "",
                            Type = entryLua["type"]?.ToString() ?? WargameUnitType.Infantry,
                            Attack = ToDouble(entryLua["attack"], 1.0),
                            Defense = ToDouble(entryLua["defense"], 1.0),
                            SupplyCost = ToDouble(entryLua["supplyCost"], 1.0),
                            DefaultMultiplier = (int)ToDouble(entryLua["defaultMultiplier"], 1),
                            Power = ToDouble(entryLua["power"], 10.0),
                            VehicleCount = (int)ToDouble(entryLua["vehicleCount"], 0),
                            DetectedDynamic = (int)ToDouble(entryLua["detectedDynamic"], 0),
                            DetectedStatic = (int)ToDouble(entryLua["detectedStatic"], 0),
                        };

                        if (entry.DefaultMultiplier <= 0)
                            entry.DefaultMultiplier = 1;

                        catalog.Entries.Add(entry);
                    }
                }
            }
            catch (Exception ex)
            {
                FormUtils.LogRegister("WargameTemplateCatalog | erreur lecture " + pathFile + " : " + ex.Message);
            }

            return catalog;
        }

        // -------------------- Ecriture --------------------

        public void Save(string pathFile)
        {
            var sb = new StringBuilder();

            sb.AppendLine("wargame_templates = ");
            sb.AppendLine("{");

            foreach (WargameTemplateEntry e in Entries)
            {
                sb.AppendLine("\t[\"" + Escape(e.Template) + "\"] = ");
                sb.AppendLine("\t{");
                sb.AppendLine("\t\t[\"side\"] = \"" + Escape(e.Side) + "\",");
                sb.AppendLine("\t\t[\"type\"] = \"" + Escape(e.Type) + "\",");
                sb.AppendLine("\t\t[\"attack\"] = " + FormatNumber(e.Attack) + ",");
                sb.AppendLine("\t\t[\"defense\"] = " + FormatNumber(e.Defense) + ",");
                sb.AppendLine("\t\t[\"supplyCost\"] = " + FormatNumber(e.SupplyCost) + ",");
                sb.AppendLine("\t\t[\"power\"] = " + FormatNumber(e.Power) + ",");
                sb.AppendLine("\t\t[\"defaultMultiplier\"] = " + e.DefaultMultiplier + ",");
                sb.AppendLine("\t\t[\"vehicleCount\"] = " + e.VehicleCount + ",");
                sb.AppendLine("\t\t[\"detectedDynamic\"] = " + e.DetectedDynamic + ",");
                sb.AppendLine("\t\t[\"detectedStatic\"] = " + e.DetectedStatic + ",");
                sb.AppendLine("\t},");
            }

            sb.AppendLine("}");

            File.WriteAllText(pathFile, sb.ToString());

            FormUtils.LogRegister("WargameTemplateCatalog | " + Entries.Count + " template(s) écrit(s) dans " + pathFile);
        }

        private static string FormatNumber(double value)
        {
            return value.ToString(CultureInfo.InvariantCulture);
        }

        private static string Escape(string value)
        {
            return (value ?? "").Replace("\"", "\\\"");
        }

        private static double ToDouble(object value, double fallback)
        {
            if (value == null) return fallback;
            if (value is double d) return d;

            double parsed;
            if (double.TryParse(value.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out parsed))
                return parsed;

            return fallback;
        }
    }
}
