using System;
using Xunit;

namespace BaGet.Azure.Tests.Search;

/// <summary>
/// Covers the query construction of <see cref="AzureSearchService"/>. The queries run with
/// the full Lucene query syntax, so a value interpolated into one must not be able to change
/// the query's meaning.
/// See https://learn.microsoft.com/azure/search/query-lucene-syntax.
/// </summary>
public class AzureSearchQueryTests
{
    public class EscapeQueryPhrase
    {
        [Theory]
        [InlineData("Newtonsoft.Json", "\"Newtonsoft.Json\"")]
        [InlineData("newtonsoft.json", "\"newtonsoft.json\"")]
        [InlineData("my-package", "\"my-package\"")]
        [InlineData("my_package", "\"my_package\"")]
        [InlineData("Package1", "\"Package1\"")]
        public void QuotesOrdinaryIds(string? input, string expected)
        {
            Assert.Equal(expected, AzureSearchService.EscapeQueryPhrase(input));
        }

        /// <summary>
        /// The characters the docs list as operators in the full Lucene syntax:
        /// <c>+ - &amp; | ! ( ) { } [ ] ^ " ~ * ? : \ /</c>. Inside a quoted phrase they are
        /// content rather than operators, so only the phrase delimiter and the escape
        /// character itself need a backslash. Each case asserts the phrase still has exactly
        /// one opening and one closing delimiter.
        /// </summary>
        [Theory]
        [InlineData("+", "\"a+b\"")]
        [InlineData("-", "\"a-b\"")]
        [InlineData("&", "\"a&b\"")]
        [InlineData("|", "\"a|b\"")]
        [InlineData("!", "\"a!b\"")]
        [InlineData("(", "\"a(b\"")]
        [InlineData(")", "\"a)b\"")]
        [InlineData("{", "\"a{b\"")]
        [InlineData("}", "\"a}b\"")]
        [InlineData("[", "\"a[b\"")]
        [InlineData("]", "\"a]b\"")]
        [InlineData("^", "\"a^b\"")]
        [InlineData("~", "\"a~b\"")]
        [InlineData("*", "\"a*b\"")]
        [InlineData("?", "\"a?b\"")]
        [InlineData(":", "\"a:b\"")]
        [InlineData("/", "\"a/b\"")]
        [InlineData("\"", "\"a\\\"b\"")]
        [InlineData("\\", "\"a\\\\b\"")]
        public void HandlesEverySignificantCharacter(string character, string expected)
        {
            Assert.Equal(expected, AzureSearchService.EscapeQueryPhrase("a" + character + "b"));
        }

        [Fact]
        public void EscapesTheQuotationMarkSoThePhraseCannotBeClosedEarly()
        {
            Assert.Equal("\"foo\\\" OR bar:baz\"", AzureSearchService.EscapeQueryPhrase("foo\" OR bar:baz"));
        }

        [Fact]
        public void EscapesABackslashThatWouldOtherwiseEscapeTheClosingQuotationMark()
        {
            // Without escaping the backslash the result would be `"foo\"`, whose closing
            // delimiter is escaped away, leaving an unterminated phrase.
            Assert.Equal("\"foo\\\\\"", AzureSearchService.EscapeQueryPhrase("foo\\"));
        }

        [Fact]
        public void EscapesAnEscapeThatIsAlreadyInTheValue()
        {
            // An id that already contains an escape sequence must be matched literally: the
            // backslash is doubled so the parser hands `a\-b` to the analyzer.
            Assert.Equal("\"a\\\\-b\"", AzureSearchService.EscapeQueryPhrase("a\\-b"));
            Assert.Equal("\"a\\\\\\\\b\"", AzureSearchService.EscapeQueryPhrase("a\\\\b"));
        }

        [Fact]
        public void KeepsWhitespaceInsideTheSinglePhrase()
        {
            // Whitespace cannot be escaped in the Lucene syntax; quoting is what keeps the
            // value from being split into several terms (and `OR` from becoming an operator).
            Assert.Equal("\"foo OR bar\"", AzureSearchService.EscapeQueryPhrase("foo OR bar"));
        }

        [Fact]
        public void QuotesAnEmptyValue()
        {
            Assert.Equal("\"\"", AzureSearchService.EscapeQueryPhrase(string.Empty));
        }

        [Fact]
        public void ThrowsOnNull()
        {
            Assert.Throws<ArgumentNullException>(() => AzureSearchService.EscapeQueryPhrase(null));
        }
    }

    public class BuildDependentsQuery
    {
        [Fact]
        public void BuildsAFieldedPhraseQuery()
        {
            Assert.Equal(
                "dependencies:\"newtonsoft.json\"",
                AzureSearchService.BuildDependentsQuery("newtonsoft.json"));
        }

        [Fact]
        public void LowercasesThePackageId()
        {
            // The field is indexed with a keyword tokenizer and a lowercase filter.
            Assert.Equal(
                "dependencies:\"newtonsoft.json\"",
                AzureSearchService.BuildDependentsQuery("Newtonsoft.Json"));
        }

        [Fact]
        public void DoesNotLetAnIdAddAClause()
        {
            var query = AzureSearchService.BuildDependentsQuery("foo OR dependencies:bar");

            Assert.Equal("dependencies:\"foo or dependencies:bar\"", query);
            Assert.Equal(2, CountPhraseDelimiters(query));
        }

        [Fact]
        public void DoesNotLetAnIdEscapeThePhrase()
        {
            var query = AzureSearchService.BuildDependentsQuery("foo\" OR id:/.*/ OR x:\"y");

            Assert.Equal("dependencies:\"foo\\\" or id:/.*/ or x:\\\"y\"", query);
            Assert.Equal(2, CountPhraseDelimiters(query));
        }

        [Fact]
        public void DoesNotLetATrailingBackslashEscapeThePhrase()
        {
            var query = AzureSearchService.BuildDependentsQuery("foo\\");

            Assert.Equal("dependencies:\"foo\\\\\"", query);
            Assert.Equal(2, CountPhraseDelimiters(query));
        }

        [Fact]
        public void BuildsAnEmptyPhraseForAnEmptyId()
        {
            Assert.Equal("dependencies:\"\"", AzureSearchService.BuildDependentsQuery(string.Empty));
        }

        [Fact]
        public void ThrowsOnNull()
        {
            Assert.Throws<ArgumentNullException>(() => AzureSearchService.BuildDependentsQuery(null));
        }

        /// <summary>
        /// Counts the quotation marks that are not preceded by an escape, which is how the
        /// query parser finds the start and the end of the phrase. Anything other than 2 means
        /// the value escaped the phrase.
        /// </summary>
        private static int CountPhraseDelimiters(string query)
        {
            var delimiters = 0;
            var escaped = false;

            foreach (var character in query)
            {
                if (escaped)
                {
                    escaped = false;
                    continue;
                }

                if (character == '\\')
                {
                    escaped = true;
                }
                else if (character == '"')
                {
                    delimiters++;
                }
            }

            return delimiters;
        }
    }
}