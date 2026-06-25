
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using DCE_Manager.Parameters;
using DCE_Manager.Utils;
using static DCE_Manager.Utils.FormUtils;

namespace DCE_Manager
{
    internal class TriggerParser
    {
        // Cette fonction charge et retourne le texte du briefing de campagne.
        // Pourquoi : on sépare le parsing du briefing de l'interface CampaignEdit.
        public string LoadBriefingCampaign(string campaignName)
        {
            string briefingCampaign = "";

            string campTrigger =
                SharedData.textBox_SavedGames +
                @"\Mods\tech\DCE\Missions\Campaigns\" +
                campaignName +
                @"\Init\camp_triggers_init.lua";

            FormUtils.LogRegister(
                "TriggerParser.cs: campTrigger = " + campTrigger);

            ParamCampaign.NameFileParse = campTrigger;

            var luaObj = LuaParser.ParseFile(campTrigger, "camp_triggers");

            if (luaObj == null || luaObj.luaobj == null)
            {
                FormUtils.LogRegister(
                    "TriggerParser.cs: luaObj null");
                return "";
            }

            var root = luaObj.luaobj as Dictionary<string, LuaObject>;

            if (root == null)
            {
                FormUtils.LogRegister(
                    "TriggerParser.cs: root null");
                return "";
            }

            foreach (var entry1 in root)
            {
                Dictionary<string, LuaObject> trigger = null;

                // Cas 1 : la clé est directement "Campaign Briefing"
                if (entry1.Key.Equals(
                    "Campaign Briefing",
                    StringComparison.OrdinalIgnoreCase))
                {
                    trigger = entry1.Value.luaobj as Dictionary<string, LuaObject>;
                }
                else
                {
                    // Cas 2 : une table possède name = "Campaign Briefing"
                    var tmp = entry1.Value.luaobj as Dictionary<string, LuaObject>;

                    if (tmp != null &&
                        tmp.ContainsKey("name") &&
                        tmp["name"].luaobj != null &&
                        tmp["name"].luaobj.ToString().Equals(
                            "Campaign Briefing",
                            StringComparison.OrdinalIgnoreCase))
                    {
                        trigger = tmp;
                    }
                }

                if (trigger == null)
                    continue;

                if (!trigger.ContainsKey("action"))
                    continue;

                var actions =
                    trigger["action"].luaobj as Dictionary<string, LuaObject>;

                if (actions == null)
                    continue;

                foreach (var action in actions)
                {
                    string val = action.Value.luaobj?.ToString();

                    if (string.IsNullOrEmpty(val))
                        continue;

                    if (val.Contains("Action.AddImage"))
                        break;

                    if (!val.Contains("Action.Text("))
                        continue;

                    int start = val.IndexOf("Action.Text(");

                    if (start < 0)
                        continue;

                    start += "Action.Text(".Length;

                    int end = val.LastIndexOf(")");

                    if (end <= start)
                        continue;

                    string content = val.Substring(start, end - start).Trim();

                    if ((content.StartsWith("\"") && content.EndsWith("\"")) ||
                        (content.StartsWith("'") && content.EndsWith("'")))
                    {
                        content = content.Substring(1, content.Length - 2);
                    }

                    briefingCampaign += content + "\r\n";
                }
            }

            briefingCampaign = briefingCampaign
                .Replace("\\n", "\r\n")
                .Replace("\\\"", "\"");

            briefingCampaign = Regex.Replace(
                briefingCampaign,
                @"\[.\]=",
                "");

            //FormUtils.LogRegister( "TriggerParser.cs: briefing length = " +  briefingCampaign.Length);

            return briefingCampaign.Trim();
        }
    }
}
