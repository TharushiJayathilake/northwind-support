using System.Net;
using Microsoft.AspNetCore.Identity;
using System.Net.Http.Headers;
using SupportDesk.Presentation.Authentication;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Data.Sqlite;
using Microsoft.Data.SqlClient;
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

public sealed class TicketCreationTests : IAsyncLifetime
{
    private readonly SqliteConnection connection = new("Data Source=:memory:");
    private readonly Customer standard = new("Standard", "standard@example.test", null, CustomerTier.Standard, FixedClock.DefaultNow);
    private readonly Customer premium = new("Premium", "premium@example.test", null, CustomerTier.Premium, FixedClock.DefaultNow);
    private readonly Category general = new("General", false, false);
    private readonly Category forced = new("Arbitrary forced category", true, true);
    private readonly Agent first = new("First", "first@example.test", 1, FixedClock.DefaultNow);
    private readonly Agent second = new("Second", "second@example.test", 2, FixedClock.DefaultNow);
    private readonly JsonSerializerOptions json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };
    private WebApplication app = null!;
    private HttpClient client = null!;
    private readonly CreationConcurrencyControl concurrency = new();
    private string? sqlConnectionString;
    private bool sqlDatabaseCreated;

    public async Task InitializeAsync()
    {
        var sqlServer = Environment.GetEnvironmentVariable("SUPPORTDESK_TEST_SQLSERVER");
        if (string.IsNullOrWhiteSpace(sqlServer))
            await connection.OpenAsync();
        else
        {
            // Never use the caller's database: every test receives a unique disposable database.
            sqlConnectionString = new SqlConnectionStringBuilder(sqlServer)
            {
                InitialCatalog = $"SupportDeskTriageTest_{Guid.NewGuid():N}"
            }.ConnectionString;
        }
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Production" });
        builder.Logging.ClearProviders();
        TestAuthentication.Configure(builder);
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Configuration.AddJsonFile(Path.Combine(AppContext.BaseDirectory, "sla-appsettings.json"));
        builder.Services.AddSlaSettings(builder.Configuration).AddApplication();
        builder.Services.AddSingleton<IClock>(new FixedClock());
        builder.Services.AddDbContext<SupportDbContext>(options =>
        {
            if (sqlConnectionString is null) options.UseSqlite(connection);
            else options.UseSqlServer(sqlConnectionString, sql => sql.EnableRetryOnFailure());
        });
        builder.Services.AddScoped<IUnitOfWork>(sp => new CreationConcurrencyControl.UnitOfWork(
            sp.GetRequiredService<SupportDbContext>(), concurrency));
        builder.Services.AddScoped<ITicketRepository>(sp => new CreationConcurrencyControl.Tickets(
            new TicketRepository(sp.GetRequiredService<SupportDbContext>()), concurrency));
        builder.Services.AddScoped<ICustomerRepository, CustomerRepository>();
        builder.Services.AddScoped<ICategoryRepository, CategoryRepository>();
        builder.Services.AddScoped<IAgentQueries>(sp => new CreationConcurrencyControl.Agents(
            new AgentQueries(sp.GetRequiredService<SupportDbContext>()), concurrency));
        builder.Services.AddScoped<ITicketQueries, TicketQueries>();
        builder.Services.AddScoped<ICustomerQueries, CustomerQueries>();
        builder.Services.AddScoped<ICategoryQueries, CategoryQueries>();
        builder.Services.AddControllers(options => options.Filters.Add<ValidationFilter>())
            .AddApplicationPart(typeof(TicketsController).Assembly)
            .AddJsonOptions(options => options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
        app = builder.Build();
        app.UseMiddleware<ExceptionHandlingMiddleware>();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapControllers();
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SupportDbContext>();
            if (sqlConnectionString is null) await db.Database.EnsureCreatedAsync();
            else
            {
                await db.Database.MigrateAsync();
                sqlDatabaseCreated = true;
            }
            db.AddRange(standard, premium, general, forced, first, second);
            first.SetPasswordHash(new PasswordHasher<Agent>().HashPassword(first, "Test-login-password!"));
            await db.SaveChangesAsync();
            first.AddSpecialization(forced.Id);
            second.AddSpecialization(forced.Id);
            await db.SaveChangesAsync();
        }
        await app.StartAsync();
        var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        client = new HttpClient { BaseAddress = new Uri(address) };
        TestAuthentication.SignIn(client, app.Services, first.Id, first.FullName);
    }

    public async Task DisposeAsync()
    {
        client?.Dispose();
        if (app is not null)
        {
            await app.StopAsync();
            if (sqlDatabaseCreated)
            {
                var database = new SqlConnectionStringBuilder(sqlConnectionString).InitialCatalog;
                if (!database.StartsWith("SupportDeskTriageTest_", StringComparison.Ordinal))
                    throw new InvalidOperationException("Refusing to delete a non-test database.");
                using var scope = app.Services.CreateScope();
                await scope.ServiceProvider.GetRequiredService<SupportDbContext>().Database.EnsureDeletedAsync();
            }
            await app.DisposeAsync();
        }
        await connection.DisposeAsync();
    }

    [Theory]
    [InlineData(TicketPriority.Low, false, false, TicketPriority.Low, 72)]
    [InlineData(TicketPriority.High, false, false, TicketPriority.High, 8)]
    [InlineData(TicketPriority.Critical, false, false, TicketPriority.Critical, 4)]
    [InlineData(null, false, false, TicketPriority.Medium, 24)]
    [InlineData(TicketPriority.Medium, true, false, TicketPriority.Medium, 12)]
    [InlineData(TicketPriority.Low, true, true, TicketPriority.Critical, 2)]
    public async Task Post_Returns201WithTriageAndPersistsCalculatedValues(
        TicketPriority? requested, bool isPremium, bool isForced, TicketPriority expectedPriority, double hours)
    {
        var request = Request(requested, isPremium ? premium.Id : standard.Id, isForced ? forced.Id : general.Id);
        using var response = await client.PostAsJsonAsync("/api/tickets", request, json);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var result = (await response.Content.ReadFromJsonAsync<TicketDetailDto>(json))!;
        Assert.Equal(expectedPriority, result.Priority);
        Assert.Equal(first.Id, result.AssignedAgent!.Id);
        Assert.Equal(FixedClock.DefaultNow.AddHours(hours), result.DueAtUtc);
        Assert.Equal(SlaStatus.WithinSla, result.SlaStatus);
        Assert.NotNull(result.Triage);
        Assert.False(string.IsNullOrWhiteSpace(result.Triage.PriorityReason));
        Assert.False(string.IsNullOrWhiteSpace(result.Triage.SlaReason));
        Assert.Contains("Selected agent", result.Triage.AssignmentReason);
        Assert.EndsWith($"/api/tickets/{result.Id}", response.Headers.Location!.ToString());

        using var scope = app.Services.CreateScope();
        var persisted = await scope.ServiceProvider.GetRequiredService<SupportDbContext>().Set<Ticket>().AsNoTracking().SingleAsync();
        Assert.Equal(result.Priority, persisted.Priority);
        Assert.Equal(result.DueAtUtc, persisted.DueAtUtc);
        Assert.Equal(first.Id, persisted.AssignedAgentId);
        Assert.Equal(FixedClock.DefaultNow, persisted.CreatedAtUtc);
        Assert.Equal(FixedClock.DefaultNow, persisted.UpdatedAtUtc);
        Assert.Equal(TicketStatus.New, persisted.Status);
        var reread = (await client.GetFromJsonAsync<TicketDetailDto>(response.Headers.Location, json))!;
        Assert.Equal(result.DueAtUtc, reread.DueAtUtc);
        Assert.Equal(result.AssignedAgent.Id, reread.AssignedAgent!.Id);
        Assert.Null(reread.Triage);
    }

    [Fact]
    public async Task Post_DeactivatedAuthenticatedAgentReturns401WithoutCreatingTicket()
    {
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SupportDbContext>();
            foreach (var agent in await db.Set<Agent>().ToListAsync()) agent.Deactivate();
            await db.SaveChangesAsync();
        }
        using var response = await client.PostAsJsonAsync("/api/tickets", Request(null, standard.Id, general.Id), json);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        using var readScope = app.Services.CreateScope();
        Assert.Equal(0, await readScope.ServiceProvider.GetRequiredService<SupportDbContext>().Set<Ticket>().CountAsync());
    }

    [Fact]
    public async Task Post_SequentialRequestsUseUpdatedWorkload()
    {
        using var one = await client.PostAsJsonAsync("/api/tickets", Request(null, standard.Id, general.Id), json);
        using var two = await client.PostAsJsonAsync("/api/tickets", Request(null, standard.Id, general.Id), json);
        Assert.Equal(HttpStatusCode.Created, one.StatusCode);
        Assert.Equal(HttpStatusCode.Created, two.StatusCode);
        Assert.Equal(first.Id, (await one.Content.ReadFromJsonAsync<TicketDetailDto>(json))!.AssignedAgent!.Id);
        Assert.Equal(second.Id, (await two.Content.ReadFromJsonAsync<TicketDetailDto>(json))!.AssignedAgent!.Id);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Post_UnknownCustomerOrCategoryReturns404WithoutSaving(bool invalidCustomer)
    {
        using var response = await client.PostAsJsonAsync("/api/tickets",
            Request(null, invalidCustomer ? 999 : standard.Id, invalidCustomer ? general.Id : 999), json);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        using var scope = app.Services.CreateScope();
        Assert.Equal(0, await scope.ServiceProvider.GetRequiredService<SupportDbContext>().Set<Ticket>().CountAsync());
    }

    [Fact]
    public async Task Post_InvalidRequestStillReturns400()
    {
        using var response = await client.PostAsJsonAsync("/api/tickets", new CreateTicketRequest("", "", standard.Id, general.Id, null), json);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData(5, 0.25, 1.25)]
    [InlineData(2, 0.25, 1)]
    public async Task Post_CustomConfigurationAppliesFractionalWindowAndMinimum(double baseHours, double multiplier, double expectedHours)
    {
        var settings = app.Services.GetRequiredService<SlaSettings>();
        settings.BaseWindowHours[TicketPriority.High] = baseHours;
        settings.PremiumCustomerMultiplier = multiplier;
        using var response = await client.PostAsJsonAsync("/api/tickets", Request(TicketPriority.High, premium.Id, general.Id), json);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var result = (await response.Content.ReadFromJsonAsync<TicketDetailDto>(json))!;
        Assert.Equal(FixedClock.DefaultNow.AddHours(expectedHours), result.DueAtUtc);
        Assert.Contains($"applied window {expectedHours.ToString(System.Globalization.CultureInfo.InvariantCulture)} hours", result.Triage!.SlaReason);
        using var scope = app.Services.CreateScope();
        Assert.Equal(result.DueAtUtc, (await scope.ServiceProvider.GetRequiredService<SupportDbContext>().Set<Ticket>().SingleAsync()).DueAtUtc);
    }

    [Fact]
    public async Task Post_NoMatchingSpecialistReturns201Unassigned()
    {
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SupportDbContext>();
            db.RemoveRange(await db.Set<AgentSpecialization>().ToListAsync());
            await db.SaveChangesAsync();
        }
        using var response = await client.PostAsJsonAsync("/api/tickets", Request(TicketPriority.Low, premium.Id, forced.Id), json);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var result = (await response.Content.ReadFromJsonAsync<TicketDetailDto>(json))!;
        Assert.Null(result.AssignedAgent);
        Assert.Equal(TicketPriority.Critical, result.Priority);
        Assert.Contains("required specialization", result.Triage!.AssignmentReason);
        using var readScope = app.Services.CreateScope();
        Assert.Null((await readScope.ServiceProvider.GetRequiredService<SupportDbContext>().Set<Ticket>().SingleAsync()).AssignedAgentId);
    }

    [Fact]
    public async Task Post_AllAgentsAtCapacityReturns201Unassigned()
    {
        for (var index = 0; index < 3; index++)
        {
            using var seeded = await client.PostAsJsonAsync("/api/tickets", Request(null, standard.Id, general.Id), json);
            Assert.Equal(HttpStatusCode.Created, seeded.StatusCode);
        }
        using var response = await client.PostAsJsonAsync("/api/tickets", Request(null, standard.Id, general.Id), json);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var result = (await response.Content.ReadFromJsonAsync<TicketDetailDto>(json))!;
        Assert.Null(result.AssignedAgent);
        Assert.Contains("capacity", result.Triage!.AssignmentReason);
        using var scope = app.Services.CreateScope();
        var tickets = await scope.ServiceProvider.GetRequiredService<SupportDbContext>().Set<Ticket>().ToListAsync();
        Assert.Equal(4, tickets.Count);
        Assert.Null(tickets.Single(ticket => ticket.Id == result.Id).AssignedAgentId);
    }

    [SqlServerFact]
    public async Task Post_ConcurrentStaleWorkloadsCanExceedCapacity()
    {
        concurrency.Mode = CreationConcurrencyControl.Schedule.CapacityRace;
        var responses = await Task.WhenAll(
            client.PostAsJsonAsync("/api/tickets", Request(null, standard.Id, general.Id), json),
            client.PostAsJsonAsync("/api/tickets", Request(null, standard.Id, general.Id), json));
        try
        {
            Assert.All(responses, response => Assert.Equal(HttpStatusCode.Created, response.StatusCode));
            using var scope = app.Services.CreateScope();
            var tickets = await scope.ServiceProvider.GetRequiredService<SupportDbContext>().Set<Ticket>().ToListAsync();
            Assert.Equal(2, tickets.Count);
            Assert.Equal(2, tickets.Select(ticket => ticket.Reference).Distinct().Count());
            Assert.All(tickets, ticket => Assert.Equal(first.Id, ticket.AssignedAgentId));
            Assert.True(tickets.Count > first.MaxOpenTickets);
        }
        finally { foreach (var response in responses) response.Dispose(); }
    }

    [SqlServerFact]
    public async Task Post_ConcurrentReferenceReadsCanCauseUniqueKeyConflict()
    {
        concurrency.Mode = CreationConcurrencyControl.Schedule.ReferenceRace;
        var responses = await Task.WhenAll(
            client.PostAsJsonAsync("/api/tickets", Request(null, standard.Id, general.Id), json),
            client.PostAsJsonAsync("/api/tickets", Request(null, standard.Id, general.Id), json));
        try
        {
            Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Created);
            Assert.Single(responses, response => response.StatusCode == HttpStatusCode.InternalServerError);
            Assert.Equal(2, concurrency.References.Count);
            Assert.Single(concurrency.References.Distinct());
            Assert.Contains(concurrency.SqlErrorNumbers, number => number is 2601 or 2627);
            using var scope = app.Services.CreateScope();
            Assert.Equal(1, await scope.ServiceProvider.GetRequiredService<SupportDbContext>().Set<Ticket>().CountAsync());
        }
        finally { foreach (var response in responses) response.Dispose(); }
    }

    [Fact]
    public async Task Auth_LoginIsPublicReturnsJwtAndMeWithoutHash()
    {
        client.DefaultRequestHeaders.Authorization = null;
        using var response = await client.PostAsJsonAsync("/api/auth/login", new { email = first.Email, password = "Test-login-password!" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain("passwordHash", await response.Content.ReadAsStringAsync());
        var login = (await response.Content.ReadFromJsonAsync<LoginResponse>(json))!;
        Assert.Equal(first.Id, login.Agent.Id);
        Assert.True(login.ExpiresAtUtc > DateTime.UtcNow);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);
        using var me = await client.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        Assert.Equal(first.Id, (await me.Content.ReadFromJsonAsync<AuthAgent>(json))!.Id);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/tickets")).StatusCode);
    }

    [Fact]
    public async Task Auth_UnknownWrongPasswordAndInactiveReturnIdentical401()
    {
        client.DefaultRequestHeaders.Authorization = null;
        using var wrong = await client.PostAsJsonAsync("/api/auth/login", new { email = first.Email, password = "wrong" });
        using var unknown = await client.PostAsJsonAsync("/api/auth/login", new { email = "unknown@example.test", password = "wrong" });
        Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, unknown.StatusCode);
        Assert.Equal(await wrong.Content.ReadAsStringAsync(), await unknown.Content.ReadAsStringAsync());
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SupportDbContext>();
            (await db.Set<Agent>().SingleAsync(a => a.Id == first.Id)).Deactivate();
            await db.SaveChangesAsync();
        }
        using var inactive = await client.PostAsJsonAsync("/api/auth/login", new { email = first.Email, password = "Test-login-password!" });
        Assert.Equal(HttpStatusCode.Unauthorized, inactive.StatusCode);
        Assert.Equal(await wrong.Content.ReadAsStringAsync(), await inactive.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("/api/tickets")]
    [InlineData("/api/customers")]
    [InlineData("/api/agents")]
    [InlineData("/api/categories")]
    [InlineData("/api/auth/me")]
    public async Task Auth_EndpointsRequireToken(string path)
    {
        client.DefaultRequestHeaders.Authorization = null;
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(path)).StatusCode);
        TestAuthentication.SignIn(client, app.Services, first.Id, first.FullName);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(path)).StatusCode);
    }

    [Theory]
    [InlineData("invalid")]
    [InlineData("expired")]
    [InlineData("wrongAudience")]
    [InlineData("wrongKey")]
    public async Task Auth_RejectsInvalidExpiredAndWrongScopeTokens(string kind)
    {
        var settings = app.Services.GetRequiredService<JwtSettings>();
        var testSettings = new JwtSettings { Issuer = settings.Issuer,
            Audience = kind == "wrongAudience" ? "Wrong" : settings.Audience,
            SigningKey = kind == "wrongKey" ? new string('x', 64) : settings.SigningKey };
        var clock = new FixedClock(kind == "expired" ? DateTime.UtcNow.AddHours(-2) : DateTime.UtcNow);
        var token = new JwtTokens(testSettings, clock).Create(new AuthAgent(first.Id, first.FullName, first.Email));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", kind == "invalid" ? "invalid.token" : token.AccessToken);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/me")).StatusCode);
    }

    [Fact]
    public async Task Auth_CompleteLoginCreateTriageEscalateHistoryAndClientLogoutWorkflow()
    {
        client.DefaultRequestHeaders.Authorization = null;
        using var loginResponse = await client.PostAsJsonAsync("/api/auth/login", new { email = first.Email, password = "Test-login-password!" });
        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);
        var login = (await loginResponse.Content.ReadFromJsonAsync<LoginResponse>(json))!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);
        using var creation = await client.PostAsJsonAsync("/api/tickets", Request(null, standard.Id, general.Id), json);
        Assert.Equal(HttpStatusCode.Created, creation.StatusCode);
        var ticket = (await creation.Content.ReadFromJsonAsync<TicketDetailDto>(json))!;
        Assert.NotNull(ticket.Triage);
        using var escalation = await client.PostAsJsonAsync($"/api/tickets/{ticket.Id}/escalate", new EscalateTicketRequest("Impact increased"), json);
        Assert.Equal(HttpStatusCode.OK, escalation.StatusCode);
        var result = (await escalation.Content.ReadFromJsonAsync<EscalateTicketResponse>(json))!;
        Assert.Equal(TicketPriority.High, result.Ticket.Priority);
        Assert.Equal($"agent:{first.Id} {first.FullName}", result.Escalation.EscalatedBy);
        var history = (await client.GetFromJsonAsync<List<TicketEscalationDto>>($"/api/tickets/{ticket.Id}/escalations", json))!;
        Assert.Single(history);
        Assert.Equal(result.Escalation, history[0]);
        client.DefaultRequestHeaders.Authorization = null;
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/tickets")).StatusCode);
    }

    private static CreateTicketRequest Request(TicketPriority? priority, int customerId, int categoryId) =>
        new("Cannot access account", "Unable to access the account this morning.", customerId, categoryId, priority);
}
