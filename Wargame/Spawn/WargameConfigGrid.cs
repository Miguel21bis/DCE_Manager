using System;
using System.Collections.Generic;
using System.Globalization;
using DCE_Manager.Utils;
using NLua;

namespace DCE_Manager
{
    // Lecture des grilles (tableaux lignes x colonnes) de camp.wargame_config, dans camp_init.lua.
    //
    // Une grille ressemble à celle des réparations de conf_mod :
    //
    //     supply_route = {  -- @ui matrix ... rows="none:No road,road:Road" cols="1:Recovery x,2:Combat x"
    //         none = { 0.25, 0.25 },     -- ligne "none" : colonne 1, colonne 2
    //         road = { 1.0,  1.0  },
    //     }
    //
    // Dans le Lua, les colonnes sont POSITIONNELLES (c'est ce que sait éditer l'interface). Le code ne
    // leur donne un sens qu'ici, à l'appel :
    //
    //     ReadColumn(cfg, "supply_route", 1, config.RouteRecovery, 0.0, 3.0);   // colonne 1 = récupération
    //     ReadColumn(cfg, "supply_route", 2, config.RouteCombat,   0.1, 3.0);   // colonne 2 = combat
    //
    // Ajouter une colonne plus tard = l'ajouter À LA FIN de chaque ligne : rien d'autre ne se décale.
    //
    // Tolérant : grille, ligne ou case absente, texte au lieu d'un nombre -> la valeur par défaut reste
    // en place. Valeur hors limites -> ramenée à la limite. Jamais d'exception ; chaque cas est écrit
    // dans le log. Les variables de TEST de la preview n'ont rien à faire ici (elles restent dans la Form).
    internal static class WargameConfigGrid
    {
        // Lit UNE colonne d'une grille et range chaque valeur dans "target" (clé = nom de la ligne).
        // "target" contient déjà les valeurs par défaut : seules les lignes qu'il connaît sont lues.
        public static void ReadColumn(LuaTable cfg, string gridKey, int column,
            Dictionary<string, double> target, double min, double max)
        {
            LuaTable grid = cfg == null ? null : cfg[gridKey] as LuaTable;
            if (grid == null)
                return;   // grille absente (camp_init ancien) : on garde les défauts du code

            // copie des clés : on modifie "target" pendant la boucle
            foreach (string row in new List<string>(target.Keys))
            {
                LuaTable rowTable = grid[row] as LuaTable;
                if (rowTable == null)
                {
                    FormUtils.LogRegister("WargameConfigGrid | " + gridKey + " : ligne '" + row + "' absente, valeur par défaut gardée");
                    continue;
                }

                object raw = rowTable[column];
                if (raw == null)
                {
                    FormUtils.LogRegister("WargameConfigGrid | " + gridKey + "." + row + " : colonne " + column + " absente, valeur par défaut gardée");
                    continue;
                }

                // Convert.ToString en InvariantCulture : NLua rend un double, et un ToString() "nu"
                // écrirait 0,25 sur un Windows en français (le TryParse invariant échouerait ensuite).
                double value;
                if (!double.TryParse(Convert.ToString(raw, CultureInfo.InvariantCulture),
                        NumberStyles.Float, CultureInfo.InvariantCulture, out value) || double.IsNaN(value))
                {
                    FormUtils.LogRegister("WargameConfigGrid | " + gridKey + "." + row + " colonne " + column + " : '" + raw + "' n'est pas un nombre, valeur par défaut gardée");
                    continue;
                }

                if (value < min || value > max)
                {
                    double clamped = Math.Max(min, Math.Min(max, value));
                    FormUtils.LogRegister("WargameConfigGrid | " + gridKey + "." + row + " colonne " + column + " : "
                        + value.ToString(CultureInfo.InvariantCulture) + " hors limites [" + min.ToString(CultureInfo.InvariantCulture)
                        + " ; " + max.ToString(CultureInfo.InvariantCulture) + "], ramené à " + clamped.ToString(CultureInfo.InvariantCulture));
                    value = clamped;
                }

                target[row] = value;
            }
        }
    }
}
