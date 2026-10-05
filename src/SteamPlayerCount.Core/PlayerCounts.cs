using System.Threading;
using System.Threading.Tasks;

namespace SteamPlayerCount.Core
{
    public enum CountStatus
    {
        Success,
        NoData,
        Failed,
    }

    public sealed class CountOutcome
    {
        private CountOutcome(CountStatus status, int playerCount)
        {
            Status = status;
            PlayerCount = playerCount;
        }

        public CountStatus Status { get; }
        public int PlayerCount { get; }

        public static CountOutcome Success(int playerCount)
        {
            return new CountOutcome(CountStatus.Success, playerCount);
        }

        public static CountOutcome NoData()
        {
            return new CountOutcome(CountStatus.NoData, 0);
        }

        public static CountOutcome Failed()
        {
            return new CountOutcome(CountStatus.Failed, 0);
        }
    }

    public interface ISteamPlayerCounts
    {
        Task<CountOutcome> GetAsync(int appId, CancellationToken ct);
    }

    public sealed class PlayerCountResult
    {
        public static readonly PlayerCountResult None = new PlayerCountResult();

        private PlayerCountResult()
        {
        }

        public PlayerCountResult(int appId, string steamName, int playerCount)
        {
            HasCount = true;
            AppId = appId;
            SteamName = steamName;
            PlayerCount = playerCount;
        }

        public bool HasCount { get; }
        public int AppId { get; }
        public string SteamName { get; }
        public int PlayerCount { get; }
    }
}
