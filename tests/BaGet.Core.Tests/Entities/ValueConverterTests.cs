using System.Collections.Generic;
using System;
using Xunit;

namespace BaGet.Core.Tests.Entities;

public class StringArrayToJsonConverterTests
{
    private readonly StringArrayToJsonConverter? _target = StringArrayToJsonConverter.Instance;

    private string? ToStore(string[] value) => (string?)_target?.ConvertToProvider(value);

    private string[]? FromStore(string? value) => (string[]?)_target?.ConvertFromProvider(value);

    public static IEnumerable<object[]> Arrays => new[]
    {
        new object[] { new string[0] },
        new object[] { new[] { "one" } },
        new object[] { new[] { "one", "two", "three" } },
        new object[] { new[] { "" } },
        new object[] { new[] { "with \"quotes\"", "with,comma", "with\\backslash" } },
        new object[] { new[] { "ünïcödé", "中文" } },
    };

    [Theory]
    [MemberData(nameof(Arrays))]
    public void RoundTripsAnArray(string[] value)
    {
        Assert.Equal(value, FromStore(ToStore(value)));
    }

    [Fact]
    public void ReadsAnEmptyColumnAsAnEmptyArray()
    {
        // Rows written before the column existed hold an empty string.
        Assert.Equal(new string[0], FromStore(""));
    }

    [Fact]
    public void PreservesOrder()
    {
        var value = new[] { "z", "a", "m" };

        Assert.Equal(value, FromStore(ToStore(value)));
    }

    [Fact]
    public void PreservesDuplicates()
    {
        var value = new[] { "a", "a", "b" };

        Assert.Equal(value, FromStore(ToStore(value)));
    }
}

public class UriToStringConverterTests
{
    private readonly UriToStringConverter? _target = UriToStringConverter.Instance;

    private string? ToStore(Uri value) => (string?)_target?.ConvertToProvider(value);

    private Uri? FromStore(string? value) => (Uri?)_target?.ConvertFromProvider(value);

    [Theory]
    [InlineData("https://example.test/package")]
    [InlineData("http://example.test/")]
    [InlineData("https://example.test/path/to/thing?query=1&other=2")]
    [InlineData("https://example.test/path#fragment")]
    public void RoundTripsAnAbsoluteUri(string value)
    {
        var uri = new Uri(value);

        Assert.Equal(uri, FromStore(ToStore(uri)));
    }

    [Fact]
    public void RoundTripsAUriWithAnEscapedSpace()
    {
        var uri = new Uri("https://example.test/a%20b");

        Assert.Equal(uri, FromStore(ToStore(uri)));
    }

    [Fact]
    public void StoresTheAbsoluteUri()
    {
        Assert.Equal(
            "https://example.test/package",
            ToStore(new Uri("https://example.test/package")));
    }
}