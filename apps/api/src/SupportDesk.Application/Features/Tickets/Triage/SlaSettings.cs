using SupportDesk.Domain.Aggregates.Tickets;

namespace SupportDesk.Application.Features.Tickets.Triage;

public sealed class SlaSettings
{
    public const string SectionName = "Sla";

    public Dictionary<TicketPriority, double> BaseWindowHours { get; set; } = new();
    public double PremiumCustomerMultiplier { get; set; }
    public double MinimumWindowHours { get; set; }
    public double AtRiskThresholdPercent { get; set; }

    public void Validate()
    {
        foreach (var priority in Enum.GetValues<TicketPriority>())
        {
            if (BaseWindowHours is null || !BaseWindowHours.TryGetValue(priority, out var hours)
                || !double.IsFinite(hours) || hours <= 0 || hours > TimeSpan.MaxValue.TotalHours)
            {
                throw new InvalidOperationException($"Sla:BaseWindowHours:{priority} must be a positive, finite, representable number of hours.");
            }
        }

        if (!double.IsFinite(PremiumCustomerMultiplier) || PremiumCustomerMultiplier <= 0 || PremiumCustomerMultiplier > 1)
            throw new InvalidOperationException("Sla:PremiumCustomerMultiplier must be greater than 0 and at most 1.");
        if (!double.IsFinite(MinimumWindowHours) || MinimumWindowHours <= 0 || MinimumWindowHours > TimeSpan.MaxValue.TotalHours)
            throw new InvalidOperationException("Sla:MinimumWindowHours must be a positive, finite, representable number of hours.");
        if (!double.IsFinite(AtRiskThresholdPercent) || AtRiskThresholdPercent <= 0 || AtRiskThresholdPercent > 100)
            throw new InvalidOperationException("Sla:AtRiskThresholdPercent must be greater than 0 and at most 100.");
    }
}
