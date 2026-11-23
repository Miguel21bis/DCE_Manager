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
    // Fenêtre de saisie des pistes. Deux onglets :
    //  - "This base" : une ligne par piste (cap, longueur, où se trouve le point de la base sur la piste)
    //  - "Whole map" : règle de position du point (x, y) de db_airbases pour tout le théâtre
    // La fenêtre ne change rien elle-même : la carte garde le résultat en attente jusqu'au clic sur Save.
    internal class Map_RunwayDialog : Form
    {
        private readonly DataGridView _grid = new DataGridView();
        private readonly double _bx, _by;
        private readonly List<Map_Runway> _initial;
        private readonly string _initialAnchor;
        private readonly RadioButton[] _radios = new RadioButton[5];
        private static readonly string[] AnchorKeys = { "auto", "middle", "start", "east", "west" };
        private readonly CheckBox _chkWeb = new CheckBox();
        private readonly string _initialSource;

        // Onglet "This base"
        public List<Map_Runway> BaseResult;
        public bool BaseChanged, BaseReset;

        // Onglet "Whole map"
        public string AnchorResult = "auto";
        public bool AnchorChanged;
        public string SourceResult = "db";
        public bool SourceChanged;

        // anchor : réglage de la carte ("auto" ou middle / start / east / west) ; status : règle appliquée en ce moment ; source : "db" ou "web"
        public Map_RunwayDialog(string baseName, double baseX, double baseY, List<Map_Runway> current, string theatre, string anchor, string status, string measured, string source)
        {
            _initialSource = source;
            _bx = baseX;
            _by = baseY;
            _initial = current;
            _initialAnchor = anchor;

            Text = "Runways";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            ClientSize = new Size(540, 560);

            var tabs = new TabControl { Dock = DockStyle.Fill };
            var tabBase = new TabPage("This base" + (baseName != null ? " (" + baseName + ")" : ""));
            var tabMap = new TabPage("Whole map" + (string.IsNullOrEmpty(theatre) ? "" : " (" + theatre + ")"));
            tabs.TabPages.Add(tabBase);
            tabs.TabPages.Add(tabMap);

            // ---------- Onglet "This base" ----------
            var info = new Label
            {
                Text = baseName == null ? "Click a base on the map first (then press # again) to type its runways."
                     : "One line per runway.\nHeading: direction of the runway, 0 = north, 90 = east.   Length in metres.\n"
                     + "Base point at: where the point of the base is on the runway. Middle = the middle; East end / West end = one end (north / south end if the runway runs north-south); Custom = a position you set by dragging, kept as it is.",
                Dock = DockStyle.Top, Height = 78, Padding = new Padding(4, 6, 4, 0)
            };

            _grid.Dock = DockStyle.Fill;
            _grid.AllowUserToAddRows = baseName != null;
            _grid.AllowUserToDeleteRows = baseName != null;
            _grid.Enabled = baseName != null;
            _grid.RowHeadersVisible = false;
            _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            _grid.Columns.Add("hdg", "Heading (deg)");
            _grid.Columns.Add("len", "Length (m)");
            var at = new DataGridViewComboBoxColumn { Name = "at", HeaderText = "Base point at", FlatStyle = FlatStyle.Flat };
            at.Items.AddRange("Middle", "East end", "West end", "Custom");
            _grid.Columns.Add(at);

            foreach (Map_Runway r in current)
            {
                double h = ((r.Hdg % 180) + 180) % 180, half = r.Length / 2;
                double dx = (r.X == 0 && r.Y == 0) ? 0 : r.X - baseX, dy = (r.X == 0 && r.Y == 0) ? 0 : r.Y - baseY;
                double ux = Math.Cos(h * Math.PI / 180), uy = Math.Sin(h * Math.PI / 180);       // vers le bout est (ou nord)
                double along = dx * ux + dy * uy, perp = -dx * uy + dy * ux;                        // along > 0 : le milieu de la piste est du côté est du point de la base

                string kind = "Custom";
                if (Math.Abs(perp) < 25)
                {
                    if (Math.Abs(along) < 25) kind = "Middle";
                    else if (Math.Abs(along + half) < 40) kind = "East end";        // milieu = point - moitie vers l'est : le point est au bout est
                    else if (Math.Abs(along - half) < 40) kind = "West end";
                }
                int i = _grid.Rows.Add(Math.Round(h, 1), Math.Round(r.Length), kind);
                _grid.Rows[i].Tag = new[] { dx, dy };
            }

            var reset = new Button { Text = "Back to the original runways of this base", Dock = DockStyle.Bottom, Height = 28, Enabled = baseName != null };
            reset.Click += (s, e) => { BaseReset = true; DialogResult = DialogResult.OK; };

            tabBase.Controls.Add(_grid);
            tabBase.Controls.Add(reset);
            tabBase.Controls.Add(info);

            // ---------- Onglet "Whole map" ----------
            var mapInfo = new Label
            {
                Text = "Where is the point (x, y) of db_airbases on a runway? The answer is the same for all the runways of this map: each runway is moved "
                     + "by HALF ITS OWN LENGTH along its own heading (so a base with several runways of different headings or lengths is handled runway by runway). "
                     + "A base with a setting of its own (tab \"This base\") is left alone.",
                Dock = DockStyle.Top, Height = 78, Padding = new Padding(4, 6, 4, 0)
            };
            string[] texts =
            {
                "Automatic: the rule of the data file for this map, else the measure below, else middle",
                "In the middle of the runway",
                "At the START of the runway (threshold): the runway goes from this point in the direction of its heading",
                "At the east end of the runway (north end if it runs north-south)",
                "At the west end of the runway (south end if it runs north-south)"
            };
            var radioPanel = new Panel { Dock = DockStyle.Top, Height = 160, Padding = new Padding(8, 4, 4, 0) };
            for (int i = texts.Length - 1; i >= 0; i--)
            {
                _radios[i] = new RadioButton { Text = texts[i], Dock = DockStyle.Top, Height = 30, Checked = AnchorKeys[i] == anchor };
                radioPanel.Controls.Add(_radios[i]);
            }
            if (!_radios.Any(r => r.Checked)) _radios[0].Checked = true;

            _chkWeb.Text = "Use the WEB runways (OurAirports) for the whole map. They replace db_airbases and the runway settings of all the bases of this map "
                         + "(cleared when you click Save). A base the web does not know keeps db_airbases.";
            _chkWeb.Checked = source == "web";
            _chkWeb.Dock = DockStyle.Top;
            _chkWeb.Height = 58;
            _chkWeb.Padding = new Padding(8, 4, 4, 0);

            var measure = new Label
            {
                Text = status + "\n\n" + (string.IsNullOrEmpty(measured) ? "Measure: not available yet (it comes with the web map calibration, which needs the internet)." : "Measure: " + measured)
                     + "\n\nHeadings of db_airbases are read in degrees (0 = north, 90 = east). Check it on the map: the drawn runway must lie along the real one.",
                Dock = DockStyle.Fill, Padding = new Padding(4, 8, 4, 0)
            };
            tabMap.Controls.Add(measure);
            tabMap.Controls.Add(_chkWeb);
            tabMap.Controls.Add(radioPanel);
            tabMap.Controls.Add(mapInfo);

            // ---------- Boutons ----------
            var bottom = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 40, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(6) };
            var ok = new Button { Text = "OK", Width = 80 };
            var cancel = new Button { Text = "Cancel", Width = 80, DialogResult = DialogResult.Cancel };
            ok.Click += (s, e) => OnOk(baseName != null);
            bottom.Controls.Add(ok);
            bottom.Controls.Add(cancel);

            Controls.Add(tabs);
            Controls.Add(bottom);
            AcceptButton = ok;
            CancelButton = cancel;
        }

        private void OnOk(bool hasBase)
        {
            for (int i = 0; i < _radios.Length; i++)
                if (_radios[i].Checked) AnchorResult = AnchorKeys[i];
            AnchorChanged = AnchorResult != _initialAnchor;
            SourceResult = _chkWeb.Checked ? "web" : "db";
            SourceChanged = SourceResult != _initialSource;

            if (hasBase)
            {
                _grid.EndEdit();
                var list = new List<Map_Runway>();
                foreach (DataGridViewRow row in _grid.Rows)
                {
                    if (row.IsNewRow) continue;
                    double h, l;
                    string sh = Convert.ToString(row.Cells[0].Value) ?? "", sl = Convert.ToString(row.Cells[1].Value) ?? "";
                    if (!double.TryParse(sh.Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out h) || h < 0 || h > 360
                        || !double.TryParse(sl.Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out l) || l < 50 || l > 10000)
                    {
                        MessageBox.Show("Line " + (row.Index + 1) + ": heading must be 0 to 360 and length 50 to 10000 m.", "Runways");
                        return;
                    }

                    string at = Convert.ToString(row.Cells[2].Value) ?? "Middle";
                    double hm = ((h % 180) + 180) % 180;
                    double ux = Math.Cos(hm * Math.PI / 180), uy = Math.Sin(hm * Math.PI / 180), half = l / 2;
                    double dx = 0, dy = 0;
                    if (at == "East end") { dx = -half * ux; dy = -half * uy; }          // le point est au bout est : le milieu est vers l'ouest
                    else if (at == "West end") { dx = half * ux; dy = half * uy; }
                    else if (at == "Custom")
                    {
                        double[] t = row.Tag as double[];
                        if (t != null) { dx = t[0]; dy = t[1]; }
                    }
                    list.Add(new Map_Runway { Hdg = h, Length = l, X = _bx + dx, Y = _by + dy });
                }

                // Rien n'a changé : on ne marque rien comme modifié
                BaseChanged = !SameRunways(list, _initial);
                BaseResult = list;
                if (list.Count == 0 && BaseChanged)
                {
                    MessageBox.Show("Add at least one runway, or use the button \"Back to the original runways\".", "Runways");
                    return;
                }
            }
            DialogResult = DialogResult.OK;
        }

        private static bool SameRunways(List<Map_Runway> a, List<Map_Runway> b)
        {
            if (a.Count != b.Count) return false;
            for (int i = 0; i < a.Count; i++)
            {
                double bx = (b[i].X == 0 && b[i].Y == 0) ? 0 : b[i].X, by = (b[i].X == 0 && b[i].Y == 0) ? 0 : b[i].Y;
                if (Math.Abs(a[i].Hdg - b[i].Hdg) > 0.6 || Math.Abs(a[i].Length - b[i].Length) > 1.5) return false;
                if (!(b[i].X == 0 && b[i].Y == 0) && (Math.Abs(a[i].X - bx) > 45 || Math.Abs(a[i].Y - by) > 45)) return false;      // tolérance : "Middle" est pris pour milieu à 25 m près
            }
            return true;
        }
    }
}
