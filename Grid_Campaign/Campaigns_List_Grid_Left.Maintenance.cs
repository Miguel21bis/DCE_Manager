using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Xml.Linq;
using DCE_Manager.Parameters;
using DCE_Manager.Update;
using DCE_Manager.Utils;

namespace DCE_Manager
{
    // Suppression, renommage et réparation d'une campagne. Partie de CampaignGridLeft, voir Campaigns_List_Grid_Left.cs
    public partial class CampaignGridLeft
    {
        // Compte-rendu d'une suppression : ce qui a été fait, ce qui a bloqué.
        private class DeleteOutcome
        {
            public int DeletedCount;
            public List<string> LockedFiles = new List<string>();
            public bool FolderRemoved = true; // true aussi si aucun dossier n'existait (fichier orphelin seul)
            public string FolderRemovalError;
        }

        // Supprime le dossier d'une campagne et ses fichiers associés (mission .miz, .cmp,
        // image .png) à partir de son nom et de son folderPath. Marche aussi pour un fichier
        // orphelin sans dossier (folderPath n'existe simplement pas, ignoré).
        // Async pour ne pas geler l'UI pendant les tentatives (voir TryDeleteFileWithRetryAsync).
        private async Task<DeleteOutcome> DeleteCampaignFilesAndFolderAsync(string name, string folderPath)
        {
            var outcome = new DeleteOutcome();

            List<string> filesToDelete = Directory.Exists(folderPath)
                ? Directory.GetFiles(folderPath, "*", SearchOption.AllDirectories).ToList()
                : new List<string>();

            string parentFolder = Path.GetDirectoryName(folderPath) ?? "";
            string baseName = name;

            HashSet<string> expected = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                baseName,
                baseName + "_first",
                baseName + "_ongoing"
            };

            var extraFiles = Directory.GetFiles(parentFolder)
                .Where(f => expected.Contains(Path.GetFileNameWithoutExtension(f)))
                .ToList();

            filesToDelete.AddRange(extraFiles);

            // IMPORTANT : un verrou de fichier est souvent transitoire (DCS qui vient de se
            // fermer, antivirus qui scanne au mauvais moment...) et se lève en une fraction de
            // seconde. On retente plusieurs fois par fichier avant d'abandonner, on supprime
            // tout ce qui est possible, et on renvoie PRÉCISÉMENT ce qui reste bloqué — au lieu
            // du message générique de Directory.Delete qui ne cite que le dossier racine.
            foreach (string file in filesToDelete.Where(File.Exists))
            {
                if (await TryDeleteFileWithRetryAsync(file))
                {
                    outcome.DeletedCount++;
                }
                else
                {
                    outcome.LockedFiles.Add(file);
                }
            }

            // Retire le dossier (normalement vide à ce stade, ou avec juste des sous-dossiers
            // vides) s'il en reste un. IMPORTANT : avant, un échec ici était juste loggé dans un
            // fichier, sans que l'utilisateur ne voie jamais rien — d'où l'ajout de
            // FolderRemoved/FolderRemovalError, repris dans le compte-rendu affiché.
            if (Directory.Exists(folderPath))
            {
                try
                {
                    Directory.Delete(folderPath, true);
                }
                catch (Exception exDir)
                {
                    outcome.FolderRemoved = false;
                    outcome.FolderRemovalError = exDir.Message;
                    FormUtils.LogRegister($"Suppression de '{folderPath}' : le dossier n'a pas pu être retiré ({exDir.Message}).");
                }
            }

            CampaignHierarchy.OnCampaignDeleted(name);

            return outcome;
        }

        // Retente la suppression d'un fichier avant d'abandonner.
        // Pourquoi : un verrou (antivirus, DCS qui vient de libérer le fichier...) est souvent
        // transitoire et se lève en une fraction de seconde ; ça évite de bloquer toute une
        // suppression pour un verrou qui n'existe déjà plus une demi-seconde après.
        // Task.Delay (pas Thread.Sleep) : laisse l'UI répondre (curseur sablier, pas de "ne
        // répond pas") pendant l'attente au lieu de geler le thread principal.
        private async Task<bool> TryDeleteFileWithRetryAsync(string file, int attempts = 3, int delayMs = 400)
        {
            for (int i = 0; i < attempts; i++)
            {
                try
                {
                    if (File.Exists(file))
                    {
                        File.SetAttributes(file, FileAttributes.Normal); // au cas où le fichier serait en lecture seule
                        File.Delete(file);
                    }
                    return true;
                }
                catch (IOException)
                {
                    if (i < attempts - 1)
                        await Task.Delay(delayMs);
                }
                catch (UnauthorizedAccessException)
                {
                    if (i < attempts - 1)
                        await Task.Delay(delayMs);
                }
            }

            return false;
        }

        // Supprime toutes les lignes cochées, avec confirmation, sablier et compte-rendu.
        // Appelée par la corbeille de la dernière ligne et par la colonne Delete.
        private async Task DeleteSelectedRowsAsync()
        {
            var checkedRows = GetCheckedRows();

            if (checkedRows.Count == 0)
            {
                MessageBox.Show(
                    "No item selected. Tick the boxes in the last column first.",
                    "Delete selected",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            // IMPORTANT : les cases sont désormais disponibles sur TOUTES les lignes, donc une
            // suppression groupée peut emporter de vraies campagnes. On liste les noms dans la
            // confirmation plutôt qu'un simple compte, pour éviter la mauvaise surprise.
            string namesPreview = string.Join("\r\n", checkedRows.Take(15).Select(r => "  - " + r.Name));
            if (checkedRows.Count > 15)
                namesPreview += "\r\n  ... and " + (checkedRows.Count - 15) + " more";

            var confirmBulk = MessageBox.Show(
                "Delete " + checkedRows.Count + " selected item(s) ?\r\n\r\n" + namesPreview,
                "Confirm",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning
            );

            if (confirmBulk != DialogResult.Yes)
                return;

            int minRowIndex = checkedRows.Min(r => r.RowIndex);
            var results = new List<(string Name, DeleteOutcome Outcome)>();

            // Curseur sablier : les tentatives sur fichier verrouillé peuvent prendre quelques
            // secondes (surtout à plusieurs éléments) ; sans ça, l'appli paraît figée.
            _mainForm.Cursor = Cursors.WaitCursor;
            try
            {
                foreach (var item in checkedRows)
                {
                    DeleteOutcome outcome = await DeleteCampaignFilesAndFolderAsync(item.Name, item.FolderPath);
                    results.Add((item.Name, outcome));
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error: " + ex.Message);
            }
            finally
            {
                _mainForm.Cursor = Cursors.Default;
            }

            if (results.Count > 0)
            {
                ShowDeleteReport(results);
            }

            await LoadCampaignsAsync(restoreRowIndex: minRowIndex);
        }

        // Compte-rendu (en anglais) affiché après une suppression simple ou groupée : ce qui a
        // été supprimé avec succès, et le détail de ce qui a bloqué (fichiers verrouillés,
        // dossier non retiré). Toujours affiché, même en cas de succès complet.
        private void ShowDeleteReport(List<(string Name, DeleteOutcome Outcome)> results)
        {
            var problems = results.Where(r => r.Outcome.LockedFiles.Count > 0 || !r.Outcome.FolderRemoved).ToList();
            int fullySuccessful = results.Count - problems.Count;

            var sb = new StringBuilder();

            if (results.Count == 1)
            {
                var r = results[0];

                if (problems.Count == 0)
                {
                    sb.AppendLine("'" + r.Name + "' deleted successfully (" + r.Outcome.DeletedCount + " file(s) removed).");
                }
                else
                {
                    sb.AppendLine("'" + r.Name + "' : " + r.Outcome.DeletedCount + " file(s) deleted.");

                    if (r.Outcome.LockedFiles.Count > 0)
                    {
                        sb.AppendLine();
                        sb.AppendLine(r.Outcome.LockedFiles.Count + " file(s) still in use (not deleted):");
                        foreach (string f in r.Outcome.LockedFiles)
                            sb.AppendLine("  - " + Path.GetFileName(f));
                    }

                    if (!r.Outcome.FolderRemoved)
                    {
                        sb.AppendLine();
                        sb.AppendLine("The campaign folder itself could not be removed: " + r.Outcome.FolderRemovalError);
                    }

                    sb.AppendLine();
                    sb.AppendLine("Close DCS (and the mission editor), then try again.");
                }
            }
            else
            {
                sb.AppendLine(fullySuccessful + " of " + results.Count + " item(s) deleted successfully.");

                if (problems.Count > 0)
                {
                    sb.AppendLine();
                    sb.AppendLine(problems.Count + " item(s) could not be fully removed:");

                    foreach (var r in problems)
                    {
                        sb.Append("  - " + r.Name);

                        if (r.Outcome.LockedFiles.Count > 0)
                        {
                            sb.Append(" : " + r.Outcome.LockedFiles.Count + " file(s) still in use (" +
                                string.Join(", ", r.Outcome.LockedFiles.Select(Path.GetFileName)) + ")");
                        }

                        if (!r.Outcome.FolderRemoved)
                        {
                            sb.Append(" — folder not removed (" + r.Outcome.FolderRemovalError + ")");
                        }

                        sb.AppendLine();
                    }

                    sb.AppendLine();
                    sb.AppendLine("Close DCS (and the mission editor), then try again.");
                }
            }

            MessageBox.Show(
                sb.ToString(),
                "Delete report",
                MessageBoxButtons.OK,
                problems.Count > 0 ? MessageBoxIcon.Warning : MessageBoxIcon.Information);
        }

        // Petite fenêtre de saisie construite en code (pas de Form dédiée pour si peu).
        // Renvoie le nom saisi, ou null si annulé.
        private string AskNewCampaignName(string oldName)
        {
            using (var form = new Form())
            {
                form.Text = "Rename campaign";
                form.FormBorderStyle = FormBorderStyle.FixedDialog;
                form.StartPosition = FormStartPosition.CenterParent;
                form.MinimizeBox = false;
                form.MaximizeBox = false;
                form.ShowInTaskbar = false;
                form.ClientSize = new Size(440, 120);

                var label = new Label { Text = "New name for '" + oldName + "' :", Left = 12, Top = 12, Width = 416, AutoEllipsis = true };
                var textBox = new TextBox { Text = oldName, Left = 12, Top = 38, Width = 416 };
                var buttonOk = new Button { Text = "Rename", Left = 252, Top = 76, Width = 84, DialogResult = DialogResult.OK };
                var buttonCancel = new Button { Text = "Cancel", Left = 344, Top = 76, Width = 84, DialogResult = DialogResult.Cancel };

                form.Controls.AddRange(new Control[] { label, textBox, buttonOk, buttonCancel });
                form.AcceptButton = buttonOk;
                form.CancelButton = buttonCancel;

                return form.ShowDialog(_mainForm) == DialogResult.OK ? textBox.Text.Trim() : null;
            }
        }

        // Remplace le "title" dans le texte d'un camp_status.lua (format sérialisé ['title'] = '...').
        // Seule la 1ère occurrence est touchée.
        private static string PatchTitleInCampStatusText(string text, string newName)
        {
            var regex = new Regex(@"((?:\[\s*[""']title[""']\s*\]|(?<!\w)title)\s*=\s*)([""'])(.*?)\2");
            return regex.Replace(text, m => m.Groups[1].Value + m.Groups[2].Value + newName + m.Groups[2].Value, 1);
        }

        // Active\camp_status.lua sur disque. Sans effet si le fichier n'existe pas encore.
        private static void RenameTitleInCampStatus(string file, string newName)
        {
            if (!File.Exists(file))
                return;

            string text = File.ReadAllText(file, Encoding.UTF8);
            string patched = PatchTitleInCampStatusText(text, newName);

            if (patched != text)
                File.WriteAllText(file, patched, new UTF8Encoding(false));
        }

        // camp_status.lua embarqué dans un .miz (l10n/DEFAULT/camp_status.lua).
        // Un .miz est un zip : on réécrit juste cette entrée.
        private static void RenameTitleInMiz(string mizPath, string newName)
        {
            if (!File.Exists(mizPath))
                return;

            try
            {
                using (var fs = new FileStream(mizPath, FileMode.Open, FileAccess.ReadWrite))
                using (var archive = new System.IO.Compression.ZipArchive(fs, System.IO.Compression.ZipArchiveMode.Update))
                {
                    var entry = archive.GetEntry("l10n/DEFAULT/camp_status.lua");

                    if (entry == null)
                    {
                        FormUtils.LogRegister("Rename : pas de camp_status.lua dans '" + mizPath + "'");
                        return;
                    }

                    string text;
                    using (var reader = new StreamReader(entry.Open(), Encoding.UTF8))
                    {
                        text = reader.ReadToEnd();
                    }

                    string patched = PatchTitleInCampStatusText(text, newName);

                    if (patched == text)
                        return;

                    entry.Delete();

                    var newEntry = archive.CreateEntry("l10n/DEFAULT/camp_status.lua");
                    using (var writer = new StreamWriter(newEntry.Open(), new UTF8Encoding(false)))
                    {
                        writer.Write(patched);
                    }
                }
            }
            catch (Exception ex)
            {
                FormUtils.LogRegister("Rename : échec sur '" + mizPath + "' : " + ex.Message);
            }
        }

        // Renomme la campagne : dossier, .miz, .cmp, .png + contenus qui portent le nom.
        private async Task RenameCampaignAsync(string oldName)
        {
            string campaignsRoot = ParamConf.PATH_SavedGames_DCS + @"\Mods\tech\DCE\Missions\Campaigns";

            string newName = AskNewCampaignName(oldName);

            if (string.IsNullOrEmpty(newName) || newName == oldName)
                return;

            // ' interdit en plus des caractères de fichier invalides : il casserait les chaînes Lua '...'
            if (newName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || newName.Contains("'"))
            {
                MessageBox.Show("This name contains forbidden characters (\\ / : * ? \" < > | ').", "Rename", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            // Windows ne fait pas la différence de casse : Directory.Move refuserait
            if (string.Equals(newName, oldName, StringComparison.OrdinalIgnoreCase))
            {
                MessageBox.Show("The new name must differ by more than upper/lower case.", "Rename", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            string[] suffixes = { "_first.miz", "_ongoing.miz", ".cmp", ".png", ".bmp" };

            string oldFolder = Path.Combine(campaignsRoot, oldName);
            string newFolder = Path.Combine(campaignsRoot, newName);

            if (Directory.Exists(newFolder) || suffixes.Any(s => File.Exists(Path.Combine(campaignsRoot, newName + s))))
            {
                MessageBox.Show("A campaign (or leftover file) named '" + newName + "' already exists.", "Rename", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            // On note chaque déplacement réussi pour pouvoir tout annuler si l'un échoue.
            var done = new List<(string From, string To)>();

            try
            {
                Directory.Move(oldFolder, newFolder);
                done.Add((oldFolder, newFolder));

                foreach (string suffix in suffixes)
                {
                    string src = Path.Combine(campaignsRoot, oldName + suffix);

                    if (!File.Exists(src))
                        continue;

                    string dst = Path.Combine(campaignsRoot, newName + suffix);
                    File.Move(src, dst);
                    done.Add((src, dst));
                }
            }
            catch (Exception ex)
            {
                for (int i = done.Count - 1; i >= 0; i--)
                {
                    try
                    {
                        if (Directory.Exists(done[i].To))
                            Directory.Move(done[i].To, done[i].From);
                        else
                            File.Move(done[i].To, done[i].From);
                    }
                    catch (Exception exUndo)
                    {
                        FormUtils.LogRegister("Rename : annulation impossible de '" + done[i].To + "' : " + exUndo.Message);
                    }
                }

                MessageBox.Show(
                    "Rename failed, nothing was changed.\r\n\r\n" + ex.Message + "\r\n\r\nClose DCS and any Explorer window on this campaign, then try again.",
                    "Rename", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            // Contenus qui portent le nom
            DCE_Manager.Clone.CloneHelper.UpdateCmpFile(Path.Combine(campaignsRoot, newName + ".cmp"), oldName, newName);
            DCE_Manager.Clone.CloneHelper.UpdateCampInit(newFolder, oldName, newName);
            RenameTitleInCampStatus(Path.Combine(newFolder, "Active", "camp_status.lua"), newName);

            RenameTitleInMiz(Path.Combine(campaignsRoot, newName + "_first.miz"), newName);
            RenameTitleInMiz(Path.Combine(campaignsRoot, newName + "_ongoing.miz"), newName);

            CampaignHierarchy.OnCampaignRenamed(oldName, newName);

            FormUtils.LogRegister("Rename : '" + oldName + "' -> '" + newName + "'");

            await LoadCampaignsAsync(selectCampaignName: newName);
        }

        // contextLabel : permet de réutiliser cette méthode ailleurs (ex: après un Import)
        // sans que le message final ne parle à tort de "Repair".
        internal async Task RepairCampaignAsync(string name, string contextLabel = "Repair")
        {
            Utils.FormUtils.LogRegister(contextLabel + " demandé pour la campagne '" + name + "'");

            string campaignsRoot = ParamConf.PATH_SavedGames_DCS + @"\Mods\tech\DCE\Missions\Campaigns";
            string folderPath = Path.Combine(campaignsRoot, name);
            string initFolderPath = Path.Combine(folderPath, "Init");

            var report = new List<string>();

            // camp_init.lua / conf_mod.lua : créés depuis la référence s'ils sont absents,
            // sinon simplement recalés (voir CampInitUpdater / ConfModTemplateUpdater).
            bool campInitExistedBefore = File.Exists(new CampInitUpdater().GetCampInitPath(name));
            bool confModExistedBefore = File.Exists(new ConfModLoader().GetConfModPath(name));

            ConfUpdateResult campInitResult = new CampInitUpdater().UpdateCampaign(name);
            ConfUpdateResult confModResult = new ConfModTemplateUpdater().UpdateCampaign(name); // dans cet ordre

            report.Add(DescribeResult("camp_init.lua", campInitExistedBefore, campInitResult));
            report.Add(DescribeResult("conf_mod.lua", confModExistedBefore, confModResult));

            // path.bat : toujours régénéré si les chemins DCS sont configurés, absent ou pas.
            string pathBatFile = Path.Combine(initFolderPath, "path.bat");
            bool pathBatExistedBefore = File.Exists(pathBatFile);

            if (ParamConf.PATH_DCS_Root != "" && ParamConf.PATH_SavedGames_DCS != "")
            {
                string textPathBat = "REM Core or Main DCS ou DCS.beta path, always end the line with \\ \r\n" +
                               "set \"pathDCS=" + ParamConf.PATH_DCS_Root + "\\\"\r\n" +
                               "REM Core or Main DCS ou DCS.beta path, always end the line with \\ \r\n" +
                               "set \"pathSavedGames=" + ParamConf.PATH_SavedGames_DCS + "\\\"\r\n" +
                               "REM DCE ScriptMod version not any / or \\ and no space before and after = \r\n" +
                               "set \"versionPackageICM=" + TestFile.ScriptsMod + "\"\r\n" +
                               "\r\n" +
                               "\r\n" +
                               "REM After each change, You must launch the FirsMission.bat for it to be taken into account.";

                Directory.CreateDirectory(initFolderPath); // au cas où même le dossier Init aurait disparu
                File.WriteAllText(pathBatFile, textPathBat);
                report.Add("path.bat: " + (pathBatExistedBefore ? "regenerated" : "created"));
            }
            else
            {
                report.Add("path.bat: not regenerated (DCS / Saved Games paths not configured in Options)");
            }

            // .cmp : la convention de nommage est fixe (nameCamp_first.miz / nameCamp_ongoing.miz),
            // donc on peut l'écrire même si les .miz manquent encore - requiredMissionFiles continue
            // de les signaler séparément.
            bool cmpExistedBefore = File.Exists(Path.Combine(campaignsRoot, name + ".cmp"));
            CampaignRepair.TryRepairCmpFile(campaignsRoot, name);
            report.Add(".cmp: " + (cmpExistedBefore ? "already present" : "created (references " + name + "_first.miz / " + name + "_ongoing.miz)"));

            // .png : jamais bloquant, juste un visuel de secours.
            bool pngExistedBefore = File.Exists(Path.Combine(campaignsRoot, name + ".png"));
            CampaignRepair.TryRepairPictureFile(campaignsRoot, name);
            report.Add(".png: " + (pngExistedBefore ? "already present" : "placeholder image created"));

            if (campInitResult == ConfUpdateResult.ReferenceMissing || confModResult == ConfUpdateResult.ReferenceMissing)
            {
                report.Add("");
                report.Add("⚠ UTIL_REF_conf_mod.lua and/or UTIL_REF_camp_init.lua could not be found in ScriptsMod - please update ScriptsMod.");
            }

            // .miz manquants : on ne les régénère QUE si base_mission.miz existe (c'est le vrai
            // template, voir discussion précédente). Approche simple, choisie par Miguel : on
            // vide Active/ (First Mission n'en a pas besoin pour tourner, mais il la repeuple
            // lui-même - nécessaire ensuite pour que Skip Mission puisse générer _ongoing.miz),
            // puis on enchaîne First Mission et Skip Mission. Confirmation obligatoire : vider
            // Active/ efface toute progression déjà présente.
            bool hasBaseMission = File.Exists(Path.Combine(initFolderPath, "base_mission.miz"));
            bool firstMizMissing = !File.Exists(Path.Combine(campaignsRoot, name + "_first.miz"));
            bool ongoingMizMissing = !File.Exists(Path.Combine(campaignsRoot, name + "_ongoing.miz"));

            report.Add("");
            report.Add("base_mission.miz: " + (hasBaseMission ? "found" : "not found"));

            if ((firstMizMissing || ongoingMizMissing) && hasBaseMission)
            {
                var confirm = MessageBox.Show(
                    "The mission files for '" + name + "' are missing, but the base mission template (base_mission.miz) is present.\n\n" +
                    "DCE_Manager can generate them now by clearing the Active folder and running First Mission, then Skip Mission.\n\n" +
                    "WARNING: this will erase any saved progress currently in the Active folder for this campaign.\n\n" +
                    "Generate the missing missions now?",
                    "Generate missing missions — " + name,
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning);

                if (confirm == DialogResult.Yes)
                {
                    string activeFolderPath = Path.Combine(folderPath, "Active");

                    if (Directory.Exists(activeFolderPath))
                    {
                        foreach (string file in Directory.GetFiles(activeFolderPath, "*", SearchOption.AllDirectories))
                        {
                            try { File.Delete(file); }
                            catch (IOException) { /* fichier verrouillé, tant pis, on continue */ }
                        }
                    }

                    report.Add("Active folder cleared.");
                    report.Add("Generating missions from base_mission.miz...");

                    WargameLog.FirstMission(name);
                    await RunScriptsModInteractiveAsync(name, folderPath, "FirstMission.bat");

                    WargameEngineLosses.ProcessBeforeMission(name);
                    Saver_TargetList_Wargame.WriteNewActiveFormations(name);
                    await RunScriptsModInteractiveAsync(name, folderPath, "SkipMission.bat");
                }
            }

            _alreadyUpdated.Add(name); // évite de refaire ce travail juste après, au premier clic normal

            await LoadCampaignsAsync(selectCampaignName: name); // recharge la grid ET se repositionne dessus

            // Ce qui reste manquant après coup - fichiers propres à la campagne qu'on ne sait
            // jamais régénérer, ou .miz que tu as refusé de générer.
            var stillMissing = RequiredInitFiles.Where(f => !File.Exists(Path.Combine(initFolderPath, f))).ToList();

            string[] requiredMissionFiles = { name + "_first.miz", name + "_ongoing.miz", name + ".cmp" };
            stillMissing.AddRange(requiredMissionFiles.Where(f => !File.Exists(Path.Combine(campaignsRoot, f))));

            if (stillMissing.Count > 0)
            {
                report.Add("");
                report.Add("⚠ Still missing, cannot be repaired automatically: " + string.Join(", ", stillMissing));
            }

            MessageBox.Show(
                string.Join("\r\n", report),
                (stillMissing.Count > 0 ? "Partial " + contextLabel.ToLowerInvariant() + " — " : contextLabel + " complete — ") + name,
                MessageBoxButtons.OK,
                stillMissing.Count > 0 ? MessageBoxIcon.Warning : MessageBoxIcon.Information);
        }

        private static string DescribeResult(string fileLabel, bool existedBefore, ConfUpdateResult result)
        {
            if (result == ConfUpdateResult.ReferenceMissing)
                return fileLabel + ": not regenerated (reference file missing in ScriptsMod)";

            if (result == ConfUpdateResult.MergeAborted)
                return fileLabel + ": merge failed, local file left unchanged";

            return fileLabel + ": " + (existedBefore ? "checked / updated if needed" : "created from reference template");
        }
    }
}
