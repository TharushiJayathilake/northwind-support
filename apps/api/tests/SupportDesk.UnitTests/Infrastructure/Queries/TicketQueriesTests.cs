using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Moq;
using SupportDesk.Application.Abstractions;
using SupportDesk.Application.Contracts.Common;
using SupportDesk.Application.Contracts.Tickets;
using SupportDesk.Domain.Aggregates.Agents;
using SupportDesk.Domain.Aggregates.Categories;
using SupportDesk.Domain.Aggregates.Customers;
using SupportDesk.Domain.Aggregates.Tickets;
using SupportDesk.Infrastructure.Data;
using SupportDesk.Infrastructure.Queries;
using SupportDesk.UnitTests.TestDoubles;

namespace SupportDesk.UnitTests.Infrastructure.Queries;

public sealed class TicketQueriesTests : IAsyncLifetime
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly FixedClock _clock = new();
    private readonly Customer _acme = new("Acme Industries", "acme@example.test", null,
        CustomerTier.Standard, FixedClock.DefaultNow);
    private readonly Customer _beta = new("Beta Services", "beta@example.test", null,
        CustomerTier.Premium, FixedClock.DefaultNow);
    private readonly Category _billing = new("Billing", false, false);
    private readonly Category _technical = new("Technical", false, false);
    private readonly Agent _alex = new("Alex", "alex@example.test", 10, FixedClock.DefaultNow);
    private readonly Agent _ben = new("Ben", "ben@example.test", 10, FixedClock.DefaultNow);
    private SupportDbContext _db = null!;
    private TicketQueries _queries = null!;

    public async Task InitializeAsync()
    {
        // Keep the connection open so this test's relational database survives between queries.
        await _connection.OpenAsync();
        var options = new DbContextOptionsBuilder<SupportDbContext>()
            .UseSqlite(_connection)
            .Options;
        _db = new SupportDbContext(options);
        await _db.Database.EnsureCreatedAsync();
        _db.AddRange(_acme, _beta, _billing, _technical, _alex, _ben);
        await _db.SaveChangesAsync();

        _db.AddRange(
            CreateTicket(1, "Invoice mismatch", _acme, _billing, TicketPriority.High, TicketStatus.Open, _alex),
            CreateTicket(2, "Printer offline", _acme, _technical, TicketPriority.High, TicketStatus.InProgress, _ben),
            CreateTicket(3, "Invoice export failed", _beta, _billing, TicketPriority.High, TicketStatus.Open),
            CreateTicket(4, "Password reset", _beta, _technical, TicketPriority.Low, TicketStatus.Resolved, _alex),
            CreateTicket(5, "Network outage", _beta, _technical, TicketPriority.Critical, TicketStatus.Open, _ben),
            CreateTicket(6, "Account locked", _acme, _technical, TicketPriority.Medium, TicketStatus.New),
            CreateTicket(7, "Invoice duplicate", _acme, _billing, TicketPriority.High, TicketStatus.Open, _alex),
            CreateTicket(8, "General question", _beta, _billing, TicketPriority.Low, TicketStatus.Closed));
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
        _queries = new TicketQueries(_db, _clock, SlaTestSettings.Create());
    }

    public async Task DisposeAsync()
    {
        if (_db is not null)
        {
            await _db.DisposeAsync();
        }

        await _connection.DisposeAsync();
    }

    [Theory]
    [InlineData(TicketPriority.Low, "8,4")]
    [InlineData(TicketPriority.Medium, "6")]
    [InlineData(TicketPriority.High, "7,3,2,1")]
    [InlineData(TicketPriority.Critical, "5")]
    public async Task GetPagedAsync_FiltersByExactPriority(TicketPriority priority, string expected)
    {
        var result = await SearchAsync(new TicketQuery { Priority = priority });

        AssertTickets(result, expected);
        Assert.All(result.Items, ticket => Assert.Equal(priority, ticket.Priority));
    }

    [Theory]
    [InlineData(TicketStatus.New, "6")]
    [InlineData(TicketStatus.Open, "7,5,3,1")]
    [InlineData(TicketStatus.InProgress, "2")]
    [InlineData(TicketStatus.Resolved, "4")]
    [InlineData(TicketStatus.Closed, "8")]
    public async Task GetPagedAsync_FiltersByExactStatus(TicketStatus status, string expected)
    {
        var result = await SearchAsync(new TicketQuery { Status = status });

        AssertTickets(result, expected);
        Assert.All(result.Items, ticket => Assert.Equal(status, ticket.Status));
    }

    [Theory]
    [InlineData(true, "8,7,3,1")]
    [InlineData(false, "6,5,4,2")]
    public async Task GetPagedAsync_FiltersByCategory(bool billing, string expected)
    {
        var category = billing ? _billing : _technical;
        var result = await SearchAsync(new TicketQuery { CategoryId = category.Id });

        AssertTickets(result, expected);
        Assert.All(result.Items, ticket => Assert.Equal(category.Id, ticket.Category.Id));
    }

    [Theory]
    [InlineData(true, "7,6,2,1")]
    [InlineData(false, "8,5,4,3")]
    public async Task GetPagedAsync_FiltersByCustomer(bool acme, string expected)
    {
        var customer = acme ? _acme : _beta;
        var result = await SearchAsync(new TicketQuery { CustomerId = customer.Id });

        AssertTickets(result, expected);
        Assert.All(result.Items, ticket => Assert.Equal(customer.Id, ticket.Customer.Id));
    }

    [Theory]
    [InlineData(true, "7,4,1")]
    [InlineData(false, "5,2")]
    public async Task GetPagedAsync_FiltersByAssignedAgent(bool alex, string expected)
    {
        var agent = alex ? _alex : _ben;
        var result = await SearchAsync(new TicketQuery { AssignedAgentId = agent.Id });

        AssertTickets(result, expected);
        Assert.All(result.Items, ticket => Assert.Equal(agent.Id, ticket.AssignedAgent?.Id));
    }

    [Fact]
    public async Task GetPagedAsync_UnassignedOnly_ReturnsOnlyNullAssignments()
    {
        var result = await SearchAsync(new TicketQuery { UnassignedOnly = true });

        AssertTickets(result, "8,6,3");
        Assert.All(result.Items, ticket => Assert.Null(ticket.AssignedAgent));
    }

    [Fact]
    public async Task GetPagedAsync_WithoutFilters_PreservesUnassignedTicketsAndDoesNotTrackEntities()
    {
        var result = await SearchAsync(new TicketQuery());

        AssertTickets(result, "8,7,6,5,4,3,2,1");
        Assert.Equal(3, result.Items.Count(ticket => ticket.AssignedAgent is null));
        Assert.Empty(_db.ChangeTracker.Entries());
    }

    [Fact]
    public async Task GetPagedAsync_UnassignedOnlyFalse_DoesNotRestrictAssignments()
    {
        var result = await SearchAsync(new TicketQuery { UnassignedOnly = false });

        AssertTickets(result, "8,7,6,5,4,3,2,1");
    }

    [Theory]
    [InlineData("Invoice", "7,3,1")]
    [InlineData("TCK-0002", "2")]
    [InlineData("Acme", "7,6,2,1")]
    [InlineData("  Invoice  ", "7,3,1")]
    [InlineData("\t TCK-0002 \r\n", "2")]
    public async Task GetPagedAsync_SearchesTitleReferenceOrCustomerAndTrimsInput(string search, string expected)
    {
        var result = await SearchAsync(new TicketQuery { Search = search });

        AssertTickets(result, expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\r\n")]
    public async Task GetPagedAsync_IgnoresBlankSearch(string? search)
    {
        var result = await SearchAsync(new TicketQuery { Search = search });

        AssertTickets(result, "8,7,6,5,4,3,2,1");
    }

    [Fact]
    public async Task GetPagedAsync_CombinesAllSuppliedFiltersWithAnd()
    {
        var result = await SearchAsync(new TicketQuery
        {
            Priority = TicketPriority.High,
            Status = TicketStatus.Open,
            CategoryId = _billing.Id,
            CustomerId = _acme.Id,
            AssignedAgentId = _alex.Id,
            Search = "Invoice"
        });

        AssertTickets(result, "7,1");
    }

    [Fact]
    public async Task GetPagedAsync_CombinesSearchWithUnassignedOnly()
    {
        var result = await SearchAsync(new TicketQuery { Search = "Invoice", UnassignedOnly = true });

        AssertTickets(result, "3");
    }

    [Fact]
    public async Task GetPagedAsync_ContradictoryAssignmentFilters_ReturnNoMatches()
    {
        var result = await SearchAsync(new TicketQuery { AssignedAgentId = _alex.Id, UnassignedOnly = true });

        AssertTickets(result, "");
    }

    [Fact]
    public async Task GetPagedAsync_ContradictoryStatusAndPriority_ReturnNoMatches()
    {
        var result = await SearchAsync(new TicketQuery { Status = TicketStatus.Resolved, Priority = TicketPriority.Critical });

        AssertTickets(result, "");
    }

    [Theory]
    [InlineData(1, 2, 2, "7,3")]
    [InlineData(2, 2, 2, "2,1")]
    [InlineData(3, 2, 2, "")]
    [InlineData(2, 3, 2, "1")]
    [InlineData(1, 10, 1, "7,3,2,1")]
    public async Task GetPagedAsync_CountsFilteredTicketsBeforePaging(
        int page, int pageSize, int totalPages, string expected)
    {
        var result = await SearchAsync(new TicketQuery
        {
            Priority = TicketPriority.High,
            Page = page,
            PageSize = pageSize
        });

        AssertReferences(result, expected);
        Assert.Equal(4, result.TotalCount);
        Assert.Equal(totalPages, result.TotalPages);
        Assert.Equal(page, result.Page);
        Assert.Equal(pageSize, result.PageSize);
    }

    [Fact]
    public async Task GetPagedAsync_NoSearchMatches_ReturnsZeroCounts()
    {
        var result = await SearchAsync(new TicketQuery { Search = "No matching ticket", PageSize = 2 });

        AssertTickets(result, "");
    }

    [Theory]
    [InlineData("createdAtUtc", "asc", "8,7,6,5,4,3,2,1")]
    [InlineData("createdAtUtc", "desc", "8,7,6,5,4,3,2,1")]
    [InlineData("updatedAtUtc", "asc", "8,7,6,5,4,3,2,1")]
    [InlineData("dueAtUtc", "asc", "8,7,6,5,4,3,2,1")]
    [InlineData("priority", "asc", "8,4,6,7,3,2,1,5")]
    [InlineData("priority", "desc", "5,7,3,2,1,6,8,4")]
    [InlineData("status", "asc", "6,7,5,3,1,2,4,8")]
    [InlineData("status", "desc", "8,4,2,7,5,3,1,6")]
    [InlineData("unknown", "asc", "8,7,6,5,4,3,2,1")]
    public async Task GetPagedAsync_SortsInSqlWithDescendingIdAsStableTieBreaker(
        string sortBy, string direction, string expected)
    {
        var result = await SearchAsync(new TicketQuery { SortBy = sortBy, SortDirection = direction });

        AssertTickets(result, expected);
    }

    [Fact]
    public async Task GetPagedAsync_FilteredPagesHaveStableOrderingAcrossRepeatedRequests()
    {
        var firstQuery = new TicketQuery { Priority = TicketPriority.High, SortBy = "priority", PageSize = 2 };
        var first = await SearchAsync(firstQuery);
        var second = await SearchAsync(firstQuery with { Page = 2 });
        var repeated = await SearchAsync(firstQuery);

        AssertReferences(first, "7,3");
        AssertReferences(second, "2,1");
        AssertReferences(repeated, "7,3");
        Assert.Empty(first.Items.Select(ticket => ticket.Id).Intersect(second.Items.Select(ticket => ticket.Id)));
    }

    private Task<PagedResult<TicketListItemDto>> SearchAsync(TicketQuery query) =>
        _queries.GetPagedAsync(query.Normalized(), CancellationToken.None);

    [Theory]
    [InlineData(CustomerTier.Standard, 4)]
    [InlineData(CustomerTier.Premium, 2)]
    public async Task SlaStatus_AllReadPathsUseStoredWindowAndMovingClock(CustomerTier tier, double hours)
    {
        var customer = tier == CustomerTier.Premium ? _beta : _acme;
        var settings = SlaTestSettings.Create();
        var start = _clock.UtcNow;
        var due = new SupportDesk.Application.Features.Tickets.Triage.TicketTriagePolicy(settings)
            .CalculateDeadline(TicketPriority.Critical, tier, start).DueAtUtc;
        var ticket = CreateTicket(99, "SLA test", customer, _technical, TicketPriority.Critical, TicketStatus.New);
        ticket.SetDueDate(due, start);
        _db.Add(ticket);
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
        await AssertSlaReadPaths(ticket.Id, customer.Id, SlaStatus.WithinSla);
        _clock.Advance(TimeSpan.FromHours(hours * .75));
        await AssertSlaReadPaths(ticket.Id, customer.Id, SlaStatus.AtRisk);
        _clock.Advance(TimeSpan.FromHours(hours * .25));
        await AssertSlaReadPaths(ticket.Id, customer.Id, SlaStatus.AtRisk);
        _clock.Advance(TimeSpan.FromTicks(1));
        await AssertSlaReadPaths(ticket.Id, customer.Id, SlaStatus.Breached);
        Assert.Equal(due, (await _db.Set<Ticket>().AsNoTracking().SingleAsync(t => t.Id == ticket.Id)).DueAtUtc);
    }

    [Fact]
    public async Task SlaStatus_EscalationStartsNewWindowRatherThanUsingCreationOrUpdatedTime()
    {
        var ticket = CreateTicket(99, "Escalated SLA", _acme, _technical, TicketPriority.Low, TicketStatus.New);
        ticket.SetDueDate(_clock.UtcNow.AddHours(72), _clock.UtcNow);
        _db.Add(ticket);
        await _db.SaveChangesAsync();
        _clock.Advance(TimeSpan.FromDays(2));
        var start = _clock.UtcNow;
        var due = start.AddHours(24);
        ticket.Escalate(due, null, start);
        _db.TicketEscalations.Add(new TicketEscalation(ticket.Id, TicketPriority.Low, TicketPriority.Medium,
            null, null, ticket.CreatedAtUtc.AddHours(72), due, "Impact increased", "Operator", start));
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
        _clock.Advance(TimeSpan.FromHours(12));
        await AssertSlaReadPaths(ticket.Id, _acme.Id, SlaStatus.WithinSla);
        _clock.Advance(TimeSpan.FromHours(6));
        ticket = await _db.Set<Ticket>().SingleAsync(t => t.Id == ticket.Id);
        ticket.AssignTo(_alex.Id, _clock.UtcNow);
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
        await AssertSlaReadPaths(ticket.Id, _acme.Id, SlaStatus.AtRisk);
    }

    [Fact]
    public async Task SlaStatus_ConfiguredThresholdAndPremiumMinimumAreRespected()
    {
        var settings = SlaTestSettings.Create();
        settings.BaseWindowHours[TicketPriority.Critical] = 1;
        settings.AtRiskThresholdPercent = 50;
        _queries = new TicketQueries(_db, _clock, settings);
        var ticket = CreateTicket(99, "Minimum SLA", _beta, _technical, TicketPriority.Critical, TicketStatus.New);
        var decision = new SupportDesk.Application.Features.Tickets.Triage.TicketTriagePolicy(settings)
            .CalculateDeadline(TicketPriority.Critical, CustomerTier.Premium, _clock.UtcNow);
        Assert.Equal(TimeSpan.FromHours(1), decision.Window);
        ticket.SetDueDate(decision.DueAtUtc, _clock.UtcNow);
        _db.Add(ticket);
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
        _clock.Advance(TimeSpan.FromMinutes(30).Subtract(TimeSpan.FromTicks(1)));
        await AssertSlaReadPaths(ticket.Id, _beta.Id, SlaStatus.WithinSla);
        _clock.Advance(TimeSpan.FromTicks(1));
        await AssertSlaReadPaths(ticket.Id, _beta.Id, SlaStatus.AtRisk);
    }

    private async Task AssertSlaReadPaths(int ticketId, int customerId, SlaStatus expected)
    {
        Assert.Equal(expected, (await _queries.GetDetailAsync(ticketId, CancellationToken.None))!.SlaStatus);
        var page = await SearchAsync(new TicketQuery { Search = "TCK-0099" });
        Assert.Equal(expected, Assert.Single(page.Items).SlaStatus);
        Assert.Equal(expected, (await _queries.GetForCustomerAsync(customerId, CancellationToken.None)).Single(t => t.Id == ticketId).SlaStatus);
        Assert.Empty(_db.ChangeTracker.Entries());
    }

    [Theory]
    [InlineData("detail")]
    [InlineData("paged")]
    [InlineData("customer")]
    public async Task SlaStatus_EachReadCapturesClockExactlyOnce(string readPath)
    {
        var clock = new Mock<IClock>();
        clock.SetupGet(value => value.UtcNow).Returns(_clock.UtcNow);
        var queries = new TicketQueries(_db, clock.Object, SlaTestSettings.Create());
        if (readPath == "detail") await queries.GetDetailAsync((await _db.Set<Ticket>().FirstAsync()).Id, CancellationToken.None);
        else if (readPath == "paged") await queries.GetPagedAsync(new TicketQuery().Normalized(), CancellationToken.None);
        else await queries.GetForCustomerAsync(_acme.Id, CancellationToken.None);
        clock.VerifyGet(value => value.UtcNow, Times.Once);
    }

    private Ticket CreateTicket(int number, string title, Customer customer, Category category,
        TicketPriority priority, TicketStatus status, Agent? agent = null)
    {
        var ticket = Ticket.Raise($"TCK-{number:D4}", title, "A realistic support ticket description.",
            customer.Id, category.Id, priority, _clock.UtcNow);
        ticket.ChangeStatus(status, _clock.UtcNow);
        ticket.AssignTo(agent?.Id, _clock.UtcNow);
        return ticket;
    }

    private static void AssertTickets(PagedResult<TicketListItemDto> result, string expected)
    {
        AssertReferences(result, expected);
        Assert.Equal(result.Items.Count, result.TotalCount);
        Assert.Equal(result.Items.Count == 0 ? 0 : 1, result.TotalPages);
    }

    private static void AssertReferences(PagedResult<TicketListItemDto> result, string expected)
    {
        var references = expected.Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(number => $"TCK-{int.Parse(number):D4}");
        Assert.Equal(references, result.Items.Select(ticket => ticket.Reference));
    }
}
