namespace SteamPlayerCount.Core
{
    public sealed class SteamCandidate
    {
        public SteamCandidate(int appId, string name, string type)
        {
            AppId = appId;
            Name = name;
            Type = type;
        }

        public int AppId { get; }
        public string Name { get; }
        public string Type { get; }
    }
}
