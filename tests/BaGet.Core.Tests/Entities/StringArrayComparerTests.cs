using Microsoft.EntityFrameworkCore.ChangeTracking;
using Xunit;

namespace BaGet.Core.Tests.Entities;

public class StringArrayComparerTests
{
    private readonly StringArrayComparer _target = StringArrayComparer.Instance;

    [Fact]
    public void EqualsIsTrueForTheSameContent()
    {
        Assert.True(_target.Equals(new[] { "a", "b" }, new[] { "a", "b" }));
    }

    [Fact]
    public void EqualsIsTrueForTwoEmptyArrays()
    {
        Assert.True(_target.Equals(new string[0], new string[0]));
    }

    [Fact]
    public void EqualsIsFalseForDifferentOrder()
    {
        // The property is an array, so order is part of its value.
        Assert.False(_target.Equals(new[] { "a", "b" }, new[] { "b", "a" }));
    }

    [Fact]
    public void EqualsIsFalseForDifferentLengths()
    {
        Assert.False(_target.Equals(new[] { "a" }, new[] { "a", "b" }));
    }

    [Fact]
    public void EqualsIsCaseSensitive()
    {
        Assert.False(_target.Equals(new[] { "a" }, new[] { "A" }));
    }

    [Fact]
    public void EqualsHandlesNullThroughTheUntypedComparer()
    {
        // Snapshotting compares through the untyped member, which is where a null
        // property value arrives.
        var comparer = (ValueComparer)_target;

        Assert.True(comparer.Equals(null, null));
        Assert.False(comparer.Equals(null, new[] { "a" }));
        Assert.False(comparer.Equals(new[] { "a" }, null));
    }

    [Fact]
    public void EqualArraysHaveEqualHashCodes()
    {
        Assert.Equal(
            _target.GetHashCode(new[] { "a", "b" }),
            _target.GetHashCode(new[] { "a", "b" }));
    }

    [Fact]
    public void EmptyArraysHaveEqualHashCodes()
    {
        Assert.Equal(
            _target.GetHashCode(new string[0]),
            _target.GetHashCode(new string[0]));
    }

    [Fact]
    public void GetHashCodeHandlesNullThroughTheUntypedComparer()
    {
        var comparer = (ValueComparer)_target;

        Assert.Equal(0, comparer.GetHashCode(null));
    }

    [Fact]
    public void SnapshotCopiesTheArray()
    {
        // A snapshot must not alias the original, otherwise mutating the property in
        // place would mutate the snapshot too and the change would go undetected.
        var original = new[] { "a", "b" };

        var snapshot = _target.Snapshot(original);

        Assert.NotSame(original, snapshot);
        Assert.Equal(original, snapshot);

        original[0] = "changed";
        Assert.Equal("a", snapshot[0]);
    }
}