using SupportDesk.Application.Abstractions;
using SupportDesk.Application.Contracts.Tickets;
using SupportDesk.Application.Exceptions;
using SupportDesk.Application.Features.Tickets.Queries.GetTicket;
using SupportDesk.Application.Features.Tickets.Triage;
using SupportDesk.Domain.Aggregates.Tickets;
using SupportDesk.Domain.Repositories;

namespace SupportDesk.Application.Features.Tickets.Commands.EscalateTicket;

public sealed class EscalateTicketCommandHandler(
    ITicketRepository tickets, ICustomerRepository customers, ICategoryRepository categories,
    IAgentQueries agents, TicketTriagePolicy triagePolicy, TicketAssignmentPolicy assignmentPolicy,
    IUnitOfWork unitOfWork, IClock clock, GetTicketQueryHandler getTicket)
{
    public async Task<EscalateTicketResponse> HandleAsync(int id, EscalateTicketCommand request, CancellationToken ct)
    {
        var ticket = await tickets.GetByIdAsync(id, ct) ?? throw new NotFoundException("Ticket", id);
        var nextPriority = ticket.GetEscalatedPriority();
        var customer = await customers.GetByIdAsync(ticket.CustomerId, ct)
            ?? throw new NotFoundException("Customer", ticket.CustomerId);
        var category = await categories.GetByIdAsync(ticket.CategoryId, ct)
            ?? throw new NotFoundException("Category", ticket.CategoryId);
        var nowUtc = clock.UtcNow;
        var deadline = triagePolicy.CalculateDeadline(nextPriority, customer.Tier, nowUtc);
        var assignment = assignmentPolicy.SelectAgentForEscalation(category, await agents.GetAllAsync(ct), ticket.AssignedAgentId);
        var escalation = new TicketEscalation(ticket.Id, ticket.Priority, nextPriority,
            ticket.AssignedAgentId, assignment.AssignedAgentId, ticket.DueAtUtc, deadline.DueAtUtc,
            request.Reason, request.EscalatedBy, nowUtc);

        ticket.Escalate(deadline.DueAtUtc, assignment.AssignedAgentId, nowUtc);
        await tickets.AddEscalationAsync(escalation, ct);
        await unitOfWork.SaveChangesAsync(ct);

        var updated = await getTicket.HandleAsync(id, ct);
        return new EscalateTicketResponse(updated, new TicketEscalationDto(escalation.Id, escalation.TicketId,
            escalation.FromPriority, escalation.ToPriority, escalation.FromAgentId, escalation.ToAgentId,
            escalation.FromDueAtUtc, escalation.ToDueAtUtc, escalation.Reason, escalation.EscalatedBy, escalation.EscalatedAtUtc));
    }
}
