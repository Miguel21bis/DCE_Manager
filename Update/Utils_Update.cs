using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows.Forms;
using DCE_Manager.Parameters;
using DCE_Manager.Utils;
using Newtonsoft.Json.Linq;

namespace DCE_Manager.Update
{
    public static class Utils_Update
    {
        // Compare deux versions.
        // Pourquoi : déterminer si une mise à jour est disponible.
        public static bool IsVersionNewer(
            string githubVersion,
            string localVersion)
        {
            try
            {
                Version github =
                    new Version(githubVersion);

                Version local =
                    new Version(localVersion);

                return github > local;
            }
            catch
            {
                return false;
            }
        }

        // Met à jour le nom de l'onglet Update.
        // Pourquoi : afficher automatiquement le nombre de mises à jour disponibles.
        public static void RefreshUpdateTab(Main_Form form)
        {
            Updater_Param.NbUpdateAvailable = 0;

            if (Updater_Param.ScriptsModUpdateAvailable)
                Updater_Param.NbUpdateAvailable++;

            if (Updater_Param.DCEManagerUpdateAvailable)
                Updater_Param.NbUpdateAvailable++;

            if (form.campaignUpdater != null)
                Updater_Param.NbUpdateAvailable += form.campaignUpdater.GetUpdateCount();

            TabPage updateTab =
                form.tabControl_LEFT.TabPages["tabPageLeft_Update"];

            FormUtils.LogRegister(
                "RefreshUpdateTab() nbUpdate: " +
                Updater_Param.NbUpdateAvailable +
                " updateTab " +
                updateTab);

            if (updateTab != null)
            {
                updateTab.Text =
                    Updater_Param.NbUpdateAvailable > 0
                    ? "Update (" + Updater_Param.NbUpdateAvailable + ")"
                    : "Update";
            }
        }

    }

}
