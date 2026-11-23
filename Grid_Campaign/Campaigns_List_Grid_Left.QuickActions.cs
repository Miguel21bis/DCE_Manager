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
    // Colonne "Actions" de la grille : les icônes First (▶) / Skip (⏭) / Debrief (📋), leur infobulle et leur clic.
    // Partie de CampaignGridLeft, voir Campaigns_List_Grid_Left.cs
    //
    // Clic gauche : lance l'action. Clic droit sur ▶ (CampaignMaker seulement) : petite fenêtre
    // "Launch options" (voir LaunchOptions_Form). Les options ne valent que pour CE lancement : rien n'est retenu.
    public partial class CampaignGridLeft
    {
        private int _lastQuickActionsMouseX = -1;

        // Infobulle des icônes QuickActions, pilotée à la main (voir CellMouseMove).
        // _lastQuickActionsZone = ligne * 3 + zone sous la souris, pour ne ré-afficher que si on change d'icône.
        // Infobulle maison : un simple Label posé sur la grid (le ToolTip standard ne s'affiche pas sur ce contrôle)
        private Label _quickActionsTip;
        private int _lastQuickActionsZone = -1;

        // Colonne "First + Skip + Debrief" regroupée en une seule case (gain de place).
        // Ce n'est PAS une colonne bouton : les 3 icônes sont dessinées à la main dans
        // GridCampaigns_QuickActions_CellPainting, et le clic est découpé en 3 zones (tiers
        // de la largeur de la cellule) dans GridCampaigns_QuickActions_CellMouseClick.
        private void GridCampaigns_AddQuickActionsColumn()
        {
            _mainForm.dataGridViewCampaigns.Columns.Add(new DataGridViewTextBoxColumn()
            {
                Name = "QuickActions",
                HeaderText = "Actions",
                Width = 100, // avant 130 : resserre les 3 icônes pour laisser de la place à Campaign
                ReadOnly = true,
                DefaultCellStyle = { Alignment = DataGridViewContentAlignment.MiddleCenter }
            });
        }

        // Centralise les règles de visibilité des 3 icônes, utilisées par le dessin
        // (CellPainting), le clic (CellMouseClick) et le survol (CellMouseMove).
        private void GetQuickActionVisibility(int rowIndex, out bool showFirst, out bool showSkip, out bool showDebrief)
        {
            var row = _mainForm.dataGridViewCampaigns.Rows[rowIndex];

            int nbMission;
            int.TryParse(row.Cells["Missions"].Value?.ToString(), out nbMission);

            showFirst = true;
            showSkip = nbMission >= 1;
            showDebrief = "1".Equals(row.Cells["DebriefPending"].Value?.ToString());
        }

        // Numéro de la zone (0 = ▶, 1 = ⏭, 2 = 📋) sous la souris. mouseX est relatif à la cellule.
        private int GetQuickActionZone(int mouseX)
        {
            int thirdWidth = _mainForm.dataGridViewCampaigns.Columns["QuickActions"].Width / 3;
            return Math.Min(mouseX / thirdWidth, 2);
        }

        // Peint les 1 à 3 icônes de la colonne QuickActions. Toujours 3 emplacements de
        // largeur égale (un tiers de cellule chacun), qu'ils soient utilisés ou non : ça
        // garde le découpage des clics simple et stable, peu importe la combinaison visible.
        private void GridCampaigns_QuickActions_CellPainting(object sender, DataGridViewCellPaintingEventArgs e)
        {
            if (e.RowIndex < 0 || _mainForm.dataGridViewCampaigns.Columns[e.ColumnIndex].Name != "QuickActions")
                return;

            e.PaintBackground(e.ClipBounds, true);
            e.Handled = true;

            var row = _mainForm.dataGridViewCampaigns.Rows[e.RowIndex];
            string name = row.Cells["Name"].Value?.ToString();

            if (DeleteSelectedRowTag.Equals(row.Tag) || string.IsNullOrEmpty(name))
                return;

            if (_incompleteOrOrphanNames.Contains(name))
            {
                if (_repairableIncompleteNames.Contains(name))
                {
                    using (var repairFont = new Font("Segoe UI", 14, FontStyle.Bold))
                    {
                        DrawQuickActionIcon(e.Graphics, "🔧", true, e.CellBounds.Left, e.CellBounds.Width, e.CellBounds.Top, e.CellBounds.Height, repairFont);
                    }
                }

                return; // ligne orpheline non réparable, ou 🔧 déjà dessiné : rien d'autre à peindre ici
            }

            // ScriptsMod absent : un seul ⚠ orange à la place des 3 icônes (First/Skip/Debrief impossibles)
            if (!_scriptsModInstalled)
            {
                using (var warnFont = new Font("Segoe UI Symbol", 16, FontStyle.Bold))
                {
                    TextRenderer.DrawText(e.Graphics, "⚠", warnFont, e.CellBounds, Color.DarkOrange,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                }

                return;
            }

            bool showFirst, showSkip, showDebrief;
            GetQuickActionVisibility(e.RowIndex, out showFirst, out showSkip, out showDebrief);

            int thirdWidth = e.CellBounds.Width / 3;

            using (var font = new Font("Segoe UI", 14, FontStyle.Bold))
            {
                DrawQuickActionIcon(e.Graphics, "▶", showFirst, e.CellBounds.Left, thirdWidth, e.CellBounds.Top, e.CellBounds.Height, font);
                DrawQuickActionIcon(e.Graphics, "⏭", showSkip, e.CellBounds.Left + thirdWidth, thirdWidth, e.CellBounds.Top, e.CellBounds.Height, font);
                DrawQuickActionIcon(e.Graphics, "📋", showDebrief, e.CellBounds.Left + 2 * thirdWidth, thirdWidth, e.CellBounds.Top, e.CellBounds.Height, font);
            }
        }

        // Vrai si la zone (tiers de cellule) sous la souris correspond à une icône
        // effectivement affichée pour cette ligne. Sert au clic ET au curseur main.
        private bool IsQuickActionZoneActive(int rowIndex, int mouseX)
        {
            var row = _mainForm.dataGridViewCampaigns.Rows[rowIndex];
            string name = row.Cells["Name"].Value?.ToString();

            if (string.IsNullOrEmpty(name))
                return false;

            if (_incompleteOrOrphanNames.Contains(name))
                return _repairableIncompleteNames.Contains(name); // toute la cellule = zone "Repair"

            if (!_scriptsModInstalled)
                return true; // toute la cellule = zone "ScriptsMod manquant"

            bool showFirst, showSkip, showDebrief;
            GetQuickActionVisibility(rowIndex, out showFirst, out showSkip, out showDebrief);

            int zone = GetQuickActionZone(mouseX);

            if (zone == 0) return showFirst;
            if (zone == 1) return showSkip;
            return showDebrief;
        }

        private void DrawQuickActionIcon(Graphics g, string icon, bool visible, int x, int width, int y, int height, Font font)
        {
            if (!visible)
                return;

            var rect = new Rectangle(x, y, width, height);
            TextRenderer.DrawText(g, icon, font, rect, Color.Black, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }

        // Découpe le clic sur la colonne QuickActions en 3 zones (mêmes tiers que le dessin).
        // e.X/e.Y sont relatifs à la cellule dans CellMouseClick (contrairement à CellClick).
        // Clic gauche : First / Skip / Debrief. Clic droit : seulement sur ▶ et seulement en CampaignMaker
        // (fenêtre "Launch options").
        private async void GridCampaigns_QuickActions_CellMouseClickAsync(object sender, DataGridViewCellMouseEventArgs e)
        {
            bool isLeft = e.Button == MouseButtons.Left;
            bool isRight = e.Button == MouseButtons.Right;

            if (!isLeft && !isRight)
                return;

            if (e.RowIndex < 0 || _mainForm.dataGridViewCampaigns.Columns[e.ColumnIndex].Name != "QuickActions")
                return;

            // Clic droit : réservé au CampaignMaker, et seulement sur l'icône ▶
            if (isRight && (ParamConf.UserLevel != UserLevel.CampaignMaker || GetQuickActionZone(e.X) != 0))
                return;

            if (!IsQuickActionZoneActive(e.RowIndex, e.X))
                return; // zone vide, pas d'action

            HideQuickActionsToolTip();

            var row = _mainForm.dataGridViewCampaigns.Rows[e.RowIndex];
            string name = row.Cells["Name"].Value?.ToString();

            if (_repairableIncompleteNames.Contains(name))
            {
                if (isLeft)
                    await RepairCampaignAsync(name);   // le clic droit n'a rien à proposer sur une ligne à réparer
                return;
            }

            EnsureCampaignFilesUpToDate(name);


            if (!_scriptsModInstalled)
            {
                MessageBox.Show(
                    "ScriptsMod is not installed yet.\r\n\r\nIt is the engine that builds the missions.\r\nGo to the Update tab and click Install.",
                    "ScriptsMod missing",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            int zone = GetQuickActionZone(e.X);

            string folderPath = Path.Combine(ParamConf.PATH_SavedGames_DCS + @"\Mods\tech\DCE\Missions\Campaigns\", name);

            if (zone == 0)
            {
                bool scanRelief = false;

                if (isRight)
                {
                    using (var optionsForm = new LaunchOptions_Form(name))
                    {
                        if (optionsForm.ShowDialog(_mainForm) != DialogResult.OK)
                            return;   // fenêtre fermée ou Cancel : rien ne démarre

                        scanRelief = optionsForm.ScanRelief;
                    }
                }

                await LaunchFirstMissionAsync(row, name, folderPath, scanRelief);
            }
            else if (zone == 1)
            {
                WargameEngineLosses.ProcessBeforeMission(name);
                Saver_TargetList_Wargame.WriteNewActiveFormations(name);
                await RunScriptsModInteractiveAsync(name, folderPath, "SkipMission.bat");
            }
            else
            {
                await RunScriptsModInteractiveAsync(name, folderPath, "DEBUG_DebriefMission.bat", "Debrief_Master.lua");
            }
        }

        // First Mission : même chemin pour le clic gauche (sans option) et pour la fenêtre "Launch options".
        private async Task LaunchFirstMissionAsync(DataGridViewRow row, string name, string folderPath, bool scanRelief)
        {
            // First Mission repart de zéro : si des missions ont déjà été jouées,
            // on prévient avant d'écraser la progression.
            int nbMissionPlayed;
            int.TryParse(row.Cells["Missions"].Value?.ToString(), out nbMissionPlayed);

            if (nbMissionPlayed >= 1)
            {
                var confirm = MessageBox.Show(
                    "Campaign '" + name + "' already has " + nbMissionPlayed + " mission(s) played.\r\n\r\n" +
                    "Running First Mission will restart the campaign from the beginning and ERASE all its current progress.\r\n\r\n" +
                    "Are you sure?",
                    "Restart campaign — " + name,
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning,
                    MessageBoxDefaultButton.Button2);

                if (confirm != DialogResult.Yes)
                    return;
            }

            WargameLog.FirstMission(name);
            await RunScriptsModInteractiveAsync(name, folderPath, "FirstMission.bat", null, scanRelief);
        }

        // Lance FirstMission.bat / SkipMission.bat / DEBUG_DebriefMission.bat en tâche de fond
        // (fenêtre cachée) et pilote l'interaction console (ScriptsMod) via une Form dédiée, au
        // lieu d'une fenêtre console visible.
        // luaScriptName : uniquement pour les .bat hors convention BAT_xxx.lua (ex: Debriefing,
        // qui appelle Debrief_Master.lua directement) - laisser null pour First/Skip.
        // scanRelief : option de lancement (clic droit sur ▶), transmise au runner puis à ScriptsMod.
        private async Task RunScriptsModInteractiveAsync(string campaignName, string folderPath, string batFileName, string luaScriptName = null, bool scanRelief = false)
        {
            string batPath = Path.Combine(folderPath, batFileName);

            if (!File.Exists(batPath))
                return;

            using (var runnerForm = new ScriptsModRunner_Form(batPath, folderPath, campaignName, luaScriptName, scanRelief))
            {
                runnerForm.ShowDialog(_mainForm);
            }

            // Que la mission ait été générée ou que l'utilisateur ait fermé en cours de route,
            // on rafraîchit la liste (nombre de missions, bouton Skip, icône Debriefing... peuvent avoir changé).
            await LoadCampaignsAsync(selectCampaignName: campaignName);
        }

        // Texte d'infobulle (en anglais, comme le reste de l'UI visible) pour l'icône
        // actuellement sous la souris. N'est appelé que si IsQuickActionZoneActive a déjà
        // confirmé que l'icône concernée est bien affichée pour cette ligne.
        // 1re ligne = la fonction principale du bouton. 2e ligne (CampaignMaker, ▶ seulement) = l'option du clic droit.
        private string GetQuickActionTooltipText(int mouseX)
        {
            switch (GetQuickActionZone(mouseX))
            {
                case 0:
                    return ParamConf.UserLevel == UserLevel.CampaignMaker
                        ? "Generate the first mission of this campaign\nRight click: launch options"
                        : "Generate the first mission of this campaign";
                case 1: return "Skip the current mission and generate the next one";
                default: return "Debrief the last played mission";
            }
        }

        private void GridCampaigns_QuickActions_CellMouseMove(object sender, DataGridViewCellMouseEventArgs e)
        {
            var grid = _mainForm.dataGridViewCampaigns;

            if (e.RowIndex < 0 || grid.Columns[e.ColumnIndex].Name != "QuickActions")
            {
                grid.Cursor = Cursors.Default;
                _lastQuickActionsMouseX = -1;

                if (e.ColumnIndex < 0 || grid.Columns[e.ColumnIndex].Name != "Family") // Family gère sa propre infobulle
                    HideQuickActionsToolTip();

                return;
            }

            _lastQuickActionsMouseX = e.X;

            bool zoneActive = IsQuickActionZoneActive(e.RowIndex, e.X);
            grid.Cursor = zoneActive ? Cursors.Hand : Cursors.Default;

            if (!zoneActive)
            {
                HideQuickActionsToolTip();
                return;
            }

            // Ligne "problème" réparable : toute la cellule = Repair, pas de découpage en tiers
            string name = grid.Rows[e.RowIndex].Cells["Name"].Value?.ToString();
            int zone;
            string text;

            if (_incompleteOrOrphanNames.Contains(name))
            {
                zone = 0;
                text = "Repair this campaign (recreate the missing files)";
            }
            else if (!_scriptsModInstalled)
            {
                zone = 0;
                text = "ScriptsMod is not installed. Go to the Update tab and click Install.";
            }
            else
            {
                zone = GetQuickActionZone(e.X);
                text = GetQuickActionTooltipText(e.X);
            }

            int key = e.RowIndex * 3 + zone;

            if (key == _lastQuickActionsZone)
                return; // même icône, l'infobulle est déjà affichée

            _lastQuickActionsZone = key;

            Rectangle cell = grid.GetCellDisplayRectangle(e.ColumnIndex, e.RowIndex, false);
            ShowQuickActionsTip(text, cell.Left + e.X + 12, cell.Top + e.Y + 22);
        }

        private void GridCampaigns_QuickActions_CellMouseLeave(object sender, DataGridViewCellEventArgs e)
        {
            if (e.ColumnIndex >= 0 && _mainForm.dataGridViewCampaigns.Columns[e.ColumnIndex].Name == "QuickActions")
            {
                _mainForm.dataGridViewCampaigns.Cursor = Cursors.Default;
                _lastQuickActionsMouseX = -1;
                HideQuickActionsToolTip();
            }
        }


        private void HideQuickActionsToolTip()
        {
            if (_lastQuickActionsZone == -1)
                return;

            _lastQuickActionsZone = -1;

            if (_quickActionsTip != null)
                _quickActionsTip.Visible = false;
        }

        // Affiche le Label d'infobulle aux coordonnées (x, y) de la grid.
        // Créé au premier appel, puis simplement déplacé.
        private void ShowQuickActionsTip(string text, int x, int y)
        {
            var grid = _mainForm.dataGridViewCampaigns;

            if (_quickActionsTip == null)
            {
                _quickActionsTip = new Label
                {
                    AutoSize = true,
                    BackColor = Color.LightYellow,
                    ForeColor = Color.Black,
                    BorderStyle = BorderStyle.FixedSingle,
                    Font = new Font("Segoe UI", 9),
                    Padding = new Padding(4, 2, 4, 2),
                    Visible = false
                };
                grid.Controls.Add(_quickActionsTip);
            }

            _quickActionsTip.Text = text;

            // Empêche l'infobulle de dépasser du bord droit de la grid
            int width = _quickActionsTip.PreferredSize.Width;
            if (x + width > grid.ClientSize.Width)
                x = Math.Max(0, grid.ClientSize.Width - width);

            _quickActionsTip.Location = new Point(x, y);
            _quickActionsTip.Visible = true;
            _quickActionsTip.BringToFront();
        }
    }
}
