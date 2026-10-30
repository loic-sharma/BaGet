using System;
using Xunit;

namespace BaGet.Azure.Tests.Search;

/// <summary>
/// The search runs with the full Lucene syntax, so every caller supplied value that
/// reaches the query has to be escaped or it changes what the query means.
/// </summary>
public class AzureSearchQueryBuilderTests
{
    public class EscapeQueryTerm
    {
        [Theory]
        [InlineData("newtonsoft", "newtonsoft")]
        [InlineData("Newtonsoft.Json", "Newtonsoft.Json")]
        [InlineData("my_package", "my_package")]
        [InlineData("", "")]
        public void LeavesAnOrdinaryTermAlone(string? input, string expected)
        {
            Assert.Equal(expected, AzureSearchService.EscapeQueryTerm(input));
        }

        [Theory]
        [InlineData("+", @"\+")]
        [InlineData("-", @"\-")]
        [InlineData("&", @"\&")]
        [InlineData("|", @"\|")]
        [InlineData("!", @"\!")]
        [InlineData("(", @"\(")]
        [InlineData(")", @"\)")]
        [InlineData("{", @"\{")]
        [InlineData("}", @"\}")]
        [InlineData("[", @"\[")]
        [InlineData("]", @"\]")]
        [InlineData("^", @"\^")]
        [InlineData("~", @"\~")]
        [InlineData("*", @"\*")]
        [InlineData("?", @"\?")]
        [InlineData(":", @"\:")]
        [InlineData("/", @"\/")]
        public void EscapesEachSyntaxCharacter(string? input, string expected)
        {
            Assert.Equal(expected, AzureSearchService.EscapeQueryTerm(input));
        }

        [Fact]
        public void EscapesAQuote()
        {
            Assert.Equal(@"\""", AzureSearchService.EscapeQueryTerm(@""""));
        }

        [Fact]
        public void EscapesABackslash()
        {
            Assert.Equal(@"\\", AzureSearchService.EscapeQueryTerm(@"\"));
        }

        [Fact]
        public void DoesNotQuote()
        {
            // A phrase cannot carry a trailing "*", so a term must not be quoted.
            Assert.DoesNotContain(@"""", AzureSearchService.EscapeQueryTerm("prefix"));
        }

        [Fact]
        public void ThrowsForNull()
        {
            Assert.Throws<ArgumentNullException>(() => AzureSearchService.EscapeQueryTerm(null));
        }
    }

    public class BuildSearchQuery
    {
        /// <summary>
        /// The number of quotes that actually delimit a phrase, ignoring any that are
        /// escaped and are therefore literal text inside one.
        /// </summary>
        private static int UnescapedQuotes(string value)
        {
            var count = 0;

            for (var i = 0; i < value.Length; i++)
            {
                if (value[i] != '"') continue;
                if (i > 0 && value[i - 1] == '\\') continue;

                count++;
            }

            return count;
        }

        [Fact]
        public void IsEmptyWithNoCriteria()
        {
            Assert.Equal(string.Empty, AzureSearchService.BuildSearchQuery(null, null, null));
        }

        [Fact]
        public void MakesTheQueryAPrefixSearch()
        {
            Assert.Equal("json*", AzureSearchService.BuildSearchQuery("json", null, null));
        }

        [Fact]
        public void DoesNotDoubleTheWildcard()
        {
            Assert.Equal("json*", AzureSearchService.BuildSearchQuery("json*", null, null));
        }

        [Fact]
        public void EscapesTheQueryWhileKeepingItAPrefixSearch()
        {
            // Unescaped, the ")" closes a clause the caller never opened and the "+id:"
            // that follows becomes query syntax rather than text to match.
            var built = AzureSearchService.BuildSearchQuery(") +id:evil", null, null);

            Assert.Equal(@"\) \+id\:evil*", built);
        }

        [Fact]
        public void QuotesThePackageType()
        {
            Assert.Equal(
                @" +packageTypes:""DotnetTool""",
                AzureSearchService.BuildSearchQuery(null, "DotnetTool", null));
        }

        [Fact]
        public void EscapesAPackageTypeThatWouldInjectAClause()
        {
            var built = AzureSearchService.BuildSearchQuery(
                null, @"a"" OR id:evil OR ""b", null);

            // The injected quotes are escaped, so they are literal text inside the phrase
            // and only the two delimiters are unescaped - the clause cannot be closed.
            Assert.Equal(@" +packageTypes:""a\"" OR id:evil OR \""b""", built);
            Assert.Equal(2, UnescapedQuotes(built));
        }

        [Fact]
        public void QuotesEveryFramework()
        {
            Assert.Equal(
                @" +frameworks:(""net6.0"" ""any"")",
                AzureSearchService.BuildSearchQuery(null, null, new[] { "net6.0", "any" }));
        }

        [Fact]
        public void EscapesAFrameworkThatWouldInjectAClause()
        {
            // An unrecognised moniker is passed through verbatim by
            // FindAllCompatibleFrameworks, so it reaches the query as the caller typed it.
            var built = AzureSearchService.BuildSearchQuery(
                null, null, new[] { @"a"") OR id:evil OR (""b", "any" });

            // Four unescaped quotes: two phrases, each with its own pair.
            Assert.Equal(4, UnescapedQuotes(built));
            Assert.Contains(@"\""", built);
        }

        [Fact]
        public void IgnoresAnEmptyFrameworkList()
        {
            Assert.Equal(string.Empty, AzureSearchService.BuildSearchQuery(null, null, new string[0]));
        }

        [Fact]
        public void CombinesEveryCriterion()
        {
            var built = AzureSearchService.BuildSearchQuery("json", "DotnetTool", new[] { "net6.0" });

            Assert.Equal(@"json* +packageTypes:""DotnetTool"" +frameworks:(""net6.0"")", built);
        }
    }
}