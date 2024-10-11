using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace DCE_Manager
{
    public partial class CustomMessageBox : Form
    {
        public CustomMessageBox(string message, [System.Runtime.CompilerServices.CallerLineNumber] int lineNumber = 0)
        {
            TextBox textBox = new TextBox
            {
                Text = $"Line error {lineNumber}: {message}",
                //Text = message,
                Dock = DockStyle.Fill,
                Multiline = true,
                ReadOnly = true,
                WordWrap = true,
                ScrollBars = ScrollBars.Vertical
            };

            Button buttonOK = new Button
            {
                Text = "OK",
                Dock = DockStyle.Bottom
            };
            buttonOK.Click += (sender, e) => this.Close();

            this.Controls.Add(textBox);
            this.Controls.Add(buttonOK);
            this.StartPosition = FormStartPosition.CenterParent;
            this.Size = new Size(400, 300);
        }

        public static void ShowMessage(string message)
        {
            using (var messageBox = new CustomMessageBox(message))
            {
                messageBox.ShowDialog();
            }
        }
    }
}


