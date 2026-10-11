using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using SupportDesk.Infrastructure.Data;
using SupportDesk.Presentation.Health;

namespace SupportDesk.UnitTests.Presentation;

public sealed class DatabaseHealthCheckTests
{
    [Fact]
    public async Task ReachableDatabase_IsHealthy()
    {
        await using var db = new SupportDbContext(new DbContextOptionsBuilder<SupportDbContext>()
            .UseSqlite("Data Source=:memory:").Options);
        await db.Database.OpenConnectionAsync();
        var result = await new DatabaseHealthCheck(db).CheckHealthAsync(new HealthCheckContext());
        Assert.Equal(HealthStatus.Healthy, result.Status);
    }

    [Fact]
    public async Task UnavailableDatabase_IsUnhealthy()
    {
        await using var db = new SupportDbContext(new DbContextOptionsBuilder<SupportDbContext>()
            .UseSqlite("Data Source=/nonexistent/northwind/health.db").Options);
        var result = await new DatabaseHealthCheck(db).CheckHealthAsync(new HealthCheckContext());
        Assert.Equal(HealthStatus.Unhealthy, result.Status);
    }
}
