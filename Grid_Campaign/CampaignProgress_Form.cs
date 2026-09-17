using System;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;

namespace DCE_Manager
{
    // Etat de progression transmis par CampaignExporter / CampaignImporter.
    // Deux niveaux : "Overall" = avancement global, "Detail" = element en cours
    // (fichier dans la livree courante, par ex.), pour distinguer les deux phases longues.
    internal class CampaignProgressInfo
    {
        public int OverallPercent;
        public string OverallText;
        public int DetailPercent;
        public string DetailText;
    }

    // Fenetre de progression pour les operations longues (zippage/extraction avec livrees).
    // Volontairement maison plutot que Ookii.ProgressDialog : ce dernier s'appuie sur une
    // interface COM et fige le thread UI pendant l'attente.
    internal class CampaignProgress_Form : Form
    {
        private readonly ProgressBar progressBarOverall;
        private readonly ProgressBar progressBarDetail;
        private readonly Label labelOverall;
        private readonly Label labelDetail;
        private readonly Button buttonCancel;

        private readonly CancellationTokenSource _cts = new CancellationTokenSource();

        public CancellationToken Token => _cts.Token;

        public CampaignProgress_Form(string title)
        {
            Text = title;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false;
            MinimizeBox = false;
            ControlBox = false;   // on annule par le bouton Cancel, pas par la croix
            ClientSize = new Size(560, 175);

            labelOverall = new Label
            {
                Text = "Starting...",
                AutoEllipsis = true,
                Location = new Point(12, 12),
                Size = new Size(536, 18)
            };

            progressBarOverall = new ProgressBar
            {
                Location = new Point(12, 34),
                Size = new Size(536, 22),
                Minimum = 0,
                Maximum = 100
            };

            labelDetail = new Label
            {
                Text = "",
                AutoEllipsis = true,   // chemins longs coupes proprement au lieu de deborder
                Location = new Point(12, 68),
                Size = new Size(536, 34)
            };

            progressBarDetail = new ProgressBar
            {
                Location = new Point(12, 106),
                Size = new Size(536, 18),
                Minimum = 0,
                Maximum = 100
            };

            buttonCancel = new Button
            {
                Text = "Cancel",
                Location = new Point(468, 138),
                Width = 80
            };
            buttonCancel.Click += (s, args) =>
            {
                _cts.Cancel();
                buttonCancel.Enabled = false;
                labelOverall.Text = "Cancelling...";
            };

            Controls.Add(labelOverall);
            Controls.Add(progressBarOverall);
            Controls.Add(labelDetail);
            Controls.Add(progressBarDetail);
            Controls.Add(buttonCancel);
        }

        // Appelee depuis le thread UI (via IProgress), donc pas d'Invoke necessaire.
        public void UpdateProgress(CampaignProgressInfo info)
        {
            if (info == null)
                return;

            progressBarOverall.Value = Math.Max(0, Math.Min(100, info.OverallPercent));
            progressBarDetail.Value = Math.Max(0, Math.Min(100, info.DetailPercent));

            if (!_cts.IsCancellationRequested)
                labelOverall.Text = info.OverallText ?? "";

            labelDetail.Text = info.DetailText ?? "";
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                _cts.Dispose();

            base.Dispose(disposing);
        }
    }
}