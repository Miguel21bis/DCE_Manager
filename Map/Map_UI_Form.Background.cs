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
    // Partie de Map_UI_Form (voir Map_UI_Form.cs) : fond de carte web, alias des bases
    internal partial class Map_UI_Form : Form
    {
        private void UseBackground(Map_Background bg)
        {
            if (_background != null)
                _background.Dispose();

            _background = bg;
            _view.SetBackground(bg);
            Map_RunwayData.SetMeasured(Map_RunwayData.TheatreOf(_campaign), bg != null ? bg.AnchorRule : "");        // règle de position mesurée sur cette carte
            if (_airbases != null && _airbases.Count > 0) ApplyMode();      // pistes replacées (règle mesurée, projection de la carte)
            _view.ShowBackground = true;
            ShowDefaultStatus();
            SetBackgroundChecked(true);
        }

        // Toutes les bases posées sur la carte servent à caler la carte : celles qui ont un code ICAO (cherchées dans la liste des aéroports)
        // et les autres (rattachées à la piste réelle la plus proche, si elle est à moins de 1 km)
        private List<Map_Match> BuildMatchInputs()
        {
            var list = new List<Map_Match>();
            foreach (KeyValuePair<string, AirbaseInfo> kv in _airbases)
            {
                AirbaseInfo a = kv.Value;
                if (a.BaseAirStart) continue;       // base "airstart" loin du théâtre : ne sert pas au calage
                if (kv.Key.IndexOf("FARP", StringComparison.OrdinalIgnoreCase) >= 0) continue;     // un FARP n'existe pas dans la réalité

                bool placed = !(a.X == 0 && a.Y == 0) && Math.Abs(a.X) < 1e7 && Math.Abs(a.Y) < 1e7;
                if (!placed) continue;

                string icao = null;
                if (a.Code == null || !a.Code.TryGetValue("ICAO", out icao) || icao == null) icao = "";

                list.Add(new Map_Match
                {
                    Name = kv.Key, Icao = icao.Trim(), X = a.X, Y = a.Y,
                    DbRunways = a.Runways == null ? null : a.Runways.Select(rw => new Map_Runway { Hdg = rw.Hdg, Length = rw.Length, X = rw.X, Y = rw.Y }).ToList()
                });
            }
            return list;
        }

        // Téléchargement + calage, sans rien demander : on le fait une seule fois, tout reste en cache ensuite
        private async Task BuildBackgroundAsync()
        {
            if (_busy || IsDisposed)
                return;

            // Interrupteur "Web data" de la fenêtre principale : OFF = aucun téléchargement de tuiles
            if (!Main_WebAccess.Enabled)
            {
                _lblStatus.Text = "Web map: web access is OFF (switch \"Web data\" in the main window), nothing was downloaded.";
                SetBackgroundChecked(false);
                return;
            }

            List<Map_Match> inputs = BuildMatchInputs();
            // Toutes les bases posées sur la carte : elles servent à choisir la zone à télécharger
            var allPoints = new List<double[]>();
            foreach (AirbaseInfo a in _airbases.Values)
            {
                if (a.BaseAirStart) continue;       // base "airstart" loin de la zone de conflit : pas de tuiles pour elle
                bool placed = !(a.X == 0 && a.Y == 0) && Math.Abs(a.X) < 1e7 && Math.Abs(a.Y) < 1e7;
                if (placed) allPoints.Add(new double[] { a.X, a.Y });
            }
            if (allPoints.Count == 0)
            {
                _lblStatus.Text = "Web map: no airbase with a position on the map.";
                SetBackgroundChecked(false);
                return;
            }

            _busy = true;
            _chkBackground.Enabled = false;
            _progressTiles.Style = ProgressBarStyle.Marquee;      // on ne sait pas encore combien de tuiles : barre qui défile
            _progressTiles.Visible = true;
            try
            {
                var progress = new Progress<string>(msg => { if (!IsDisposed) _lblStatus.Text = msg; });
                var tilesProgress = new Progress<int[]>(p =>
                {
                    if (IsDisposed) return;
                    if (_progressTiles.Style != ProgressBarStyle.Continuous) _progressTiles.Style = ProgressBarStyle.Continuous;
                    _progressTiles.Maximum = Math.Max(1, p[1]);
                    _progressTiles.Value = Math.Max(0, Math.Min(p[0], _progressTiles.Maximum));
                });
                Map_Background bg = await Task.Run(() => Map_Web.BuildBackground(_campaign, inputs, allPoints, progress, tilesProgress));

                if (IsDisposed)
                {
                    bg.Dispose();
                    return;
                }
                UseBackground(bg);
            }
            catch (Exception ex)
            {
                FormUtils.LogRegister("Map_UI_Form | web map : " + ex.Message);
                if (!IsDisposed)
                {
                    _lblStatus.Text = "Web map not available: " + ex.Message;
                    SetBackgroundChecked(false);
                }
            }
            finally
            {
                _busy = false;
                if (!IsDisposed)
                {
                    _chkBackground.Enabled = true;
                    _progressTiles.Visible = false;
                }
            }
        }

        // Nouveau style choisi : même calage, on charge (ou télécharge) les tuiles de ce style
        private void StyleChanged()
        {
            if (_busy)
            {
                _cmbStyle.SelectedIndex = Math.Max(0, Map_Web.Styles.FindIndex(x => x.Id == Map_Web.CurrentStyle.Id));
                return;
            }

            Map_Web.UseCampaign(_campaign);
            Map_Web.CurrentStyle = Map_Web.Styles[_cmbStyle.SelectedIndex];

            if (_background != null)
            {
                _view.SetBackground(null);
                _background.Dispose();
                _background = null;
            }

            Map_Background cached = Map_Web.TryLoadCached(_campaign);
            if (cached != null)
                UseBackground(cached);
            else
                BuildBackgroundAsync();         // télécharge les tuiles de ce style (calage refait, rapide)
        }

        private async void ChkBackground_CheckedChanged(object sender, EventArgs e)
        {
            if (_loading || _busy)
                return;

            if (!_chkBackground.Checked)
            {
                _view.ShowBackground = false;
                return;
            }

            if (_background != null)
            {
                _view.ShowBackground = true;
                return;
            }

            Map_Background cached = Map_Web.TryLoadCached(_campaign);
            if (cached != null)
            {
                UseBackground(cached);
                return;
            }

            await BuildBackgroundAsync();
        }

        // Efface le calage enregistré puis relance la construction (réseau si besoin)
        private void RebuildBackground()
        {
            if (_busy)
                return;

            Map_Web.ForgetCalibration(_campaign);

            if (_background != null)
            {
                _view.SetBackground(null);
                _background.Dispose();
                _background = null;
            }
            _view.ShowBackground = false;
            _lblStatus.Text = "";

            SetBackgroundChecked(false);
            _chkBackground.Checked = true;      // déclenche ChkBackground_CheckedChanged
        }

        // ------------------------------------------------------------------
        //  Lecture des données
        // ------------------------------------------------------------------

        // Noms de remplacement des bases : BaseNameAlias dans Init\various_table.lua  ["Bodo"] = "Takhli",
        private void LoadAliases()
        {
            _aliases.Clear();
            Map_Flights.TypeAlias.Clear();
            try
            {
                string file = Path.Combine(ParamConf.PATH_SavedGames_DCS, @"Mods\tech\DCE\Missions\Campaigns", _campaign, "Init", "various_table.lua");
                if (!File.Exists(file)) return;

                // Deux blocs dans le fichier : BaseNameAlias (noms de bases) et TypeAlias (noms de types d'avion)
                string block = "";
                foreach (string raw in File.ReadAllLines(file))
                {
                    string line = raw.Trim();
                    if (line.StartsWith("--")) continue;
                    if (line.StartsWith("BaseNameAlias")) { block = "base"; continue; }
                    if (line.StartsWith("TypeAlias")) { block = "type"; continue; }
                    if (block == "") continue;
                    if (line.StartsWith("}")) { block = ""; continue; }

                    var m = System.Text.RegularExpressions.Regex.Match(line, "^\\[\"(.+?)\"\\]\\s*=\\s*\"(.*?)\"");
                    if (!m.Success) continue;
                    if (block == "base") _aliases[m.Groups[1].Value] = m.Groups[2].Value;
                    else Map_Flights.TypeAlias[m.Groups[1].Value] = m.Groups[2].Value;
                }
            }
            catch (Exception ex)
            {
                FormUtils.LogRegister("Map_UI_Form | various_table : " + ex.Message);
            }
        }

        private string AliasOf(string baseName)
        {
            string a;
            if (_aliases.TryGetValue(baseName, out a)) return a;
            AirbaseInfo info;
            if (_airbases.TryGetValue(baseName, out info) && !string.IsNullOrWhiteSpace(info.AliasName)) return info.AliasName;
            return "";
        }
    }
}
