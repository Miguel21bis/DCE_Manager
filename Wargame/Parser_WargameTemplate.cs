using System;
using System.Collections.Generic;
using System.IO;
using DCE_Manager.Utils;
using NLua;

namespace DCE_Manager
{
    // Résultat du comptage d'un template .stm
    internal class WargameTemplateCount
    {
        // Unités mobiles : vehicle, ship, plane, helicopter
        public int Dynamic;

        // Unités statiques : bâtiments, décor, objets posés
        public int Static;

        public int Total => Dynamic + Static;
    }

    // Compte les unités d'un template .stm.
    //
    // Un .stm est du Lua brut (pas zippé, contrairement au .miz), structuré ainsi :
    //   staticTemplate.coalition.<camp>.country[n].<catégorie>.group[n].units[n]
    // avec <catégorie> valant "vehicle", "static", "ship", "plane" ou "helicopter".
    //
    // Le comptage est indicatif : les objets statiques (bâtiments, décor) sont
    // comptés comme des unités alors qu'ils ne représentent pas forcément de la
    // force de combat. C'est pourquoi la valeur reste modifiable à la main dans
    // le catalogue.
    internal class Parser_WargameTemplate
    {
        // Catégories considérées comme des unités mobiles
        private static readonly string[] DynamicCategories = { "vehicle", "ship", "plane", "helicopter" };

        public WargameTemplateCount CountUnits(string stmPath)
        {
            var result = new WargameTemplateCount();

            if (!File.Exists(stmPath))
            {
                FormUtils.LogRegister("Parser_WargameTemplate | fichier introuvable : " + stmPath);
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
                        FormUtils.LogRegister("Parser_WargameTemplate | staticTemplate.coalition introuvable dans " + stmPath);
                        return result;
                    }

                    // Les camps sont nommés (neutrals / blue / red), pas indexés
                    foreach (object coalitionKey in coalitions.Keys)
                    {
                        LuaTable coalition = coalitions[coalitionKey] as LuaTable;
                        LuaTable countries = coalition?["country"] as LuaTable;
                        if (countries == null) continue;

                        int countryIndex = 1;
                        while (true)
                        {
                            LuaTable country = countries[countryIndex] as LuaTable;
                            if (country == null) break;

                            CountCountry(country, result);
                            countryIndex++;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                FormUtils.LogRegister("Parser_WargameTemplate | erreur parsing " + stmPath + " : " + ex.Message);
            }

            return result;
        }

        private void CountCountry(LuaTable country, WargameTemplateCount result)
        {
            // Statiques
            result.Static += CountCategory(country["static"] as LuaTable);

            // Mobiles
            foreach (string category in DynamicCategories)
                result.Dynamic += CountCategory(country[category] as LuaTable);
        }

        private int CountCategory(LuaTable category)
        {
            LuaTable groups = category?["group"] as LuaTable;
            if (groups == null) return 0;

            int total = 0;
            int groupIndex = 1;

            while (true)
            {
                LuaTable group = groups[groupIndex] as LuaTable;
                if (group == null) break;

                LuaTable units = group["units"] as LuaTable;
                if (units != null)
                {
                    int unitIndex = 1;
                    while (units[unitIndex] != null)
                    {
                        total++;
                        unitIndex++;
                    }
                }

                groupIndex++;
            }

            return total;
        }
    }
}
