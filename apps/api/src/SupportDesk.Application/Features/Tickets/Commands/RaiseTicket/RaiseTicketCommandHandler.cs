using Microsoft.Extensions.Logging;
using SupportDesk.Application.Abstractions;
using SupportDesk.Application.Contracts.Tickets;
using SupportDesk.Application.Exceptions;
using SupportDesk.Application.Features.Tickets.Queries.GetTicket;
using SupportDesk.Application.Features.Tickets.Triage;
using SupportDesk.Domain.Aggregates.Tickets;
using SupportDesk.Domain.Repositories;

namespace SupportDesk.Application.Features.Tickets.Commands.RaiseTicket;

/// <summary>
/// Raises a ticket for a customer.
/// </summary>
public sealed class RaiseTicketCommandHandler(
    ITicketRepository tickets,
    ICustomerRepository customers,
    ICategoryRepository categories,
    IAgentQueries agents,
    TicketTriagePolicy triagePolicy,
    TicketAssignmentPolicy assignmentPolicy,
    IUnitOfWork unitOfWork,
    IClock clock,
    GetTicketQueryHandler getTicket,
    ILogger<RaiseTicketCommandHandler> logger)
{
    /// <exception cref="NotFoundException">The customer or the category does not exist.</exception>
    public async Task<TicketDetailDto> HandleAsync(CreateTicketRequest request, CancellationToken ct)
    {
        var customer = await customers.GetByIdAsync(request.CustomerId, ct)
            ?? throw new NotFoundException("Customer", request.CustomerId);
        var category = await categories.GetByIdAsync(request.CategoryId, ct)
            ?? throw new NotFoundException("Category", request.CategoryId);
        var nowUtc = clock.UtcNow;
        var triage = triagePolicy.Calculate(request.RequestedPriority, category, customer.Tier, nowUtc);
        var assignment = assignmentPolicy.SelectAgent(category, await agents.GetAllAsync(ct));

        var ticket = Ticket.Raise(
            await tickets.NextReferenceAsync(ct),
            request.Title,
            request.Description,
            request.CustomerId,
            request.CategoryId,
            triage.Priority,
            nowUtc);
        ticket.SetDueDate(triage.DueAtUtc, nowUtc);
        ticket.AssignTo(assignment.AssignedAgentId, nowUtc);

        await tickets.AddAsync(ticket, ct);
        await unitOfWork.SaveChangesAsync(ct);

        logger.LogInformation(
            "Ticket {Reference} raised for customer {CustomerId} in category {CategoryId}.",
            ticket.Reference, ticket.CustomerId, ticket.CategoryId);

        var result = await getTicket.HandleAsync(ticket.Id, ct);
        result.Triage = new TicketTriageDto(triage.PriorityReason, triage.SlaReason, assignment.Reason);
        return result;
    }
}
