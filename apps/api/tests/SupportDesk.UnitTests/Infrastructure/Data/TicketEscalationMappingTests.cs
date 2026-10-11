using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using SupportDesk.Domain.Aggregates.Agents;
using SupportDesk.Domain.Aggregates.Categories;
using SupportDesk.Domain.Aggregates.Customers;
using SupportDesk.Domain.Aggregates.Tickets;
using SupportDesk.Infrastructure.Data;
using SupportDesk.Infrastructure.Data.Migrations;
using SupportDesk.UnitTests.TestDoubles;

namespace SupportDesk.UnitTests.Infrastructure.Data;

public sealed class TicketEscalationMappingTests : IAsyncLifetime
{
    private readonly SqliteConnection sqlite = new("Data Source=:memory:");
    private SupportDbContext db = null!;
    private string? sqlDatabase;
    private Ticket ticket = null!;
    private Agent fromAgent = null!;
    private Agent toAgent = null!;

    public async Task InitializeAsync()
    {
        var options = new DbContextOptionsBuilder<SupportDbContext>();
        var server = Environment.GetEnvironmentVariable("SUPPORTDESK_TEST_SQLSERVER");
        if (string.IsNullOrWhiteSpace(server))
        {
            await sqlite.OpenAsync();
            options.UseSqlite(sqlite);
        }
        else
        {
            sqlDatabase = $"SupportDeskEscalationTest_{Guid.NewGuid():N}";
            options.UseSqlServer(new SqlConnectionStringBuilder(server) { InitialCatalog = sqlDatabase }.ConnectionString);
        }
        db = new SupportDbContext(options.Options);
        if (sqlDatabase is null) await db.Database.EnsureCreatedAsync();
        else await db.Database.MigrateAsync();

        var customer = new Customer("Customer", "customer@example.test", null, CustomerTier.Standard, FixedClock.DefaultNow);
        var category = new Category("General", false, false);
        fromAgent = new Agent("Previous", "previous@example.test", 10, FixedClock.DefaultNow);
        toAgent = new Agent("Next", "next@example.test", 10, FixedClock.DefaultNow);
        db.AddRange(customer, category, fromAgent, toAgent);
        await db.SaveChangesAsync();
        ticket = Ticket.Raise("TCK-0001", "Test ticket", "Support request details", customer.Id, category.Id,
            TicketPriority.Low, FixedClock.DefaultNow);
        db.Add(ticket);
        await db.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        if (db is not null)
        {
            if (sqlDatabase is not null)
            {
                if (!sqlDatabase.StartsWith("SupportDeskEscalationTest_", StringComparison.Ordinal))
                    throw new InvalidOperationException("Refusing to delete a non-test database.");
                await db.Database.EnsureDeletedAsync();
            }
            await db.DisposeAsync();
        }
        await sqlite.DisposeAsync();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Mapping_PersistsSnapshotAndNullableFields(bool nullable)
    {
        var record = Record(nullable);
        db.TicketEscalations.Add(record);
        await db.SaveChangesAsync();
        Assert.True(record.Id > 0);
        db.ChangeTracker.Clear();
        var saved = await db.TicketEscalations.AsNoTracking().SingleAsync();
        Assert.Equal(ticket.Id, saved.TicketId);
        Assert.Equal(TicketPriority.Low, saved.FromPriority);
        Assert.Equal(TicketPriority.Medium, saved.ToPriority);
        Assert.Equal(record.FromAgentId, saved.FromAgentId);
        Assert.Equal(record.ToAgentId, saved.ToAgentId);
        Assert.Equal(record.FromDueAtUtc, saved.FromDueAtUtc);
        Assert.Equal(record.ToDueAtUtc, saved.ToDueAtUtc);
        Assert.Equal(new string('r', 500), saved.Reason);
        Assert.Equal(new string('a', 100), saved.EscalatedBy);
        Assert.Equal(FixedClock.DefaultNow, saved.EscalatedAtUtc);
        Assert.Equal(DateTimeKind.Utc, saved.EscalatedAtUtc.Kind);
        if (saved.FromDueAtUtc.HasValue) Assert.Equal(DateTimeKind.Utc, saved.FromDueAtUtc.Value.Kind);
        Assert.Equal(TicketPriority.Low, (await db.Set<Ticket>().SingleAsync()).Priority);
    }

    [Fact]
    public void Mapping_ConfiguresRequiredFieldsLengthsIndexAndRestrictForeignKeys()
    {
        var entity = db.Model.FindEntityType(typeof(TicketEscalation))!;
        Assert.Equal("Id", Assert.Single(entity.FindPrimaryKey()!.Properties).Name);
        Assert.Equal(500, entity.FindProperty(nameof(TicketEscalation.Reason))!.GetMaxLength());
        Assert.Equal(100, entity.FindProperty(nameof(TicketEscalation.EscalatedBy))!.GetMaxLength());
        foreach (var name in new[] { "TicketId", "FromPriority", "ToPriority", "Reason", "EscalatedBy", "EscalatedAtUtc" })
            Assert.False(entity.FindProperty(name)!.IsNullable);
        foreach (var name in new[] { "FromAgentId", "ToAgentId", "FromDueAtUtc", "ToDueAtUtc" })
            Assert.True(entity.FindProperty(name)!.IsNullable);
        Assert.Equal(3, entity.GetForeignKeys().Count());
        Assert.All(entity.GetForeignKeys(), key => Assert.Equal(DeleteBehavior.Restrict, key.DeleteBehavior));
        Assert.Contains(entity.GetIndexes(), index => index.GetDatabaseName() == "IX_TicketEscalations_TicketId");
    }

    [Theory]
    [InlineData("ticket")]
    [InlineData("fromAgent")]
    [InlineData("toAgent")]
    public async Task Mapping_DatabaseRejectsUnknownForeignKeys(string invalid)
    {
        db.TicketEscalations.Add(new TicketEscalation(invalid == "ticket" ? 999 : ticket.Id,
            TicketPriority.Low, TicketPriority.Medium,
            invalid == "fromAgent" ? 999 : fromAgent.Id, invalid == "toAgent" ? 999 : toAgent.Id,
            null, null, "Impact increased", "Team lead", FixedClock.DefaultNow));
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Theory]
    [InlineData("ticket")]
    [InlineData("fromAgent")]
    [InlineData("toAgent")]
    public async Task Mapping_DatabasePreventsDeletingReferencedTicketOrAgent(string deleted)
    {
        db.TicketEscalations.Add(Record());
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        if (deleted == "ticket") db.Remove(await db.Set<Ticket>().SingleAsync());
        else db.Remove(await db.Set<Agent>().SingleAsync(agent => agent.Id == (deleted == "fromAgent" ? fromAgent.Id : toAgent.Id)));
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        db.ChangeTracker.Clear();
        Assert.Equal(1, await db.TicketEscalations.CountAsync());
        Assert.Equal(1, await db.Set<Ticket>().CountAsync());
        Assert.Equal(2, await db.Set<Agent>().CountAsync());
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task History_RejectsUpdatesAndDeletesThroughSyncAndAsyncSaves(bool delete, bool synchronous)
    {
        var record = Record();
        db.TicketEscalations.Add(record);
        await db.SaveChangesAsync();
        if (delete) db.TicketEscalations.Remove(record);
        else db.Entry(record).Property(escalation => escalation.Reason).CurrentValue = "Attempted rewrite";
        if (synchronous) Assert.Throws<InvalidOperationException>(() => db.SaveChanges());
        else await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
        db.ChangeTracker.Clear();
        Assert.Equal(new string('r', 500), (await db.TicketEscalations.SingleAsync()).Reason);
    }

    [Fact]
    public void Migration_SqlServerModelMatchesSnapshot()
    {
        using var context = new SupportDbContext(new DbContextOptionsBuilder<SupportDbContext>()
            .UseSqlServer("Server=unused;Database=unused;Integrated Security=True;TrustServerCertificate=True").Options);
        Assert.False(context.Database.HasPendingModelChanges());
    }

    [Fact]
    public void Migration_AddsOnlyAuditTableAndItsIndexes()
    {
        var migration = new AddTicketEscalations();
        var table = Assert.Single(migration.UpOperations.OfType<CreateTableOperation>());
        Assert.Equal("TicketEscalations", table.Name);
        Assert.Equal(11, table.Columns.Count);
        Assert.Equal(3, table.ForeignKeys.Count);
        Assert.Equal(2, table.CheckConstraints.Count);
        Assert.Equal(4, migration.UpOperations.Count);
        Assert.All(migration.UpOperations.OfType<CreateIndexOperation>(), index => Assert.Equal("TicketEscalations", index.Table));
        Assert.Equal("TicketEscalations", Assert.IsType<DropTableOperation>(Assert.Single(migration.DownOperations)).Name);
    }

    private TicketEscalation Record(bool nullable = false) => new(ticket.Id, TicketPriority.Low, TicketPriority.Medium,
        nullable ? null : fromAgent.Id, nullable ? null : toAgent.Id,
        nullable ? null : FixedClock.DefaultNow.AddHours(72), nullable ? null : FixedClock.DefaultNow.AddHours(24),
        new string('r', 500), new string('a', 100), FixedClock.DefaultNow);

    [SqlServerFact]
    public async Task Migration_SqlServerCatalogContainsAuditForeignKeysAndIndexes()
    {
        Assert.Contains("20261011095659_AddTicketEscalations", await db.Database.GetAppliedMigrationsAsync());
        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
        var indexes = await db.Database.SqlQueryRaw<string>("""
            SELECT i.name + ':' + c.name AS [Value]
            FROM sys.indexes i
            JOIN sys.index_columns ic ON i.object_id = ic.object_id AND i.index_id = ic.index_id
            JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
            WHERE i.object_id = OBJECT_ID('dbo.TicketEscalations') AND ic.key_ordinal > 0
            """).ToListAsync();
        Assert.Equal(new[] { "IX_TicketEscalations_FromAgentId:FromAgentId", "IX_TicketEscalations_TicketId:TicketId",
            "IX_TicketEscalations_ToAgentId:ToAgentId", "PK_TicketEscalations:Id" }, indexes.OrderBy(value => value).ToArray());
        var foreignKeys = await db.Database.SqlQueryRaw<string>("""
            SELECT c.name + ':' + OBJECT_NAME(fk.referenced_object_id) + ':' +
                fk.delete_referential_action_desc COLLATE DATABASE_DEFAULT AS [Value]
            FROM sys.foreign_keys fk
            JOIN sys.foreign_key_columns fkc ON fk.object_id = fkc.constraint_object_id
            JOIN sys.columns c ON c.object_id = fkc.parent_object_id AND c.column_id = fkc.parent_column_id
            WHERE fk.parent_object_id = OBJECT_ID('dbo.TicketEscalations')
            """).ToListAsync();
        Assert.Equal(new[] { "FromAgentId:Agents:NO_ACTION", "TicketId:Tickets:NO_ACTION", "ToAgentId:Agents:NO_ACTION" },
            foreignKeys.OrderBy(value => value).ToArray());
    }
}
