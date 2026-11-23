using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace DCE_Manager   // <-- meme namespace que tes autres fichiers
{
    /// <summary>
    /// Gere la modification de DCS\Scripts\MissionScripting.lua
    /// (commenter / decommenter sanitizeModule('os'), ('io') et ('lfs'))
    /// Les lignes _G['require'], _G['loadlib'], _G['package'] = nil ne sont JAMAIS touchees.
    ///
    /// DCE_Manager tourne en utilisateur NORMAL. Si l'ecriture est refusee (DCS dans
    /// Program Files), on relance DCE_Manager.exe une seule fois en administrateur
    /// (fenetre UAC) avec --dcs-patch / --dcs-unpatch / --dcs-restore : il fait le
    /// travail et se ferme tout de suite. Voir RunCli().
    ///
    /// Regles de securite :
    ///  - on ne garde JAMAIS le fichier ouvert (lecture/ecriture rapide, tout est ferme aussitot)
    ///  - on ne met JAMAIS le fichier en lecture seule (sinon l'update DCS ne peut plus le remplacer)
    ///  - on ecrit d'abord dans un fichier temporaire, puis on remplace : pas de fichier a moitie ecrit
    ///  - on garde l'encodage d'origine (avec ou sans BOM) et les fins de ligne
    ///  - la sauvegarde .bak est refaite quand le fichier est "propre" (apres update DCS)
    /// </summary>
    public static class DcsPatcher
    {
        // Les 3 modules desactives. Ajouter/retirer un nom ici suffit.
        private const string Modules = "os|io|lfs";
        private const int NbModules = 3;

        private const string FlagPatch = "--dcs-patch";
        private const string FlagUnpatch = "--dcs-unpatch";
        private const string FlagRestore = "--dcs-restore";

        // true = on est le processus admin lance par RelaunchElevated : on ne re-eleve pas
        private static bool _inElevatedChild = false;

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

        // true = les 3 lignes (os, io et lfs) sont commentees
        // Un ancien fichier patche avec seulement os et io est vu comme "pas patche" :
        // un clic sur la case ajoute alors lfs.
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
                    @"^[ \t]*sanitizeModule\('(" + Modules + @")'\)",
                    RegexOptions.Multiline);

                // lignes commentees ?
                int commentees = Regex.Matches(contenu,
                    @"^[ \t]*--[ \t]*sanitizeModule\('(" + Modules + @")'\)",
                    RegexOptions.Multiline).Count;

                return !actives && commentees >= NbModules;
            }
            catch
            {
                return false;
            }
        }

        // ---------- actions ----------

        // Commente sanitizeModule('os'), ('io') et ('lfs')
        public static bool Patch(string dcsPath, out string message)
        {
            string fichier = GetFichier(dcsPath);
            try
            {
                if (!File.Exists(fichier))
                {
                    message = "MissionScripting.lua not found.\n" +
                                "Check the \"DCS Root Folder\" of the current configuration: " +
                                "it must be the DCS installation folder (the one containing bin and Scripts).";
                    return false;
                }

                bool bom;
                string contenu = Lire(fichier, out bom);

                string nouveau = Regex.Replace(contenu,
                    @"^([ \t]*)(sanitizeModule\('(" + Modules + @")'\))",
                    "--$1$2",
                    RegexOptions.Multiline);

                if (nouveau == contenu)
                {
                    message = "Already modified, nothing to do.";
                    return true;
                }

                // Sauvegarde : seulement si le fichier est "propre" (aucune ligne sanitizeModule
                // deja commentee), ou si aucune sauvegarde n'existe. Sinon un vieux fichier
                // patche a moitie (os/io seulement) ecraserait la bonne sauvegarde.
                string bak = fichier + ".dcemanager.bak";
                bool dejaCommente = Regex.IsMatch(contenu,
                    @"^[ \t]*--[ \t]*sanitizeModule\('(" + Modules + @")'\)",
                    RegexOptions.Multiline);

                if (!dejaCommente || !File.Exists(bak))
                    File.Copy(fichier, bak, true);

                Ecrire(fichier, nouveau, bom);
                message = "MissionScripting.lua modified.";
                return true;
            }
            catch (UnauthorizedAccessException)
            {
                return RelaunchElevated(FlagPatch, dcsPath, out message);
            }
            catch (IOException ex)
            {
                message = "File in use by another program (DCS or its updater?). Close DCS and try again.\n" + ex.Message;
                return false;
            }
            catch (Exception ex)
            {
                message = "Error: " + ex.Message;
                return false;
            }
        }

        // Decommente les 3 lignes (retour a l'etat d'origine, sans utiliser le .bak)
        public static bool Unpatch(string dcsPath, out string message)
        {
            string fichier = GetFichier(dcsPath);
            try
            {
                if (!File.Exists(fichier))
                {
                    message = "MissionScripting.lua not found.\n" +
                                "Check the \"DCS Root Folder\" of the current configuration: " +
                                "it must be the DCS installation folder (the one containing bin and Scripts).";
                    return false;
                }

                bool bom;
                string contenu = Lire(fichier, out bom);

                string nouveau = Regex.Replace(contenu,
                    @"^([ \t]*)--([ \t]*)(sanitizeModule\('(" + Modules + @")'\))",
                    "$1$2$3",
                    RegexOptions.Multiline);

                if (nouveau == contenu)
                {
                    message = "Already original, nothing to do.";
                    return true;
                }

                Ecrire(fichier, nouveau, bom);
                message = "MissionScripting.lua restored to original.";
                return true;
            }
            catch (UnauthorizedAccessException)
            {
                return RelaunchElevated(FlagUnpatch, dcsPath, out message);
            }
            catch (IOException ex)
            {
                message = "File in use by another program (DCS or its updater?). Close DCS and try again.\n" + ex.Message;
                return false;
            }
            catch (Exception ex)
            {
                message = "Error: " + ex.Message;
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
                    message = "No backup found.";
                    return false;
                }
                File.Copy(backup, fichier, true);
                message = "Original file restored.";
                return true;
            }
            catch (UnauthorizedAccessException)
            {
                return RelaunchElevated(FlagRestore, dcsPath, out message);
            }
            catch (Exception ex)
            {
                message = "Error: " + ex.Message;
                return false;
            }
        }

        // ---------- elevation a la demande ----------

        // Relance DCE_Manager.exe en administrateur pour UNE action, attend, et lit le code retour.
        // L'appli principale reste en utilisateur normal.
        private static bool RelaunchElevated(string flag, string dcsPath, out string message)
        {
            if (_inElevatedChild)
            {
                message = "Access denied, even as administrator. Check the folder permissions.";
                return false;
            }

            // Explication AVANT la fenetre UAC de Windows (qui, seule, n'explique rien)
            DialogResult reponse = MessageBox.Show(
                "DCS is installed in a protected folder.\r\n\r\n" +
                "To change MissionScripting.lua, Windows will now ask for administrator permission.\r\n" +
                "DCE_Manager will only edit this one file, then close the admin window.\r\n\r\n" +
                "Click OK to continue, or Cancel to change nothing.",
                "DCE_Manager - Administrator permission",
                MessageBoxButtons.OKCancel,
                MessageBoxIcon.Information);

            if (reponse != DialogResult.OK)
            {
                message = "Cancelled. MissionScripting.lua was not changed.";
                return false;
            }

            try
            {
                ProcessStartInfo psi = new ProcessStartInfo();
                psi.FileName = Process.GetCurrentProcess().MainModule.FileName;
                psi.Arguments = flag + " \"" + dcsPath.TrimEnd('\\', '/') + "\"";
                psi.UseShellExecute = true;   // obligatoire pour "runas"
                psi.Verb = "runas";           // demande UAC
                psi.WindowStyle = ProcessWindowStyle.Hidden;

                using (Process p = Process.Start(psi))
                {
                    p.WaitForExit();

                    if (p.ExitCode == 0)
                    {
                        message = "MissionScripting.lua updated (administrator rights used).";
                        return true;
                    }

                    message = "The administrator action failed (code " + p.ExitCode + "). See log.txt.";
                    return false;
                }
            }
            catch (Win32Exception ex)
            {
                // 1223 = l'utilisateur a refuse la fenetre UAC
                message = ex.NativeErrorCode == 1223
                    ? "Administrator permission was refused. MissionScripting.lua was not changed."
                    : "Cannot ask for administrator rights: " + ex.Message;
                return false;
            }
            catch (Exception ex)
            {
                message = "Error: " + ex.Message;
                return false;
            }
        }

        // A appeler au tout debut de Main() (avant Application.Run).
        // Retourne true si les arguments etaient --dcs-xxx : le travail est fait, Main doit
        // juste sortir avec exitCode. Retourne false = demarrage normal.
        public static bool RunCli(string[] args, out int exitCode)
        {
            exitCode = 0;
            if (args == null || args.Length < 2) return false;

            string flag = args[0].ToLowerInvariant();
            if (flag != FlagPatch && flag != FlagUnpatch && flag != FlagRestore)
                return false;

            _inElevatedChild = true;

            string msg;
            bool ok = flag == FlagPatch ? Patch(args[1], out msg)
                    : flag == FlagUnpatch ? Unpatch(args[1], out msg)
                    : Restore(args[1], out msg);

            try { DCE_Manager.Utils.FormUtils.LogRegister("DcsPatcher (admin) | " + flag + " | " + msg); } catch { }

            exitCode = ok ? 0 : 1;
            return true;
        }
    }
}
