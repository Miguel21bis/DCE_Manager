using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows.Forms;
using DCE_Manager.Parameters;
using DCE_Manager.Utils;
using Newtonsoft.Json;

namespace DCE_Manager.Update
{
    // Une entrée du catalogue de news (une par annonce : nouvelle campagne, mise à jour, etc.)
    public class NewsEntry
    {
        public string Id { get; set; }
        public string Date { get; set; }
        public string Title { get; set; }
        public string Message { get; set; }
        public string Type { get; set; } // "new", "update", "fix"
    }

    public class Updater_News
    {
        private readonly Main_Form form;

        private List<NewsEntry> lastFetchedEntries = new List<NewsEntry>();

        // URL du catalogue JSON brut, hébergé dans le dépôt GitHub de DCE_Manager (pas l'API -> pas de quota).
        // Si le repo utilise "master" au lieu de "main", change juste ce mot ici.
        private static string CatalogUrl =>
            $"https://raw.githubusercontent.com/{GithubHelper.GithubAccount}/{GithubHelper.Repository_Manager}/main/News/campaigns_catalog.json";

        public Updater_News(Main_Form form)
        {
            this.form = form;
        }


        // Télécharge le catalogue de news et met à jour le badge de l'onglet si des news n'ont pas encore été vues.
        // Pourquoi : prévenir l'utilisateur dès le lancement, sans qu'il ait besoin d'ouvrir l'onglet News.
        public async Task CheckNewsAsync()
        {
            try
            {
                using (HttpClient client = new HttpClient())
                {
                    client.DefaultRequestHeaders.Add("User-Agent", "DCE_Manager");

                    string json = await client.GetStringAsync(CatalogUrl);

                    lastFetchedEntries =
                        JsonConvert.DeserializeObject<List<NewsEntry>>(json)
                        ?? new List<NewsEntry>();
                }
            }
            catch (Exception ex)
            {
                FormUtils.LogRegister(
                    "CheckNewsAsync : impossible de récupérer le catalogue de news : " + ex.Message);

                return; // pas de connexion / repo indisponible : on ne bloque jamais l'utilisateur pour ça
            }

            int unseenCount = GetUnseenEntries().Count;

            form.tabPageLeftNews.Text =
                unseenCount > 0
                    ? $"News ({unseenCount})"
                    : "News";
        }


        // Retourne les entrées plus récentes que la dernière news vue par l'utilisateur.
        private List<NewsEntry> GetUnseenEntries()
        {
            if (lastFetchedEntries.Count == 0)
                return new List<NewsEntry>();

            string lastSeenId = ParamConf.LastNewsVersion;

            if (string.IsNullOrWhiteSpace(lastSeenId))
                return lastFetchedEntries; // jamais rien vu -> tout est nouveau

            int index = lastFetchedEntries.FindIndex(n => n.Id == lastSeenId);

            // Id introuvable (catalogue remanié entre-temps) : on affiche tout par prudence plutôt que de risquer de rater une news.
            return index < 0
                ? lastFetchedEntries
                : lastFetchedEntries.Take(index).ToList();
        }


        // Construit l'affichage de l'onglet News et marque les news actuelles comme vues.
        // Pourquoi : appelé quand l'utilisateur ouvre réellement l'onglet -> le badge ne doit disparaître qu'à ce moment-là.
        public void DisplayNews()
        {
            form.tabPageLeftNews.Controls.Clear();

            Panel newsPanel = new Panel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                Padding = new Padding(10)
            };

            form.tabPageLeftNews.Controls.Add(newsPanel);

            if (lastFetchedEntries.Count == 0)
            {
                newsPanel.Controls.Add(new Label
                {
                    Text = "No news available (offline or catalog unreachable).",
                    AutoSize = true
                });

                return;
            }

            int y = 0;
            int contentWidth = Math.Max(newsPanel.ClientSize.Width - 20, 200);

            foreach (NewsEntry entry in lastFetchedEntries)
            {
                int rowStartY = y;

                // Icône (simple caractère, pas de fichier image à gérer)
                Label iconLabel = new Label
                {
                    Text = GetIcon(entry.Type),
                    Font = new Font(newsPanel.Font.FontFamily, 14f),
                    AutoSize = true,
                    Location = new Point(0, rowStartY)
                };
                newsPanel.Controls.Add(iconLabel);

                int textX = iconLabel.Width + 10;

                // Titre
                Label titleLabel = new Label
                {
                    Text = entry.Title,
                    Font = new Font(newsPanel.Font, FontStyle.Bold),
                    AutoSize = true,
                    Location = new Point(textX, rowStartY)
                };
                newsPanel.Controls.Add(titleLabel);

                // Badge (type) juste après le titre
                (string badgeText, Color badgeColor) = GetBadge(entry.Type);

                Label badgeLabel = new Label
                {
                    Text = badgeText,
                    Font = new Font(newsPanel.Font, FontStyle.Bold),
                    ForeColor = Color.White,
                    BackColor = badgeColor,
                    AutoSize = true,
                    Padding = new Padding(6, 2, 6, 2),
                    Location = new Point(textX + titleLabel.Width + 8, rowStartY)
                };
                newsPanel.Controls.Add(badgeLabel);

                // Date, alignée à droite
                Label dateLabel = new Label
                {
                    Text = entry.Date,
                    ForeColor = Color.Gray,
                    AutoSize = true
                };
                newsPanel.Controls.Add(dateLabel);
                dateLabel.Location = new Point(contentWidth - dateLabel.Width, rowStartY);

                y += Math.Max(titleLabel.Height, iconLabel.Height) + 2;

                // Description
                Label messageLabel = new Label
                {
                    Text = entry.Message,
                    MaximumSize = new Size(contentWidth - textX, 0),
                    AutoSize = true,
                    Location = new Point(textX, y)
                };
                newsPanel.Controls.Add(messageLabel);
                y += messageLabel.Height + 20;
            }

            // Marque toutes les news actuelles comme vues : le badge disparaît (sauvegardé au prochain FormClosed).
            ParamConf.LastNewsVersion = lastFetchedEntries[0].Id;
            ParamConf.configDictionary["LastNewsVersion"] = lastFetchedEntries[0].Id;

            form.tabPageLeftNews.Text = "News";
        }


        // Caractère affiché à gauche de chaque news, selon son type.
        private static string GetIcon(string type)
        {
            switch (type)
            {
                case "new": return "🏳";
                case "fix": return "🛠";
                case "update":
                default: return "🚀";
            }
        }


        // Texte + couleur du badge affiché à côté du titre, selon le type.
        private static (string text, Color color) GetBadge(string type)
        {
            switch (type)
            {
                case "new": return ("NEW", Color.SeaGreen);
                case "fix": return ("FIX", Color.Firebrick);
                case "update":
                default: return ("UPDATE", Color.SteelBlue);
            }
        }
    }
}
