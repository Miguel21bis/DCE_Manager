using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using DCE_Manager.Utils;
using NLua;

namespace DCE_Manager
{
    // Une unité issue de la mission générée, appartenant à un groupe posé par le
    // wargame. Purement indicatif : sert à afficher un point sur la carte,
    // rien ici n'alimente le modèle de jeu.
    internal class WargameMissionUnit
    {
        public PointF Position;         // coordonnées DCS (x = nord, y = est)
        public string Side = "";        // "blue" / "red" / "neutrals"
        public string Category = "";    // vehicle, static, ship, plane, helicopter
        public string UnitName = "";    // ex: "BLUE_062_INFANTRY-3"
        public string GroupName = "";   // ex: "BLUE_062_INFANTRY"
        public string TemplateGroup = "";  // wargameTemplateGroupName, ex: "CBF 1-1"
        public int FormationId;         // wargameFormationId du groupe, 0 si absent

        public bool IsStatic => Category == "static";
    }

    // Lit la position des unités POSÉES PAR LE WARGAME, pour les afficher sur la carte.
    //
    // Deux sources possibles, même structure interne à un niveau près :
    //  1) Active/last_Mission.lua (la mission extraite du .miz), si présent :
    //       last_Mission.coalition.<camp>.country[n].<catégorie>.group[n].units[n]
    //  2) en secours, Active/oob_ground.lua (toujours présent dès FirstMission) :
    //       oob_ground.<camp>[n].<catégorie>.group[n].units[n]
    //     oob_ground.<camp> est une copie directe de mission.coalition.<camp>.country
    //     (voir UTIL_ResetCampaign.lua) : il manque juste le niveau "country".
    //
    // Pourquoi le secours : last_Mission.lua n'est écrit ni par DCE_Manager ni par
    // les scripts BAT/DEBRIEF - il peut donc manquer alors que la campagne tourne
    // très bien (cas réel sur Qeshm-Blue), et la carte restait vide sans rien dire.
    //
    // Attention, deux pièges :
    //  - la variable globale s'appelle "last_Mission", pas "mission"
    //  - le marqueur wargameTemplateGroupName est porté par le GROUPE, pas par
    //    l'unité : on filtre au niveau groupe, puis on prend toutes ses unités
    //
    // L'absence des deux fichiers est un cas NORMAL (campagne jamais lancée) : on
    // renvoie une liste vide sans rien casser, l'affichage se contente de ne rien montrer.
    internal class Parser_WargameMissionUnits
    {
        private static readonly string[] Categories = { "vehicle", "static", "ship", "plane", "helicopter" };

        public List<WargameMissionUnit> Load(string missionLuaPath)
        {
            var result = new List<WargameMissionUnit>();

            string sourcePath = missionLuaPath;
            bool isOobGround = false;

            if (string.IsNullOrEmpty(sourcePath) || !File.Exists(sourcePath))
            {
                // last_Mission.lua absent : on se rabat sur oob_ground.lua, dans le même dossier Active
                string oobPath = string.IsNullOrEmpty(missionLuaPath)
                    ? null
                    : Path.Combine(Path.GetDirectoryName(missionLuaPath), "oob_ground.lua");

                if (oobPath == null || !File.Exists(oobPath))
                {
                    // Cas normal, pas une erreur : simple trace pour le diagnostic
                    FormUtils.LogRegister("Parser_WargameMissionUnits | pas de mission ni d'oob_ground à afficher : " + missionLuaPath);
                    return result;
                }

                FormUtils.LogRegister("Parser_WargameMissionUnits | last_Mission.lua absent, lecture de oob_ground.lua à la place");
                sourcePath = oobPath;
                isOobGround = true;
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

                    lua.DoFile(sourcePath);

                    // Table des camps (blue / red / neutrals), selon la source
                    LuaTable coalitions;
                    if (isOobGround)
                    {
                        coalitions = lua["oob_ground"] as LuaTable;
                    }
                    else
                    {
                        // "last_Mission" d'abord, "mission" en secours au cas où le
                        // fichier viendrait d'une autre source
                        LuaTable root = lua["last_Mission"] as LuaTable ?? lua["mission"] as LuaTable;
                        coalitions = root?["coalition"] as LuaTable;
                    }

                    if (coalitions == null)
                    {
                        FormUtils.LogRegister("Parser_WargameMissionUnits | table des camps introuvable dans " + sourcePath);
                        return result;
                    }

                    // Les camps sont nommés (blue / red / neutrals), pas indexés
                    foreach (object coalitionKey in coalitions.Keys)
                    {
                        string side = coalitionKey.ToString();

                        LuaTable coalition = coalitions[coalitionKey] as LuaTable;

                        // oob_ground : le camp EST directement la liste des pays.
                        // mission : il faut descendre dans ["country"].
                        LuaTable countries = isOobGround ? coalition : coalition?["country"] as LuaTable;
                        if (countries == null) continue;

                        int countryIndex = 1;
                        while (true)
                        {
                            LuaTable country = countries[countryIndex] as LuaTable;
                            if (country == null) break;

                            foreach (string category in Categories)
                                CollectCategory(country[category] as LuaTable, side, category, result);

                            countryIndex++;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                // Un fichier illisible ne doit jamais empêcher d'ouvrir l'éditeur
                FormUtils.LogRegister("Parser_WargameMissionUnits | erreur lecture " + sourcePath + " : " + ex.Message);
            }

            FormUtils.LogRegister("Parser_WargameMissionUnits | " + result.Count + " unité(s) wargame lue(s) depuis " + Path.GetFileName(sourcePath));

            return result;
        }

        private void CollectCategory(LuaTable category, string side, string categoryName, List<WargameMissionUnit> result)
        {
            LuaTable groups = category?["group"] as LuaTable;
            if (groups == null) return;

            int groupIndex = 1;
            while (true)
            {
                LuaTable group = groups[groupIndex] as LuaTable;
                if (group == null) break;

                groupIndex++;

                // Filtre : seuls les groupes posés par le wargame nous intéressent.
                // Sans ça on afficherait toute la mission (aéroports, décor, etc).
                string templateGroup = group["wargameTemplateGroupName"]?.ToString();
                if (string.IsNullOrEmpty(templateGroup))
                    continue;

                // Groupe entièrement détruit (oob_ground garde les morts) : rien à montrer
                if (group["dead"] is bool groupDead && groupDead)
                    continue;

                string groupName = group["name"]?.ToString() ?? "";
                int formationId = (int)ToDouble(group["wargameFormationId"]);

                LuaTable units = group["units"] as LuaTable;
                if (units == null) continue;

                int unitIndex = 1;
                while (true)
                {
                    LuaTable unit = units[unitIndex] as LuaTable;
                    if (unit == null) break;

                    unitIndex++;

                    // Unité détruite : pas de point, sinon la carte montrerait des
                    // formations "pleines" alors qu'elles ont pris des pertes
                    if (unit["dead"] is bool unitDead && unitDead)
                        continue;

                    result.Add(new WargameMissionUnit
                    {
                        Position = new PointF((float)ToDouble(unit["x"]), (float)ToDouble(unit["y"])),
                        Side = side,
                        Category = categoryName,
                        UnitName = unit["name"]?.ToString() ?? "",
                        GroupName = groupName,
                        TemplateGroup = templateGroup,
                        FormationId = formationId,
                    });
                }
            }
        }

        private double ToDouble(object value)
        {
            if (value == null) return 0;
            if (value is double d) return d;

            double parsed;
            if (double.TryParse(value.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out parsed))
                return parsed;

            return 0;
        }
    }
}
