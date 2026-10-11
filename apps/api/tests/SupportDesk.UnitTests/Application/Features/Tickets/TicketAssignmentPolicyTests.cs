using SupportDesk.Application.Contracts.Agents;
using SupportDesk.Application.Contracts.Tickets;
using SupportDesk.Application.Features.Tickets.Triage;
using SupportDesk.Domain.Aggregates.Categories;

namespace SupportDesk.UnitTests.Application.Features.Tickets;

public sealed class TicketAssignmentPolicyTests
{
    private readonly TicketAssignmentPolicy policy = new();
    private readonly Category general = new("Arbitrary general category", false, false);
    private readonly Category specialist = new("Arbitrary specialist category", true, false);

    [Fact]
    public void SelectAgent_SelectsActiveAgentWithSpareCapacity()
    {
        var result = policy.SelectAgent(general, [Agent(7)]);
        Assert.Equal(7, result.AssignedAgentId);
        Assert.Contains("Selected agent", result.Reason);
    }

    [Fact]
    public void SelectAgent_ExcludesInactiveAgentEvenWithLowerWorkload()
    {
        Assert.Equal(2, policy.SelectAgent(general, [Agent(1, active: false), Agent(2, open: 1)]).AssignedAgentId);
    }

    [Theory]
    [InlineData(3)]
    [InlineData(4)]
    public void SelectAgent_ExcludesAgentsAtOrAboveCapacity(int open)
    {
        Assert.Null(policy.SelectAgent(general, [Agent(1, open: open, capacity: 3)]).AssignedAgentId);
        Assert.False(policy.IsEligible(Agent(1, open: open, capacity: 3), general));
    }

    [Fact]
    public void SelectAgent_SpecialistCategoryMatchesCategoryIdNotName()
    {
        var wrong = Agent(1, specializations: [new CategorySummaryDto(specialist.Id + 1, specialist.Name)]);
        var matching = Agent(2, open: 1, specializations: [new CategorySummaryDto(specialist.Id, "Different display name")]);
        Assert.Equal(2, policy.SelectAgent(specialist, [wrong, matching]).AssignedAgentId);
        Assert.True(policy.IsEligible(matching, specialist));
        Assert.False(policy.IsEligible(wrong, specialist));
    }

    [Fact]
    public void SelectAgent_GeneralCategoryDoesNotRequireSpecialization()
    {
        Assert.Equal(1, policy.SelectAgent(general, [Agent(1), Agent(2, open: 1)]).AssignedAgentId);
    }

    [Fact]
    public void SelectAgent_FewestOpenTicketsWinsNotLargestRemainingCapacity()
    {
        Assert.Equal(8, policy.SelectAgent(general,
            [Agent(1, open: 4, capacity: 100), Agent(8, open: 1, capacity: 2)]).AssignedAgentId);
    }

    [Fact]
    public void SelectAgent_TiesUseAscendingIdRegardlessOfInputOrder()
    {
        AgentDto[] agents = [Agent(9, open: 1), Agent(2, open: 1), Agent(5, open: 1)];
        for (var attempt = 0; attempt < 3; attempt++)
        {
            Assert.Equal(2, policy.SelectAgent(general, agents).AssignedAgentId);
            Assert.Equal(2, policy.SelectAgent(general, agents.Reverse().ToArray()).AssignedAgentId);
        }
    }

    [Fact]
    public void SelectAgent_NoAgentsReturnsUnassignedWithReason()
    {
        var result = policy.SelectAgent(general, []);
        Assert.Null(result.AssignedAgentId);
        Assert.Equal("No agents are available.", result.Reason);
    }

    [Fact]
    public void SelectAgent_AllInactiveReturnsUnassignedWithReason()
    {
        var result = policy.SelectAgent(general, [Agent(1, active: false), Agent(2, active: false)]);
        Assert.Null(result.AssignedAgentId);
        Assert.Equal("All agents are inactive.", result.Reason);
    }

    [Fact]
    public void SelectAgent_AllActiveAgentsFullReturnsUnassignedWithReason()
    {
        var result = policy.SelectAgent(general, [Agent(1, open: 3), Agent(2, open: 4), Agent(3, active: false)]);
        Assert.Null(result.AssignedAgentId);
        Assert.Contains("capacity", result.Reason);
    }

    [Fact]
    public void SelectAgent_NoMatchingAvailableSpecialistReturnsUnassignedWithReason()
    {
        var result = policy.SelectAgent(specialist,
            [Agent(1), Agent(2, active: false, specializations: [new CategorySummaryDto(specialist.Id, specialist.Name)]),
             Agent(3, open: 3, specializations: [new CategorySummaryDto(specialist.Id, specialist.Name)])]);
        Assert.Null(result.AssignedAgentId);
        Assert.Contains("required specialization", result.Reason);
    }

    [Fact]
    public void SelectAgent_InvalidInputsAreNotReportedAsNormalUnavailability()
    {
        Assert.Throws<ArgumentNullException>(() => policy.SelectAgent(general, null!));
        Assert.Throws<ArgumentNullException>(() => policy.SelectAgent(null!, []));
    }

    private static AgentDto Agent(int id, int open = 0, int capacity = 3, bool active = true,
        IReadOnlyList<CategorySummaryDto>? specializations = null) =>
        new(id, $"Agent {id}", $"agent{id}@example.test", active, capacity, open, specializations ?? []);

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    public void Escalation_RetainsCurrentAgentIncludingExactCapacity(int open)
    {
        var result = policy.SelectAgentForEscalation(general, [Agent(9, open: open), Agent(1)], 9);
        Assert.Equal(9, result.AssignedAgentId);
        Assert.Contains("Retained", result.Reason);
    }

    [Theory]
    [InlineData(false, 1)]
    [InlineData(true, 4)]
    public void Escalation_ReplacesInactiveOrOverCapacityIncumbent(bool active, int open)
    {
        var result = policy.SelectAgentForEscalation(general, [Agent(1, open: open, active: active), Agent(2)], 1);
        Assert.Equal(2, result.AssignedAgentId);
    }

    [Fact]
    public void Escalation_ReplacesIncumbentMissingRequiredSpecialization()
    {
        var result = policy.SelectAgentForEscalation(specialist,
            [Agent(1), Agent(2, specializations: [new CategorySummaryDto(specialist.Id, specialist.Name)])], 1);
        Assert.Equal(2, result.AssignedAgentId);
    }

    [Fact]
    public void Escalation_SelectsOwnerForUnassignedTicket()
    {
        Assert.Equal(1, policy.SelectAgentForEscalation(general, [Agent(2), Agent(1)], null).AssignedAgentId);
    }

    [Fact]
    public void Escalation_NoEligibleReplacementReturnsUnassigned()
    {
        Assert.Null(policy.SelectAgentForEscalation(general, [Agent(1, active: false)], 1).AssignedAgentId);
    }
}
