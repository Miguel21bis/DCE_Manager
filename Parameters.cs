using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DCE_Manager.Parameters
{

    public static class Side
    {
        public static string[] TabSide =
            new[] { "neutral", "blue", "red", "neutral", };
    }
    public static class TestFile
    {
        public static bool structureValide = false;
        public static bool presenceMisScript = false;
        public static bool presenceScriptMod = false;
        public static bool presenceOobAirInit = false;
        public static bool presenceCampInit = false;

        public static string ScriptsMod = "NG";
    }
    public static class TestPath
    {
        public static bool DCS_Root = false;
        public static bool DCS_SavedGames = false;
        public static bool OVGME = false;
        public static bool DCE_alreadyInstalled = false;
    }
    //public static class ParamCampaign
    //{
    //    public static string NameCampaign = "";
    //    public static string pathCampaign = "";
    //    public static int NbMission = 0;
    //    public static string NameFileParse = "";
    //}
    public static class ParamCampaign
    {
        public static string NameCampaign { get; set; } = "";
        public static string PathCampaign { get; set; } = "";
        public static int NbMission { get; set; } = 0;
        public static string NameFileParse { get; set; } = "";
    }
    public static class ParamDownload
    {
        public static bool DownloadFinish { get; set; } = false;
        public static string UpgradeTime { get; set; } = "";
        public static int NbFileOutToDate { get; set; } = 0;
    }
    //public static class ParamDownload
    //{
    //    public static bool DownloadFinish = false;
    //    public static string upgradeTime;
    //    public static int nbFileOutToDate = 0;
    //}
    public static class DceNews
    {
        public static string LastNewsVersion = "0.0.0";
    }
    public static class Divers
    {
        public static string ScriptsMod = "inc";

    }

    public static class ParamConf
    {
        // Dictionnaire statique pour stocker la configuration
        public static Dictionary<string, string> configDictionary { get; set; } = new Dictionary<string, string>();

        public static Dictionary<string, int> configMap = new Dictionary<string, int>();

        public static int NumSelectConfig = 0;

        public static int NumMaxConfig = 0;
    }


    public static class ParamServ
    {
        public static int tmpResponse = 1000;


        //public static string FileServerName01 = "http://miguel21.byethost3.com/";
        //public static string FileServDgUpgradeTXT01 = "http://miguel21.byethost3.com/upgrade.txt";
        //public static string ServerNickName01 = "Server 1";//"000webhostapp"
        //public static string fileTypeServer01 = "ftp";


        public static string FileServerName01 = "http://dce-manager.alwaysdata.net/";
        public static string FileServDgUpgradeTXT01 = "http://dce-manager.alwaysdata.net//upgrade.txt";
        public static string ServerNickName01 = "Server 1";//"000webhostapp"
        public static string fileTypeServer01 = "ftp";
        public static Boolean Server01Exit = true;



        public static string FileServerName02 = "https://drive.google.com/uc?export=download&id=";
        //public static string FileServDgUpgradeTXT02 = "1kfO_8LCU7Zvu2tNAZ2WKlUEL2x6t0wFK";
        public static string FileServDgUpgradeTXT02 = "https://bit.ly/3dDSSq1";
        public static string FileServDceManager02 = "https://bit.ly/3au28Lq";
        public static string ServerNickName02 = "Server 2";//"GoogleDrive";
        public static string fileTypeServer02 = "drivegoogle";
        public static Boolean Server02Exit = true;


        public static string FileServerName03 = "http://dcemanager.free.fr/public_html/";
        public static string FileServDgUpgradeTXT03 = "http://dcemanager.free.fr/public_html/upgrade.txt";
        public static string ServerNickName03 = "Server 3";//"free.fr";
        public static string fileTypeServer03 = "ftp";
        public static Boolean Server03Exit = true;


        //public static string ServerSelected = FileServerName02;
        //public static string fileTypeServer = fileTypeServer02;
        //public static string FileServDgUpgradeTXT = FileServDgUpgradeTXT02;

        public static string ServerSelected = "";
        public static string ServerNickNameSelected = "";
        public static string fileTypeServer = "";

        public static string FileServDgUpgradeTXT = "";
    }

    public static class ParamManager
    {
        public static string pathManager = System.Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments) + @"\DCE_Manager\";
        public static int NbLancement { get; set; } = 0;
        public static string verDceManager = "0.0.0";
        public static int userLevel { get; set; } = 1;
        //user = 1
        //expert = 2
        //campaignMaker = 3
        //DEV = 5

    }
    public static class ParamDivers
    {
        public static Boolean NewParseOobAir = true;
    }
    public static class ParamScriptsMod
    {
        public static string verScriptsMod = "0.0.0";
    }
    public class Squad
    {

        public string SideString { get; set; }
        public string FolderFile { get; set; }
        public int IdSquad { get; set; }
        public string Name { get; set; }
        public bool Inactive { get; set; }
        public bool Player { get; set; }
        public string Type { get; set; }
        public string Country { get; set; }
        public Dictionary<string, object> Livery { get; set; }
        public string Base { get; set; }
        public List<string> BaseAlternative { get; set; }
        public string Skill { get; set; }
        public Dictionary<string, object> Tasks { get; set; }
        public Dictionary<string, object> TasksCoef { get; set; }
        public int InitNumber { get; set; }
        public int InitReserve { get; set; }
        public int Number { get; set; }
        public int Reserve { get; set; }
        public Dictionary<string, object> Roster { get; set; }
        public Dictionary<string, object> Score { get; set; }


        // Dictionnaire pour les propriétés supplémentaires
        public Dictionary<string, object> AdditionalProperties { get; set; }

        public Squad()
        {
            AdditionalProperties = new Dictionary<string, object>();
        }
    }

    public static class List_oob_air_Manager
    {
        // Liste publique et statique de squads
        public static List<Squad> List_oob_air { get; set; } = new List<Squad>();
    }

    public static class PublicTable
    {
        //(3)camp//nbSquad(100)//nbEnregist(80)//variable(3)

        public static string[,,,] tableOobAiriNIT = new string[3, 100, 100, 4];
        public static string[,,,] tableOobAir = new string[3, 100, 100, 4];

        // Déclaration et initialisation de Airdrome
        public static Dictionary<string, Dictionary<string, List<string>>> Airdrome = new Dictionary<string, Dictionary<string, List<string>>>()
        {
            { "Init", new Dictionary<string, List<string>> { { "blue", new List<string>() }, { "red", new List<string>() } } },
            { "Active", new Dictionary<string, List<string>> { { "blue", new List<string>() }, { "red", new List<string>() } } }
        };

        public static readonly Dictionary<string, int> sideToInt = new Dictionary<string, int>
        {
                { "blue", 1 },
                { "red", 2 },
                { "neutral", 3 }
        };
        public static readonly Dictionary<int, string> sideToString = new Dictionary<int, string>
        {
                { 1, "blue" },
                { 2, "red" },
                { 3, "neutral" }
        };
        public static Dictionary<string, string> errorTable = new Dictionary<string, string>()
        {
        };

        public static List<string> SideList = new List<string>()
        {
            "blue",
            "red",
            "neutral"
        };
    }


    public static class CloneCampaign
    {
        public static string OldNameCamp = "";
        public static string path = "";
        public static string SquadName = "";
    }


    public static class SharedData
    {
        //il faut, pour faire ceci pour les mettre à jour avant de les utiliser
        //// Appel à UpdateSharedData avant de récupérer les valeurs de SharedData
        //Form1.Instance.UpdateSharedData();

        public static string comboBox_Config { get; set; }
        public static string textBox_Campaign { get; set; }
        public static string textBox_DCS { get; set; }
        public static string textBox_SavedGames { get; set; }
        public static string textBox_OvGME { get; set; }

        public static string textBox_ASTI_MissionFile { get; set; }
        public static string textBox_ASTI_importTemplateFolder { get; set; }

    }


}
