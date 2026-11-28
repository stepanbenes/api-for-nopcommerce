namespace Nop.Plugin.Api.Attributes;

public enum ApiAudience
{
    Storefront,
    BackOffice,
    Both
}

// Documentation metadata only. Authorization is enforced independently.
[AttributeUsage(AttributeTargets.Method)]
public sealed class ApiAudienceAttribute(ApiAudience audience) : Attribute
{
    public ApiAudience Audience { get; } = audience;
}
