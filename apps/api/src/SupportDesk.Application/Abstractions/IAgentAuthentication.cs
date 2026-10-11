namespace SupportDesk.Application.Abstractions;

public sealed record AuthAgent(int Id, string FullName, string Email);

public interface IAgentAuthentication
{
    Task<AuthAgent?> VerifyAsync(string email, string password, CancellationToken ct);
    Task<AuthAgent?> GetActiveAsync(int id, CancellationToken ct);
}
