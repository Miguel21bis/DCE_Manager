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
        public void SetOobInitMode(bool init)
        {
            radioButton_OOB_INIT.Checked = init;
        }
        public void SetOobActiveMode(bool init)
        {
            radioButton_OOB_ACTIVE.Checked = init;
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

    }
}
