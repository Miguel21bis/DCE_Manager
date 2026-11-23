using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Xml.Linq;
using DCE_Manager.Parameters;
using DCE_Manager.Update;
using DCE_Manager.Utils;

namespace DCE_Manager
{
    // Colonne "Family" de la grille (campagnes maîtres / filles). Partie de CampaignGridLeft, voir Campaigns_List_Grid_Left.cs
    public partial class CampaignGridLeft
    {
        // État déplié/replié des campagnes maîtres (colonne Family). En mémoire
        // uniquement : remis à zéro à chaque relance de l'appli, pas persisté.
        private readonly HashSet<string> _expandedMasters = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private int _lastFamilyMouseX = -1;

        // Largeur de la zone "⋯" tout à droite de la colonne Family (le reste = flèche/étoile)
        private const int FamilyMenuZoneWidth = 32;

        // Détermine le rôle d'une campagne dans la hiérarchie (maître / fille / seule),
        // recalculé à la volée à chaque peinture/clic - pas de cache ici, CampaignHierarchy
        // fait déjà le sien en interne.
        private void GetFamilyRole(string name, out bool isMaster, out bool isChild, out bool expanded)
        {
            isChild = CampaignHierarchy.IsChild(name);
            isMaster = !isChild && CampaignHierarchy.IsMaster(name); // une fille n'est jamais aussi maître (2 niveaux max)
            expanded = isMaster && _expandedMasters.Contains(name);
        }

        // Ordonne les noms de dossiers pour l'affichage : les maîtres (et campagnes
        // seules) gardent un ordre alphabétique global, mais chaque maître est
        // TOUJOURS immédiatement suivi de ses filles (triées entre elles) - visibles
        // ou pas, la boucle décidera ensuite ligne par ligne (voir "isHiddenChild").
        // Un simple tri alphabétique global ne suffit pas : il ne regroupe pas les
        // familles dont le nom commun est à la FIN de la chaîne (ex: "Falcon over PG"
        // / "Tomcat over PG"), et éparpille les filles n'importe où une fois dépliées.
        private List<string> OrderCampaignFoldersForDisplay(IEnumerable<string> allNames)
        {
            List<string> allNamesList = allNames.ToList();
            HashSet<string> nameSet = new HashSet<string>(allNamesList, StringComparer.OrdinalIgnoreCase);

            List<string> topLevel = allNamesList
                .Where(n => !CampaignHierarchy.IsChild(n) || !nameSet.Contains(CampaignHierarchy.ResolveMaster(n)))
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                .ToList();

            List<string> result = new List<string>();

            foreach (string name in topLevel)
            {
                result.Add(name);

                IEnumerable<string> children = CampaignHierarchy.GetChildren(name)
                    .Where(c => nameSet.Contains(c))
                    .OrderBy(c => c, StringComparer.OrdinalIgnoreCase);

                result.AddRange(children);
            }

            return result;
        }

        // Colonne Family :
        // - Gauche : sur un maître "▶ ★ 3" (déplié : "▼ ★ 3", le chiffre = nombre de filles).
        //   "↳" grisé sur une fille. Rien sur une campagne seule.
        // - Droite (FamilyMenuZoneWidth px) : bouton "⋯" encadré -> popup "Campaign family".
        private void GridCampaigns_Family_CellPainting(object sender, DataGridViewCellPaintingEventArgs e)
        {
            if (e.RowIndex < 0 || _mainForm.dataGridViewCampaigns.Columns[e.ColumnIndex].Name != "Family")
                return;

            e.PaintBackground(e.ClipBounds, true);
            e.Handled = true;

            var row = _mainForm.dataGridViewCampaigns.Rows[e.RowIndex];
            string name = row.Cells["Name"].Value?.ToString();

            if (DeleteSelectedRowTag.Equals(row.Tag) || string.IsNullOrEmpty(name) || _incompleteOrOrphanNames.Contains(name))
                return; // ligne "corbeille" ou "problème" : pas de gestion de famille dessus

            bool isMaster, isChild, expanded;
            GetFamilyRole(name, out isMaster, out isChild, out expanded);

            // Ligne sélectionnée = fond bleu, donc texte clair pour rester lisible
            bool selected = (e.State & DataGridViewElementStates.Selected) != 0;
            Color mainColor = selected ? Color.White : Color.FromArgb(40, 40, 40);
            Color softColor = selected ? Color.FromArgb(220, 230, 245) : Color.Gray;

            var flags = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter;

            var leftRect = new Rectangle(e.CellBounds.Left, e.CellBounds.Top, e.CellBounds.Width - FamilyMenuZoneWidth, e.CellBounds.Height);

            // Petit bouton encadré, centré verticalement, pour que "⋯" ressemble à un vrai bouton
            var menuBox = new Rectangle(e.CellBounds.Right - FamilyMenuZoneWidth + 3,
                                        e.CellBounds.Top + (e.CellBounds.Height - 28) / 2,
                                        FamilyMenuZoneWidth - 6, 28);

            using (var arrowFont = new Font("Segoe UI Symbol", 12, FontStyle.Bold))
            using (var dotsFont = new Font("Segoe UI", 13, FontStyle.Bold))
            using (var borderPen = new Pen(softColor))
            {
                if (isMaster)
                {
                    int familySize = CampaignHierarchy.GetChildren(name).Count + 1; // +1 = la campagne mère elle-même
                    string text = (expanded ? "▼ " : "▶ ") + "★ " + familySize;
                    TextRenderer.DrawText(e.Graphics, text, arrowFont, leftRect, mainColor, flags);
                }
                else if (isChild)
                {
                    TextRenderer.DrawText(e.Graphics, "↳", arrowFont, leftRect, softColor, flags);
                }

                e.Graphics.DrawRectangle(borderPen, menuBox);
                TextRenderer.DrawText(e.Graphics, "⋯", dotsFont, menuBox, mainColor, flags);
            }
        }

        // Dessine l'en-tête de la colonne Family : 2 petits boutons, ▶ (tout replier) et ▼ (tout déplier).
        private void GridCampaigns_Family_HeaderPainting(object sender, DataGridViewCellPaintingEventArgs e)
        {
            if (e.RowIndex != -1 || e.ColumnIndex < 0 || _mainForm.dataGridViewCampaigns.Columns[e.ColumnIndex].Name != "Family")
                return;

            e.Paint(e.ClipBounds, DataGridViewPaintParts.Background | DataGridViewPaintParts.Border);
            e.Handled = true;

            int half = e.CellBounds.Width / 2;
            var flags = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter;

            var leftBox = new Rectangle(e.CellBounds.Left + 6, e.CellBounds.Top + 5, half - 9, e.CellBounds.Height - 10);
            var rightBox = new Rectangle(e.CellBounds.Left + half + 3, e.CellBounds.Top + 5, half - 9, e.CellBounds.Height - 10);

            using (var font = new Font("Segoe UI Symbol", 9, FontStyle.Bold))
            using (var pen = new Pen(Color.Gray))
            {
                e.Graphics.DrawRectangle(pen, leftBox);
                e.Graphics.DrawRectangle(pen, rightBox);

                TextRenderer.DrawText(e.Graphics, "▶", font, leftBox, Color.Black, flags);
                TextRenderer.DrawText(e.Graphics, "▼", font, rightBox, Color.Black, flags);
            }
        }

        // e.X est relatif à la cellule dans CellMouseClick (comme pour QuickActions).
        private async void GridCampaigns_Family_CellMouseClickAsync(object sender, DataGridViewCellMouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left || e.RowIndex < 0)
                return;

            if (_mainForm.dataGridViewCampaigns.Columns[e.ColumnIndex].Name != "Family")
                return;

            var row = _mainForm.dataGridViewCampaigns.Rows[e.RowIndex];
            string name = row.Cells["Name"].Value?.ToString();

            if (DeleteSelectedRowTag.Equals(row.Tag) || string.IsNullOrEmpty(name) || _incompleteOrOrphanNames.Contains(name))
                return;

            int colWidth = _mainForm.dataGridViewCampaigns.Columns["Family"].Width;
            bool inMenuZone = e.X >= colWidth - FamilyMenuZoneWidth;

            bool isMaster, isChild, expanded;
            GetFamilyRole(name, out isMaster, out isChild, out expanded);

            if (!inMenuZone)
            {
                // Zone gauche : bascule déplié/replié, seulement si c'est un maître.
                if (!isMaster)
                    return;

                if (expanded)
                {
                    _expandedMasters.Remove(name);
                }
                else
                {
                    _expandedMasters.Clear(); // une seule famille dépliée à la fois
                    _expandedMasters.Add(name);
                }

                // Si elle est devenue fille, on déplie sa nouvelle famille (et seulement elle)
                // pour ne pas la faire disparaître de la liste.
                if (CampaignHierarchy.IsChild(name))
                {
                    _expandedMasters.Clear();
                    _expandedMasters.Add(CampaignHierarchy.ResolveMaster(name));
                }

                // keepRightPanel : le panneau de droite garde la campagne affichée
                await LoadCampaignsAsync(selectCampaignName: name, keepRightPanel: true);
            }
            else
            {
                // Zone droite : ouvre la popup de gestion, pour tout le monde (maître,
                // fille ou campagne seule - elle peut y être rattachée à une autre).
                bool applied;

                using (var form = new ManageFamily_Form(name))
                {
                    applied = form.ShowDialog(_mainForm) == DialogResult.OK;
                }

                if (applied)
                {
                    // On déplie la famille de cette campagne (et seulement elle) pour qu'elle
                    // reste visible : celle de sa mère si elle est fille, la sienne si elle est mère.
                    string familyToShow = CampaignHierarchy.IsChild(name) ? CampaignHierarchy.ResolveMaster(name)
                                        : (CampaignHierarchy.IsMaster(name) ? name : null);

                    if (familyToShow != null)
                    {
                        _expandedMasters.Clear();
                        _expandedMasters.Add(familyToShow);
                    }
                }

                await LoadCampaignsAsync(selectCampaignName: name, keepRightPanel: true);
            }
        }

        // Curseur main uniquement sur ce qui est vraiment cliquable + infobulle maison
        // (on réutilise celle de QuickActions, voir ShowQuickActionsTip).
        private void GridCampaigns_Family_CellMouseMove(object sender, DataGridViewCellMouseEventArgs e)
        {
            var grid = _mainForm.dataGridViewCampaigns;

            if (e.RowIndex < 0 || grid.Columns[e.ColumnIndex].Name != "Family")
            {
                if (_lastFamilyMouseX >= 0)
                {
                    grid.Cursor = Cursors.Default;
                    HideQuickActionsToolTip();
                }

                _lastFamilyMouseX = -1;
                return;
            }

            _lastFamilyMouseX = e.X;

            var row = grid.Rows[e.RowIndex];
            string name = row.Cells["Name"].Value?.ToString();

            if (DeleteSelectedRowTag.Equals(row.Tag) || string.IsNullOrEmpty(name) || _incompleteOrOrphanNames.Contains(name))
            {
                grid.Cursor = Cursors.Default;
                HideQuickActionsToolTip();
                return;
            }

            bool isMaster, isChild, expanded;
            GetFamilyRole(name, out isMaster, out isChild, out expanded);

            bool inMenuZone = e.X >= grid.Columns["Family"].Width - FamilyMenuZoneWidth;

            string text = null;

            if (inMenuZone)
                text = "Open the family window";
            else if (isMaster)
                text = (expanded ? "Click to hide the " : "Click to show the ") + CampaignHierarchy.GetChildren(name).Count + " child campaign(s)";
            else if (isChild)
                text = "Child of: " + CampaignHierarchy.ResolveMaster(name);

            grid.Cursor = (inMenuZone || isMaster) ? Cursors.Hand : Cursors.Default;

            if (text == null)
            {
                HideQuickActionsToolTip();
                return;
            }

            // Clé propre à Family (>= 1000000) pour ne pas se mélanger avec celles de QuickActions
            int key = 1000000 + e.RowIndex * 2 + (inMenuZone ? 1 : 0);

            if (key == _lastQuickActionsZone)
                return; // même zone, l'infobulle est déjà affichée

            _lastQuickActionsZone = key;

            Rectangle cell = grid.GetCellDisplayRectangle(e.ColumnIndex, e.RowIndex, false);
            ShowQuickActionsTip(text, cell.Left + e.X + 12, cell.Top + e.Y + 22);
        }

        private void GridCampaigns_Family_CellMouseLeave(object sender, DataGridViewCellEventArgs e)
        {
            if (e.ColumnIndex >= 0 && _mainForm.dataGridViewCampaigns.Columns[e.ColumnIndex].Name == "Family")
            {
                _mainForm.dataGridViewCampaigns.Cursor = Cursors.Default;
                _lastFamilyMouseX = -1;
                HideQuickActionsToolTip();
            }
        }

        // Clic sur l'en-tête de la colonne Family : moitié gauche (▶) = tout replier,
        // moitié droite (▼) = tout déplier. e.X est relatif à la cellule d'en-tête.
        private async void GridCampaigns_Family_HeaderMouseClickAsync(object sender, DataGridViewCellMouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left || e.ColumnIndex < 0)
                return;

            if (_mainForm.dataGridViewCampaigns.Columns[e.ColumnIndex].Name != "Family")
                return;

            bool expandAll = e.X >= _mainForm.dataGridViewCampaigns.Columns["Family"].Width / 2;

            _expandedMasters.Clear();

            if (expandAll)
            {
                foreach (string master in CampaignHierarchy.GetAllMasters())
                    _expandedMasters.Add(master);
            }

            await LoadCampaignsAsync(keepRightPanel: true);
        }
    }
}
