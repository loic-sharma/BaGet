using System;
using Xunit;

namespace BaGet.Core.Tests.Entities
{
    public class PackageTests
    {
        [Fact]
        public void PublishedIsUtcWhenReadBackFromTheDatabase()
        {
            // The database returns a DateTime whose Kind is Unspecified.
            var package = new Package
            {
                Published = new DateTime(2020, 5, 6, 7, 8, 9, DateTimeKind.Unspecified)
            };

            Assert.Equal(DateTimeKind.Utc, package.Published.Kind);
        }

        [Fact]
        public void PublishedKeepsItsValueWhenAlreadyUtc()
        {
            var published = new DateTime(2020, 5, 6, 7, 8, 9, DateTimeKind.Utc);
            var package = new Package { Published = published };

            Assert.Equal(published, package.Published);
            Assert.Equal(DateTimeKind.Utc, package.Published.Kind);
        }

        [Fact]
        public void PublishedConvertsToAnUnshiftedDateTimeOffset()
        {
            var package = new Package
            {
                Published = new DateTime(2020, 5, 6, 7, 8, 9, DateTimeKind.Unspecified)
            };

            DateTimeOffset published = package.Published;

            // Without an explicit UTC kind the conversion applies the server's local
            // offset, moving the published timestamp reported by the NuGet protocol.
            Assert.Equal(TimeSpan.Zero, published.Offset);
            Assert.Equal(new DateTimeOffset(2020, 5, 6, 7, 8, 9, TimeSpan.Zero), published);
        }

        [Fact]
        public void DefaultPublishedConvertsToDateTimeOffset()
        {
            var package = new Package();

            // A package with no published date - V2UpstreamClient falls back to
            // DateTime.MinValue - threw on servers east of UTC, because applying a
            // positive local offset to DateTime.MinValue underflows year 0.
            DateTimeOffset published = package.Published;

            Assert.Equal(TimeSpan.Zero, published.Offset);
            Assert.Equal(DateTimeOffset.MinValue, published);
        }
    }
}
