using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;


namespace DCE_Manager.Update
{
    // Informations extraites d'un dépôt.
    // Pourquoi : séparer les données distantes des données locales.
    public class CampaignRepository
    {
        public string RepositoryUrl { get; set; } = "";

        public string LatestVersion { get; set; } = "";

        public string AssetName { get; set; } = "";

        public string DownloadUrl { get; set; } = "";

        public bool ValidRepository { get; set; } = false;
    }
}