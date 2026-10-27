using System.Linq.Expressions;

namespace Plaat.Db.Expressions;

/// <summary>
/// Combines independently-built <see cref="Expression{TDelegate}"/> predicates into a single
/// expression tree with one shared parameter, so EF Core can translate the combined predicate to
/// SQL. Plain LINQ can only chain implicit ANDs via sequential <c>.Where()</c> calls on the same
/// queryable; OR requires manually combining two expression trees.
/// </summary>
internal static class PredicateBuilderExtensions
{
    public static Expression<Func<T, bool>> True<T>()
    {
        return _ => true;
    }

    public static Expression<Func<T, bool>> False<T>()
    {
        return _ => false;
    }

    public static Expression<Func<T, bool>> AndAlso<T>(
        this Expression<Func<T, bool>> left, Expression<Func<T, bool>> right)
    {
        return Combine(left, right, Expression.AndAlso);
    }

    public static Expression<Func<T, bool>> OrElse<T>(
        this Expression<Func<T, bool>> left, Expression<Func<T, bool>> right)
    {
        return Combine(left, right, Expression.OrElse);
    }

    private static Expression<Func<T, bool>> Combine<T>(
        Expression<Func<T, bool>> left,
        Expression<Func<T, bool>> right,
        Func<Expression, Expression, BinaryExpression> combine)
    {
        var rightBody = new ParameterRebinder(right.Parameters[0], left.Parameters[0]).Visit(right.Body);
        return Expression.Lambda<Func<T, bool>>(combine(left.Body, rightBody), left.Parameters[0]);
    }

    private class ParameterRebinder(ParameterExpression from, ParameterExpression to) : ExpressionVisitor
    {
        protected override Expression VisitParameter(ParameterExpression node)
        {
            return node == from ? to : base.VisitParameter(node);
        }
    }
}
