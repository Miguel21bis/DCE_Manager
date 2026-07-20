using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DCE_Manager.Parameters
{

    public static class Updater_Param
    {
        //public static string Repository = "DCE_Manager";

        public static DateTime LastGithubCheckUtc = DateTime.MinValue;

        public static readonly TimeSpan GithubCheckInterval = TimeSpan.FromHours(24);

        public static bool ScriptsModUpdateAvailable;
        public static bool DCEManagerUpdateAvailable;

        public static int NbUpdateAvailable = 0;

        public static readonly string PathDownload = Path.Combine(ParamManager.pathManager, "Downloads");
        public static readonly string PathDownloadCampaigns = Path.Combine(PathDownload, "Campaigns");
        public static readonly string PathDownloadScriptsMod = Path.Combine(PathDownload, "ScriptsMod");
        public static readonly string PathDownloadManager = Path.Combine(PathDownload, "DCE_Manager");
        public static readonly string PathTemp = Path.Combine(ParamManager.pathManager, "Temp");
        public static readonly string PathCache = Path.Combine(ParamManager.pathManager, "Cache");

        public static bool SetupDownloaded = false;
        public static string SetupFile = "";

        public static void CreateFolders()
        {
            Directory.CreateDirectory(PathDownload);
            Directory.CreateDirectory(PathDownloadCampaigns);
            Directory.CreateDirectory(PathDownloadScriptsMod);
            Directory.CreateDirectory(PathDownloadManager);
            Directory.CreateDirectory(PathTemp);
            Directory.CreateDirectory(PathCache);
        }
    }


}
