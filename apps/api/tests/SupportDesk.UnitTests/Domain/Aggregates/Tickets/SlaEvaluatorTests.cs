using SupportDesk.Domain.Aggregates.Tickets;
using SupportDesk.UnitTests.TestDoubles;

namespace SupportDesk.UnitTests.Domain.Aggregates.Tickets;

public class SlaEvaluatorTests
{
    private static readonly DateTime Now = FixedClock.DefaultNow;

    [Fact]
    public void Evaluate_WithoutADueDate_IsNotApplicable() =>
        Assert.Equal(SlaStatus.NotApplicable, Evaluate(null, null, Now));

    [Fact]
    public void Evaluate_ResolvedOnTime_IsMet() =>
        Assert.Equal(SlaStatus.Met, Evaluate(Now, Now, Now.AddDays(1)));

    [Fact]
    public void Evaluate_ResolvedLate_IsBreached() =>
        Assert.Equal(SlaStatus.Breached, Evaluate(Now, Now.AddMinutes(1), Now));

    [Fact]
    public void Evaluate_OpenAndPastDue_IsBreached() =>
        Assert.Equal(SlaStatus.Breached, Evaluate(Now.AddMinutes(-1), null, Now));

    [Fact]
    public void Evaluate_OpenAndBeforeDue_IsWithinSla() =>
        Assert.Equal(SlaStatus.WithinSla, Evaluate(Now.AddHours(1), null, Now));

    private static SlaStatus Evaluate(DateTime? due, DateTime? resolved, DateTime now) =>
        SlaEvaluator.Evaluate(due, resolved, now, Now, 25, TicketStatus.Open);

    [Theory]
    [InlineData(-1, SlaStatus.WithinSla)]
    [InlineData(0, SlaStatus.AtRisk)]
    [InlineData(1, SlaStatus.AtRisk)]
    public void Evaluate_AtRiskThresholdHasExactTickBoundary(long offsetTicks, SlaStatus expected)
    {
        var clock = new FixedClock(Now.AddHours(3).AddTicks(offsetTicks));
        Assert.Equal(expected, SlaEvaluator.Evaluate(Now.AddHours(4), null, clock.UtcNow, Now, 25, TicketStatus.Open));
    }

    [Theory]
    [InlineData(0, SlaStatus.AtRisk)]
    [InlineData(1, SlaStatus.Breached)]
    public void Evaluate_DeadlineBoundary(long offsetTicks, SlaStatus expected) =>
        Assert.Equal(expected, SlaEvaluator.Evaluate(Now.AddHours(4), null, Now.AddHours(4).AddTicks(offsetTicks), Now, 25, TicketStatus.New));

    [Theory]
    [InlineData(-1, SlaStatus.Met)]
    [InlineData(0, SlaStatus.Met)]
    [InlineData(1, SlaStatus.Breached)]
    public void Evaluate_ResolutionBoundary(long offsetTicks, SlaStatus expected) =>
        Assert.Equal(expected, SlaEvaluator.Evaluate(Now.AddHours(4), Now.AddHours(4).AddTicks(offsetTicks), Now.AddDays(1), Now, 25, TicketStatus.Resolved));

    [Theory]
    [InlineData(TicketStatus.Closed)]
    [InlineData(TicketStatus.Resolved)]
    public void Evaluate_TerminalTicketWithoutResolution_IsNotApplicable(TicketStatus status) =>
        Assert.Equal(SlaStatus.NotApplicable, SlaEvaluator.Evaluate(Now.AddHours(4), null, Now.AddDays(1), Now, 25, status));

    [Theory]
    [InlineData(-1, SlaStatus.Met)]
    [InlineData(1, SlaStatus.Breached)]
    public void Evaluate_ClosedTicketUsesPreservedResolution(long offsetTicks, SlaStatus expected) =>
        Assert.Equal(expected, SlaEvaluator.Evaluate(Now.AddHours(4), Now.AddHours(4).AddTicks(offsetTicks), Now.AddDays(1), Now, 25, TicketStatus.Closed));

    [Fact]
    public void Evaluate_RejectsNonUtcTime() =>
        Assert.Throws<ArgumentException>(() => Evaluate(Now.AddHours(4), null, DateTime.SpecifyKind(Now, DateTimeKind.Local)));
}
