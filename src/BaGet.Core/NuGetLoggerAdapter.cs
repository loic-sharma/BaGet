using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace BaGet.Core
{
    public class NuGetLoggerAdapter<T> : NuGet.Common.ILogger
    {
        Microsoft.Extensions.Logging.ILogger<T> logger;

        public NuGetLoggerAdapter(Microsoft.Extensions.Logging.ILogger<T> logger) {
            this.logger = logger ?? throw new ArgumentNullException("logger");
        }


        public void Log(NuGet.Common.LogLevel level, string data)
        {
            switch(level) {
                case NuGet.Common.LogLevel.Error:
                    this.logger.LogError(data);
                    break;
                case NuGet.Common.LogLevel.Debug:
                    this.logger.LogDebug(data);
                    break;
                case NuGet.Common.LogLevel.Minimal:
                    this.logger.LogDebug(data);
                    break;
                case NuGet.Common.LogLevel.Information:
                    this.logger.LogInformation(data);
                    break;
                case NuGet.Common.LogLevel.Verbose:
                    this.logger.LogTrace(data);
                    break;
                case NuGet.Common.LogLevel.Warning:
                    this.logger.LogWarning(data);
                    break;
            }            
        }

        public void Log(NuGet.Common.ILogMessage message)
        {
            this.Log(message.Level, message.Message);
        }

        public Task LogAsync(NuGet.Common.LogLevel level, string data)
        {
            this.Log(level, data);
            return Task.CompletedTask;
        }

        public Task LogAsync(NuGet.Common.ILogMessage message)
        {
            this.Log(message.Level, message.Message);
            return Task.CompletedTask;
        }

        public void LogDebug(string data)
        {
            this.logger.LogDebug(data);
        }

        public void LogError(string data)
        {
            this.logger.LogError(data);
        }

        public void LogInformation(string data)
        {
            this.logger.LogInformation(data);
        }

        public void LogInformationSummary(string data)
        {
            this.logger.LogInformation(data);
        }

        public void LogMinimal(string data)
        {
            this.logger.LogDebug(data);
        }

        public void LogVerbose(string data)
        {
            this.logger.LogDebug(data);
        }

        public void LogWarning(string data)
        {
            this.logger.LogWarning(data);
        }
    }
}