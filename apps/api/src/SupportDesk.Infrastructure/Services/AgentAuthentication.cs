using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using SupportDesk.Application.Abstractions;
using SupportDesk.Domain.Aggregates.Agents;
using SupportDesk.Infrastructure.Data;

namespace SupportDesk.Infrastructure.Services;

public sealed class AgentAuthentication(SupportDbContext db) : IAgentAuthentication
{
    private static readonly PasswordHasher<Agent> Hasher = new();
    private static readonly Agent Dummy = new("Unknown", "unknown@example.invalid", 1, DateTime.UnixEpoch);
    private static readonly string DummyHash = Hasher.HashPassword(Dummy, Guid.NewGuid().ToString());

    public async Task<AuthAgent?> VerifyAsync(string email, string password, CancellationToken ct)
    {
        var normalized = email.Trim().ToUpperInvariant();
        var agent = await db.Set<Agent>().SingleOrDefaultAsync(a => a.Email.ToUpper() == normalized, ct);
        var result = Hasher.VerifyHashedPassword(agent ?? Dummy, agent?.PasswordHash ?? DummyHash, password);
        if (agent is null || !agent.IsActive || agent.PasswordHash is null || result == PasswordVerificationResult.Failed) return null;
        if (result == PasswordVerificationResult.SuccessRehashNeeded)
        {
            agent.SetPasswordHash(Hasher.HashPassword(agent, password));
            await db.SaveChangesAsync(ct);
        }
        return new AuthAgent(agent.Id, agent.FullName, agent.Email);
    }

    public Task<AuthAgent?> GetActiveAsync(int id, CancellationToken ct) => db.Set<Agent>().AsNoTracking()
        .Where(a => a.Id == id && a.IsActive).Select(a => new AuthAgent(a.Id, a.FullName, a.Email)).SingleOrDefaultAsync(ct);
}
