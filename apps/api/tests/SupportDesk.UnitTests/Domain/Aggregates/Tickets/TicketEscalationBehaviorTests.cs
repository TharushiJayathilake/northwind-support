using SupportDesk.Domain.Aggregates.Tickets;
using SupportDesk.Domain.Exceptions;
using SupportDesk.UnitTests.TestDoubles;

namespace SupportDesk.UnitTests.Domain.Aggregates.Tickets;

public sealed class TicketEscalationBehaviorTests
{
    [Theory]
    [InlineData(TicketPriority.Low, TicketPriority.Medium)]
    [InlineData(TicketPriority.Medium, TicketPriority.High)]
    [InlineData(TicketPriority.High, TicketPriority.Critical)]
    public void Escalate_IncreasesExactlyOneLevel(TicketPriority from, TicketPriority to)
    {
        var ticket = new TicketBuilder().WithPriority(from).Build();
        Assert.Equal(to, ticket.GetEscalatedPriority());
        Assert.Equal(from, ticket.Priority);
        ticket.Escalate(FixedClock.DefaultNow.AddHours(4), 7, FixedClock.DefaultNow);
        Assert.Equal(to, ticket.Priority);
        Assert.Equal(7, ticket.AssignedAgentId);
        Assert.Equal(FixedClock.DefaultNow.AddHours(4), ticket.DueAtUtc);
    }

    [Theory]
    [InlineData(TicketPriority.Critical, TicketStatus.New)]
    [InlineData(TicketPriority.Low, TicketStatus.Resolved)]
    [InlineData(TicketPriority.Low, TicketStatus.Closed)]
    public void Escalate_RejectsForbiddenStatesWithoutMutation(TicketPriority priority, TicketStatus status)
    {
        var ticket = new TicketBuilder().WithPriority(priority).WithStatus(status).AssignedTo(2).Build();
        Assert.Throws<BusinessRuleViolationException>(() => ticket.Escalate(FixedClock.DefaultNow.AddHours(4), 3, FixedClock.DefaultNow));
        Assert.Equal(priority, ticket.Priority);
        Assert.Equal(status, ticket.Status);
        Assert.Equal(2, ticket.AssignedAgentId);
        Assert.Null(ticket.DueAtUtc);
    }

    [Fact]
    public void Escalate_InvalidDeadlineDoesNotPartiallyMutateTicket()
    {
        var ticket = new TicketBuilder().WithPriority(TicketPriority.Low).AssignedTo(2).Build();
        Assert.Throws<ArgumentOutOfRangeException>(() => ticket.Escalate(FixedClock.DefaultNow, 3, FixedClock.DefaultNow));
        Assert.Equal(TicketPriority.Low, ticket.Priority);
        Assert.Equal(2, ticket.AssignedAgentId);
        Assert.Null(ticket.DueAtUtc);
    }
}
