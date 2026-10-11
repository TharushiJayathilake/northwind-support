using SupportDesk.Application.Abstractions;
using SupportDesk.Application.Contracts.Tickets;
using SupportDesk.Application.Exceptions;
using SupportDesk.Application.Features.Tickets.Queries.GetTicketEscalations;
using SupportDesk.Domain.Aggregates.Tickets;
using SupportDesk.UnitTests.TestDoubles;

namespace SupportDesk.UnitTests.Application.Features.Tickets.Queries;

public sealed class GetTicketEscalationsQueryHandlerTests
{
    [Fact]
    public async Task HandleAsync_ReturnsHistoryAndForwardsIdAndCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        IReadOnlyList<TicketEscalationDto> records = [new(2, 7, TicketPriority.Low, TicketPriority.Medium,
            null, null, null, null, "Impact increased", "Team lead", FixedClock.DefaultNow)];
        var queries = new Mock<ITicketQueries>();
        queries.Setup(query => query.GetEscalationsAsync(7, cancellation.Token)).ReturnsAsync(records);
        var result = await new GetTicketEscalationsQueryHandler(queries.Object).HandleAsync(7, cancellation.Token);
        Assert.Same(records, result);
        queries.Verify(query => query.GetEscalationsAsync(7, cancellation.Token), Times.Once());
    }

    [Fact]
    public async Task HandleAsync_ExistingTicketWithEmptyHistoryReturnsEmptyCollection()
    {
        var queries = new Mock<ITicketQueries>();
        queries.Setup(query => query.GetEscalationsAsync(7, It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<TicketEscalationDto>());
        Assert.Empty(await new GetTicketEscalationsQueryHandler(queries.Object).HandleAsync(7, CancellationToken.None));
    }

    [Fact]
    public async Task HandleAsync_MissingTicketThrowsNotFound()
    {
        var queries = new Mock<ITicketQueries>();
        queries.Setup(query => query.GetEscalationsAsync(7, It.IsAny<CancellationToken>())).ReturnsAsync((IReadOnlyList<TicketEscalationDto>?)null);
        var error = await Assert.ThrowsAsync<NotFoundException>(() => new GetTicketEscalationsQueryHandler(queries.Object).HandleAsync(7, CancellationToken.None));
        Assert.Contains("Ticket", error.Message);
    }
}
