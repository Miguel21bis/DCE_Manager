using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;

using DCE_Manager.Parameters;
using DCE_Manager.Utils;
using Newtonsoft.Json.Linq;

namespace DCE_Manager.Update
{

    public class GithubHelper
    {
        //private const string GithubOwner = "Miguel21bis";

        // Lit la dernière release GitHub.
        // Pourquoi : factoriser le code de récupération des releases.
        //public async Task<bool> GetLatestRelease(
        //    string repository,
        //    string assetExtension,
        //    string assetFilter,
        //    Action<string, string, string> saveResult)
        //{

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

                    //string json =
                    //    await client.GetStringAsync(
                    //        $"https://api.github.com/repos/{GithubOwner}/{repository}/releases/latest");
                    
                    string json =
                        await client.GetStringAsync(
                            $"https://api.github.com/repos/{githubOwner}/{repository}/releases/latest");

                    JObject release = JObject.Parse(json);

                    string version =
                        release["tag_name"]?
                        .ToString()
                        .Replace("v", "");

                    JArray assets =
                        (JArray)release["assets"];

                    if (assets == null)
                        return false;

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
                    "GetLatestRelease",
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
