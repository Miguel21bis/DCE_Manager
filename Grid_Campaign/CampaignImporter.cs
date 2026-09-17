using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using DCE_Manager.Utils;

namespace DCE_Manager
{
    // Pendant de CampaignExporter : reconnaît et extrait le format léger "Export campagne"
    // (une seule campagne + ses fichiers satellites + livrées, produit par le bouton Export de
    // la grid), à distinguer du gros paquet de distribution complet que gère
    // ExtractZipFileToDirectory.
    internal static class CampaignImporter
    {
        // Signature du format : un fichier "xxx_first.miz" posé À LA RACINE du zip (pas de
        // sous-dossier Mods/SavedGame/OvGME comme dans un paquet de distribution complet).
        public static bool LooksLikeExportedCampaignZip(string zipPath)
        {
            using (var archive = ZipFile.OpenRead(zipPath))
            {
                foreach (ZipArchiveEntry entry in archive.Entries)
                {
                    if (!entry.FullName.Contains("/") &&
                        entry.FullName.EndsWith("_first.miz", StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        // Extrait le zip : le dossier de campagne va dans campaignsRoot (ex: "...\Mods\tech\DCE
        // \Missions\Campaigns"), les éventuelles livrées vont dans savedGamesRoot\Liveries\...
        // (leur vrai emplacement DCS). Renvoie le nom de la campagne trouvé dans l'archive.
        // onProgress(pourcentage, dossier parent en cours) : facultatif, appelé uniquement
        // quand le dossier parent change (pas à chaque fichier, trop de bruit sur des livrées
        // avec des centaines de textures).
        public static string ExtractExportedCampaignZip(string zipPath, string campaignsRoot, string savedGamesRoot,
            Action<int, string> onProgress = null)
        {
            using (var archive = ZipFile.OpenRead(zipPath))
            {
                // Le nom de la campagne = le seul dossier de campagne présent dans ce format
                // d'export (premier segment de n'importe quelle entrée contenant un "/", en
                // ignorant "Liveries" qui est un dossier "satellite" lui aussi).
                string nameCamp = archive.Entries
                    .Select(en => en.FullName)
                    .Where(fn => fn.Contains("/"))
                    .Select(fn => fn.Split('/')[0])
                    .FirstOrDefault(seg => !string.Equals(seg, "Liveries", StringComparison.OrdinalIgnoreCase));

                if (string.IsNullOrEmpty(nameCamp))
                    throw new InvalidDataException("Unrecognized package: no campaign folder found in the zip.");

                Directory.CreateDirectory(campaignsRoot);

                int liveryFilesInstalled = 0;
                int totalEntries = archive.Entries.Count;
                int processed = 0;
                string lastReportedFolder = null;

                foreach (ZipArchiveEntry entry in archive.Entries)
                {
                    bool isFolderEntry = entry.FullName.EndsWith("/");
                    bool isLiveryEntry = entry.FullName.StartsWith("Liveries/", StringComparison.OrdinalIgnoreCase);

                    string destPath;

                    if (isLiveryEntry)
                    {
                        // Liveries/<type>/<livery>/... -> Saved Games\Liveries\<type>\<livery>\...
                        destPath = Path.Combine(savedGamesRoot, entry.FullName.Replace('/', Path.DirectorySeparatorChar));
                    }
                    else if (entry.FullName.StartsWith(nameCamp + "/"))
                    {
                        // Contenu du dossier de campagne : NomCampagne/... -> campaignsRoot\NomCampagne\...
                        destPath = Path.Combine(campaignsRoot, entry.FullName.Replace('/', Path.DirectorySeparatorChar));
                    }
                    else
                    {
                        // Fichier satellite (.miz/.cmp/.png) posé à la racine du zip -> campaignsRoot directement.
                        destPath = Path.Combine(campaignsRoot, entry.FullName);
                    }

                    if (isFolderEntry)
                    {
                        Directory.CreateDirectory(destPath);
                    }
                    else
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(destPath));
                        entry.ExtractToFile(destPath, overwrite: true);

                        if (isLiveryEntry)
                            liveryFilesInstalled++;
                    }

                    processed++;

                    if (onProgress != null)
                    {
                        string parentFolder = Path.GetDirectoryName(entry.FullName.TrimEnd('/'))?.Replace('/', '\\');
                        if (parentFolder != lastReportedFolder)
                        {
                            lastReportedFolder = parentFolder;
                            int percent = totalEntries > 0 ? (processed * 100 / totalEntries) : 0;
                            onProgress(percent, parentFolder);
                        }
                    }
                }

                if (liveryFilesInstalled > 0)
                {
                    FormUtils.LogRegister("CampaignImporter | " + liveryFilesInstalled +
                        " fichier(s) de livery installé(s) dans " + Path.Combine(savedGamesRoot, "Liveries"));
                }

                FormUtils.LogRegister("CampaignImporter | Import terminé pour '" + nameCamp + "' depuis " + zipPath);

                return nameCamp;
            }
        }
    }
}