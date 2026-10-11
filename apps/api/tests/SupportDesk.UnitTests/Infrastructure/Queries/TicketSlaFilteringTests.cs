using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Moq;
using SupportDesk.Application.Abstractions;
using SupportDesk.Application.Contracts.Tickets;
using SupportDesk.Application.Features.Tickets.Triage;
using SupportDesk.Domain.Aggregates.Categories;
using SupportDesk.Domain.Aggregates.Customers;
using SupportDesk.Domain.Aggregates.Tickets;
using SupportDesk.Infrastructure.Data;
using SupportDesk.Infrastructure.Queries;
using SupportDesk.UnitTests.TestDoubles;

namespace SupportDesk.UnitTests.Infrastructure.Queries;

public sealed class TicketSlaFilteringTests : IAsyncLifetime
{
    private static readonly DateTime Start = FixedClock.DefaultNow;
    private readonly FixedClock clock = new(Start.AddHours(3));
    private readonly SqliteConnection sqlite = new("Data Source=:memory:");
    private readonly List<string> commands = [];
    private readonly SlaSettings settings = SlaTestSettings.Create();
    private SupportDbContext db = null!;
    private TicketQueries queries = null!;
    private string? database;
    private Customer standard = null!;
    private Customer premium = null!;
    private Category category = null!;

    public async Task InitializeAsync()
    {
        try
        {
            await InitializeDatabaseAsync();
        }
        catch
        {
            await DisposeAsync();
            throw;
        }
    }

    private async Task InitializeDatabaseAsync()
    {
        var options = new DbContextOptionsBuilder<SupportDbContext>()
            .LogTo(commands.Add, [RelationalEventId.CommandExecuted]);
        var server = Environment.GetEnvironmentVariable("SUPPORTDESK_TEST_SQLSERVER");
        if (string.IsNullOrWhiteSpace(server))
        {
            await sqlite.OpenAsync();
            options.UseSqlite(sqlite);
        }
        else
        {
            database = $"SupportDeskSlaFilterTest_{Guid.NewGuid():N}";
            options.UseSqlServer(new SqlConnectionStringBuilder(server) { InitialCatalog = database }.ConnectionString);
        }
        db = new SupportDbContext(options.Options);
        if (database is null) await db.Database.EnsureCreatedAsync();
        else await db.Database.MigrateAsync();
        standard = new Customer("Standard", "standard@example.test", null, CustomerTier.Standard, Start);
        premium = new Customer("Premium", "premium@example.test", null, CustomerTier.Premium, Start);
        category = new Category("General", false, false);
        db.AddRange(standard, premium, category);
        await db.SaveChangesAsync();
        await Add("no-due", Start, null);
        await Add("within", Start, Start.AddHours(8), priority: TicketPriority.Medium);
        await Add("threshold", Start, Start.AddHours(4));
        await Add("above", Start.AddMilliseconds(1), Start.AddHours(4).AddMilliseconds(1));
        await Add("below", Start.AddMilliseconds(-1), Start.AddHours(4).AddMilliseconds(-1));
        await Add("deadline", Start, clock.UtcNow);
        await Add("breached", Start, clock.UtcNow.AddMilliseconds(-1));
        await Add("resolved-before", Start, Start.AddHours(4), TicketStatus.Resolved, Start.AddHours(4).AddMilliseconds(-1));
        await Add("resolved-at", Start, Start.AddHours(4), TicketStatus.Resolved, Start.AddHours(4));
        await Add("resolved-after", Start, Start.AddHours(4), TicketStatus.Resolved, Start.AddHours(4).AddMilliseconds(1));
        await Add("closed-met", Start, Start.AddHours(4), TicketStatus.Closed, Start.AddHours(4));
        await Add("closed-late", Start, Start.AddHours(4), TicketStatus.Closed, Start.AddHours(4).AddMilliseconds(1));
        await Add("legacy-closed", Start, Start.AddHours(4), TicketStatus.Closed);
        await Add("legacy-resolved", Start, Start.AddHours(4), TicketStatus.Resolved);
        var premiumDue = new TicketTriagePolicy(settings).CalculateDeadline(TicketPriority.Critical, CustomerTier.Premium, Start).DueAtUtc;
        await Add("premium", Start, premiumDue, customer: premium);
        await Add("invalid-window", Start, Start);
        db.ChangeTracker.Clear();
        queries = new TicketQueries(db, clock, settings);
    }

    public async Task DisposeAsync()
    {
        if (db is not null)
        {
            if (database is not null)
            {
                if (!database.StartsWith("SupportDeskSlaFilterTest_", StringComparison.Ordinal))
                    throw new InvalidOperationException("Refusing to delete a non-test database.");
                await db.Database.EnsureDeletedAsync();
            }
            await db.DisposeAsync();
        }
        await sqlite.DisposeAsync();
    }

    [Theory]
    [InlineData(SlaStatus.NotApplicable)]
    [InlineData(SlaStatus.WithinSla)]
    [InlineData(SlaStatus.AtRisk)]
    [InlineData(SlaStatus.Breached)]
    [InlineData(SlaStatus.Met)]
    public async Task Filter_EachStatusMatchesEvaluatorAndFiltersCountInSql(SlaStatus status)
    {
        var all = await Query(new TicketQuery { PageSize = 100 });
        var expected = all.Items.Where(ticket => ticket.SlaStatus == status).Select(ticket => ticket.Id).ToArray();
        Assert.NotEmpty(expected);
        commands.Clear();
        var filtered = await Query(new TicketQuery { SlaStatus = status, PageSize = 100 });
        Assert.Equal(expected, filtered.Items.Select(ticket => ticket.Id));
        Assert.Equal(expected.Length, filtered.TotalCount);
        Assert.All(filtered.Items, ticket => Assert.Equal(status, ticket.SlaStatus));
        Assert.Equal(2, commands.Count);
        Assert.Contains("COUNT", commands[0], StringComparison.OrdinalIgnoreCase);
        Assert.All(commands, sql =>
        {
            Assert.Contains("WHERE", sql, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("CASE", sql, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("TicketEscalations", sql, StringComparison.Ordinal);
        });
        Assert.Contains(database is null ? "LIMIT" : "OFFSET", commands[1], StringComparison.OrdinalIgnoreCase);
        Assert.Empty(db.ChangeTracker.Entries());
    }

    [Theory]
    [InlineData("no-due", SlaStatus.NotApplicable)]
    [InlineData("within", SlaStatus.WithinSla)]
    [InlineData("threshold", SlaStatus.AtRisk)]
    [InlineData("above", SlaStatus.WithinSla)]
    [InlineData("below", SlaStatus.AtRisk)]
    [InlineData("deadline", SlaStatus.AtRisk)]
    [InlineData("breached", SlaStatus.Breached)]
    [InlineData("resolved-before", SlaStatus.Met)]
    [InlineData("resolved-at", SlaStatus.Met)]
    [InlineData("resolved-after", SlaStatus.Breached)]
    [InlineData("closed-met", SlaStatus.Met)]
    [InlineData("closed-late", SlaStatus.Breached)]
    [InlineData("legacy-closed", SlaStatus.NotApplicable)]
    [InlineData("legacy-resolved", SlaStatus.NotApplicable)]
    [InlineData("premium", SlaStatus.Breached)]
    [InlineData("invalid-window", SlaStatus.Breached)]
    public async Task Filter_BoundariesAndNullableCases(string reference, SlaStatus expected)
    {
        var result = await Query(new TicketQuery { Search = reference, SlaStatus = expected });
        var ticket = Assert.Single(result.Items, ticket => ticket.Reference == reference);
        Assert.Equal(expected, ticket.SlaStatus);
        Assert.Equal(expected, (await queries.GetDetailAsync(ticket.Id, CancellationToken.None))!.SlaStatus);
    }

    [Theory]
    [InlineData(-1, SlaStatus.WithinSla)]
    [InlineData(0, SlaStatus.AtRisk)]
    [InlineData(1, SlaStatus.AtRisk)]
    public async Task Filter_ClockAtThresholdRetainsTickPrecision(long ticks, SlaStatus expected)
    {
        clock.Advance(TimeSpan.FromTicks(ticks));
        var result = await Query(new TicketQuery { Search = "threshold", SlaStatus = expected });
        Assert.Equal(expected, Assert.Single(result.Items).SlaStatus);
    }

    [Theory]
    [InlineData(0, SlaStatus.AtRisk)]
    [InlineData(1, SlaStatus.Breached)]
    public async Task Filter_ClockAtDeadlineRetainsTickPrecision(long ticks, SlaStatus expected)
    {
        clock.Advance(TimeSpan.FromTicks(ticks));
        Assert.Equal(expected, Assert.Single((await Query(new TicketQuery { Search = "deadline", SlaStatus = expected })).Items).SlaStatus);
    }

    [Fact]
    public async Task Filter_CustomThresholdPremiumAndMinimum()
    {
        settings.AtRiskThresholdPercent = 50;
        settings.BaseWindowHours[TicketPriority.Critical] = 1;
        var start = clock.UtcNow;
        var due = new TicketTriagePolicy(settings).CalculateDeadline(TicketPriority.Critical, CustomerTier.Premium, start).DueAtUtc;
        Assert.Equal(start.AddHours(1), due);
        await Add("minimum", start, due, customer: premium);
        db.ChangeTracker.Clear();
        queries = new TicketQueries(db, clock, settings);
        clock.Advance(TimeSpan.FromMinutes(30));
        var result = await Query(new TicketQuery { Search = "minimum", SlaStatus = SlaStatus.AtRisk, CustomerId = premium.Id });
        Assert.Equal(SlaStatus.AtRisk, Assert.Single(result.Items).SlaStatus);
    }

    [Fact]
    public async Task Filter_CombinesWithStatusPriorityCustomerAndPagination()
    {
        var filter = new TicketQuery { SlaStatus = SlaStatus.AtRisk, Status = TicketStatus.New,
            Priority = TicketPriority.Critical, CustomerId = standard.Id, PageSize = 2, SortBy = "priority" };
        var all = await Query(filter with { PageSize = 100 });
        Assert.Equal(3, all.TotalCount);
        var first = await Query(filter);
        var second = await Query(filter with { Page = 2 });
        Assert.Equal(3, first.TotalCount);
        Assert.Equal(2, first.TotalPages);
        Assert.Equal(2, first.Items.Count);
        Assert.Single(second.Items);
        Assert.Equal(all.Items.Select(t => t.Id), first.Items.Concat(second.Items).Select(t => t.Id));
        Assert.Equal(all.Items.Select(t => t.Id).OrderDescending(), all.Items.Select(t => t.Id));
        Assert.Empty((await Query(filter with { Page = 3 })).Items);
        var empty = await Query(filter with { Status = TicketStatus.Resolved });
        Assert.Empty(empty.Items);
        Assert.Equal(0, empty.TotalCount);
        Assert.Equal(0, empty.TotalPages);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Filter_LatestEscalationStartsWindowAndAssignmentDoesNotRestartIt(bool equalTimestamps)
    {
        var ticket = await Add("escalated", Start.AddDays(-3), clock.UtcNow.AddHours(4));
        var latest = clock.UtcNow;
        var older = equalTimestamps ? latest : latest.AddHours(-4);
        db.TicketEscalations.Add(new TicketEscalation(ticket.Id, TicketPriority.Low, TicketPriority.Medium,
            null, null, null, ticket.DueAtUtc, "First escalation", "Actor", older));
        await db.SaveChangesAsync();
        db.TicketEscalations.Add(new TicketEscalation(ticket.Id, TicketPriority.Medium, TicketPriority.High,
            null, null, ticket.DueAtUtc, ticket.DueAtUtc, "Latest escalation", "Actor", latest));
        await db.SaveChangesAsync();
        clock.Advance(TimeSpan.FromHours(2));
        ticket.AssignTo(null, clock.UtcNow);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        Assert.Single((await Query(new TicketQuery { Search = "escalated", SlaStatus = SlaStatus.WithinSla })).Items);
        clock.Advance(TimeSpan.FromHours(1));
        Assert.Single((await Query(new TicketQuery { Search = "escalated", SlaStatus = SlaStatus.AtRisk })).Items);
        Assert.Contains(commands, sql => sql.Contains("ORDER BY", StringComparison.OrdinalIgnoreCase)
            && sql.Contains("EscalatedAtUtc", StringComparison.Ordinal) && sql.Contains("DESC", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Filter_OmittedPreservesResultsAndNormalizingPreservesFilter()
    {
        var result = await Query(new TicketQuery { PageSize = 100 });
        Assert.Equal(16, result.TotalCount);
        Assert.Equal(16, result.Items.Count);
        Assert.Equal(SlaStatus.AtRisk, new TicketQuery { SlaStatus = SlaStatus.AtRisk, Page = -1 }.Normalized().SlaStatus);
    }

    [Fact]
    public async Task Filter_CountAndPageUseExactlyOneClockReading()
    {
        var reads = 0;
        var clockMock = new Mock<IClock>();
        clockMock.SetupGet(value => value.UtcNow).Returns(() => ++reads == 1 ? clock.UtcNow : clock.UtcNow.AddDays(1));
        var query = new TicketQueries(db, clockMock.Object, settings);
        var result = await query.GetPagedAsync(new TicketQuery { SlaStatus = SlaStatus.AtRisk }, CancellationToken.None);
        Assert.Equal(3, result.TotalCount);
        Assert.Equal(3, result.Items.Count);
        Assert.All(result.Items, ticket => Assert.Equal(SlaStatus.AtRisk, ticket.SlaStatus));
        clockMock.VerifyGet(value => value.UtcNow, Times.Once);
    }

    [Fact]
    public async Task Filter_LongLegacyWindowDoesNotOverflowSqlDateDifferences()
    {
        await Add("long-window", new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2100, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        db.ChangeTracker.Clear();
        Assert.Single((await Query(new TicketQuery { Search = "long-window", SlaStatus = SlaStatus.WithinSla })).Items);
    }

    private Task<SupportDesk.Application.Contracts.Common.PagedResult<TicketListItemDto>> Query(TicketQuery query) =>
        queries.GetPagedAsync(query.Normalized(), CancellationToken.None);

    private async Task<Ticket> Add(string reference, DateTime start, DateTime? due,
        TicketStatus status = TicketStatus.New, DateTime? resolution = null,
        TicketPriority priority = TicketPriority.Critical, Customer? customer = null)
    {
        var ticket = Ticket.Raise(reference, reference, "Support request description", (customer ?? standard).Id, category.Id, priority, start);
        if (due > start) ticket.SetDueDate(due.Value, start);
        else if (due.HasValue) db.Entry(ticket).Property(t => t.DueAtUtc).CurrentValue = due;
        if (status != TicketStatus.New)
        {
            ticket.ChangeStatus(status, resolution ?? start);
            if (resolution is null) db.Entry(ticket).Property(t => t.ResolvedAtUtc).CurrentValue = null;
        }
        db.Add(ticket);
        await db.SaveChangesAsync();
        return ticket;
    }
}
