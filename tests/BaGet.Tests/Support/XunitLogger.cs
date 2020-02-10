using System;
using Microsoft.Extensions.Logging;
using Xunit.Abstractions;

namespace BaGet.Tests
{
    // Based off:
    // https://stackoverflow.com/questions/46169169/net-core-2-0-configurelogging-xunit-test
    public class XunitLogger : ILogger
    {
        private readonly ITestOutputHelper _output;
        private readonly string _category;

        public XunitLogger(ITestOutputHelper output, string category)
        {
            _output = output ?? throw new ArgumentNullException(nameof(output));
            _category = category ?? throw new ArgumentNullException(nameof(category));
        }

        public IDisposable BeginScope<TState>(TState state) => NullScope.Instance;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
        {
            _output.WriteLine($"{_category} [{eventId}] {formatter(state, exception)}");

            if (exception != null)
            {
                _output.WriteLine(exception.ToString());
            }
        }

        private class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new NullScope();

            public void Dispose() { }
        }
    }
}
