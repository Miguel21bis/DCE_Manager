using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using DCE_Manager.Parameters;

namespace DCE_Manager
{
    // Interrupteur général "web" de DCE_Manager pour les calculs (relief du terrain, etc.).
    // L'état est gardé dans options.txt (clé WebTerrain : 1 = autorisé, 0 = interdit) :
    // il reste tel quel au prochain démarrage. Clé absente = autorisé.
    internal static class Main_WebAccess
    {
        private const string Key = "WebTerrain";

        // Déclenché quand l'utilisateur change l'état (pour repainter les contrôles, relancer un web "tombé"...)
        public static event Action Changed;

        public static bool Enabled
        {
            get
            {
                string v;
                if (ParamConf.configDictionary.TryGetValue(Key, out v) && v != null && v.Trim() == "0")
                    return false;
                return true;
            }
        }

        public static void Set(bool on)
        {
            ParamConf.configDictionary[Key] = on ? "1" : "0";
            Configuration_Form.Save_Config();   // écrit options.txt tout de suite (pas seulement à la fermeture)

            if (Changed != null) Changed();
        }
    }

    // Petit contrôle (ligne des onglets de gauche) : icône "wifi" verte quand le web est autorisé,
    // rouge et barrée quand l'utilisateur l'a interdit. Un clic change l'état.
    internal class Main_WebToggle : Control
    {
        private static readonly Color Green = Color.FromArgb(40, 160, 70);
        private static readonly Color Red = Color.FromArgb(205, 40, 40);

        private bool _hover;
        private readonly ToolTip _tip = new ToolTip();

        public Main_WebToggle()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);

            Size = new Size(150, 34);
            Cursor = Cursors.Hand;
            TabStop = false;
            AccessibleName = "Web access";

            Main_WebAccess.Changed += OnChanged;
            UpdateTip();
        }

        private void OnChanged()
        {
            UpdateTip();
            Invalidate();
        }

        private void UpdateTip()
        {
            _tip.SetToolTip(this, Main_WebAccess.Enabled
                ? "Web MAP is ON.\r\nDCE_Manager may download map tiles and topographic data (slopes, water) from the web to place units and FARPs better.\r\nThis has nothing to do with updates: ScriptsMod and campaign updates are not affected.\r\nIf the web is not reachable, nothing is blocked.\r\nClick to turn it OFF."
                : "Web MAP is OFF.\r\nNo map tiles or topographic data are downloaded. Placement uses only the zones and the data already downloaded.\r\nUpdates (ScriptsMod, campaigns) still work.\r\nClick to turn it ON again.");
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                Main_WebAccess.Changed -= OnChanged;
                _tip.Dispose();
            }
            base.Dispose(disposing);
        }

        protected override void OnClick(EventArgs e)
        {
            base.OnClick(e);
            Main_WebAccess.Set(!Main_WebAccess.Enabled);
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(_hover ? Color.FromArgb(225, 232, 240) : (Parent != null ? Parent.BackColor : SystemColors.Control));

            bool on = Main_WebAccess.Enabled;
            Color col = on ? Green : Red;

            // Tout est dessiné pour une hauteur de 34 px, puis mis à l'échelle : le contrôle
            // peut donc être bas (ligne des onglets) comme haut (barre du haut).
            float s = Height / 34f;

            // Icône wifi : un point + 3 arcs
            float cx = 18f * s, cy = Height / 2f + 8f * s;

            using (var pen = new Pen(col, Math.Max(1.5f, 2.6f * s)))
            using (var brush = new SolidBrush(col))
            {
                pen.StartCap = LineCap.Round;
                pen.EndCap = LineCap.Round;

                float dot = 2.2f * s;
                g.FillEllipse(brush, cx - dot, cy - dot, dot * 2, dot * 2);

                float[] radius = { 6.5f, 11.5f, 16.5f };
                foreach (float r0 in radius)
                {
                    float r = r0 * s;
                    g.DrawArc(pen, cx - r, cy - r, r * 2, r * 2, 225, 90);
                }
            }

            // Barre rouge (avec un liseré blanc pour qu'elle se voie sur les arcs)
            if (!on)
            {
                using (var white = new Pen(Color.White, Math.Max(3f, 6f * s)))
                using (var red = new Pen(Red, Math.Max(2f, 3.2f * s)))
                {
                    white.StartCap = white.EndCap = LineCap.Round;
                    red.StartCap = red.EndCap = LineCap.Round;
                    g.DrawLine(white, 4 * s, 5 * s, 32 * s, Height - 5 * s);
                    g.DrawLine(red, 4 * s, 5 * s, 32 * s, Height - 5 * s);
                }
            }

            using (var font = new Font("Segoe UI", 8.25f, FontStyle.Bold))
            using (var textBrush = new SolidBrush(col))
            {
                string text = on ? "Web MAP : ON" : "Web MAP : OFF";
                SizeF sz = g.MeasureString(text, font);
                g.DrawString(text, font, textBrush, 42f * s, (Height - sz.Height) / 2f);
            }
        }
    }
}
