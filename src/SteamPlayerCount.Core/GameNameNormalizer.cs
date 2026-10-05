using System.Text;

namespace SteamPlayerCount.Core
{
    public static class GameNameNormalizer
    {
        public static string Normalize(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return string.Empty;
            }

            var source = name.Replace("&", " and ").ToLowerInvariant();
            var builder = new StringBuilder(source.Length);
            foreach (var c in source)
            {
                if (char.IsLetterOrDigit(c))
                {
                    builder.Append(c);
                }
            }

            return builder.ToString();
        }
    }
}
