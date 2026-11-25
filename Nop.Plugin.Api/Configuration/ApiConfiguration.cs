using Nop.Core.Configuration;
using System.Text;

namespace Nop.Plugin.Api.Configuration
{
    public class ApiConfiguration : IConfig
    {
        public int AllowedClockSkewInMinutes { get; set; } = 5;

        public string SecurityKey { get; set; }

        internal byte[] GetSigningKey()
        {
            const string legacyDefault = "NowIsTheTimeForAllGoodMenToComeToTheAideOfTheirCountry";
            return !string.IsNullOrWhiteSpace(SecurityKey) && SecurityKey != legacyDefault &&
                   Encoding.UTF8.GetByteCount(SecurityKey) >= 32
                ? Encoding.UTF8.GetBytes(SecurityKey)
                : null;
        }
    }
}
