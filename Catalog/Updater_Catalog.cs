using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;
using DCE_Manager.Parameters;
using DCE_Manager.Utils;
using Newtonsoft.Json;

namespace DCE_Manager.Update
{
    // Racine du fichier campaigns.json (hébergé dans un Gist GitHub).
    public class CatalogRoot
    {
        public string Updated { get; set; }

        // Bandeau optionnel affiché en haut de l'onglet (annonce ponctuelle). Vide = rien.
        public string Message { get; set; }

        public List<CatalogCampaign> Campaigns { get; set; } = new List<CatalogCampaign>();
    }

    // Une campagne du catalogue.
    public class CatalogCampaign
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Author { get; set; }
        public string Map { get; set; }
        public string Version { get; set; }

        // Date d'ajout/mise à jour, au format yyyy-MM-dd (dd-MM-yyyy toléré aussi).
        public string Added { get; set; }

        public string Changes { get; set; }

        [JsonProperty("url_download")]
        public string UrlDownload { get; set; }

        [JsonProperty("url_detail")]
        public string UrlDetail { get; set; }

        public string Description { get; set; }

        // Anciens noms de dossier connus pour cette campagne (aide au rapprochement
        // quand le nom du dossier installé ne ressemble pas au nom du catalogue).
        public List<string> Aliases { get; set; } = new List<string>();

        // Appareils jouables dans cette campagne, ex: ["Mi-24P", "SA342M"]. Sert au filtre "Aircraft".
        [JsonProperty("planeType")]
        public List<string> PlaneType { get; set; } = new List<string>();

        // Texte du bouton/de la ligne quand cette entrée est une variante ("Hind", "Blue"...).
        // Vide = on déduit le texte du nom (ce qui suit le dernier "-").
        public string Label { get; set; }

        // Campagnes "jumelles" regroupées sur UNE carte (Hind / Gazelle / Mirage...).
        // Chaque variante a son id, son nom, ses liens ; version/auteur/map/appareils sont
        // repris de la carte principale quand la variante ne les précise pas.
        public List<CatalogCampaign> Variants { get; set; } = new List<CatalogCampaign>();
    }

    // Remplace Updater_News : même branchement dans Main_Form (vérification au démarrage,
    // compteur sur l'onglet, affichage à l'ouverture de l'onglet), mais le contenu est
    // maintenant le catalogue des campagnes disponibles en ligne.
    public class Updater_Catalog
    {
        private readonly Main_Form form;

        // Lien "raw" du Gist, SANS le segment de révision (sinon il reste figé sur une vieille version).
        private const string CatalogUrl =
            "https://gist.githubusercontent.com/Miguel21bis/8720c35a55290b05579c0b4eeac9a312/raw/campaigns.json";

        private CatalogRoot _root;          // dernier catalogue lu (internet, sinon cache)
        private bool _usingCache;           // vrai si on affiche la copie locale (hors ligne)
        private bool _isLoading;
        private bool _downloading;          // un seul téléchargement à la fois
        private string _filterMap = "";      // filtre "Map" (vide = toutes)
        private HashSet<string> _filterPlanes = new HashSet<string>(StringComparer.OrdinalIgnoreCase); // filtre "Aircraft" (vide = tous)
        private ToolStripDropDown _planeMenu;  // liste à cocher des appareils (avec ascenseur)
        private static HashSet<string> _allKeys = new HashSet<string>(); // noms de tout le catalogue (voir FindInstalled)
        private bool _userCancelled;        // vrai si l'utilisateur a cliqué sur Cancel (et pas une panne réseau)
        private Panel _panel;               // panneau actuellement affiché dans l'onglet

        // Campagne trouvée dans les dossiers installés (lecture de Init\camp_init.lua).
        private class InstalledCampaign
        {
            public string Folder;       // nom du dossier tel qu'il est sur le disque
            public string BaseName;     // nom du dossier sans le suffixe de version (" 9.4")
            public string CampaignId;   // peut être vide sur les anciennes campagnes
            public string Version;
        }

        public Updater_Catalog(Main_Form form)
        {
            this.form = form;
        }


        // ============================================================================
        // Téléchargement du catalogue + compteur sur l'onglet
        // ============================================================================

        // Lit le catalogue sur internet (cache local si hors ligne ou JSON cassé), puis met à jour
        // le compteur "Catalog (n)" = campagnes ajoutées depuis la dernière ouverture de l'onglet.
        public async Task CheckCatalogAsync()
        {
            _isLoading = true;

            try
            {
                using (HttpClient client = new HttpClient())
                {
                    client.Timeout = TimeSpan.FromSeconds(15);
                    client.DefaultRequestHeaders.Add("User-Agent", "DCE_Manager");

                    string json = await client.GetStringAsync(CatalogUrl);

                    // On lit AVANT d'écrire le cache : un JSON cassé ne doit pas écraser une bonne copie.
                    CatalogRoot parsed = JsonConvert.DeserializeObject<CatalogRoot>(json);

                    if (parsed == null || parsed.Campaigns == null)
                        throw new Exception("catalogue vide");

                    CleanCatalog(parsed);

                    _root = parsed;
                    _usingCache = false;

                    SaveCache(json);
                }
            }
            catch (Exception ex)
            {
                FormUtils.LogRegister("CheckCatalogAsync : catalogue internet inutilisable (" + ex.Message + "), essai du cache local.");

                _root = LoadCache();
                _usingCache = _root != null;
            }
            finally
            {
                _isLoading = false;
            }

            int unseen = CountUnseen();

            form.tabPageLeftCatalog.Text = unseen > 0 ? "Catalog (" + unseen + ")" : "Catalog";

            // Si l'utilisateur a ouvert l'onglet pendant le téléchargement, on le reconstruit.
            if (form.tabControl_LEFT.SelectedTab == form.tabPageLeftCatalog)
                DisplayCatalog();
        }

        // Retire les entrées sans nom (inutilisables), remplace les null par des valeurs vides
        // et fait hériter les variantes des infos de la carte principale.
        private static void CleanCatalog(CatalogRoot root)
        {
            root.Campaigns.RemoveAll(c => c == null || string.IsNullOrWhiteSpace(c.Name));

            foreach (CatalogCampaign c in root.Campaigns)
                CleanOne(c, null);
        }

        private static void CleanOne(CatalogCampaign c, CatalogCampaign parent)
        {
            if (c.Aliases == null)
                c.Aliases = new List<string>();

            c.PlaneType = (c.PlaneType ?? new List<string>())
                .Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p.Trim()).ToList();

            if (c.Variants == null)
                c.Variants = new List<CatalogCampaign>();

            if (parent != null)
            {
                if (string.IsNullOrWhiteSpace(c.Version)) c.Version = parent.Version;
                if (string.IsNullOrWhiteSpace(c.Author)) c.Author = parent.Author;
                if (string.IsNullOrWhiteSpace(c.Map)) c.Map = parent.Map;
                if (string.IsNullOrWhiteSpace(c.Added)) c.Added = parent.Added;
                if (c.PlaneType.Count == 0) c.PlaneType = new List<string>(parent.PlaneType);
            }

            c.Variants.RemoveAll(v => v == null || string.IsNullOrWhiteSpace(v.Name));

            foreach (CatalogCampaign v in c.Variants)
                CleanOne(v, c);
        }

        private static string CachePath
        {
            get { return Path.Combine(ParamManager.pathManager, "catalog_cache.json"); }
        }

        private static void SaveCache(string json)
        {
            try
            {
                Directory.CreateDirectory(ParamManager.pathManager);
                File.WriteAllText(CachePath, json);
            }
            catch (Exception ex)
            {
                FormUtils.LogRegister("SaveCache catalogue : " + ex.Message);
            }
        }

        private static CatalogRoot LoadCache()
        {
            try
            {
                if (!File.Exists(CachePath))
                    return null;

                CatalogRoot cached = JsonConvert.DeserializeObject<CatalogRoot>(File.ReadAllText(CachePath));

                if (cached == null || cached.Campaigns == null)
                    return null;

                CleanCatalog(cached);
                return cached;
            }
            catch (Exception ex)
            {
                FormUtils.LogRegister("LoadCache catalogue : " + ex.Message);
                return null;
            }
        }


        // ============================================================================
        // Dates et compteur "non vus"
        // ============================================================================

        private static DateTime ParseDate(string text)
        {
            DateTime d;

            if (DateTime.TryParseExact((text ?? "").Trim(), new[] { "yyyy-MM-dd", "dd-MM-yyyy" },
                CultureInfo.InvariantCulture, DateTimeStyles.None, out d))
                return d;

            return DateTime.MinValue;
        }

        // Dernière date vue, relue depuis ParamConf.LastNewsVersion (même clé qu'avant dans options.txt).
        // Les 10 premiers caractères suffisent : un ancien identifiant de news comme
        // "2024-06-16-persian-gulf-update" donne bien "2024-06-16".
        private static DateTime ParseLastSeen()
        {
            string raw = ParamConf.LastNewsVersion ?? "";

            if (raw.Length >= 10)
                raw = raw.Substring(0, 10);

            return ParseDate(raw);
        }

        private int CountUnseen()
        {
            if (_root == null)
                return 0;

            DateTime lastSeen = ParseLastSeen();

            return _root.Campaigns.Count(c => ParseDate(c.Added) > lastSeen);
        }

        private static bool IsRecent(CatalogCampaign c)
        {
            DateTime added = ParseDate(c.Added);

            return added != DateTime.MinValue && (DateTime.Today - added).TotalDays <= 30;
        }


        // ============================================================================
        // Rapprochement catalogue <-> campagnes installées
        // ============================================================================

        // Minuscules, lettres et chiffres seulement : "NAM-Red", "nam red" et "NAM_Red" deviennent "namred".
        private static string Normalize(string text)
        {
            if (string.IsNullOrEmpty(text))
                return "";

            return new string(text.Where(ch => char.IsLetterOrDigit(ch)).Select(ch => char.ToLowerInvariant(ch)).ToArray());
        }

        // Lit camp_init.lua de chaque dossier de campagne installé.
        private List<InstalledCampaign> ScanInstalledCampaigns()
        {
            List<InstalledCampaign> list = new List<InstalledCampaign>();

            if (string.IsNullOrEmpty(ParamConf.PATH_SavedGames_DCS))
                return list;

            string root = Path.Combine(ParamConf.PATH_SavedGames_DCS, @"Mods\tech\DCE\Missions\Campaigns");

            if (!Directory.Exists(root))
                return list;

            foreach (string dir in Directory.GetDirectories(root))
            {
                string campInit = Path.Combine(dir, @"Init\camp_init.lua");

                if (!File.Exists(campInit))
                    continue;

                string text;

                try { text = File.ReadAllText(campInit); }
                catch { continue; } // fichier verrouillé : on passe

                InstalledCampaign item = new InstalledCampaign();
                item.Folder = Path.GetFileName(dir);

                // Retire un suffixe de version éventuel (" 9.4", " v2.1") ajouté à l'installation.
                item.BaseName = Regex.Replace(item.Folder, @"\s+v?\d+(\.\d+)*$", "", RegexOptions.IgnoreCase);

                Match match = Regex.Match(text, @"campaignId\s*=\s*""([^""]+)""");
                item.CampaignId = match.Success ? match.Groups[1].Value : "";

                match = Regex.Match(text, @"(?<!\w)version\s*=\s*""([^""]+)""");
                item.Version = match.Success ? match.Groups[1].Value : "";

                list.Add(item);
            }

            return list;
        }

        // Vrai si le NOM de ce dossier correspond à une entrée du catalogue (nom, id ou alias).
        // Pourquoi : deux campagnes sœurs copiées l'une de l'autre gardent parfois le même campaignId ;
        // si on s'y fiait, le dossier de l'une serait compté comme installé pour l'autre aussi.
        private static bool FolderClaimedByName(InstalledCampaign i)
        {
            return _allKeys.Contains(Normalize(i.Folder)) || _allKeys.Contains(Normalize(i.BaseName));
        }

        private static void CollectKeys(CatalogCampaign c, HashSet<string> keys)
        {
            keys.Add(Normalize(c.Name));
            keys.Add(Normalize(c.Id));

            foreach (string alias in c.Aliases)
                keys.Add(Normalize(alias));

            foreach (CatalogCampaign v in c.Variants)
                CollectKeys(v, keys);
        }

        // Une campagne du catalogue correspond à un dossier installé si, dans l'ordre :
        // - campaignId == id du catalogue
        // - le nom du dossier (avec ou sans suffixe de version) ressemble au nom, à l'id ou à un alias
        private static List<InstalledCampaign> FindInstalled(CatalogCampaign c, List<InstalledCampaign> installed)
        {
            HashSet<string> keys = new HashSet<string>();
            keys.Add(Normalize(c.Name));
            keys.Add(Normalize(c.Id));

            foreach (string alias in c.Aliases)
                keys.Add(Normalize(alias));

            keys.Remove(""); // une clé vide ferait tout correspondre

            return installed.Where(i =>
                (!string.IsNullOrEmpty(i.CampaignId) && !string.IsNullOrEmpty(c.Id)
                    && string.Equals(i.CampaignId, c.Id, StringComparison.OrdinalIgnoreCase)
                    && !FolderClaimedByName(i))
                || keys.Contains(Normalize(i.Folder))
                || keys.Contains(Normalize(i.BaseName))).ToList();
        }


        // ============================================================================
        // Affichage (cartes)
        // ============================================================================

        // Construit l'affichage de l'onglet et marque les campagnes actuelles comme vues.
        // Pourquoi : appelé quand l'utilisateur ouvre réellement l'onglet -> le compteur ne doit
        // disparaître qu'à ce moment-là.
        public void DisplayCatalog()
        {
            TabPage tab = form.tabPageLeftCatalog;

            if (_panel != null)
            {
                tab.Controls.Remove(_panel);
                _panel.Dispose();
            }

            tab.Controls.Clear(); // anciens contrôles du Designer (panel_News, textBox_News...)

            _panel = new Panel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                Padding = new Padding(10)
            };

            tab.Controls.Add(_panel);

            if (_isLoading)
            {
                AddInfoLabel("Loading catalog...", 10);
                return;
            }

            if (_root == null || _root.Campaigns.Count == 0)
            {
                AddInfoLabel("No catalog available (offline or catalog unreachable).", 10);
                return;
            }

            int y = 10;
            int cardWidth = Math.Max(_panel.ClientSize.Width - 30, 320);

            // Bandeau d'annonce (champ "message" du JSON)
            if (!string.IsNullOrWhiteSpace(_root.Message))
            {
                int textHeight = TextRenderer.MeasureText(_root.Message, form.Font,
                    new Size(cardWidth - 20, 0), TextFormatFlags.WordBreak).Height;

                Label banner = new Label
                {
                    Text = _root.Message,
                    BackColor = Color.LightYellow,
                    BorderStyle = BorderStyle.FixedSingle,
                    Padding = new Padding(8, 6, 8, 6),
                    AutoSize = false,
                    Location = new Point(10, y),
                    Size = new Size(cardWidth, textHeight + 20),
                    Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
                };

                _panel.Controls.Add(banner);
                y += banner.Height + 10;
            }

            if (_usingCache)
            {
                Label offline = AddInfoLabel("Offline: showing the last saved copy of the catalog.", y);
                offline.ForeColor = Color.Gray;
                y += offline.Height + 6;
            }

            HashSet<string> allKeys = new HashSet<string>();

            foreach (CatalogCampaign camp in _root.Campaigns)
                CollectKeys(camp, allKeys);

            allKeys.Remove("");
            _allKeys = allKeys;

            List<CatalogCampaign> shown = _root.Campaigns
                .Where(Matches)
                .OrderByDescending(x => ParseDate(x.Added))
                .ThenBy(x => x.Name)
                .ToList();

            y = AddFilterBar(y, shown.Count, _root.Campaigns.Count);

            if (shown.Count == 0)
                AddInfoLabel("No campaign matches these filters.", y);

            List<InstalledCampaign> installed = ScanInstalledCampaigns();

            foreach (CatalogCampaign c in shown)
            {
                Panel card = BuildCard(c, installed, cardWidth);
                card.Location = new Point(10, y);

                _panel.Controls.Add(card);
                y += card.Height + 10;
            }

            // Tout ce qui est affiché est maintenant "vu" (enregistré au prochain FormClosed).
            DateTime newest = _root.Campaigns.Max(c => ParseDate(c.Added));

            if (newest != DateTime.MinValue)
            {
                string newestText = newest.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

                ParamConf.LastNewsVersion = newestText;
                ParamConf.configDictionary["LastNewsVersion"] = newestText;
            }

            tab.Text = "Catalog";
        }

        // Tous les appareils d'une carte, variantes comprises.
        private static IEnumerable<string> AllPlanes(CatalogCampaign c)
        {
            return c.PlaneType.Concat(c.Variants.SelectMany(v => v.PlaneType));
        }

        // La carte passe-t-elle les filtres Map / Aircraft ? (une variante qui correspond suffit)
        private bool Matches(CatalogCampaign c)
        {
            bool mapOk = string.IsNullOrEmpty(_filterMap)
                || string.Equals((c.Map ?? "").Trim(), _filterMap, StringComparison.OrdinalIgnoreCase)
                || c.Variants.Any(v => string.Equals((v.Map ?? "").Trim(), _filterMap, StringComparison.OrdinalIgnoreCase));

            // Plusieurs appareils cochés : la campagne passe si elle en a AU MOINS UN.
            bool planeOk = _filterPlanes.Count == 0
                || AllPlanes(c).Any(p => _filterPlanes.Contains(p.Trim()));

            return mapOk && planeOk;
        }

        // Barre "Map : [..]  Aircraft : [..]  n / total". Renvoie le Y suivant.
        private int AddFilterBar(int y, int shownCount, int totalCount)
        {
            List<string> maps = _root.Campaigns
                .SelectMany(c => new[] { c.Map }.Concat(c.Variants.Select(v => v.Map)))
                .Where(m => !string.IsNullOrWhiteSpace(m)).Select(m => m.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(m => m, StringComparer.OrdinalIgnoreCase).ToList();

            List<string> planes = _root.Campaigns
                .SelectMany(c => AllPlanes(c))
                .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToList();

            Label lblMap = new Label { Text = "Map:", AutoSize = true, Location = new Point(10, y + 4) };
            ComboBox cbMap = MakeFilterCombo("All maps", maps, _filterMap, 48, y, 150, value => _filterMap = value);

            Label lblPlane = new Label { Text = "Aircraft:", AutoSize = true, Location = new Point(210, y + 4) };
            Button btnPlane = MakePlaneButton(planes, 268, y, 150);

            Label count = new Label
            {
                Text = shownCount + " / " + totalCount + " campaigns",
                ForeColor = Color.Gray,
                AutoSize = true,
                Location = new Point(430, y + 4)
            };

            _panel.Controls.AddRange(new Control[] { lblMap, cbMap, lblPlane, btnPlane, count });

            return y + cbMap.Height + 10;
        }

        // Bouton "All aircraft / F-14B / 3 aircraft" qui ouvre une liste à cocher avec ascenseur
        // (hauteur fixe, quel que soit le nombre d'appareils).
        // L'affichage n'est reconstruit qu'à la fermeture de la liste.
        private Button MakePlaneButton(List<string> planes, int x, int y, int width)
        {
            // Un appareil coché qui n'existe plus dans le catalogue est oublié
            _filterPlanes.RemoveWhere(p => !planes.Contains(p, StringComparer.OrdinalIgnoreCase));

            Button btn = new Button
            {
                Location = new Point(x, y - 1),
                Width = width,
                Height = 23,
                TextAlign = ContentAlignment.MiddleLeft,
                UseVisualStyleBackColor = true
            };

            Action refreshText = () =>
            {
                string text = _filterPlanes.Count == 0 ? "All aircraft"
                    : _filterPlanes.Count == 1 ? _filterPlanes.First()
                    : _filterPlanes.Count + " aircraft";

                btn.Text = text + "  \u25BE";
            };

            refreshText();

            bool changed = false;

            CheckedListBox list = new CheckedListBox
            {
                Dock = DockStyle.Fill,
                CheckOnClick = true,
                IntegralHeight = false,
                BorderStyle = BorderStyle.None
            };

            foreach (string plane in planes)
                list.Items.Add(plane, _filterPlanes.Contains(plane));

            // Branché APRÈS le remplissage : sinon il se déclencherait pour chaque ligne ajoutée.
            list.ItemCheck += (s, e) =>
            {
                string name = (string)list.Items[e.Index];

                if (e.NewValue == CheckState.Checked)
                    _filterPlanes.Add(name);
                else
                    _filterPlanes.Remove(name);

                changed = true;
                refreshText();
            };

            Button clear = new Button { Text = "Clear selection", Dock = DockStyle.Top, Height = 26 };

            clear.Click += (s, e) =>
            {
                for (int k = 0; k < list.Items.Count; k++)
                    list.SetItemChecked(k, false);

                _filterPlanes.Clear();
                changed = true;
                refreshText();
            };

            // Bouton OK : referme la liste (c'est la fermeture qui applique le filtre).
            Button ok = new Button { Text = "OK", Dock = DockStyle.Bottom, Height = 28 };

            ok.Click += (s, e) => _planeMenu.Close();

            // Hauteur fixe d'environ 10 lignes : au-delà, l'ascenseur apparaît tout seul.
            Panel panel = new Panel { Size = new Size(Math.Max(width, 190), 26 + 10 * 17 + 28 + 4) };
            panel.Controls.Add(list);   // Fill d'abord, puis Top et Bottom : l'ordre d'ajout compte pour le Dock
            panel.Controls.Add(clear);
            panel.Controls.Add(ok);

            ToolStripControlHost host = new ToolStripControlHost(panel)
            {
                AutoSize = false,
                Size = panel.Size,
                Margin = Padding.Empty,
                Padding = Padding.Empty
            };

            if (_planeMenu != null)
                _planeMenu.Dispose();

            _planeMenu = new ToolStripDropDown { AutoClose = true };
            _planeMenu.Items.Add(host);

            _planeMenu.Opened += (s, e) => list.Focus();   // pour que la molette défile la liste

            _planeMenu.Closed += (s, e) =>
            {
                if (changed)
                {
                    changed = false;
                    form.BeginInvoke(new Action(DisplayCatalog));
                }
            };

            btn.Click += (s, e) => _planeMenu.Show(btn, new Point(0, btn.Height));

            return btn;
        }

        private ComboBox MakeFilterCombo(string allText, List<string> values, string selected, int x, int y, int width, Action<string> onChange)
        {
            ComboBox cb = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Location = new Point(x, y), Width = width };

            cb.Items.Add(allText);

            foreach (string v in values)
                cb.Items.Add(v);

            int index = string.IsNullOrEmpty(selected) ? 0 : cb.Items.IndexOf(selected);
            cb.SelectedIndex = index < 0 ? 0 : index;

            // Branché APRÈS la sélection de départ : sinon il se déclencherait tout seul.
            cb.SelectedIndexChanged += (s, e) =>
            {
                onChange(cb.SelectedIndex <= 0 ? "" : (string)cb.SelectedItem);

                // Reconstruction différée : on ne détruit pas le ComboBox pendant son propre événement.
                form.BeginInvoke(new Action(DisplayCatalog));
            };

            return cb;
        }

        private Label AddInfoLabel(string text, int y)
        {
            Label label = new Label
            {
                Text = text,
                AutoSize = true,
                Location = new Point(10, y)
            };

            _panel.Controls.Add(label);
            return label;
        }

        // Vrai si une version identique de cette entrée est déjà installée (rien à télécharger).
        private static bool IsUpToDate(CatalogCampaign item, List<InstalledCampaign> installedAll)
        {
            List<InstalledCampaign> found = FindInstalled(item, installedAll);

            return found.Count > 0
                && (string.IsNullOrWhiteSpace(item.Version)
                    || found.Any(i => string.Equals(CleanVersion(i.Version), CleanVersion(item.Version), StringComparison.OrdinalIgnoreCase)));
        }

        // Texte d'une variante : champ "label", sinon ce qui suit le dernier "-" du nom, sinon le nom entier.
        private static string VariantLabel(CatalogCampaign v)
        {
            if (!string.IsNullOrWhiteSpace(v.Label))
                return v.Label.Trim();

            // "India-Pak War-71 - MiG-19" -> "MiG-19" (séparateur " - " en priorité), "Cyprus Incident-Hind" -> "Hind"
            int dash = v.Name.LastIndexOf(" - ", StringComparison.Ordinal);
            int sepLength = 3;

            if (dash < 0)
            {
                dash = v.Name.LastIndexOf('-');
                sepLength = 1;
            }

            if (dash >= 0 && dash + sepLength < v.Name.Length)
                return v.Name.Substring(dash + sepLength).Trim();

            return v.Name;
        }

        // Une carte = une campagne (ou un groupe de campagnes jumelles) :
        // titre + badges, auteur/map, appareils, description, nouveautés, une ligne de boutons par entrée.
        // Pas de "UPDATE AVAILABLE" ici : les mises à jour sont dans l'onglet Update.
        private Panel BuildCard(CatalogCampaign c, List<InstalledCampaign> installedAll, int width)
        {
            // Dossiers installés correspondant à la carte : la campagne elle-même + toutes ses variantes
            List<InstalledCampaign> found = FindInstalled(c, installedAll);

            foreach (CatalogCampaign v in c.Variants)
                foreach (InstalledCampaign i in FindInstalled(v, installedAll))
                    if (!found.Contains(i))
                        found.Add(i);

            Panel card = new Panel
            {
                Width = width,
                BackColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };

            const int margin = 14;
            int y = 10;
            int wrapWidth = width - 2 * margin - 4;

            Font normalFont = form.Font;
            Font smallFont = new Font(form.Font.FontFamily, 8.5f);

            // ----- Titre -----
            Label title = new Label
            {
                Text = c.Name,
                Font = new Font(form.Font.FontFamily, 11f, FontStyle.Bold),
                AutoSize = true,
                Location = new Point(margin, y)
            };
            card.Controls.Add(title);

            // ----- Badges, juste après le titre -----
            int bx = margin + title.PreferredSize.Width + 10;

            if (IsRecent(c))
                bx = AddBadge(card, "NEW", Color.DarkOrange, bx, y + 2);

            // État d'installation : pour un groupe, on compte les variantes installées.
            List<CatalogCampaign> items = c.Variants.Count > 0 ? c.Variants : new List<CatalogCampaign> { c };
            int installedCount = items.Count(it => FindInstalled(it, installedAll).Count > 0);

            // Groupe dont seule la carte principale correspond à un dossier installé
            if (c.Variants.Count > 0 && installedCount == 0 && FindInstalled(c, installedAll).Count > 0)
                installedCount = items.Count;

            if (installedCount == 0)
                AddBadge(card, "AVAILABLE", Color.ForestGreen, bx, y + 2);
            else if (installedCount == items.Count)
                AddBadge(card, "INSTALLED", Color.SlateGray, bx, y + 2);
            else
                AddBadge(card, "INSTALLED " + installedCount + "/" + items.Count, Color.SlateGray, bx, y + 2);

            // ----- Version + date, alignées à droite -----
            DateTime added = ParseDate(c.Added);

            string rightText = (string.IsNullOrWhiteSpace(c.Version) ? "" : "v" + c.Version)
                + (added != DateTime.MinValue ? "     " + added.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : "");

            if (rightText.Length > 0)
            {
                Label right = new Label
                {
                    Text = rightText,
                    ForeColor = Color.Gray,
                    AutoSize = true,
                    Anchor = AnchorStyles.Top | AnchorStyles.Right
                };

                right.Location = new Point(width - 2 - right.PreferredSize.Width - margin, y + 3);
                card.Controls.Add(right);
            }

            y += title.PreferredSize.Height + 4;

            // ----- Auteur / map -----
            string subtitle = string.Join("   |   ",
                new[] { c.Author, c.Map }.Where(s => !string.IsNullOrWhiteSpace(s)));

            if (subtitle.Length > 0)
            {
                Label sub = new Label { Text = subtitle, ForeColor = Color.DimGray, AutoSize = true, Location = new Point(margin, y) };
                card.Controls.Add(sub);
                y += sub.PreferredSize.Height + 4;
            }

            // ----- Appareils -----
            string planes = string.Join(", ", AllPlanes(c).Distinct(StringComparer.OrdinalIgnoreCase));

            if (planes.Length > 0)
            {
                Label pl = MakeWrapLabel("Aircraft: " + planes, smallFont, Color.DimGray, margin, y, wrapWidth);
                card.Controls.Add(pl);
                y += pl.Height + 2;
            }

            // ----- Version(s) installée(s) -----
            string versions = string.Join(", ", found.Select(i => i.Version).Where(v => !string.IsNullOrWhiteSpace(v)).Distinct().OrderByDescending(v => CleanVersion(v), StringComparer.OrdinalIgnoreCase));

            if (versions.Length > 0 && c.Variants.Count == 0)
            {
                Label inst = new Label { Text = "Installed: " + versions, ForeColor = Color.DimGray, Font = smallFont, AutoSize = true, Location = new Point(margin, y) };
                card.Controls.Add(inst);
                y += inst.PreferredSize.Height + 4;
            }

            // ----- Description -----
            if (!string.IsNullOrWhiteSpace(c.Description))
            {
                Label desc = MakeWrapLabel(c.Description, normalFont, Color.Black, margin, y, wrapWidth);
                card.Controls.Add(desc);
                y += desc.Height + 4;
            }

            // ----- Nouveautés -----
            if (!string.IsNullOrWhiteSpace(c.Changes))
            {
                Label changes = MakeWrapLabel("What's new: " + c.Changes, smallFont, Color.DimGray, margin, y, wrapWidth);
                card.Controls.Add(changes);
                y += changes.Height + 4;
            }

            // ----- Boutons : une ligne par entrée (la carte elle-même, puis chaque variante) -----
            // Progression partagée par toute la carte : un seul téléchargement à la fois.
            List<Button> downloadButtons = new List<Button>();

            Button cancel = new Button { Text = "Cancel", Width = 70, Height = 24, Visible = false };
            ProgressBar bar = new ProgressBar { Height = 14, Visible = false };
            Label status = new Label { Font = smallFont, ForeColor = Color.DimGray, AutoSize = false, Height = 18, Width = wrapWidth, Visible = false };

            cancel.Click += (s, e) => { _userCancelled = true; form.campaignUpdater.CancelDownload(); };

            void AddRow(CatalogCampaign item, string label)
            {
                bool hasDownload = IsWebUrl(item.UrlDownload) && !IsManualOnlyHost(item.UrlDownload) && !IsUpToDate(item, installedAll);
                bool hasPage = IsWebUrl(item.UrlDetail);

                if (!hasDownload && !hasPage)
                    return;

                int x = margin;

                if (!string.IsNullOrEmpty(label))
                {
                    List<InstalledCampaign> itemFound = FindInstalled(item, installedAll);
                    bool isInstalled = itemFound.Count > 0;

                    string itemVersions = string.Join(", ", itemFound.Select(f => f.Version)
                        .Where(ver => !string.IsNullOrWhiteSpace(ver)).Distinct());

                    // Colonne 1 : nom de la variante
                    Label rowLabel = new Label
                    {
                        Text = label,
                        Font = new Font(form.Font.FontFamily, 9f, FontStyle.Bold),
                        ForeColor = isInstalled ? Color.SlateGray : Color.Black,
                        AutoSize = false,
                        AutoEllipsis = true,
                        TextAlign = ContentAlignment.MiddleLeft,
                        Size = new Size(190, 26),
                        Location = new Point(x, y + 4)
                    };
                    card.Controls.Add(rowLabel);

                    // Colonne 2 : état (vert si pas encore installée, gris + version si installée)
                    Label rowState = new Label
                    {
                        Text = isInstalled ? "installed" + (itemVersions.Length > 0 ? " " + itemVersions : "") : "available",
                        Font = new Font(form.Font.FontFamily, 8.5f, isInstalled ? FontStyle.Regular : FontStyle.Bold),
                        ForeColor = isInstalled ? Color.SlateGray : Color.ForestGreen,
                        AutoSize = false,
                        AutoEllipsis = true,
                        TextAlign = ContentAlignment.MiddleLeft,
                        Size = new Size(130, 26),
                        Location = new Point(x + 190, y + 4)
                    };
                    card.Controls.Add(rowState);

                    x += 190 + 130 + 4;
                }

                if (hasDownload)
                {
                    Button download = new Button { Text = "Download", Width = 100, Height = 26, Location = new Point(x, y + 4), Cursor = Cursors.Hand };

                    download.Click += async (s, e) => await DownloadAndInstallAsync(item, downloadButtons, cancel, bar, status);

                    card.Controls.Add(download);
                    downloadButtons.Add(download);
                    x += 108;
                }

                if (hasPage)
                {
                    Button open = new Button { Text = "Open page", Width = 100, Height = 26, Location = new Point(x, y + 4), Cursor = Cursors.Hand };

                    string url = item.UrlDetail;
                    open.Click += (s, e) => OpenUrl(url);

                    card.Controls.Add(open);
                }

                y += 26 + 4;
            }

            if (c.Variants.Count > 0)
            {
                // Si la carte principale a ses propres liens, elle garde sa ligne ("All")
                AddRow(c, "All");

                foreach (CatalogCampaign v in c.Variants)
                    AddRow(v, VariantLabel(v));
            }
            else
            {
                AddRow(c, null);
            }

            // Place réservée à la progression (visible seulement pendant un téléchargement)
            if (downloadButtons.Count > 0)
            {
                cancel.Location = new Point(margin, y + 2);
                bar.Location = new Point(margin + 78, y + 7);
                bar.Width = Math.Max(100, wrapWidth - 78);
                status.Location = new Point(margin, y + 2 + 24 + 2);

                card.Controls.Add(cancel);
                card.Controls.Add(bar);
                card.Controls.Add(status);

                y += 2 + 24 + 2 + 18 + 4;
            }

            card.Height = y + 12;
            return card;
        }

        // Étiquette colorée type "NEW" / "INSTALLED". Renvoie le X où placer la suivante.
        private int AddBadge(Panel card, string text, Color color, int x, int y)
        {
            Label badge = new Label
            {
                Text = text,
                Font = new Font(form.Font.FontFamily, 8.25f, FontStyle.Bold),
                ForeColor = Color.White,
                BackColor = color,
                AutoSize = true,
                Padding = new Padding(5, 2, 5, 2),
                Location = new Point(x, y)
            };

            card.Controls.Add(badge);
            return x + badge.PreferredSize.Width + 6;
        }

        // Label dont la hauteur est calculée à la main pour un texte qui passe à la ligne.
        private static Label MakeWrapLabel(string text, Font font, Color color, int x, int y, int wrapWidth)
        {
            int textHeight = TextRenderer.MeasureText(text, font, new Size(wrapWidth, 0), TextFormatFlags.WordBreak).Height;

            return new Label
            {
                Text = text,
                Font = font,
                ForeColor = color,
                AutoSize = false,
                Location = new Point(x, y),
                Size = new Size(wrapWidth, textHeight + 6)
            };
        }


        // ============================================================================
        // Téléchargement + installation d'une campagne
        // ============================================================================

        // Sites qui refusent les téléchargements faits par un programme (erreur 403) :
        // pour ces liens on ne propose pas Download, seulement "Open page".
        private static bool IsManualOnlyHost(string url)
        {
            Uri uri;

            if (!Uri.TryCreate(url, UriKind.Absolute, out uri))
                return false;

            string host = uri.Host.ToLowerInvariant();

            return host == "digitalcombatsimulator.com" || host.EndsWith(".digitalcombatsimulator.com")
                || host == "eagle.ru" || host.EndsWith(".eagle.ru");
        }

        // "V22.3 " -> "22.3" : on ignore le V de tête et les espaces pour comparer deux versions.
        private static string CleanVersion(string v)
        {
            v = (v ?? "").Trim();

            if (v.Length > 1 && (v[0] == 'v' || v[0] == 'V') && char.IsDigit(v[1]))
                v = v.Substring(1);

            return v;
        }

        // Vrai si le lien est une PAGE de dépôt GitHub (owner/repo, /releases, /tree/...), pas un fichier.
        // Dans ce cas on va chercher le zip de la dernière release, comme le fait l'onglet Update.
        private static bool IsGithubRepoUrl(string url)
        {
            Uri uri;

            if (!IsWebUrl(url) || !Uri.TryCreate(url, UriKind.Absolute, out uri))
                return false;

            if (!uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase)
                && !uri.Host.Equals("www.github.com", StringComparison.OrdinalIgnoreCase))
                return false;

            string[] seg = uri.AbsolutePath.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);

            if (seg.Length < 2)
                return false;

            if (seg.Length == 2)
                return true;

            // /archive/... et /releases/download/... sont de vrais fichiers
            if (seg[2].Equals("archive", StringComparison.OrdinalIgnoreCase))
                return false;

            if (seg[2].Equals("releases", StringComparison.OrdinalIgnoreCase) && seg.Length > 3
                && seg[3].Equals("download", StringComparison.OrdinalIgnoreCase))
                return false;

            return true;
        }

        // Nom de fichier utilisable sur le disque pour le zip téléchargé.
        private static string SafeZipName(string url, string fallbackId)
        {
            string name = "";

            try
            {
                name = Path.GetFileName(Uri.UnescapeDataString(new Uri(url).AbsolutePath));
            }
            catch { }

            if (string.IsNullOrWhiteSpace(name) || !name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                name = (string.IsNullOrWhiteSpace(fallbackId) ? "campaign" : fallbackId) + ".zip";

            foreach (char bad in Path.GetInvalidFileNameChars())
                name = name.Replace(bad, '_');

            return name;
        }

        // Lit le zip et renvoie le nom du dossier de campagne qu'il contient
        // (DCS_SavedGames_Path/Mods/tech/DCE/Missions/Campaigns/<NOM>/Init/camp_init.lua).
        // Renvoie null si ce n'est pas un zip, ou pas un paquet de campagne DCE.
        // Pourquoi : le nom affiché dans le catalogue peut différer du dossier réel dans le zip,
        // et ExtractCampaignZip a besoin du vrai nom pour renommer le dossier.
        private static string ReadCampaignFolderFromZip(string zipFile)
        {
            try
            {
                // Signature d'un zip : "PK". Une page HTML d'erreur ou de connexion ne l'a pas.
                using (FileStream fs = new FileStream(zipFile, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    if (fs.ReadByte() != 'P' || fs.ReadByte() != 'K')
                        return null;
                }

                using (ZipArchive archive = ZipFile.OpenRead(zipFile))
                {
                    foreach (ZipArchiveEntry entry in archive.Entries)
                    {
                        Match m = Regex.Match(entry.FullName.Replace('\\', '/'),
                            @"^DCS_SavedGames_Path/Mods/tech/DCE/Missions/Campaigns/([^/]+)/Init/camp_init\.lua$",
                            RegexOptions.IgnoreCase);

                        if (m.Success)
                            return m.Groups[1].Value;
                    }
                }
            }
            catch (Exception ex)
            {
                FormUtils.LogRegister("ReadCampaignFolderFromZip : " + ex.Message);
            }

            return null;
        }

        // Si le téléchargement automatique n'a pas marché : on propose la page de la campagne.
        private static void OfferDetailPage(CatalogCampaign c, string reason)
        {
            if (IsWebUrl(c.UrlDetail))
            {
                DialogResult answer = MessageBox.Show(
                    reason + "\r\n\r\nOpen the campaign page to download it manually?",
                    "Download - " + c.Name,
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning);

                if (answer == DialogResult.Yes)
                    OpenUrl(c.UrlDetail);
            }
            else
            {
                MessageBox.Show(reason, "Download - " + c.Name, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        // Télécharge le zip de la campagne puis l'installe avec les mêmes méthodes que l'onglet Update
        // (DownloadCampaign + ExtractCampaignZip) : nouveau dossier "Nom Version", la progression
        // (Active) des campagnes déjà installées n'est jamais touchée.
        private async Task DownloadAndInstallAsync(CatalogCampaign c, List<Button> downloadButtons, Button cancel, ProgressBar bar, Label status)
        {
            if (_downloading)
            {
                MessageBox.Show("A download is already in progress.", "Download", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            if (string.IsNullOrEmpty(ParamConf.PATH_SavedGames_DCS))
            {
                MessageBox.Show("The Saved Games folder is not set. Please set it in Options first.", "Download", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            _downloading = true;
            _userCancelled = false;

            foreach (Button b in downloadButtons)
                b.Enabled = false;

            cancel.Visible = true;
            bar.Visible = true;
            status.Visible = true;
            status.Text = "Connecting...";

            string installedFolder = null;

            try
            {
                CampaignInfo info = new CampaignInfo();
                info.Name = c.Name;
                info.LatestVersion = string.IsNullOrWhiteSpace(c.Version) ? "1.0" : c.Version.Trim();
                info.DownloadUrl = c.UrlDownload;
                info.AssetName = SafeZipName(c.UrlDownload, c.Id);

                // Lien vers un dépôt GitHub : on prend le zip de la DERNIÈRE release (comme l'onglet Update).
                if (IsGithubRepoUrl(c.UrlDownload))
                {
                    status.Text = "Looking for the latest release on GitHub...";

                    string version = "", asset = "", assetUrl = "";

                    GithubHelper github = new GithubHelper();

                    bool ok = await github.GetLatestReleaseFromUrl(c.UrlDownload, ".zip", "",
                        (v, a, u) => { version = v; asset = a; assetUrl = u; });

                    if (!ok || string.IsNullOrWhiteSpace(assetUrl))
                    {
                        OfferDetailPage(c, "No downloadable .zip was found in the latest GitHub release.");
                        return;
                    }

                    info.LatestVersion = string.IsNullOrWhiteSpace(version) ? info.LatestVersion : version;
                    info.AssetName = string.IsNullOrWhiteSpace(asset) ? info.AssetName : asset;
                    info.DownloadUrl = assetUrl;
                }

                Directory.CreateDirectory(Updater_Param.PathDownloadCampaigns);

                // Ces deux labels sont demandés par DownloadCampaign mais ne sont pas affichés ici.
                Label unusedPct = new Label();
                Label unusedTitle = new Label();

                string zipFile = "";

                try
                {
                    zipFile = await form.campaignUpdater.DownloadCampaign(info, bar, status, unusedPct, unusedTitle);
                }
                catch (Exception ex)
                {
                    FormUtils.LogRegister("DownloadAndInstallAsync : téléchargement impossible : " + ex.Message);
                    OfferDetailPage(c, "The automatic download failed:\r\n" + ex.Message);
                    return;
                }

                if (string.IsNullOrEmpty(zipFile))
                {
                    // DownloadCampaign renvoie "" aussi pour un timeout : on distingue par le bouton Cancel.
                    if (!_userCancelled)
                        OfferDetailPage(c, "The automatic download failed (timeout or interrupted).");

                    return;
                }

                // Le fichier reçu est-il bien un paquet de campagne ?
                string folderName = ReadCampaignFolderFromZip(zipFile);

                if (folderName == null)
                {
                    try { File.Delete(zipFile); } catch { }

                    OfferDetailPage(c, "The downloaded file is not a valid DCE campaign package (no DCS_SavedGames_Path\\...\\Init\\camp_init.lua inside).");
                    return;
                }

                info.Name = folderName;

                string newFolderName = folderName + " " + info.LatestVersion;
                string newFolderPath = Path.Combine(ParamConf.PATH_SavedGames_DCS, "Mods", "tech", "DCE", "Missions", "Campaigns", newFolderName);

                if (Directory.Exists(newFolderPath))
                {
                    DialogResult overwrite = MessageBox.Show(
                        "'" + newFolderName + "' is already installed.\r\n\r\nInstall it again over the existing files? (campaign progress in the Active folder is not touched)",
                        "Download - " + c.Name,
                        MessageBoxButtons.YesNo,
                        MessageBoxIcon.Question,
                        MessageBoxDefaultButton.Button2);

                    if (overwrite != DialogResult.Yes)
                        return;
                }

                status.Text = "Installing...";

                try
                {
                    form.campaignUpdater.ExtractCampaignZip(zipFile, ParamConf.PATH_SavedGames_DCS, info);
                }
                catch (Exception ex)
                {
                    FormUtils.LogRegister("DownloadAndInstallAsync : installation impossible : " + ex.Message);
                    OfferDetailPage(c, "The installation failed:\r\n" + ex.Message);
                    return;
                }

                installedFolder = newFolderName;
            }
            catch (Exception ex)
            {
                FormUtils.LogRegister("DownloadAndInstallAsync : " + ex);
                OfferDetailPage(c, "Unexpected error:\r\n" + ex.Message);
            }
            finally
            {
                _downloading = false;

                // La carte a pu être reconstruite entre-temps (DisplayCatalog) : on ne touche que ce qui existe encore.
                foreach (Button b in downloadButtons)
                    if (!b.IsDisposed) b.Enabled = true;

                if (!cancel.IsDisposed) cancel.Visible = false;
                if (!bar.IsDisposed) bar.Visible = false;
                if (!status.IsDisposed) status.Visible = false;
            }

            if (installedFolder != null)
            {
                // Met à jour l'onglet Update, la liste des campagnes, puis les badges de ce catalogue.
                form.campaignUpdater.RefreshCampaignUpdatesLocalOnly(form.CampaignDataGridView, ParamConf.PATH_SavedGames_DCS);

                await form.CampaignGridLeft.LoadCampaignsAsync(selectCampaignName: installedFolder);

                DisplayCatalog();

                MessageBox.Show("'" + installedFolder + "' installed.", "Download - " + c.Name, MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }


        // ============================================================================
        // Liens
        // ============================================================================

        // Le catalogue vient d'internet : on n'ouvre que du http(s), jamais autre chose.
        private static bool IsWebUrl(string url)
        {
            return !string.IsNullOrWhiteSpace(url)
                && (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                 || url.StartsWith("https://", StringComparison.OrdinalIgnoreCase));
        }

        private static void OpenUrl(string url)
        {
            if (!IsWebUrl(url))
                return;

            try
            {
                Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
            }
            catch (Exception ex)
            {
                FormUtils.LogRegister("OpenUrl : " + ex.Message);
            }
        }
    }
}
