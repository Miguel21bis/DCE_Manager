using System;
using System.Collections.Generic;
using System.Linq;
//using System.Linq;
//using System.Text;
//using System.Threading.Tasks;

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

    public static class ParamCampaign
    {
        public static string NameCampaign { get; set; } = "";
        public static string PathCampaign { get; set; } = "";
        public static int NbMission { get; set; } = 0;
        public static string NameFileParse { get; set; } = "";
    }
    public static class ParamCampaignSelected
    {
        public static string NameCampaign { get; set; } = "";
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

        public static string CurrentConfigName { get; set; } = string.Empty;

        public static string AstiMissionFile { get; set; } = "";
        public static string AstiImportTemplateFolder { get; set; } = "";

        public static string PATH_DCS_Root { get; set; } = "";
        public static string PATH_SavedGames_DCS { get; set; } = "";
        public static string PATH_OVGME_MOD { get; set; } = "";

        public static bool test_DCS_Root = false;
        public static bool test_DCS_SavedGames = false;
        public static bool test_OVGME = false;
        public static bool test_DCE_alreadyInstalled = false;

        public static bool DownloadFinish { get; set; } = false;
        public static string UpgradeTime { get; set; } = "";
        public static int NbFileOutToDate { get; set; } = 0;

        //public static class DceNews
        public static string LastNewsVersion = "0.0.0";
        

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

    public static class ParamGithub
    {
        // Dernière version détectée sur GitHub.
        // Pourquoi : éviter plusieurs appels API inutiles.
        public static string LastVersion = "";

        // URL de téléchargement de l'asset ScriptsMod.
        // Pourquoi : réutilisée lors du clic sur Update.
        public static string DownloadUrl = "";

        // Nom du fichier ZIP.
        // Pourquoi : utilisé lors du téléchargement.
        public static string AssetName = "";

        //public static string Repository = "DCE";
    }

    public class CampaignSquad
    {
        // Identifiant unique du squad dans la campagne.
        // Pourquoi : un même nom peut exister côté blue et red.
        public string Key { get; set; }

        public string SideString { get; set; }

        // Données provenant de Init\oob_air_init.lua
        public Squad Init { get; set; }

        // Données provenant de Active\oob_air.lua
        public Squad Active { get; set; }

    }

    public static class CampaignSquadTools
    {
        // Copie les informations utiles du Active vers Init.
        // Pourquoi : permettre plus tard "sauver la campagne courante comme nouveau départ".
        public static void CopyActiveToInit(CampaignSquad campaignSquad)
        {
            if (campaignSquad == null ||
                campaignSquad.Active == null ||
                campaignSquad.Init == null)
            {
                return;
            }

            if (campaignSquad.Active.Roster != null)
            {
                if (campaignSquad.Active.Roster.ContainsKey("ready"))
                {
                    campaignSquad.Init.Number =
                        Convert.ToInt32(campaignSquad.Active.Roster["ready"]);
                }

                if (campaignSquad.Active.Roster.ContainsKey("reserve"))
                {
                    campaignSquad.Init.Reserve =
                        Convert.ToInt32(campaignSquad.Active.Roster["reserve"]);
                }
            }

            campaignSquad.Init.Base = campaignSquad.Active.Base;
            campaignSquad.Init.Type = campaignSquad.Active.Type;
            campaignSquad.Init.Player = campaignSquad.Active.Player;
            campaignSquad.Init.Squad_Inactive = campaignSquad.Active.Squad_Inactive;
        }
    }

    public class Squad
    {
        // Retourne une copie superficielle du squad
        // Pourquoi : simplifier le clonage sans recopier chaque propriété
        public Squad Clone()
        {
            return (Squad)this.MemberwiseClone();
        }

        // Génère automatiquement un nouveau nom et un nouvel IdSquad
        // Pourquoi : éviter conflits lors du clonage
        public void GenerateCloneIdentity(List<Squad> existingSquads)
        {
            // -------------------------------------------------
            // Nouveau nom
            // -------------------------------------------------

            string baseName = Name;

            int index = 1;

            string newName;

            do
            {
                newName = baseName + "-" + index;
                index++;
            }
            while (existingSquads.Any(s =>
                s.Name.Equals(newName, StringComparison.OrdinalIgnoreCase)));

            Name = newName;
            DisplayName = newName;

            // -------------------------------------------------
            // Nouvel ID
            // -------------------------------------------------

            int maxId = existingSquads.Count > 0
                ? existingSquads.Max(s => s.IdSquad)
                : 0;

            IdSquad = maxId + 1;
        }

        public string SideString { get; set; }
        public string FolderFile { get; set; }
        public int IdSquad { get; set; }
        public string Name { get; set; }
        public bool Squad_Inactive { get; set; }
        public bool Player { get; set; }
        public bool HumainOnly { get; set; }
        
        public string Type { get; set; }
        public string Country { get; set; }
        public string Base { get; set; }
        public List<string> BaseAlternative { get; set; }
        public string Skill { get; set; }
        public int InitNumber { get; set; }
        public int InitReserve { get; set; }
        public int Number { get; set; }
        public int Reserve { get; set; }
        public Dictionary<string, object> Roster { get; set; }
        public Dictionary<string, object> Score { get; set; }
        public Dictionary<string, object> parking_id { get; set; }

        //public object Livery { get; set; }
        public Dictionary<int, string> Livery { get; set; }
        public Dictionary<int, string> LiveryModex { get; set; }
        public Dictionary<string, bool> Tasks { get; set; }
        public Dictionary<string, double> TasksCoef { get; set; }

        public string Side { get; set; }          // "blue"
        public string Callsign { get; set; }      // "Uzi"
        // ID Lua réel du callsign
        // Pourquoi : DCE sauvegarde un entier et non le texte affiché
        public int CallsignId { get; set; }
        public Dictionary<string, object> ScoreLast { get; set; }
        public Dictionary<string, int> TasksCoefPourcent { get; set; }

        //public List<int> SideNumber { get; set; }
        // Index 0 = min
        // Index 1 = max
        public int SideNumberMin { get; set; }
        public int SideNumberMax { get; set; }

        // Nom affiché dans l'UI (différent du Name brut Lua)
        // Pourquoi : gérer les doublons sans casser le matching
        public string DisplayName { get; set; }

        //public bool Squad_Active => !Squad_Inactive;
        public bool Squad_Active
        {
            get { return !Squad_Inactive; }
            set { Squad_Inactive = !value; }
        }

        // Valeur affichée dans la grille colonne "Ready"
        // Pourquoi : en Active on veut afficher roster.ready dynamiquement
        public int DisplayReady
        {
            get
            {
                if (Roster == null)
                    return Number;

                if (Roster.TryGetValue("ready", out object value))
                {
                    if (value is int intValue)
                        return intValue;

                    if (int.TryParse(value.ToString(), out int parsed))
                        return parsed;
                }

                return Number;
            }
        }

        // Dictionnaire pour les propriétés supplémentaires
        public Dictionary<string, object> AdditionalProperties { get; set; }

        public Squad()
        {
            AdditionalProperties = new Dictionary<string, object>();
        }
    }

    public class AirbaseInfo
    {
        public string Name { get; set; }
        public string AliasName { get; set; }
        public string Side { get; set; }
        public int Elevation { get; set; }
        public int AirdromeId { get; set; }
        public bool Divert { get; set; }
        public bool Inactive { get; set; }
        public double X { get; set; }
        public double Y { get; set; }

        public List<RunwayInfo> Runways { get; set; }
        public List<ParkSpot> ParkAlertSAR { get; set; }

        public Dictionary<string, string> Code { get; set; }
        public List<double> ATCFrequencies { get; set; }

        public AirbaseInfo()
        {
            Runways = new List<RunwayInfo>();
            ParkAlertSAR = new List<ParkSpot>();
            Code = new Dictionary<string, string>();
            ATCFrequencies = new List<double>();
        }
    }


    public class RunwayInfo
    {
        public string Name { get; set; }
        public double Hdg { get; set; }
        public bool TrueHdg { get; set; }
        public int Length { get; set; }
        public double X { get; set; }
        public double Y { get; set; }
    }

    public class ParkSpot
    {
        public double X { get; set; }
        public double Y { get; set; }
        public bool ReservedAR { get; set; }
        public bool ReservedSAR { get; set; }
        public bool Occupied { get; set; }
    }


    public class CampaignLuaData
    {
        public static CampaignLuaData Current { get; set; }

        public HashSet<string> PlayableAircraft { get; set; }
        public HashSet<string> AllPlaneHeli { get; set; }
        public HashSet<string> TabSquad { get; set; } = new HashSet<string>();

        public Dictionary<string, List<string>> CallsignWest { get; set; }

        //public Dictionary<string, Dictionary<string, Dictionary<int, string>>> SpecificCallnames { get; set; }
        public Dictionary<string, Dictionary<string, Dictionary<string, string>>> SpecificCallnames { get; set; }

        public Dictionary<string, List<string>> TaskByPlane { get; set; }

        public List<string> Country { get; set; } = new List<string>();
    }

    public static class List_oob_air_Manager
    {
        // Liste publique et statique de squads
        public static List<Squad> List_oob_air { get; set; } = new List<Squad>();

        // Nouvelle liste logique regroupant Init + Active.
        // Pourquoi : manipuler un seul squad par nom.
        public static List<CampaignSquad> List_campaignSquads = new List<CampaignSquad>();

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

}
