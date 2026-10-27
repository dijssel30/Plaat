using System.Linq.Expressions;
using Plaat.Db.Expressions;
using Shouldly;
using Xunit;

namespace Plaat.Db.Tests;

public sealed class PredicateBuilderExtensionsTests
{
    private sealed record Item(int Value);

    // Each predicate below is built as its own independent lambda expression (its own
    // ParameterExpression instance), exercising the parameter-rebinding path in
    // PredicateBuilderExtensions rather than accidentally reusing a shared parameter.
    private static Expression<Func<Item, bool>> GreaterThan(int threshold)
    {
        return i => i.Value > threshold;
    }

    private static Expression<Func<Item, bool>> LessThan(int threshold)
    {
        return i => i.Value < threshold;
    }

    [Fact]
    public void AndAlso_BothMustMatch()
    {
        var predicate = GreaterThan(2).AndAlso(LessThan(5));
        var compiled = predicate.Compile();

        compiled(new Item(3)).ShouldBeTrue();
        compiled(new Item(2)).ShouldBeFalse();
        compiled(new Item(5)).ShouldBeFalse();
    }

    [Fact]
    public void OrElse_EitherMatches()
    {
        var predicate = GreaterThan(10).OrElse(LessThan(2));
        var compiled = predicate.Compile();

        compiled(new Item(15)).ShouldBeTrue();
        compiled(new Item(1)).ShouldBeTrue();
        compiled(new Item(5)).ShouldBeFalse();
    }

    [Fact]
    public void True_AlwaysMatches()
    {
        PredicateBuilderExtensions.True<Item>().Compile()(new Item(0)).ShouldBeTrue();
    }

    [Fact]
    public void False_NeverMatches()
    {
        PredicateBuilderExtensions.False<Item>().Compile()(new Item(0)).ShouldBeFalse();
    }

    [Fact]
    public void AndAlso_TranslatesAgainstQueryableSource()
    {
        List<Item> items = [new(1), new(3), new(10)];

        var predicate = GreaterThan(2).AndAlso(LessThan(5));
        var result = items.AsQueryable().Where(predicate).ToArray();

        result.Select(i => i.Value).ShouldBe([3]);
    }

    [Fact]
    public void OrElse_TranslatesAgainstQueryableSource()
    {
        List<Item> items = [new(1), new(3), new(10)];

        var predicate = GreaterThan(5).OrElse(LessThan(2));
        var result = items.AsQueryable().Where(predicate).ToArray();

        result.Select(i => i.Value).ShouldBe([1, 10]);
    }
}
