using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace DCE_Manager
{
    // Ce que le campaignMaker est en train de choisir sur la carte
    internal enum AxisPick { Start, End, WaypointAdd, WaypointChange }

    // Une demande de choix de zone sur la carte : pour quel axe, quel rôle, quelle étape
    internal class AxisPickRequest
    {
        public WargameAxis Axis;
        public AxisPick Kind;
        public int WaypointIndex;   // seulement pour WaypointChange
    }

    // Panneau d'édition des axes de progression (WargameState.Axes).
    // A placer dans Wargame\UI\.
    //
    // Un axe = une zone de départ (Start) + des étapes obligées (Waypoints, optionnelles,
    // dans l'ordre voulu) + une zone objectif (Objective). Le chemin entre les points est
    // calculé tout seul (WargameAxisPathFinder), le campaignMaker ne le saisit jamais.
    // Un axe devient actif en jeu quand le flag de campagne qu'on lui donne ici est posé
    // par un trigger de camp_triggers_init.lua (bouton "Copy trigger").
    //
    // Tous les axes sont affichés d'un coup, une "carte" par axe. Le panneau modifie
    // directement les objets WargameAxis de la liste reçue dans Bind() ; l'écriture disque
    // est gérée par la Form (Save -> Init).
    internal class ucWargameAxisPanel : UserControl
    {
        // Mêmes couleurs, dans le même ordre, que sur la carte (ucWargameMapView.AxisColors)
        private static readonly Color[] AxisColors = { Color.Lime, Color.Gold, Color.Aqua, Color.HotPink, Color.MediumPurple };

        private List<WargameAxis> _axes = new List<WargameAxis>();
        private List<WargameZoneData> _zones = new List<WargameZoneData>();
        private bool _editable = true;

        // Reconstruction demandée mais pas encore faite (on reconstruit APRES le clic en cours,
        // pour ne jamais détruire le bouton sur lequel on vient de cliquer)
        private bool _rebuildPending;

        // Axe à faire apparaître à l'écran après la prochaine reconstruction (axe qu'on vient d'ajouter)
        private WargameAxis _scrollToAxis;

        // Un axe a été modifié par l'utilisateur (la Form passe en "dirty" et redessine)
        public event Action AxesChanged;

        // L'utilisateur a travaillé sur cet axe (la carte le met en évidence)
        public event Action<WargameAxis> SelectedAxisChanged;

        // L'utilisateur veut choisir une zone sur la carte
        public event Action<AxisPickRequest> PickZoneRequested;

        // L'utilisateur a cliqué une ligne : la carte met cette zone en évidence
        public event Action<string> HighlightZoneRequested;

        private Label _lblTitle;
        private Label _lblNote;
        private Panel _scroll;
        private Button _btnAddAxis;
        private readonly ToolTip _tip = new ToolTip { ShowAlways = true };
        private readonly Font _fontBold = new Font("Segoe UI", 9.5f, FontStyle.Bold);
        private readonly Font _fontSymbol = new Font("Segoe UI Symbol", 8f);

        public ucWargameAxisPanel()
        {
            BuildUi();
            Resize += (s, e) => ApplyLayout();
            ApplyLayout();
            ShowNormalNote();
        }

        // -------------------- API pour la Form --------------------

        // axes : la liste vivante (WargameZoneRepository.State.Axes). editable = false
        // en vue Active : les axes se définissent dans l'Init.
        public void Bind(List<WargameAxis> axes, List<WargameZoneData> zones, bool editable)
        {
            _axes = axes ?? new List<WargameAxis>();
            _zones = zones ?? new List<WargameZoneData>();
            _editable = editable;

            ShowNormalNote();
            Rebuild();

            SelectedAxisChanged?.Invoke(_axes.Count > 0 ? _axes[0] : null);
        }

        // A appeler quand les voisins des zones ont pu changer : les chemins sont recalculés.
        public void RefreshPaths()
        {
            foreach (WargameAxis axis in _axes)
                RecomputePath(axis);

            RebuildLater();
        }

        // Réponse à PickZoneRequested : la zone cliquée sur la carte.
        public void SetPickedZone(AxisPickRequest request, string zoneId)
        {
            WargameAxis axis = request == null ? null : request.Axis;
            if (axis == null || !_editable || !_axes.Contains(axis))
            {
                EndPick();
                return;
            }

            // Une zone ne peut servir qu'une fois dans un même axe
            if (IsZoneUsed(axis, zoneId, request))
            {
                ShowPickBanner("'" + zoneId + "' is already used by this axis. Pick another zone, or right-click to stop.");
                PickZoneRequested?.Invoke(request);   // on reste en attente
                return;
            }

            switch (request.Kind)
            {
                case AxisPick.Start:
                    axis.StartZoneId = zoneId;
                    break;

                case AxisPick.End:
                    axis.EndZoneId = zoneId;
                    break;

                case AxisPick.WaypointChange:
                    if (request.WaypointIndex >= 0 && request.WaypointIndex < axis.ViaZoneIds.Count)
                        axis.ViaZoneIds[request.WaypointIndex] = zoneId;
                    break;

                case AxisPick.WaypointAdd:
                    axis.ViaZoneIds.Add(zoneId);
                    break;
            }

            RecomputePath(axis);
            SelectAxis(axis);
            RebuildLater();
            AxesChanged?.Invoke();

            if (request.Kind == AxisPick.WaypointAdd)
            {
                // Ajout en série : on repart tout de suite en attente de la zone suivante
                ShowPickBanner(axis.Name + ": waypoint " + axis.ViaZoneIds.Count + " added (" + zoneId
                    + "). Click the next zone, or right-click to stop.");
                PickZoneRequested?.Invoke(request);
            }
            else
            {
                EndPick();
            }
        }

        // La carte a annulé le choix (clic droit) : on remet le message normal
        public void EndPick()
        {
            ShowNormalNote();
        }

        // -------------------- Logique --------------------

        private void SelectAxis(WargameAxis axis)
        {
            SelectedAxisChanged?.Invoke(axis);
        }

        private void RecomputePath(WargameAxis axis)
        {
            axis.Path = WargameAxisPathFinder.FindPath(_zones, axis.StartZoneId, axis.ViaZoneIds, axis.EndZoneId);
        }

        // La zone est-elle déjà prise par un autre rôle du même axe (départ, objectif, autre étape) ?
        private static bool IsZoneUsed(WargameAxis axis, string zoneId, AxisPickRequest request)
        {
            bool Same(string other)
            {
                return string.Equals(other, zoneId, StringComparison.OrdinalIgnoreCase);
            }

            if (request.Kind != AxisPick.Start && Same(axis.StartZoneId)) return true;
            if (request.Kind != AxisPick.End && Same(axis.EndZoneId)) return true;

            for (int i = 0; i < axis.ViaZoneIds.Count; i++)
            {
                // L'étape qu'on est en train de remplacer ne compte pas
                if (request.Kind == AxisPick.WaypointChange && i == request.WaypointIndex) continue;
                if (Same(axis.ViaZoneIds[i])) return true;
            }

            return false;
        }

        private void RequestPick(AxisPickRequest request)
        {
            if (!_editable || request == null) return;

            SelectAxis(request.Axis);

            string text;
            switch (request.Kind)
            {
                case AxisPick.Start:
                    text = "click the new START zone on the map.";
                    break;
                case AxisPick.End:
                    text = "click the new OBJECTIVE zone on the map.";
                    break;
                case AxisPick.WaypointChange:
                    text = "click the new zone for waypoint " + (request.WaypointIndex + 1) + " on the map.";
                    break;
                default:
                    text = "click the zones to cross, in order. Right-click to stop.";
                    break;
            }

            ShowPickBanner(request.Axis.Name + ": " + text);
            PickZoneRequested?.Invoke(request);
        }

        private void ButtonAddAxis_Click(object sender, EventArgs e)
        {
            if (!_editable) return;

            int n = 1;
            while (_axes.Any(a => string.Equals(a.Name, "Axis_" + n, StringComparison.OrdinalIgnoreCase)
                               || string.Equals(a.ActivationFlag, "axis_" + n, StringComparison.OrdinalIgnoreCase)))
                n++;

            var axis = new WargameAxis { Name = "Axis_" + n, ActivationFlag = "axis_" + n };
            _axes.Add(axis);

            _scrollToAxis = axis;
            SelectAxis(axis);
            RebuildLater();
            AxesChanged?.Invoke();
        }

        private void DeleteAxis(WargameAxis axis)
        {
            if (!_editable) return;

            DialogResult answer = MessageBox.Show("Delete the axis '" + axis.Name + "'?",
                "Wargame", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (answer != DialogResult.Yes) return;

            _axes.Remove(axis);

            SelectAxis(null);
            RebuildLater();
            AxesChanged?.Invoke();
        }

        // Déplace une étape vers le haut (delta = -1) ou vers le bas (delta = +1)
        private void MoveWaypoint(WargameAxis axis, int index, int delta)
        {
            int target = index + delta;
            if (!_editable || index < 0 || target < 0 || index >= axis.ViaZoneIds.Count || target >= axis.ViaZoneIds.Count)
                return;

            string moved = axis.ViaZoneIds[index];
            axis.ViaZoneIds.RemoveAt(index);
            axis.ViaZoneIds.Insert(target, moved);

            AfterWaypointsChanged(axis);
        }

        private void RemoveWaypoint(WargameAxis axis, int index)
        {
            if (!_editable || index < 0 || index >= axis.ViaZoneIds.Count) return;

            axis.ViaZoneIds.RemoveAt(index);

            AfterWaypointsChanged(axis);
        }

        private void AfterWaypointsChanged(WargameAxis axis)
        {
            RecomputePath(axis);
            SelectAxis(axis);
            RebuildLater();
            AxesChanged?.Invoke();
        }

        // Prépare dans le presse-papiers le trigger qui pose le flag de l'axe. Le
        // campaignMaker le colle dans camp_triggers_init.lua et règle la condition.
        private void CopyTrigger(WargameAxis axis)
        {
            string name = CleanForLua(axis.Name);
            string flag = CleanForLua(axis.ActivationFlag);

            if (flag.Length == 0)
            {
                MessageBox.Show("Type a trigger flag name first.", "Wargame",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var sb = new StringBuilder();
            sb.AppendLine("\t[\"Wargame Axis " + name + "\"] = {");
            sb.AppendLine("\t\tactive = true,");
            sb.AppendLine("\t\tonce = true,");
            sb.AppendLine("\t\tcondition = 'Return.Mission() >= 1',");
            sb.AppendLine("\t\taction = {");
            sb.AppendLine("\t\t\t'Action.SetCampFlag(\"" + flag + "\", true)',");
            sb.AppendLine("\t\t},");
            sb.AppendLine("\t},");

            try
            {
                Clipboard.SetText(sb.ToString());
            }
            catch (Exception ex)
            {
                MessageBox.Show("Could not use the clipboard: " + ex.Message, "Wargame",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            MessageBox.Show(
                "Trigger copied.\n\nPaste it inside camp_triggers = { ... } in Init\\camp_triggers_init.lua, "
                + "then change its condition (by default the axis starts at mission 1).\n\n"
                + "Any condition works: Return.Mission(), Return.TargetAlive(...), Return.CampFlag(...)...",
                "Wargame", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        // Guillemets retirés : le flag et le nom sont insérés dans une chaîne Lua
        private static string CleanForLua(string text)
        {
            return (text ?? "").Replace("\"", "").Replace("'", "").Trim();
        }

        // -------------------- Message du haut --------------------

        private void ShowNormalNote()
        {
            _lblNote.Text = _editable
                ? "One card per axis: a Start, some Waypoints (optional, in order) and an Objective. Use Change / Add on the map."
                : "Axes are defined in the Init view. Switch to Init to edit them.";
            _lblNote.BackColor = SystemColors.Control;
            _lblNote.ForeColor = Color.DimGray;
        }

        // Pendant un choix sur la carte : message bien visible
        private void ShowPickBanner(string text)
        {
            _lblNote.Text = text;
            _lblNote.BackColor = SystemColors.Info;
            _lblNote.ForeColor = SystemColors.InfoText;
        }

        // -------------------- Reconstruction des cartes --------------------

        private int CardWidth()
        {
            // On réserve toujours la place de la barre de défilement : les cartes ne changent
            // pas de largeur quand elle apparaît
            return Math.Max(300, _scroll.Width - SystemInformation.VerticalScrollBarWidth - 6);
        }

        private void RebuildLater()
        {
            if (_rebuildPending) return;
            _rebuildPending = true;

            if (IsHandleCreated)
                BeginInvoke(new Action(Rebuild));
            else
                Rebuild();
        }

        private void Rebuild()
        {
            _rebuildPending = false;

            // On garde la position de défilement, sauf si on doit montrer un nouvel axe
            int scrollY = -_scroll.AutoScrollPosition.Y;

            _scroll.SuspendLayout();

            foreach (Control old in _scroll.Controls.Cast<Control>().ToList())
            {
                _scroll.Controls.Remove(old);
                old.Dispose();
            }

            int cardWidth = CardWidth();
            int y = 4;
            Panel scrollTarget = null;

            for (int i = 0; i < _axes.Count; i++)
            {
                Panel card = BuildCard(_axes[i], i, cardWidth);
                card.Location = new Point(2, y);
                _scroll.Controls.Add(card);

                y += card.Height + 8;

                if (_axes[i] == _scrollToAxis)
                    scrollTarget = card;
            }

            _scrollToAxis = null;
            _scroll.ResumeLayout();

            if (scrollTarget != null)
                _scroll.ScrollControlIntoView(scrollTarget);
            else
                _scroll.AutoScrollPosition = new Point(0, scrollY);

            _btnAddAxis.Enabled = _editable;
        }

        // Une carte = un axe, tout visible d'un coup :
        //   [nom]                    [état du chemin] [Delete axis]
        //   Start        zone                [Change]
        //   Waypoint 1   zone    [Change] [^] [v] [x]
        //   [+ Add waypoint]
        //   Objective    zone                [Change]
        //   Flag         [flag]  [Copy trigger]
        //   message d'aide
        private Panel BuildCard(WargameAxis axis, int index, int width)
        {
            bool canEdit = _editable;
            Color axisColor = AxisColors[index % AxisColors.Length];

            var card = new Panel
            {
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = SystemColors.ControlLight,
            };
            card.Width = width;

            int W = card.ClientSize.Width;
            int y = 8;

            card.Click += (s, e) => SelectAxis(axis);

            // ---- En-tête : nom, état du chemin, suppression ----
            var txtName = new TextBox
            {
                Text = axis.Name,
                Location = new Point(16, y),
                Width = W - 16 - 220,
                Font = _fontBold,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                Enabled = canEdit,
            };
            txtName.Enter += (s, e) => SelectAxis(axis);
            txtName.TextChanged += (s, e) =>
            {
                axis.Name = txtName.Text;
                AxesChanged?.Invoke();
            };

            var lblStatus = new Label
            {
                Location = new Point(W - 216, y + 3),
                Size = new Size(110, 22),
                TextAlign = ContentAlignment.MiddleRight,
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
            };

            bool bothSet = !string.IsNullOrEmpty(axis.StartZoneId) && !string.IsNullOrEmpty(axis.EndZoneId);
            if (axis.IsValid)
            {
                lblStatus.Text = "\u2714 " + axis.Path.Count + " zones";
                lblStatus.ForeColor = Color.ForestGreen;
                _tip.SetToolTip(lblStatus, "Path: " + string.Join(" > ", axis.Path));
            }
            else if (bothSet)
            {
                lblStatus.Text = "\u2716 no path";
                lblStatus.ForeColor = Color.Firebrick;
                _tip.SetToolTip(lblStatus, "No path found between the start and the objective.");
            }
            else
            {
                lblStatus.Text = "incomplete";
                lblStatus.ForeColor = Color.DimGray;
                _tip.SetToolTip(lblStatus, "Set the start and the objective on the map.");
            }

            var btnDelete = new Button
            {
                Text = "Delete axis",
                Location = new Point(W - 102, y - 1),
                Size = new Size(90, 26),
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
                Enabled = canEdit,
            };
            btnDelete.Click += (s, e) => DeleteAxis(axis);
            _tip.SetToolTip(btnDelete, "Delete this axis");

            card.Controls.Add(txtName);
            card.Controls.Add(lblStatus);
            card.Controls.Add(btnDelete);

            y += 38;

            // ---- Une ligne de zone : légende, zone, [Change] et, pour une étape, [^] [v] [x] ----
            Button SmallButton(string text, int x, string tip, bool enabled, Action onClick)
            {
                var b = new Button
                {
                    Text = text,
                    Font = _fontSymbol,
                    Location = new Point(x, y + 1),
                    Size = new Size(28, 26),
                    Anchor = AnchorStyles.Top | AnchorStyles.Right,
                    Enabled = enabled,
                };
                b.Click += (s, e) => onClick();
                _tip.SetToolTip(b, tip);
                card.Controls.Add(b);
                return b;
            }

            void AddZoneRow(string caption, string zoneId, AxisPickRequest changeRequest, int waypointIndex)
            {
                var lbl = new Label
                {
                    Text = caption,
                    Location = new Point(16, y + 5),
                    Size = new Size(78, 20),
                };

                var txt = new TextBox
                {
                    ReadOnly = true,
                    Text = string.IsNullOrEmpty(zoneId) ? "(not set)" : zoneId,
                    Location = new Point(96, y + 2),
                    Width = W - 96 - 178,
                    Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                };

                // Un clic sur la ligne met la zone en évidence sur la carte
                txt.Click += (s, e) =>
                {
                    SelectAxis(axis);
                    if (!string.IsNullOrEmpty(zoneId))
                        HighlightZoneRequested?.Invoke(zoneId);
                };

                var btnChange = new Button
                {
                    Text = "Change",
                    Location = new Point(W - 172, y + 1),
                    Size = new Size(64, 26),
                    Anchor = AnchorStyles.Top | AnchorStyles.Right,
                    Enabled = canEdit,
                };
                btnChange.Click += (s, e) => RequestPick(changeRequest);
                _tip.SetToolTip(btnChange, "Pick another zone on the map");

                card.Controls.Add(lbl);
                card.Controls.Add(txt);
                card.Controls.Add(btnChange);

                if (waypointIndex >= 0)
                {
                    int wi = waypointIndex;
                    SmallButton("\u25B2", W - 104, "Move this waypoint earlier", canEdit && wi > 0,
                        () => MoveWaypoint(axis, wi, -1));
                    SmallButton("\u25BC", W - 72, "Move this waypoint later", canEdit && wi < axis.ViaZoneIds.Count - 1,
                        () => MoveWaypoint(axis, wi, 1));
                    SmallButton("\u2715", W - 40, "Remove this waypoint", canEdit,
                        () => RemoveWaypoint(axis, wi));
                }

                y += 30;
            }

            AddZoneRow("Start", axis.StartZoneId,
                new AxisPickRequest { Axis = axis, Kind = AxisPick.Start }, -1);

            for (int k = 0; k < axis.ViaZoneIds.Count; k++)
            {
                AddZoneRow("Waypoint " + (k + 1), axis.ViaZoneIds[k],
                    new AxisPickRequest { Axis = axis, Kind = AxisPick.WaypointChange, WaypointIndex = k }, k);
            }

            var btnAddWaypoint = new Button
            {
                Text = "+ Add waypoint",
                Location = new Point(96, y + 1),
                Size = new Size(150, 26),
                Enabled = canEdit,
            };
            btnAddWaypoint.Click += (s, e) => RequestPick(new AxisPickRequest { Axis = axis, Kind = AxisPick.WaypointAdd });
            _tip.SetToolTip(btnAddWaypoint, "Click zones on the map, in order. Right-click on the map to stop.");
            card.Controls.Add(btnAddWaypoint);
            y += 30;

            AddZoneRow("Objective", axis.EndZoneId,
                new AxisPickRequest { Axis = axis, Kind = AxisPick.End }, -1);

            // ---- Activation : flag + trigger ----
            y += 4;

            var lblFlag = new Label
            {
                Text = "Flag",
                Location = new Point(16, y + 5),
                Size = new Size(78, 20),
            };

            var txtFlag = new TextBox
            {
                Text = axis.ActivationFlag,
                Location = new Point(96, y + 2),
                Width = W - 96 - 150,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                Enabled = canEdit,
            };
            _tip.SetToolTip(txtFlag, "The axis starts when a camp trigger sets this flag.");

            var btnCopy = new Button
            {
                Text = "Copy trigger",
                Location = new Point(W - 142, y + 1),
                Size = new Size(130, 26),
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
            };
            btnCopy.Click += (s, e) => CopyTrigger(axis);
            _tip.SetToolTip(btnCopy, "Copy a trigger that sets the flag. Paste it in Init\\camp_triggers_init.lua");

            card.Controls.Add(lblFlag);
            card.Controls.Add(txtFlag);
            card.Controls.Add(btnCopy);

            y += 32;

            // ---- Message d'aide (le problème le plus important d'abord) ----
            var lblMessage = new Label
            {
                Location = new Point(16, y),
                Size = new Size(W - 28, 34),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            };
            card.Controls.Add(lblMessage);

            void UpdateMessage()
            {
                if (bothSet && !axis.IsValid)
                {
                    lblMessage.Text = "No path found. Check that the zones in between are neighbors, and the order of the waypoints.";
                    lblMessage.ForeColor = Color.Firebrick;
                }
                else if (string.IsNullOrWhiteSpace(axis.ActivationFlag))
                {
                    lblMessage.Text = "No flag: this axis will never start.";
                    lblMessage.ForeColor = Color.DarkOrange;
                }
                else
                {
                    lblMessage.Text = "Starts when a camp trigger sets the flag \"" + axis.ActivationFlag.Trim() + "\".";
                    lblMessage.ForeColor = Color.DimGray;
                }
            }

            UpdateMessage();

            txtFlag.Enter += (s, e) => SelectAxis(axis);
            txtFlag.TextChanged += (s, e) =>
            {
                axis.ActivationFlag = txtFlag.Text.Trim();
                UpdateMessage();
                AxesChanged?.Invoke();
            };

            y += 38;

            // ---- Hauteur finale + bande de couleur (celle de l'axe sur la carte) ----
            card.ClientSize = new Size(W, y);

            var strip = new Panel
            {
                Location = new Point(0, 0),
                Size = new Size(6, card.ClientSize.Height),
                BackColor = axisColor,
            };
            _tip.SetToolTip(strip, "Color of this axis on the map");
            card.Controls.Add(strip);

            return card;
        }

        // -------------------- Construction de l'UI --------------------

        private void BuildUi()
        {
            _lblTitle = new Label
            {
                Text = "Axes of progression",
                Location = new Point(10, 10),
                AutoSize = true,
                Font = new Font("Segoe UI", 11f, FontStyle.Bold),
            };

            _lblNote = new Label
            {
                Location = new Point(10, 38),
                Size = new Size(260, 36),
            };

            _scroll = new Panel { AutoScroll = true };

            _btnAddAxis = new Button { Text = "+ Add axis", Height = 30 };
            _btnAddAxis.Click += ButtonAddAxis_Click;
            _tip.SetToolTip(_btnAddAxis, "Create a new axis of progression");

            Controls.Add(_lblTitle);
            Controls.Add(_lblNote);
            Controls.Add(_scroll);
            Controls.Add(_btnAddAxis);
        }

        // Pas d'Anchor sur les éléments du haut : on recalcule à chaque changement de taille
        // (même méthode que ucWargameZoneEditPanel). Les cartes, elles, suivent leur largeur.
        private void ApplyLayout()
        {
            int inner = Math.Max(240, ClientSize.Width - 20);
            int top = 78;
            int bottom = 42;

            _lblNote.Width = inner;

            _scroll.SetBounds(0, top, ClientSize.Width, Math.Max(60, ClientSize.Height - top - bottom));
            _btnAddAxis.SetBounds(10, Math.Max(top + 60, ClientSize.Height - 36), inner, 30);

            int cardWidth = CardWidth();
            foreach (Control card in _scroll.Controls)
                card.Width = cardWidth;
        }
    }
}
