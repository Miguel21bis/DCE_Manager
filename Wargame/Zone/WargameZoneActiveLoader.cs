using System.Collections.Generic;
using System.Globalization;
using System.IO;
using DCE_Manager.Utils;
using NLua;
using System.Linq;

namespace DCE_Manager
{
    // Lit Active/wargame_zones.lua (écrit par Saver_WargameZoneActive) et applique
    // les champs évolutifs (control, resupplyModifier, forceGroups, irregular)
    // PAR-DESSUS une liste de zones déjà chargée via WargameZoneInitLoader (qui
    // fournit Id, géométrie et Neighbors - inchangés ici).
    //
    // Si le fichier Active n'existe pas encore (toute première fois), les zones
    // gardent simplement les valeurs posées dans l'Init - rien à faire de spécial.
    internal class WargameZoneActiveLoader
    {
        public void ApplyActiveState(string pathFile, List<WargameZoneData> zones)
        {
            if (!File.Exists(pathFile))
            {
                FormUtils.LogRegister("WargameZoneActiveLoader | pas de fichier Active, on garde l'état Init : " + pathFile);
                return;
            }

            var zonesById = new Dictionary<string, WargameZoneData>();
            foreach (WargameZoneData zone in zones)
                zonesById[zone.Id] = zone;

            // Toutes les formations de l'Init, toutes zones confondues, repérées AVANT de
            // toucher aux zones : une formation repliée dans une autre zone par le combat
            // doit garder Priority / Attributes / Firepower, qui ne vivent que dans l'Init.
            var allFormationsById = new Dictionary<int, WargameFormation>();
            foreach (WargameZoneData zone in zones)
            {
                foreach (WargameFormation f in zone.Formations)
                    allFormationsById[f.FormationId] = f;
            }

            using (Lua lua = new Lua())
            {
                lua.DoFile(pathFile);

                LuaTable zonesLua = lua["wargame_zones_active"] as LuaTable;
                if (zonesLua == null)
                {
                    FormUtils.LogRegister("WargameZoneActiveLoader | table wargame_zones_active introuvable dans " + pathFile);
                    return;
                }

                foreach (object key in zonesLua.Keys)
                {
                    string zoneId = key.ToString();

                    if (!zonesById.TryGetValue(zoneId, out WargameZoneData zone))
                    {
                        // Zone présente dans l'Active mais plus dans l'Init (zone renommée/supprimée
                        // côté wargame_zone.miz) - on l'ignore plutôt que de planter.
                        FormUtils.LogRegister("WargameZoneActiveLoader | zone '" + zoneId + "' ignorée (absente de l'Init)");
                        continue;
                    }

                    LuaTable zoneLua = zonesLua[key] as LuaTable;
                    if (zoneLua == null) continue;

                    string control = zoneLua["control"]?.ToString();
                    if (!string.IsNullOrEmpty(control)) zone.Control = control;

                    zone.ResupplyModifier = ToDouble(zoneLua["resupplyModifier"], zone.ResupplyModifier);
                    MergeFormations(zone, zoneLua["formations"] as LuaTable, allFormationsById);
                    //zone.Irregular = LoadIrregular(zoneLua["irregular"] as LuaTable);
                }
            }
        }

        // Met à jour les formations déjà chargées depuis l'Init (par FormationId)
        // plutôt que de les remplacer - sinon Priority/Attributes/Firepower, qui ne
        // vivent que dans l'Init, seraient perdus à chaque application de l'Active.
        // Met à jour les formations déjà chargées depuis l'Init (par FormationId)
        // plutôt que de les remplacer - sinon Priority/Attributes/Firepower, qui ne
        // vivent que dans l'Init, seraient perdus à chaque application de l'Active.
        // allById : toutes les formations de l'Init, quelle que soit leur zone d'origine
        // (une formation repliée par le combat est listée ici sous une autre zone).
        private void MergeFormations(WargameZoneData zone, LuaTable table, Dictionary<int, WargameFormation> allById)
        {
            if (table == null) return;

            var merged = new List<WargameFormation>();

            int i = 1;
            while (true)
            {
                LuaTable entry = table[i] as LuaTable;
                if (entry == null) break;

                int formationId = (int)ToDouble(entry["formationId"], 0);

                if (!allById.TryGetValue(formationId, out WargameFormation f))
                {
                    // Inconnue de l'Init (formation ajoutée en vue Active) - reconstruite
                    // a minima plutôt que perdue.
                    f = new WargameFormation { FormationId = formationId };
                }

                f.Name = entry["name"]?.ToString() ?? f.Name;
                f.Template = entry["template"]?.ToString() ?? f.Template;
                f.Side = entry["side"]?.ToString() ?? f.Side;
                f.Multiplier = (int)ToDouble(entry["multiplier"], f.Multiplier);
                if (f.Multiplier <= 0) f.Multiplier = 1;
                f.ForcePower = ToDouble(entry["forcePower"], f.ForcePower);
                f.SpawnGeneration = (int)ToDouble(entry["spawnGeneration"], f.SpawnGeneration);
                if (f.SpawnGeneration <= 0) f.SpawnGeneration = 1;

                merged.Add(f);
                i++;
            }

            zone.Formations = merged;
        }

        private double ToDouble(object value, double fallback)
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
