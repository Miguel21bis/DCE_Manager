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
    public static class UpdateUtils
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
        public static void RefreshUpdateTab(Form1 form)
        {
            int nbUpdate = 0;

            if (ParamUpdater.ScriptsModUpdateAvailable)
                nbUpdate++;

            if (ParamUpdater.DCEManagerUpdateAvailable)
                nbUpdate++;

            TabPage updateTab =
                form.tabControl.TabPages["tabPageLeft_Update"];
            //form.tabControl.TabPages[2];

            FormUtils.LogRegister("RefreshUpdateTab() nbUpdate: " + nbUpdate + " updateTab " + updateTab);

            if (updateTab != null)
            {
                updateTab.Text =
                    nbUpdate > 0
                    ? "Update (" + nbUpdate + ")"
                    : "Update";
            }
        }


    }

}
