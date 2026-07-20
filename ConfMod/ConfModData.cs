namespace DCE_Manager.Parameters
{
    // Modèle du bloc mission_ini de conf_mod.lua.
    // Périmètre actuel : weather, current_date, et le groupe "difficulty options".
    internal class ConfModWeatherData
    {
        public double Trend { get; set; }
        public double Variance { get; set; }
        public double RefTemp { get; set; }
        public double Instability { get; set; }
        public double WindActivity { get; set; }
        public double WinDirection { get; set; }

        // Décale le trend imposé par la campagne (triggers du campaignMaker) vers
        // du beau (+) ou du mauvais (-) temps. 0 = respecte l'intention du campaignMaker.
        public double PlayerBias { get; set; }
    }

    internal class ConfModDateData
    {
        public bool SetDateInNextMission { get; set; }
        public int Day { get; set; }
        public int Month { get; set; }
        public int Year { get; set; }
    }

    internal class ConfModDifficultyData
    {
        // 0 = false ("pas de changement" côté Lua), sinon 1 à 4
        public int CampaignDuration { get; set; }
        // 0 = false ("pas de changement" côté Lua), sinon 1 à 4
        public int EnemyLevel { get; set; }
        public bool RandomizeSkills { get; set; }
        // 0 = false ("pas de changement" côté Lua), sinon 1 à 100
        public int PercentPlane { get; set; }
        public bool StrikeOnlyWithEscorte { get; set; }
    }

    internal class ConfModData
    {
        public string CampaignName { get; set; }

        public ConfModWeatherData Weather { get; set; } = new ConfModWeatherData();
        public ConfModDateData Date { get; set; } = new ConfModDateData();
        public ConfModDifficultyData Difficulty { get; set; } = new ConfModDifficultyData();
    }
}
