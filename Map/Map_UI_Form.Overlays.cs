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
    // Partie de Map_UI_Form (voir Map_UI_Form.cs) : calques de la carte : dessins, problèmes, éléments au sol, options de debug
    internal partial class Map_UI_Form : Form
    {
        // Frontières / zones du fichier mission : lues une fois, affichées par défaut s'il y en a
        private void LoadDrawings()
        {
            _presetOrig.Clear();
            _presetUnits.Clear();
            _presetFirst.Clear();
            SetGroup("mission", Map_Drawings.Load(_campaign), _chkDrawings, true);
            SetGroup("zones", Map_Drawings.LoadWargameZones(_campaign, _radioActive.Checked), _chkZones, true);
            SetGroup("spawn", Map_Drawings.LoadWargameSpawn(_campaign), _chkSpawn, false);
            if (!_missionMode) LoadAaa(Path.Combine(ParamConf.PATH_SavedGames_DCS, @"Mods\tech\DCE\Missions\Campaigns", _campaign, "Init", "base_mission.miz"));

            if (_missionMode)
            {
                Map_MissionData data = Map_Drawings.LoadMission(_campaign, _justGenerated);
                SetGroup("plans", data.Plans, _chkPlans, true);
                SetGroup("threats", data.Threats, _chkThreats, true);
                SetGroup("ships", data.ShipRoutes, _chkShips, false);       // trajets des navires : à cocher pour les voir
                _ewrCircles = data.Radars;
                _chkRadars.Checked = false;                                 // zones radar : à cocher pour les voir

                // Groupes "hidden" : cachés au départ ; le bouton H (debug / CampaignMaker) les ajoute. Les boutons suivent la liste affichée.
                bool hiddenAllowed = ParamConf.UserLevel == UserLevel.CampaignMaker || IsDebugOn();
                _chkGroundHidden.Enabled = hiddenAllowed && data.HiddenGround.Count > 0;
                _chkGroundHidden.Checked = false;
                _view.SetGround(data.Ground, data.GroundGroups);
                bool hasBlue = data.Ground.Any(u => !u.Red), hasRed = data.Ground.Any(u => u.Red);
                _chkGround.Enabled = data.Ground.Count > 0;
                _chkGroundNames.Enabled = data.Ground.Count > 0;
                if (_chkGroundBlue != null) { _chkGroundBlue.Enabled = hasBlue; _chkGroundRed.Enabled = hasRed; }
                ApplyGround();

                _missionInfo = data.Error != ""
                    ? data.Error
                    : "Mission: " + Path.GetFileName(data.Source) + (data.Fallback ? " (the expected file had no player flight)" : "") + "   |   " + data.PlayerFlights + " player flight(s) + " + data.PackageFlights + " package flight(s) + " + data.OtherFlights + " other flight(s), " + data.SamSites + " SAM site(s) shown, " + data.Ground.Count + " ground element(s) available";
                _lblStatus.Text = _missionInfo;

                SetDefaultCampBoxes(data);      // CampaignMaker : le camp adverse est décoché
                FillFlightList(data);
                SelectMyFlights();              // le vol du joueur et son package sont choisis, la carte se cadre dessus
                RefreshRadars();

                _missionFile = data.Source;
                LoadAaa(data.Source);           // zones AAA du .miz affiché
                _fileIssues = string.IsNullOrEmpty(_missionFile) || _btnIssues == null ? new List<Map_Issue>() : Map_Drawings.CheckMission(_missionFile);
                RefreshIssues();
                UpdateTitle();

                if (_sky != null) _sky.Reload(_campaign, _justGenerated, data.Source == "" ? null : data.Source);
                if (_secStats != null) ShowMissionStats(data.Source == "" ? null : data.Source);
                LoadMissionDebugBox(data.Source == "" ? null : data.Source);
                if (_secSky != null) _secSky.Summary = "mission start " + ((int)(data.StartTime / 3600) % 24).ToString("00") + ":" + ((int)(data.StartTime % 3600) / 60).ToString("00");
            }
        }

        private string _missionInfo = "";

        // ---- Encart "Mission check" ----

        private void PlaceIssues()
        {
            if (_btnIssues == null || _view == null) return;
            _btnIssues.Location = new Point(Math.Max(4, _view.ClientSize.Width - 140), 10);
            _issuesPanel.Location = new Point(Math.Max(4, _view.ClientSize.Width - _issuesPanel.Width - 10), 44);
            _issuesPanel.Height = Math.Max(80, Math.Min(260, _view.ClientSize.Height - 60));
            _btnIssues.BringToFront();
            _issuesPanel.BringToFront();
        }

        // Changements en attente (numéros de queue, noms) qui créeraient un doublon : montrés avant le Save
        private List<Map_Issue> PendingIssues()
        {
            var res = new List<Map_Issue>();
            if (_missionData == null || _unitRows.Count == 0) return res;

            foreach (KeyValuePair<string, List<UnitRow>> kv in _unitRows)
            {
                Map_Flight f = _missionData.Flights.FirstOrDefault(q => q.Name == kv.Key);
                if (f == null || _pendingRemove.ContainsKey(f.Name)) continue;

                var otherNames = new Dictionary<string, string>(StringComparer.Ordinal);
                var otherTails = new Dictionary<string, string>(StringComparer.Ordinal);
                string squad = Map_Drawings.SquadOf(f.Name);
                foreach (Map_Flight q in _missionData.Flights)
                {
                    if (q.Name == f.Name) continue;
                    bool sameSquad = Map_Drawings.SquadOf(q.Name) == squad;
                    foreach (UnitRow r in RowsOf(q))
                    {
                        if (r.Name != "") otherNames[r.Name] = q.Title;
                        if (sameSquad && r.Onboard != "") otherTails[r.Onboard] = q.Title;
                    }
                }

                var seenNames = new HashSet<string>(StringComparer.Ordinal);
                var seenTails = new HashSet<string>(StringComparer.Ordinal);
                foreach (UnitRow r in kv.Value)
                {
                    string nm = (r.Name ?? "").Trim(), tail = (r.Onboard ?? "").Trim();
                    string by;
                    if (nm == "") res.Add(new Map_Issue { Error = true, Text = "Waiting change, " + f.Title + ": an aircraft has no name." });
                    else if (!seenNames.Add(nm)) res.Add(new Map_Issue { Error = true, Text = "Waiting change, " + f.Title + ": two aircraft are named '" + nm + "'." });
                    else if (otherNames.TryGetValue(nm, out by)) res.Add(new Map_Issue { Error = true, Text = "Waiting change, " + f.Title + ": the name '" + nm + "' is already used by " + by + "." });

                    if (tail != "")
                    {
                        if (!seenTails.Add(tail)) res.Add(new Map_Issue { Text = "Waiting change, " + f.Title + ": tail number " + tail + " is used twice in this flight." });
                        else if (otherTails.TryGetValue(tail, out by)) res.Add(new Map_Issue { Text = "Waiting change, " + f.Title + ": tail number " + tail + " is also used by " + by + " (same squadron)." });
                    }
                }
            }
            return res;
        }

        private void RefreshIssues()
        {
            if (_btnIssues == null) return;

            var all = new List<Map_Issue>(_fileIssues);
            all.AddRange(PendingIssues());
            List<Map_Issue> errors = all.Where(x => x.Error).ToList();
            List<Map_Issue> infos = all.Where(x => !x.Error).ToList();

            _btnIssues.Text = (all.Count > 0 ? "\u26A0 " : "\u2714 ") + all.Count;
            _btnIssues.ForeColor = errors.Count > 0 ? Color.Firebrick : (all.Count > 0 ? Color.DarkOrange : Color.SeaGreen);
            _btnIssues.BackColor = all.Count > 0 ? Color.FromArgb(255, 244, 214) : Color.White;

            _issuesBox.Clear();
            if (all.Count == 0)
            {
                IssueLine("No problem found in the mission file.", Color.SeaGreen, true);
            }
            else
            {
                if (errors.Count > 0)
                {
                    IssueLine("CRITICAL (" + errors.Count + ") : duplicate ids or names, they can break the mission", Color.Firebrick, true);
                    foreach (Map_Issue i in errors) IssueLine("  " + i.Text, Color.Firebrick, false);
                    IssueLine("", Color.Black, false);
                }
                if (infos.Count > 0)
                {
                    IssueLine("INFO (" + infos.Count + ") : tail numbers used twice in the same squadron, usually harmless", Color.DarkOrange, true);
                    foreach (Map_Issue i in infos) IssueLine("  " + i.Text, Color.DarkOrange, false);
                    IssueLine("", Color.Black, false);
                }
                IssueLine("Problems already in the file do not block a change that does not make them worse.", Color.Gray, false);
            }
            _issuesBox.Select(0, 0);
        }

        private void IssueLine(string text, Color color, bool bold)
        {
            _issuesBox.SelectionStart = _issuesBox.TextLength;
            _issuesBox.SelectionLength = 0;
            _issuesBox.SelectionColor = color;
            _issuesBox.SelectionFont = new Font(_issuesBox.Font, bold ? FontStyle.Bold : FontStyle.Regular);
            _issuesBox.AppendText(text + "\n");
        }

        // Zones AAA (hachurées) lues dans le .miz : le bouton est grisé s'il n'y en a pas
        private void LoadAaa(string file)
        {
            List<AaaZone> zones = Map_Drawings.ReadAaaZones(file);
            _view.SetAaaZones(zones);
            _chkAaa.Enabled = zones.Count > 0;
            _view.SetDrawingsVisible("aaa", zones.Count > 0 && _chkAaa.Checked);
        }

        // Cases des éléments au sol : "tous" coche / décoche bleu + rouge ; bleu et rouge cochés ensemble = "tous" coché
        private bool _syncGround;

        private void GroundBoxChanged(object sender)
        {
            if (_syncGround) return;
            if (_chkGroundBlue != null)
            {
                _syncGround = true;
                if (sender == _chkGround)
                    _chkGroundBlue.Checked = _chkGroundRed.Checked = _chkGround.Checked;
                else if (sender == _chkGroundBlue || sender == _chkGroundRed)
                    _chkGround.Checked = (_chkGroundBlue.Checked || !_chkGroundBlue.Enabled) && (_chkGroundRed.Checked || !_chkGroundRed.Enabled);
                _syncGround = false;
            }
            ApplyGround();
        }

        // Cases debug / traces : on note la valeur voulue ; elle est écrite dans le camp_status.lua du .miz au clic sur "Save modification"
        // (rien n'est changé sur la carte elle-même)
        private void CampBoolChanged(CheckBox box, string key)
        {
            if (_syncOpts || string.IsNullOrEmpty(_missionFile)) return;
            bool orig;
            if (_campOrig.TryGetValue(key, out orig) && orig == box.Checked) _pendingCamp.Remove(key);
            else _pendingCamp[key] = box.Checked;
            UpdateSaveWpt();
        }

        private readonly Dictionary<string, bool> _campOrig = new Dictionary<string, bool>();   // valeurs lues dans le .miz (pour savoir si une case est revenue à l'origine)

        // Au chargement : l'état des 3 cases vient du .miz
        private void LoadMissionDebugBox(string miz)
        {
            if (_chkMissionDebug == null) return;
            bool hasFile;
            bool? v = Map_MissionStats.ReadCampBool(miz, Map_MissionStats.KeyDebug, out hasFile);
            bool? tp = Map_MissionStats.ReadCampBool(miz, Map_MissionStats.KeyTracePlayers, out hasFile);
            bool? ta = Map_MissionStats.ReadCampBool(miz, Map_MissionStats.KeyTraceAI, out hasFile);
            _syncOpts = true;
            bool canEdit = hasFile && ParamConf.UserLevel == UserLevel.CampaignMaker;   // l'écriture passe par "Save modification" (CampaignMaker)
            _chkMissionDebug.Enabled = canEdit;
            _chkTrPlayers.Enabled = canEdit;
            _chkTrAI.Enabled = canEdit;
            _campOrig[Map_MissionStats.KeyDebug] = v == true;
            _campOrig[Map_MissionStats.KeyTracePlayers] = tp != false;
            _campOrig[Map_MissionStats.KeyTraceAI] = ta != false;
            _pendingCamp.Clear();
            _chkMissionDebug.Checked = v == true;
            _chkTrPlayers.Checked = tp != false;   // variable absente = traces actives (seul "false" les coupe)
            _chkTrAI.Checked = ta != false;
            _syncOpts = false;
        }

        // Debug.debug de conf_mod.lua de la campagne (même lecture que la fenêtre ScriptsMod)
        private bool IsDebugOn()
        {
            try
            {
                object value;
                ConfModDynamicData cd = new ConfModLoader().Load(_campaign);
                return cd != null && cd.Values.TryGetValue("Debug.debug", out value) && Convert.ToBoolean(value);
            }
            catch (Exception ex)
            {
                FormUtils.LogRegister("Map_UI_Form | IsDebugOn | " + ex.Message);
                return false;
            }
        }

        // Bouton H : les groupes "hidden" sont ajoutés à la liste du sol (ou retirés), la carte la reprend
        private void ShowHiddenGroundChanged()
        {
            if (_view == null || _missionData == null) return;
            var data = _missionData;
            if (data == null) return;
            bool on = _chkGroundHidden.Checked;
            var units = on ? data.Ground.Concat(data.HiddenGround).ToList() : data.Ground;
            var groups = on ? data.GroundGroups.Concat(data.HiddenGroundGroups).ToList() : data.GroundGroups;
            _view.SetGround(units, groups);
            bool hasBlue = units.Any(u => !u.Red), hasRed = units.Any(u => u.Red);
            _chkGround.Enabled = units.Count > 0;
            _chkGroundNames.Enabled = units.Count > 0;
            if (_chkGroundBlue != null) { _chkGroundBlue.Enabled = hasBlue; _chkGroundRed.Enabled = hasRed; }
            ApplyGround();
        }

        private void ApplyGround()
        {
            if (_view == null || _chkGround == null) return;
            bool blue = _chkGroundBlue != null ? _chkGroundBlue.Checked : _chkGround.Checked;
            bool red = _chkGroundRed != null ? _chkGroundRed.Checked : _chkGround.Checked;
            _view.SetGroundShown(blue, red, _chkGroundNames.Checked);
        }

        // Clic sur un élément au sol : petite fenêtre avec son nom et ses champs (texte copiable)
        private Map_UI_GroundInfo _groundInfo;

        private void OnGroundClicked(Map_GroundUnit u)
        {
            try
            {
                if (_groundInfo == null || _groundInfo.IsDisposed)
                {
                    _groundInfo = new Map_UI_GroundInfo();
                    _groundInfo.Location = PointToScreen(new Point(Width - _groundInfo.Width - 460, 140));
                }
                _groundInfo.ShowUnit(u, _background != null ? _background.Proj : null);
                if (!_groundInfo.Visible) _groundInfo.Show(this);
            }
            catch (Exception ex)
            {
                FormUtils.LogRegister("Map_UI_Form | ground info : " + ex);
            }
        }

        // Recharge seulement les zones wargame (couleur = camp du mode Init ou Active), en gardant l'état du bouton
        private void ReloadZones()
        {
            bool wasOn = _chkZones.Checked;
            List<Map_Drawing> list = Map_Drawings.LoadWargameZones(_campaign, _radioActive.Checked);

            _view.SetDrawings("zones", list);
            _chkZones.Enabled = list.Count > 0;
            _view.SetDrawingsVisible("zones", wasOn && list.Count > 0);
        }

        // Charge un groupe de tracés dans la carte : bouton grisé s'il n'y a rien à montrer
        private void SetGroup(string group, List<Map_Drawing> list, CheckBox button, bool shownByDefault)
        {
            _view.SetDrawings(group, list);
            button.Enabled = list.Count > 0;
            bool on = list.Count > 0 && shownByDefault;
            button.Checked = on;
            _view.SetDrawingsVisible(group, on);
        }
    }
}
