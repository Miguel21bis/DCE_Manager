using System;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;
using DCE_Manager.Parameters;
using DCE_Manager.Utils;


namespace DCE_Manager.UserControls
{
    public partial class ucHome : UserControl
    {
        public ucHome()
        {
            InitializeComponent();
            //BackColor = Color.Black;
        }

        public void SetClientId(string id)
        {
            AjusterLargeurTextBox(textBox_id_client);
            textBox_id_client.Text = id;
            ParamConf.DCE_Manager_LocVer = id;
        }
        public void idClient_Visible(bool visible)
        {
            textBox_id_client.Visible = visible;
        }

        public void SetDceManagerVersion(string version)
        {
            label_Accueil_DceManagerVer.Text = version;
        }


        private void AjusterLargeurTextBox(TextBox tb)
        {
            using (Graphics g = tb.CreateGraphics())
            {
                SizeF size = g.MeasureString(tb.Text, tb.Font);
                tb.Width = (int)size.Width + 10; // marge de 10 pixels
            }
        }


        private void pictureBoxOvGME_Click(object sender, System.EventArgs e)
        {
            string OvGME_Path = FormUtils.IsApplicationInstalled("OvGME");
            string Empty = "";
            bool result = Empty.Equals(OvGME_Path);

            //MessageBox.Show(OvGME_Path, OvGME_Path);
            if (!result)
            {

                Process process = new Process();

                process.StartInfo.FileName = OvGME_Path + @"\OvGME.exe";
                process.StartInfo.Arguments = " ";
                process.StartInfo.WindowStyle = ProcessWindowStyle.Normal;
                process.StartInfo.WorkingDirectory = OvGME_Path;

                process.Start();
            }
        }

        private void pic_Accueil_DCS_Click(object sender, System.EventArgs e)
        {
            Process process = new Process();
            // Configure the process using the StartInfo properties.BoxOvGME
            process.StartInfo.FileName = ParamConf.PATH_DCS_Root + @"\bin\DCS.exe";
            process.StartInfo.Arguments = " ";
            process.StartInfo.WindowStyle = ProcessWindowStyle.Normal;
            process.StartInfo.WorkingDirectory = ParamConf.PATH_DCS_Root + @"\bin";

            process.Start();
        }


        
        public bool ButtonPreview = false;
        public void ucHome_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.A)
            {
                ButtonPreview = true;
            }
        }

        public void ucHome_KeyUp(object sender, KeyEventArgs e)
        {
            ////if (e.KeyCode == Keys.Control)
            if (e.KeyCode == Keys.A)
            {
                ButtonPreview = false;
            }
        }

        private void VersionDceManager_Click(object sender, System.EventArgs e)
        {
            //Pour devenir DEV

            if (ButtonPreview == true)
            {

                //GetVersionDceManager();

                Main_Form.Instance.but_Level_DEV.Visible = true;


                //but_GPS_LL.Visible = true;
                //LabelStatut.Text = "DEV";
                this.Text = "DCE_Manager - DEV - " + ParamConf.CurrentConfigName;

                idClient_Visible(true);

            }

        }

        private void pic_Accueil_CEFI_Click(object sender, System.EventArgs e)
        {

            string url = "https://en.wikipedia.org/wiki/French_Expeditionary_Corps_(1943%E2%80%9344)";

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true // Requis sous .NET Core / .NET 5+ pour ouvrir une URL
                });
            }
            catch (Exception ex)
            {
                // Optionnel : gérer l'erreur si le navigateur ne peut pas s'ouvrir
                System.Windows.Forms.MessageBox.Show("Unable to open the link : " + ex.Message);
            }

        }


        private void pic_Accueil_SPA3_Click(object sender, EventArgs e)
        {

            // 3. Construit l'URL dynamique
            string url = "https://en.wikipedia.org/wiki/Escadrille_3";

            // 4. Ouvre le navigateur web
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true // Requis sous .NET Core / .NET 5+ pour ouvrir une URL
                });
            }
            catch (Exception ex)
            {
                // Optionnel : gérer l'erreur si le navigateur ne peut pas s'ouvrir
                System.Windows.Forms.MessageBox.Show("Unable to open the link : " + ex.Message);
            }
        }

        private void label_Systeme_Status_Click(object sender, EventArgs e)
        {

        }
    }
}
