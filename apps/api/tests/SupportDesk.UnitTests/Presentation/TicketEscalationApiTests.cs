using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SupportDesk.Application;
using SupportDesk.Application.Abstractions;
using SupportDesk.Application.Contracts.Tickets;
using SupportDesk.Application.Features.Tickets.Triage;
using SupportDesk.Domain.Aggregates.Agents;
using SupportDesk.Domain.Aggregates.Categories;
using SupportDesk.Domain.Aggregates.Customers;
using SupportDesk.Domain.Aggregates.Tickets;
using SupportDesk.Domain.Repositories;
using SupportDesk.Infrastructure.Data;
using SupportDesk.Infrastructure.Queries;
using SupportDesk.Infrastructure.Repositories;
using SupportDesk.Presentation.Controllers;
using SupportDesk.Presentation.Extensions;
using SupportDesk.Presentation.Filters;
using SupportDesk.Presentation.Middleware;
using SupportDesk.UnitTests.TestDoubles;

namespace SupportDesk.UnitTests.Presentation;

public sealed class TicketEscalationApiTests : IAsyncLifetime
{
    private readonly SqliteConnection sqlite = new("Data Source=:memory:");
    private readonly FixedClock clock = new();
    private readonly Customer standard = new("Standard", "standard@example.test", null, CustomerTier.Standard, FixedClock.DefaultNow);
    private readonly Customer premium = new("Premium", "premium@example.test", null, CustomerTier.Premium, FixedClock.DefaultNow);
    private readonly Category general = new("General", false, false);
    private readonly Category specialist = new("Special category", true, false);
    private readonly Agent current = new("Current", "current@example.test", 1, FixedClock.DefaultNow);
    private readonly Agent replacement = new("Replacement", "replacement@example.test", 5, FixedClock.DefaultNow);
    private readonly JsonSerializerOptions json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };
    private WebApplication app = null!;
    private HttpClient client = null!;
    private string? sqlConnection;

    public async Task InitializeAsync()
    {
        var server = Environment.GetEnvironmentVariable("SUPPORTDESK_TEST_SQLSERVER");
        if (string.IsNullOrWhiteSpace(server)) await sqlite.OpenAsync();
        else sqlConnection = new SqlConnectionStringBuilder(server)
        {
            InitialCatalog = $"SupportDeskEscalationApiTest_{Guid.NewGuid():N}"
        }.ConnectionString;
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Production" });
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Configuration.AddJsonFile(Path.Combine(AppContext.BaseDirectory, "sla-appsettings.json"));
        builder.Services.AddSlaSettings(builder.Configuration).AddApplication();
        builder.Services.AddSingleton<IClock>(clock);
        builder.Services.AddDbContext<SupportDbContext>(options =>
        {
            if (sqlConnection is null) options.UseSqlite(sqlite);
            else options.UseSqlServer(sqlConnection, sql => sql.EnableRetryOnFailure());
        });
        builder.Services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<SupportDbContext>());
        builder.Services.AddScoped<ITicketRepository, TicketRepository>();
        builder.Services.AddScoped<ICustomerRepository, CustomerRepository>();
        builder.Services.AddScoped<ICategoryRepository, CategoryRepository>();
        builder.Services.AddScoped<IAgentQueries, AgentQueries>();
        builder.Services.AddScoped<ITicketQueries, TicketQueries>();
        builder.Services.AddControllers(options => options.Filters.Add<ValidationFilter>())
            .AddApplicationPart(typeof(TicketsController).Assembly)
            .AddJsonOptions(options => options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
        app = builder.Build();
        app.UseMiddleware<ExceptionHandlingMiddleware>();
        app.MapControllers();
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SupportDbContext>();
            if (sqlConnection is null) await db.Database.EnsureCreatedAsync();
            else await db.Database.MigrateAsync();
            db.AddRange(standard, premium, general, specialist, current, replacement);
            await db.SaveChangesAsync();
            current.AddSpecialization(specialist.Id);
            replacement.AddSpecialization(specialist.Id);
            await db.SaveChangesAsync();
        }
        await app.StartAsync();
        client = new HttpClient
        {
            BaseAddress = new Uri(app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single())
        };
    }

    public async Task DisposeAsync()
    {
        client?.Dispose();
        if (app is not null)
        {
            await app.StopAsync();
            if (sqlConnection is not null)
            {
                if (!new SqlConnectionStringBuilder(sqlConnection).InitialCatalog.StartsWith("SupportDeskEscalationApiTest_", StringComparison.Ordinal))
                    throw new InvalidOperationException("Refusing to delete a non-test database.");
                using var scope = app.Services.CreateScope();
                await scope.ServiceProvider.GetRequiredService<SupportDbContext>().Database.EnsureDeletedAsync();
            }
            await app.DisposeAsync();
        }
        await sqlite.DisposeAsync();
    }

    [Theory]
    [InlineData(TicketPriority.Low, false, TicketPriority.Medium, 24)]
    [InlineData(TicketPriority.Medium, false, TicketPriority.High, 8)]
    [InlineData(TicketPriority.High, false, TicketPriority.Critical, 4)]
    [InlineData(TicketPriority.Low, true, TicketPriority.Medium, 12)]
    [InlineData(TicketPriority.Medium, true, TicketPriority.High, 4)]
    [InlineData(TicketPriority.High, true, TicketPriority.Critical, 2)]
    public async Task Post_EscalatesFromNowRetainsAtCapacityOwnerAndRecordsBeforeAfter(
        TicketPriority from, bool isPremium, TicketPriority to, double hours)
    {
        var ticket = await SeedTicketAsync(from, isPremium);
        var oldDue = ticket.DueAtUtc;
        clock.Advance(TimeSpan.FromMinutes(37));
        using var response = await PostAsync(ticket.Id);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = (await response.Content.ReadFromJsonAsync<EscalateTicketResponse>(json))!;
        Assert.Equal(to, result.Ticket.Priority);
        Assert.Equal(clock.UtcNow.AddHours(hours), result.Ticket.DueAtUtc);
        Assert.Equal(current.Id, result.Ticket.AssignedAgent!.Id);
        Assert.Equal(ticket.CreatedAtUtc, result.Ticket.CreatedAtUtc);
        Assert.Equal(clock.UtcNow, result.Ticket.UpdatedAtUtc);
        Assert.Equal(from, result.Escalation.FromPriority);
        Assert.Equal(to, result.Escalation.ToPriority);
        Assert.Equal(current.Id, result.Escalation.FromAgentId);
        Assert.Equal(current.Id, result.Escalation.ToAgentId);
        Assert.Equal(oldDue, result.Escalation.FromDueAtUtc);
        Assert.Equal(result.Ticket.DueAtUtc, result.Escalation.ToDueAtUtc);
        Assert.Equal(clock.UtcNow, result.Escalation.EscalatedAtUtc);
        Assert.Equal("Impact increased", result.Escalation.Reason);
        Assert.Equal("Team lead", result.Escalation.EscalatedBy);
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SupportDbContext>();
        var saved = await db.Set<Ticket>().AsNoTracking().SingleAsync();
        Assert.Equal(to, saved.Priority);
        Assert.Equal(result.Ticket.DueAtUtc, saved.DueAtUtc);
        Assert.Equal(current.Id, saved.AssignedAgentId);
        var audit = await db.TicketEscalations.AsNoTracking().SingleAsync();
        Assert.Equal(result.Escalation.Id, audit.Id);
        Assert.Equal(saved.Id, audit.TicketId);
        Assert.Equal(oldDue, audit.FromDueAtUtc);
        Assert.Equal(saved.DueAtUtc, audit.ToDueAtUtc);
    }

    [Theory]
    [InlineData(TicketPriority.Critical, TicketStatus.New, "Critical")]
    [InlineData(TicketPriority.Low, TicketStatus.Resolved, "Resolved")]
    [InlineData(TicketPriority.Low, TicketStatus.Closed, "Closed")]
    public async Task Post_ForbiddenEscalationsReturn409WithoutChanges(TicketPriority priority, TicketStatus status, string message)
    {
        var ticket = await SeedTicketAsync(priority, status: status);
        using var response = await PostAsync(ticket.Id);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var problem = (await response.Content.ReadFromJsonAsync<ProblemDetails>(json))!;
        Assert.Equal(409, problem.Status);
        Assert.Equal("Conflict", problem.Title);
        Assert.Contains(message, problem.Detail);
        await AssertUnchangedAsync(ticket);
    }

    [Theory]
    [InlineData("nullReason")]
    [InlineData("emptyReason")]
    [InlineData("blankReason")]
    [InlineData("shortReason")]
    [InlineData("paddedShortReason")]
    [InlineData("longReason")]
    [InlineData("nullActor")]
    [InlineData("emptyActor")]
    [InlineData("blankActor")]
    [InlineData("longActor")]
    public async Task Post_InvalidInputReturns400WithoutChanges(string invalid)
    {
        var ticket = await SeedTicketAsync();
        var reason = invalid switch
        {
            "nullReason" => null!, "emptyReason" => "", "blankReason" => "     ",
            "shortReason" => "four", "paddedShortReason" => "  abc  ", "longReason" => new string('r', 501),
            _ => "Valid reason"
        };
        var actor = invalid switch
        {
            "nullActor" => null!, "emptyActor" => "", "blankActor" => "   ", "longActor" => new string('a', 101),
            _ => "Team lead"
        };
        using var response = await client.PostAsJsonAsync($"/api/tickets/{ticket.Id}/escalate", new EscalateTicketRequest(reason, actor), json);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = (await response.Content.ReadFromJsonAsync<ValidationProblemDetails>(json))!;
        Assert.Equal(400, problem.Status);
        Assert.NotEmpty(problem.Errors);
        await AssertUnchangedAsync(ticket);
    }

    [Theory]
    [InlineData(5, 1)]
    [InlineData(500, 100)]
    public async Task Post_ValidLengthBoundariesArePersisted(int reasonLength, int actorLength)
    {
        var ticket = await SeedTicketAsync();
        using var response = await client.PostAsJsonAsync($"/api/tickets/{ticket.Id}/escalate",
            new EscalateTicketRequest(new string('r', reasonLength), new string('a', actorLength)), json);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = (await response.Content.ReadFromJsonAsync<EscalateTicketResponse>(json))!;
        Assert.Equal(reasonLength, result.Escalation.Reason.Length);
        Assert.Equal(actorLength, result.Escalation.EscalatedBy.Length);
    }

    [Theory]
    [InlineData(5, 1.25)]
    [InlineData(2, 1)]
    public async Task Post_CustomPremiumSettingsApplyFractionalHoursAndFloor(double baseHours, double expectedHours)
    {
        var settings = app.Services.GetRequiredService<SlaSettings>();
        settings.BaseWindowHours[TicketPriority.High] = baseHours;
        settings.PremiumCustomerMultiplier = 0.25;
        var ticket = await SeedTicketAsync(TicketPriority.Medium, isPremium: true);
        using var response = await PostAsync(ticket.Id);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = (await response.Content.ReadFromJsonAsync<EscalateTicketResponse>(json))!;
        Assert.Equal(clock.UtcNow.AddHours(expectedHours), result.Ticket.DueAtUtc);
    }

    [Theory]
    [InlineData("inactive")]
    [InlineData("overCapacity")]
    [InlineData("missingSpecialist")]
    public async Task Post_IneligibleOwnerIsReassigned(string scenario)
    {
        var ticket = await SeedTicketAsync(needsSpecialist: scenario == "missingSpecialist");
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SupportDbContext>();
            if (scenario == "inactive") (await db.Set<Agent>().SingleAsync(agent => agent.Id == current.Id)).Deactivate();
            else if (scenario == "missingSpecialist")
                db.Remove(await db.Set<AgentSpecialization>().SingleAsync(s => s.AgentId == current.Id));
            else
            {
                var extra = Ticket.Raise("TCK-0002", "Extra ticket", "Existing extra open workload", standard.Id, general.Id, TicketPriority.Low, clock.UtcNow);
                extra.AssignTo(current.Id, clock.UtcNow);
                db.Add(extra);
            }
            await db.SaveChangesAsync();
        }
        using var response = await PostAsync(ticket.Id);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = (await response.Content.ReadFromJsonAsync<EscalateTicketResponse>(json))!;
        Assert.Equal(replacement.Id, result.Ticket.AssignedAgent!.Id);
        Assert.Equal(current.Id, result.Escalation.FromAgentId);
        Assert.Equal(replacement.Id, result.Escalation.ToAgentId);
    }

    [Fact]
    public async Task Post_NoSuitableReplacementSucceedsUnassigned()
    {
        var ticket = await SeedTicketAsync(needsSpecialist: true);
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SupportDbContext>();
            db.RemoveRange(await db.Set<AgentSpecialization>().ToListAsync());
            await db.SaveChangesAsync();
        }
        using var response = await PostAsync(ticket.Id);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = (await response.Content.ReadFromJsonAsync<EscalateTicketResponse>(json))!;
        Assert.Null(result.Ticket.AssignedAgent);
        Assert.Null(result.Escalation.ToAgentId);
        using var readScope = app.Services.CreateScope();
        var dbRead = readScope.ServiceProvider.GetRequiredService<SupportDbContext>();
        Assert.Null((await dbRead.Set<Ticket>().SingleAsync()).AssignedAgentId);
        Assert.Null((await dbRead.TicketEscalations.SingleAsync()).ToAgentId);
    }

    [Fact]
    public async Task Post_UnknownTicketReturns404()
    {
        var existing = await SeedTicketAsync();
        using var response = await PostAsync(999);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var problem = (await response.Content.ReadFromJsonAsync<ProblemDetails>(json))!;
        Assert.Equal(404, problem.Status);
        Assert.Contains("Ticket", problem.Detail);
        await AssertUnchangedAsync(existing);
    }

    [Theory]
    [InlineData("Tickets")]
    [InlineData("TicketEscalations")]
    public async Task Post_DatabaseFailureRollsBackBothTicketAndAudit(string table)
    {
        var ticket = await SeedTicketAsync();
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SupportDbContext>();
            var operation = table == "Tickets" ? "UPDATE" : "INSERT";
            // Failure injection is confined to this case's isolated relational database.
            var failureRule = sqlConnection is null
                ? $"CREATE TRIGGER RejectWrite BEFORE {operation} ON {table} BEGIN SELECT RAISE(ABORT, 'Injected write failure'); END;"
                : table == "Tickets"
                    ? $"ALTER TABLE dbo.Tickets ADD CONSTRAINT CK_RejectEscalation CHECK ([Priority] = {(int)TicketPriority.Low});"
                    : "ALTER TABLE dbo.TicketEscalations ADD CONSTRAINT CK_RejectAudit CHECK ([Reason] = 'never-allowed');";
            await db.Database.ExecuteSqlRawAsync(failureRule);
        }
        using var response = await PostAsync(ticket.Id);
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        await AssertUnchangedAsync(ticket);
    }

    [Fact]
    public async Task Post_RepeatedEscalationsAppendHistoryWithoutRewritingOldRecords()
    {
        var ticket = await SeedTicketAsync();
        using var first = await PostAsync(ticket.Id);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var original = (await first.Content.ReadFromJsonAsync<EscalateTicketResponse>(json))!.Escalation;
        clock.Advance(TimeSpan.FromHours(1));
        using var secondResponse = await PostAsync(ticket.Id);
        Assert.Equal(HttpStatusCode.OK, secondResponse.StatusCode);
        using var scope = app.Services.CreateScope();
        var records = await scope.ServiceProvider.GetRequiredService<SupportDbContext>().TicketEscalations.OrderBy(record => record.Id).ToListAsync();
        Assert.Equal(2, records.Count);
        Assert.Equal(original.Id, records[0].Id);
        Assert.Equal(original.ToDueAtUtc, records[0].ToDueAtUtc);
        Assert.Equal(original.EscalatedAtUtc, records[0].EscalatedAtUtc);
        Assert.Equal(TicketPriority.Medium, records[1].FromPriority);
        Assert.Equal(TicketPriority.High, records[1].ToPriority);
    }

    [Fact]
    public async Task Post_LegacyUnassignedTicketWithoutDeadlineRecordsNullBeforeValues()
    {
        var ticket = await SeedTicketAsync(assigned: false, hasDeadline: false);
        using var response = await PostAsync(ticket.Id);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = (await response.Content.ReadFromJsonAsync<EscalateTicketResponse>(json))!;
        Assert.Null(result.Escalation.FromAgentId);
        Assert.Null(result.Escalation.FromDueAtUtc);
        Assert.Equal(current.Id, result.Escalation.ToAgentId);
        Assert.Equal(clock.UtcNow.AddHours(24), result.Escalation.ToDueAtUtc);
    }

    [Fact]
    public async Task Post_EligibleAtCapacitySpecialistIsRetained()
    {
        var ticket = await SeedTicketAsync(needsSpecialist: true);
        using var response = await PostAsync(ticket.Id);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(current.Id, (await response.Content.ReadFromJsonAsync<EscalateTicketResponse>(json))!.Ticket.AssignedAgent!.Id);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Get_HistoryOrdersByTimestampThenIdAndPreservesAllStoredValues(bool matchingTimestamps)
    {
        var ticket = await SeedTicketAsync();
        using var firstResponse = await PostAsync(ticket.Id);
        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
        var first = (await firstResponse.Content.ReadFromJsonAsync<EscalateTicketResponse>(json))!.Escalation;
        if (!matchingTimestamps) clock.Advance(TimeSpan.FromHours(1));
        using var secondResponse = await PostAsync(ticket.Id);
        Assert.Equal(HttpStatusCode.OK, secondResponse.StatusCode);
        var second = (await secondResponse.Content.ReadFromJsonAsync<EscalateTicketResponse>(json))!.Escalation;

        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SupportDbContext>();
        // A later ID with an older timestamp proves time is the primary sort, not ID.
        var oldest = new TicketEscalation(ticket.Id, TicketPriority.Low, TicketPriority.Medium,
            null, null, null, null, "Older historical event", "Other lead", first.EscalatedAtUtc.AddHours(-1));
        db.TicketEscalations.Add(oldest);
        var other = Ticket.Raise("TCK-0002", "Other ticket", "Another support request", standard.Id, general.Id,
            TicketPriority.Low, clock.UtcNow);
        db.Add(other);
        await db.SaveChangesAsync();
        db.TicketEscalations.Add(new TicketEscalation(other.Id, TicketPriority.Low, TicketPriority.Medium,
            null, null, null, null, "Different ticket history", "Other lead", clock.UtcNow.AddHours(1)));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var beforeHistory = JsonSerializer.Serialize(await db.TicketEscalations.AsNoTracking().OrderBy(record => record.Id).ToListAsync(), json);
        var beforeTickets = JsonSerializer.Serialize(await db.Set<Ticket>().AsNoTracking().OrderBy(record => record.Id).ToListAsync(), json);

        using var response = await client.GetAsync($"/api/tickets/{ticket.Id}/escalations");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var history = (await response.Content.ReadFromJsonAsync<List<TicketEscalationDto>>(json))!;
        Assert.Equal(new[] { second.Id, first.Id, oldest.Id }, history.Select(record => record.Id));
        Assert.All(history, record => Assert.Equal(ticket.Id, record.TicketId));
        Assert.Equal(second, history[0]);
        Assert.Equal(first, history[1]);
        Assert.Equal(new TicketEscalationDto(oldest.Id, ticket.Id, TicketPriority.Low, TicketPriority.Medium,
            null, null, null, null, oldest.Reason, oldest.EscalatedBy, oldest.EscalatedAtUtc), history[2]);
        Assert.Equal(matchingTimestamps, first.EscalatedAtUtc == second.EscalatedAtUtc);

        var direct = await new TicketQueries(db, clock).GetEscalationsAsync(ticket.Id, CancellationToken.None);
        Assert.Equal(history, direct!);
        Assert.Empty(db.ChangeTracker.Entries());
        Assert.Equal(beforeHistory, JsonSerializer.Serialize(await db.TicketEscalations.AsNoTracking().OrderBy(record => record.Id).ToListAsync(), json));
        Assert.Equal(beforeTickets, JsonSerializer.Serialize(await db.Set<Ticket>().AsNoTracking().OrderBy(record => record.Id).ToListAsync(), json));
        var repeated = (await client.GetFromJsonAsync<List<TicketEscalationDto>>($"/api/tickets/{ticket.Id}/escalations", json))!;
        Assert.Equal(history, repeated);
    }

    [Fact]
    public async Task Get_ExistingTicketWithoutHistoryReturns200AndEmptyArray()
    {
        var ticket = await SeedTicketAsync();
        using var response = await client.GetAsync($"/api/tickets/{ticket.Id}/escalations");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("[]", await response.Content.ReadAsStringAsync());
        await AssertUnchangedAsync(ticket);
    }

    [Fact]
    public async Task Get_NonexistentTicketReturns404WithoutModifyingExistingTicket()
    {
        var ticket = await SeedTicketAsync();
        using var response = await client.GetAsync("/api/tickets/999/escalations");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var problem = (await response.Content.ReadFromJsonAsync<ProblemDetails>(json))!;
        Assert.Equal(404, problem.Status);
        Assert.Contains("Ticket", problem.Detail);
        await AssertUnchangedAsync(ticket);
    }

    private async Task<Ticket> SeedTicketAsync(TicketPriority priority = TicketPriority.Low, bool isPremium = false,
        TicketStatus status = TicketStatus.New, bool needsSpecialist = false, bool assigned = true, bool hasDeadline = true)
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SupportDbContext>();
        var created = clock.UtcNow.AddDays(-1);
        var ticket = Ticket.Raise("TCK-0001", "Existing ticket", "Existing support request details",
            isPremium ? premium.Id : standard.Id, needsSpecialist ? specialist.Id : general.Id, priority, created);
        if (hasDeadline) ticket.SetDueDate(created.AddHours(72), created);
        ticket.AssignTo(assigned ? current.Id : null, created);
        ticket.ChangeStatus(status, created);
        db.Add(ticket);
        await db.SaveChangesAsync();
        return ticket;
    }

    private Task<HttpResponseMessage> PostAsync(int id) => client.PostAsJsonAsync($"/api/tickets/{id}/escalate",
        new EscalateTicketRequest("  Impact increased  ", "  Team lead  "), json);

    private async Task AssertUnchangedAsync(Ticket original)
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SupportDbContext>();
        var saved = await db.Set<Ticket>().AsNoTracking().SingleAsync();
        Assert.Equal(original.Priority, saved.Priority);
        Assert.Equal(original.Status, saved.Status);
        Assert.Equal(original.AssignedAgentId, saved.AssignedAgentId);
        Assert.Equal(original.DueAtUtc, saved.DueAtUtc);
        Assert.Equal(original.UpdatedAtUtc, saved.UpdatedAtUtc);
        Assert.Equal(0, await db.TicketEscalations.CountAsync());
    }
}
