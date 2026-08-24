using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;

namespace DCE_Manager
{
    // Form dédiée à la calibration DCS <-> pixel d'une campagne wargame.
    //
    // Pour chaque point de référence : coordonnées DCS (X/Y, lues sur la carte F10
    // dans DCS), puis "Pick on map" et clic sur l'image à l'endroit correspondant.
    //
    // Les coordonnées DCS déjà enregistrées sont rechargées au démarrage : en
    // général on ne change que les clics, pas les chiffres.
    //
    // Un affichage live de la position pixel sous la souris (sans clic) permet de
    // vérifier que ce qu'on vise correspond bien à ce que le composant reçoit.
    internal class WargameCalibration_Form : Form
    {
        private readonly ucWargameMapView _mapView;
        private readonly string _calibJsonPath;

        private TextBox _textDcsX1, _textDcsY1;
        private TextBox _textDcsX2, _textDcsY2;
        private Label _labelPixel1, _labelPixel2;
        private Label _labelLivePixel;
        private Label _labelDiagnostics;
        private Button _buttonSave;

        private PointF? _pixel1;
        private PointF? _pixel2;
        private int _pickingRow; // 1 ou 2, 0 = aucun pick en cours

        public WargameCalibration_Form(Image mapImage, string calibJsonPath)
        {
            _calibJsonPath = calibJsonPath;

            Text = "Wargame map calibration";
            Width = 900;
            Height = 700;
            StartPosition = FormStartPosition.CenterParent;
            WindowState = FormWindowState.Maximized;

            _mapView = new ucWargameMapView { ReadOnly = false };
            // Pas encore de calibration ni de zones à ce stade, juste l'image de fond
            _mapView.LoadMap(mapImage, new WargameMapCalibration(), new List<WargameZoneData>());
            _mapView.CalibrationPointPicked += MapView_CalibrationPointPicked;
            _mapView.MapMouseMoved += MapView_MapMouseMoved;

            var mapPanel = new NoAutoScrollPanel { Dock = DockStyle.Fill, AutoScroll = true };
            mapPanel.Controls.Add(_mapView);

            var splitContainer = new SplitContainer { Dock = DockStyle.Fill, FixedPanel = FixedPanel.Panel1 };
            splitContainer.Panel1.Controls.Add(BuildSidePanel());
            splitContainer.Panel2.Controls.Add(mapPanel);

            Controls.Add(splitContainer);

            // SplitterDistance posé dans l'initialiseur ne tient pas : le contrôle n'a
            // pas encore sa taille finale, et le SplitContainer réajuste ensuite la
            // position proportionnellement. On le fixe donc au Load, taille connue.
            Load += (s, e) => splitContainer.SplitterDistance = 280;

            LoadExistingCalibration();
        }

        // Recharge les coordonnées DCS (et les pixels) déjà enregistrées, pour ne pas
        // avoir à retaper les chiffres quand on veut juste refaire les clics.
        private void LoadExistingCalibration()
        {
            WargameMapCalibration existing = WargameMapCalibration.Load(_calibJsonPath);

            if (!existing.IsCalibrated)
                return;

            _textDcsX1.Text = existing.DcsRef1.X.ToString(CultureInfo.InvariantCulture);
            _textDcsY1.Text = existing.DcsRef1.Y.ToString(CultureInfo.InvariantCulture);
            _textDcsX2.Text = existing.DcsRef2.X.ToString(CultureInfo.InvariantCulture);
            _textDcsY2.Text = existing.DcsRef2.Y.ToString(CultureInfo.InvariantCulture);

            _pixel1 = existing.PixelRef1;
            _pixel2 = existing.PixelRef2;
            _labelPixel1.Text = "Position: " + FormatPoint(existing.PixelRef1);
            _labelPixel2.Text = "Position: " + FormatPoint(existing.PixelRef2);

            UpdateSaveButtonState();
        }

        private Control BuildSidePanel()
        {
            var flow = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                Padding = new Padding(10),
            };

            var instructions = new Label
            {
                Text = "For each point: type its DCS X/Y (F10 map in DCS), then click its position on the image.",
                Width = 230,
                Height = 55,
            };
            flow.Controls.Add(instructions);

            _labelLivePixel = new Label
            {
                Text = "Mouse: -",
                Width = 230,
                Height = 22,
                Font = new Font("Segoe UI", 9f, FontStyle.Bold),
            };
            flow.Controls.Add(_labelLivePixel);

            flow.Controls.Add(BuildPointRow(1, out _textDcsX1, out _textDcsY1, out _labelPixel1));
            _labelDiagnostics = new Label
            {
                Text = "",
                Width = 230,
                Height = 110,
                ForeColor = Color.DimGray,
            };
            flow.Controls.Add(_labelDiagnostics);
            flow.Controls.Add(BuildPointRow(2, out _textDcsX2, out _textDcsY2, out _labelPixel2));

            _buttonSave = new Button { Text = "Save calibration", Width = 230, Enabled = false };
            _buttonSave.Click += ButtonSave_Click;
            flow.Controls.Add(_buttonSave);

            return flow;
        }

        private Control BuildPointRow(int rowNumber, out TextBox textX, out TextBox textY, out Label pixelLabel)
        {
            var group = new GroupBox { Text = "Reference point " + rowNumber, Width = 240, Height = 130 };

            var labelX = new Label { Text = "DCS X", Location = new Point(10, 22), AutoSize = true };
            var localTextX = new TextBox { Location = new Point(70, 19), Width = 150 };
            localTextX.TextChanged += (s, e) => UpdateSaveButtonState();

            var labelY = new Label { Text = "DCS Y", Location = new Point(10, 49), AutoSize = true };
            var localTextY = new TextBox { Location = new Point(70, 46), Width = 150 };
            localTextY.TextChanged += (s, e) => UpdateSaveButtonState();

            var localPixelLabel = new Label { Text = "Position: not picked", Location = new Point(10, 76), AutoSize = true };

            var pickButton = new Button { Text = "Pick on map", Location = new Point(10, 98), Width = 200 };
            pickButton.Click += (s, e) => StartPicking(rowNumber);

            group.Controls.Add(labelX);
            group.Controls.Add(localTextX);
            group.Controls.Add(labelY);
            group.Controls.Add(localTextY);
            group.Controls.Add(localPixelLabel);
            group.Controls.Add(pickButton);

            textX = localTextX;
            textY = localTextY;
            pixelLabel = localPixelLabel;

            return group;
        }

        private void MapView_MapMouseMoved(PointF pixelPoint)
        {
            string text = "Mouse: " + FormatPoint(pixelPoint);

            WargameMapCalibration current = BuildCurrentCalibration();

            if (current != null && current.IsCalibrated)
            {
                PointF dcs = current.PixelToDcs(pixelPoint);
                text += "   DCS: " + (int)dcs.X + ", " + (int)dcs.Y;
            }

            _labelLivePixel.Text = text;
        }

        private void StartPicking(int rowNumber)
        {
            _pickingRow = rowNumber;
            _mapView.BeginPickCalibrationPoint();
        }

        private void MapView_CalibrationPointPicked(PointF pixelPoint)
        {
            if (_pickingRow == 1)
            {
                _pixel1 = pixelPoint;
                _labelPixel1.Text = "Position: " + FormatPoint(pixelPoint);
            }
            else if (_pickingRow == 2)
            {
                _pixel2 = pixelPoint;
                _labelPixel2.Text = "Position: " + FormatPoint(pixelPoint);
            }

            _pickingRow = 0;
            UpdateSaveButtonState();
        }

        private void UpdateSaveButtonState()
        {
            WargameMapCalibration current = BuildCurrentCalibration();

            _buttonSave.Enabled = current != null && current.IsCalibrated;

            _labelDiagnostics.Text = current == null ? "" : current.GetDiagnostics();
        }

        // Calibration provisoire construite à partir de ce qui est saisi à
        // l'instant. Sert à la fois au diagnostic affiché et à la lecture des
        // coordonnées DCS sous la souris. Retourne null tant que tout n'est
        // pas renseigné.
        private WargameMapCalibration BuildCurrentCalibration()
        {
            if (!_pixel1.HasValue || !_pixel2.HasValue)
                return null;

            if (!TryParseDouble(_textDcsX1.Text, out double x1) || !TryParseDouble(_textDcsY1.Text, out double y1) ||
                !TryParseDouble(_textDcsX2.Text, out double x2) || !TryParseDouble(_textDcsY2.Text, out double y2))
                return null;

            return new WargameMapCalibration
            {
                DcsRef1 = new PointF((float)x1, (float)y1),
                DcsRef2 = new PointF((float)x2, (float)y2),
                PixelRef1 = _pixel1.Value,
                PixelRef2 = _pixel2.Value,
            };
        }

        private void ButtonSave_Click(object sender, EventArgs e)
        {
            if (!_pixel1.HasValue || !_pixel2.HasValue)
                return;

            if (!TryParseDouble(_textDcsX1.Text, out double x1) || !TryParseDouble(_textDcsY1.Text, out double y1) ||
                !TryParseDouble(_textDcsX2.Text, out double x2) || !TryParseDouble(_textDcsY2.Text, out double y2))
            {
                MessageBox.Show("Invalid DCS coordinates.", "Calibration", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var calibration = new WargameMapCalibration
            {
                DcsRef1 = new PointF((float)x1, (float)y1),
                DcsRef2 = new PointF((float)x2, (float)y2),
                PixelRef1 = _pixel1.Value,
                PixelRef2 = _pixel2.Value,
            };

            calibration.Save(_calibJsonPath);

            MessageBox.Show("Calibration saved.", "Calibration", MessageBoxButtons.OK, MessageBoxIcon.Information);

            DialogResult = DialogResult.OK;
            Close();
        }

        private static bool TryParseDouble(string text, out double value)
        {
            return double.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out value);
        }

        private static string FormatPoint(PointF p)
        {
            return "(" + (int)p.X + ", " + (int)p.Y + ")";
        }
    }
}
