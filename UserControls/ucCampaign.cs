using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using DCE_Manager.Parameters;

namespace DCE_Manager.UserControls
{
    public partial class ucCampaign : UserControl
    {
        public DataGridView DataGridViewBlue => dataGridViewBlue;

        public DataGridView DataGridViewRed => dataGridViewRed;

        public ucCampaign()
        {
            InitializeComponent();

            CampaignTab.Selected += CampaignTab_Selected;

            // CheckedChanged part sur les DEUX radios à chaque changement :
            // celui qui se décoche ET celui qui se coche. Sans le filtre ci-dessous,
            // Main_Form recevait donc systématiquement deux appels au lieu d'un.
            radioButton_OOB_INIT.CheckedChanged += (s, e) =>
            {
                if (!radioButton_OOB_INIT.Checked)
                    return;

                Main_Form.Instance?.radioButton_OOB_INIT_CheckedChanged(s, e);
            };

            radioButton_OOB_ACTIVE.CheckedChanged += (s, e) =>
            {
                if (!radioButton_OOB_ACTIVE.Checked)
                    return;

                Main_Form.Instance?.radioButton_OOB_ACTIVE_CheckedChanged(s, e);
            };
        }

        private void CampaignTab_Selected(object sender, TabControlEventArgs e)
        {
            Main_Form.Instance.CampaignGridLeft.UpdateCampaignButtonsVisibility();
        }


        //les RadioButtons
        public bool IsOobInit
        {
            get { return radioButton_OOB_INIT.Checked; }
        }

        public bool IsOobActive
        {
            get { return radioButton_OOB_ACTIVE.Checked; }
        }


        //Puis : les boutons

        // Une seule méthode pour choisir la version affichée.
        // Pourquoi : deux setters indépendants permettaient de mettre les deux radios
        //            à true (ou à false) et de se retrouver dans un état impossible.
        public void SetOobMode(bool init)
        {
            if (init)
                radioButton_OOB_INIT.Checked = true;
            else
                radioButton_OOB_ACTIVE.Checked = true;
        }

        // Conservées pour ne pas casser les appels existants.
        public void SetOobInitMode(bool init)
        {
            SetOobMode(init);
        }

        public void SetOobActiveMode(bool active)
        {
            SetOobMode(!active);
        }

        public void EnableSaveButton(bool enabled)
        {
            buttonSaveChgtCampaign.Enabled = enabled;
        }

        public void EnableResetButton(bool enabled)
        {
            buttonResetBackup.Enabled = enabled;
        }

        //Ensuite : CampaignTab
        public void SelectMissionTab()
        {
            CampaignTab.SelectedIndex = 0;
        }

        public void SelectBlueTab()
        {
            CampaignTab.SelectedIndex = 1;
        }

        public void SelectRedTab()
        {
            CampaignTab.SelectedIndex = 2;
        }
        public int SelectedTabIndex
        {
            get { return CampaignTab.SelectedIndex; }
        }
        public bool IsOobTabSelected
        {
            get
            {
                return CampaignTab.SelectedTab == tabPage14 ||
                       CampaignTab.SelectedTab == tabPage15;
            }
        }

        //Label
        public void ShowCampaignName(bool visible)
        {
            label_Right_Campaign_Name.Visible = visible;
        }

        private void buttonSaveChgtCampaign_Click(object sender, EventArgs e)
        {
            Main_Form.Instance.CampaignGridLeft.CurrentCampaignEdit?.buttonSaveChgtCampaign_Click(sender, e);
        }

        private void buttonResetBackup_Click(object sender, EventArgs e)
        {
            Main_Form.Instance.CampaignGridLeft.CurrentCampaignEdit?.buttonResetBackup_Click(sender, e);
        }
    }
}
