using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;
using DCE_Manager.Parameters;
using DCE_Manager.Utils;

namespace DCE_Manager
{
    // Bandeau d'une section de la colonne de gauche de la carte (météo, plus tard presets radio et datalink) : un clic le replie sur cette seule ligne
    // Panneau sans scintillement : double tampon + WS_EX_COMPOSITED (les contrôles enfants se dessinent d'un bloc)
    internal class Map_UI_FastPanel : Panel
    {
        public Map_UI_FastPanel() { DoubleBuffered = true; ResizeRedraw = true; }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= 0x02000000;
                return cp;
            }
        }
    }

    // Tableau sans scintillement (double tampon) : le cadre des avions du vol est reconstruit à chaque ajout / retrait d'avion
    internal class Map_UI_FastTable : TableLayoutPanel
    {
        public Map_UI_FastTable() { DoubleBuffered = true; }
    }

    internal class Map_UI_Section : Control
    {
        private bool _expanded = true;
        private string _summary = "";
        public string Title = "";
        public event EventHandler Toggled;

        private static readonly Font FTitle = new Font("Segoe UI", 8.5f, FontStyle.Bold);
        private static readonly Font FSym = new Font("Segoe UI Symbol", 9f);

        public Map_UI_Section()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Cursor = Cursors.Hand;
        }

        public bool Expanded
        {
            get { return _expanded; }
            set
            {
                if (_expanded == value) return;
                _expanded = value;
                Invalidate();
                if (Toggled != null) Toggled(this, EventArgs.Empty);
            }
        }

        public string Summary
        {
            get { return _summary; }
            set { _summary = value ?? ""; Invalidate(); }
        }

        protected override void OnClick(EventArgs e)
        {
            base.OnClick(e);
            Expanded = !Expanded;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            using (var fill = new SolidBrush(Color.FromArgb(33, 41, 60))) g.FillRectangle(fill, 0, 0, Width, Height);
            using (var pen = new Pen(Color.FromArgb(62, 78, 112))) g.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);

            TextRenderer.DrawText(g, _expanded ? "\u25BE" : "\u25B8", FSym, new Rectangle(4, 0, 18, Height), Color.FromArgb(120, 225, 150), TextFormatFlags.VerticalCenter | TextFormatFlags.Left);
            TextRenderer.DrawText(g, Title, FTitle, new Rectangle(24, 0, Width - 28, Height), Color.FromArgb(225, 230, 240), TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPadding);

            int tw = TextRenderer.MeasureText(g, Title, FTitle).Width;
            int sx = 24 + tw + 10;
            if (_summary != "" && sx < Width - 20)
                TextRenderer.DrawText(g, _summary, Font, new Rectangle(sx, 0, Width - sx - 6, Height), Color.FromArgb(140, 150, 170), TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
        }
    }
}
