using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using DCE_Manager;
using NLua;

namespace DCE_Manager
{
    public partial class Form4_CampaignEdit : Form
    {
        Form1 _form1;

        public void FormCampaignEdit2(Form1 form1, string path, string NameCamp)
        {
            InitializeComponent();

            InitializeComponent();
            _form1 = form1;
            Lua lua = new Lua();


            //lua.DoFile(_form1.textBox_SavedGames.Text + @"\Mods\tech\DCE\ScriptsMod.NG\UTIL_ChangePlane_DceM.lua");

            lua.NewTable("TabSquad");
            var result0 = lua.DoFile(_form1.textBox_SavedGames.Text + @"\Mods\tech\DCE\Missions\Campaigns\" + NameCamp + @"\Active\oob_air.lua");
            LuaTable TabSquadC = lua.GetTable("TabSquad");

            var enumerator = TabSquadC.GetEnumerator();
            int i = 1;
            while (enumerator.MoveNext())
            {
                //comboPlaneChoice.Items.Add(enumerator.Value.ToString());
                //comboPlaneChoice.SelectedItem = enumerator.Value.ToString();
                i++;
            }

            //string tempTXT = (string)comboPlaneChoice.SelectedItem;
            //string[] words = tempTXT.Split('|');
            //planeFIX.Text = words[0].Replace(" ", "");
            //SquadName.Text = words[1];

            //SquadName.Text = SquadName.Text.TrimStart();
            //SquadName.Text = SquadName.Text.TrimEnd();

            //CloneCampaign.SquadName = SquadName.Text;
            //BaseName.Text = words[2];
            //CampaignName.Text = OldNameCamp + "-" + planeFIX.Text;
            //string NewdNameCamp = CampaignName.Text;

            //CloneCampaign.path = path;
            //CloneCampaign.OldNameCamp = OldNameCamp;

        }
    }
}

