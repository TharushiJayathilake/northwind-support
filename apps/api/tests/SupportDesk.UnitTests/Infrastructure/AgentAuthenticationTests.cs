using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SupportDesk.Domain.Aggregates.Agents;
using SupportDesk.Infrastructure.Data;
using SupportDesk.Infrastructure.Services;
using SupportDesk.UnitTests.TestDoubles;

namespace SupportDesk.UnitTests.Infrastructure;

public sealed class AgentAuthenticationTests
{
    [Fact]
    public async Task HashesAreSalted_VerifyPasswords_RejectUnknownWrongAndInactive()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new SupportDbContext(new DbContextOptionsBuilder<SupportDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        var agent = new Agent("Test", "agent@example.test", 3, FixedClock.DefaultNow);
        var hasher = new PasswordHasher<Agent>();
        agent.SetPasswordHash(hasher.HashPassword(agent, "Test-only-password"));
        Assert.NotEqual("Test-only-password", agent.PasswordHash);
        Assert.NotEqual(agent.PasswordHash, hasher.HashPassword(agent, "Test-only-password"));
        db.Add(agent); await db.SaveChangesAsync();
        var service = new AgentAuthentication(db);
        Assert.Equal(agent.Id, (await service.VerifyAsync(" AGENT@example.test ", "Test-only-password", default))?.Id);
        Assert.Null(await service.VerifyAsync(agent.Email, "wrong", default));
        Assert.Null(await service.VerifyAsync("missing@example.test", "wrong", default));
        agent.Deactivate(); await db.SaveChangesAsync();
        Assert.Null(await service.VerifyAsync(agent.Email, "Test-only-password", default));
    }

    [Fact]
    public async Task DevelopmentCredentialSeedingIsRepeatableAndDoesNotReplaceExistingHashes()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new SupportDbContext(new DbContextOptionsBuilder<SupportDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        var demo = new Agent("Alex", "alex.turner@northwind-support.example", 1, FixedClock.DefaultNow);
        var other = new Agent("Other", "other@example.test", 1, FixedClock.DefaultNow);
        var existing = new Agent("Ben", "ben.osei@northwind-support.example", 1, FixedClock.DefaultNow);
        var existingHash = new PasswordHasher<Agent>().HashPassword(existing, "Existing credential");
        existing.SetPasswordHash(existingHash);
        db.AddRange(demo, other, existing); await db.SaveChangesAsync();
        var seeder = new SupportDbSeeder(db, new FixedClock(), NullLogger<SupportDbSeeder>.Instance);
        await seeder.SeedDevelopmentCredentialsAsync();
        var original = demo.PasswordHash;
        await seeder.SeedDevelopmentCredentialsAsync();
        Assert.NotNull(original); Assert.Equal(original, demo.PasswordHash); Assert.Null(other.PasswordHash);
        Assert.Equal(existingHash, existing.PasswordHash);
    }
}
