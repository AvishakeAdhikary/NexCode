namespace NexCode.Gui.Services;

internal sealed class HelperRequestException(int code, string message) : InvalidOperationException(message)
{
    public int Code { get; } = code;
}
