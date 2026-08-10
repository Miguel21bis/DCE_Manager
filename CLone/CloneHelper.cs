using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using DCE_Manager.Utils;
using static System.Net.Mime.MediaTypeNames;

namespace DCE_Manager.Clone
{
    internal class CloneHelper
    {

       

        //public static void RenameMissionFiles(string campaignPath, string oldName, string newName)
        //{
        //    FormUtils.LogRegister($"Renaming mission files from '{oldName}' to '{newName}' in campaign path: {campaignPath}");
        //    foreach (string suffix in new[] { "_first.miz", "_ongoing.miz" })
        //    {
        //        string src = Path.Combine(campaignPath, oldName + suffix);
        //        string dst = Path.Combine(campaignPath, newName + suffix);

        //        FormUtils.LogRegister($"Attempting to rename file: {src} to {dst}");

        //        if (File.Exists(src))
        //        {
        //            File.Move(src, dst);
        //            FormUtils.LogRegister($"Successfully renamed file: {src} to {dst}");
        //        }
        //        else
        //        {
        //            FormUtils.LogRegister($"File not found: {src}");
        //        }
        //    }
        //}

        public static void UpdateCampInit(string campaignPath, string oldName, string newName)
        {
            string file = Path.Combine(campaignPath, "Init", "camp_init.lua");
            if (!File.Exists(file)) return;

            // IMPORTANT : on repère la ligne "title" par sa forme (comme le faisait l'ancien
            // Modifier_camp_init, aujourd'hui désactivé), pas par un Replace exact de oldName.
            // Pourquoi : text.Replace($"title = \"{oldName}\"", ...) ne fait RIEN si le texte
            // réel du titre dans le fichier ne correspond pas caractère pour caractère à oldName
            // (espace en trop, casse différente, titre déjà retouché...) : le titre reste alors
            // sur l'ancien nom après clonage, sans aucune erreur.
            string[] lines = File.ReadAllLines(file, Encoding.UTF8);
            bool titreTrouve = false;

            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];

                if (!line.Contains(",")) continue;

                string[] words = line.Split(',');

                if (words[0].Contains("title"))
                {
                    lines[i] = "\ttitle = \"" + newName + "\",\t\t--Title of campaign (name of missions)";
                    titreTrouve = true;
                }
            }

            if (!titreTrouve)
            {
                FormUtils.LogRegister($"UpdateCampInit: ligne 'title' introuvable dans '{file}'.");
            }

            File.WriteAllLines(file, lines, new UTF8Encoding(false));
        }

        public static void UpdateCmpFile(string fileCmdPath, string oldName, string newName)
        {
            FormUtils.LogRegister($"UpdateCmpFile: '{fileCmdPath}', remplacement de '{oldName}' par '{newName}'.");

            if (!File.Exists(fileCmdPath))
            {
                FormUtils.LogRegister("UpdateCmpFile: fichier introuvable.");
                return;
            }

            string text = File.ReadAllText(fileCmdPath, Encoding.UTF8);
            text = text.Replace(oldName, newName);
            File.WriteAllText(fileCmdPath, text, new UTF8Encoding(false));

            FormUtils.LogRegister("UpdateCmpFile: terminé.");
        }

        //public static void UpdateCmpFile(string fileCmdPath, string oldName, string newName)
        //{
        //    FormUtils.LogRegister($"UpdateCampInit=> A UpdateCmpFile in campaign path: {campaignPath} from '{oldName}' to '{newName}' ");

        //    string cmdFile = Directory.GetFiles(campaignPath, "*.cmd", SearchOption.TopDirectoryOnly).FirstOrDefault();
        //    if (cmdFile == null)
        //    {
        //        FormUtils.LogRegister($"UpdateCampInit=> B cmdFile == null RETURN ");
        //        return;
        //    }

        //    FormUtils.LogRegister($"UpdateCampInit=> C ");

        //    string text = File.ReadAllText(cmdFile, Encoding.Default);
        //    text = text.Replace(oldName, newName);
        //    //File.WriteAllText(cmdFile, text, Encoding.Default);
        //    File.WriteAllText(cmdFile, text, new UTF8Encoding(false));
        //}

    }
}
