using SupportDesk.Domain.Aggregates.Tickets;

namespace SupportDesk.Application.Contracts.Tickets;

/// <summary>Body of POST /api/tickets.</summary>
public sealed record CreateTicketRequest(
    string Title,
    string Description,
    int CustomerId,
    int CategoryId,
    TicketPriority? RequestedPriority);

/// <summary>Body of PATCH /api/tickets/{id}/status.</summary>
public sealed record UpdateTicketStatusRequest(TicketStatus Status);

/// <summary>Body of PATCH /api/tickets/{id}/assignment. A null agent unassigns the ticket.</summary>
public sealed record AssignTicketRequest(int? AgentId);

/// <summary>Body of POST /api/tickets/{id}/escalate. Identity comes from authentication.</summary>
public sealed record EscalateTicketRequest(string Reason);

/// <summary>Internal command; actor is supplied by the authenticated HTTP boundary.</summary>
public sealed record EscalateTicketCommand(string Reason, string EscalatedBy);
