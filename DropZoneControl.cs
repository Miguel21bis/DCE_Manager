using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace DCE_Manager.Controls
{
    /// <summary>
    /// Zone de "Drag & Drop" pour fichiers ZIP, inspirée de l'UI de DCE Manager.
    /// - Bordure en pointillés
    /// - Icône + texte "Drag & drop ZIP here / or click to browse"
    /// - Réagit au survol (highlight) et au clic (ouvre un OpenFileDialog)
    /// - Filtre les fichiers .zip lors du drop
    /// </summary>
    public class DropZoneControl : Panel
    {
        // Couleurs alignées sur le thème clair (SystemColors.Control), même esprit que le reste de l'appli
        private Color _borderColor = Color.FromArgb(47, 111, 237);        // bleu, garde le même accent que "Install Campaign"
        private Color _borderHoverColor = Color.FromArgb(90, 150, 255);
        private Color _borderValidColor = Color.FromArgb(46, 160, 67);    // vert, cohérent avec les ✅ de "DCS Installation"
        private Color _backColorNormal = SystemColors.Control;             // gris clair standard Windows (#F0F0F0 environ)
        private Color _backColorHover = ControlPaint.Light(SystemColors.Control, 0.15f);
        private Color _textColorMain = SystemColors.ControlText;           // noir/gris foncé au lieu de blanc
        private Color _textColorSub = Color.FromArgb(90, 90, 90);          // gris moyen pour le sous-texte

        private bool _isHover = false;
        private bool _hasValidFile = false;
        private string _selectedFilePath = "";
        private string _mainText = "Drag & drop ZIP here";
        private string _subText = "or click to browse";
        private string _filter = "Fichiers ZIP (*.zip)|*.zip";
        private Image _customIcon = null;   // Point 3 : icône personnalisée (PNG/etc.), null = icône dessinée par défaut

        /// <summary>Déclenché quand un(des) fichier(s) valide(s) est/sont sélectionné(s) (drop ou dialogue).</summary>
        public event EventHandler<string[]> FilesSelected;

        public DropZoneControl()
        {
            this.SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                          ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);

            this.BackColor = _backColorNormal;
            this.AllowDrop = true;
            this.Cursor = Cursors.Hand;
            this.Size = new Size(600, 140);

            this.DragEnter += DropZoneControl_DragEnter;
            this.DragLeave += (s, e) => { _isHover = false; Invalidate(); };
            this.DragDrop += DropZoneControl_DragDrop;
            this.Click += (s, e) => BrowseForFile();
            this.MouseEnter += (s, e) => { _isHover = true; Invalidate(); };
            this.MouseLeave += (s, e) => { _isHover = false; Invalidate(); };
        }

        [System.ComponentModel.Category("DropZone")]
        public string MainText { get => _mainText; set { _mainText = value; Invalidate(); } }

        [System.ComponentModel.Category("DropZone")]
        public string SubText { get => _subText; set { _subText = value; Invalidate(); } }

        [System.ComponentModel.Category("DropZone")]
        public string FileFilter { get => _filter; set => _filter = value; }

        /// <summary>Icône personnalisée à afficher au centre (remplace l'icône dessinée par défaut).</summary>
        [System.ComponentModel.Category("DropZone")]
        public Image CustomIcon { get => _customIcon; set { _customIcon = value; Invalidate(); } }

        /// <summary>
        /// Marque un fichier comme sélectionné et valide : passe la bordure en vert,
        /// affiche le nom du fichier en texte principal. À appeler depuis votre Form
        /// après réception de FilesSelected, une fois vos propres vérifications faites.
        /// </summary>
        public void SetSelectedFile(string filePath)
        {
            _selectedFilePath = filePath;
            _hasValidFile = !string.IsNullOrEmpty(filePath);

            if (_hasValidFile)
            {
                MainText = Path.GetFileName(filePath);
                SubText = "Campagne détectée — prête à installer (cliquer pour changer)";
            }
            else
            {
                MainText = "Drag & drop ZIP here";
                SubText = "or click to browse";
            }
            Invalidate();
        }

        /// <summary>Réinitialise la zone à son état vide initial (ex: après une installation terminée).</summary>
        public void ResetState() => SetSelectedFile(null);

        private void DropZoneControl_DragEnter(object sender, DragEventArgs e)
        {
            DCE_Manager.Utils.FormUtils.LogRegister("DropZoneControl : DragEnter déclenché");

            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                var files = (string[])e.Data.GetData(DataFormats.FileDrop);
                DCE_Manager.Utils.FormUtils.LogRegister($"DropZoneControl : {files.Length} fichier(s) détecté(s) : " + string.Join(" | ", files));

                bool valid = files.Any(f => Path.GetExtension(f).Equals(".zip", StringComparison.OrdinalIgnoreCase));
                e.Effect = valid ? DragDropEffects.Copy : DragDropEffects.None;
                _isHover = valid;

                DCE_Manager.Utils.FormUtils.LogRegister($"DropZoneControl : valid={valid}, e.Effect={e.Effect}");
            }
            else
            {
                DCE_Manager.Utils.FormUtils.LogRegister("DropZoneControl : GetDataPresent(FileDrop) = false");
                e.Effect = DragDropEffects.None;
            }
            Invalidate();
        }

        private void DropZoneControl_DragDrop(object sender, DragEventArgs e)
        {
            DCE_Manager.Utils.FormUtils.LogRegister("DropZoneControl : DragDrop déclenché");

            _isHover = false;
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                var files = (string[])e.Data.GetData(DataFormats.FileDrop);
                var zipFiles = files.Where(f => Path.GetExtension(f).Equals(".zip", StringComparison.OrdinalIgnoreCase)).ToArray();

                DCE_Manager.Utils.FormUtils.LogRegister($"DropZoneControl : {zipFiles.Length} fichier(s) zip valide(s) sur {files.Length} déposé(s)");

                if (zipFiles.Length > 0)
                    FilesSelected?.Invoke(this, zipFiles);
            }
            else
            {
                DCE_Manager.Utils.FormUtils.LogRegister("DropZoneControl : DragDrop, GetDataPresent(FileDrop) = false");
            }
            Invalidate();
        }

        private void BrowseForFile()
        {
            using (var ofd = new OpenFileDialog())
            {
                ofd.Filter = _filter;
                ofd.Multiselect = false;
                ofd.Title = "Sélectionner un fichier";

                if (ofd.ShowDialog() == DialogResult.OK)
                {
                    FilesSelected?.Invoke(this, new[] { ofd.FileName });
                }
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            Color back = _isHover ? _backColorHover : _backColorNormal;
            Color border = _hasValidFile ? _borderValidColor : (_isHover ? _borderHoverColor : _borderColor);

            this.BackColor = back;

            // Bordure en pointillés arrondie
            using (var pen = new Pen(border, 2f))
            {
                pen.DashStyle = DashStyle.Dash;
                pen.DashPattern = new float[] { 6f, 4f };
                var rect = new Rectangle(4, 4, this.Width - 9, this.Height - 9);
                using (var path = RoundedRect(rect, 10))
                {
                    g.DrawPath(pen, path);
                }
            }

            // Icône : personnalisée si fournie (Point 3), sinon dessin par défaut
            int iconSize = 34;
            int iconX = this.Width / 2 - 90;
            int iconY = this.Height / 2 - iconSize / 2;

            if (_customIcon != null)
            {
                g.DrawImage(_customIcon, new Rectangle(iconX, iconY - 3, iconSize, iconSize + 6));
            }
            else
            {
                DrawZipIcon(g, iconX, iconY, iconSize, border);
            }

            // Textes
            using (var mainFont = new Font("Segoe UI", 11f, FontStyle.Bold))
            using (var subFont = new Font("Segoe UI", 9f, FontStyle.Regular))
            using (var mainBrush = new SolidBrush(_textColorMain))
            using (var subBrush = new SolidBrush(_textColorSub))
            {
                var textX = iconX + iconSize + 16;
                var mainSize = g.MeasureString(_mainText, mainFont);
                var subSize = g.MeasureString(_subText, subFont);

                float totalTextHeight = mainSize.Height + subSize.Height + 2;
                float startY = this.Height / 2f - totalTextHeight / 2f;

                g.DrawString(_mainText, mainFont, mainBrush, textX, startY);
                g.DrawString(_subText, subFont, subBrush, textX, startY + mainSize.Height + 2);
            }
        }

        private void DrawZipIcon(Graphics g, int x, int y, int size, Color accent)
        {
            // Corps du "document"
            using (var bodyBrush = new SolidBrush(Color.FromArgb(210, 215, 222)))
            using (var pen = new Pen(accent, 1.5f))
            {
                var body = new Rectangle(x, y, size, size + 6);
                g.FillRectangle(bodyBrush, body);
                g.DrawRectangle(pen, body);

                // Bande centrale façon "zip"
                using (var stripeBrush = new SolidBrush(accent))
                {
                    int stripeW = size / 5;
                    g.FillRectangle(stripeBrush, x + size / 2 - stripeW / 2, y, stripeW, size + 6);
                }
            }
        }

        private static GraphicsPath RoundedRect(Rectangle bounds, int radius)
        {
            int d = radius * 2;
            var path = new GraphicsPath();
            path.AddArc(bounds.X, bounds.Y, d, d, 180, 90);
            path.AddArc(bounds.Right - d, bounds.Y, d, d, 270, 90);
            path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
            path.AddArc(bounds.X, bounds.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }
}
