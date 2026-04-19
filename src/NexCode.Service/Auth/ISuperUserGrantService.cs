namespace NexCode.Service.Auth;

public interface ISuperUserGrantService
{
    Task<bool> HasValidGrantAsync(string? email, CancellationToken cancellationToken = default);
}
