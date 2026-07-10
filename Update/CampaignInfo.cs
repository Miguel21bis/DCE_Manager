using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DCE_Manager.Update
{
    // Représente une campagne installée.
    // Pourquoi : regrouper toutes les informations nécessaires aux mises à jour.
    public class CampaignInfo
    {
        public string Name { get; set; } = "";

        public string Folder { get; set; } = "";

        public string CampaignId { get; set; } = "";

        public string RepositoryUrl { get; set; } = "";

        public string LocalVersion { get; set; } = "";

        public string LatestVersion { get; set; } = "";

        public string DownloadUrl { get; set; } = "";

        public string AssetName { get; set; } = "";

        public bool UpdateAvailable { get; set; } = false;

        public List<string> InstalledVersions { get; } = new List<string>();

        public List<string> InstalledCampaigns { get; } = new List<string>();

        public bool AlreadyInstalledLatestVersion { get; set; } = false;


    }

    // Informations minimales contenues dans une archive de campagne.
    // Pourquoi : vérifier le contenu d'un ZIP avant installation.
    public class CampaignPackageInfo
    {
        public string CampaignId = "";

        public string Version = "";

        public string Title = "";

        public string RepositoryUrl = "";
    }
}
