using SupportDesk.Application.Contracts.Common;
using SupportDesk.Application.Contracts.Tickets;

namespace SupportDesk.Application.Abstractions;

/// <summary>
/// The read side of tickets. Projects straight to DTOs in the database; never tracks.
/// </summary>
public interface ITicketQueries
{
    /// <param name="query">Filters, sorting and paging, already normalized.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<PagedResult<TicketListItemDto>> GetPagedAsync(TicketQuery query, CancellationToken ct);

    Task<TicketDetailDto?> GetDetailAsync(int id, CancellationToken ct);

    /// <summary>Newest audit records first; null if the ticket does not exist, empty if it has no history.</summary>
    Task<IReadOnlyList<TicketEscalationDto>?> GetEscalationsAsync(int ticketId, CancellationToken ct);

    /// <summary>A customer's tickets, newest first.</summary>
    Task<IReadOnlyList<TicketListItemDto>> GetForCustomerAsync(int customerId, CancellationToken ct);
}
