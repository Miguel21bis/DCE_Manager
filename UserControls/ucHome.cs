using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
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

            InitMissionScriptingTooltip();
        }

        // Bulle d'aide sur le label (et la case) MissionScripting
        // (champ de classe : sinon le ToolTip peut être supprimé par le ramasse-miettes)
        private ToolTip _tipMissionScripting = new ToolTip();

        private void InitMissionScriptingTooltip()
        {
            string texte =
                "Needed for DCE to work properly.\n" +
                "It lets the DCE scripts read and write files while a mission runs.\n\n" +
                "Side effect: this change skips the DCS integrity check for this file\n" +
                "(Scripts\\MissionScripting.lua).\n\n" +
                "A DCS update restores the original file: just tick the box again.\n" +
                "As with any mod, only run missions from sources you trust.";

            _tipMissionScripting.AutoPopDelay = 30000;
            _tipMissionScripting.InitialDelay = 300;
            _tipMissionScripting.ReshowDelay = 200;
            _tipMissionScripting.ShowAlways = true;
            _tipMissionScripting.ToolTipTitle = "MissionScripting mod";

            _tipMissionScripting.SetToolTip(label_accueil_satinize, texte);
            _tipMissionScripting.SetToolTip(chkMissionScripting, texte);
            _tipMissionScripting.SetToolTip(pic_Accueil_sanitize_status, texte);
        }

        public void SetClientId(string id)
        {
            AjusterLargeurTextBox(textBox_id_client);
            textBox_id_client.Text = id;
            //ParamConf.DCE_Manager_LocVer = id;
        }
        public void idClient_Visible(bool visible)
        {
            textBox_id_client.Visible = visible;
        }

        public void SetDceManagerVersion(string version)
        {
            label_Accueil_DceManager_Ver.Text = version;
        }

        public void SetScriptsModVersion(string version)
        {
            FormUtils.LogRegister("SetScriptsModVersion() : " + version);

            label_Accueil_ScriptsMod_Ver.Text = version;
        }

        public void SetInstalledCampaignsCount(int count)
        {
            label_Accueil_InstalledCamp_Nb.Text = count.ToString();
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

        // Install client : bin\DCS.exe. Install serveur dédié : bin\DCS_server.exe.
        // On prend celui qui existe dans le bin de la config sélectionnée.
        private void pic_Accueil_DCS_Click(object sender, System.EventArgs e)
        {
            string binFolder = ParamConf.PATH_DCS_Root + @"\bin";

            string exePath = binFolder + @"\DCS.exe";
            if (!System.IO.File.Exists(exePath))
            {
                string exePathServer = binFolder + @"\DCS_server.exe";
                if (System.IO.File.Exists(exePathServer))
                {
                    exePath = exePathServer;
                }
                else
                {
                    MessageBox.Show(
                        "Neither DCS.exe nor DCS_server.exe found in:\r\n" + binFolder +
                        "\r\n\r\nCheck the DCS path in the configuration.",
                        "DCS not found");
                    return;
                }
            }

            try
            {
                Process process = new Process();
                process.StartInfo.FileName = exePath;
                process.StartInfo.Arguments = " ";
                process.StartInfo.WindowStyle = ProcessWindowStyle.Normal;
                process.StartInfo.WorkingDirectory = binFolder;

                process.Start();
            }
            catch (Exception ex)
            {
                FormUtils.ErrorGeneral_BoxOrLog(ex, "pic_Accueil_DCS_Click", exePath, false, true);
                MessageBox.Show("Unable to launch DCS: " + ex.Message);
            }
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

        private void pict_accueil_satinize_Click(object sender, EventArgs e)
        {

        }

        // Met la case (et l'icône de statut) sur l'état réel du fichier MissionScripting.lua
        // Appelée via Main_Form.checkBoxMod() : au chargement, au changement de chemin DCS, etc.
        public void RefreshMissionScriptingCheck()
        {
            bool cheminOk = !string.IsNullOrWhiteSpace(ParamConf.PATH_DCS_Root)
                            && Directory.Exists(ParamConf.PATH_DCS_Root);

            bool patched = cheminOk && DcsPatcher.IsPatched(ParamConf.PATH_DCS_Root);

            chkMissionScripting.Enabled = cheminOk;
            chkMissionScripting.Checked = patched;

            pic_Accueil_sanitize_status.Image = patched
                ? global::DCE_Manager.Properties.Resources.icons8_ok_24
                : global::DCE_Manager.Properties.Resources.icons8_warning_blue_30;
        }

        private void chkMissionScripting_Click(object sender, EventArgs e)
        {
            bool veutAppliquer = chkMissionScripting.Checked;   // déjà changé au moment du clic

            string msg;
            bool ok = veutAppliquer
                ? DcsPatcher.Patch(ParamConf.PATH_DCS_Root, out msg)
                : DcsPatcher.Unpatch(ParamConf.PATH_DCS_Root, out msg);

            // On ne se fie pas qu'au retour de Patch/Unpatch : on relit le fichier pour vérifier le résultat réel
            bool etatReel = DcsPatcher.IsPatched(ParamConf.PATH_DCS_Root);
            bool reussi = ok && (etatReel == veutAppliquer);

            // Cas rare : pas d'erreur, mais le fichier n'a pas l'état attendu (contenu inattendu, DCS modifié...)
            if (ok && !reussi)
                msg = "No error, but the sanitizeModule('os') / ('io') lines were not found as expected in the file.";

            if (reussi)
            {
                // Succès : petit popup discret qui s'efface tout seul
                string sousTitre = DcsPatcher.IsDcsRunning() ? "Takes effect at the next mission launch." : "";

                if (veutAppliquer)
                    ShowSuccessToast("MissionScripting mod activated", sousTitre,
                                     Color.FromArgb(46, 160, 67), Color.FromArgb(232, 245, 233));
                else
                    ShowSuccessToast("MissionScripting mod deactivated", sousTitre,
                                     Color.FromArgb(96, 125, 139), Color.FromArgb(236, 240, 243));
            }
            else
            {
                // Échec : fenêtre avec texte sélectionnable / copiable
                string fichier = Path.Combine(ParamConf.PATH_DCS_Root ?? "", "Scripts", "MissionScripting.lua");

                string texte = (veutAppliquer ? "Activation FAILED." : "Deactivation FAILED.")
                               + "\n\n" + msg
                               + "\n\nFile: " + fichier;

                ShowFailureDialog("MissionScripting", texte);
            }

            RefreshMissionScriptingCheck();
        }

        // Petit popup "tout mignon" : pastille ronde + texte, sans bordure, s'efface tout seul (clic = fermer).
        private void ShowSuccessToast(string titre, string sousTitre, Color couleurPastille, Color couleurFond)
        {
            Form f = new Form();
            f.FormBorderStyle = FormBorderStyle.None;
            f.StartPosition = FormStartPosition.Manual;   // position calculée plus bas (CenterParent ne marche pas avec Show)
            f.ShowInTaskbar = false;
            f.BackColor = couleurFond;
            f.Size = new Size(330, string.IsNullOrEmpty(sousTitre) ? 64 : 84);

            // coins arrondis
            int r = 20;
            GraphicsPath coins = new GraphicsPath();
            coins.AddArc(0, 0, r, r, 180, 90);
            coins.AddArc(f.Width - r, 0, r, r, 270, 90);
            coins.AddArc(f.Width - r, f.Height - r, r, r, 0, 90);
            coins.AddArc(0, f.Height - r, r, r, 90, 90);
            coins.CloseFigure();
            f.Region = new Region(coins);

            // pastille ronde avec la coche
            Label pastille = new Label();
            pastille.Text = "✓";
            pastille.Font = new Font("Segoe UI Symbol", 14f, FontStyle.Bold);
            pastille.ForeColor = Color.White;
            pastille.BackColor = couleurPastille;
            pastille.TextAlign = ContentAlignment.MiddleCenter;
            pastille.Size = new Size(36, 36);
            pastille.Location = new Point(16, (f.Height - 36) / 2);
            GraphicsPath cercle = new GraphicsPath();
            cercle.AddEllipse(0, 0, 36, 36);
            pastille.Region = new Region(cercle);

            Label lblTitre = new Label();
            lblTitre.Text = titre;
            lblTitre.Font = new Font("Segoe UI", 10f, FontStyle.Bold);
            lblTitre.ForeColor = Color.FromArgb(40, 40, 40);
            lblTitre.BackColor = Color.Transparent;
            lblTitre.AutoSize = true;
            lblTitre.Location = new Point(64, string.IsNullOrEmpty(sousTitre) ? 20 : 16);

            f.Controls.Add(pastille);
            f.Controls.Add(lblTitre);

            if (!string.IsNullOrEmpty(sousTitre))
            {
                Label lblSous = new Label();
                lblSous.Text = sousTitre;
                lblSous.Font = new Font("Segoe UI", 8.5f);
                lblSous.ForeColor = Color.DimGray;
                lblSous.BackColor = Color.Transparent;
                lblSous.AutoSize = true;
                lblSous.Location = new Point(64, 42);
                lblSous.Click += (s, e) => f.Close();
                f.Controls.Add(lblSous);
            }

            // un clic n'importe où ferme le popup
            f.Click += (s, e) => f.Close();
            pastille.Click += (s, e) => f.Close();
            lblTitre.Click += (s, e) => f.Close();

            // disparition automatique en fondu
            int dureeAvantFondu = string.IsNullOrEmpty(sousTitre) ? 1600 : 3200;
            int ecoule = 0;
            System.Windows.Forms.Timer t = new System.Windows.Forms.Timer();
            t.Interval = 30;
            t.Tick += (s, e) =>
            {
                ecoule += 30;
                if (ecoule > dureeAvantFondu)
                {
                    f.Opacity -= 0.08;
                    if (f.Opacity <= 0.05)
                    {
                        t.Stop();
                        f.Close();
                    }
                }
            };
            f.FormClosed += (s, e) =>
            {
                t.Stop();
                t.Dispose();
                f.Dispose();
            };

            // Centré au milieu de la fenêtre principale
            Form parent = this.FindForm();
            if (parent != null)
            {
                f.Location = new Point(
                    parent.Left + (parent.Width - f.Width) / 2,
                    parent.Top + (parent.Height - f.Height) / 2);
                f.Show(parent);
            }
            else
            {
                f.StartPosition = FormStartPosition.CenterScreen;
                f.Show();
            }
            t.Start();
        }

        // Fenêtre d'échec : texte sélectionnable / copiable (Ctrl+C), sans texte présélectionné en bleu,
        // + bouton "Copy" qui met tout le message dans le presse-papiers.
        private void ShowFailureDialog(string titre, string texte)
        {
            using (Form f = new Form())
            {
                f.Text = titre;
                f.StartPosition = FormStartPosition.CenterParent;
                f.FormBorderStyle = FormBorderStyle.FixedDialog;
                f.MaximizeBox = false;
                f.MinimizeBox = false;
                f.ShowInTaskbar = false;
                f.ClientSize = new Size(540, 230);

                PictureBox pic = new PictureBox();
                pic.Image = SystemIcons.Error.ToBitmap();
                pic.SizeMode = PictureBoxSizeMode.AutoSize;
                pic.Location = new Point(16, 16);

                TextBox txt = new TextBox();
                txt.Multiline = true;
                txt.ReadOnly = true;
                txt.BorderStyle = BorderStyle.None;
                txt.BackColor = f.BackColor;
                txt.ScrollBars = ScrollBars.Vertical;
                txt.TabStop = false;
                txt.Text = texte.Replace("\r\n", "\n").Replace("\n", "\r\n");
                txt.Location = new Point(64, 16);
                txt.Size = new Size(460, 155);

                Button btnCopier = new Button();
                btnCopier.Text = "Copy";
                btnCopier.Size = new Size(90, 28);
                btnCopier.Location = new Point(16, 186);
                btnCopier.Click += (s, e) =>
                {
                    try { Clipboard.SetText(txt.Text); } catch { /* presse-papiers occupé : on ignore */ }
                };

                Button btnOk = new Button();
                btnOk.Text = "OK";
                btnOk.Size = new Size(90, 28);
                btnOk.Location = new Point(434, 186);
                btnOk.DialogResult = DialogResult.OK;

                f.Controls.Add(pic);
                f.Controls.Add(txt);
                f.Controls.Add(btnCopier);
                f.Controls.Add(btnOk);
                f.AcceptButton = btnOk;
                f.CancelButton = btnOk;
                f.ActiveControl = btnOk;   // le focus va sur OK, pas dans le texte

                // Un TextBox sélectionne tout à l'affichage : on annule la présélection
                f.Shown += (s, e) =>
                {
                    txt.SelectionStart = 0;
                    txt.SelectionLength = 0;
                    btnOk.Focus();
                };

                f.ShowDialog(this);
            }
        }
    }
}
