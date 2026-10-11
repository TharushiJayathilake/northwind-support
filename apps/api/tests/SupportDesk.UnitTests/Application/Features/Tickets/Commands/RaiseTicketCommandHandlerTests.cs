using SupportDesk.Application.Contracts.Tickets;
using SupportDesk.Application.Exceptions;
using SupportDesk.Domain.Aggregates.Tickets;
using SupportDesk.UnitTests.TestDoubles;

namespace SupportDesk.UnitTests.Application.Features.Tickets.Commands;

public class RaiseTicketCommandHandlerTests
{
    private static CreateTicketRequest Request(TicketPriority? priority = null) =>
        new("Cannot log in to the portal", "Happens for every user since this morning.", 1, 3, priority);

    [Fact]
    public async Task HandleAsync_StampsReferenceStatusAndTimestamps()
    {
        var context = new TicketCommandTestContext();

        await context.RaiseTicket.HandleAsync(Request(TicketPriority.High), CancellationToken.None);

        var created = context.AddedTicket;
        Assert.NotNull(created);
        Assert.Equal("TCK-0041", created.Reference);
        Assert.Equal(TicketStatus.New, created.Status);
        Assert.Equal(TicketPriority.High, created.Priority);
        Assert.Equal(FixedClock.DefaultNow, created.CreatedAtUtc);
        Assert.Equal(FixedClock.DefaultNow, created.UpdatedAtUtc);
        Assert.Equal(FixedClock.DefaultNow.AddHours(8), created.DueAtUtc);
        context.Customers.Verify(c => c.GetByIdAsync(1, It.IsAny<CancellationToken>()), Times.Once());
        context.Categories.Verify(c => c.GetByIdAsync(3, It.IsAny<CancellationToken>()), Times.Once());
        context.Customers.Verify(c => c.ExistsAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never());
        context.Categories.Verify(c => c.ExistsAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never());
        context.VerifySaved(Times.Once());
    }

    [Fact]
    public async Task HandleAsync_WithoutRequestedPriority_FallsBackToMedium()
    {
        var context = new TicketCommandTestContext();

        await context.RaiseTicket.HandleAsync(Request(), CancellationToken.None);

        Assert.NotNull(context.AddedTicket);
        Assert.Equal(TicketPriority.Medium, context.AddedTicket.Priority);
    }

    [Fact]
    public async Task HandleAsync_WhenCustomerDoesNotExist_Throws()
    {
        var context = new TicketCommandTestContext();
        context.Customers
            .Setup(c => c.GetByIdAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((SupportDesk.Domain.Aggregates.Customers.Customer?)null);

        var exception = await Assert.ThrowsAsync<NotFoundException>(
            () => context.RaiseTicket.HandleAsync(Request(), CancellationToken.None));

        Assert.Contains("Customer", exception.Message);
        context.VerifySaved(Times.Never());
    }

    [Fact]
    public async Task HandleAsync_WhenCategoryDoesNotExist_ThrowsWithoutSaving()
    {
        var context = new TicketCommandTestContext();
        context.Categories.Setup(c => c.GetByIdAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((SupportDesk.Domain.Aggregates.Categories.Category?)null);
        var error = await Assert.ThrowsAsync<NotFoundException>(() => context.RaiseTicket.HandleAsync(Request(), CancellationToken.None));
        Assert.Contains("Category", error.Message);
        context.VerifySaved(Times.Never());
    }

    [Fact]
    public async Task HandleAsync_AgentQueryFailureDoesNotCreateUnassignedTicket()
    {
        var context = new TicketCommandTestContext();
        context.AgentQueries.Setup(a => a.GetAllAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Database unavailable"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => context.RaiseTicket.HandleAsync(Request(), CancellationToken.None));
        Assert.Null(context.AddedTicket);
        context.VerifySaved(Times.Never());
    }

    [Fact]
    public async Task HandleAsync_CapturesCreationClockOnce()
    {
        var clock = new Mock<SupportDesk.Application.Abstractions.IClock>();
        clock.SetupGet(c => c.UtcNow).Returns(FixedClock.DefaultNow);
        var context = new TicketCommandTestContext(clock.Object);
        await context.RaiseTicket.HandleAsync(Request(), CancellationToken.None);
        clock.VerifyGet(c => c.UtcNow, Times.Once());
        Assert.Equal(FixedClock.DefaultNow, context.AddedTicket!.CreatedAtUtc);
        Assert.Equal(FixedClock.DefaultNow.AddHours(24), context.AddedTicket.DueAtUtc);
    }

    [Fact]
    public async Task HandleAsync_SaveFailurePropagatesWithoutReturningSuccess()
    {
        var context = new TicketCommandTestContext();
        context.UnitOfWork.Setup(unit => unit.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Database save failed"));
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            context.RaiseTicket.HandleAsync(Request(), CancellationToken.None));
        Assert.Equal("Database save failed", error.Message);
        context.TicketQueries.Verify(query => query.GetDetailAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never());
    }
}
