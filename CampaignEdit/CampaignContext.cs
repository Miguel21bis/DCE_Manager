using System.Collections.Generic;
using DCE_Manager.Parameters;

namespace DCE_Manager
{
    public class CampaignContext
    {
        public string CampaignName { get; set; }

        public CampaignLuaData LuaData { get; set; }

        public Dictionary<string, AirbaseInfo> Airbases { get; set; }

        public CampaignContext()
        {
            Airbases = new Dictionary<string, AirbaseInfo>();
        }
    }
}
