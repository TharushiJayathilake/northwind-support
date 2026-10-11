using SupportDesk.Application.Contracts.Agents;
using SupportDesk.Domain.Aggregates.Categories;

namespace SupportDesk.Application.Features.Tickets.Triage;

public sealed class TicketAssignmentPolicy
{
    public bool IsEligible(AgentDto agent, Category category)
    {
        ArgumentNullException.ThrowIfNull(agent);
        ArgumentNullException.ThrowIfNull(category);
        return GetIneligibility(agent, category) == Ineligibility.None;
    }

    public AssignmentDecision SelectAgent(Category category, IReadOnlyList<AgentDto> agents)
    {
        ArgumentNullException.ThrowIfNull(category);
        ArgumentNullException.ThrowIfNull(agents);

        var evaluated = agents.Select(agent => (Agent: agent, Failure: GetIneligibility(agent, category))).ToArray();
        var selected = evaluated.Where(candidate => candidate.Failure == Ineligibility.None)
            .Select(candidate => candidate.Agent)
            .OrderBy(agent => agent.OpenTicketCount)
            .ThenBy(agent => agent.Id)
            .FirstOrDefault();

        if (selected is not null)
            return new AssignmentDecision(selected.Id,
                $"Selected agent {selected.FullName} (ID {selected.Id}): lowest open-ticket count ({selected.OpenTicketCount}) among eligible agents, with ascending agent ID breaking ties.");

        var reason = evaluated.Length == 0
            ? "No agents are available."
            : evaluated.All(candidate => candidate.Failure == Ineligibility.Inactive)
                ? "All agents are inactive."
                : evaluated.All(candidate => candidate.Failure is Ineligibility.Inactive or Ineligibility.AtCapacity)
                    ? "All active agents are at or above their open-ticket capacity."
                    : $"No active agent with spare capacity has the required specialization for category {category.Id}.";
        return new AssignmentDecision(null, reason);
    }

    private static Ineligibility GetIneligibility(AgentDto agent, Category category)
    {
        if (!agent.IsActive) return Ineligibility.Inactive;
        if (agent.OpenTicketCount >= agent.MaxOpenTickets) return Ineligibility.AtCapacity;
        if (category.RequiresSpecialist && !agent.Specializations.Any(specialization => specialization.Id == category.Id))
            return Ineligibility.MissingSpecialization;
        return Ineligibility.None;
    }

    private enum Ineligibility { None, Inactive, AtCapacity, MissingSpecialization }
}

public sealed record AssignmentDecision(int? AssignedAgentId, string Reason);
