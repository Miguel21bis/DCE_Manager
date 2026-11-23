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
    // Partie de Map_UI_Form (voir Map_UI_Form.cs) : sauvegarde, fermeture, retour en arrière
    internal partial class Map_UI_Form : Form
    {
        // Écrit les changements de pistes (réglages dans dce_data_user.ini). false = échec (message déjà affiché)
        private bool CommitRunways()
        {
            if (!Map_RunwayData.HasPending) return true;
            try
            {
                Map_RunwayData.CommitPending();
                _lblStatus.Text = "Runway changes saved.";
                return true;
            }
            catch (Exception ex)
            {
                FormUtils.LogRegister("Map_UI_Form | runways : " + ex);
                MessageBox.Show("The runway changes could not be saved: " + ex.Message, "Save");
                return false;
            }
        }

        // Écrit oob_air_init.lua (Init) ou oob_air.lua (Active), avec la même fonction que le bouton Save de la fenêtre principale
        private bool SaveFolder(bool init)
        {
            if (_readOnly || _edit == null)
                return false;

            string root = Path.Combine(ParamConf.PATH_SavedGames_DCS, @"Mods\tech\DCE\Missions\Campaigns", _campaign);
            string file = init ? Path.Combine(root, "Init", "oob_air_init.lua") : Path.Combine(root, "Active", "oob_air.lua");
            string backup = init ? Path.Combine(root, "Init", "oob_air_init_backup_DTT.lua") : null;

            if (!File.Exists(file))
            {
                MessageBox.Show("The file does not exist yet:\n" + file, "Save");
                return false;
            }

            try
            {
                _edit.modifiedCampaign(file, backup, init ? "Init" : "Active");

                if (init) _dirtyInit = false;
                else _dirtyActive = false;
                TakeSnapshot(init);       // c'est maintenant l'état "sauvé" auquel Quit sans sauver ne revient plus
                return true;
            }
            catch (Exception ex)
            {
                FormUtils.LogRegister("Map_UI_Form | save : " + ex);
                MessageBox.Show("The squadrons could not be saved: " + ex.Message, "Save");
                return false;
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            bool squadsDirty = !_readOnly && (_dirtyInit || _dirtyActive);
            bool runwaysDirty = Map_RunwayData.HasPending;
            bool parksDirty = ParkAnyDirty;
            if ((squadsDirty || runwaysDirty || parksDirty) && (e.CloseReason == CloseReason.UserClosing || e.CloseReason == CloseReason.None))
            {
                var what = new List<string>();
                if (squadsDirty && _dirtyInit) what.Add("Init squadrons");
                if (squadsDirty && _dirtyActive) what.Add("Active squadrons");
                if (runwaysDirty) what.Add("runways");
                if (parksDirty) what.Add("helicopter spots");
                DialogResult answer = MessageBox.Show(
                    "The changes (" + string.Join(" + ", what.ToArray()) + ") have not been saved.\n\n"
                    + "Yes = Save, then quit\nNo = Quit WITHOUT saving (the changes are lost)\nCancel = stay on the map",
                    "Unsaved changes", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Warning);

                if (answer == DialogResult.Cancel)
                {
                    e.Cancel = true;
                    return;
                }
                if (answer == DialogResult.Yes)
                {
                    if (squadsDirty && _dirtyInit && !SaveFolder(true)) { e.Cancel = true; return; }
                    if (squadsDirty && _dirtyActive && !SaveFolder(false)) { e.Cancel = true; return; }
                    if (runwaysDirty && !CommitRunways()) { e.Cancel = true; return; }
                    if (parksDirty && !CommitParks()) { e.Cancel = true; return; }
                }
                else
                {
                    if (squadsDirty) RestoreSnapshot(_dirtyInit, _dirtyActive);
                    _dirtyInit = false;
                    _dirtyActive = false;
                    Map_RunwayData.DiscardPending();
                }
            }
            base.OnFormClosing(e);
        }

        // ------------------------------------------------------------------
        //  Photo des squadrons (pour Quit sans Save)
        // ------------------------------------------------------------------

        // Photo de la version Init ou Active de tous les squadrons (liste de la fenêtre principale)
        private void TakeSnapshot(bool init)
        {
            if (_readOnly || _flat == null) return;

            _snapshot.RemoveAll(x => x.IsInit == init);

            foreach (CampaignSquad cs in _allSquads)
            {
                Squad s = init ? cs.Init : cs.Active;
                if (s == null) continue;
                _snapshot.Add(new SnapEntry { Original = s, Copy = CopySquad(s), IsInit = init, Owner = cs });
            }
        }

        // Remet les squadrons des versions demandées dans l'état de la photo : champs recopiés, ajouts annulés, suppressions annulées
        private void RestoreSnapshot(bool init, bool active)
        {
            if (_readOnly || _flat == null) return;

            try
            {
                var keep = new HashSet<Squad>(_snapshot.Where(x => (x.IsInit && init) || (!x.IsInit && active)).Select(x => x.Original));

                // Squadrons ajoutés depuis la photo (+ Add) : retirés
                foreach (CampaignSquad cs in _allSquads.ToList())
                {
                    bool known = _snapshot.Any(x => ReferenceEquals(x.Owner, cs));
                    if (known) continue;

                    if (init && cs.Init != null) _flat.Remove(cs.Init);
                    if (active && cs.Active != null) _flat.Remove(cs.Active);
                    if (init && active) _allSquads.Remove(cs);
                }

                // Squadrons de la photo : champs remis, et remis dans la liste s'ils avaient été retirés (- Remove)
                foreach (SnapEntry entry in _snapshot)
                {
                    if (entry.IsInit ? !init : !active) continue;

                    RestoreFields(entry.Original, entry.Copy);
                    if (!_flat.Contains(entry.Original)) _flat.Add(entry.Original);
                }
            }
            catch (Exception ex)
            {
                FormUtils.LogRegister("Map_UI_Form | restore : " + ex);
            }
        }

        // Recopie tous les champs (y compris privés, y compris ceux des classes parentes) de la photo vers le squadron
        private static void RestoreFields(Squad target, Squad source)
        {
            for (Type t = typeof(Squad); t != null && t != typeof(object); t = t.BaseType)
            {
                foreach (FieldInfo f in t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                {
                    if (f.IsInitOnly || f.IsLiteral) continue;
                    f.SetValue(target, f.GetValue(source));
                }
            }
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            Map_RunwayData.Ready -= _runwaysReady;
            Map_Units.Changed -= UnitsChanged;
            if (_background != null)
                _background.Dispose();

            // Les onglets OOB de la fenêtre principale reflètent les changements faits ici
            try
            {
                if (!_readOnly)
                    Main_Form.Instance.CampaignGridLeft.RefreshGrids();
            }
            catch { }

            base.OnFormClosed(e);
        }
    }
}
