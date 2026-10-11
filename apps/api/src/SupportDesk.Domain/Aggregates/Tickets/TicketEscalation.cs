using SupportDesk.Domain.Common;

namespace SupportDesk.Domain.Aggregates.Tickets;

/// <summary>An immutable snapshot of one escalation, belonging to the ticket's audit history.</summary>
public sealed class TicketEscalation : Entity
{
    private TicketEscalation()
    {
        // For EF Core.
    }

    public TicketEscalation(int ticketId, TicketPriority fromPriority, TicketPriority toPriority,
        int? fromAgentId, int? toAgentId, DateTime? fromDueAtUtc, DateTime? toDueAtUtc,
        string reason, string escalatedBy, DateTime escalatedAtUtc)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(ticketId);
        if (!Enum.IsDefined(fromPriority)) throw new ArgumentOutOfRangeException(nameof(fromPriority));
        if (!Enum.IsDefined(toPriority)) throw new ArgumentOutOfRangeException(nameof(toPriority));
        if (fromAgentId is <= 0) throw new ArgumentOutOfRangeException(nameof(fromAgentId));
        if (toAgentId is <= 0) throw new ArgumentOutOfRangeException(nameof(toAgentId));
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        ArgumentException.ThrowIfNullOrWhiteSpace(escalatedBy);
        reason = reason.Trim();
        escalatedBy = escalatedBy.Trim();
        if (reason.Length > 500) throw new ArgumentException("Reason cannot exceed 500 characters.", nameof(reason));
        if (escalatedBy.Length > 100) throw new ArgumentException("EscalatedBy cannot exceed 100 characters.", nameof(escalatedBy));
        if (escalatedAtUtc.Kind != DateTimeKind.Utc
            || (fromDueAtUtc.HasValue && fromDueAtUtc.Value.Kind != DateTimeKind.Utc)
            || (toDueAtUtc.HasValue && toDueAtUtc.Value.Kind != DateTimeKind.Utc))
            throw new ArgumentException("Escalation timestamps must be UTC.");

        TicketId = ticketId;
        FromPriority = fromPriority;
        ToPriority = toPriority;
        FromAgentId = fromAgentId;
        ToAgentId = toAgentId;
        FromDueAtUtc = fromDueAtUtc;
        ToDueAtUtc = toDueAtUtc;
        Reason = reason;
        EscalatedBy = escalatedBy;
        EscalatedAtUtc = escalatedAtUtc;
    }

    public int TicketId { get; private set; }
    public TicketPriority FromPriority { get; private set; }
    public TicketPriority ToPriority { get; private set; }
    public int? FromAgentId { get; private set; }
    public int? ToAgentId { get; private set; }
    public DateTime? FromDueAtUtc { get; private set; }
    public DateTime? ToDueAtUtc { get; private set; }
    public string Reason { get; private set; } = string.Empty;
    public string EscalatedBy { get; private set; } = string.Empty;
    public DateTime EscalatedAtUtc { get; private set; }
}
