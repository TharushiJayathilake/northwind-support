using Microsoft.EntityFrameworkCore;
using System.Linq.Expressions;
using SupportDesk.Application.Abstractions;
using SupportDesk.Application.Contracts.Common;
using SupportDesk.Application.Contracts.Tickets;
using SupportDesk.Application.Features.Tickets.Triage;
using SupportDesk.Domain.Aggregates.Agents;
using SupportDesk.Domain.Aggregates.Categories;
using SupportDesk.Domain.Aggregates.Customers;
using SupportDesk.Domain.Aggregates.Tickets;
using SupportDesk.Infrastructure.Data;

namespace SupportDesk.Infrastructure.Queries;

/// <summary>
/// Ticket reads. Projected in the database and never tracked. Aggregates hold only each
/// other's ids, so the customer, category and agent labels are joined in here, on the read side.
/// </summary>
public sealed class TicketQueries(SupportDbContext db, IClock clock, SlaSettings settings) : ITicketQueries
{
    private readonly double atRiskThresholdPercent = ValidatedThreshold(settings);

    private static double ValidatedThreshold(SlaSettings settings)
    {
        settings.Validate();
        return settings.AtRiskThresholdPercent;
    }
    /// <remarks>
    /// Filters in the database before counting, sorting and paging the matching tickets.
    /// </remarks>
    public async Task<PagedResult<TicketListItemDto>> GetPagedAsync(TicketQuery query, CancellationToken ct)
    {
        var now = clock.UtcNow;
        var tickets = ApplySlaFilter(ApplyFilters(TicketsWithLabels(), query), query.SlaStatus, now);

        var totalCount = await tickets.CountAsync(ct);

        var items = await ApplySort(tickets, query)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(x => new TicketListItemDto
            {
                Id = x.Ticket.Id,
                Reference = x.Ticket.Reference,
                Title = x.Ticket.Title,
                Status = x.Ticket.Status,
                Priority = x.Ticket.Priority,
                Customer = new CustomerSummaryDto(x.Customer.Id, x.Customer.Name, x.Customer.Tier),
                Category = new CategorySummaryDto(x.Category.Id, x.Category.Name),
                AssignedAgent = x.Agent == null ? null : new AgentSummaryDto(x.Agent.Id, x.Agent.FullName),
                CreatedAtUtc = x.Ticket.CreatedAtUtc,
                UpdatedAtUtc = x.Ticket.UpdatedAtUtc,
                DueAtUtc = x.Ticket.DueAtUtc,
                ResolvedAtUtc = x.Ticket.ResolvedAtUtc,
                SlaStatus = SlaEvaluator.Evaluate(x.Ticket.DueAtUtc, x.Ticket.ResolvedAtUtc, now,
                    x.SlaStartsAtUtc, atRiskThresholdPercent, x.Ticket.Status)
            })
            .ToListAsync(ct);

        return new PagedResult<TicketListItemDto>(items, query.Page, query.PageSize, totalCount);
    }

    public Task<TicketDetailDto?> GetDetailAsync(int id, CancellationToken ct)
    {
        var now = clock.UtcNow;

        return TicketsWithLabels()
            .Where(x => x.Ticket.Id == id)
            .Select(x => new TicketDetailDto
            {
                Id = x.Ticket.Id,
                Reference = x.Ticket.Reference,
                Title = x.Ticket.Title,
                Description = x.Ticket.Description,
                Status = x.Ticket.Status,
                Priority = x.Ticket.Priority,
                Customer = new CustomerContactDto(
                    x.Customer.Id, x.Customer.Name, x.Customer.Email, x.Customer.Phone, x.Customer.Tier),
                Category = new CategoryDto(x.Category.Id, x.Category.Name, x.Category.RequiresSpecialist),
                AssignedAgent = x.Agent == null ? null : new AgentSummaryDto(x.Agent.Id, x.Agent.FullName),
                CreatedAtUtc = x.Ticket.CreatedAtUtc,
                UpdatedAtUtc = x.Ticket.UpdatedAtUtc,
                DueAtUtc = x.Ticket.DueAtUtc,
                ResolvedAtUtc = x.Ticket.ResolvedAtUtc,
                SlaStatus = SlaEvaluator.Evaluate(x.Ticket.DueAtUtc, x.Ticket.ResolvedAtUtc, now,
                    x.SlaStartsAtUtc, atRiskThresholdPercent, x.Ticket.Status)
            })
            .FirstOrDefaultAsync(ct);
    }

    public async Task<IReadOnlyList<TicketEscalationDto>?> GetEscalationsAsync(int ticketId, CancellationToken ct)
    {
        if (!await db.Set<Ticket>().AsNoTracking().AnyAsync(ticket => ticket.Id == ticketId, ct))
            return null;

        return await db.TicketEscalations.AsNoTracking()
            .Where(escalation => escalation.TicketId == ticketId)
            .OrderByDescending(escalation => escalation.EscalatedAtUtc)
            .ThenByDescending(escalation => escalation.Id)
            .Select(escalation => new TicketEscalationDto(escalation.Id, escalation.TicketId,
                escalation.FromPriority, escalation.ToPriority, escalation.FromAgentId, escalation.ToAgentId,
                escalation.FromDueAtUtc, escalation.ToDueAtUtc, escalation.Reason, escalation.EscalatedBy,
                escalation.EscalatedAtUtc))
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<TicketListItemDto>> GetForCustomerAsync(int customerId, CancellationToken ct)
    {
        var now = clock.UtcNow;

        return await TicketsWithLabels()
            .Where(x => x.Ticket.CustomerId == customerId)
            .OrderByDescending(x => x.Ticket.CreatedAtUtc)
            .ThenByDescending(x => x.Ticket.Id)
            .Select(x => new TicketListItemDto
            {
                Id = x.Ticket.Id,
                Reference = x.Ticket.Reference,
                Title = x.Ticket.Title,
                Status = x.Ticket.Status,
                Priority = x.Ticket.Priority,
                Customer = new CustomerSummaryDto(x.Customer.Id, x.Customer.Name, x.Customer.Tier),
                Category = new CategorySummaryDto(x.Category.Id, x.Category.Name),
                AssignedAgent = x.Agent == null ? null : new AgentSummaryDto(x.Agent.Id, x.Agent.FullName),
                CreatedAtUtc = x.Ticket.CreatedAtUtc,
                UpdatedAtUtc = x.Ticket.UpdatedAtUtc,
                DueAtUtc = x.Ticket.DueAtUtc,
                ResolvedAtUtc = x.Ticket.ResolvedAtUtc,
                SlaStatus = SlaEvaluator.Evaluate(x.Ticket.DueAtUtc, x.Ticket.ResolvedAtUtc, now,
                    x.SlaStartsAtUtc, atRiskThresholdPercent, x.Ticket.Status)
            })
            .ToListAsync(ct);
    }

    /// <summary>
    /// Every ticket with the rows that label it. Filter and sort this, then project.
    /// </summary>
    private IQueryable<TicketWithLabels> TicketsWithLabels() =>
        from ticket in db.Set<Ticket>().AsNoTracking()
        join customer in db.Set<Customer>() on ticket.CustomerId equals customer.Id
        join category in db.Set<Category>() on ticket.CategoryId equals category.Id
        from agent in db.Set<Agent>().Where(a => a.Id == ticket.AssignedAgentId).DefaultIfEmpty()
        select new TicketWithLabels
        {
            Ticket = ticket, Customer = customer, Category = category, Agent = agent,
            SlaStartsAtUtc = db.TicketEscalations.Where(escalation => escalation.TicketId == ticket.Id)
                .OrderByDescending(escalation => escalation.EscalatedAtUtc).ThenByDescending(escalation => escalation.Id)
                .Select(escalation => (DateTime?)escalation.EscalatedAtUtc).FirstOrDefault() ?? ticket.CreatedAtUtc
        };

    private static IQueryable<TicketWithLabels> ApplyFilters(IQueryable<TicketWithLabels> source, TicketQuery query)
    {
        if (query.Status is { } status)
        {
            source = source.Where(x => x.Ticket.Status == status);
        }

        if (query.Priority is { } priority)
        {
            source = source.Where(x => x.Ticket.Priority == priority);
        }

        if (query.CategoryId is { } categoryId)
        {
            source = source.Where(x => x.Ticket.CategoryId == categoryId);
        }

        if (query.CustomerId is { } customerId)
        {
            source = source.Where(x => x.Ticket.CustomerId == customerId);
        }

        if (query.AssignedAgentId is { } assignedAgentId)
        {
            source = source.Where(x => x.Ticket.AssignedAgentId == assignedAgentId);
        }

        if (query.UnassignedOnly)
        {
            source = source.Where(x => x.Ticket.AssignedAgentId == null);
        }

        var search = query.Search?.Trim();
        if (!string.IsNullOrEmpty(search))
        {
            source = source.Where(x =>
                x.Ticket.Title.Contains(search)
                || x.Ticket.Reference.Contains(search)
                || x.Customer.Name.Contains(search));
        }

        return source;
    }

    private IQueryable<TicketWithLabels> ApplySlaFilter(IQueryable<TicketWithLabels> source, SlaStatus? status, DateTime now)
    {
        if (status is null) return source;
        if (!Enum.IsDefined(status.Value)) throw new ArgumentOutOfRangeException(nameof(status));
        Expression<Func<TicketWithLabels, DateTime?>> due = x => x.Ticket.DueAtUtc;
        Expression<Func<TicketWithLabels, DateTime?>> resolved = x => x.Ticket.ResolvedAtUtc;
        Expression<Func<TicketWithLabels, TicketStatus>> ticketStatus = x => x.Ticket.Status;
        Expression<Func<TicketWithLabels, DateTime>> start = x => x.SlaStartsAtUtc;
        var row = due.Parameters[0];
        var deadline = Expression.Property(due.Body, nameof(Nullable<DateTime>.Value));
        var startBody = Replace(start, row);
        var window = SlaDurationExpressions.Between(startBody, deadline, db.Database.IsSqlServer());
        // Decompose the clock outside SQL date parameters, retaining its full tick precision.
        var remaining = Expression.Subtract(
            SlaDurationExpressions.Between(Expression.Constant(now.Date), deadline, db.Database.IsSqlServer()),
            Expression.Constant((double)now.TimeOfDay.Ticks));
        var rule = SlaEvaluator.Classification;
        Expression[] values = [due.Body, Replace(resolved, row), Replace(ticketStatus, row), window, remaining,
            Expression.Constant(atRiskThresholdPercent)];
        var replacements = rule.Parameters.Select((parameter, index) => (parameter, values[index]))
            .ToDictionary(pair => pair.parameter, pair => pair.Item2);
        var classification = new SlaDurationExpressions.Substitute(replacements).Visit(rule.Body)!;
        return source.Where(Expression.Lambda<Func<TicketWithLabels, bool>>(
            Expression.Equal(classification, Expression.Constant(status.Value)), row));
    }

    private static Expression Replace(LambdaExpression expression, ParameterExpression row) =>
        new SlaDurationExpressions.Substitute(new Dictionary<ParameterExpression, Expression>
            { [expression.Parameters[0]] = row }).Visit(expression.Body)!;

    private static IQueryable<TicketWithLabels> ApplySort(IQueryable<TicketWithLabels> source, TicketQuery query)
    {
        var descending = !string.Equals(query.SortDirection, "asc", StringComparison.OrdinalIgnoreCase);

        var sorted = query.SortBy?.ToLowerInvariant() switch
        {
            "updatedatutc" => descending
                ? source.OrderByDescending(x => x.Ticket.UpdatedAtUtc)
                : source.OrderBy(x => x.Ticket.UpdatedAtUtc),
            "dueatutc" => descending
                ? source.OrderByDescending(x => x.Ticket.DueAtUtc)
                : source.OrderBy(x => x.Ticket.DueAtUtc),
            "priority" => descending
                ? source.OrderByDescending(x => x.Ticket.Priority)
                : source.OrderBy(x => x.Ticket.Priority),
            "status" => descending
                ? source.OrderByDescending(x => x.Ticket.Status)
                : source.OrderBy(x => x.Ticket.Status),
            _ => descending
                ? source.OrderByDescending(x => x.Ticket.CreatedAtUtc)
                : source.OrderBy(x => x.Ticket.CreatedAtUtc)
        };

        // Keeps paging stable when the sort column has duplicates.
        return sorted.ThenByDescending(x => x.Ticket.Id);
    }

    private sealed class TicketWithLabels
    {
        public DateTime SlaStartsAtUtc { get; init; }
        public required Ticket Ticket { get; init; }

        public required Customer Customer { get; init; }

        public required Category Category { get; init; }

        public Agent? Agent { get; init; }
    }
}
