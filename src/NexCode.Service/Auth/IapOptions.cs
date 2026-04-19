namespace NexCode.Service.Auth;

public sealed class IapOptions
{
    public const string SectionName = "Iap";

    public int CacheLifetimeHours { get; set; } = 24;
}
