using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DCE_Manager.Parameters;
using DCE_Manager.Utils;
using Newtonsoft.Json.Linq;

namespace DCE_Manager
{
    // Gère le lien maître/fille entre campagnes (ex: "Crisis in PG-Blue" est le
    // maître de "Crisis in PG-Blue-GC22"). Stocké dans dce_manager_settings.json,
    // section "CampaignHierarchy", séparément du reste (les autres sections du
    // fichier ne sont jamais touchées par cette classe).
    //
    // Cloisonné par configuration (ParamConf.NumSelectConfig) : deux configs
    // (DCSA/DCSB...) peuvent avoir des campagnes qui portent le même nom de
    // dossier (cf. ConfModLoader.ClearCache), donc un même dico plat mélangerait
    // tout. On indexe par ID de config, pas par nom, car le nom peut être
    // renommé (voir Configuration_Form) alors que l'ID, lui, ne change jamais.
    //
    // Le modèle est volontairement à 2 niveaux (maître -> filles), pas un arbre :
    // une fille ne peut jamais avoir de fille elle-même. Toutes les méthodes qui
    // pourraient créer un 3e niveau redirigent automatiquement vers le vrai maître
    // (voir ResolveMaster).
    internal static class CampaignHierarchy
    {
        // Longueur minimum du préfixe commun pour considérer deux noms comme
        // "de la même famille" lors du classement automatique. Évite de grouper
        // des noms courts qui se ressemblent par hasard.
        private const int MinRootLength = 6;

        private const string SettingsFileName = "dce_manager_settings.json";
        private const string SectionName = "CampaignHierarchy";

        // configId -> (fille -> maître). Une campagne absente du dico de sa
        // config (ni clé, ni valeur) est soit un maître sans fille, soit une
        // campagne "seule" (standalone) : comportement inchangé pour elle.
        private static Dictionary<int, Dictionary<string, string>> _dataByConfig;

        private static string SettingsFilePath =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "DCE_Manager", SettingsFileName);

        // ------------------------------------------------------------------
        // Lecture / écriture
        // ------------------------------------------------------------------

        private static void EnsureLoaded()
        {
            if (_dataByConfig != null)
                return;

            _dataByConfig = new Dictionary<int, Dictionary<string, string>>();

            try
            {
                if (!File.Exists(SettingsFilePath))
                    return;

                JObject root = JObject.Parse(File.ReadAllText(SettingsFilePath));
                JObject section = root[SectionName] as JObject;

                if (section == null)
                    return;

                foreach (var configProp in section.Properties())
                {
                    if (!int.TryParse(configProp.Name, out int configId))
                        continue; // clé inattendue, on ignore plutôt que de planter

                    JObject inner = configProp.Value as JObject;
                    if (inner == null)
                        continue;

                    var map = new Dictionary<string, string>();
                    foreach (var childProp in inner.Properties())
                        map[childProp.Name] = childProp.Value.ToString();

                    _dataByConfig[configId] = map;
                }
            }
            catch (Exception ex)
            {
                // Fichier corrompu ou verrouillé : on repart sur du vide plutôt
                // que de planter le chargement de la grid. Le classement auto se
                // relancera au prochain LoadCampaignsAsync.
                FormUtils.LogRegister("CampaignHierarchy | erreur de lecture " + SettingsFilePath + " : " + ex.Message);
                _dataByConfig = new Dictionary<int, Dictionary<string, string>>();
            }
        }

        // Dico fille->maître de la configuration actuellement sélectionnée.
        // Créé vide à la volée si cette config n'a encore rien d'enregistré.
        private static Dictionary<string, string> CurrentMap()
        {
            EnsureLoaded();

            int configId = ParamConf.NumSelectConfig;

            if (!_dataByConfig.TryGetValue(configId, out Dictionary<string, string> map))
            {
                map = new Dictionary<string, string>();
                _dataByConfig[configId] = map;
            }

            return map;
        }

        private static void Save()
        {
            try
            {
                string folder = Path.GetDirectoryName(SettingsFilePath);
                if (!Directory.Exists(folder))
                    Directory.CreateDirectory(folder);

                JObject root;

                if (File.Exists(SettingsFilePath))
                {
                    try { root = JObject.Parse(File.ReadAllText(SettingsFilePath)); }
                    catch { root = new JObject(); } // fichier illisible : on le régénère plutôt que de tout perdre en plantant
                }
                else
                {
                    root = new JObject();
                }

                // On réécrit TOUTE la section (toutes les configs), pas seulement
                // la config courante, sinon on perdrait les autres configs à la
                // prochaine sauvegarde.
                var section = new JObject();
                foreach (var configEntry in _dataByConfig)
                {
                    var inner = new JObject();
                    foreach (var kvp in configEntry.Value)
                        inner[kvp.Key] = kvp.Value;

                    section[configEntry.Key.ToString()] = inner;
                }

                root[SectionName] = section;

                File.WriteAllText(SettingsFilePath, root.ToString());
            }
            catch (Exception ex)
            {
                FormUtils.LogRegister("CampaignHierarchy | erreur d'écriture " + SettingsFilePath + " : " + ex.Message);
            }
        }

        // ------------------------------------------------------------------
        // Consultation
        // ------------------------------------------------------------------

        // Renvoie le vrai maître d'une campagne (dans la config actuelle) : sa
        // valeur dans le dico si elle est fille, sinon elle-même (elle EST déjà
        // le maître, ou elle est seule). C'est la fonction à utiliser partout où
        // on a besoin de "qui est le chef de famille de X" (clonage, affichage...).
        public static string ResolveMaster(string campaignName)
        {
            Dictionary<string, string> map = CurrentMap();
            return map.TryGetValue(campaignName, out string master) ? master : campaignName;
        }

        // True si campaignName est fille de quelqu'un (dans la config actuelle).
        public static bool IsChild(string campaignName)
        {
            return CurrentMap().ContainsKey(campaignName);
        }

        // True si campaignName a au moins une fille (dans la config actuelle).
        public static bool IsMaster(string campaignName)
        {
            return CurrentMap().ContainsValue(campaignName);
        }

        // Filles directes d'un maître (config actuelle), triées alphabétiquement
        // (ordre stable pour l'affichage et pour la promotion automatique en cas
        // de suppression).
        public static List<string> GetChildren(string masterName)
        {
            return CurrentMap()
                .Where(kvp => kvp.Value == masterName)
                .Select(kvp => kvp.Key)
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        // ------------------------------------------------------------------
        // Modification manuelle (popup "Gérer la famille")
        // ------------------------------------------------------------------

        // Rattache childName à masterName (dans la config actuelle). Si
        // masterName est lui-même une fille, on redirige automatiquement vers
        // SON maître (pas de 3e niveau possible).
        public static void SetChild(string childName, string masterName)
        {
            if (string.IsNullOrEmpty(childName) || string.IsNullOrEmpty(masterName) || childName == masterName)
                return;

            string realMaster = ResolveMaster(masterName);

            if (realMaster == childName)
                return; // évite de se rattacher à sa propre fille

            Dictionary<string, string> map = CurrentMap();

            // Si childName était lui-même un maître (avait des filles), elles suivent
            // automatiquement vers le nouveau maître : pas de 3e niveau possible.
            foreach (string grandChild in GetChildren(childName))
                map[grandChild] = realMaster;

            map[childName] = realMaster;
            Save();
        }

        // Détache campaignName de son maître (config actuelle) : redevient seule.
        public static void Detach(string campaignName)
        {
            if (CurrentMap().Remove(campaignName))
                Save();
        }

        // ------------------------------------------------------------------
        // Clonage : appelée par le workflow de clone existant (bouton Clone).
        // Règle : le clone rejoint TOUJOURS le vrai maître de la source, jamais
        // la source elle-même si elle est déjà une fille.
        // ------------------------------------------------------------------

        public static void OnCampaignCloned(string sourceCampaignName, string newCampaignName)
        {
            SetChild(newCampaignName, ResolveMaster(sourceCampaignName));
        }

        // ------------------------------------------------------------------
        // Suppression : appelée par le workflow de suppression existant.
        // Si campaignName était un maître avec des filles, la première fille
        // restante (ordre alphabétique) est promue maître et récupère les autres.
        // Si campaignName était une simple fille, on la détache juste.
        // ------------------------------------------------------------------

        public static void OnCampaignDeleted(string campaignName)
        {
            Dictionary<string, string> map = CurrentMap();
            List<string> children = GetChildren(campaignName);

            if (children.Count > 0)
            {
                string newMaster = children[0];

                foreach (string child in children)
                {
                    if (child == newMaster)
                        map.Remove(child); // le nouveau maître n'a plus d'entrée
                    else
                        map[child] = newMaster;
                }
            }

            map.Remove(campaignName);
            Save();
        }

        // ------------------------------------------------------------------
        // Classement automatique (1ère passe uniquement)
        // ------------------------------------------------------------------

        // À appeler une fois par LoadCampaignsAsync avec la liste complète des
        // noms de campagnes présentes sur le disque (config actuelle). Les
        // campagnes déjà connues (clé ou valeur du dico) sont ignorées : le
        // classement auto ne repasse jamais sur une campagne déjà traitée ou
        // corrigée à la main.
        public static void ClassifyUnknown(IEnumerable<string> allCampaignNames)
        {
            Dictionary<string, string> map = CurrentMap();

            List<string> unknown = allCampaignNames
                .Where(n => !map.ContainsKey(n) && !map.ContainsValue(n))
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                .ToList();

            bool changed = false;

            while (unknown.Count > 0)
            {
                string anchor = unknown[0];
                unknown.RemoveAt(0);

                List<string> group = new List<string> { anchor };

                for (int i = unknown.Count - 1; i >= 0; i--)
                {
                    // Préfixe commun ("Crisis in PG-Blue..." / "Afghan Bear Trap - ...")
                    // OU suffixe commun ("Falcon over PG" / "Tomcat over PG" : le nom de
                    // l'avion change devant, mais la fin est identique).
                    if (CommonRoot(anchor, unknown[i]).Length >= MinRootLength ||
                        CommonSuffix(anchor, unknown[i]).Length >= MinRootLength)
                    {
                        group.Add(unknown[i]);
                        unknown.RemoveAt(i);
                    }
                }

                if (group.Count < 2)
                    continue; // seule dans sa "famille" -> reste standalone, rien à écrire

                string master = PickMaster(group);

                foreach (string name in group)
                {
                    if (name == master)
                        continue;

                    map[name] = master;
                    changed = true;
                }
            }

            if (changed)
                Save();
        }

        // Choisit le maître d'un groupe : le nom qui est EXACTEMENT le préfixe
        // commun de tout le groupe s'il existe (ex: "Crisis in PG-Blue" est un
        // dossier qui existe réellement), sinon le premier par ordre alphabétique
        // (ex: aucune campagne ne s'appelle juste "Afghan Bear Trap").
        private static string PickMaster(List<string> group)
        {
            string root = group[0];
            for (int i = 1; i < group.Count; i++)
                root = CommonRoot(root, group[i]);

            string exactMatch = group.FirstOrDefault(n => string.Equals(n, root, StringComparison.OrdinalIgnoreCase));

            return exactMatch ?? group.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).First();
        }

        // Suffixe commun entre deux noms (ex: "over PG" entre "Falcon over PG" et
        // "Tomcat over PG"), avec le même "recul" jusqu'au séparateur logique que
        // CommonRoot. Astuce : on retrouve un suffixe commun en cherchant un préfixe
        // commun sur les chaînes inversées, puis en ré-inversant le résultat.
        private static string CommonSuffix(string a, string b)
        {
            string reversedResult = CommonRoot(
                new string(a.Reverse().ToArray()),
                new string(b.Reverse().ToArray()));

            return new string(reversedResult.Reverse().ToArray());
        }

        // Préfixe commun entre deux noms, "reculé" jusqu'au dernier séparateur
        // logique (espace, -, _) sauf si l'un des deux noms EST entièrement ce
        // préfixe (cas "Crisis in PG-Blue" / "Crisis in PG-Blue-GC22").
        private static string CommonRoot(string a, string b)
        {
            int max = Math.Min(a.Length, b.Length);
            int i = 0;

            while (i < max && char.ToLowerInvariant(a[i]) == char.ToLowerInvariant(b[i]))
                i++;

            if (i == a.Length || i == b.Length)
                return a.Substring(0, i);

            string common = a.Substring(0, i);
            int lastSep = common.LastIndexOfAny(new[] { ' ', '-', '_' });

            if (lastSep < 0)
                return "";

            return common.Substring(0, lastSep).TrimEnd(' ', '-', '_');
        }
    }
}
