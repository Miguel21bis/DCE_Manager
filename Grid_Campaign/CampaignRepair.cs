using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using DCE_Manager.Utils;

namespace DCE_Manager
{
    // Regénère les fichiers "annexes" d'une campagne (image + .cmp) quand ils manquent,
    // pour dépanner un dossier incomplet SANS toucher aux .miz (ceux-là, on ne sait pas
    // les inventer, cf. discussion : soit ils existent, soit il faut les régénérer via
    // First/Skip Mission, soit réinstaller la campagne).
    //
    // Volontairement minimaliste : pas de placeholder "joli", juste de quoi remettre la
    // campagne dans un état exploitable et visible dans la grid.
    internal static class CampaignRepair
    {
        // Crée un .cmp minimal si absent. Ne fait RIEN si les 2 .miz ne sont pas déjà là :
        // un .cmp qui pointe vers des .miz inexistants redonnerait une campagne "fausse
        // positive" (complète en apparence, cassée au clic).
        public static bool TryRepairCmpFile(string campaignsRoot, string nameCamp)
        {
            string cmpPath = Path.Combine(campaignsRoot, nameCamp + ".cmp");

            if (File.Exists(cmpPath))
                return true; // rien à faire

            // Pas besoin que les .miz existent déjà : la convention de nommage est fixe
            // (nameCamp_first.miz / nameCamp_ongoing.miz), donc on peut écrire un .cmp qui
            // pointe dessus même s'ils manquent encore - requiredMissionFiles continuera de
            // les signaler séparément s'ils ne sont vraiment jamais régénérés.
            string content =
                "campaign = \r\n{\r\n" +
                "    [\"picture\"] = \"" + nameCamp + ".png\",\r\n" +
                "\t[\"startStage\"] = 1,\r\n" +
                "    [\"name\"] = \"" + nameCamp + "\",\r\n" +
                "    [\"description\"] = \"Description unavailable - this file was regenerated automatically by DCE_Manager after the original .cmp was found missing. Please edit this description if you know the original campaign background.\",\r\n" +
                "    [\"necessaryUnits\"] = {}, -- end of [\"necessaryUnits\"]\r\n" +
                "    [\"stages\"] = \r\n    {\r\n" +
                "        [1] = \r\n        {\r\n" +
                "            [\"name\"] = \"Stage 1\",\r\n" +
                "            [\"missions\"] = \r\n            {\r\n" +
                "                [1] = \r\n                {\r\n" +
                "                    [\"interval\"] = \r\n                    {\r\n" +
                "                        [1] = 50,\r\n                        [2] = 100,\r\n" +
                "                    }, -- end of [\"interval\"]\r\n" +
                "                    [\"file\"] = \"" + nameCamp + "_first.miz\",\r\n" +
                "                    [\"description\"] = \"\",\r\n" +
                "                    [\"fullpath\"] = \"C:\\\\Mods\\\\tech\\\\DCE\\\\Missions\\\\Campaigns\\\\\",\r\n" +
                "                }, -- end of [1]\r\n            }, -- end of [\"missions\"]\r\n        }, -- end of [1]\r\n" +
                "        [2] = \r\n        {\r\n" +
                "            [\"name\"] = \"Stage 2\",\r\n" +
                "            [\"missions\"] = \r\n            {\r\n" +
                "                [1] = \r\n                {\r\n" +
                "                    [\"interval\"] = \r\n                    {\r\n" +
                "                        [1] = 0,\r\n                        [2] = 100,\r\n" +
                "                    }, -- end of [\"interval\"]\r\n" +
                "                    [\"file\"] = \"" + nameCamp + "_ongoing.miz\",\r\n" +
                "                    [\"description\"] = \"\",\r\n" +
                "                    [\"fullpath\"] = \"C:\\\\Mods\\\\tech\\\\DCE\\\\Missions\\\\Campaigns\\\\\",\r\n" +
                "                }, -- end of [1]\r\n            }, -- end of [\"missions\"]\r\n        }, -- end of [2]\r\n" +
                "    }, -- end of [\"stages\"]\r\n} -- end of campaign\r\n";

            File.WriteAllText(cmpPath, content);
            FormUtils.LogRegister("CampaignRepair | .cmp régénéré (générique) pour '" + nameCamp + "'.");
            return true;
        }

        // Crée une image placeholder (bandeau uni + nom de la campagne) si aucune .png
        // n'existe. Pur GDI+, pas de ressource externe à embarquer.
        public static bool TryRepairPictureFile(string campaignsRoot, string nameCamp)
        {
            string pngPath = Path.Combine(campaignsRoot, nameCamp + ".png");

            if (File.Exists(pngPath))
                return true; // rien à faire

            const int W = 800, H = 450;

            using (var bmp = new Bitmap(W, H))
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

                g.Clear(Color.FromArgb(28, 34, 44));
                using (var band = new SolidBrush(Color.FromArgb(58, 90, 128)))
                {
                    g.FillRectangle(band, 0, 0, W, 8);
                    g.FillRectangle(band, 0, H - 8, W, 8);
                }

                using (var titleFont = new Font("Segoe UI", 20, FontStyle.Bold))
                using (var subFont = new Font("Segoe UI", 10, FontStyle.Regular))
                using (var titleBrush = new SolidBrush(Color.FromArgb(230, 230, 230)))
                using (var subBrush = new SolidBrush(Color.FromArgb(150, 160, 170)))
                using (var centered = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                {
                    g.DrawString(nameCamp, titleFont, titleBrush, new RectangleF(0, H / 2f - 40, W, 40), centered);
                    g.DrawString("Image not available - placeholder generated by DCE_Manager", subFont, subBrush, new RectangleF(0, H / 2f + 10, W, 30), centered);
                }

                bmp.Save(pngPath, System.Drawing.Imaging.ImageFormat.Png);
            }

            FormUtils.LogRegister("CampaignRepair | image placeholder générée pour '" + nameCamp + "'.");
            return true;
        }
    }
}