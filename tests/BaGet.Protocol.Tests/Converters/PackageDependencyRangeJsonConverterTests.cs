using System.Text.Json;
using BaGet.Protocol.Internal;
using Xunit;

namespace BaGet.Protocol.Tests
{
    public class PackageDependencyRangeJsonConverterTests
    {
        [Fact]
        public void DeserializesNull()
        {
            var json = @"null";

            var options = new JsonSerializerOptions();
            options.Converters.Add(new PackageDependencyRangeJsonConverter());

            var result = JsonSerializer.Deserialize<string>(json, options);

            Assert.Null(result);
        }

        [Fact]
        public void DeserializesString()
        {
            var json = @"""Hello""";

            var options = new JsonSerializerOptions();
            options.Converters.Add(new PackageDependencyRangeJsonConverter());

            var result = JsonSerializer.Deserialize<string>(json, options);

            Assert.Equal("Hello", result);
        }

        [Fact]
        public void DeserializesStringArray()
        {
            var json = @"[""first"", ""second"", ""third""]";

            var options = new JsonSerializerOptions();
            options.Converters.Add(new PackageDependencyRangeJsonConverter());

            var result = JsonSerializer.Deserialize<string>(json, options);

            Assert.Equal("first", result);
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
            options.Converters.Add(new PackageDependencyRangeJsonConverter());

            Assert.Throws<JsonException>(
                () => JsonSerializer.Deserialize<string>(json, options));
        }

        [Fact]
        public void SerializesNull()
        {
            var options = new JsonSerializerOptions();
            options.Converters.Add(new StringOrStringArrayJsonConverter());

            var json = JsonSerializer.Serialize<string>(null, options);

            Assert.Equal("null", json);
        }

        [Fact]
        public void SerializesString()
        {
            var options = new JsonSerializerOptions();
            options.Converters.Add(new StringOrStringArrayJsonConverter());

            var json = JsonSerializer.Serialize("foo", options);

            Assert.Equal(@"""foo""", json);
        }
    }
}
