using SupportDesk.Domain.Aggregates.Tickets;
using SupportDesk.UnitTests.TestDoubles;

namespace SupportDesk.UnitTests.Domain.Aggregates.Tickets;

public sealed class TicketEscalationTests
{
    [Fact]
    public void Constructor_CapturesSnapshotWithoutChangingTicket()
    {
        var ticket = new TicketBuilder().WithPriority(TicketPriority.Low).Build();
        var escalation = Create(reason: "  Customer impact increased.  ", actor: "  Team lead  ");
        Assert.Equal(1, escalation.TicketId);
        Assert.Equal(TicketPriority.Low, escalation.FromPriority);
        Assert.Equal(TicketPriority.Medium, escalation.ToPriority);
        Assert.Equal(2, escalation.FromAgentId);
        Assert.Equal(3, escalation.ToAgentId);
        Assert.Equal(FixedClock.DefaultNow.AddHours(72), escalation.FromDueAtUtc);
        Assert.Equal(FixedClock.DefaultNow.AddHours(24), escalation.ToDueAtUtc);
        Assert.Equal("Customer impact increased.", escalation.Reason);
        Assert.Equal("Team lead", escalation.EscalatedBy);
        Assert.Equal(FixedClock.DefaultNow, escalation.EscalatedAtUtc);
        Assert.Equal(TicketPriority.Low, ticket.Priority);
        Assert.Equal(0, escalation.Id);
    }

    [Fact]
    public void Constructor_AllowsNullableHistoryFields()
    {
        var escalation = new TicketEscalation(1, TicketPriority.Low, TicketPriority.Medium,
            null, null, null, null, "Impact increased", "Team lead", FixedClock.DefaultNow);
        Assert.Null(escalation.FromAgentId);
        Assert.Null(escalation.ToAgentId);
        Assert.Null(escalation.FromDueAtUtc);
        Assert.Null(escalation.ToDueAtUtc);
    }

    [Theory]
    [InlineData(500, 100)]
    [InlineData(1, 1)]
    public void Constructor_AcceptsStringLengthBoundaries(int reasonLength, int actorLength)
    {
        var escalation = Create(new string('r', reasonLength), new string('a', actorLength));
        Assert.Equal(reasonLength, escalation.Reason.Length);
        Assert.Equal(actorLength, escalation.EscalatedBy.Length);
    }

    [Theory]
    [InlineData("", "Actor")]
    [InlineData("   ", "Actor")]
    [InlineData("Reason", "")]
    [InlineData("Reason", "   ")]
    public void Constructor_RejectsBlankStrings(string reason, string actor) =>
        Assert.Throws<ArgumentException>(() => Create(reason, actor));

    [Theory]
    [InlineData(501, 100)]
    [InlineData(500, 101)]
    public void Constructor_RejectsOversizedStrings(int reasonLength, int actorLength) =>
        Assert.Throws<ArgumentException>(() => Create(new string('r', reasonLength), new string('a', actorLength)));

    [Theory]
    [InlineData("ticket")]
    [InlineData("fromPriority")]
    [InlineData("toPriority")]
    [InlineData("fromAgent")]
    [InlineData("toAgent")]
    public void Constructor_RejectsInvalidIdsOrEnums(string invalid) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new TicketEscalation(
            invalid == "ticket" ? 0 : 1,
            invalid == "fromPriority" ? (TicketPriority)0 : TicketPriority.Low,
            invalid == "toPriority" ? (TicketPriority)99 : TicketPriority.Medium,
            invalid == "fromAgent" ? 0 : 2, invalid == "toAgent" ? -1 : 3,
            null, null, "Impact increased", "Team lead", FixedClock.DefaultNow));

    [Theory]
    [InlineData("escalated")]
    [InlineData("fromDue")]
    [InlineData("toDue")]
    public void Constructor_RejectsNonUtcTimestamps(string field)
    {
        var unspecified = DateTime.SpecifyKind(FixedClock.DefaultNow, DateTimeKind.Unspecified);
        Assert.Throws<ArgumentException>(() => new TicketEscalation(1, TicketPriority.Low, TicketPriority.Medium,
            null, null, field == "fromDue" ? unspecified : null, field == "toDue" ? unspecified : null,
            "Impact increased", "Team lead", field == "escalated" ? unspecified : FixedClock.DefaultNow));
    }

    [Fact]
    public void Entity_HasNoPublicPropertySetters()
    {
        Assert.All(typeof(TicketEscalation).GetProperties(), property =>
            Assert.False(property.GetSetMethod(nonPublic: true)?.IsPublic ?? false));
    }

    private static TicketEscalation Create(string reason = "Impact increased", string actor = "Team lead") =>
        new(1, TicketPriority.Low, TicketPriority.Medium, 2, 3,
            FixedClock.DefaultNow.AddHours(72), FixedClock.DefaultNow.AddHours(24), reason, actor, FixedClock.DefaultNow);
}
