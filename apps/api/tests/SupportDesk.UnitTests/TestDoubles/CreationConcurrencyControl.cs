using System.Collections.Concurrent;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using SupportDesk.Application.Abstractions;
using SupportDesk.Application.Contracts.Agents;
using SupportDesk.Domain.Aggregates.Tickets;
using SupportDesk.Domain.Repositories;

namespace SupportDesk.UnitTests.TestDoubles;

// Test-only scheduling gates. Real queries, reference generation and saves remain unchanged.
public sealed class CreationConcurrencyControl
{
    public enum Schedule { None, CapacityRace, ReferenceRace }
    public Schedule Mode { get; set; }
    public ConcurrentBag<string> References { get; } = new();
    public ConcurrentBag<int> SqlErrorNumbers { get; } = new();
    private readonly TaskCompletionSource bothRead = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource firstSaved = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int arrivals;
    private int referenceCalls;

    private async Task WaitForBothAsync(CancellationToken ct)
    {
        if (Interlocked.Increment(ref arrivals) == 2) bothRead.TrySetResult();
        await bothRead.Task.WaitAsync(TimeSpan.FromSeconds(15), ct);
    }

    public sealed class Agents(IAgentQueries inner, CreationConcurrencyControl control) : IAgentQueries
    {
        public async Task<IReadOnlyList<AgentDto>> GetAllAsync(CancellationToken ct)
        {
            var result = await inner.GetAllAsync(ct);
            if (control.Mode == Schedule.CapacityRace) await control.WaitForBothAsync(ct);
            return result;
        }
    }

    public sealed class Tickets(ITicketRepository inner, CreationConcurrencyControl control) : ITicketRepository
    {
        public Task<Ticket?> GetByIdAsync(int id, CancellationToken ct) => inner.GetByIdAsync(id, ct);
        public Task AddAsync(Ticket ticket, CancellationToken ct) => inner.AddAsync(ticket, ct);

        public async Task<string> NextReferenceAsync(CancellationToken ct)
        {
            // Isolate the capacity race: let the second reference read happen after the first insert.
            if (control.Mode == Schedule.CapacityRace && Interlocked.Increment(ref control.referenceCalls) == 2)
                await control.firstSaved.Task.WaitAsync(TimeSpan.FromSeconds(15), ct);
            var result = await inner.NextReferenceAsync(ct);
            if (control.Mode == Schedule.ReferenceRace)
            {
                control.References.Add(result);
                await control.WaitForBothAsync(ct);
            }
            return result;
        }
    }

    public sealed class UnitOfWork(IUnitOfWork inner, CreationConcurrencyControl control) : IUnitOfWork
    {
        public async Task<int> SaveChangesAsync(CancellationToken ct)
        {
            try
            {
                var result = await inner.SaveChangesAsync(ct);
                control.firstSaved.TrySetResult();
                return result;
            }
            catch (DbUpdateException error) when (error.InnerException is SqlException sqlError)
            {
                control.SqlErrorNumbers.Add(sqlError.Number);
                throw;
            }
        }
    }
}

public sealed class SqlServerFactAttribute : FactAttribute
{
    public SqlServerFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("SUPPORTDESK_TEST_SQLSERVER")))
            Skip = "Set SUPPORTDESK_TEST_SQLSERVER to run isolated SQL Server concurrency checks.";
    }
}
