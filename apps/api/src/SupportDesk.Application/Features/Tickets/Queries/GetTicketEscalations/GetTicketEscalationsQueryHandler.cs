using SupportDesk.Application.Abstractions;
using SupportDesk.Application.Contracts.Tickets;
using SupportDesk.Application.Exceptions;

namespace SupportDesk.Application.Features.Tickets.Queries.GetTicketEscalations;

public sealed class GetTicketEscalationsQueryHandler(ITicketQueries tickets)
{
    public async Task<IReadOnlyList<TicketEscalationDto>> HandleAsync(int id, CancellationToken ct) =>
        await tickets.GetEscalationsAsync(id, ct) ?? throw new NotFoundException("Ticket", id);
}
