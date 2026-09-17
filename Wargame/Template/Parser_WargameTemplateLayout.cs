using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using DCE_Manager.Utils;
using NLua;

namespace DCE_Manager
{
    // Une unité individuelle d'un template .stm, avec sa position D'ORIGINE
    // (telle que capturée dans l'éditeur DCS quand le template a été construit -
    // absolue, sans rapport avec l'endroit où le template sera posé en jeu).
    internal class WargameTemplateUnit
    {
        public string Name;
        public string GroupName;

        // "vehicle", "static", "ship", "plane" ou "helicopter"
        public string Category;

        public PointF Position;
        public double Heading;
    }

    // Lit un template .stm en entier : nom, position et catégorie de CHAQUE unité,
    // pas juste leur nombre (voir Parser_WargameTemplate pour le comptage utilisé
    // par le catalogue - celui-ci sert au solveur de placement, qui a besoin de la
    // disposition interne réelle du template pour la reproduire ailleurs sur la carte.
    internal class Parser_WargameTemplateLayout
    {
        private static readonly string[] Categories = { "vehicle", "static", "ship", "plane", "helicopter" };

        public List<WargameTemplateUnit> LoadLayout(string stmPath)
        {
            var result = new List<WargameTemplateUnit>();

            if (!File.Exists(stmPath))
            {
                FormUtils.LogRegister("Parser_WargameTemplateLayout | fichier introuvable : " + stmPath);
                return result;
            }

            try
            {
                using (Lua lua = new Lua())
                {
                    lua.DoString(@"
                        os = nil
                        io = nil
                        file = nil
                        debug = nil
                    ");

                    lua.DoFile(stmPath);

                    LuaTable root = lua["staticTemplate"] as LuaTable;
                    LuaTable coalitions = root?["coalition"] as LuaTable;

                    if (coalitions == null)
                    {
                        FormUtils.LogRegister("Parser_WargameTemplateLayout | staticTemplate.coalition introuvable dans " + stmPath);
                        return result;
                    }

                    foreach (object coalitionKey in coalitions.Keys)
                    {
                        LuaTable coalition = coalitions[coalitionKey] as LuaTable;
                        LuaTable countries = coalition?["country"] as LuaTable;
                        if (countries == null) continue;

                        // L'index de pays n'est PAS forcément 1, 2, 3... : DCS garde le
                        // numéro de slot d'origine de la mission dont le template a été
                        // extrait (vu sur un vrai fichier : un seul pays, à l'index 5).
                        // Il faut donc parcourir toutes les clés présentes, pas supposer
                        // une séquence continue à partir de 1.
                        foreach (object countryKey in countries.Keys)
                        {
                            LuaTable country = countries[countryKey] as LuaTable;
                            if (country != null)
                                ReadCountry(country, result);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                FormUtils.LogRegister("Parser_WargameTemplateLayout | erreur parsing " + stmPath + " : " + ex.Message);
            }

            return result;
        }

        private void ReadCountry(LuaTable country, List<WargameTemplateUnit> result)
        {
            foreach (string category in Categories)
                ReadCategory(country[category] as LuaTable, category, result);
        }

        private void ReadCategory(LuaTable categoryTable, string categoryName, List<WargameTemplateUnit> result)
        {
            LuaTable groups = categoryTable?["group"] as LuaTable;
            if (groups == null) return;

            int groupIndex = 1;
            while (true)
            {
                LuaTable group = groups[groupIndex] as LuaTable;
                if (group == null) break;

                string groupName = group["name"]?.ToString() ?? ("group_" + groupIndex);
                LuaTable units = group["units"] as LuaTable;

                if (units != null)
                {
                    int unitIndex = 1;
                    while (true)
                    {
                        LuaTable unit = units[unitIndex] as LuaTable;
                        if (unit == null) break;

                        result.Add(new WargameTemplateUnit
                        {
                            Name = unit["name"]?.ToString() ?? (groupName + "-" + unitIndex),
                            GroupName = groupName,
                            Category = categoryName,
                            Position = new PointF(
                                (float)ToDouble(unit["x"]),
                                (float)ToDouble(unit["y"])),
                            Heading = ToDouble(unit["heading"]) * 180.0 / Math.PI, // radians (DCS) -> degrés (interne)
                        });

                        unitIndex++;
                    }
                }

                // Un groupe "static" DCS peut aussi porter x/y directement sur le
                // groupe plutôt que sur une unité (selon comment le template a été
                // construit) - si aucune unité n'a été trouvée, on retombe dessus
                // pour ne pas perdre l'objet silencieusement.
                if (units == null || CountLuaEntries(units) == 0)
                {
                    object groupX = group["x"], groupY = group["y"];
                    if (groupX != null && groupY != null)
                    {
                        result.Add(new WargameTemplateUnit
                        {
                            Name = groupName,
                            GroupName = groupName,
                            Category = categoryName,
                            Position = new PointF((float)ToDouble(groupX), (float)ToDouble(groupY)),
                            Heading = ToDouble(group["heading"]),
                        });
                    }
                }

                groupIndex++;
            }
        }

        private int CountLuaEntries(LuaTable table)
        {
            int n = 0;
            while (table[n + 1] != null) n++;
            return n;
        }

        private double ToDouble(object value)
        {
            if (value == null) return 0;
            if (value is double d) return d;

            double parsed;
            if (double.TryParse(value.ToString(), System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out parsed))
                return parsed;

            return 0;
        }
    }
}
