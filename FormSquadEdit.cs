using System;
using System.Windows.Forms;
using DCE_Manager.Parameters;

namespace DCE_Manager
{
    public partial class FormSquadEdit : Form
    {
        public Squad EditedSquad { get; private set; }

        public FormSquadEdit(Squad squad, bool cloneMode = false)
        {
            InitializeComponent();

            // On travaille sur une copie si on clone
            if (cloneMode)
            {
                EditedSquad = new Squad()
                {
                    Name = squad.Name + "_Copy",
                    Type = squad.Type,
                    Base = squad.Base,
                    Country = squad.Country,
                    Skill = squad.Skill,
                    Number = squad.Number,
                    Reserve = squad.Reserve,
                    Player = squad.Player,
                    SideString = squad.SideString,
                    FolderFile = squad.FolderFile
                };

                this.Text = "Clone squad";
            }
            else
            {
                EditedSquad = squad;
                this.Text = "Edit squad";
            }

            textBoxName.Text = EditedSquad.Name;
            textBoxType.Text = EditedSquad.Type;
            textBoxBase.Text = EditedSquad.Base;
            numericNumber.Value = EditedSquad.Number;
            numericReserve.Value = EditedSquad.Reserve;
            checkBoxPlayer.Checked = EditedSquad.Player;
        }

        private void buttonOK_Click(object sender, EventArgs e)
        {
            EditedSquad.Name = textBoxName.Text;
            EditedSquad.Type = textBoxType.Text;
            EditedSquad.Base = textBoxBase.Text;
            EditedSquad.Number = (int)numericNumber.Value;
            EditedSquad.Reserve = (int)numericReserve.Value;
            EditedSquad.Player = checkBoxPlayer.Checked;

            DialogResult = DialogResult.OK;
            Close();
        }

        private void buttonCancel_Click(object sender, EventArgs e)
        {
            DialogResult = DialogResult.Cancel;
            Close();
        }
    }
}