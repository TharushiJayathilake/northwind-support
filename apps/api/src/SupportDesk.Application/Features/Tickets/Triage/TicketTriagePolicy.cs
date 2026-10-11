using SupportDesk.Domain.Aggregates.Categories;
using SupportDesk.Domain.Aggregates.Customers;
using SupportDesk.Domain.Aggregates.Tickets;

namespace SupportDesk.Application.Features.Tickets.Triage;

public sealed class TicketTriagePolicy
{
    private readonly Dictionary<TicketPriority, double> baseWindowHours;
    private readonly double premiumMultiplier;
    private readonly double minimumHours;

    public TicketTriagePolicy(SlaSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.Validate();
        baseWindowHours = new(settings.BaseWindowHours);
        premiumMultiplier = settings.PremiumCustomerMultiplier;
        minimumHours = settings.MinimumWindowHours;
    }

    public PrioritySlaDecision Calculate(
        TicketPriority? requestedPriority, Category category, CustomerTier tier, DateTime createdAtUtc)
    {
        ArgumentNullException.ThrowIfNull(category);
        if (requestedPriority is { } requested && !Enum.IsDefined(requested))
            throw new ArgumentOutOfRangeException(nameof(requestedPriority));

        var priority = category.ForcesCriticalPriority
            ? TicketPriority.Critical
            : requestedPriority ?? TicketPriority.Medium;
        return CalculateDeadline(priority, tier, createdAtUtc) with
        {
            PriorityReason = category.ForcesCriticalPriority
                ? "The category requires Critical priority, overriding any requested priority."
                : requestedPriority.HasValue
                    ? $"Using the requested {priority} priority."
                    : "No priority was requested; defaulted to Medium."
        };
    }

    // Escalation can reuse deadline calculation with its already-decided priority and start time.
    public PrioritySlaDecision CalculateDeadline(TicketPriority priority, CustomerTier tier, DateTime startsAtUtc)
    {
        if (!Enum.IsDefined(priority)) throw new ArgumentOutOfRangeException(nameof(priority));
        if (!Enum.IsDefined(tier)) throw new ArgumentOutOfRangeException(nameof(tier));
        if (startsAtUtc.Kind != DateTimeKind.Utc)
            throw new ArgumentException("The SLA start timestamp must be UTC.", nameof(startsAtUtc));

        var hours = baseWindowHours[priority];
        if (tier == CustomerTier.Premium) hours *= premiumMultiplier;
        var window = TimeSpan.FromHours(Math.Max(minimumHours, hours));
        return new PrioritySlaDecision(priority, window, startsAtUtc.Add(window),
            $"Using {priority} priority.",
            FormattableString.Invariant($"{tier} customer: configured base {baseWindowHours[priority]} hours, multiplier {(tier == CustomerTier.Premium ? premiumMultiplier : 1)}, minimum {minimumHours} hours; applied window {window.TotalHours} hours from the UTC start time."));
    }
}

public sealed record PrioritySlaDecision(TicketPriority Priority, TimeSpan Window, DateTime DueAtUtc,
    string PriorityReason, string SlaReason);
