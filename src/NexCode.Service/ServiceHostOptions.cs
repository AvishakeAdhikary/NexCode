namespace NexCode.Service;

public sealed class ServiceHostOptions
{
    public const string SectionName = "ServiceHost";

    public string PipeName { get; set; } = "nexcode-service-dev";

    public string Version { get; set; } = "0.1.0-foundation";
}
