using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DCE_Manager.Utils;
using NLua;

namespace DCE_Manager
{
    // Contexte "campagne" du wargame, regroupé ici pour éviter d'éparpiller :
    //  - les libellés affichés des deux camps, lus dans Init/camp_init.lua
    //    (country_blue / country_red) : le campaignMaker voit "US" / "Iran",
    //    jamais "blue" / "red"
    //  - la liste des templates .stm disponibles par camp, lus dans
    //    Templates/wargame_blue et Templates/wargame_red
    internal class WargameCampaignInfo
    {
        public string BlueLabel = "Blue";
        public string RedLabel = "Red";

        public List<string> BlueTemplates = new List<string>();
        public List<string> RedTemplates = new List<string>();

        // Dossiers d'où viennent les templates, gardés pour pouvoir retrouver le
        // chemin d'un .stm et le parser (comptage des unités).
        public string BlueFolder = "";
        public string RedFolder = "";

        public static WargameCampaignInfo Load(string campaignName)
        {
            var info = new WargameCampaignInfo();

            string campaignFolder = WargameZoneRepository.GetCampaignFolder(campaignName);

            info.LoadSideLabels(Path.Combine(campaignFolder, "Init", "camp_init.lua"));

            info.BlueFolder = Path.Combine(campaignFolder, "Init", "Wargame", "Templates_blue");
            info.RedFolder = Path.Combine(campaignFolder, "Init", "Wargame", "Templates_red");

            info.BlueTemplates = ListTemplates(info.BlueFolder);
            info.RedTemplates = ListTemplates(info.RedFolder);

            return info;
        }

        // Libellé affiché pour une valeur de Control (WargameSide.*)
        public string GetLabel(string control)
        {
            if (control == WargameSide.Blue) return BlueLabel;
            if (control == WargameSide.Red) return RedLabel;
            return "Contested";
        }

        // Templates proposables pour une zone : ceux de son camp, ou les deux
        // si la zone est contestée (les deux camps peuvent y avoir des unités).
        public List<string> GetTemplatesFor(string control)
        {
            if (control == WargameSide.Blue) return BlueTemplates;
            if (control == WargameSide.Red) return RedTemplates;

            return BlueTemplates.Concat(RedTemplates).ToList();
        }

        // Retrouve le camp d'un template, utile quand on ajoute une unité dans une
        // zone contestée (où les deux listes sont proposées ensemble).
        public string GetSideOfTemplate(string template)
        {
            if (BlueTemplates.Contains(template, StringComparer.OrdinalIgnoreCase))
                return WargameSide.Blue;

            if (RedTemplates.Contains(template, StringComparer.OrdinalIgnoreCase))
                return WargameSide.Red;

            return "";
        }

        // Chemin complet du .stm d'un template, ou "" s'il est introuvable.
        public string GetTemplateFilePath(string template)
        {
            if (string.IsNullOrEmpty(template))
                return "";

            string side = GetSideOfTemplate(template);
            string folder = side == WargameSide.Blue ? BlueFolder : side == WargameSide.Red ? RedFolder : "";

            if (string.IsNullOrEmpty(folder))
                return "";

            return Path.Combine(folder, template + ".stm");
        }

        private void LoadSideLabels(string campInitPath)
        {
            if (!File.Exists(campInitPath))
            {
                FormUtils.LogRegister("WargameCampaignInfo | camp_init.lua introuvable : " + campInitPath);
                return;
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

                    lua.DoFile(campInitPath);

                    LuaTable camp = lua["camp"] as LuaTable;
                    if (camp == null)
                    {
                        FormUtils.LogRegister("WargameCampaignInfo | table 'camp' introuvable dans " + campInitPath);
                        return;
                    }

                    LuaTable wargameConfig = camp["wargame_config"] as LuaTable;
                    if (wargameConfig == null)
                    {
                        FormUtils.LogRegister("WargameCampaignInfo | table 'camp.wargame_config' absente de " + campInitPath);
                        return;
                    }

                    string blue = wargameConfig["country_blue"]?.ToString();
                    string red = wargameConfig["country_red"]?.ToString();

                    if (!string.IsNullOrWhiteSpace(blue)) BlueLabel = blue;
                    if (!string.IsNullOrWhiteSpace(red)) RedLabel = red;
                }
            }
            catch (Exception ex)
            {
                // camp_init pas encore recalé sur la référence (champs absents), ou
                // fichier illisible : on garde les libellés par défaut plutôt que de planter.
                FormUtils.LogRegister("WargameCampaignInfo | erreur lecture camp_init.lua : " + ex.Message);
            }
        }

        private static List<string> ListTemplates(string folder)
        {
            var list = new List<string>();

            if (!Directory.Exists(folder))
            {
                FormUtils.LogRegister("WargameCampaignInfo | dossier de templates absent : " + folder);
                return list;
            }

            foreach (string file in Directory.GetFiles(folder, "*.stm"))
                list.Add(Path.GetFileNameWithoutExtension(file));

            list.Sort(StringComparer.OrdinalIgnoreCase);

            return list;
        }
    }
}
