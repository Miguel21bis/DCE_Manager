using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Policy;
using System.Threading.Tasks;

using DCE_Manager.Parameters;
using DCE_Manager.Utils;
using Newtonsoft.Json.Linq;

namespace DCE_Manager.Update
{

    public class GithubHelper
    {
        public const string GithubAccount = "Miguel21bis";
        public const string Repository_Manager = "DCE_Manager";
        public const string Repository_ScriptsMod = "DCE";

        private static readonly Dictionary<string, string> _etagCache =
            new Dictionary<string, string>();

        private static readonly Dictionary<string, string> _jsonCache =
            new Dictionary<string, string>();

        // Nouveau : état du quota GitHub, lu par l'UI pour afficher un message clair
        public bool IsRateLimited { get; private set; }
        public DateTime RateLimitResetUtc { get; private set; }

        public GithubCheckStatus LastStatus { get; private set; } = GithubCheckStatus.Success;
        public string LastErrorDetail { get; private set; } = "";

        public async Task<bool> GetLatestRelease(
            string githubOwner,
            string repository,
            string assetExtension,
            string assetFilter,
            Action<string, string, string> saveResult)
        {
            LastStatus = GithubCheckStatus.Success; // reset avant chaque tentative
            LastErrorDetail = "";

            try
            {
                using (HttpClient client = new HttpClient())
                {
                    //client.DefaultRequestHeaders.Add("User-Agent", "DCE_Manager");
                    client.DefaultRequestHeaders.Add("User-Agent", "DCE_Manager_" + Guid.NewGuid());

                    string url = $"https://api.github.com/repos/{githubOwner}/{repository}/releases/latest";

                    if (_etagCache.TryGetValue(url, out string etag))
                        client.DefaultRequestHeaders.TryAddWithoutValidation("If-None-Match", etag);

                    HttpResponseMessage response = await client.GetAsync(url);

                    FormUtils.LogRegister( $"GetLatestRelease A: GitHub HTTP {(int)response.StatusCode} {response.ReasonPhrase} {url}");

                    var remaining =
                        response.Headers.TryGetValues("X-RateLimit-Remaining", out var rem)
                            ? rem.FirstOrDefault() : "?";

                    var reset =
                        response.Headers.TryGetValues("X-RateLimit-Reset", out var rst)
                            ? rst.FirstOrDefault() : "?";

                    // ---- Détection quota dépassé ----
                    if (response.StatusCode == HttpStatusCode.Forbidden && remaining == "0")
                    {
                        LastStatus = GithubCheckStatus.RateLimited;

                        RateLimitResetUtc = long.TryParse(reset, out long resetUnix)
                            ? DateTimeOffset.FromUnixTimeSeconds(resetUnix).UtcDateTime
                            : DateTime.UtcNow.AddHours(1);

                        FormUtils.LogRegister( $"GetLatestRelease B: GitHub rate limit dépassé. Reset prévu à {RateLimitResetUtc:u} UTC");

                        return false;
                    }

                    // ---- Repo/release introuvable ----
                    if (response.StatusCode == HttpStatusCode.NotFound)
                    {
                        LastStatus = GithubCheckStatus.NotFound;
                        FormUtils.LogRegister($"GetLatestRelease C: GitHub 404 : {githubOwner}/{repository}");
                        return false;
                    }

                    string json;

                    if (response.StatusCode == HttpStatusCode.NotModified &&
                        _jsonCache.TryGetValue(url, out string cachedJson))
                    {
                        json = cachedJson;
                    }
                    else
                    {
                        json = await response.Content.ReadAsStringAsync();

                        if (response.Headers.ETag != null)
                            _etagCache[url] = response.Headers.ETag.Tag;

                        _jsonCache[url] = json;
                    }

                    if (response.StatusCode != HttpStatusCode.NotModified)
                        response.EnsureSuccessStatusCode();

                    FormUtils.LogRegister(
                        $"GetLatestRelease D: GitHub HTTP {(int)response.StatusCode} {response.ReasonPhrase} " +
                        $"Remaining={remaining} Reset={reset}");

                    JObject release = JObject.Parse(json);

                    string version = release["tag_name"]?.ToString().Replace("v", "");

                    //*****************************************

                    JArray assets = (JArray)release["assets"];

                    if (assets == null)
                    {
                        LastStatus = GithubCheckStatus.NotFound;

                        string body = await response.Content.ReadAsStringAsync();
                        FormUtils.LogRegister("GetLatestRelease E: GitHub response body: " + body);

                        return false;
                    }

                    // ---- Passe 1 : extension + filtre (comportement strict) ----
                    foreach (JToken asset in assets)
                    {
                        string name = asset["name"]?.ToString();

                        if (!string.IsNullOrEmpty(name) &&
                            name.EndsWith(assetExtension) &&
                            name.Contains(assetFilter))
                        {
                            saveResult(version, name, asset["browser_download_url"]?.ToString());
                            return true;
                        }
                    }

                    // ---- Passe 2 : fallback sur l'extension seule ----
                    // (utile si le nom du fichier a changé côté GitHub, mais l'extension reste fiable)
                    if (!string.IsNullOrEmpty(assetFilter))
                    {
                        foreach (JToken asset in assets)
                        {
                            string name = asset["name"]?.ToString();

                            if (!string.IsNullOrEmpty(name) && name.EndsWith(assetExtension))
                            {
                                FormUtils.LogRegister(
                                    $"GetLatestRelease F: aucun asset ne correspond au filtre '{assetFilter}', " +
                                    $"mais '{name}' correspond à l'extension '{assetExtension}' -> utilisé en secours.");

                                saveResult(version, name, asset["browser_download_url"]?.ToString());
                                return true;
                            }
                        }
                    }

                    // Vraiment rien de compatible, même en secours
                    LastStatus = GithubCheckStatus.NotFound;

                    var assetNames = assets.Select(a => a["name"]?.ToString()).ToList();
                    FormUtils.LogRegister(
                        $"GetLatestRelease G: aucun asset compatible (ext='{assetExtension}'). " +
                        $"Assets trouvés : [{string.Join(", ", assetNames)}]");

                    return false;

                }
            }
            catch (HttpRequestException ex)
            {
                LastStatus = GithubCheckStatus.NetworkError;
                LastErrorDetail = ex.Message;

                FormUtils.ErrorGeneral_BoxOrLog( ex, "GetLatestRelease // réseau", "", false, true);
            }
            catch (TaskCanceledException ex)
            {
                LastStatus = GithubCheckStatus.NetworkError;
                LastErrorDetail = "Délai d'attente dépassé";

                FormUtils.ErrorGeneral_BoxOrLog( ex, "GetLatestRelease // timeout", "", false, true);
            }
            catch (Exception ex)
            {
                LastStatus = GithubCheckStatus.UnknownError;
                LastErrorDetail = ex.Message;

                FormUtils.ErrorGeneral_BoxOrLog(
                    ex,
                    "GetLatestRelease // " + githubOwner + " // " + repository + " // " + assetExtension + " // " + assetFilter,
                    "", false, true);
            }

            return false;
        }


        void client_DownloadFileCompleted(object se, System.ComponentModel.AsyncCompletedEventArgs ea)
        {
            string contentState = "DownLoad Success!!!";

            if (ea.Error != null)
            {
                contentState = ea.Error.Message;
            }
            else
            {
                //CheckVersionScriptsModLocal();
            }
        }

        // Lit la dernière release à partir de l'URL d'un dépôt.
        // Pourquoi : permettre aux campagnes d'indiquer uniquement leur dépôt.
        // Lit la dernière release à partir de l'URL d'un dépôt.
        // Pourquoi : permettre aux campagnes d'indiquer uniquement leur dépôt.
        public async Task<bool> GetLatestReleaseFromUrl(
            string repositoryUrl,
            string assetExtension,
            string assetFilter,
            Action<string, string, string> saveResult)
        {
            if (DateTime.UtcNow - ParamUpdater.LastGithubCheckUtc < ParamUpdater.GithubCheckInterval)
            {
                //return true; // utiliser les valeurs déjà en mémoire
            }

            try
            {
                if (string.IsNullOrWhiteSpace(repositoryUrl))
                    return false;

                Uri uri = new Uri(repositoryUrl);

                string[] segments =
                    uri.AbsolutePath
                       .Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);

                // On attend au minimum :
                // /owner/repository
                if (segments.Length < 2)
                    return false;

                string owner = segments[0];
                string repository = segments[1];

                // On ignore volontairement tout ce qui suit :
                // /tree/main
                // /releases
                // /issues
                // etc.

                return await GetLatestRelease(
                    owner,
                    repository,
                    assetExtension,
                    assetFilter,
                    saveResult);
            }
            catch (Exception ex)
            {
                FormUtils.ErrorGeneral_BoxOrLog(
                    ex,
                    "GetLatestReleaseFromUrl",
                    "",
                    false,
                    true);

                return false;
            }
        }


        // ---- Nouveau : diagnostic de la dernière tentative ----
        public enum GithubCheckStatus
        {
            Success,
            RateLimited,     // 403 + quota épuisé
            NotFound,        // repo/release/asset introuvable
            NetworkError,    // pas d'accès réseau / timeout / DNS
            UnknownError     // tout le reste (parsing JSON, etc.)
        }

        // Donne un libellé prêt à afficher dans l'UI
        public string GetStatusMessage()
        {
            switch (LastStatus)
            {
                case GithubCheckStatus.RateLimited:
                    TimeSpan wait = RateLimitResetUtc - DateTime.UtcNow;
                    int minutes = Math.Max(1, (int)wait.TotalMinutes);
                    return $"Quota GitHub dépassé - réessai dans {minutes} min";

                case GithubCheckStatus.NotFound:
                    return "Aucune release trouvée sur GitHub";

                case GithubCheckStatus.NetworkError:
                    return "Connexion à GitHub impossible (vérifiez internet)";

                case GithubCheckStatus.UnknownError:
                    return "Erreur GitHub : " + LastErrorDetail;

                default:
                    return "";
            }
        }




    }

}
