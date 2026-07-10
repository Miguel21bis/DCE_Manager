using System;
using System.Collections.Generic;
using System.Linq;
using System.Management;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using DCE_Manager.Parameters;
using DCE_Manager.Utils;

namespace DCE_Manager
{
    internal static class Statistic
    {


        public static async Task EnvoiStatsAsync(bool statsEnabled)
        {


            FormUtils.LogRegister("Statistics : Starting statistics processing.");

            if (!statsEnabled)
            {
                FormUtils.LogRegister("Statistics : Disabled by user (checkBox_Stat_anonym unchecked).");
                return;
            }

            if (!statsEnabled)
                return;


            try
            {
                await EnvoiStats();
            }
            catch (Exception ex)
            {
                FormUtils.ErrorGeneral_BoxOrLog(ex, "Erreur lors du step one", "", true, true);
            }
        }



        public static bool IsUrlExist(string url, int timeOutMs = 1)
        {
            WebRequest webRequest = WebRequest.Create(url);
            webRequest.Method = "HEAD";
            webRequest.Timeout = timeOutMs;

            try
            {
                var response = webRequest.GetResponse();
                /* response is `200 OK` */
                response.Close();
            }
            catch
            {
                /* Any other response */
                FormUtils.LogRegister("LogRegister 63 No response : " + url);
                return false;
            }

            return true;
        }

        public static string CreateIdClient()
        {
            // Récupérer les informations matérielles
            string processorId = GetProcessorId();
            string diskSerial = GetDiskSerial();
            string motherboardSerial = GetMotherboardSerial();

            // Combiner les informations en une chaîne
            string combinedInfo = $"{processorId}-{diskSerial}-{motherboardSerial}";

            // Générer un identifiant unique avec SHA-256
            string clientId = GenerateSHA256Hash(combinedInfo);
            return clientId;
        }

        static string GetProcessorId()
        {
            string processorId = string.Empty;
            ManagementObjectSearcher searcher = new ManagementObjectSearcher("select ProcessorId from Win32_Processor");

            foreach (ManagementObject obj in searcher.Get())
            {
                processorId = obj["ProcessorId"]?.ToString() ?? "Unknown";
            }

            return processorId;
        }

        static string GetDiskSerial()
        {
            string diskSerial = string.Empty;
            ManagementObjectSearcher searcher = new ManagementObjectSearcher("select SerialNumber from Win32_DiskDrive");

            foreach (ManagementObject obj in searcher.Get())
            {
                diskSerial = obj["SerialNumber"]?.ToString().Trim() ?? "Unknown";
                break; // Utiliser seulement le premier disque pour cet exemple
            }

            return diskSerial;
        }

        static string GetMotherboardSerial()
        {
            string motherboardSerial = string.Empty;
            ManagementObjectSearcher searcher = new ManagementObjectSearcher("select SerialNumber from Win32_BaseBoard");

            foreach (ManagementObject obj in searcher.Get())
            {
                motherboardSerial = obj["SerialNumber"]?.ToString().Trim() ?? "Unknown";
            }

            return motherboardSerial;
        }

        static string GenerateSHA256Hash(string rawData)
        {
            using (SHA256 sha256Hash = SHA256.Create())
            {
                byte[] bytes = sha256Hash.ComputeHash(Encoding.UTF8.GetBytes(rawData));

                StringBuilder builder = new StringBuilder();
                foreach (byte b in bytes)
                {
                    builder.Append(b.ToString("x2"));
                }
                return builder.ToString();
            }
        }

        static async Task EnvoiStats()
        {
            FormUtils.LogRegister("Statistics : Preparing statistics.");

            // Vérifier si la clé "LastStats" existe, sinon la définir avec une valeur par défaut
            if (!ParamConf.configDictionary.TryGetValue("LastStats", out string lastStatsValue) || string.IsNullOrEmpty(lastStatsValue))
            {
                lastStatsValue = DateTime.MinValue.ToString(); // Valeur par défaut si la clé est absente
                ParamConf.configDictionary["LastStats"] = lastStatsValue;
            }


            // Vérifiez si une requête a déjà été envoyée aujourd'hui
            DateTime lastSentDate;
            if (DateTime.TryParse(lastStatsValue, out lastSentDate))
            {
                if (lastSentDate.Date == DateTime.Today)
                {
                    FormUtils.LogRegister("Statistics : Already sent today.");
                    return; // Déjà envoyé aujourd'hui
                }
            }

            // Continuer avec l'envoi des statistiques
            var data = new
            {
                id_client = CreateIdClient(),
                usage_count = ParamManager.NbLancement,
                token = appsettings.token
            };

            FormUtils.LogRegister("Statistics : Client ID created.");


            using (var client = new HttpClient())
            {
                FormUtils.LogRegister("Statistics : Sending statistics to server...");

                try
                {
                    var content = new FormUrlEncodedContent(new[]
                    {
                        new KeyValuePair<string, string>("id_client", data.id_client),
                        new KeyValuePair<string, string>("usage_count", data.usage_count.ToString()),
                        new KeyValuePair<string, string>("verDceManager", ParamManager.verDceManager),
                        new KeyValuePair<string, string>("verScriptsMod", ParamScriptsMod.verScriptsMod),
                        new KeyValuePair<string, string>("token", data.token)
                    });

                    HttpResponseMessage response = await client.PostAsync(appsettings.url, content);

                    if (response.IsSuccessStatusCode)
                    {
                        ParamConf.configDictionary["LastStats"] = DateTime.Now.ToString();
                        ParamManager.NbLancement = 0;
                        FormUtils.LogRegister("Statistics : Successfully sent.");
                    }
                    else
                    {
                        //MessageBox.Show($"Erreur: {response.StatusCode}", "Erreur step one");
                        FormUtils.LogRegister("Statistics : Server returned " + response.StatusCode);
                    }

                }
                catch (Exception ex)
                {
                    FormUtils.LogRegister("Statistics : Exception while sending.");
                    FormUtils.ErrorGeneral_BoxOrLog(ex, "Erreur lors du step one", "", false, true);
                }
            }
        }


    }
}
