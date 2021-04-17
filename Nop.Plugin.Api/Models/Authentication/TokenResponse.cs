using System;
using Newtonsoft.Json;

namespace Nop.Plugin.Api.Models.Authentication
{
    public class TokenResponse
    {
        public TokenResponse(string accessToken, DateTime createdAtUtc, long expiresInSeconds)
        {
            AccessToken = accessToken;
            CreatedAtUtc = createdAtUtc;
            ExpiresInSeconds = expiresInSeconds;
        }

        [JsonProperty("access_token", Required = Required.Always)]
        public string AccessToken { get; init; }

        [JsonProperty("token_type", Required = Required.Always)]
        public string TokenType { get; init; } = "Bearer";

        [JsonProperty("expires_in")]
        public long ExpiresInSeconds { get; init; }

        [JsonProperty("created_at_utc")]
        public DateTime CreatedAtUtc { get; init; }

        [JsonProperty("username")]
        public string Username { get; init; }

        [JsonProperty("customer_id")]
        public int CustomerId { get; init; }

        [JsonProperty("customer_guid")]
        public Guid CustomerGuid { get; init; }
    }
}
