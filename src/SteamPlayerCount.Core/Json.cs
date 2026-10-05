using System;
using System.IO;
using System.Runtime.Serialization.Json;
using System.Text;

namespace SteamPlayerCount.Core
{
    // Framework-only JSON. Playnite's serializer only works inside Playnite, so it cannot be unit-tested.
    public static class Json
    {
        public static bool TryDeserialize<T>(string json, out T value) where T : class
        {
            value = null;
            if (string.IsNullOrWhiteSpace(json))
            {
                return false;
            }

            try
            {
                var serializer = new DataContractJsonSerializer(typeof(T));
                using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(json)))
                {
                    value = serializer.ReadObject(stream) as T;
                }

                return value != null;
            }
            catch (Exception)
            {
                value = null;
                return false;
            }
        }

        public static string Serialize<T>(T value)
        {
            var serializer = new DataContractJsonSerializer(typeof(T));
            using (var stream = new MemoryStream())
            {
                serializer.WriteObject(stream, value);
                return Encoding.UTF8.GetString(stream.ToArray());
            }
        }
    }
}
