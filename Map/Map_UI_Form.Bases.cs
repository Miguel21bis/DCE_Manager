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
    // Partie de Map_UI_Form (voir Map_UI_Form.cs) : carte des bases : fiche base, chargement des squadrons, édition / déplacement
    internal partial class Map_UI_Form : Form
    {
        // Fiche de la base (carte de mission) : altitude, pistes, fréquences, avions
        private List<Squad> _cardSquads;        // derniers arguments de la fiche base (pour la redessiner quand on change d'unités)
        private int _cardReady, _cardReserve;

        private void UpdateBaseCard(List<Squad> squads, int ready, int reserve)
        {
            if (!_missionMode || _baseCard == null) return;
            _cardSquads = squads; _cardReady = ready; _cardReserve = reserve;

            if (_selectedBase == "")
            {
                _baseCard.SetContent("Bases", "Click a base on the map.", Color.Gray, null);
                ResizeBaseHost();
                return;
            }

            CultureInfo c = CultureInfo.InvariantCulture;
            AirbaseInfo a;
            _airbases.TryGetValue(_selectedBase, out a);

            string alias = AliasOf(_selectedBase);
            string title = alias != "" ? _selectedBase + " (" + alias + ")" : _selectedBase;

            string side = a != null && a.Side != null ? a.Side : "";
            string sub = side.ToUpperInvariant();
            if (a != null)
            {
                string zs = ZoneSideOf(a.X, a.Y, _radioInit.Checked);
                if (zs != "" && !string.Equals(zs, side, StringComparison.OrdinalIgnoreCase)) sub += "  -  held by " + zs + " (zone)";
                if (a.Inactive) sub += "  -  INACTIVE";
            }

            Color accent = side.Equals("blue", StringComparison.OrdinalIgnoreCase) ? Color.FromArgb(70, 140, 255)
                         : side.Equals("red", StringComparison.OrdinalIgnoreCase) ? Color.FromArgb(235, 70, 70) : Color.Gray;

            var rows = new List<KeyValuePair<string, string>>();
            if (a != null)
            {
                rows.Add(new KeyValuePair<string, string>("Altitude", Map_Units.Alt(a.Elevation)));

                string rwSrc;
                List<Map_Runway> eff = Map_RunwayData.Get(_selectedBase, out rwSrc);
                if (eff != null && eff.Count > 0 && rwSrc != "db_airbases")
                    rows.Add(new KeyValuePair<string, string>("Runways", string.Join("\n",
                        eff.Select(r => Map_Units.Alt(r.Length) + "   " + (((r.Hdg % 360) + 360) % 360).ToString("0", c) + "\u00B0")) + "\n(" + rwSrc + ")"));
                else if (a.Runways != null && a.Runways.Count > 0)
                    rows.Add(new KeyValuePair<string, string>("Runways", string.Join("\n",
                        a.Runways.Select(r => (r.Name ?? "?") + "   " + Map_Units.Alt(r.Length) + "   " + r.Hdg.ToString("0", c) + "\u00B0"))));

                if (a.ATCFrequencies != null && a.ATCFrequencies.Count > 0)
                    rows.Add(new KeyValuePair<string, string>("ATC", string.Join("\n",
                        a.ATCFrequencies.Select(f => (f > 100000 ? f / 1000000.0 : f).ToString("0.000", c) + " MHz"))));

                if (a.Code != null && a.Code.Count > 0)
                    rows.Add(new KeyValuePair<string, string>("Codes", string.Join("   ", a.Code.Select(k => k.Key + " " + k.Value))));
            }
            rows.Add(new KeyValuePair<string, string>("Squadrons", squads.Count.ToString(c)));
            rows.Add(new KeyValuePair<string, string>("Aircraft", ready.ToString(c) + " ready   /   " + reserve.ToString(c) + " in reserve"));

            _baseCard.SetContent(title, sub, accent, rows);
            ResizeBaseHost();
        }

        // Le conteneur de l'onglet Bases défile quand fiche + escadrons + bases sans position dépassent la hauteur
        private void ResizeBaseHost()
        {
            if (_baseHost == null) return;
            _baseHost.AutoScrollMinSize = new Size(0, _baseCard.Height + SquadListHeight + 120 + 8 + (_parkBar != null && _parkBar.Visible ? _parkBar.Height : 0));
        }

        private void LoadFromDisk()
        {
            try
            {
                var loaded = new Parser_db_airbases().Load_db_airbases(_campaign);
                _airbases = new Dictionary<string, AirbaseInfo>(loaded, StringComparer.OrdinalIgnoreCase);
            }
            catch (Exception ex)
            {
                FormUtils.LogRegister("Map_UI_Form | db_airbases : " + ex.Message);
                _airbases = new Dictionary<string, AirbaseInfo>(StringComparer.OrdinalIgnoreCase);
                MessageBox.Show("The airbases could not be read: " + ex.Message, "Map");
            }

            if (_ctx != null)
                _ctx.Airbases = _airbases;

            LoadAliases();
            LoadDrawings();
            LoadParks();

            _zonesInit = Map_Drawings.LoadWargameZoneData(_campaign, false);
            _zonesActive = Map_Drawings.LoadWargameZoneData(_campaign, true);

            // Squadrons : on prend ceux de la fenêtre principale s'ils concernent bien cette campagne
            Campaign_Edit_Grid_Right edit = null;
            try
            {
                edit = Main_Form.Instance.CampaignGridLeft.CurrentCampaignEdit;
            }
            catch
            {
                edit = null;
            }

            bool sameCampaign = string.Equals(ParamCampaignSelected.NameCampaign, _campaign, StringComparison.OrdinalIgnoreCase);

            if (edit != null && sameCampaign && edit.CurrentCampaignSquads != null && edit.CurrentCampaignSquads.Count > 0)
            {
                _edit = edit;
                _allSquads = edit.CurrentCampaignSquads;
                _flat = edit.CurrentSquads;
                _readOnly = false;
            }
            else
            {
                _edit = null;
                _flat = null;
                _readOnly = true;
                _allSquads = LoadSquadsWithoutSideEffect();
            }

            if (_missionMode)
                _readOnly = true;       // pour l'instant la carte de mission ne modifie rien
        }

        // Le parseur d'oob_air remplit des listes globales (List_oob_air_Manager) que les onglets OOB utilisent.
        // On les sauvegarde puis on les remet : ouvrir la carte ne touche donc pas aux modifications en cours.
        private List<CampaignSquad> LoadSquadsWithoutSideEffect()
        {
            List<Squad> oldSquads = List_oob_air_Manager.List_oob_air;
            List<CampaignSquad> oldCampaignSquads = List_oob_air_Manager.List_campaignSquads;
            string oldParseName = ParamCampaign.NameFileParse;

            try
            {
                List<CampaignSquad> result = new Parser_OobAir().LoadCampaignSquads(_campaign);
                return result != null ? new List<CampaignSquad>(result) : new List<CampaignSquad>();
            }
            catch (Exception ex)
            {
                FormUtils.LogRegister("Map_UI_Form | oob_air : " + ex.Message);
                MessageBox.Show("The squadrons could not be read: " + ex.Message, "Map");
                return new List<CampaignSquad>();
            }
            finally
            {
                List_oob_air_Manager.List_oob_air = oldSquads;
                List_oob_air_Manager.List_campaignSquads = oldCampaignSquads;
                ParamCampaign.NameFileParse = oldParseName;
            }
        }

        // ------------------------------------------------------------------
        //  Affichage selon le mode (Init / Active)
        // ------------------------------------------------------------------

        // Le squadron à afficher pour ce mode (null = il n'existe pas dans ce mode ou il a été supprimé)
        private Squad SquadOf(CampaignSquad cs, bool init, HashSet<Squad> liveSet)
        {
            Squad s;
            if (_readOnly)
                s = init ? cs.Init : (cs.Active ?? cs.Init);     // lecture seule : on se contente de ce qui existe
            else
                s = init ? cs.Init : cs.Active;

            if (s == null) return null;
            if (liveSet != null && !liveSet.Contains(s)) return null;      // supprimé de la liste
            return s;
        }

        private void ApplyMode()
        {
            bool init = _radioInit.Checked;
            HashSet<Squad> liveSet = _flat != null ? new HashSet<Squad>(_flat) : null;

            // Regroupe les squadrons par base
            _squadsByBase = new Dictionary<string, List<Squad>>(StringComparer.OrdinalIgnoreCase);
            var aircraftByBase = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            foreach (CampaignSquad cs in _allSquads)
            {
                Squad s = SquadOf(cs, init, liveSet);
                if (s == null) continue;

                string baseName = (s.Base ?? "").Trim();
                if (baseName == "") baseName = "(no base)";

                List<Squad> list;
                if (!_squadsByBase.TryGetValue(baseName, out list))
                {
                    list = new List<Squad>();
                    _squadsByBase[baseName] = list;
                }
                list.Add(s);

                // Tous les squadrons comptent (même inactifs, souvent le cas en Init) : la silhouette montre ce qui est basé là
                {
                    int n;
                    aircraftByBase.TryGetValue(baseName, out n);
                    aircraftByBase[baseName] = n + (init ? s.Number : s.DisplayReady);
                }
            }

            // Bases à dessiner / squadrons sans position
            bool hideEmpty = _chkHideEmpty.Checked;
            var markers = new List<Map_Base>();
            var noPos = new List<string>();

            foreach (KeyValuePair<string, AirbaseInfo> kv in _airbases)
            {
                AirbaseInfo a = kv.Value;
                int count = _squadsByBase.ContainsKey(kv.Key) ? _squadsByBase[kv.Key].Count : 0;
                int aircraft;
                aircraftByBase.TryGetValue(kv.Key, out aircraft);

                // Porte-avions, LHA : pas de x/y ; "Reserves" : x = 9999999999 (volontairement hors carte)
                bool placed = !(a.X == 0 && a.Y == 0) && Math.Abs(a.X) < 1e7 && Math.Abs(a.Y) < 1e7;

                if (placed)
                {
                    if (hideEmpty && count == 0) continue;

                    markers.Add(new Map_Base
                    {
                        Name = kv.Key,
                        X = a.X,
                        Y = a.Y,
                        Side = a.Side,
                        ZoneSide = ZoneSideOf(a.X, a.Y, init),
                        Inactive = a.Inactive,
                        IsFarp = kv.Key.IndexOf("FARP", StringComparison.OrdinalIgnoreCase) >= 0,
                        Squads = count,
                        Aircraft = aircraft,
                        Alias = AliasOf(kv.Key),
                        Parks = ParksOf(kv.Key),
                        Runways = a.Runways == null ? new List<Map_Runway>()
                            : a.Runways.Select(rw => new Map_Runway { Hdg = rw.Hdg, Length = rw.Length, X = rw.X, Y = rw.Y }).ToList()
                    });
                }
                else if (count > 0)
                {
                    noPos.Add(kv.Key);
                }
            }

            // Squadrons dont la base n'existe pas du tout dans db_airbases
            foreach (string baseName in _squadsByBase.Keys)
            {
                if (!_airbases.ContainsKey(baseName))
                    noPos.Add(baseName);
            }

            noPos.Sort(StringComparer.OrdinalIgnoreCase);
            _noPosNames.Clear();
            _noPosNames.AddRange(noPos);

            _listNoPos.Items.Clear();
            foreach (string n in _noPosNames)
                _listNoPos.Items.Add(n + "  (" + _squadsByBase[n].Count + ")");

            Map_RunwayData.Fill(_campaign, markers, _background != null ? _background.Proj : null);        // réglage de l'utilisateur > db_airbases > données publiques

            _view.SetBases(markers);

            // On garde la base choisie quand on change de mode ou après une modification
            if (_selectedBase != "")
            {
                bool onMap = markers.Any(m => string.Equals(m.Name, _selectedBase, StringComparison.OrdinalIgnoreCase));
                _view.SelectedName = onMap ? _selectedBase : "";
                ShowBase(_selectedBase);
            }
            else
            {
                UpdateButtons();
            }
        }

        // Camp qui tient la zone wargame contenant la position (x = nord, y = est) : "blue" / "red" / "other", "" si hors zone
        private string ZoneSideOf(double x, double y, bool init)
        {
            List<WargameZoneData> zones = init ? _zonesInit : _zonesActive;
            if (zones == null) return "";

            var p = new PointF((float)x, (float)y);
            foreach (WargameZoneData z in zones)
            {
                if (z.Id != null && z.Id.StartsWith("LANDING", StringComparison.OrdinalIgnoreCase)) continue;   // plans de débarquement : pas des zones tenues

                bool inside;
                if (z.Shape == WargameZoneShape.Circle)
                {
                    double dx = x - z.Center.X, dy = y - z.Center.Y;
                    inside = dx * dx + dy * dy <= (double)z.Radius * z.Radius;
                }
                else
                {
                    inside = WargameEngineCombat.FindZoneContaining(new List<WargameZoneData> { z }, p) != null;
                }

                if (!inside) continue;
                return z.Control == WargameSide.Blue ? "blue" : z.Control == WargameSide.Red ? "red" : "other";
            }
            return "";
        }

        // Remplit le panneau de droite pour la base choisie
        private void ShowBase(string name, Squad reselect = null)
        {
            _selectedBase = name ?? "";
            _listSquads.BeginUpdate();
            _listSquads.Items.Clear();

            List<Squad> squads;
            if (!_squadsByBase.TryGetValue(_selectedBase, out squads))
                squads = new List<Squad>();

            bool init = _radioInit.Checked;
            int ready = 0, reserve = 0;

            foreach (Squad s in squads.OrderBy(x => x.DisplayName ?? x.Name, StringComparer.OrdinalIgnoreCase))
            {
                int r = init ? s.Number : s.DisplayReady;
                int res = ReserveOf(s);
                ready += r;
                reserve += res;

                var item = new ListViewItem(s.DisplayName ?? s.Name ?? "?");
                item.Tag = s;
                item.SubItems.Add(s.Type ?? "");
                item.SubItems.Add(r.ToString());
                item.SubItems.Add(res.ToString());

                string flags = "";
                if (s.Player) flags += "PLAYER ";
                if (s.Squad_Inactive) flags += "inactive";
                item.SubItems.Add(flags.Trim());

                string side = (s.SideString ?? s.Side ?? "").ToLowerInvariant();
                if (side == "blue") item.ForeColor = Color.RoyalBlue;
                else if (side == "red") item.ForeColor = Color.Firebrick;
                if (s.Squad_Inactive) item.ForeColor = Color.Gray;

                if (reselect != null && ReferenceEquals(s, reselect))
                    item.Selected = true;

                _listSquads.Items.Add(item);
            }

            // Ligne vide finale : c'est là que s'affichent "+" et poubelle (jamais sélectionnable)
            if (_selectedBase != "")
                _listSquads.Items.Add(new ListViewItem(""));

            _listSquads.EndUpdate();

            UpdateBaseCard(squads, ready, reserve);

            string alias = _selectedBase == "" ? "" : AliasOf(_selectedBase);
            _lblBase.Text = _selectedBase == "" ? "Click a base on the map" : (alias != "" ? _selectedBase + " (" + alias + ")" : _selectedBase);

            if (_selectedBase == "")
            {
                _lblInfo.Text = "";
                UpdateButtons();
                return;
            }

            AirbaseInfo a;
            string info = "";
            if (_airbases.TryGetValue(_selectedBase, out a))
            {
                string zs = ZoneSideOf(a.X, a.Y, init);
                if (zs != "" && !string.Equals(zs, a.Side, StringComparison.OrdinalIgnoreCase)) info += "held by " + zs + " (zone)  -  ";
                else if (!string.IsNullOrEmpty(a.Side)) info += a.Side + "  -  ";
                if (a.Inactive) info += "INACTIVE  -  ";
            }
            info += squads.Count + " squadron(s), " + ready + " ready, " + reserve + " in reserve";
            _lblInfo.Text = info;
            UpdateButtons();
        }

        // Avions en réserve : roster.reserve en Active, sinon la valeur du squadron
        private static int ReserveOf(Squad s)
        {
            object value;
            int parsed;
            if (s.Roster != null && s.Roster.TryGetValue("reserve", out value)
                && value != null && int.TryParse(value.ToString(), out parsed))
                return parsed;

            return s.Reserve;
        }

        // Clic sur une base de la carte : soit on affiche ses squadrons, soit c'est la destination d'un Move
        private void OnBaseClicked(string name)
        {
            if (_moveSquad != null)
            {
                DoMove(name);
                return;
            }

            _listNoPos.ClearSelected();

            if (_missionMode && _tabs != null)
            {
                _view.SelectedFlight = "";
                OnFlightsClicked(new List<string>());
                _tabs.SelectedTab = _tabBases;
            }
            ShowBase(name);
        }

        // ------------------------------------------------------------------
        //  Édition des squadrons
        // ------------------------------------------------------------------

        private Squad SelectedSquad()
        {
            if (_listSquads.SelectedItems.Count == 0)
                return null;
            return _listSquads.SelectedItems[0].Tag as Squad;
        }

        // Une modification a été faite sur le mode affiché (Init ou Active)
        private void MarkDirty(bool alsoOtherMode)
        {
            if (_radioInit.Checked || alsoOtherMode) _dirtyInit = true;
            if (!_radioInit.Checked || alsoOtherMode) _dirtyActive = true;
        }

        // Après une modification : on remet à jour les onglets OOB de la fenêtre principale puis cette carte
        private void AfterChange(Squad reselect = null)
        {
            try
            {
                Main_Form.Instance.CampaignGridLeft.RefreshGrids();
            }
            catch (Exception ex)
            {
                FormUtils.LogRegister("Map_UI_Form | RefreshGrids : " + ex.Message);
            }

            ApplyMode();
            if (reselect != null && _selectedBase != "")
                ShowBase(_selectedBase, reselect);
            UpdateTitle();
        }

        // Contexte demandé par la fenêtre d'édition d'un squadron (listes de pays, avions, bases...)
        private bool EnsureContext()
        {
            if (_ctx != null)
                return true;

            try
            {
                CampaignLuaData lua = new CampaignLuaLoader().Load(_campaign);
                if (lua == null)
                {
                    MessageBox.Show("The campaign data (aircraft, tasks...) could not be loaded.", "Map");
                    return false;
                }

                _ctx = new CampaignContext
                {
                    CampaignName = _campaign,
                    LuaData = lua,
                    Airbases = _airbases,
                    CampaignEditRef = _edit
                };
                return true;
            }
            catch (Exception ex)
            {
                FormUtils.LogRegister("Map_UI_Form | contexte : " + ex.Message);
                MessageBox.Show("The campaign data needed to edit a squadron could not be loaded: " + ex.Message, "Map");
                return false;
            }
        }

        // Renvoie true si l'utilisateur a validé (Save) dans la fenêtre d'édition
        private bool OpenEditor(Squad squad)
        {
            if (!EnsureContext())
                return false;

            try
            {
                using (var frm = new SquadEdit_Form(squad, _ctx, false, "MapEdit"))
                {
                    return frm.ShowDialog(this) == DialogResult.OK;
                }
            }
            catch (Exception ex)
            {
                FormUtils.LogRegister("Map_UI_Form | SquadEdit_Form : " + ex);
                MessageBox.Show("The squadron could not be edited: " + ex.Message, "Map");
                return false;
            }
        }

        private void EditSelected()
        {
            Squad squad = SelectedSquad();
            if (squad == null || _readOnly)
                return;

            // Cancel : rien n'a changé ; Save : le squadron est à jour (fichier écrit avec le Save de la carte)
            if (!OpenEditor(squad))
                return;

            MarkDirty(false);
            AfterChange(squad);
        }

        // Modèle pour un nouveau squadron : celui qui est choisi, sinon un de la base, sinon un du même camp que la base, sinon n'importe lequel
        private Squad PickTemplate(bool init)
        {
            Squad sel = SelectedSquad();
            if (sel != null) return sel;

            List<Squad> here;
            if (_squadsByBase.TryGetValue(_selectedBase, out here) && here.Count > 0)
                return here[0];

            HashSet<Squad> liveSet = _flat != null ? new HashSet<Squad>(_flat) : null;
            string baseSide = "";
            AirbaseInfo a;
            if (_airbases.TryGetValue(_selectedBase, out a) && a != null)
            {
                string z = ZoneSideOf(a.X, a.Y, init);
                baseSide = ((z == "blue" || z == "red") ? z : (a.Side ?? "")).ToLowerInvariant();
            }

            Squad any = null;
            foreach (CampaignSquad cs in _allSquads)
            {
                Squad s = SquadOf(cs, init, liveSet);
                if (s == null) continue;
                if (any == null) any = s;
                if (baseSide != "" && string.Equals((cs.SideString ?? s.SideString ?? "").Trim(), baseSide, StringComparison.OrdinalIgnoreCase))
                    return s;
            }
            return any;
        }

        // Bouton "+" : nouveau squadron sur la base affichée (marche aussi sur une base vide)
        private void AddSquad()
        {
            if (_readOnly || _flat == null || _selectedBase == "" || _selectedBase == "(no base)")
                return;

            bool init = _radioInit.Checked;
            Squad src = PickTemplate(init);
            if (src == null)
            {
                MessageBox.Show("There is no squadron at all in this campaign to use as a model.", "Map");
                return;
            }
            CampaignSquad owner = _allSquads.FirstOrDefault(c => ReferenceEquals(c.Init, src) || ReferenceEquals(c.Active, src));

            // Copie de la version affichée, puis de l'autre version (même nom, même identifiant)
            Squad main = CopySquad(src);
            main.GenerateCloneIdentity(_flat);
            main.Player = false;
            main.Base = _selectedBase;
            main.FolderFile = init ? "Init" : "Active";

            Squad twin = CopySquad(main);
            twin.Base = _selectedBase;
            twin.FolderFile = init ? "Active" : "Init";

            string side = owner != null && owner.SideString != null ? owner.SideString : src.SideString;
            var created = new CampaignSquad
            {
                SideString = side,
                Key = (side ?? "").Trim().ToLowerInvariant() + "|" + (main.Name ?? "").Trim().ToLowerInvariant()
            };
            if (init) { created.Init = main; created.Active = twin; }
            else { created.Active = main; created.Init = twin; }

            _allSquads.Add(created);
            if (created.Init != null) _flat.Add(created.Init);
            if (created.Active != null) _flat.Add(created.Active);

            MarkDirty(true);
            AfterChange(main);

            // On ouvre directement l'édition de la copie (nom, avion, nombre...)
            if (!OpenEditor(main))
            {
                // Cancel : la copie n'est pas gardée
                _flat.Remove(created.Init);
                _flat.Remove(created.Active);
                _allSquads.Remove(created);
                AfterChange();
                return;
            }

            AfterChange(main);
        }

        // Copie d'un squadron avec ses tables (sinon l'original et la copie partageraient les mêmes listes)
        private static Squad CopySquad(Squad s)
        {
            Squad c = s.Clone();

            c.BaseAlternative = s.BaseAlternative != null ? new List<string>(s.BaseAlternative) : new List<string>();
            c.Tasks = s.Tasks != null ? new Dictionary<string, bool>(s.Tasks) : new Dictionary<string, bool>();
            c.TasksCoef = s.TasksCoef != null ? new Dictionary<string, double>(s.TasksCoef) : new Dictionary<string, double>();
            c.TasksCoefPourcent = s.TasksCoefPourcent != null ? new Dictionary<string, int>(s.TasksCoefPourcent) : null;
            c.Roster = s.Roster != null ? new Dictionary<string, object>(s.Roster) : null;
            c.Score = s.Score != null ? new Dictionary<string, object>(s.Score) : null;
            c.ScoreLast = s.ScoreLast != null ? new Dictionary<string, object>(s.ScoreLast) : null;
            c.AdditionalProperties = s.AdditionalProperties != null ? new Dictionary<string, object>(s.AdditionalProperties) : new Dictionary<string, object>();
            c.parking_id = s.parking_id != null ? new Dictionary<string, object>(s.parking_id) : new Dictionary<string, object>();
            c.Livery = s.Livery != null ? new Dictionary<int, string>(s.Livery) : new Dictionary<int, string>();
            c.LiveryModex = s.LiveryModex != null ? new Dictionary<int, string>(s.LiveryModex) : new Dictionary<int, string>();

            return c;
        }

        private void DeleteSelected()
        {
            Squad squad = SelectedSquad();
            if (squad == null || _readOnly || _flat == null)
                return;

            string mode = _radioInit.Checked ? "Init" : "Active";
            if (MessageBox.Show("Delete squadron \"" + (squad.DisplayName ?? squad.Name) + "\" from the " + mode + " list?",
                    "Confirm delete", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return;

            _flat.Remove(squad);
            MarkDirty(false);
            AfterChange();
        }

        // ------------------------------------------------------------------
        //  Déplacement : on choisit le squadron, puis on clique sa nouvelle base sur la carte
        // ------------------------------------------------------------------

        private void StartMove()
        {
            Squad squad = SelectedSquad();
            if (squad == null || _readOnly)
                return;

            _moveSquad = squad;
            _chkRuler.Checked = false;
            _lblStatus.Text = "Move \"" + (squad.DisplayName ?? squad.Name) + "\": click the destination base on the map (Esc = cancel).";
            _lblStatus.ForeColor = Color.DarkOrange;
            UpdateButtons();
        }

        private void CancelMove()
        {
            if (_moveSquad == null)
                return;

            _moveSquad = null;
            _lblStatus.ForeColor = SystemColors.ControlText;
            ShowDefaultStatus();
            UpdateButtons();
        }

        private void DoMove(string destination)
        {
            Squad squad = _moveSquad;
            if (squad == null)
                return;

            AirbaseInfo a;
            _airbases.TryGetValue(destination, out a);

            // Camp différent ou base inactive : on prévient avant de faire la modification
            string warning = "";
            string squadSide = (squad.SideString ?? squad.Side ?? "").ToLowerInvariant();
            if (a != null && !string.IsNullOrEmpty(a.Side) && squadSide != "" && !string.Equals(a.Side, squadSide, StringComparison.OrdinalIgnoreCase))
                warning += "This base belongs to the " + a.Side + " side, the squadron is " + squadSide + ".\n";
            if (a != null && a.Inactive)
                warning += "This base is inactive.\n";

            if (warning != "" && MessageBox.Show(warning + "\nMove it anyway?", "Move squadron",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
            {
                CancelMove();
                return;
            }

            squad.Base = destination;

            // Squadron avec plusieurs bases : la nouvelle base passe en tête de la liste (comme dans l'édition)
            if (squad.BaseAlternative != null && squad.BaseAlternative.Count > 0)
            {
                var list = squad.BaseAlternative.Where(b => !string.Equals(b, destination, StringComparison.OrdinalIgnoreCase)).ToList();
                list.Insert(0, destination);
                squad.BaseAlternative = list;
            }

            _moveSquad = null;
            _lblStatus.ForeColor = SystemColors.ControlText;
            ShowDefaultStatus();

            MarkDirty(false);
            _selectedBase = destination;
            AfterChange(squad);
            _view.SelectedName = destination;
        }

        // ------------------------------------------------------------------
        //  Enregistrement
        // ------------------------------------------------------------------

        private void SaveCurrentMode()
        {
            SaveFolder(_radioInit.Checked);
            CommitRunways();
            CommitParks();
            UpdateTitle();
        }
    }
}
