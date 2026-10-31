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
    // Zoom molette + déplacement : le contrôle prend la taille de l'image x zoom (voir
    // ApplyZoomSize). Il remplit son Panel parent et gère lui-même zoom et déplacement.
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

        // Zones actuellement sélectionnées. Un clic simple vide l'ensemble et n'y
        // met que la zone cliquée ; Ctrl+clic ajoute/retire la zone cliquée sans
        // toucher au reste - sélection multiple façon Explorateur Windows.
        private readonly HashSet<WargameZoneData> _selectedZones = new HashSet<WargameZoneData>();

        private WargameObjective _selectedObjective;

        public event Action<WargameObjective> ObjectiveClicked;

        // Clic droit sur un objectif (position écran du clic en second argument) : la Preview y accroche son menu d'événements
        public event Action<WargameObjective, Point> ObjectiveRightClicked;

        // Objectifs détruits dans la simulation : dessinés en gris barrés d'une croix rouge
        private HashSet<WargameObjective> _destroyedObjectives = new HashSet<WargameObjective>();

        public void SetDestroyedObjectives(HashSet<WargameObjective> destroyed)
        {
            _destroyedObjectives = destroyed ?? new HashSet<WargameObjective>();
            Invalidate();
        }

        // ---- Calque "ravitaillement" : les zones sont colorées selon leur niveau (vert = plein, rouge = faible, gris = coupée) ----
        private Dictionary<string, WargameEngineCombat.WargameSupplyInfo> _supplyMap;
        private ToolTip _supplyTip = new ToolTip();
        private string _supplyTipZoneId = "";

        // Convois du tour (Preview) : route en pointillés et point de ravitaillement avancé avec sa réserve. null = rien.
        private List<WargameEngineCombat.WargameConvoy> _convoys;

        public void SetConvoys(List<WargameEngineCombat.WargameConvoy> convoys)
        {
            _convoys = convoys;
            Invalidate();
        }

        // null = calque éteint
        public void SetSupplyOverlay(Dictionary<string, WargameEngineCombat.WargameSupplyInfo> map)
        {
            _supplyMap = map;
            if (map == null)
            {
                _supplyTip.SetToolTip(this, "");
                _supplyTipZoneId = "";
            }
            Invalidate();
        }

        private static Color SupplyColor(double level)
        {
            if (level < 0) level = 0;
            if (level > 1) level = 1;

            // rouge (0) -> jaune (0.5) -> vert (1)
            if (level < 0.5)
                return Color.FromArgb(230, (int)(210 * level * 2), 40);

            return Color.FromArgb((int)(230 * (1.0 - (level - 0.5) * 2)), 200, 40);
        }

        // Texte de l'info-bulle quand on survole une zone colorée
        private void UpdateSupplyTip(PointF imagePoint)
        {
            if (_supplyMap == null) return;

            WargameZoneData zone = HitTestZone(imagePoint);
            string id = zone == null ? "" : zone.Id;
            if (id == _supplyTipZoneId) return;
            _supplyTipZoneId = id;

            WargameEngineCombat.WargameSupplyInfo info;
            if (zone == null || !_supplyMap.TryGetValue(zone.Id, out info))
            {
                _supplyTip.SetToolTip(this, "");
                return;
            }

            string text = zone.Id + " : supply " + (info.Level * 100).ToString("0") + "%"
                + (info.CutOff ? " (CUT OFF)" : "")
                + (info.IsSource ? "\nSupply source here" : "")
                + (string.IsNullOrEmpty(info.Note) ? "" : "\n" + info.Note);
            _supplyTip.SetToolTip(this, text);
        }

        // Légende en bas à gauche (en pixels écran, pas affectée par le zoom)
        private void DrawSupplyLegend(Graphics g)
        {
            if (_supplyMap == null) return;

            int w = 190, h = 92;
            var box = new Rectangle(10, Height - h - 10, w, h);

            if (_supplyMap.Count == 0)
            {
                using (var warnBack = new SolidBrush(Color.FromArgb(220, Color.DarkRed)))
                using (var warnFont = new Font("Segoe UI", 8f, FontStyle.Bold))
                using (var warnText = new SolidBrush(Color.White))
                {
                    g.FillRectangle(warnBack, new Rectangle(box.X, box.Y - 40, 330, 36));
                    g.DrawString("No supply source: give a Supply value to an objective\n(warehouse, port...) to create a supply network.", warnFont, warnText, box.X + 6, box.Y - 37);
                }
            }

            using (var back = new SolidBrush(Color.FromArgb(200, Color.Black)))
                g.FillRectangle(back, box);

            using (var font = new Font("Segoe UI", 8f, FontStyle.Bold))
            using (var white = new SolidBrush(Color.White))
            {
                g.DrawString("Supply quality", font, white, box.X + 6, box.Y + 4);

                for (int i = 0; i < 10; i++)
                    using (var b = new SolidBrush(SupplyColor(i / 9.0)))
                        g.FillRectangle(b, box.X + 6 + i * 17, box.Y + 24, 17, 12);

                g.DrawString("0%", font, white, box.X + 4, box.Y + 38);
                g.DrawString("100%", font, white, box.X + w - 38, box.Y + 38);

                using (var grey = new SolidBrush(Color.FromArgb(90, 90, 90)))
                    g.FillRectangle(grey, box.X + 6, box.Y + 56, 17, 12);
                g.DrawString("cut off      S = source", font, white, box.X + 28, box.Y + 54);

                using (var bluePen = new Pen(GetZoneColor(WargameSide.Blue), 4f))
                using (var redPen = new Pen(GetZoneColor(WargameSide.Red), 4f))
                {
                    g.DrawLine(bluePen, box.X + 6, box.Y + 79, box.X + 26, box.Y + 79);
                    g.DrawLine(redPen, box.X + 30, box.Y + 79, box.X + 50, box.Y + 79);
                }
                g.DrawString("border = side", font, white, box.X + 56, box.Y + 72);
            }
        }

        // ---- Preview du combat : texte sous le nom des zones, zones entourées, points de force ----

        // Texte affiché sous le nom de chaque zone (clé = id de zone)
        private Dictionary<string, string> _zoneCaptions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // Zones à entourer (celles qui viennent de changer de main)
        private HashSet<string> _flashZoneIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Un point par zone occupée : nombre de formations écrit dedans, taille = force
        private Dictionary<string, WargameZoneDot> _zoneDots = new Dictionary<string, WargameZoneDot>(StringComparer.OrdinalIgnoreCase);

        public void SetZoneCaptions(Dictionary<string, string> captions)
        {
            _zoneCaptions = captions ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            Invalidate();
        }

        public void SetFlashZones(IEnumerable<string> zoneIds)
        {
            _flashZoneIds = new HashSet<string>(zoneIds ?? new List<string>(), StringComparer.OrdinalIgnoreCase);
            Invalidate();
        }

        public void SetZoneDots(Dictionary<string, WargameZoneDot> dots)
        {
            _zoneDots = dots ?? new Dictionary<string, WargameZoneDot>(StringComparer.OrdinalIgnoreCase);
            Invalidate();
        }

        // Flèches de combat (preview) : de chaque zone vers les zones ennemies auxquelles elle fait face
        private List<WargameZoneArrow> _zoneArrows = new List<WargameZoneArrow>();

        private bool _showArrows = true;
        public bool ShowArrows
        {
            get { return _showArrows; }
            set { _showArrows = value; Invalidate(); }
        }

        public void SetZoneArrows(List<WargameZoneArrow> arrows)
        {
            _zoneArrows = arrows ?? new List<WargameZoneArrow>();
            Invalidate();
        }

        // ---- Axes de progression ----
        private List<WargameAxis> _axes = new List<WargameAxis>();
        private WargameAxis _selectedAxis;
        private readonly Dictionary<string, PointF> _zoneCenters = new Dictionary<string, PointF>(StringComparer.OrdinalIgnoreCase);
        private Action<WargameZoneData> _zonePickCallback;

        // Mis à true par la Form quand le panneau des axes est ouvert
        private bool _showAxes;
        public bool ShowAxes
        {
            get { return _showAxes; }
            set { _showAxes = value; Invalidate(); }
        }

        // Opacité des traits d'axes : 1 = pleins, 0.3 = transparents (pour voir les points de force dessous)
        private float _axisOpacity = 1f;
        public float AxisOpacity
        {
            get { return _axisOpacity; }
            set { _axisOpacity = Math.Max(0f, Math.Min(1f, value)); Invalidate(); }
        }

        // Zone mise en évidence depuis le panneau des axes (clic sur une ligne)
        private string _axisHighlightZoneId;

        public void HighlightAxisZone(string zoneId)
        {
            _axisHighlightZoneId = zoneId;
            Invalidate();
        }

        // Clic droit pendant un choix de zone : le panneau doit le savoir
        public event Action ZonePickCancelled;

        // ---- Plans de débarquement (boîtes LANDING_n) ----

        // true = on peut glisser une boîte à la souris (éditeur, vue Init seulement : la géométrie
        // n'est enregistrée que dans l'Init). Toujours sans effet en ReadOnly.
        public bool LandingEditable { get; set; }

        // Clic droit dans le vide ou sur une zone ordinaire (pixel image, position écran) : l'éditeur propose "ajouter un plan"
        public event Action<PointF, Point> MapRightClicked;

        // Clic droit sur une boîte de débarquement : l'éditeur propose "changer la plage" / "supprimer"
        public event Action<WargameZoneData, Point> LandingRightClicked;

        // Fin d'un glissement de boîte (elle a bougé)
        public event Action<WargameZoneData> LandingMoved;

        // ---- FARP des plages de débarquement (marqueurs déplaçables, vue Init de l'éditeur) ----
        private List<WargameFarp> _farps = new List<WargameFarp>();
        private WargameFarp _movingFarp;          // FARP attrapée à la souris
        private bool _movingFarpMoved;

        // true = on peut glisser une FARP (éditeur, vue Init). Sans effet en ReadOnly.
        public bool FarpEditable { get; set; }

        // Fin d'un glissement de FARP (elle a bougé)
        public event Action<WargameFarp> FarpMoved;

        // Clic droit sur une FARP : l'éditeur propose de la remettre au milieu de sa plage
        public event Action<WargameFarp, Point> FarpRightClicked;

        public void SetFarps(List<WargameFarp> farps)
        {
            _farps = farps ?? new List<WargameFarp>();
            Invalidate();
        }

        private const float FarpMarkerHalf = 7f;   // demi-côté du carré, en pixels ECRAN

        private WargameFarp HitTestFarp(PointF imagePoint)
        {
            if (_calibration == null || !_calibration.IsCalibrated) return null;

            float tolerance = (FarpMarkerHalf + 3f) / _zoom;

            foreach (WargameFarp farp in _farps)
            {
                PointF p = _calibration.DcsToPixel(new PointF((float)farp.X, (float)farp.Y));
                if (Math.Abs(p.X - imagePoint.X) <= tolerance && Math.Abs(p.Y - imagePoint.Y) <= tolerance)
                    return farp;
            }

            return null;
        }

        // Carré de la couleur du camp avec un "F", et le nom dessous. Tailles divisées par le zoom : constantes à l'écran.
        private void DrawFarps(Graphics g)
        {
            if (_farps.Count == 0) return;

            float half = FarpMarkerHalf / _zoom;

            using (var outline = new Pen(Color.White, 2f / _zoom))
            using (var font = new Font("Segoe UI", 8f / _zoom, FontStyle.Bold))
            using (var textBrush = new SolidBrush(Color.White))
            using (var shadowBrush = new SolidBrush(Color.Black))
            {
                foreach (WargameFarp farp in _farps)
                {
                    PointF p = _calibration.DcsToPixel(new PointF((float)farp.X, (float)farp.Y));
                    var rect = new RectangleF(p.X - half, p.Y - half, half * 2, half * 2);

                    using (var fill = new SolidBrush(GetZoneColor(farp.Side)))
                        g.FillRectangle(fill, rect);

                    g.DrawRectangle(outline, rect.X, rect.Y, rect.Width, rect.Height);

                    SizeF fs = g.MeasureString("F", font);
                    g.DrawString("F", font, textBrush, p.X - fs.Width / 2f, p.Y - fs.Height / 2f);

                    SizeF ns = g.MeasureString(farp.Name, font);
                    float tx = p.X - ns.Width / 2f, ty = p.Y + half + 1f / _zoom, shadow = 1f / _zoom;
                    g.DrawString(farp.Name, font, shadowBrush, tx + shadow, ty + shadow);
                    g.DrawString(farp.Name, font, textBrush, tx, ty);
                }
            }
        }

        private WargameZoneData _movingLanding;   // boîte attrapée à la souris
        private PointF _movingLastImage;          // dernière position souris (pixel image)
        private bool _movingMoved;                // elle a vraiment bougé

        public WargameAxis SelectedAxis
        {
            get { return _selectedAxis; }
            set { _selectedAxis = value; Invalidate(); }
        }

        public void SetAxes(List<WargameAxis> axes)
        {
            _axes = axes ?? new List<WargameAxis>();
            Invalidate();
        }

        // Le prochain clic gauche sur une zone est renvoyé au callback (au lieu de
        // sélectionner la zone). Clic droit = annule.
        public void BeginPickZone(Action<WargameZoneData> callback)
        {
            _zonePickCallback = callback;
            Cursor = Cursors.Cross;
        }

        public void CancelPickZone()
        {
            _zonePickCallback = null;

            if (!_pickingCalibrationPoint)
                Cursor = Cursors.Default;
        }

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

        // Déplacement de la carte : on ne passe plus par les barres de défilement du Panel
        // (le zoom se faisait en 2 temps : taille du contrôle, puis position de défilement).
        // La carte remplit son parent, et c'est elle qui dessine l'image décalée de _pan
        // (position de l'angle haut-gauche de l'image, en pixels écran) : un seul rendu par cran de molette.
        private PointF _pan = PointF.Empty;
        private PointF _mouseDownPan;
        private bool _fitPending = true;
        private Image _fittedImage;

        // Drag-to-pan : clic maintenu + déplacement = on scrolle la carte dans le
        // Panel parent (celui avec AutoScroll = true) plutôt que d'ouvrir une zone.
        // En dessous du seuil, c'est un vrai clic (sélection/pick), pas un déplacement.
        private const int DragThreshold = 4;
        private Point _mouseDownScreenPoint;
        private Point _mouseDownClientPoint;
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

        // Ensemble des zones sélectionnées, à chaque changement (clic simple ou
        // Ctrl+clic). Seulement si ReadOnly = false et hors mode calibration.
        public event Action<List<WargameZoneData>> SelectionChanged;

        // Pixel brut cliqué pendant un BeginPickCalibrationPoint() (indépendant de ReadOnly,
        // utilisé par l'outil de calibration du campaignMaker)
        public event Action<PointF> CalibrationPointPicked;

        // Position pixel courante de la souris sur la carte, à chaque déplacement,
        // sans clic (sert à afficher les coordonnées en live pour diagnostiquer).
        public event Action<PointF> MapMouseMoved;

        // -------------------- Coordonnées DCS sous la souris --------------------
        // Affichées en direct en bas à gauche de la carte (mêmes X/Y que dans le .miz).
        // Alt + clic copie les deux lignes dans le presse-papiers, prêtes à coller
        // dans un fichier Lua (5 tabulations, virgule finale).
        public bool ShowMouseCoordinates { get; set; } = true;

        private Point _lastMouseClient = new Point(-1, -1);
        private bool _mouseInside = false;
        private DateTime _copiedUntil = DateTime.MinValue;
        private const int CoordBoxWidth = 360;
        private const int CoordBoxHeight = 22;

        private Rectangle CoordBoxRect()
        {
            return new Rectangle(6, Height - CoordBoxHeight - 6, CoordBoxWidth, CoordBoxHeight);
        }

        private bool TryGetMouseDcs(Point client, out PointF dcs)
        {
            dcs = PointF.Empty;
            if (_calibration == null || !_calibration.IsCalibrated || _backgroundImage == null)
                return false;

            dcs = _calibration.PixelToDcs(ClientToImage(client.X, client.Y));
            return true;
        }

        private static string FormatDcs(double value)
        {
            return value.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
        }

        private void CopyDcsToClipboard(Point client)
        {
            if (!TryGetMouseDcs(client, out PointF dcs))
                return;

            string text = "\t\t\t\t\tx = " + FormatDcs(dcs.X) + ",\r\n"
                        + "\t\t\t\t\ty = " + FormatDcs(dcs.Y) + ",";

            try
            {
                Clipboard.SetText(text);
                _copiedUntil = DateTime.Now.AddSeconds(2);
            }
            catch (Exception)
            {
                // presse-papiers verrouillé par une autre application : on ne bloque pas la carte
            }

            Invalidate(CoordBoxRect());
        }

        private void DrawCoordinates(Graphics g)
        {
            if (!ShowMouseCoordinates || !_mouseInside)
                return;

            if (!TryGetMouseDcs(_lastMouseClient, out PointF dcs))
                return;

            bool copied = DateTime.Now < _copiedUntil;
            string text = copied
                ? "COPIED   x = " + FormatDcs(dcs.X) + "   y = " + FormatDcs(dcs.Y)
                : "x = " + FormatDcs(dcs.X) + "   y = " + FormatDcs(dcs.Y) + "   (Alt+click: copy)";

            Rectangle box = CoordBoxRect();

            using (var back = new SolidBrush(Color.FromArgb(copied ? 210 : 170, copied ? Color.DarkGreen : Color.Black)))
            using (var fore = new SolidBrush(Color.White))
            using (var font = new Font("Consolas", 9f))
            {
                g.FillRectangle(back, box);
                g.DrawString(text, font, fore, box.X + 4, box.Y + 3);
            }
        }

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

            // Nouvelle image : on la fait tenir dans la fenêtre au premier affichage
            if (!ReferenceEquals(_backgroundImage, _fittedImage))
            {
                _fittedImage = _backgroundImage;
                _fitPending = true;
            }

            ApplyZoomSize();

            RebuildZonePixelShapes();
            RebuildObjectivePixelPositions();
            RebuildMissionUnitPixels();
            Invalidate();
        }

        private void RebuildZonePixelShapes()
        {
            _zonePixelShapes.Clear();
            _zoneCenters.Clear();

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

                if (!string.IsNullOrEmpty(zone.Id))
                    _zoneCenters[zone.Id] = zone.Shape == WargameZoneShape.Polygon
                        ? GetCentroid(pixelShape.PolygonPoints)
                        : pixelShape.CircleCenter;

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

        // Premier affichage d'une image : zoom qui la fait tenir entièrement dans la fenêtre.
        // Ensuite, garde la carte à l'écran (ni perdue hors champ, ni collée d'un seul côté).
        private void ApplyZoomSize()
        {
            if (_backgroundImage == null) return;

            if (_fitPending && ClientSize.Width > 0 && ClientSize.Height > 0)
            {
                _fitPending = false;
                float fit = Math.Min((float)ClientSize.Width / _backgroundImage.Width,
                                     (float)ClientSize.Height / _backgroundImage.Height);
                _zoom = Math.Max(MinZoom, Math.Min(MaxZoom, fit));
                _pan = PointF.Empty;
            }

            ClampPan();
        }

        // Image plus petite que la fenêtre : centrée. Plus grande : on ne laisse pas de vide
        // au bord (le bord de l'image ne peut pas dépasser le bord de la fenêtre).
        private void ClampPan()
        {
            if (_backgroundImage == null) return;

            float w = _backgroundImage.Width * _zoom;
            float h = _backgroundImage.Height * _zoom;

            float x = _pan.X;
            float y = _pan.Y;

            if (w <= ClientSize.Width) x = (ClientSize.Width - w) / 2f;
            else x = Math.Max(ClientSize.Width - w, Math.Min(0f, x));

            if (h <= ClientSize.Height) y = (ClientSize.Height - h) / 2f;
            else y = Math.Max(ClientSize.Height - h, Math.Min(0f, y));

            _pan = new PointF(x, y);
        }

        // Convertit un point du contrôle (pixels écran) en pixel de l'image d'origine.
        // C'est cette coordonnée-là que comprennent la calibration et les polygones.
        private PointF ClientToImage(float clientX, float clientY)
        {
            return new PointF((clientX - _pan.X) / _zoom, (clientY - _pan.Y) / _zoom);
        }

        // Revient à l'échelle 1:1, centrée (utile quand on s'est perdu à force de zoomer)
        public void ResetZoom()
        {
            if (_backgroundImage == null) return;

            _zoom = 1.0f;
            _pan = new PointF((ClientSize.Width - _backgroundImage.Width) / 2f,
                              (ClientSize.Height - _backgroundImage.Height) / 2f);
            ClampPan();
            Invalidate();
        }

        // La carte remplit son parent : plus de barres de défilement, on déplace par glissement
        protected override void OnParentChanged(EventArgs e)
        {
            base.OnParentChanged(e);

            if (Parent is Panel panel)
            {
                panel.AutoScroll = false;
                Dock = DockStyle.Fill;
            }
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
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

            // Point de l'image sous le curseur : il doit y rester après le zoom
            PointF imagePoint = ClientToImage(e.X, e.Y);

            _zoom = newZoom;
            _pan = new PointF(e.X - imagePoint.X * _zoom, e.Y - imagePoint.Y * _zoom);
            ClampPan();

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

            _mouseDownPan = _pan;

            // Clic gauche sur une boîte de débarquement : on la saisit (le glissement la déplacera au lieu de la carte)
            _movingLanding = null;
            _movingMoved = false;
            _movingFarp = null;
            _movingFarpMoved = false;

            // Une FARP est posée par-dessus tout : on la teste en premier
            if (e.Button == MouseButtons.Left && !ReadOnly && FarpEditable
                && _zonePickCallback == null && !_pickingCalibrationPoint)
            {
                _movingFarp = HitTestFarp(ClientToImage(e.X, e.Y));
            }

            if (_movingFarp == null && e.Button == MouseButtons.Left && !ReadOnly && LandingEditable
                && _zonePickCallback == null && !_pickingCalibrationPoint)
            {
                PointF downPoint = ClientToImage(e.X, e.Y);
                WargameZoneData hit = HitTestZone(downPoint);

                if (hit != null && WargameEngineCombat.IsLandingZone(hit))
                {
                    _movingLanding = hit;
                    _movingLastImage = downPoint;
                }
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);

            MapMouseMoved?.Invoke(ClientToImage(e.X, e.Y));

            if (_supplyMap != null && e.Button == MouseButtons.None)
                UpdateSupplyTip(ClientToImage(e.X, e.Y));

            _lastMouseClient = new Point(e.X, e.Y);
            _mouseInside = true;
            if (ShowMouseCoordinates)
                Invalidate(CoordBoxRect());

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

            // Une FARP est saisie : elle suit la souris (position DCS), pas la carte
            if (_movingFarp != null)
            {
                if (_isDragging && _calibration != null && _calibration.IsCalibrated)
                {
                    PointF dcs = _calibration.PixelToDcs(ClientToImage(e.X, e.Y));
                    _movingFarp.X = dcs.X;
                    _movingFarp.Y = dcs.Y;
                    _movingFarpMoved = true;
                    Invalidate();
                }

                return;
            }

            // Une boîte de débarquement est saisie : c'est elle qui suit la souris, pas la carte
            if (_movingLanding != null)
            {
                if (_isDragging)
                {
                    PointF now = ClientToImage(e.X, e.Y);
                    MoveLanding(_movingLanding, now.X - _movingLastImage.X, now.Y - _movingLastImage.Y);
                    _movingLastImage = now;
                    _movingMoved = true;
                    Invalidate();
                }

                return;
            }

            if (_isDragging)
            {
                _pan = new PointF(_mouseDownPan.X + dx, _mouseDownPan.Y + dy);
                ClampPan();
                Invalidate();
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _mouseInside = false;
            Invalidate(CoordBoxRect());
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);

            _isDragging = false;

            if (!_pickingCalibrationPoint)
                Cursor = Cursors.Default;

            if (_movingFarp != null)
            {
                WargameFarp movedFarp = _movingFarp;
                bool didMoveFarp = _movingFarpMoved;

                _movingFarp = null;
                _movingFarpMoved = false;

                if (didMoveFarp)
                    FarpMoved?.Invoke(movedFarp);
            }

            // Fin du glissement d'une boîte : on prévient l'éditeur (Unsaved changes)
            if (_movingLanding != null)
            {
                WargameZoneData moved = _movingLanding;
                bool didMove = _movingMoved;

                _movingLanding = null;
                _movingMoved = false;

                if (didMove)
                    LandingMoved?.Invoke(moved);
            }
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

            // Alt + clic gauche : copie les coordonnées DCS du point cliqué, sans rien sélectionner
            // (Ctrl+clic est déjà pris par la sélection multiple de zones).
            if (e.Button == MouseButtons.Left && (ModifierKeys & Keys.Alt) == Keys.Alt && !_pickingCalibrationPoint)
            {
                CopyDcsToClipboard(_mouseDownClientPoint);
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

            if (_zonePickCallback != null)
            {
                if (e.Button == MouseButtons.Right)
                {
                    CancelPickZone();
                    ZonePickCancelled?.Invoke();
                    return;
                }

                WargameZoneData pickedZone = HitTestZone(clickPoint);
                if (pickedZone == null)
                    return;   // clic dans le vide : on reste en attente

                Action<WargameZoneData> callback = _zonePickCallback;
                CancelPickZone();
                callback(pickedZone);
                return;
            }

            if (e.Button == MouseButtons.Right)
            {
                if (!ReadOnly && FarpEditable)
                {
                    WargameFarp rightFarp = HitTestFarp(clickPoint);
                    if (rightFarp != null)
                    {
                        FarpRightClicked?.Invoke(rightFarp, Cursor.Position);
                        return;
                    }
                }

                WargameObjective rightObjective = HitTestObjective(clickPoint);
                if (rightObjective != null)
                {
                    ObjectiveRightClicked?.Invoke(rightObjective, Cursor.Position);
                    return;
                }

                // Editeur seulement (la preview est en ReadOnly) : menu des plans de débarquement
                if (!ReadOnly)
                {
                    WargameZoneData rightZone = HitTestZone(clickPoint);

                    if (rightZone != null && WargameEngineCombat.IsLandingZone(rightZone))
                        LandingRightClicked?.Invoke(rightZone, Cursor.Position);
                    else
                        MapRightClicked?.Invoke(clickPoint, Cursor.Position);
                }

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

        // Les boîtes de débarquement sont dessinées PAR-DESSUS les zones : on les teste en premier,
        // sinon un clic sur une boîte posée sur une zone attraperait la zone dessous.
        private WargameZoneData HitTestZone(PointF pixelPoint)
        {
            WargameZoneData landing = HitTestZonePass(pixelPoint, true);
            if (landing != null)
                return landing;

            return HitTestZonePass(pixelPoint, false);
        }

        private WargameZoneData HitTestZonePass(PointF pixelPoint, bool landingOnly)
        {
            foreach (var kvp in _zonePixelShapes)
            {
                if (WargameEngineCombat.IsLandingZone(kvp.Key) != landingOnly)
                    continue;

                PixelShape shape = kvp.Value;

                bool hit = shape.Shape == WargameZoneShape.Polygon
                    ? IsPointInPolygon(pixelPoint, shape.PolygonPoints)
                    : IsPointInCircle(pixelPoint, shape.CircleCenter, shape.CircleRadius);

                if (hit)
                    return kvp.Key;
            }

            return null;
        }

        // Déplace une boîte de débarquement de (dx, dy) pixels image : le polygone dessiné ET les
        // coordonnées DCS de la zone (c'est elles qui sont enregistrées).
        private void MoveLanding(WargameZoneData zone, float dx, float dy)
        {
            PixelShape shape;
            if (_calibration == null || !_zonePixelShapes.TryGetValue(zone, out shape)) return;
            if (shape.Shape != WargameZoneShape.Polygon || shape.PolygonPoints == null) return;

            float sumX = 0, sumY = 0;
            int count = Math.Min(shape.PolygonPoints.Length, zone.DcsPoints.Count);

            for (int i = 0; i < count; i++)
            {
                var moved = new PointF(shape.PolygonPoints[i].X + dx, shape.PolygonPoints[i].Y + dy);
                shape.PolygonPoints[i] = moved;

                PointF dcs = _calibration.PixelToDcs(moved);
                zone.DcsPoints[i] = dcs;
                sumX += dcs.X;
                sumY += dcs.Y;
            }

            if (count > 0)
                zone.Center = new PointF(sumX / count, sumY / count);

            _zoneCenters[zone.Id] = GetCentroid(shape.PolygonPoints);
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
            e.Graphics.TranslateTransform(_pan.X, _pan.Y);
            e.Graphics.ScaleTransform(_zoom, _zoom);

            e.Graphics.DrawImage(_backgroundImage, 0, 0, _backgroundImage.Width, _backgroundImage.Height);

            if (_calibration != null && _calibration.IsCalibrated)
            {
                // Zones ordinaires d'abord, boîtes de débarquement par-dessus
                foreach (var kvp in _zonePixelShapes)
                {
                    if (!WargameEngineCombat.IsLandingZone(kvp.Key))
                        DrawZone(e.Graphics, kvp.Key, kvp.Value);
                }

                foreach (var kvp in _zonePixelShapes)
                {
                    if (WargameEngineCombat.IsLandingZone(kvp.Key))
                        DrawZone(e.Graphics, kvp.Key, kvp.Value);
                }

                DrawLandingArrows(e.Graphics);

                if (ShowMissionUnits)
                    DrawMissionUnits(e.Graphics);

                if (ShowAxes)
                    DrawAxes(e.Graphics);

                if (ShowArrows)
                    DrawZoneArrows(e.Graphics);

                DrawConvoys(e.Graphics);
                DrawFarps(e.Graphics);

                // Points de force APRES les axes et les flèches (et après toutes les zones) : rien ne les recouvre
                foreach (var kvp in _zonePixelShapes)
                {
                    DrawZoneDot(e.Graphics, kvp.Key, kvp.Value);
                }

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

                if (ShowAxes && !string.IsNullOrEmpty(_axisHighlightZoneId))
                {
                    foreach (var kvp in _zonePixelShapes)
                    {
                        if (string.Equals(kvp.Key.Id, _axisHighlightZoneId, StringComparison.OrdinalIgnoreCase))
                            DrawSelectionHighlight(e.Graphics, kvp.Value);
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
            else
            {
                DrawCoordinates(e.Graphics);
                DrawSupplyLegend(e.Graphics);
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

        // Contour épais autour d'une zone qui vient de changer de main (preview du combat)
        private void DrawFlash(Graphics g, PixelShape pixelShape)
        {
            using (var flashPen = new Pen(Color.White, 4f / _zoom))
            {
                if (pixelShape.Shape == WargameZoneShape.Polygon && pixelShape.PolygonPoints != null && pixelShape.PolygonPoints.Length >= 3)
                {
                    g.DrawPolygon(flashPen, pixelShape.PolygonPoints);
                }
                else if (pixelShape.Shape != WargameZoneShape.Polygon)
                {
                    float r = pixelShape.CircleRadius;
                    var rect = new RectangleF(pixelShape.CircleCenter.X - r, pixelShape.CircleCenter.Y - r, r * 2, r * 2);
                    g.DrawEllipse(flashPen, rect);
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

        private static readonly Color[] AxisColors = { Color.Lime, Color.Gold, Color.Aqua, Color.HotPink, Color.MediumPurple };

        // Un trait par axe qui suit le chemin calculé, de zone en zone (centre à centre),
        // avec une flèche vers l'objectif B. L'axe sélectionné est plus épais.
        // Pas de chemin = trait rouge pointillé direct A -> B.
        // AxisOpacity rend les traits plus ou moins transparents.
        private void DrawAxes(Graphics g)
        {
            int alpha = (int)Math.Round(255 * _axisOpacity);

            for (int i = 0; i < _axes.Count; i++)
            {
                WargameAxis axis = _axes[i];
                Color color = Color.FromArgb(alpha, AxisColors[i % AxisColors.Length]);
                float width = (axis == _selectedAxis ? 5f : 3f) / _zoom;

                var points = new List<PointF>();
                foreach (string zoneId in axis.Path)
                {
                    PointF center;
                    if (_zoneCenters.TryGetValue(zoneId, out center))
                        points.Add(center);
                }

                if (points.Count < 2)
                {
                    PointF a, b;
                    if (_zoneCenters.TryGetValue(axis.StartZoneId ?? "", out a) && _zoneCenters.TryGetValue(axis.EndZoneId ?? "", out b))
                    {
                        using (var broken = new Pen(Color.FromArgb(alpha, Color.Red), width) { DashStyle = DashStyle.Dash })
                            g.DrawLine(broken, a, b);
                    }
                    continue;
                }

                PointF[] line = points.ToArray();

                using (var outline = new Pen(Color.FromArgb(alpha, Color.Black), width + 3f / _zoom))
                    g.DrawLines(outline, line);

                using (var pen = new Pen(color, width))
                using (var arrow = new AdjustableArrowCap(3f, 3f, true))
                {
                    pen.CustomEndCap = arrow;
                    g.DrawLines(pen, line);
                }

                // Zones de passage : un losange de la couleur de l'axe, numéroté dans l'ordre
                for (int v = 0; v < axis.ViaZoneIds.Count; v++)
                {
                    PointF viaCenter;
                    if (!_zoneCenters.TryGetValue(axis.ViaZoneIds[v], out viaCenter))
                        continue;

                    float half = 8f / _zoom;
                    PointF[] diamond =
                    {
                        new PointF(viaCenter.X, viaCenter.Y - half),
                        new PointF(viaCenter.X + half, viaCenter.Y),
                        new PointF(viaCenter.X, viaCenter.Y + half),
                        new PointF(viaCenter.X - half, viaCenter.Y),
                    };

                    using (var viaBrush = new SolidBrush(color))
                    using (var viaOutline = new Pen(Color.FromArgb(alpha, Color.Black), 1.5f / _zoom))
                    using (var viaFont = new Font("Segoe UI", 8f / _zoom, FontStyle.Bold))
                    using (var viaTextBrush = new SolidBrush(Color.FromArgb(alpha, Color.Black)))
                    {
                        g.FillPolygon(viaBrush, diamond);
                        g.DrawPolygon(viaOutline, diamond);
                        g.DrawString((v + 1).ToString(), viaFont, viaTextBrush, viaCenter.X + half, viaCenter.Y - half * 1.6f);
                    }
                }

                // Pastille au départ + nom de l'axe
                PointF start = line[0];
                float dot = 5f / _zoom;

                using (var dotBrush = new SolidBrush(color))
                using (var dotOutline = new Pen(Color.FromArgb(alpha, Color.Black), 1.5f / _zoom))
                {
                    g.FillEllipse(dotBrush, start.X - dot, start.Y - dot, dot * 2, dot * 2);
                    g.DrawEllipse(dotOutline, start.X - dot, start.Y - dot, dot * 2, dot * 2);
                }

                using (var font = new Font("Segoe UI", 9f / _zoom, FontStyle.Bold))
                using (var textBrush = new SolidBrush(Color.FromArgb(alpha, Color.White)))
                using (var shadowBrush = new SolidBrush(Color.FromArgb(alpha, Color.Black)))
                {
                    float shadow = 1f / _zoom;
                    float tx = start.X + dot + 3f / _zoom;
                    float ty = start.Y - (14f + i * 13f) / _zoom;
                    g.DrawString(axis.Name, font, shadowBrush, tx + shadow, ty + shadow);
                    g.DrawString(axis.Name, font, textBrush, tx, ty);
                }
            }
        }

        private void DrawObjectiveMarker(Graphics g, WargameObjective obj, PointF center)
        {
            // Tailles divisées par le zoom pour rester constantes à l'écran : un marqueur
            // est un repère d'interface, pas un objet de la carte, il ne doit pas grossir.
            float radius = ObjectiveMarkerRadius / _zoom;

            var rect = new RectangleF(center.X - radius, center.Y - radius, radius * 2, radius * 2);

            bool destroyed = _destroyedObjectives.Contains(obj);

            using (var fill = new SolidBrush(destroyed ? Color.DimGray : Color.OrangeRed))
            using (var outline = new Pen(Color.White, 1.5f / _zoom))
            {
                g.FillEllipse(fill, rect);
                g.DrawEllipse(outline, rect);
            }

            if (destroyed)
            {
                using (var cross = new Pen(Color.Red, 2.5f / _zoom))
                {
                    g.DrawLine(cross, rect.Left, rect.Top, rect.Right, rect.Bottom);
                    g.DrawLine(cross, rect.Left, rect.Bottom, rect.Right, rect.Top);
                }
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

            // Calque ravitaillement : le remplissage prend la couleur du niveau, le contour garde la couleur du camp
            Color fillColor = zoneColor;
            WargameEngineCombat.WargameSupplyInfo supply = null;
            if (_supplyMap != null && _supplyMap.TryGetValue(zone.Id, out supply))
                fillColor = supply.CutOff ? Color.FromArgb(90, 90, 90) : SupplyColor(supply.Level);
            else
                supply = null;

            // Calque ravitaillement actif : le remplissage ne dit plus le camp, donc le contour devient épais et opaque
            float outlineWidth = supply != null ? (isSelected ? 7f : 5f) : (isSelected ? 3f : 2f);

            using (var fillBrush = new SolidBrush(Color.FromArgb(supply != null ? (isSelected ? 200 : 160) : (isSelected ? 140 : 90), fillColor)))
            using (var outlinePen = new Pen(supply != null ? Color.FromArgb(255, zoneColor) : zoneColor, outlineWidth / _zoom) { LineJoin = LineJoin.Round })
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

            if (supply != null && supply.CutOff)
            {
                // hachures par-dessus le gris : une zone coupée se voit même sans la légende
                using (var hatch = new HatchBrush(HatchStyle.WideUpwardDiagonal, Color.FromArgb(120, Color.Black), Color.Transparent))
                {
                    if (pixelShape.Shape == WargameZoneShape.Polygon)
                        g.FillPolygon(hatch, pixelShape.PolygonPoints);
                    else
                    {
                        float r = pixelShape.CircleRadius;
                        g.FillEllipse(hatch, pixelShape.CircleCenter.X - r, pixelShape.CircleCenter.Y - r, r * 2, r * 2);
                    }
                }
            }

            DrawZoneLabel(g, zone, pixelShape);

            if (supply != null && supply.IsSource)
                DrawSupplySourceMark(g, pixelShape);
        }

        // Centre approximatif d'une zone dessinée (moyenne des sommets, ou centre du cercle)
        private static PointF ShapeCenter(PixelShape pixelShape)
        {
            if (pixelShape.Shape == WargameZoneShape.Polygon && pixelShape.PolygonPoints != null && pixelShape.PolygonPoints.Length > 0)
            {
                float sx = 0, sy = 0;
                foreach (PointF p in pixelShape.PolygonPoints) { sx += p.X; sy += p.Y; }
                return new PointF(sx / pixelShape.PolygonPoints.Length, sy / pixelShape.PolygonPoints.Length);
            }

            return pixelShape.CircleCenter;
        }

        private bool TryZoneCenter(string zoneId, out PointF center)
        {
            foreach (var kvp in _zonePixelShapes)
            {
                if (string.Equals(kvp.Key.Id, zoneId, StringComparison.OrdinalIgnoreCase))
                {
                    center = ShapeCenter(kvp.Value);
                    return true;
                }
            }

            center = PointF.Empty;
            return false;
        }

        // Convois : route en pointillés (couleur du camp), point avancé = carré avec sa réserve en tours ("R2"), gris s'il est vide
        private void DrawConvoys(Graphics g)
        {
            if (_convoys == null) return;

            foreach (WargameEngineCombat.WargameConvoy convoy in _convoys)
            {
                if (convoy.Lost) continue;   // zone prise pendant le tour : plus de point avancé à montrer

                Color color = GetZoneColor(convoy.Side);

                if (convoy.ByRoad && convoy.Route.Count >= 2)
                {
                    var points = new List<PointF>();
                    foreach (string id in convoy.Route)
                    {
                        PointF p;
                        if (TryZoneCenter(id, out p)) points.Add(p);
                    }

                    if (points.Count >= 2)
                    {
                        using (var back = new Pen(Color.FromArgb(220, Color.White), 6f / _zoom) { LineJoin = LineJoin.Round })
                        using (var front = new Pen(Color.FromArgb(240, color), 3f / _zoom) { DashStyle = DashStyle.Dash, LineJoin = LineJoin.Round })
                        {
                            g.DrawLines(back, points.ToArray());
                            g.DrawLines(front, points.ToArray());
                        }
                    }
                }

                PointF c;
                if (!TryZoneCenter(convoy.TargetId, out c)) continue;

                float half = 11f / _zoom;
                var box = new RectangleF(c.X - half, c.Y + half * 0.6f, half * 2, half * 2);
                bool active = convoy.ReserveAfter > 0.001;

                using (var fill = new SolidBrush(active ? color : Color.DimGray))
                using (var outline = new Pen(Color.White, 1.5f / _zoom))
                using (var font = new Font("Segoe UI", 7.5f / _zoom, FontStyle.Bold))
                using (var text = new SolidBrush(Color.White))
                {
                    g.FillRectangle(fill, box);
                    g.DrawRectangle(outline, box.X, box.Y, box.Width, box.Height);
                    g.DrawString("R" + convoy.ReserveAfter.ToString("0.#"), font, text, box.X + 1f / _zoom, box.Y + 3f / _zoom);

                    if (!convoy.ByRoad)
                        g.DrawString("air", font, text, box.X, box.Bottom);
                }
            }
        }

        // Petit "S" dans un rond blanc, au-dessus du centre de la zone
        private void DrawSupplySourceMark(Graphics g, PixelShape pixelShape)
        {
            PointF c = ShapeCenter(pixelShape);

            float r = 8f / _zoom;
            var rect = new RectangleF(c.X - r, c.Y - r * 2.6f, r * 2, r * 2);

            using (var fill = new SolidBrush(Color.White))
            using (var outline = new Pen(Color.Black, 1.5f / _zoom))
            using (var font = new Font("Segoe UI", 8f / _zoom, FontStyle.Bold))
            using (var text = new SolidBrush(Color.Black))
            {
                g.FillEllipse(fill, rect);
                g.DrawEllipse(outline, rect);
                g.DrawString("S", font, text, rect.X + r * 0.45f, rect.Y + r * 0.25f);
            }
        }

        // Nom de la zone au centre, texte de la preview dessous, contour blanc si elle vient de changer de main
        private void DrawZoneLabel(Graphics g, WargameZoneData zone, PixelShape pixelShape)
        {
            if (string.IsNullOrEmpty(zone.Id))
                return;

            if (_flashZoneIds.Contains(zone.Id))
                DrawFlash(g, pixelShape);

            PointF anchor = pixelShape.Shape == WargameZoneShape.Polygon
                ? GetCentroid(pixelShape.PolygonPoints)
                : pixelShape.CircleCenter;

            using (var font = new Font("Segoe UI", 9f / _zoom, FontStyle.Bold))
            using (var textBrush = new SolidBrush(Color.White))
            {
                SizeF textSize = g.MeasureString(zone.Id, font);
                g.DrawString(zone.Id, font, textBrush, anchor.X - textSize.Width / 2f, anchor.Y - textSize.Height / 2f);

                string caption;
                if (_zoneCaptions.TryGetValue(zone.Id, out caption) && !string.IsNullOrEmpty(caption))
                {
                    using (var smallFont = new Font("Segoe UI", 8f / _zoom, FontStyle.Bold))
                    using (var shadowBrush = new SolidBrush(Color.Black))
                    {
                        SizeF captionSize = g.MeasureString(caption, smallFont);
                        float cx = anchor.X - captionSize.Width / 2f;
                        float cy = anchor.Y + textSize.Height / 2f;
                        float shadow = 1f / _zoom;

                        g.DrawString(caption, smallFont, shadowBrush, cx + shadow, cy + shadow);
                        g.DrawString(caption, smallFont, textBrush, cx, cy);
                    }
                }
            }
        }

        // Distance, depuis "origin" dans la direction unitaire (ux, uy), jusqu'au bord de la zone (première intersection
        // avec son contour). -1 si le bord n'est pas trouvé.
        private static float BorderDistance(PixelShape shape, PointF origin, float ux, float uy)
        {
            if (shape.Shape != WargameZoneShape.Polygon)
                return shape.CircleRadius > 0 ? shape.CircleRadius : -1f;

            PointF[] pts = shape.PolygonPoints;
            if (pts == null || pts.Length < 3) return -1f;

            float best = float.MaxValue;

            for (int i = 0; i < pts.Length; i++)
            {
                PointF a = pts[i];
                PointF b = pts[(i + 1) % pts.Length];

                float ex = b.X - a.X, ey = b.Y - a.Y;
                float denom = ux * ey - uy * ex;
                if (Math.Abs(denom) < 1e-6f) continue;   // parallèle

                float wx = a.X - origin.X, wy = a.Y - origin.Y;
                float t = (wx * ey - wy * ex) / denom;   // distance le long du rayon
                float u = (wx * uy - wy * ux) / denom;   // position sur l'arête (0..1)

                if (t > 0.01f && u >= 0f && u <= 1f && t < best)
                    best = t;
            }

            return best == float.MaxValue ? -1f : best;
        }

        // Flèches de combat : de chaque zone vers chaque zone ennemie à laquelle elle fait face.
        // La flèche reste DANS sa zone : elle part du centre et s'arrête avant le bord vers la voisine ennemie.
        // Les deux flèches de deux zones qui se font face sont décalées de côté (comme deux voies) : elles ne se
        // chevauchent pas. Épaisseur = part de la force de la zone tournée de ce côté (grosse = force principale,
        // fine = garde de flanc). Taille constante à l'écran quel que soit le zoom.
        private void DrawZoneArrows(Graphics g)
        {
            if (_zoneArrows.Count == 0) return;

            var anchors = new Dictionary<string, PointF>(StringComparer.OrdinalIgnoreCase);
            var shapes = new Dictionary<string, PixelShape>(StringComparer.OrdinalIgnoreCase);

            foreach (var kvp in _zonePixelShapes)
            {
                if (string.IsNullOrEmpty(kvp.Key.Id)) continue;

                PixelShape shape = kvp.Value;
                if (shape.Shape == WargameZoneShape.Polygon && (shape.PolygonPoints == null || shape.PolygonPoints.Length < 3)) continue;

                anchors[kvp.Key.Id] = shape.Shape == WargameZoneShape.Polygon ? GetCentroid(shape.PolygonPoints) : shape.CircleCenter;
                shapes[kvp.Key.Id] = shape;
            }

            foreach (WargameZoneArrow arrow in _zoneArrows)
            {
                PointF from, to;
                PixelShape ownShape;
                if (!anchors.TryGetValue(arrow.FromId, out from) || !anchors.TryGetValue(arrow.ToId, out to)) continue;
                if (!shapes.TryGetValue(arrow.FromId, out ownShape)) continue;

                float dx = to.X - from.X;
                float dy = to.Y - from.Y;
                float length = (float)Math.Sqrt(dx * dx + dy * dy);
                if (length < 1f) continue;

                float ux = dx / length, uy = dy / length;

                // Jusqu'où va-t-on dans SA zone dans cette direction (à défaut : le tiers de la distance entre les centres)
                float border = BorderDistance(ownShape, from, ux, uy);
                if (border <= 0f) border = length / 3f;

                float zoom = _zoom;
                float lane = 6f / zoom;                    // décalage de côté (voie), constant à l'écran
                float px = uy * lane, py = -ux * lane;     // côté droit du sens de la flèche : l'autre camp prend l'autre côté

                var p0 = new PointF(from.X + ux * border * 0.30f + px, from.Y + uy * border * 0.30f + py);
                var p1 = new PointF(from.X + ux * border * 0.88f + px, from.Y + uy * border * 0.88f + py);

                float widthPx = 2.5f + 7f * (float)Math.Max(0.0, Math.Min(1.0, arrow.Share));
                float width = widthPx / zoom;

                using (var capBack = new System.Drawing.Drawing2D.AdjustableArrowCap(2.2f, 2.2f, true))
                using (var capFront = new System.Drawing.Drawing2D.AdjustableArrowCap(2.2f, 2.2f, true))
                using (var back = new Pen(Color.FromArgb(220, Color.White), width + 3f / zoom) { CustomEndCap = capBack, LineJoin = LineJoin.Round })
                using (var front = new Pen(Color.FromArgb(240, GetZoneColor(arrow.Side)), width) { CustomEndCap = capFront, LineJoin = LineJoin.Round })
                {
                    g.DrawLine(back, p0, p1);
                    g.DrawLine(front, p0, p1);
                }
            }
        }

        // Flèche de chaque plan de débarquement : de la boîte vers la zone-plage visée, avec dessous
        // "-> plage (N vagues, M unités)". Elle part du bord de la boîte et s'arrête dans la plage.
        // Plage non choisie : pas de flèche, la boîte porte "(no target)".
        private void DrawLandingArrows(Graphics g)
        {
            foreach (var kvp in _zonePixelShapes)
            {
                WargameZoneData plan = kvp.Key;
                PixelShape boxShape = kvp.Value;

                if (!WargameEngineCombat.IsLandingZone(plan)) continue;
                if (boxShape.Shape != WargameZoneShape.Polygon || boxShape.PolygonPoints == null || boxShape.PolygonPoints.Length < 3) continue;

                PointF from = GetCentroid(boxShape.PolygonPoints);
                Color color = GetZoneColor(plan.Control);
                float zoom = _zoom;

                bool hasTarget = false;

                if (!string.IsNullOrEmpty(plan.LandingTarget))
                {
                    foreach (var other in _zonePixelShapes)
                    {
                        if (!string.Equals(other.Key.Id, plan.LandingTarget, StringComparison.OrdinalIgnoreCase)) continue;

                        PixelShape targetShape = other.Value;
                        PointF to = targetShape.Shape == WargameZoneShape.Polygon && targetShape.PolygonPoints != null && targetShape.PolygonPoints.Length >= 3
                            ? GetCentroid(targetShape.PolygonPoints)
                            : targetShape.CircleCenter;

                        float dx = to.X - from.X, dy = to.Y - from.Y;
                        float length = (float)Math.Sqrt(dx * dx + dy * dy);
                        if (length < 1f) break;

                        float ux = dx / length, uy = dy / length;

                        // départ au bord de la boîte, arrivée à mi-chemin du centre de la plage et de son bord côté boîte
                        float startBorder = BorderDistance(boxShape, from, ux, uy);
                        if (startBorder <= 0f) startBorder = 0f;

                        float endBorder = BorderDistance(targetShape, to, -ux, -uy);
                        if (endBorder <= 0f) endBorder = length / 4f;

                        var p0 = new PointF(from.X + ux * (startBorder + 4f / zoom), from.Y + uy * (startBorder + 4f / zoom));
                        var p1 = new PointF(to.X - ux * endBorder * 0.45f, to.Y - uy * endBorder * 0.45f);

                        // la flèche doit avoir une longueur positive (boîte posée contre la plage)
                        if ((p1.X - p0.X) * ux + (p1.Y - p0.Y) * uy <= 2f / zoom) { hasTarget = true; break; }

                        float width = 6f / zoom;

                        using (var capBack = new System.Drawing.Drawing2D.AdjustableArrowCap(2.2f, 2.2f, true))
                        using (var capFront = new System.Drawing.Drawing2D.AdjustableArrowCap(2.2f, 2.2f, true))
                        using (var back = new Pen(Color.FromArgb(230, Color.White), width + 3f / zoom) { CustomEndCap = capBack, LineJoin = LineJoin.Round })
                        using (var front = new Pen(Color.FromArgb(250, color), width) { CustomEndCap = capFront, LineJoin = LineJoin.Round })
                        {
                            g.DrawLine(back, p0, p1);
                            g.DrawLine(front, p0, p1);
                        }

                        hasTarget = true;
                        break;
                    }
                }

                // Texte sous le nom de la boîte
                int units = plan.Formations.Count;
                int waves = plan.Formations.Where(f => f.Wave > 0).Select(f => f.Wave).Distinct().Count();

                string text = string.IsNullOrEmpty(plan.LandingTarget)
                    ? "(no target)"
                    : (hasTarget ? "-> " : "-> ? ") + plan.LandingTarget;
                text += "  (" + waves + (waves == 1 ? " wave, " : " waves, ") + units + (units == 1 ? " unit)" : " units)");

                using (var font = new Font("Segoe UI", 8f / zoom, FontStyle.Bold))
                using (var textBrush = new SolidBrush(Color.White))
                using (var shadowBrush = new SolidBrush(Color.Black))
                {
                    SizeF size = g.MeasureString(text, font);
                    float tx = from.X - size.Width / 2f;
                    float ty = from.Y + 9f / zoom;   // sous la ligne du nom
                    float shadow = 1f / zoom;

                    g.DrawString(text, font, shadowBrush, tx + shadow, ty + shadow);
                    g.DrawString(text, font, textBrush, tx, ty);
                }
            }
        }

        // Points de force d'une zone (preview), dessinés après les axes pour rester visibles :
        //  - point plein  = formations ENGAGEES (zone au contact de l'ennemi), le nombre s'écrit dedans ;
        //  - point creux en pointillés, "+N" = SURPLUS (pas au contact : en marche ou en attente).
        // La taille de chaque point suit sa force.
        private void DrawZoneDot(Graphics g, WargameZoneData zone, PixelShape pixelShape)
        {
            WargameZoneDot dot;
            if (string.IsNullOrEmpty(zone.Id) || !_zoneDots.TryGetValue(zone.Id, out dot) || (dot.Count <= 0 && dot.Surplus <= 0))
                return;

            if (pixelShape.Shape == WargameZoneShape.Polygon && (pixelShape.PolygonPoints == null || pixelShape.PolygonPoints.Length < 3))
                return;

            PointF anchor = pixelShape.Shape == WargameZoneShape.Polygon
                ? GetCentroid(pixelShape.PolygonPoints)
                : pixelShape.CircleCenter;

            // Taille à l'écran : de 6 px (petite force) à 22 px (grosse force). La racine fait
            // suivre la SURFACE du point à la force. Le surplus est un peu plus petit.
            float radiusMain = dot.Count > 0 ? (float)Math.Min(22.0, 5.0 + 0.9 * Math.Sqrt(dot.Force)) / _zoom : 0f;
            float radiusSurplus = dot.Surplus > 0 ? (float)Math.Min(17.0, 5.0 + 0.7 * Math.Sqrt(dot.SurplusForce)) / _zoom : 0f;

            float gap = 4f / _zoom;
            float totalWidth = radiusMain * 2 + radiusSurplus * 2 + (radiusMain > 0 && radiusSurplus > 0 ? gap : 0f);

            // Au-dessus du nom de la zone, les deux points côte à côte, centrés sur la zone
            float cy = anchor.Y - (14f / _zoom) - Math.Max(radiusMain, radiusSurplus);
            float x = anchor.X - totalWidth / 2f;

            Color color = GetZoneColor(dot.Side);

            using (var outline = new Pen(Color.White, 2f / _zoom))
            using (var dashed = new Pen(Color.White, 2f / _zoom) { DashStyle = DashStyle.Dash })
            using (var font = new Font("Segoe UI", 8f / _zoom, FontStyle.Bold))
            using (var textBrush = new SolidBrush(Color.White))
            using (var shadowBrush = new SolidBrush(Color.Black))
            {
                if (radiusMain > 0)
                {
                    var rect = new RectangleF(x, cy - radiusMain, radiusMain * 2, radiusMain * 2);

                    using (var fill = new SolidBrush(Color.FromArgb(240, color)))
                        g.FillEllipse(fill, rect);

                    g.DrawEllipse(outline, rect);

                    string text = dot.Count.ToString();
                    SizeF size = g.MeasureString(text, font);
                    g.DrawString(text, font, textBrush, rect.X + radiusMain - size.Width / 2f, cy - size.Height / 2f);

                    x += radiusMain * 2 + gap;
                }

                if (radiusSurplus > 0)
                {
                    var rect = new RectangleF(x, cy - radiusSurplus, radiusSurplus * 2, radiusSurplus * 2);

                    using (var fill = new SolidBrush(Color.FromArgb(140, color)))
                        g.FillEllipse(fill, rect);

                    g.DrawEllipse(dashed, rect);

                    string text = "+" + dot.Surplus;
                    SizeF size = g.MeasureString(text, font);
                    float tx = rect.X + radiusSurplus - size.Width / 2f;
                    float ty = cy - size.Height / 2f;
                    float shadow = 1f / _zoom;

                    g.DrawString(text, font, shadowBrush, tx + shadow, ty + shadow);
                    g.DrawString(text, font, textBrush, tx, ty);
                }
            }
        }

        private static PointF GetCentroid(PointF[] points)
        {
            return WargameAxisPathFinder.PolygonCentroid(points);
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
