using Microsoft.EntityFrameworkCore;
using SupportDesk.Domain.Repositories;
using SupportDesk.Domain.Aggregates.Tickets;

namespace SupportDesk.Infrastructure.Data;

/// <summary>
/// EF Core context for the support desk, and the unit of work that commits each request's
/// changes. Mapping lives in the per-aggregate configuration classes in
/// <c>Data/Configurations</c>.
/// </summary>
public class SupportDbContext(DbContextOptions<SupportDbContext> options) : DbContext(options), IUnitOfWork
{
    public DbSet<TicketEscalation> TicketEscalations => Set<TicketEscalation>();

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        ProtectEscalationHistory();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        ProtectEscalationHistory();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private void ProtectEscalationHistory()
    {
        ChangeTracker.DetectChanges();
        if (ChangeTracker.Entries<TicketEscalation>().Any(entry => entry.State is EntityState.Modified or EntityState.Deleted))
            throw new InvalidOperationException("Ticket escalation history is append-only and cannot be updated or deleted.");
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(SupportDbContext).Assembly);
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // Every timestamp in this database is UTC; say so, so that it survives the round trip.
        configurationBuilder.Properties<DateTime>().HaveConversion<UtcDateTimeConverter>();
        configurationBuilder.Properties<DateTime?>().HaveConversion<NullableUtcDateTimeConverter>();
    }
}
