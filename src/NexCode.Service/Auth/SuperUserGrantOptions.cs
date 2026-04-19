namespace NexCode.Service.Auth;

public sealed class SuperUserGrantOptions
{
    public const string SectionName = "SuperUserGrant";

    public string GrantFileName { get; set; } = "superuser.grant";

    public string? PublicKeyPem { get; set; }
}
