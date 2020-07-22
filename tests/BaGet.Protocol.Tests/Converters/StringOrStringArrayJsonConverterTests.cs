using System.Collections.Generic;
using System.Text.Json;
using BaGet.Protocol.Internal;
using Xunit;

namespace BaGet.Protocol.Tests
{
    public class StringOrStringArrayJsonConverterTests
    {
        [Fact]
        public void DeserializesEmptyString()
        {
            var json = @"""""";

            var options = new JsonSerializerOptions();
            options.Converters.Add(new StringOrStringArrayJsonConverter());

            var result = JsonSerializer.Deserialize<IReadOnlyList<string>>(json, options);

            var first = Assert.Single(result);
            Assert.NotNull(first);
            Assert.True(string.IsNullOrEmpty(first));
        }

        [Fact]
        public void DeserializesString()
        {
            var json = @"""Foo bar""";

            var options = new JsonSerializerOptions();
            options.Converters.Add(new StringOrStringArrayJsonConverter());

            var result = JsonSerializer.Deserialize<IReadOnlyList<string>>(json, options);

            var first = Assert.Single(result);
            Assert.Equal("Foo bar", first);
        }

        [Fact]
        public void DeserializesEmptyArray()
        {
            var json = "[]";

            var options = new JsonSerializerOptions();
            options.Converters.Add(new StringOrStringArrayJsonConverter());

            var result = JsonSerializer.Deserialize<IReadOnlyList<string>>(json, options);

            Assert.Empty(result);
        }

        [Fact]
        public void DeserializesArray()
        {
            var json = @"[""Foo"", ""bar""]";

            var options = new JsonSerializerOptions();
            options.Converters.Add(new StringOrStringArrayJsonConverter());

            var result = JsonSerializer.Deserialize<IReadOnlyList<string>>(json, options);

            Assert.Equal(2, result.Count);
            Assert.Equal("Foo", result[0]);
            Assert.Equal("bar", result[1]);
        }

        [Theory]
        [InlineData(@"false")]
        [InlineData(@"0")]
        [InlineData(@"{")]
        [InlineData(@"[")]
        [InlineData(@"[""hello""")]
        [InlineData(@"[""hello""}")]
        [InlineData(@"[""hello"", 1")]
        public void ThrowsOnInvalidJson(string json)
        {
            var options = new JsonSerializerOptions();
            options.Converters.Add(new StringOrStringArrayJsonConverter());

            Assert.Throws<JsonException>(
                () => JsonSerializer.Deserialize<IReadOnlyList<string>>(json, options));
        }

        [Fact]
        public void SerializesNull()
        {
            var options = new JsonSerializerOptions();
            options.Converters.Add(new StringOrStringArrayJsonConverter());

            IReadOnlyList<string> list = null;

            var json = JsonSerializer.Serialize(list, options);

            Assert.Equal("null", json);
        }

        [Fact]
        public void SerializesEmptyString()
        {
            var options = new JsonSerializerOptions();
            options.Converters.Add(new StringOrStringArrayJsonConverter());

            IReadOnlyList<string> list = new List<string> { "" };

            var json = JsonSerializer.Serialize(list, options);

            Assert.Equal(@"[""""]", json);
        }

        [Fact]
        public void SerializesList()
        {
            var options = new JsonSerializerOptions();
            options.Converters.Add(new StringOrStringArrayJsonConverter());

            IReadOnlyList<string> list = new List<string> { "Hello", "World", null };

            var json = JsonSerializer.Serialize(list, options);

            Assert.Equal(@"[""Hello"",""World"",null]", json);
        }
    }
}
