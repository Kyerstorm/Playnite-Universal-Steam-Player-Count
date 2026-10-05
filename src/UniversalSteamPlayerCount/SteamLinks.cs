using System;
using System.Diagnostics;
using System.Globalization;
using Playnite.SDK;

namespace UniversalSteamPlayerCount
{
    public static class SteamLinks
    {
        public static void OpenGraphs(int appId, ILogger logger)
        {
            if (appId <= 0)
            {
                return;
            }

            var url = string.Format(CultureInfo.InvariantCulture, "https://steamdb.info/app/{0}/graphs/", appId);
            try
            {
                Process.Start(url);
            }
            catch (Exception e)
            {
                logger.Error(e, "Could not open " + url);
            }
        }
    }
}
