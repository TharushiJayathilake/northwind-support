using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;

namespace SupportDesk.Infrastructure.Queries;

internal static class SlaDurationExpressions
{
    public static Expression Between(Expression start, Expression end, bool sqlServer)
    {
        // Day boundaries plus time-of-day parts avoid DATEDIFF millisecond/int overflow
        // and subtraction of large, imprecise Julian timestamps in SQLite.
        Expression<Func<DateTime, DateTime, double>> duration = sqlServer
            ? (from, to) => EF.Functions.DateDiffDay(from.Date, to.Date) * 864000000000d
                + (to.Hour - from.Hour) * 36000000000d + (to.Minute - from.Minute) * 600000000d
                + (to.Second - from.Second) * 10000000d + (to.Millisecond - from.Millisecond) * 10000d
                + (to.Microsecond - from.Microsecond) * 10d + (to.Nanosecond - from.Nanosecond) / 100d
            : (from, to) => (double)(to.Date.Ticks - from.Date.Ticks)
                + (to.Hour - from.Hour) * 36000000000d + (to.Minute - from.Minute) * 600000000d
                + (to.Second - from.Second) * 10000000d + (to.Millisecond - from.Millisecond) * 10000d;
        return new Substitute(new Dictionary<ParameterExpression, Expression>
            { [duration.Parameters[0]] = start, [duration.Parameters[1]] = end }).Visit(duration.Body)!;
    }

    internal sealed class Substitute(IReadOnlyDictionary<ParameterExpression, Expression> replacements) : ExpressionVisitor
    {
        protected override Expression VisitParameter(ParameterExpression node) =>
            replacements.TryGetValue(node, out var replacement) ? replacement : base.VisitParameter(node);
    }
}
