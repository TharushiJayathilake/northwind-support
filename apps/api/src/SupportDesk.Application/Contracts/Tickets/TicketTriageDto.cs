namespace SupportDesk.Application.Contracts.Tickets;

public sealed record TicketTriageDto(string PriorityReason, string SlaReason, string AssignmentReason);
