using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace BaGet.Protocol.Catalog
{
    /// <summary>
    /// A cursor implementation which stores the cursor in local file. The cursor value is written to the file as
    /// a JSON object.
    /// Based off: https://github.com/NuGet/NuGet.Services.Metadata/blob/3a468fe534a03dcced897eb5992209fdd3c4b6c9/src/NuGet.Protocol.Catalog/FileCursor.cs
    /// </summary>
    public class FileCursor : ICursor
    {
        private readonly string _path;
        private readonly ILogger<FileCursor> _logger;

        public FileCursor(string path, ILogger<FileCursor> logger)
        {
            _path = path ?? throw new ArgumentNullException(nameof(path));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<DateTimeOffset?> GetAsync(CancellationToken cancellationToken)
        {
            try
            {
                using (var file = File.OpenRead(_path))
                {
                    var data = await JsonSerializer.DeserializeAsync<Data>(file, options: null, cancellationToken);
                    _logger.LogDebug("Read cursor value {cursor:O} from {path}.", data.Value, _path);
                    return data.Value;
                }
            }
            catch (Exception e) when (e is FileNotFoundException || e is JsonException)
            {
                return null;
            }
        }

        public Task SetAsync(DateTimeOffset value, CancellationToken cancellationToken)
        {
            var data = new Data { Value = value };
            var jsonString = JsonSerializer.Serialize(data);
            File.WriteAllText(_path, jsonString);
            _logger.LogDebug("Wrote cursor value {cursor:O} to {path}.", data.Value, _path);
            return Task.CompletedTask;
        }

        private class Data
        {
            [JsonPropertyName("value")]
            public DateTimeOffset Value { get; set; }
        }
    }
}
