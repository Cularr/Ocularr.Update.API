using System.Security.Cryptography;
using System.Text;

namespace Ocularr.Update.API.Util
{
    public static class SecretComparer
    {
        public static bool Matches(string expected, string given) =>
            !string.IsNullOrEmpty(expected) &&
            CryptographicOperations.FixedTimeEquals(SHA256.HashData(Encoding.UTF8.GetBytes(expected)), SHA256.HashData(Encoding.UTF8.GetBytes(given ?? string.Empty)));
    }
}
