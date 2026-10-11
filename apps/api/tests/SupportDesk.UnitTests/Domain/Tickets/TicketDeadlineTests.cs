using SupportDesk.UnitTests.TestDoubles;

namespace SupportDesk.UnitTests.Domain.Tickets;

public sealed class TicketDeadlineTests
{
    [Fact]
    public void SetDueDate_AppliesDeadlineAndUpdateTime()
    {
        var ticket = new TicketBuilder().Build();
        ticket.SetDueDate(FixedClock.DefaultNow.AddHours(2), FixedClock.DefaultNow);
        Assert.Equal(FixedClock.DefaultNow.AddHours(2), ticket.DueAtUtc);
        Assert.Equal(FixedClock.DefaultNow, ticket.UpdatedAtUtc);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void SetDueDate_RejectsNonFutureDeadline(int hours)
    {
        var ticket = new TicketBuilder().Build();
        Assert.Throws<ArgumentOutOfRangeException>(() => ticket.SetDueDate(FixedClock.DefaultNow.AddHours(hours), FixedClock.DefaultNow));
        Assert.Null(ticket.DueAtUtc);
    }

    [Fact]
    public void SetDueDate_RejectsNonUtcDeadline()
    {
        var ticket = new TicketBuilder().Build();
        Assert.Throws<ArgumentException>(() => ticket.SetDueDate(DateTime.SpecifyKind(FixedClock.DefaultNow.AddHours(2), DateTimeKind.Unspecified), FixedClock.DefaultNow));
        Assert.Null(ticket.DueAtUtc);
    }
}
