using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using DCE_Manager.Parameters;

namespace DCE_Manager
{
    // Une statistique du scoreboard ("15 (+1)" -> Value=15, Delta=1, HasDelta=true)
    public class ScoreboardStat
    {
        public string Label { get; set; }
        public int Value { get; set; }
        public int Delta { get; set; }
        public bool HasDelta { get; set; }
        public string Raw { get; set; } // valeur brute ("-" si vide)
    }

    // Une ligne du scoreboard = un joueur + toutes ses stats dans l'ordre des colonnes
    public class ScoreboardEntry
    {
        public string PlayerName { get; set; }
        public List<ScoreboardStat> Stats { get; set; } = new List<ScoreboardStat>();
    }

    internal class Parser_Debriefing_Scoreboard
    {
        // Colonnes dans l'ordre où elles apparaissent dans le fichier Debriefing (après "Name")
        private static readonly string[] ColumnLabels =
        {
            "Missions", "Kills Air", "Kills Ground", "Kills Ship",
            "Rescue", "Crashed", "Ejected", "MIA", "Rescued", "POW", "Dead"
        };

        // Cherche le dossier Debriefing de la campagne, prend le fichier "Debriefing N.txt"
        // avec le N le plus élevé, et en extrait le tableau Scoreboard.
        public List<ScoreboardEntry> LoadLatestScoreboard(string campaignName)
        {
            string debriefFolder = Path.Combine(
                ParamConf.PATH_SavedGames_DCS, "Mods", "tech", "DCE", "Missions", "Campaigns",
                campaignName, "Debriefing");

            string latestFile = FindLatestDebriefingFile(debriefFolder);

            if (latestFile == null)
                return new List<ScoreboardEntry>();

            string[] lines = File.ReadAllLines(latestFile);
            return ParseScoreboard(lines);
        }

        // Les fichiers sont nommés "Debriefing 7.txt", "Debriefing 15.txt", etc.
        // On prend celui avec le plus grand numéro (pas la date de modif, plus fiable).
        private string FindLatestDebriefingFile(string folder)
        {
            if (!Directory.Exists(folder))
                return null;

            var regex = new Regex(@"Debriefing\s+(\d+)\.txt$", RegexOptions.IgnoreCase);

            string best = null;
            int bestNum = -1;

            foreach (string file in Directory.GetFiles(folder, "Debriefing*.txt"))
            {
                Match m = regex.Match(Path.GetFileName(file));
                if (!m.Success) continue;

                int num = int.Parse(m.Groups[1].Value);
                if (num > bestNum)
                {
                    bestNum = num;
                    best = file;
                }
            }

            return best;
        }

        private List<ScoreboardEntry> ParseScoreboard(string[] lines)
        {
            var result = new List<ScoreboardEntry>();

            int startIndex = Array.FindIndex(lines,
                l => l.Trim().StartsWith("Scoreboard", StringComparison.OrdinalIgnoreCase));

            if (startIndex < 0)
                return result;

            // Cherche la ligne d'en-tête ("Name   Missions   Kills Air ...") juste après "Scoreboard:"
            int headerIndex = -1;
            for (int i = startIndex + 1; i < lines.Length && i < startIndex + 6; i++)
            {
                if (lines[i].TrimStart().StartsWith("Name"))
                {
                    headerIndex = i;
                    break;
                }
            }

            if (headerIndex < 0)
                return result;

            for (int i = headerIndex + 1; i < lines.Length; i++)
            {
                string line = lines[i];

                if (string.IsNullOrWhiteSpace(line))
                    break; // fin du tableau

                // Les colonnes sont séparées par au moins 2 espaces (le nom du joueur peut
                // contenir des espaces simples, ex: "RAYAK_1-1 | Corse")
                string[] tokens = Regex.Split(line.TrimEnd(), @"\s{2,}");
                if (tokens.Length < 2)
                    continue;

                var entry = new ScoreboardEntry { PlayerName = tokens[0].Trim() };

                for (int c = 0; c < ColumnLabels.Length; c++)
                {
                    string raw = (c + 1) < tokens.Length ? tokens[c + 1].Trim() : "-";
                    entry.Stats.Add(ParseStat(ColumnLabels[c], raw));
                }

                result.Add(entry);
            }

            return result;
        }

        // "15 (+1)" -> Value=15, Delta=1, HasDelta=true / "-" -> Value=0, Raw="-"
        private ScoreboardStat ParseStat(string label, string raw)
        {
            var stat = new ScoreboardStat { Label = label, Raw = raw };

            if (raw == "-" || string.IsNullOrWhiteSpace(raw))
                return stat;

            Match m = Regex.Match(raw, @"^(-?\d+)\s*(?:\(([+-]\d+)\))?$");
            if (m.Success)
            {
                stat.Value = int.Parse(m.Groups[1].Value);
                if (m.Groups[2].Success)
                {
                    stat.Delta = int.Parse(m.Groups[2].Value);
                    stat.HasDelta = true;
                }
            }

            return stat;
        }
    }
}