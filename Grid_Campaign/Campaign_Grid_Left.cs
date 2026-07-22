using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;
using DCE_Manager.Parameters;
using DCE_Manager.Update;
using DCE_Manager.Utils;

namespace DCE_Manager
{
    public class CampaignGridLeft
    {

        // Cache des images de campagnes (évite rechargement disque)
        private Dictionary<string, Image> campaignImageCache = new Dictionary<string, Image>();
        //private CampaignEdit _currentCampaignEdit;
        public CampaignEdit CurrentCampaignEdit { get; private set; }

        // Référence vers la Form principale
        private readonly Main_Form _mainForm;

        // Constructeur qui reçoit la référence de Main_Form
        public CampaignGridLeft(Main_Form mainForm)
        {
            _mainForm = mainForm;
        }

        private void GridCampaigns_Init()
        {
            _mainForm.dataGridViewCampaigns.Columns.Clear();

            // ===== CONFIG GLOBALE =====
            _mainForm.dataGridViewCampaigns.AllowUserToResizeColumns = true;
            _mainForm.dataGridViewCampaigns.AllowUserToResizeRows = true;
            _mainForm.dataGridViewCampaigns.RowHeadersVisible = false;
            _mainForm.dataGridViewCampaigns.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            _mainForm.dataGridViewCampaigns.MultiSelect = false;

            // ===== COLONNE CLONE =====
            GridCampaigns_AddButtonColumn("Clone", "＋", 40);

            // ===== IMAGE =====
            var colImg = new DataGridViewImageColumn()
            {
                Name = "Image",
                HeaderText = "",
                Width = 90,
                ImageLayout = DataGridViewImageCellLayout.Zoom
            };
            _mainForm.dataGridViewCampaigns.Columns.Add(colImg);

            // ===== TEXTE =====
            _mainForm.dataGridViewCampaigns.Columns.Add(new DataGridViewTextBoxColumn()
            {
                Name = "Name",
                HeaderText = "Campaign",
                AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill
            });

            // Colonne pour ouvrir le dossier
            GridCampaigns_AddButtonColumn("Folder", "📂", 55);


            _mainForm.dataGridViewCampaigns.Columns.Add(new DataGridViewTextBoxColumn()
            {
                Name = "Version",
                Width = 60,
                DefaultCellStyle = { Alignment = DataGridViewContentAlignment.MiddleCenter }

            });

            _mainForm.dataGridViewCampaigns.Columns.Add(new DataGridViewTextBoxColumn()
            {
                Name = "Missions",
                Width = 50,
                DefaultCellStyle = { Alignment = DataGridViewContentAlignment.MiddleCenter }

            });

            _mainForm.dataGridViewCampaigns.Columns.Add(new DataGridViewTextBoxColumn()
            {
                Name = "Aircraft",
                Width = 90
            });

            // ===== BOUTONS =====

            GridCampaigns_AddButtonColumn("First", "▶", 55);
            GridCampaigns_AddButtonColumn("Skip", "⏭", 55, useColumnTextForButtonValue: false);
            GridCampaigns_AddButtonColumn("Parameters", "⚙", 55);
            GridCampaigns_AddButtonColumn("Delete", "🗑", 55);


            // ===== STYLE BOUTONS =====
            foreach (DataGridViewColumn col in _mainForm.dataGridViewCampaigns.Columns)
            {
                //bloquer le redimensionnement
                //col.Resizable = DataGridViewTriState.False;

                if (col is DataGridViewButtonColumn)
                {
                    col.DefaultCellStyle.BackColor = Color.FromArgb(230, 230, 230);
                    col.DefaultCellStyle.ForeColor = Color.Black;
                    col.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
                    col.DefaultCellStyle.Font = new Font("Segoe UI", 14, FontStyle.Bold);
                }
            }
        }
        private void GridCampaigns_InitStyle()
        {
            //######## STYLE ##########
            _mainForm.dataGridViewCampaigns.BackgroundColor = Color.FromArgb(240, 240, 240);

            _mainForm.dataGridViewCampaigns.DefaultCellStyle.BackColor = Color.White;
            _mainForm.dataGridViewCampaigns.DefaultCellStyle.ForeColor = Color.Black;

            _mainForm.dataGridViewCampaigns.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(245, 245, 245);

            _mainForm.dataGridViewCampaigns.DefaultCellStyle.SelectionBackColor = Color.FromArgb(0, 120, 215);
            _mainForm.dataGridViewCampaigns.DefaultCellStyle.SelectionForeColor = Color.White;

            _mainForm.dataGridViewCampaigns.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(230, 230, 230);
            _mainForm.dataGridViewCampaigns.ColumnHeadersDefaultCellStyle.ForeColor = Color.Black;

            _mainForm.dataGridViewCampaigns.CellBorderStyle = DataGridViewCellBorderStyle.None;
            _mainForm.dataGridViewCampaigns.RowTemplate.Height = 60;

            _mainForm.dataGridViewCampaigns.DefaultCellStyle.Padding = new Padding(0);
            //_mainForm.dataGridViewCampaigns.DefaultCellStyle.Padding = new Padding(10);

            _mainForm.dataGridViewCampaigns.ColumnHeadersHeight = 35;

            // SCROLL
            _mainForm.dataGridViewCampaigns.ScrollBars = ScrollBars.Both;

            // SCROLL//empêche le mode “compression”
            _mainForm.dataGridViewCampaigns.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;

            _mainForm.dataGridViewCampaigns.CellMouseEnter += (s, e) =>
            {
                if (e.RowIndex >= 0)
                    _mainForm.dataGridViewCampaigns.Rows[e.RowIndex].DefaultCellStyle.BackColor = Color.FromArgb(220, 235, 252);
            };

            _mainForm.dataGridViewCampaigns.CellMouseLeave += (s, e) =>
            {
                if (e.RowIndex >= 0)
                    _mainForm.dataGridViewCampaigns.Rows[e.RowIndex].DefaultCellStyle.BackColor =
                        (e.RowIndex % 2 == 0) ? Color.White : Color.FromArgb(245, 245, 245);
            };

            //############# GridCampaigns_Init END
        }

        // Initialise entièrement le DataGridView des campagnes.
        // Pourquoi : allège le constructeur et regroupe toute la configuration du grid au même endroit.
        public void GridCampaigns_Init_DataGridView()
        {
            GridCampaigns_Init();
            GridCampaigns_InitStyle();

            _mainForm.dataGridViewCampaigns.CellClick += GridCampaigns_CellClick;

            _mainForm.dataGridViewCampaigns.RowTemplate.Height = 70;

            _mainForm.dataGridViewCampaigns.DefaultCellStyle.Font = new Font("Segoe UI", 9);
            _mainForm.dataGridViewCampaigns.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI", 9, FontStyle.Bold);

            _mainForm.dataGridViewCampaigns.EnableHeadersVisualStyles = false;
            _mainForm.dataGridViewCampaigns.ColumnHeadersDefaultCellStyle.BackColor = SystemColors.Control;
            _mainForm.dataGridViewCampaigns.ColumnHeadersDefaultCellStyle.ForeColor = Color.Black;

            _mainForm.dataGridViewCampaigns.GridColor = Color.LightGray;
            _mainForm.dataGridViewCampaigns.BorderStyle = BorderStyle.None;
        }


        private void GridCampaigns_AddButtonColumn(string name, string text, int width, bool useColumnTextForButtonValue = true)
        {
            _mainForm.dataGridViewCampaigns.Columns.Add(new DataGridViewButtonColumn()
            {
                Name = name,
                Text = text,
                UseColumnTextForButtonValue = useColumnTextForButtonValue,
                Width = width,
                FlatStyle = FlatStyle.Flat
            });
        }

        private void GridCampaigns_CellClick(object sender, DataGridViewCellEventArgs e)
        {
            // Ignore header
            if (e.RowIndex < 0 || e.ColumnIndex < 0)
                return;

            if (e.ColumnIndex >= _mainForm.dataGridViewCampaigns.Columns.Count)
                return;

            string columnName = _mainForm.dataGridViewCampaigns.Columns[e.ColumnIndex].Name;

            // Récupérer les infos de la ligne
            string name = _mainForm.dataGridViewCampaigns.Rows[e.RowIndex].Cells["Name"].Value?.ToString();

            if (string.IsNullOrEmpty(name))
                return;

            string basePath = ParamConf.PATH_SavedGames_DCS + @"\Mods\tech\DCE\Missions\Campaigns\";
            string folderPath = Path.Combine(basePath, name);

            Utils.FormUtils.LogRegister($"Clicked on column '{columnName}' for campaign '{name}' folderPath '{folderPath}'");

            if (columnName == "First")
            {
                string batPath = Path.Combine(folderPath, "FirstMission.bat");

                if (File.Exists(batPath))
                {
                    System.Diagnostics.Process.Start(new ProcessStartInfo()
                    {
                        FileName = batPath,
                        WorkingDirectory = folderPath,
                        UseShellExecute = true
                    });
                }

                return;
            }
            else if (columnName == "Skip")
            {
                string nbMissionTextSkip = _mainForm.dataGridViewCampaigns.Rows[e.RowIndex].Cells["Missions"].Value?.ToString();

                int nbMissionSkip;
                int.TryParse(nbMissionTextSkip, out nbMissionSkip);

                if (nbMissionSkip <= 0)
                    return; // bouton grisé : aucune mission jouée, on ignore le clic

                string batPath = Path.Combine(folderPath, "SkipMission.bat");

                if (File.Exists(batPath))
                {
                    System.Diagnostics.Process.Start(new ProcessStartInfo()
                    {
                        FileName = batPath,
                        WorkingDirectory = folderPath,
                        UseShellExecute = true
                    });
                }
            }
            else if (columnName == "Parameters")
            {
                //string filePath = Path.Combine(folderPath, @"Init\conf_mod.lua");

                //if (File.Exists(filePath))
                //{
                //    System.Diagnostics.Process.Start(new ProcessStartInfo()
                //    {
                //        FileName = filePath,
                //        UseShellExecute = true
                //    });
                //}

                Utils.FormUtils.LogRegister( Utils.FormUtils.ToTitleCase("Open Parameters for campaign '" + name + "'"));

                using (var form = new ConfModForm(name))
                {
                    form.ShowDialog(_mainForm);
                }
            }
            else if (columnName == "Delete")
            {
                var confirm = MessageBox.Show(
                    "Delete campaign " + name + " ?",
                    "Confirm",
                    MessageBoxButtons.YesNo
                );

                if (confirm == DialogResult.Yes)
                {
                    try
                    {
                        List<string> filesToDelete = Directory.Exists(folderPath)
                            ? Directory.GetFiles(folderPath, "*", SearchOption.AllDirectories).ToList()
                            : new List<string>();

                        string parentFolder = Path.GetDirectoryName(folderPath) ?? "";
                        string prefix = name + "-";

                        string baseName = name;

                        HashSet<string> expected = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                        {
                            baseName,
                            baseName + "_first",
                            baseName + "_ongoing"
                        };

                        var extraFiles = Directory.GetFiles(parentFolder)
                            .Where(f => expected.Contains(Path.GetFileNameWithoutExtension(f)))
                            .ToList();

                        filesToDelete.AddRange(extraFiles);

                        //foreach (string file in filesToDelete)
                        //    FormUtils.LogRegister($"À supprimer : {file}");

                        foreach (string file in filesToDelete)
                        {
                            if (!Main_Form.CanDeleteFile(file))
                            {
                                MessageBox.Show("Impossible de supprimer car le fichier est utilisé :\r\n" + file);
                                return;
                            }
                        }

                        Directory.Delete(folderPath, true);

                        foreach (string file in filesToDelete.Where(File.Exists))
                        {
                            File.Delete(file);
                            //FormUtils.LogRegister($"À supprimer : {file}");
                        }


                        // Recharge la liste
                        LoadCampaignsAsync();
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show("Error: " + ex.Message);
                    }
                }
                return; //  IMPORTANT : stoppe ici
            }
            else if (columnName == "Clone")
            {
                Campaign_CLONE_ClickOneEvent(null, null, basePath, name);
                return;
            }
            // Si on clique sur la colonne "Folder"
            // Ouvre le dossier de la campagne dans l'explorateur Windows
            else if (columnName == "Folder")
            {
                if (Directory.Exists(folderPath))
                {
                    System.Diagnostics.Process.Start(new ProcessStartInfo()
                    {
                        FileName = "explorer.exe",
                        Arguments = "\"" + folderPath + "\"",
                        UseShellExecute = true
                    });
                }
                else
                {
                    MessageBox.Show(
                        "Campaign folder not found:\r\n" + folderPath,
                        "Folder",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning
                    );
                }
            }

            _mainForm.dataGridViewCampaigns.ClearSelection();
            _mainForm.dataGridViewCampaigns.Rows[e.RowIndex].Selected = true;

            // Coche automatiquement Init ou Active selon le nombre de missions jouées
            string nbMissionText = _mainForm.dataGridViewCampaigns.Rows[e.RowIndex].Cells["Missions"].Value?.ToString();

            int nbMission = 0;
            int.TryParse(nbMissionText, out nbMission);


            // 1. Charger la campagne AVANT
            CampaignEdit1(null, null, folderPath + "\\" + name, name);

            // 2. Ensuite seulement appliquer le state UI
            if (nbMission <= 0)
            {
                Main_Form.Instance.CampaignView.SetOobInitMode(true);
            }
            else
            {
                Main_Form.Instance.CampaignView.SetOobActiveMode(true);
            }

        }

        // Charge toutes les campagnes (code existant déplacé ici)
        public async Task LoadCampaignsAsync()
        {
            // Different configurations (DCSA/DCSB...) can contain campaigns with the
            // same folder name; the ConfMod cache is only keyed by that name, so it
            // must be cleared whenever the campaign list is (re)loaded.
            ConfModLoader.ClearCache();

            ResetCurrentCampaign();

            _mainForm.dataGridViewCampaigns.Rows.Clear();

            List<CampaignInfo> campaignUpdateList = new List<CampaignInfo>();

            var LoadCampaigns = Stopwatch.StartNew();

            int nbCampaign = 0;

            bool folderCampExists = System.IO.Directory.Exists(ParamConf.PATH_SavedGames_DCS + @"\Mods\tech\DCE\Missions\Campaigns");

            if (folderCampExists)
            {
                foreach (string subFolder in Directory.GetDirectories(ParamConf.PATH_SavedGames_DCS + @"\Mods\tech\DCE\Missions\Campaigns"))
                {

                    // 🔥 cache local des fichiers (1 lecture max)
                    string campInitContent = null;
                    string campStatusContent = null;
                    string oobAirContent = null;

                    string pathCampInitFile = subFolder + @"\Init\camp_init.lua";
                    if (File.Exists(pathCampInitFile))
                        campInitContent = File.ReadAllText(pathCampInitFile);

                    string pathCampstatusFile = subFolder + @"\Active\camp_status.lua";
                    if (File.Exists(pathCampstatusFile))
                        campStatusContent = File.ReadAllText(pathCampstatusFile);

                    string path_oob_air;


                    //  COPIE ICI TOUT TON CODE ACTUEL DE LA BOUCLE
                    string[] NameCampTab = subFolder.Split('\\');
                    string NameCamp = NameCampTab[NameCampTab.Count() - 1];

                    bool folderLocExists = System.IO.Directory.Exists(subFolder);

                    //cherche la version inscrite dans path.bat
                    string PathBatFile = subFolder + @"\Init\path.bat";
                    bool fileExistPathBat = File.Exists(PathBatFile);

                    if (fileExistPathBat)
                    {
                        if (ParamConf.PATH_DCS_Root != "" & ParamConf.PATH_SavedGames_DCS != "")
                        {

                            string textPathBat = "REM Core or Main DCS ou DCS.beta path, always end the line with \\ \r\n" +
                           "set \"pathDCS=" + ParamConf.PATH_DCS_Root + "\\\"\r\n" +
                           "REM Core or Main DCS ou DCS.beta path, always end the line with \\ \r\n" +
                           "set \"pathSavedGames=" + ParamConf.PATH_SavedGames_DCS + "\\\"\r\n" +
                           "REM DCE ScriptMod version not any / or \\ and no space before and after = \r\n" +
                           "set \"versionPackageICM=" + TestFile.ScriptsMod + "\"\r\n" +
                           "\r\n" +
                           "\r\n" +
                           "REM After each change, You must launch the FirsMission.bat for it to be taken into account.";

                            System.IO.File.WriteAllText(PathBatFile, textPathBat);
                        }



                        nbCampaign++;

                        //Cherche la version de la campagne
                        string VerCamp = "";
                        string campaignId = "";
                        string repositoryUrl = "";
                        if (campInitContent != null)
                        {
                            Match match;

                            match = Regex.Match(
                                campInitContent,
                                @"(?<!\w)version\s*=\s*""([^""]+)""");

                            if (match.Success)
                                VerCamp = match.Groups[1].Value;

                            match = Regex.Match(
                                campInitContent,
                                @"campaignId\s*=\s*""([^""]+)""");

                            if (match.Success)
                                campaignId = match.Groups[1].Value;

                            match = Regex.Match(
                                campInitContent,
                                @"repositoryUrl\s*=\s*""([^""]+)""");

                            if (match.Success)
                                repositoryUrl = match.Groups[1].Value;
                        }


                        //Cherche le nombre de mission joué
                        //['mission'] = 1,
                        string NbMission = "0";
                        if (campStatusContent != null)
                        {
                            var match = Regex.Match(campStatusContent, @"mission[""']?\]\s*=\s*(\d+)");
                            if (match.Success)
                                NbMission = (Int32.Parse(match.Groups[1].Value) - 1).ToString();
                        }


                        //cherche si une campagne doit etre reset a la suite d'un update 
                        //TODO ? non, il faudra sortir "reset si upadate fait"
                        var campaignNameTab = new Dictionary<string, string>();

                        string colorFM = "";
                        string colorSM = "";
                        if (folderLocExists)
                        {

                            //string path_oob_air = "";
                            if (Int32.Parse(NbMission) >= 1)
                                path_oob_air = subFolder + @"\Active\oob_air.lua";
                            else
                                path_oob_air = subFolder + @"\Init\oob_air_init.lua";

                            if (File.Exists(path_oob_air))
                                oobAirContent = File.ReadAllText(path_oob_air);


                            string type = "default";

                            if (!string.IsNullOrEmpty(oobAirContent))
                            {
                                string content = oobAirContent;

                                // Supprime les commentaires Lua
                                content = Regex.Replace(content, @"--\[\[.*?\]\]", "", RegexOptions.Singleline);
                                content = Regex.Replace(content, @"--.*?$", "", RegexOptions.Multiline);

                                // Trouve player = true ou ["player"] = true
                                Match playerMatch = Regex.Match(
                                    content,
                                    @"(?:\[\s*""player""\s*\]|player)\s*=\s*true",
                                    RegexOptions.IgnoreCase);

                                if (playerMatch.Success)
                                {
                                    int playerPos = playerMatch.Index;

                                    // Remonte jusqu'au { du bloc contenant ce player
                                    int level = 0;
                                    int blockStart = -1;

                                    for (int i = playerPos; i >= 0; i--)
                                    {
                                        if (content[i] == '}')
                                        {
                                            level++;
                                        }
                                        else if (content[i] == '{')
                                        {
                                            if (level == 0)
                                            {
                                                blockStart = i;
                                                break;
                                            }

                                            level--;
                                        }
                                    }

                                    if (blockStart >= 0)
                                    {
                                        // Redescend jusqu'à la } correspondante
                                        level = 1;
                                        int blockEnd = -1;

                                        for (int i = blockStart + 1; i < content.Length; i++)
                                        {
                                            if (content[i] == '{')
                                                level++;
                                            else if (content[i] == '}')
                                                level--;

                                            if (level == 0)
                                            {
                                                blockEnd = i;
                                                break;
                                            }
                                        }

                                        if (blockEnd > blockStart)
                                        {
                                            string block = content.Substring(blockStart, blockEnd - blockStart + 1);

                                            Match typeMatch = Regex.Match(
                                                block,
                                                @"(?:\[\s*""type""\s*\]|type)\s*=\s*""([^""]+)""",
                                                RegexOptions.IgnoreCase);

                                            if (typeMatch.Success)
                                                type = typeMatch.Groups[1].Value;
                                        }
                                    }
                                }
                            }


                            //check si plusieurs images par type d'avion existe dans le dossier image
                            string filePNGbyePlane = (ParamConf.PATH_SavedGames_DCS + @"\Mods\tech\DCE\Missions\Campaigns\" + NameCamp + @"\Images\planescreen_" + type + ".png");
                            string filePNG = (ParamConf.PATH_SavedGames_DCS + @"\Mods\tech\DCE\Missions\Campaigns\" + NameCamp + ".png");
                            string fileBMP = ParamConf.PATH_SavedGames_DCS + @"\Mods\tech\DCE\Missions\Campaigns\" + NameCamp + ".bmp";

                            // Copie l'image spécifique à l'avion vers l'image principale de la campagne.
                            // Pourquoi : File.Copy utilise l'API native Windows et consomme moins de CPU que CopyTo.
                            if (File.Exists(filePNGbyePlane))
                            {
                                //File.Copy(filePNGbyePlane, filePNG, true);
                                try
                                {
                                    File.Copy(filePNGbyePlane, filePNG, true);
                                }
                                catch (IOException)
                                {
                                    // ignore si en cours d'utilisation
                                }

                                if (File.Exists(fileBMP))
                                {
                                    File.Delete(fileBMP);
                                }
                            }


                            // Image (avec cache)
                            Image img = null;
                            string imagePath = filePNG;

                            if (campaignImageCache.ContainsKey(imagePath))
                            {
                                img = campaignImageCache[imagePath];
                            }
                            else if (File.Exists(imagePath))
                            {
                                try
                                {
                                    var fileInfo = new FileInfo(imagePath);
                                    if (fileInfo.Length < 100) // seuil sécurité
                                    {
                                        throw new Exception("Image corrompue ou vide : " + imagePath);
                                    }

                                    if (fileInfo.Length < 100)
                                    {
                                        throw new Exception("Image corrompue ou vide : " + imagePath);
                                    }

                                    for (int i = 0; i < 3; i++)
                                    {
                                        try
                                        {
                                            using (var fs = new FileStream(imagePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                                            using (var temp = Image.FromStream(fs))
                                            {
                                                img = new Bitmap(temp);
                                            }
                                            break;
                                        }
                                        catch (IOException)
                                        {
                                            System.Threading.Thread.Sleep(50);
                                        }
                                    }

                                    if (img == null)
                                    {
                                        throw new Exception("Impossible de charger l'image après plusieurs tentatives : " + imagePath);
                                    }

                                    campaignImageCache[imagePath] = img;
                                }
                                catch (Exception ex)
                                {
                                    throw new Exception("Erreur lors du chargement de l'image : " + imagePath, ex);
                                }
                            }

                            // Ajout dans le DataGridView
                            campaignUpdateList.Add(
                                new CampaignInfo()
                                {
                                    Name = NameCamp,
                                    CampaignId = campaignId,
                                    RepositoryUrl = repositoryUrl,
                                    LocalVersion = VerCamp,
                                    Folder = subFolder
                                });

                            // Le bouton Skip ne doit être visible que si au moins une mission a été jouée
                            int nbMissionParsed;
                            int.TryParse(NbMission, out nbMissionParsed);
                            bool skipVisible = nbMissionParsed > 0;

                            _mainForm.dataGridViewCampaigns.Rows.Add(
                                null,       // Clone (bouton)
                                img,        // Image
                                NameCamp,   // Name
                                null,       // Folder
                                VerCamp,    // Version
                                NbMission,  // Missions
                                type,       // Aircraft
                                null,       // First
                                skipVisible ? "⏭" : "",   // Skip (vide = invisible)
                                null,       // Config
                                null        // Delete
                            );

                            int rowIndex = _mainForm.dataGridViewCampaigns.Rows.Count - 1;

                            // Aucune mission jouée : le bouton reste vide (pas d'icône) et non cliquable
                            _mainForm.dataGridViewCampaigns.Rows[rowIndex].Cells["Skip"].ReadOnly = !skipVisible;

                            // Exemple : bouton Skip rouge si besoin (uniquement si visible)
                            if (skipVisible && colorSM == "red")
                            {
                                _mainForm.dataGridViewCampaigns.Rows[rowIndex].Cells["Skip"].Style.BackColor = Color.DarkRed;
                            }

                            // Exemple : bouton First rouge
                            if (colorFM == "red")
                            {
                                _mainForm.dataGridViewCampaigns.Rows[rowIndex].Cells["First"].Style.BackColor = Color.DarkRed;
                            }

                        }
                    }
                }

            }

            LoadCampaigns.Stop();
            FormUtils.LogRegister($"LoadCampaigns : {LoadCampaigns.ElapsedMilliseconds} ms");
        }


        public void Campaign_CLONE_ClickOneEvent(object sender, EventArgs e, string path, string OldNameCamp)
        {

            // Assurez-vous d'appeler UpdateSharedData avant d'ouvrir Form3_Clonage
            //UpdateSharedData();

            //Test.Form3_Clonage CloneForm = new Test.Form3_Clonage(this, path, OldNameCamp);
            DCE_Manager.Clone_Form CloneForm = new DCE_Manager.Clone_Form(_mainForm, path, OldNameCamp);
            CloneForm.Show();


        }

        public void CampaignEdit1(object sender, EventArgs e, string path, string NameCamp)
        {
            var time_CampaignEdit1 = Stopwatch.StartNew();

            // 🔧 Nettoyage de l'ancienne instance
            if (CurrentCampaignEdit != null)
            {
                CurrentCampaignEdit.Dispose(); // 🔥 IMPORTANT

                CurrentCampaignEdit = null;
                UpdateCampaignButtonsVisibility();


                //_mainForm.label_Right_Campaign_Name.Text = "";
                Main_Form.Instance.CampaignView.label_Right_Campaign_Name.Text = "";
            }

            // 🔧 Nouvelle instance
            CurrentCampaignEdit = new CampaignEdit(_mainForm, this, NameCamp);

            UpdateCampaignButtonsVisibility();

            time_CampaignEdit1.Stop();
        }


        public void UpdateCampaignButtonsVisibility()
        {
            bool campaignSelected = CurrentCampaignEdit != null;

            bool show_A = campaignSelected && _mainForm.CampaignView.IsOobTabSelected;


            bool show_B = campaignSelected;

            Main_Form.Instance.CampaignView.EnableSaveButton(show_A);
            Main_Form.Instance.CampaignView.EnableResetButton(show_A);
            Main_Form.Instance.CampaignView.SetOobInitMode(show_A);
            Main_Form.Instance.CampaignView.SetOobActiveMode(show_A);

            _mainForm.CampaignView.ShowCampaignName(show_B);
        }



        public void RefreshGrids()
        {
            CampaignEdit.LoadGridStatic(Main_Form.Instance.CampaignView.DataGridViewBlue, _mainForm.currentSquads, "blue", _mainForm.currentState);
            CampaignEdit.LoadGridStatic(Main_Form.Instance.CampaignView.DataGridViewRed, _mainForm.currentSquads, "red", _mainForm.currentState);

        }


        public async void CampaignDataGridView_CellContentClick(object sender, DataGridViewCellEventArgs e)
        {


            if (e.RowIndex < 0)
                return;

            if (_mainForm.CampaignDataGridView.Columns[e.ColumnIndex].Name == "Repo")
            {
                CampaignInfo campaignRepo = _mainForm.campaignUpdater.GetCampaignFromRow(e.RowIndex);

                if (campaignRepo != null && !string.IsNullOrWhiteSpace(campaignRepo.RepositoryUrl))
                {
                    System.Diagnostics.Process.Start(new ProcessStartInfo
                    {
                        FileName = campaignRepo.RepositoryUrl,
                        UseShellExecute = true
                    });
                }

                return;
            }

            if (_mainForm.CampaignDataGridView.Columns[e.ColumnIndex].Name != "Action")
                return;

            CampaignInfo campaign = _mainForm.campaignUpdater.GetCampaignFromRow(e.RowIndex);

            if (campaign == null || string.IsNullOrWhiteSpace(campaign.DownloadUrl))
                return;

            if (!campaign.UpdateAvailable)
                return;


            //CampaignDataGridView.Enabled = false;
            //groupBox_DwlCampaign.Visible = true;

            _mainForm.pictureBoxCampaignDownload.Visible = true;

            _mainForm.labelCampaignDownload.Visible = true;

            _mainForm.progressBarCampaignDownload.Visible = true;
            _mainForm.buttonCampaignCancel.Visible = true;

            try
            {

                _mainForm.buttonCampaignCancel.Enabled = true;
                _mainForm.buttonCampaignCancel.Visible = true;
                _mainForm.labelCampaignDownload.Visible = true;


                string zipFile = await _mainForm.campaignUpdater.DownloadCampaign(
                    campaign,
                    _mainForm.progressBarCampaignDownload,
                    _mainForm.labelCampaignDownload,
                    _mainForm.labelCampaignDld_Pct,
                    _mainForm.labelCampaignTitle);

                if (string.IsNullOrEmpty(zipFile))
                {
                    FormUtils.LogRegister("Campaign download cancelled.");

                    return;
                }

                FormUtils.LogRegister("Campaign downloaded : " + zipFile);

                _mainForm.campaignUpdater.ExtractCampaignZip(
                    zipFile,
                    ParamConf.PATH_SavedGames_DCS,
                    campaign);

                //FormUtils.LogRegister("FormMain Campaign installed RefreshCampaignUpdates()");

                //await _mainForm.campaignUpdater.RefreshCampaignUpdates(
                //    _mainForm.CampaignDataGridView,
                //    _mainForm.textBox_SavedGames.Text);

                FormUtils.LogRegister("FormMain Campaign installed - rafraîchissement local (sans requête GitHub)");

                _mainForm.campaignUpdater.RefreshCampaignUpdatesLocalOnly(
                    _mainForm.CampaignDataGridView,
                    ParamConf.PATH_SavedGames_DCS);


            }
            finally
            {
                _mainForm.CampaignDataGridView.Enabled = true;

                _mainForm.buttonCampaignCancel.Enabled = false;
                _mainForm.buttonCampaignCancel.Visible = false;

                _mainForm.progressBarCampaignDownload.Visible = false;
                _mainForm.labelCampaignDownload.Visible = false;
                _mainForm.labelCampaignDld_Pct.Visible = false;
                _mainForm.labelCampaignDownload.Visible = false;
                _mainForm.groupBox_DwlCampaign.Visible = true;

                _mainForm.pictureBoxCampaignDownload.Image = Properties.Resources.icons8_ok_24;

            }

            //string zipFile = await campaignUpdater.DownloadCampaign(campaign);


        }

        public void ResetCurrentCampaign()
        {
            if (CurrentCampaignEdit != null)
            {
                CurrentCampaignEdit.Dispose();
                CurrentCampaignEdit = null;
            }

            _mainForm.dataGridViewCampaigns.ClearSelection();

            Main_Form.Instance.CampaignView.label_Right_Campaign_Name.Text = "";
            Main_Form.Instance.CampaignView.textBoxCampBriefing.Clear();

            Main_Form.Instance.CampaignView.pictureBoxCampImage.Image?.Dispose();
            Main_Form.Instance.CampaignView.pictureBoxCampImage.Image = null;

            Main_Form.Instance.CampaignView.DataGridViewBlue.DataSource = null;
            Main_Form.Instance.CampaignView.DataGridViewBlue.Columns.Clear();

            Main_Form.Instance.CampaignView.DataGridViewRed.DataSource = null;
            Main_Form.Instance.CampaignView.DataGridViewRed.Columns.Clear();

            _mainForm.currentSquads = new List<Squad>();

            //_mainForm.CampaignTab.Visible = false;
            Main_Form.Instance.ShowHome();

            ParamCampaignSelected.NameCampaign = "";

            CurrentCampaignEdit = null;
            UpdateCampaignButtonsVisibility();
        }

        public void buttonCampaignCancel_Click(object sender, EventArgs e)
        {
            FormUtils.LogRegister("buttonCampaignCancel_Click");

            _mainForm.campaignUpdater.CancelDownload();


            _mainForm.labelCampaignTitle.Visible = false;
            _mainForm.pictureBoxCampaignDownload.Visible = false;
        }


        public void groupBox_UpdateCampaign_Enter(object sender, EventArgs e)
        {

        }

        



    }
}
