using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace DCE_Manager   // <-- meme namespace que tes autres fichiers
{
    /// <summary>
    /// Gere la modification de DCS\Scripts\MissionScripting.lua
    /// (commenter / decommenter sanitizeModule('os') et sanitizeModule('io'))
    ///
    /// Regles de securite :
    ///  - on ne garde JAMAIS le fichier ouvert (lecture/ecriture rapide, tout est ferme aussitot)
    ///  - on ne met JAMAIS le fichier en lecture seule (sinon l'update DCS ne peut plus le remplacer)
    ///  - on ecrit d'abord dans un fichier temporaire, puis on remplace : pas de fichier a moitie ecrit
    ///  - on garde l'encodage d'origine (avec ou sans BOM) et les fins de ligne
    ///  - la sauvegarde .bak est refaite a chaque fois que le fichier est "propre" (apres update DCS)
    /// </summary>
    public static class DcsPatcher
    {
        private static string GetFichier(string dcsPath)
        {
            return Path.Combine(dcsPath, "Scripts", "MissionScripting.lua");
        }

        // ---------- lecture / ecriture "propres" ----------

        // Lecture sans bloquer le fichier (DCS ou l'updater peuvent l'avoir ouvert en meme temps)
        private static string Lire(string fichier, out bool avecBom)
        {
            byte[] octets;
            using (FileStream fs = new FileStream(fichier, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                octets = new byte[fs.Length];
                int lu = 0;
                while (lu < octets.Length)
                {
                    int n = fs.Read(octets, lu, octets.Length - lu);
                    if (n <= 0) break;
                    lu += n;
                }
            }
            avecBom = octets.Length >= 3 && octets[0] == 0xEF && octets[1] == 0xBB && octets[2] == 0xBF;
            return new UTF8Encoding(false).GetString(octets, avecBom ? 3 : 0, octets.Length - (avecBom ? 3 : 0));
        }

        // Ecriture : fichier temporaire d'abord, puis remplacement, puis nettoyage
        private static void Ecrire(string fichier, string contenu, bool avecBom)
        {
            // si le fichier est en lecture seule, on enleve juste l'attribut (et on ne le remet pas)
            FileAttributes attr = File.GetAttributes(fichier);
            if ((attr & FileAttributes.ReadOnly) != 0)
                File.SetAttributes(fichier, attr & ~FileAttributes.ReadOnly);

            string tmp = fichier + ".dcemanager.tmp";
            try
            {
                File.WriteAllText(tmp, contenu, new UTF8Encoding(avecBom));
                File.Copy(tmp, fichier, true);
            }
            finally
            {
                try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
            }
        }

        // ---------- infos ----------

        // true si DCS.exe tourne (le changement ne sera pris en compte qu'au prochain lancement de mission)
        public static bool IsDcsRunning()
        {
            try { return Process.GetProcessesByName("DCS").Length > 0; }
            catch { return false; }
        }

        // true = les 2 lignes (os et io) sont commentees
        public static bool IsPatched(string dcsPath)
        {
            try
            {
                string fichier = GetFichier(dcsPath);
                if (!File.Exists(fichier)) return false;

                bool bom;
                string contenu = Lire(fichier, out bom);

                // lignes encore actives (non commentees) ?
                bool actives = Regex.IsMatch(contenu,
                    @"^[ \t]*sanitizeModule\('(os|io)'\)",
                    RegexOptions.Multiline);

                // lignes commentees ?
                int commentees = Regex.Matches(contenu,
                    @"^--[ \t]*sanitizeModule\('(os|io)'\)",
                    RegexOptions.Multiline).Count;

                return !actives && commentees >= 2;
            }
            catch
            {
                return false;
            }
        }

        // ---------- actions ----------

        // Commente sanitizeModule('os') et sanitizeModule('io')
        public static bool Patch(string dcsPath, out string message)
        {
            string fichier = GetFichier(dcsPath);
            try
            {
                if (!File.Exists(fichier))
                {
                    message = "Fichier introuvable : " + fichier;
                    return false;
                }

                bool bom;
                string contenu = Lire(fichier, out bom);

                string nouveau = Regex.Replace(contenu,
                    @"^([ \t]*)(sanitizeModule\('(os|io)'\))",
                    "--$1$2",
                    RegexOptions.Multiline);

                if (nouveau == contenu)
                {
                    message = "Deja modifie, rien a faire.";
                    return true;
                }

                // Le fichier contient des lignes actives = version "propre" (d'origine ou apres update DCS)
                // -> on rafraichit la sauvegarde, comme ca elle n'est jamais une vieille version
                File.Copy(fichier, fichier + ".dcemanager.bak", true);

                Ecrire(fichier, nouveau, bom);
                message = "MissionScripting.lua modifie.";
                return true;
            }
            catch (UnauthorizedAccessException)
            {
                message = "Acces refuse : DCE_Manager doit etre lance en administrateur.";
                return false;
            }
            catch (IOException ex)
            {
                message = "Fichier utilise par un autre programme (DCS ou son updater ?). Ferme DCS et recommence.\n" + ex.Message;
                return false;
            }
            catch (Exception ex)
            {
                message = "Erreur : " + ex.Message;
                return false;
            }
        }

        // Decommente les 2 lignes (retour a l'etat d'origine, sans utiliser le .bak)
        public static bool Unpatch(string dcsPath, out string message)
        {
            string fichier = GetFichier(dcsPath);
            try
            {
                if (!File.Exists(fichier))
                {
                    message = "Fichier introuvable : " + fichier;
                    return false;
                }

                bool bom;
                string contenu = Lire(fichier, out bom);

                string nouveau = Regex.Replace(contenu,
                    @"^--([ \t]*sanitizeModule\('(os|io)'\))",
                    "$1",
                    RegexOptions.Multiline);

                if (nouveau == contenu)
                {
                    message = "Deja a l'origine, rien a faire.";
                    return true;
                }

                Ecrire(fichier, nouveau, bom);
                message = "MissionScripting.lua remis a l'origine.";
                return true;
            }
            catch (UnauthorizedAccessException)
            {
                message = "Acces refuse : DCE_Manager doit etre lance en administrateur.";
                return false;
            }
            catch (IOException ex)
            {
                message = "Fichier utilise par un autre programme (DCS ou son updater ?). Ferme DCS et recommence.\n" + ex.Message;
                return false;
            }
            catch (Exception ex)
            {
                message = "Erreur : " + ex.Message;
                return false;
            }
        }

        // Remet le fichier depuis le .bak (derniere version "propre" vue avant un patch)
        public static bool Restore(string dcsPath, out string message)
        {
            string fichier = GetFichier(dcsPath);
            string backup = fichier + ".dcemanager.bak";
            try
            {
                if (!File.Exists(backup))
                {
                    message = "Pas de sauvegarde trouvee.";
                    return false;
                }
                File.Copy(backup, fichier, true);
                message = "Fichier d'origine restaure.";
                return true;
            }
            catch (UnauthorizedAccessException)
            {
                message = "Acces refuse : DCE_Manager doit etre lance en administrateur.";
                return false;
            }
            catch (Exception ex)
            {
                message = "Erreur : " + ex.Message;
                return false;
            }
        }
    }
}
