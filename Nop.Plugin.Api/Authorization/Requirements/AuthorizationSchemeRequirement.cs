using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;

namespace Nop.Plugin.Api.Authorization.Requirements
{
    public class AuthorizationSchemeRequirement : IAuthorizationRequirement
    {
        public bool IsValid(IHeaderDictionary requestHeaders) => HasBearerCredential(requestHeaders);

        internal static bool HasBearerCredential(IHeaderDictionary headers)
        {
            if (headers is null)
                return false;

            return headers.Authorization.Any(header =>
            {
                var value = header.AsSpan().TrimStart();
                var scheme = JwtBearerDefaults.AuthenticationScheme.AsSpan();
                return value.StartsWith(scheme, StringComparison.OrdinalIgnoreCase) &&
                    (value.Length == scheme.Length || char.IsWhiteSpace(value[scheme.Length]));
            });
        }
    }
}
