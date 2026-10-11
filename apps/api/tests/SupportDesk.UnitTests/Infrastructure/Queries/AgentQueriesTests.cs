using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using SupportDesk.Application.Features.Tickets.Triage;
using SupportDesk.Domain.Aggregates.Agents;
using SupportDesk.Domain.Aggregates.Categories;
using SupportDesk.Domain.Aggregates.Customers;
using SupportDesk.Domain.Aggregates.Tickets;
using SupportDesk.Infrastructure.Data;
using SupportDesk.Infrastructure.Queries;
using SupportDesk.UnitTests.TestDoubles;

namespace SupportDesk.UnitTests.Infrastructure.Queries;

public sealed class AgentQueriesTests : IAsyncLifetime
{
    private readonly SqliteConnection connection = new("Data Source=:memory:");
    private readonly Agent specialist = new("Specialist", "specialist@example.test", 3, FixedClock.DefaultNow);
    private readonly Agent generalist = new("Generalist", "generalist@example.test", 3, FixedClock.DefaultNow);
    private readonly Category category = new("Special category", true, false);
    private SupportDbContext db = null!;
    private Customer customer = null!;

    public async Task InitializeAsync()
    {
        await connection.OpenAsync();
        db = new SupportDbContext(new DbContextOptionsBuilder<SupportDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        customer = new Customer("Customer", "customer@example.test", null, CustomerTier.Standard, FixedClock.DefaultNow);
        db.AddRange(customer, category, specialist, generalist);
        await db.SaveChangesAsync();
        specialist.AddSpecialization(category.Id);
        await db.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        if (db is not null) await db.DisposeAsync();
        await connection.DisposeAsync();
    }

    [Theory]
    [InlineData(TicketStatus.New, 1)]
    [InlineData(TicketStatus.Open, 1)]
    [InlineData(TicketStatus.InProgress, 1)]
    [InlineData(TicketStatus.Resolved, 0)]
    [InlineData(TicketStatus.Closed, 0)]
    public async Task GetAllAsync_CountsOnlyAssignedOpenTickets(TicketStatus status, int expected)
    {
        db.Add(TicketFor("TCK-0001", status, specialist.Id));
        db.Add(TicketFor("TCK-0002", TicketStatus.Open, generalist.Id));
        db.Add(TicketFor("TCK-0003", TicketStatus.Open, null));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var agents = await new AgentQueries(db).GetAllAsync(CancellationToken.None);
        Assert.Equal(expected, agents.Single(agent => agent.Id == specialist.Id).OpenTicketCount);
        Assert.Equal(1, agents.Single(agent => agent.Id == generalist.Id).OpenTicketCount);
    }

    [Fact]
    public async Task GetAllAsync_ReturnsSpecializationsAndWorkloadUsableByAssignmentPolicy()
    {
        db.Add(TicketFor("TCK-0001", TicketStatus.Resolved, specialist.Id));
        db.Add(TicketFor("TCK-0002", TicketStatus.Closed, specialist.Id));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var agents = await new AgentQueries(db).GetAllAsync(CancellationToken.None);
        var candidate = agents.Single(agent => agent.Id == specialist.Id);
        Assert.True(candidate.IsActive);
        Assert.Equal(3, candidate.MaxOpenTickets);
        Assert.Equal(0, candidate.OpenTicketCount);
        Assert.Equal(category.Id, Assert.Single(candidate.Specializations).Id);
        Assert.Empty(agents.Single(agent => agent.Id == generalist.Id).Specializations);
        var decision = new TicketAssignmentPolicy().SelectAgent(category, agents);
        Assert.Equal(specialist.Id, decision.AssignedAgentId);
    }

    [Fact]
    public async Task GetAllAsync_RecalculatesWorkloadAfterStatusChanges()
    {
        var ticket = TicketFor("TCK-0001", TicketStatus.Open, specialist.Id);
        db.Add(ticket);
        await db.SaveChangesAsync();
        var queries = new AgentQueries(db);
        Assert.Equal(1, (await queries.GetAllAsync(CancellationToken.None)).Single(agent => agent.Id == specialist.Id).OpenTicketCount);
        ticket.ChangeStatus(TicketStatus.Resolved, FixedClock.DefaultNow);
        await db.SaveChangesAsync();
        Assert.Equal(0, (await queries.GetAllAsync(CancellationToken.None)).Single(agent => agent.Id == specialist.Id).OpenTicketCount);
    }

    [Fact]
    public async Task GetAllAsync_CancellationIsNotConvertedIntoUnassignedDecision()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new AgentQueries(db).GetAllAsync(cancellation.Token));
    }

    private Ticket TicketFor(string reference, TicketStatus status, int? agentId)
    {
        var ticket = Ticket.Raise(reference, "Test ticket", "Test ticket description", customer.Id, category.Id,
            TicketPriority.Medium, FixedClock.DefaultNow);
        ticket.ChangeStatus(status, FixedClock.DefaultNow);
        ticket.AssignTo(agentId, FixedClock.DefaultNow);
        return ticket;
    }
}
