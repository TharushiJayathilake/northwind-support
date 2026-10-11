using System.Linq.Expressions;

namespace SupportDesk.Domain.Aggregates.Tickets;

/// <summary>
/// Works out where a ticket stands against its due date. SLA status is always derived from
/// the ticket's dates and the current time - never stored.
/// </summary>
public static class SlaEvaluator
{
    // Infrastructure substitutes SQL duration expressions into these same classification rules.
    public static Expression<Func<DateTime?, DateTime?, TicketStatus, double, double, double, SlaStatus>> Classification { get; } =
        (due, resolved, status, windowTicks, remainingTicks, thresholdPercent) =>
            due == null ? SlaStatus.NotApplicable
            : resolved != null ? (resolved <= due ? SlaStatus.Met : SlaStatus.Breached)
            : status == TicketStatus.Resolved || status == TicketStatus.Closed ? SlaStatus.NotApplicable
            : remainingTicks < 0 ? SlaStatus.Breached
            : windowTicks <= 0 ? SlaStatus.NotApplicable
            : remainingTicks <= windowTicks * (thresholdPercent / 100d) ? SlaStatus.AtRisk
            : SlaStatus.WithinSla;

    private static readonly Func<DateTime?, DateTime?, TicketStatus, double, double, double, SlaStatus> Classify =
        Classification.Compile();

    public static SlaStatus Evaluate(DateTime? dueAtUtc, DateTime? resolvedAtUtc, DateTime nowUtc,
        DateTime startsAtUtc, double atRiskThresholdPercent, TicketStatus status)
    {
        if (nowUtc.Kind != DateTimeKind.Utc || startsAtUtc.Kind != DateTimeKind.Utc
            || (dueAtUtc.HasValue && dueAtUtc.Value.Kind != DateTimeKind.Utc)
            || (resolvedAtUtc.HasValue && resolvedAtUtc.Value.Kind != DateTimeKind.Utc))
            throw new ArgumentException("SLA timestamps must be UTC.");
        if (!double.IsFinite(atRiskThresholdPercent) || atRiskThresholdPercent <= 0 || atRiskThresholdPercent > 100)
            throw new ArgumentOutOfRangeException(nameof(atRiskThresholdPercent));
        return Classify(dueAtUtc, resolvedAtUtc, status,
            dueAtUtc.HasValue ? (dueAtUtc.Value - startsAtUtc).Ticks : 0,
            dueAtUtc.HasValue ? (dueAtUtc.Value - nowUtc).Ticks : 0, atRiskThresholdPercent);
    }
}
