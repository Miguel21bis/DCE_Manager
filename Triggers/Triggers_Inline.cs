using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace DCE_Manager
{
    // Étape 1 des "cartes d'actions complètes" : l'action CHOISIE dans la liste THEN devient modifiable
    // directement dans sa ligne ("set target [ liste ] active to [x] true").
    // Seulement pour les actions simples (cible, true/false, nombre). Les autres restent modifiables dans DETAILS, comme avant.
    // Les mêmes éditeurs que DETAILS sont utilisés (MakeEditor) : aucun comportement n'est dupliqué.
    public partial class Triggers_Form
    {
        private Panel _inlinePanel;             // le bandeau posé sur la ligne choisie
        private ActionRow _inlineState;         // l'action affichée dans le bandeau
        private CampTrigger _inlineTrigger;
        private bool _inlineKeep;               // true : la modification vient du bandeau, on ne le reconstruit pas pendant qu'on s'en sert

        private static bool InlineAllowed(FuncDef def, int k, ParamDef p, LuaNode arg)
        {
            // un paramètre dont la valeur a une forme inattendue (expression, table...) reste dans DETAILS
            if (TriggerLists.LiteralChoices(p.Source) != null)
                return arg == null || arg.Kind != NodeKind.Table;

            switch (p.Type)
            {
                case TrigParam.Bool:
                    return arg == null || arg.Kind == NodeKind.Bool;
                case TrigParam.Number:
                    return !p.Max.HasValue && !p.Min.HasValue && p.Unit.Length == 0 && (arg == null || arg.Kind == NodeKind.Number);
                case TrigParam.Flag:
                    return arg == null || arg.Kind == NodeKind.Number || arg.Kind == NodeKind.Text;
                case TrigParam.Text:
                    // les textes de briefing (plusieurs lignes) restent dans DETAILS
                    return !(k == 0 && (def.Name == "Text" || def.Name == "TextPlayMission")) && (arg == null || arg.Kind == NodeKind.Text);
                case TrigParam.Choice:
                case TrigParam.TargetTitle:
                case TrigParam.TargetName:
                case TrigParam.AirUnit:
                case TrigParam.Airbase:
                case TrigParam.ShipGroup:
                    return arg == null || arg.Kind == NodeKind.Text;
                default:
                    return false;
            }
        }

        private void ClearInline()
        {
            if (_inlinePanel != null)
            {
                _listThen.Controls.Remove(_inlinePanel);
                _inlinePanel.Dispose();
                _inlinePanel = null;
            }

            _inlineState = null;
            _inlineTrigger = null;
        }

        // Ce que ShowDetails appelle à la fin : reconstruit le bandeau, sauf si c'est lui qui vient de modifier l'action.
        private void RefreshInline()
        {
            bool keep = _inlineKeep && _inlineState != null && _inlineTrigger == _editing && _inlineState.Index == _listThen.SelectedIndex;
            _inlineKeep = false;

            if (!keep)
                BuildInline();
        }

        private void BuildInline()
        {
            ClearInline();

            CampTrigger t = _editing;
            int index = _listThen.SelectedIndex;

            if (t == null || index < 0 || index >= t.Actions.Count || index >= t.ActionNodes.Count)
                return;

            LuaNode node = t.ActionNodes[index];

            if (node == null || node.Kind != NodeKind.Call || node.Def == null || node.Def.Kind != "Action")
                return;

            FuncDef def = node.Def;
            var matches = PlaceholderRx.Matches(def.Sentence).Cast<System.Text.RegularExpressions.Match>().ToList();

            if (matches.Count == 0)
                return;

            // tous les paramètres de la phrase doivent être "simples", sinon on laisse DETAILS faire
            foreach (var m in matches)
            {
                int k = int.Parse(m.Value.Substring(1, m.Value.Length - 2));

                if (k >= def.Params.Count || !InlineAllowed(def, k, def.Params[k], k < node.Items.Count ? node.Items[k] : null))
                    return;
            }

            var state = new ActionRow { Index = index, Def = def };
            int count = Math.Max(def.Params.Count, node.Items.Count);

            for (int k = 0; k < count; k++)
                state.Args.Add(k < node.Items.Count ? node.Items[k].Source : null);

            var panel = new Panel { BackColor = SystemColors.Window };
            int x = 4;
            int h = _listThen.ItemHeight - 2;
            int last = 0;

            Action<string> addText = text =>
            {
                text = text.Replace("&", "&&");

                if (text.Length == 0)
                    return;

                int w = TextRenderer.MeasureText(text, _listThen.Font, new Size(2000, 20), TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix).Width + 4;
                panel.Controls.Add(new Label { Left = x, Top = 0, Width = w, Height = h, Text = text, UseMnemonic = false, TextAlign = ContentAlignment.MiddleLeft, Font = _listThen.Font });
                x += w;
            };

            addText((index + 1) + ".  ");

            foreach (var m in matches)
            {
                addText(def.Sentence.Substring(last, m.Index - last));
                last = m.Index + m.Length;

                int k = int.Parse(m.Value.Substring(1, m.Value.Length - 2));
                ParamDef p = def.Params[k];
                int eh;
                Control ed = MakeEditor(t, state, k, p, k < node.Items.Count ? node.Items[k] : null, out eh);

                if (ed is CheckBox)
                {
                    ((CheckBox)ed).AutoSize = true;
                    ed.Left = x;
                    ed.Top = Math.Max(0, (h - 20) / 2);
                    panel.Controls.Add(ed);
                    x += TextRenderer.MeasureText("false", ed.Font).Width + 26;
                }
                else
                {
                    ed.Left = x;
                    ed.Width = p.Type == TrigParam.Number || p.Type == TrigParam.Flag ? 80 : p.Type == TrigParam.Choice ? 110 : 220;
                    ed.Top = Math.Max(0, (h - ed.Height) / 2);
                    panel.Controls.Add(ed);
                    x += ed.Width + 2;
                }

                _toolTip.SetToolTip(ed, p.Label);
            }

            addText(def.Sentence.Substring(last));

            _inlinePanel = panel;
            _inlineState = state;
            _inlineTrigger = t;

            _listThen.Controls.Add(panel);
            PositionInline();
        }

        // Pose le bandeau sur la ligne choisie (appelé aussi quand la liste défile ou change de taille).
        private void PositionInline()
        {
            if (_inlinePanel == null)
                return;

            int index = _listThen.SelectedIndex;

            if (index < 0)
            {
                _inlinePanel.Visible = false;
                return;
            }

            Rectangle r = _listThen.GetItemRectangle(index);
            int left = r.X + ThenGripWidth + 2;
            int width = Math.Max(40, r.Width - ThenGripWidth - ThenBtnZone - 4);
            bool seen = r.Bottom > 0 && r.Top < _listThen.ClientSize.Height;

            _inlinePanel.SetBounds(left, r.Y + 1, width, r.Height - 2);
            _inlinePanel.Visible = seen;
            _inlinePanel.BringToFront();
        }

        private void HookInline()
        {
            ((WideListBox)_listThen).Scrolled += (s, e) => PositionInline();
            _listThen.SizeChanged += (s, e) => PositionInline();
        }
    }
}
