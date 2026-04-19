namespace NexCode.Remote;

public sealed class RemoteHostOptions
{
    public const string SectionName = "RemoteHost";

    public string Version { get; set; } = "0.2.0-account-foundation";

    public string Transport { get; set; } = "grpc";
}
