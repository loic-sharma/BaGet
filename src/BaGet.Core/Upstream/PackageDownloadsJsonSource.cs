using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NuGet.Versioning;

namespace BaGet.Core
{
    // See https://github.com/NuGet/NuGet.Services.Metadata/blob/master/src/NuGet.Indexing/Downloads.cs
    public class PackageDownloadsJsonSource : IPackageDownloadsSource
    {
        public const string PackageDownloadsV1Url = "https://nugetprod0.blob.core.windows.net/ng-search-data/downloads.v1.json";

        private readonly HttpClient _httpClient;
        private readonly ILogger<PackageDownloadsJsonSource> _logger;

        public PackageDownloadsJsonSource(HttpClient httpClient, ILogger<PackageDownloadsJsonSource> logger)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<Dictionary<string, Dictionary<string, long>>> GetPackageDownloadsAsync()
        {
            _logger.LogInformation("Fetching package downloads...");

            var results = new Dictionary<string, Dictionary<string, long>>();

            using (var downloadsStream = await GetDownloadsStreamAsync())
            using (var downloadStreamReader = new StreamReader(downloadsStream))
            using (var jsonReader = new JsonTextReader(downloadStreamReader))
            {
                _logger.LogInformation("Parsing package downloads...");

                jsonReader.Read();

                while (jsonReader.Read())
                {
                    try
                    {
                        if (jsonReader.TokenType == JsonToken.StartArray)
                        {
                            // TODO: This line reads the entire document into memory...
                            var record = JToken.ReadFrom(jsonReader);
                            var id = string.Intern(record[0].ToString().ToLowerInvariant());

                            // The second entry in each record should be an array of versions, if not move on to next entry.
                            // This is a check to safe guard against invalid entries.
                            if (record.Count() == 2 && record[1].Type != JTokenType.Array)
                            {
                                continue;
                            }

                            if (!results.ContainsKey(id))
                            {
                                results.Add(id, new Dictionary<string, long>());
                            }

                            foreach (var token in record)
                            {
                                if (token != null && token.Count() == 2)
                                {
                                    var version = string.Intern(NuGetVersion.Parse(token[0].ToString()).ToNormalizedString().ToLowerInvariant());
                                    var downloads = token[1].ToObject<int>();

                                    results[id][version] = downloads;
                                }
                            }
                        }
                    }
                    catch (JsonReaderException e)
                    {
                        _logger.LogError(e, "Invalid entry in downloads.v1.json");
                    }
                }

                _logger.LogInformation("Parsed package downloads");
            }

            return results;
        }

        private async Task<Stream> GetDownloadsStreamAsync()
        {
            _logger.LogInformation("Downloading downloads.v1.json...");

            var fileStream = File.Open(Path.GetTempFileName(), FileMode.Create);
            var response = await _httpClient.GetAsync(PackageDownloadsV1Url, HttpCompletionOption.ResponseHeadersRead);

            response.EnsureSuccessStatusCode();

            using (var networkStream = await response.Content.ReadAsStreamAsync())
            {
                await networkStream.CopyToAsync(fileStream);
            }

            fileStream.Seek(0, SeekOrigin.Begin);

            _logger.LogInformation("Downloaded downloads.v1.json");

            return fileStream;
        }
    }
}
