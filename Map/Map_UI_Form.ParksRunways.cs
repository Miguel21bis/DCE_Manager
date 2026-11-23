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
    // Partie de Map_UI_Form (voir Map_UI_Form.cs) : pistes du web, places d'hélicoptère, saisie des pistes
    internal partial class Map_UI_Form : Form
    {
        // Le bouton "pistes du web" reflète le réglage du théâtre (enregistré ou en attente)
        private void SyncWebRunwaysButton()
        {
            if (_chkWebRunways == null) return;
            _syncWeb = true;
            _chkWebRunways.Checked = Map_RunwayData.GetSource(Map_RunwayData.TheatreOf(_campaign)) == "web";
            _syncWeb = false;
        }

        // Clic sur le bouton : pistes du web (on ignore les données locales) ou retour aux données locales. Écrit au Save seulement.
        private void SetWebRunways(bool on)
        {
            string theatre = Map_RunwayData.TheatreOf(_campaign);
            if (theatre == "")
            {
                _lblStatus.Text = "Runways: the theatre of this campaign is unknown, the web runways can not be chosen.";
                SyncWebRunwaysButton();
                return;
            }

            Map_RunwayData.SetWebView(theatre, on, _airbases.Keys);
            UpdateTitle();
            UpdateSaveWpt();
            ApplyMode();
            _lblStatus.Text = on
                ? "Runways: web data shown, local data ignored. Click " + (_missionMode ? "Save modification" : "Save") + " to keep it (your own runway settings are then removed)."
                : "Runways: local data shown again.";
        }

        // ------------------------------------------------------------------
        //  Places d'hélicoptère (parkAlertSAR de db_airbases.lua)
        //  Carte Map : le dossier regardé (Init ou Active). Carte de mission : <campagne>_first.miz -> Init, <campagne>_ongoing.miz -> Active
        //  (autre nom de fichier : selon Init / Active). Un jeu de places et ses changements en attente par dossier.
        // ------------------------------------------------------------------

        private string ParkFolder()
        {
            if (_missionMode)
            {
                string n = Path.GetFileNameWithoutExtension(_missionFile ?? "");
                if (n.EndsWith("_first", StringComparison.OrdinalIgnoreCase)) return "Init";
                if (n.EndsWith("_ongoing", StringComparison.OrdinalIgnoreCase)) return "Active";
            }
            return _radioActive != null && _radioActive.Checked ? "Active" : "Init";
        }

        private string ParkFile(string folder)
        {
            return Path.Combine(ParamConf.PATH_SavedGames_DCS, @"Mods\tech\DCE\Missions\Campaigns", _campaign, folder, "db_airbases.lua");
        }

        private Dictionary<string, List<Map_ParkSpot>> ParkStoreOf(string folder)
        {
            Dictionary<string, List<Map_ParkSpot>> s;
            if (!_parkStores.TryGetValue(folder, out s))
            {
                s = new Dictionary<string, List<Map_ParkSpot>>(StringComparer.OrdinalIgnoreCase);
                _parkStores[folder] = s;
            }
            return s;
        }

        private HashSet<string> ParkDirtyOf(string folder)
        {
            HashSet<string> d;
            if (!_parkDirties.TryGetValue(folder, out d))
            {
                d = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                _parkDirties[folder] = d;
            }
            return d;
        }

        // Le jeu de places et les changements en attente du dossier en cours
        private Dictionary<string, List<Map_ParkSpot>> _parkStore { get { return ParkStoreOf(ParkFolder()); } }
        private HashSet<string> _parkDirty { get { return ParkDirtyOf(ParkFolder()); } }
        private bool ParkAnyDirty { get { return _parkDirties.Values.Any(d => d.Count > 0); } }

        // Relit les fichiers (sauf un dossier qui a des changements en attente)
        private void LoadParks()
        {
            foreach (string f in new[] { "Init", "Active" })
                if (ParkDirtyOf(f).Count == 0) _parkStores.Remove(f);
            EnsureParks();
        }

        // Lit le dossier en cours s'il ne l'a pas encore été
        private void EnsureParks()
        {
            string folder = ParkFolder();
            if (!_parkStores.ContainsKey(folder))
                _parkStores[folder] = Map_Drawings.ReadParkSpots(ParkFile(folder));
        }

        // db_airbases relu (Init puis Active) : les points SAR utilisés pour placer les avions d'un vol suivent les places changées
        private void ReloadAirbases()
        {
            try
            {
                var loaded = new Parser_db_airbases().Load_db_airbases(_campaign);
                _airbases = new Dictionary<string, AirbaseInfo>(loaded, StringComparer.OrdinalIgnoreCase);
                if (_ctx != null) _ctx.Airbases = _airbases;
            }
            catch (Exception ex)
            {
                FormUtils.LogRegister("Map_UI_Form | db_airbases (reload) : " + ex.Message);
            }
        }

        private List<Map_ParkSpot> ParksOf(string baseName)
        {
            Dictionary<string, List<Map_ParkSpot>> st;
            List<Map_ParkSpot> l;
            return _parkStores.TryGetValue(ParkFolder(), out st) && st.TryGetValue(baseName, out l) ? l : new List<Map_ParkSpot>();
        }

        private void ParkToggled()
        {
            bool on = _chkPark.Checked;
            _view.ParkShow = on;
            if (_parkBar != null) _parkBar.Visible = on;
            _parkCur = null;
            if (_missionMode && on && _tabs != null && _tabBases != null) _tabs.SelectedTab = _tabBases;
            ResizeBaseHost();
            UpdateParkBar();
        }

        private void MarkParkDirty(string baseName)
        {
            _parkDirty.Add(baseName);
            UpdateTitle();
            if (_missionMode) UpdateSaveWpt();
        }

        // Boutons et cases de la barre : selon la place choisie
        private void UpdateParkBar()
        {
            if (_parkBar == null) return;
            _parkSync = true;
            Map_ParkSpot s = _parkCur;
            _numParkHdg.Enabled = s != null;
            _btnParkDel.Enabled = s != null;
            _btnParkKeep.Enabled = s != null && !s.HasHdg;
            if (s != null)
            {
                double deg = ((s.Hdg * 180.0 / Math.PI) % 360 + 360) % 360;
                _numParkHdg.Value = (decimal)Math.Min(359.9, Math.Round(deg, 1));
                _lblParkRad.Text = "= " + s.Hdg.ToString("0.000", CultureInfo.InvariantCulture) + " rad";
            }
            else
            {
                _numParkHdg.Value = 0;
                _lblParkRad.Text = "";
            }

            Map_Base b = _view.GetBase(_selectedBase);
            _btnParkAdd.Enabled = b != null;
            int n = b == null ? 0 : b.Parks.Count;
            _lblParkInfo.Text = b == null ? "Click a base, then a spot (or + Add spot)."
                : b.Name + " (" + ParkFolder() + "): " + n + " spot(s)." + (s != null && !s.HasHdg ? " This spot has no heading in the file (shown facing north)." : "");
            _parkSync = false;
        }

        private void OnParkSelected(Map_ParkSpot s)
        {
            _parkCur = s;
            if (s != null && !string.Equals(_selectedBase, s.Base, StringComparison.OrdinalIgnoreCase))
            {
                _view.SelectedName = s.Base;
                ShowBase(s.Base);
            }
            UpdateParkBar();
        }

        private void OnParkChanged(Map_ParkSpot s)
        {
            if (ReferenceEquals(s, _parkCur)) UpdateParkBar();      // le cap affiché suit la souris
        }

        private void OnParksEdited(string baseName)
        {
            MarkParkDirty(baseName);
            _lblStatus.Text = "Helicopter spots of " + baseName + " changed: click " + (_missionMode ? "Save modification" : "Save") + " to keep them.";
        }

        private void ParkAddPressed()
        {
            if (_view.GetBase(_selectedBase) == null) { _lblStatus.Text = "Helicopter spots: click a base first."; return; }
            _view.ParkAddMode = true;
            _lblStatus.Text = "Helicopter spots: click on the map where the new spot goes (Esc = cancel).";
        }

        private void OnParkAddClicked(double x, double y)
        {
            Map_Base b = _view.GetBase(_selectedBase);
            if (b == null) return;

            List<Map_ParkSpot> list;
            if (!_parkStore.TryGetValue(b.Name, out list)) { list = new List<Map_ParkSpot>(); _parkStore[b.Name] = list; }
            b.Parks = list;

            var s = new Map_ParkSpot { Base = b.Name, X = x, Y = y, Hdg = 0, HasHdg = true };
            list.Add(s);
            _parkCur = s;
            _view.SelectedPark = s;
            MarkParkDirty(b.Name);
            UpdateParkBar();
            _lblStatus.Text = "Helicopter spot added on " + b.Name + " (facing north). Turn it with the round handle, then click " + (_missionMode ? "Save modification" : "Save") + ".";
        }

        private void ParkDeletePressed()
        {
            Map_ParkSpot s = _parkCur;
            if (s == null) return;

            List<Map_ParkSpot> list;
            if (_parkStore.TryGetValue(s.Base, out list)) list.Remove(s);
            _parkCur = null;
            _view.SelectedPark = null;
            MarkParkDirty(s.Base);
            UpdateParkBar();
            _lblStatus.Text = "Helicopter spot removed from " + s.Base + ": click " + (_missionMode ? "Save modification" : "Save") + " to keep it." + (_missionMode ? " (A helicopter of the mission standing on it stays where it is.)" : "");
        }

        private void ParkKeepPressed()
        {
            if (_parkCur == null) return;
            _parkCur.HasHdg = true;
            MarkParkDirty(_parkCur.Base);
            UpdateParkBar();
            _view.Invalidate();
        }

        private void ParkHeadingChanged()
        {
            if (_parkSync || _parkCur == null) return;
            _parkCur.Hdg = (double)_numParkHdg.Value * Math.PI / 180.0;
            _parkCur.HasHdg = true;
            MarkParkDirty(_parkCur.Base);
            _lblParkRad.Text = "= " + _parkCur.Hdg.ToString("0.000", CultureInfo.InvariantCulture) + " rad";
            _btnParkKeep.Enabled = false;
            _view.Invalidate();
        }

        // Écrit les places changées (tous les dossiers qui ont des changements). false = échec (message déjà affiché)
        private bool CommitParks()
        {
            foreach (string folder in _parkDirties.Where(kv => kv.Value.Count > 0).Select(kv => kv.Key).ToList())
                if (!CommitParksOf(folder)) return false;
            return true;
        }

        private bool CommitParksOf(string folder)
        {
            HashSet<string> dirty = ParkDirtyOf(folder);
            if (dirty.Count == 0) return true;
            Dictionary<string, List<Map_ParkSpot>> store = ParkStoreOf(folder);

            var wanted = new Dictionary<string, List<Map_ParkSpot>>(StringComparer.OrdinalIgnoreCase);
            foreach (string n in dirty)
            {
                List<Map_ParkSpot> l;
                wanted[n] = store.TryGetValue(n, out l) ? l : new List<Map_ParkSpot>();
            }

            // Carte de mission : les hélicos du .miz posés sur une place qui a bougé la suivent (contrôle d'abord, écriture après db_airbases)
            List<Map_Drawings.ParkMove> moves = null;
            string msg;
            if (_missionMode && _missionFile != "")
            {
                moves = new List<Map_Drawings.ParkMove>();
                foreach (List<Map_ParkSpot> list in wanted.Values)
                    foreach (Map_ParkSpot sp in list)
                    {
                        if (!sp.Changed) continue;
                        double h = ((sp.Hdg % (2 * Math.PI)) + 2 * Math.PI) % (2 * Math.PI);
                        moves.Add(new Map_Drawings.ParkMove { OldX = sp.OrigX, OldY = sp.OrigY, NewX = sp.X, NewY = sp.Y, NewHdg = Math.Round(h, 5), SetHdg = sp.HasHdg });
                    }
                if (moves.Count > 0 && !Map_Drawings.MoveParkUnits(_missionFile, moves, true, out msg))
                {
                    MessageBox.Show(msg, "Save helicopter spots", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return false;
                }
            }

            if (!Map_Drawings.ApplyParkSpots(ParkFile(folder), wanted, out msg))
            {
                MessageBox.Show(msg, "Save helicopter spots", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }

            if (moves != null && moves.Count > 0)
            {
                string msg2;
                if (Map_Drawings.MoveParkUnits(_missionFile, moves, false, out msg2)) msg += "   " + msg2;
                else MessageBox.Show("The spots are saved in " + folder + "\\db_airbases.lua, but the helicopters of the mission file could not be moved:\n\n" + msg2, "Save helicopter spots", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }

            dirty.Clear();
            _parkCur = null;
            _view.SelectedPark = null;
            _parkStores.Remove(folder);
            EnsureParks();
            ReloadAirbases();
            ApplyMode();
            UpdateParkBar();
            UpdateTitle();
            if (_missionMode) UpdateSaveWpt();
            _parkMsg = msg;
            _lblStatus.Text = msg;
            return true;
        }

        // Pistes tirées à la souris : gardées en attente, écrites seulement au clic sur Save
        private void OnRunwaysEdited(string baseName)
        {
            Map_Base b = _view.GetBase(baseName);
            if (b == null) return;

            Map_RunwayData.SetPending(b.Name, b.X, b.Y, b.Runways);
            UpdateTitle();
            _lblStatus.Text = "Runways of " + b.Name + " changed: click Save to keep them.";
        }

        // Saisie exacte : pistes de la base choisie (onglet "This base") + règle de position pour toute la carte (onglet "Whole map")
        private void EditRunwaysData()
        {
            Map_Base b = _view.GetBase(_selectedBase);
            if (b != null && (b.IsFarp || Map_UI_View.IsCarrier(b))) b = null;

            string theatre = Map_RunwayData.TheatreOf(_campaign);
            using (var dlg = new Map_RunwayDialog(b == null ? null : b.Name, b == null ? 0 : b.X, b == null ? 0 : b.Y,
                b == null ? new List<Map_Runway>() : Map_RunwayData.Merged(b),
                theatre, Map_RunwayData.GetAnchorSetting(theatre), Map_RunwayData.AnchorStatus(theatre),
                _background != null ? _background.AnchorInfo : "", Map_RunwayData.GetSource(theatre)))
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;

                if (b != null && dlg.BaseReset) Map_RunwayData.SetPending(b.Name, b.X, b.Y, null);
                else if (b != null && dlg.BaseChanged) Map_RunwayData.SetPending(b.Name, b.X, b.Y, dlg.BaseResult);
                if (dlg.AnchorChanged) Map_RunwayData.SetPendingAnchor(theatre, dlg.AnchorResult);
                if (dlg.SourceChanged)
                {
                    Map_RunwayData.SetPendingSource(theatre, dlg.SourceResult);
                    if (dlg.SourceResult == "web") Map_RunwayData.ResetSettingsOf(_airbases.Keys);      // les réglages à la main de cette carte sont écartés (au prochain Save)
                }

                UpdateTitle();
                _lblStatus.Text = "Runway changes made: click Save to keep them.";
                ApplyMode();
            }
        }
    }
}
