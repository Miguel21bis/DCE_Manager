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

        public async Task<bool> GetLatestRelease(
        string githubOwner,
        string repository,
        string assetExtension,
        string assetFilter,
        Action<string, string, string> saveResult)
        {
            try
            {
                using (HttpClient client = new HttpClient())
                {
                    client.DefaultRequestHeaders.Add(
                        "User-Agent",
                        "DCE_Manager");

                  
                    string url = $"https://api.github.com/repos/{githubOwner}/{repository}/releases/latest";

                    if (_etagCache.TryGetValue(url, out string etag))
                        client.DefaultRequestHeaders.TryAddWithoutValidation("If-None-Match", etag);

                    HttpResponseMessage response = await client.GetAsync(url);

                    FormUtils.LogRegister(
                        $"GitHub HTTP {(int)response.StatusCode} {response.ReasonPhrase}");

                    //string json = await response.Content.ReadAsStringAsync();
                    string json;

                    if (response.StatusCode == System.Net.HttpStatusCode.NotModified &&
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

                    //response.EnsureSuccessStatusCode();
                    if (response.StatusCode != HttpStatusCode.NotModified)
                        response.EnsureSuccessStatusCode();


                    var remaining =
                     response.Headers.TryGetValues("X-RateLimit-Remaining", out var rem)
                         ? rem.FirstOrDefault()
                         : "?";

                    var reset =
                        response.Headers.TryGetValues("X-RateLimit-Reset", out var rst)
                            ? rst.FirstOrDefault()
                            : "?";

                    FormUtils.LogRegister(
                        $"GitHub HTTP {(int)response.StatusCode} {response.ReasonPhrase} " +
                        $"Remaining={remaining} Reset={reset}");


                    JObject release = JObject.Parse(json);

                    string version =
                        release["tag_name"]?
                        .ToString()
                        .Replace("v", "");

                    JArray assets =
                        (JArray)release["assets"];

                    if (assets == null)
                    {
                        string body = await response.Content.ReadAsStringAsync();
                        FormUtils.LogRegister("GitHub response body: " + body);

                        return false;
                    }
                       

                    foreach (JToken asset in assets)
                    {
                        string name =
                            asset["name"]?.ToString();

                        if (!string.IsNullOrEmpty(name) &&
                            name.EndsWith(assetExtension) &&
                            name.Contains(assetFilter))
                        {
                            saveResult(
                                version,
                                name,
                                asset["browser_download_url"]?.ToString());

                            return true;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                FormUtils.ErrorGeneral_BoxOrLog(
                    ex,
                    "GetLatestRelease // " + githubOwner + " // " + repository + " // " + assetExtension + " // " + assetFilter,
                    "",
                    false,
                    true);
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

    }

}
