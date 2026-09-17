using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using System.Linq;

namespace DCE_Manager
{
    // Composant de rendu de la carte wargame : image de fond + zones dessinées
    // par-dessus (cercle ou polygone selon WargameZoneData.Shape), colorées
    // selon leur Control.
    //
    // Deux usages du même composant (ReadOnly décide) :
    //  - campaignMaker (ReadOnly = false) : clic sur une zone -> ZoneClicked,
    //    plus un mode "pick" pour aider à la calibration (clic -> pixel brut).
    //  - vue joueur (ReadOnly = true) : affichage seul, aucun clic actif.
    //
    // Pourquoi pas de zoom/pan ici : le contrôle prend la taille exacte de
    // l'image de fond (voir LoadMap), donc les pixels cliqués correspondent
    // 1:1 aux pixels utilisés par la calibration. Si l'image est grande,
    // on met ce contrôle dans un Panel avec AutoScroll = true côté Form.
    internal class ucWargameMapView : UserControl
    {
        // Représentation déjà convertie en pixels d'une zone, prête à être
        // dessinée/testée au clic sans repasser par la calibration à chaque fois.
        private class PixelShape
        {
            public WargameZoneShape Shape;
            public PointF[] PolygonPoints;  // Shape == Polygon
            public PointF CircleCenter;     // Shape == Circle
            public float CircleRadius;      // Shape == Circle
        }

        private Image _backgroundImage;
        private WargameMapCalibration _calibration;
        private List<WargameZoneData> _zones = new List<WargameZoneData>();

        private Dictionary<WargameZoneData, PixelShape> _zonePixelShapes = new Dictionary<WargameZoneData, PixelShape>();

        private List<WargameObjective> _objectives = new List<WargameObjective>();
        private Dictionary<WargameObjective, PointF> _objectivePixelPositions = new Dictionary<WargameObjective, PointF>();

        public void SetObjectives(List<WargameObjective> objectives)
        {
            _objectives = objectives ?? new List<WargameObjective>();
            RebuildObjectivePixelPositions();
            Invalidate();
        }

        private void RebuildObjectivePixelPositions()
        {
            _objectivePixelPositions.Clear();

            if (_calibration == null || !_calibration.IsCalibrated)
                return;

            foreach (WargameObjective obj in _objectives)
                _objectivePixelPositions[obj] = _calibration.DcsToPixel(obj.Position);
        }

        // Unités de la mission générée (Active/last_Mission.lua), affichées en points.
        // Purement indicatif : n'alimente rien du modèle de jeu.
        private List<WargameMissionUnit> _missionUnits = new List<WargameMissionUnit>();
        private readonly List<KeyValuePair<WargameMissionUnit, PointF>> _missionUnitPixels = new List<KeyValuePair<WargameMissionUnit, PointF>>();

        // Affichage activable : sur une grosse mission ça fait beaucoup de points.
        public bool ShowMissionUnits { get; set; } = true;

        public int MissionUnitCount => _missionUnits.Count;

        public void SetMissionUnits(List<WargameMissionUnit> units)
        {
            _missionUnits = units ?? new List<WargameMissionUnit>();
            RebuildMissionUnitPixels();
            Invalidate();
        }

        private void RebuildMissionUnitPixels()
        {
            _missionUnitPixels.Clear();

            if (_calibration == null || !_calibration.IsCalibrated)
                return;

            foreach (WargameMissionUnit unit in _missionUnits)
            {
                _missionUnitPixels.Add(new KeyValuePair<WargameMissionUnit, PointF>(
                    unit, _calibration.DcsToPixel(unit.Position)));
            }
        }

        private WargameZoneData _selectedZone;

        //// Assignée par la Form au clic. Redessine automatiquement.
        //public WargameZoneData SelectedZone
        //{
        //    get { return _selectedZone; }
        //    set { _selectedZone = value; Invalidate(); }
        //}

        // Zones actuellement sélectionnées. Un clic simple vide l'ensemble et n'y
        // met que la zone cliquée ; Ctrl+clic ajoute/retire la zone cliquée sans
        // toucher au reste - sélection multiple façon Explorateur Windows.
        private readonly HashSet<WargameZoneData> _selectedZones = new HashSet<WargameZoneData>();

        private WargameObjective _selectedObjective;

        public event Action<WargameObjective> ObjectiveClicked;

        public IReadOnlyCollection<WargameZoneData> SelectedZones => _selectedZones;

        public void ClearSelection()
        {
            if (_selectedZones.Count == 0) return;

            _selectedZones.Clear();
            Invalidate();
            SelectionChanged?.Invoke(new List<WargameZoneData>());
        }

        private bool _pickingCalibrationPoint = false;

        // Zoom molette. Tout le reste du composant (calibration, polygones, objectifs,
        // hit-test) raisonne en pixels IMAGE : le zoom n'intervient qu'au dessin et à la
        // conversion des coordonnées souris. La calibration reste donc valable à tout zoom.
        private float _zoom = 1.0f;
        private const float MinZoom = 0.15f;
        private const float MaxZoom = 6.0f;
        private const float ZoomStep = 1.15f;

        public float Zoom => _zoom;

        // Drag-to-pan : clic maintenu + déplacement = on scrolle la carte dans le
        // Panel parent (celui avec AutoScroll = true) plutôt que d'ouvrir une zone.
        // En dessous du seuil, c'est un vrai clic (sélection/pick), pas un déplacement.
        private const int DragThreshold = 4;
        private Point _mouseDownScreenPoint;
        private Point _mouseDownClientPoint;
        private Point _mouseDownScrollPosition;
        private bool _isDragging;
        private bool _wasDragging;

        // true = vue joueur (aucun clic actif), false = vue campaignMaker
        public bool ReadOnly { get; set; } = false;

        // Couleur par valeur de Control (valeurs internes WargameSide.*, pas les
        // libellés affichés). Modifiable à la volée si un campaignMaker veut
        // d'autres couleurs pour son conflit.
        public Dictionary<string, Color> ControlColors { get; set; } = new Dictionary<string, Color>(StringComparer.OrdinalIgnoreCase)
        {
            { WargameSide.Blue, Color.DodgerBlue },
            { WargameSide.Red, Color.Firebrick },
            { WargameSide.Contested, Color.Orange },
        };

        // Zone cliquée (seulement si ReadOnly = false et hors mode calibration)
        //public event Action<WargameZoneData> ZoneClicked;

        // Ensemble des zones sélectionnées, à chaque changement (clic simple ou
        // Ctrl+clic). Seulement si ReadOnly = false et hors mode calibration.
        public event Action<List<WargameZoneData>> SelectionChanged;

        // Pixel brut cliqué pendant un BeginPickCalibrationPoint() (indépendant de ReadOnly,
        // utilisé par l'outil de calibration du campaignMaker)
        public event Action<PointF> CalibrationPointPicked;

        // Position pixel courante de la souris sur la carte, à chaque déplacement,
        // sans clic (sert à afficher les coordonnées en live pour diagnostiquer).
        public event Action<PointF> MapMouseMoved;

        public ucWargameMapView()
        {
            SetStyle(
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.UserPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw,
                true);

            // Selectable = true : nécessaire pour recevoir les événements molette (zoom).
            // Le recentrage intempestif au clic qui posait problème est neutralisé en
            // amont par NoAutoScrollPanel, qui bloque ScrollControlIntoView.
            SetStyle(ControlStyles.Selectable, true);

            BackColor = Color.Black;
        }

        // Charge (ou recharge) tout ce qu'il faut pour afficher la carte.
        // A appeler à chaque fois que l'image, la calibration ou les zones changent.
        public void LoadMap(Image backgroundImage, WargameMapCalibration calibration, List<WargameZoneData> zones)
        {
            _backgroundImage = backgroundImage;
            _calibration = calibration;
            _zones = zones ?? new List<WargameZoneData>();

            ApplyZoomSize();

            RebuildZonePixelShapes();
            RebuildObjectivePixelPositions();
            RebuildMissionUnitPixels();
            Invalidate();
        }

        private void RebuildZonePixelShapes()
        {
            _zonePixelShapes.Clear();

            if (_calibration == null || !_calibration.IsCalibrated)
                return;

            double scale = _calibration.GetScale();

            foreach (WargameZoneData zone in _zones)
            {
                var pixelShape = new PixelShape { Shape = zone.Shape };

                if (zone.Shape == WargameZoneShape.Polygon)
                {
                    var points = new PointF[zone.DcsPoints.Count];

                    for (int i = 0; i < zone.DcsPoints.Count; i++)
                        points[i] = _calibration.DcsToPixel(zone.DcsPoints[i]);

                    pixelShape.PolygonPoints = points;
                }
                else
                {
                    pixelShape.CircleCenter = _calibration.DcsToPixel(zone.Center);
                    pixelShape.CircleRadius = (float)(zone.Radius * scale);
                }

                _zonePixelShapes[zone] = pixelShape;
            }
        }

        // -------------------- Mode calibration --------------------

        // Passe le composant en mode "prochain clic = point de calibration".
        // Le clic suivant (quel que soit ReadOnly) déclenche CalibrationPointPicked
        // avec le pixel brut, et ne sélectionne aucune zone.
        public void BeginPickCalibrationPoint()
        {
            _pickingCalibrationPoint = true;
            Cursor = Cursors.Cross;
        }

        public void CancelPickCalibrationPoint()
        {
            _pickingCalibrationPoint = false;
            Cursor = Cursors.Default;
        }

        // -------------------- Zoom molette --------------------

        private void ApplyZoomSize()
        {
            if (_backgroundImage == null) return;

            ClientSize = new Size(
                (int)Math.Round(_backgroundImage.Width * _zoom),
                (int)Math.Round(_backgroundImage.Height * _zoom));
        }

        // Convertit un point du contrôle (pixels écran) en pixel de l'image d'origine.
        // C'est cette coordonnée-là que comprennent la calibration et les polygones.
        private PointF ClientToImage(float clientX, float clientY)
        {
            return new PointF(clientX / _zoom, clientY / _zoom);
        }

        // Revient à l'échelle 1:1 (utile quand on s'est perdu à force de zoomer)
        public void ResetZoom()
        {
            _zoom = 1.0f;
            ApplyZoomSize();
            Invalidate();
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            // Sans ça, l'événement remonte au Panel parent qui fait défiler la vue
            // EN PLUS du zoom : les deux se superposent et la carte part en vrille.
            if (e is HandledMouseEventArgs handledArgs)
                handledArgs.Handled = true;

            base.OnMouseWheel(e);

            if (_backgroundImage == null) return;

            float oldZoom = _zoom;
            float newZoom = e.Delta > 0 ? _zoom * ZoomStep : _zoom / ZoomStep;

            if (newZoom < MinZoom) newZoom = MinZoom;
            if (newZoom > MaxZoom) newZoom = MaxZoom;

            if (Math.Abs(newZoom - oldZoom) < 0.0001f) return;

            // Point de l'image actuellement sous le curseur : on veut qu'il y reste
            // après le zoom, sinon la carte fuit sous la souris.
            PointF imagePoint = ClientToImage(e.X, e.Y);

            // Position du curseur dans la zone visible du panel (Left est négatif
            // quand le contrôle est déjà scrollé vers la gauche).
            int viewportX = e.X + Left;
            int viewportY = e.Y + Top;

            _zoom = newZoom;
            ApplyZoomSize();

            if (Parent is Panel scrollPanel && scrollPanel.AutoScroll)
            {
                scrollPanel.AutoScrollPosition = new Point(
                    (int)Math.Round(imagePoint.X * _zoom) - viewportX,
                    (int)Math.Round(imagePoint.Y * _zoom) - viewportY);
            }

            Invalidate();
        }

        // -------------------- Clic souris / drag-to-pan --------------------

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);

            // Sans le focus, le contrôle ne recevrait pas les événements molette
            if (!Focused) Focus();

            _mouseDownScreenPoint = Cursor.Position;
            _mouseDownClientPoint = new Point(e.X, e.Y);
            _isDragging = false;

            if (Parent is Panel scrollPanel && scrollPanel.AutoScroll)
            {
                // AutoScrollPosition renvoie des valeurs négatives, on les repasse
                // en positif pour manipuler plus simplement le delta ensuite.
                _mouseDownScrollPosition = new Point(-scrollPanel.AutoScrollPosition.X, -scrollPanel.AutoScrollPosition.Y);
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);

            MapMouseMoved?.Invoke(ClientToImage(e.X, e.Y));

            if (e.Button != MouseButtons.Left)
                return;

            Point current = Cursor.Position;
            int dx = current.X - _mouseDownScreenPoint.X;
            int dy = current.Y - _mouseDownScreenPoint.Y;

            if (!_isDragging && (Math.Abs(dx) > DragThreshold || Math.Abs(dy) > DragThreshold))
            {
                _isDragging = true;
                _wasDragging = true;
                Cursor = Cursors.SizeAll;
            }

            if (_isDragging && Parent is Panel scrollPanel && scrollPanel.AutoScroll)
            {
                scrollPanel.AutoScrollPosition = new Point(
                    _mouseDownScrollPosition.X - dx,
                    _mouseDownScrollPosition.Y - dy);
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);

            _isDragging = false;

            if (!_pickingCalibrationPoint)
                Cursor = Cursors.Default;
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);

            if (_wasDragging)
            {
                // On vient de déplacer la carte, pas de faire un vrai clic de sélection
                _wasDragging = false;
                return;
            }

            // On se fie à la position mémorisée au MouseDown : celle de l'événement
            // Click est relative à la position courante du contrôle, qui a pu bouger
            // entre-temps (scroll), ce qui décalerait le point retenu.
            PointF clickPoint = ClientToImage(_mouseDownClientPoint.X, _mouseDownClientPoint.Y);

            if (_pickingCalibrationPoint)
            {
                _pickingCalibrationPoint = false;
                Cursor = Cursors.Default;
                CalibrationPointPicked?.Invoke(clickPoint);
                return;
            }

            WargameObjective clickedObjective = HitTestObjective(clickPoint);

            if (clickedObjective != null)
            {
                _selectedObjective = clickedObjective;
                _selectedZones.Clear();
                Invalidate();
                ObjectiveClicked?.Invoke(clickedObjective);
                return;
            }

            _selectedObjective = null;

            if (ReadOnly)
                return;

            WargameZoneData clickedZone = HitTestZone(clickPoint);
            if (clickedZone == null)
                return;

            bool multiSelect = (ModifierKeys & Keys.Control) == Keys.Control;

            if (multiSelect)
            {
                // Ctrl+clic : bascule la zone dans/hors de la sélection, sans
                // toucher au reste.
                if (!_selectedZones.Remove(clickedZone))
                    _selectedZones.Add(clickedZone);
            }
            else
            {
                _selectedZones.Clear();
                _selectedZones.Add(clickedZone);
            }

            Invalidate();
            SelectionChanged?.Invoke(_selectedZones.ToList());
        }

        private WargameZoneData HitTestZone(PointF pixelPoint)
        {
            foreach (var kvp in _zonePixelShapes)
            {
                PixelShape shape = kvp.Value;

                bool hit = shape.Shape == WargameZoneShape.Polygon
                    ? IsPointInPolygon(pixelPoint, shape.PolygonPoints)
                    : IsPointInCircle(pixelPoint, shape.CircleCenter, shape.CircleRadius);

                if (hit)
                    return kvp.Key;
            }

            return null;
        }

        // Algorithme classique "ray casting" (point dans un polygone quelconque)
        private static bool IsPointInPolygon(PointF point, PointF[] polygon)
        {
            if (polygon == null || polygon.Length < 3)
                return false;

            bool inside = false;
            int j = polygon.Length - 1;

            for (int i = 0; i < polygon.Length; i++)
            {
                bool crosses =
                    (polygon[i].Y > point.Y) != (polygon[j].Y > point.Y) &&
                    point.X < (polygon[j].X - polygon[i].X) * (point.Y - polygon[i].Y)
                        / (polygon[j].Y - polygon[i].Y) + polygon[i].X;

                if (crosses)
                    inside = !inside;

                j = i;
            }

            return inside;
        }

        private static bool IsPointInCircle(PointF point, PointF center, float radius)
        {
            double dx = point.X - center.X;
            double dy = point.Y - center.Y;
            return (dx * dx + dy * dy) <= (double)radius * radius;
        }

        // -------------------- Rendu --------------------

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

            if (_backgroundImage == null)
            {
                DrawCenteredMessage(e.Graphics, "No map image loaded");
                return;
            }

            // Tout est dessiné en coordonnées IMAGE, le zoom est appliqué globalement
            // par la matrice : pas besoin de multiplier chaque point à la main.
            GraphicsState state = e.Graphics.Save();
            e.Graphics.ScaleTransform(_zoom, _zoom);

            e.Graphics.DrawImage(_backgroundImage, 0, 0, _backgroundImage.Width, _backgroundImage.Height);

            if (_calibration != null && _calibration.IsCalibrated)
            {
                foreach (var kvp in _zonePixelShapes)
                {
                    DrawZone(e.Graphics, kvp.Key, kvp.Value);
                }

                if (ShowMissionUnits)
                    DrawMissionUnits(e.Graphics);

                foreach (WargameObjective obj in _objectives)
                {
                    if (!_objectivePixelPositions.TryGetValue(obj, out PointF p))
                        continue;

                    DrawObjectiveMarker(e.Graphics, obj, p);
                }

                if (_selectedObjective != null && _objectivePixelPositions.TryGetValue(_selectedObjective, out PointF selPos))
                {
                    // Rayon et épaisseur divisés par le zoom : le marqueur garde la même
                    // taille à l'écran quel que soit le grossissement.
                    using (var ring = new Pen(Color.Yellow, 2.5f / _zoom))
                    {
                        float r = (ObjectiveMarkerRadius + 4f) / _zoom;
                        e.Graphics.DrawEllipse(ring, selPos.X - r, selPos.Y - r, r * 2, r * 2);
                    }
                }

                foreach (WargameZoneData selected in _selectedZones)
                {
                    if (_zonePixelShapes.TryGetValue(selected, out PixelShape selectedShape))
                        DrawSelectionHighlight(e.Graphics, selectedShape);
                }
            }

            e.Graphics.Restore(state);

            // Message hors transformation, pour rester lisible quel que soit le zoom
            if (_calibration == null || !_calibration.IsCalibrated)
            {
                DrawCenteredMessage(e.Graphics, "Map not calibrated yet");
            }
        }

        private void DrawSelectionHighlight(Graphics g, PixelShape pixelShape)
        {
            using (var highlightPen = new Pen(Color.Yellow, 3f / _zoom) { DashStyle = DashStyle.Dash })
            {
                if (pixelShape.Shape == WargameZoneShape.Polygon && pixelShape.PolygonPoints != null && pixelShape.PolygonPoints.Length >= 3)
                {
                    g.DrawPolygon(highlightPen, pixelShape.PolygonPoints);
                }
                else if (pixelShape.Shape != WargameZoneShape.Polygon)
                {
                    float r = pixelShape.CircleRadius;
                    var rect = new RectangleF(pixelShape.CircleCenter.X - r, pixelShape.CircleCenter.Y - r, r * 2, r * 2);
                    g.DrawEllipse(highlightPen, rect);
                }
            }
        }

        // Taille du point d'une unité, en pixels ECRAN (divisée par le zoom au dessin).
        // Assez gros pour être repérable sur une carte de théâtre, où les unités d'un
        // même groupe sont espacées de quelques dizaines de mètres seulement.
        private const float MissionUnitDotSize = 7f;

        // Un point par unité wargame posée dans la mission. Couleur selon le camp,
        // contour noir pour rester visible sur un fond clair comme sur un fond foncé.
        private void DrawMissionUnits(Graphics g)
        {
            if (_missionUnitPixels.Count == 0) return;

            float size = MissionUnitDotSize / _zoom;
            float half = size / 2f;

            using (var blueBrush = new SolidBrush(Color.Cyan))
            using (var redBrush = new SolidBrush(Color.Magenta))
            using (var neutralBrush = new SolidBrush(Color.White))
            using (var outline = new Pen(Color.Black, 1f / _zoom))
            {
                foreach (var kvp in _missionUnitPixels)
                {
                    WargameMissionUnit unit = kvp.Key;
                    PointF p = kvp.Value;

                    SolidBrush brush =
                        unit.Side == "blue" ? blueBrush :
                        unit.Side == "red" ? redBrush : neutralBrush;

                    var rect = new RectangleF(p.X - half, p.Y - half, size, size);

                    // Carré pour les statiques, rond pour les unités mobiles :
                    // ça distingue les deux sans avoir à jouer sur l'opacité, qui
                    // devenait illisible sur les fonds clairs.
                    if (unit.IsStatic)
                    {
                        g.FillRectangle(brush, rect);
                        g.DrawRectangle(outline, rect.X, rect.Y, rect.Width, rect.Height);
                    }
                    else
                    {
                        g.FillEllipse(brush, rect);
                        g.DrawEllipse(outline, rect);
                    }
                }
            }
        }

        private const float ObjectiveMarkerRadius = 6f;

        private void DrawObjectiveMarker(Graphics g, WargameObjective obj, PointF center)
        {
            // Tailles divisées par le zoom pour rester constantes à l'écran : un marqueur
            // est un repère d'interface, pas un objet de la carte, il ne doit pas grossir.
            float radius = ObjectiveMarkerRadius / _zoom;

            var rect = new RectangleF(center.X - radius, center.Y - radius, radius * 2, radius * 2);

            using (var fill = new SolidBrush(Color.OrangeRed))
            using (var outline = new Pen(Color.White, 1.5f / _zoom))
            {
                g.FillEllipse(fill, rect);
                g.DrawEllipse(outline, rect);
            }

            using (var font = new Font("Segoe UI", 7.5f / _zoom))
            using (var textBrush = new SolidBrush(Color.White))
            using (var shadowBrush = new SolidBrush(Color.Black))
            {
                float shadow = 1f / _zoom;
                var textPos = new PointF(center.X + radius + (2f / _zoom), center.Y - (7f / _zoom));
                g.DrawString(obj.Name, font, shadowBrush, textPos.X + shadow, textPos.Y + shadow);
                g.DrawString(obj.Name, font, textBrush, textPos.X, textPos.Y);
            }
        }


        private const float ObjectiveHitTolerance = 10f;

        private WargameObjective HitTestObjective(PointF clickPointPixel)
        {
            foreach (var kvp in _objectivePixelPositions)
            {
                float dx = kvp.Value.X - clickPointPixel.X;
                float dy = kvp.Value.Y - clickPointPixel.Y;

                // Tolérance ramenée en pixels image : viser un marqueur doit rester
                // aussi facile quel que soit le zoom.
                float tolerance = ObjectiveHitTolerance / _zoom;

                if (dx * dx + dy * dy <= tolerance * tolerance)
                    return kvp.Key;
            }

            return null;
        }

        private void DrawZone(Graphics g, WargameZoneData zone, PixelShape pixelShape)
        {
            Color zoneColor = GetZoneColor(zone.Control);
            bool isSelected = _selectedZones.Contains(zone);

            using (var fillBrush = new SolidBrush(Color.FromArgb(isSelected ? 140 : 90, zoneColor)))
            using (var outlinePen = new Pen(zoneColor, (isSelected ? 3f : 2f) / _zoom))
            {
                if (pixelShape.Shape == WargameZoneShape.Polygon)
                {
                    if (pixelShape.PolygonPoints == null || pixelShape.PolygonPoints.Length < 3)
                        return;

                    g.FillPolygon(fillBrush, pixelShape.PolygonPoints);
                    g.DrawPolygon(outlinePen, pixelShape.PolygonPoints);
                }
                else
                {
                    float r = pixelShape.CircleRadius;
                    var rect = new RectangleF(pixelShape.CircleCenter.X - r, pixelShape.CircleCenter.Y - r, r * 2, r * 2);

                    g.FillEllipse(fillBrush, rect);
                    g.DrawEllipse(outlinePen, rect);
                }
            }

            DrawZoneLabel(g, zone, pixelShape);
        }

        private void DrawZoneLabel(Graphics g, WargameZoneData zone, PixelShape pixelShape)
        {
            if (string.IsNullOrEmpty(zone.Id))
                return;

            PointF anchor = pixelShape.Shape == WargameZoneShape.Polygon
                ? GetCentroid(pixelShape.PolygonPoints)
                : pixelShape.CircleCenter;

            using (var font = new Font("Segoe UI", 9f / _zoom, FontStyle.Bold))
            using (var textBrush = new SolidBrush(Color.White))
            {
                SizeF textSize = g.MeasureString(zone.Id, font);
                g.DrawString(zone.Id, font, textBrush, anchor.X - textSize.Width / 2f, anchor.Y - textSize.Height / 2f);
            }
        }

        private static PointF GetCentroid(PointF[] points)
        {
            if (points == null || points.Length == 0)
                return PointF.Empty;

            float sumX = 0, sumY = 0;

            foreach (PointF p in points)
            {
                sumX += p.X;
                sumY += p.Y;
            }

            return new PointF(sumX / points.Length, sumY / points.Length);
        }

        private Color GetZoneColor(string controlValue)
        {
            if (!string.IsNullOrEmpty(controlValue) && ControlColors.TryGetValue(controlValue, out Color color))
                return color;

            return Color.Gray; // faction inconnue / Control pas encore renseigné -> gris par défaut
        }

        private void DrawCenteredMessage(Graphics g, string message)
        {
            using (var font = new Font("Segoe UI", 12f))
            using (var brush = new SolidBrush(Color.White))
            {
                SizeF textSize = g.MeasureString(message, font);
                float x = (ClientSize.Width - textSize.Width) / 2f;
                float y = (ClientSize.Height - textSize.Height) / 2f;
                g.DrawString(message, font, brush, Math.Max(0, x), Math.Max(0, y));
            }
        }
    }

    // Panel avec AutoScroll, mais qui ne se repositionne PAS tout seul quand un
    // contrôle enfant devient actif. Sans ça, cliquer sur la carte déclenche
    // ScrollControlIntoView : la vue saute (retour au centre) juste avant que le
    // clic ne soit traité, et le pixel retenu ne correspond plus à ce qu'on visait.
    // Le scroll manuel (molette, barres, drag) fonctionne normalement.
    internal class NoAutoScrollPanel : Panel
    {
        protected override Point ScrollToControl(Control activeControl)
        {
            return DisplayRectangle.Location; // on garde la position courante
        }
    }


}
