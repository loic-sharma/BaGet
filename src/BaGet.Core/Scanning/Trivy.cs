using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace BaGet.Core;

/// <summary>A vulnerability a scanner found, before it is stored as a <see cref="ScanFinding"/>.</summary>
/// <param name="Id">The advisory id.</param>
/// <param name="Component">The vulnerable component.</param>
/// <param name="ComponentVersion">Its version.</param>
/// <param name="Severity">0 low … 3 critical.</param>
/// <param name="Summary">One line, or null.</param>
/// <param name="Url">An absolute http(s) link.</param>
/// <param name="FixedVersion">The first fixed version, or null.</param>
public sealed record ScanHit(string Id, string Component, string ComponentVersion, int Severity, string? Summary, string Url, string? FixedVersion);

/// <summary>Runs Trivy on a container image reference. Replaced in tests; the default runs the configured executable.</summary>
public interface ITrivyRunner
{
    /// <summary>Whether Trivy is configured (<see cref="ScanningOptions.TrivyPath"/> and a registry host are set).</summary>
    bool IsConfigured(ScanningOptions options);

    /// <summary>Scans <paramref name="imageReference"/> (<c>host/repository/image@digest</c>) and returns what Trivy reports.</summary>
    /// <exception cref="ScanUnavailableException">Trivy is not usable or failed; the message is safe to show.</exception>
    Task<IReadOnlyList<ScanHit>> ScanImageAsync(ScanningOptions options, string imageReference, CancellationToken cancellationToken);
}

/// <summary>Reads the JSON Trivy prints with <c>--format json</c>.</summary>
public static class TrivyReport
{
    /// <summary>The vulnerabilities of every result of the report (OS packages and language packages alike).</summary>
    /// <exception cref="ScanUnavailableException">The text is not a Trivy report.</exception>
    public static IReadOnlyList<ScanHit> Parse(string json)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException e)
        {
            throw new ScanUnavailableException("Trivy printed something that is not JSON.", e);
        }

        using (document)
        {
            var hits = new List<ScanHit>();
            if (document.RootElement.ValueKind != JsonValueKind.Object) throw new ScanUnavailableException("Trivy printed an unexpected document.");

            if (document.RootElement.TryGetProperty("Results", out var results) && results.ValueKind == JsonValueKind.Array)
            {
                foreach (var result in results.EnumerateArray())
                {
                    if (!result.TryGetProperty("Vulnerabilities", out var vulnerabilities) || vulnerabilities.ValueKind != JsonValueKind.Array) continue;
                    foreach (var vulnerability in vulnerabilities.EnumerateArray())
                    {
                        var id = Text(vulnerability, "VulnerabilityID");
                        var name = Text(vulnerability, "PkgName");
                        if (id == null || name == null) continue;

                        ScanSeverities.TryParseName(Text(vulnerability, "Severity"), out var severity);
                        var url = Text(vulnerability, "PrimaryURL");
                        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp) || url!.Length > 900)
                        {
                            url = "https://avd.aquasec.com/nvd/" + Uri.EscapeDataString(id.ToLowerInvariant());
                        }

                        var summary = Text(vulnerability, "Title") ?? Text(vulnerability, "Description");
                        if (summary != null && summary.Length > 500) summary = summary.Substring(0, 500);

                        hits.Add(new ScanHit(id, name, Text(vulnerability, "InstalledVersion") ?? "unknown", severity, summary, url!, Text(vulnerability, "FixedVersion")));
                    }
                }
            }

            return hits;
        }
    }

    private static string? Text(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString()) ? value.GetString() : null;
}

/// <summary>
/// Runs the configured <c>trivy</c> executable: <c>trivy image --format json --scanners vuln [--server URL] [--insecure] REF</c>.
/// The registry credentials travel in the environment (<c>TRIVY_USERNAME</c>, <c>TRIVY_PASSWORD</c>), never on the command line.
/// </summary>
public sealed class ProcessTrivyRunner : ITrivyRunner
{
    private const int MaxOutputBytes = 64 * 1024 * 1024;

    /// <inheritdoc/>
    public bool IsConfigured(ScanningOptions options)
        => !string.IsNullOrWhiteSpace(options.TrivyPath) && !string.IsNullOrWhiteSpace(options.TrivyRegistryHost);

    /// <inheritdoc/>
    public async Task<IReadOnlyList<ScanHit>> ScanImageAsync(ScanningOptions options, string imageReference, CancellationToken cancellationToken)
    {
        if (!IsConfigured(options)) throw new ScanUnavailableException("Trivy is not configured.");

        var start = new ProcessStartInfo(options.TrivyPath!)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
        };
        foreach (var argument in new[] { "image", "--quiet", "--no-progress", "--format", "json", "--scanners", "vuln" })
        {
            start.ArgumentList.Add(argument);
        }

        if (!string.IsNullOrWhiteSpace(options.TrivyServerUrl))
        {
            start.ArgumentList.Add("--server");
            start.ArgumentList.Add(options.TrivyServerUrl!);
        }

        if (options.TrivyInsecure)
        {
            start.ArgumentList.Add("--insecure");
            start.Environment["TRIVY_NON_SSL"] = "true";
        }

        if (!string.IsNullOrEmpty(options.TrivyRegistryUser)) start.Environment["TRIVY_USERNAME"] = options.TrivyRegistryUser;
        if (!string.IsNullOrEmpty(options.TrivyRegistryPassword)) start.Environment["TRIVY_PASSWORD"] = options.TrivyRegistryPassword;
        start.ArgumentList.Add(imageReference);

        Process process;
        try
        {
            process = Process.Start(start) ?? throw new ScanUnavailableException("Trivy could not be started.");
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception || e is InvalidOperationException || e is IOException)
        {
            throw new ScanUnavailableException("Trivy could not be started: " + e.Message, e);
        }

        using (process)
        using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
        {
            timeout.CancelAfter(options.TrivyTimeout > TimeSpan.Zero ? options.TrivyTimeout : TimeSpan.FromMinutes(10));
            process.StandardInput.Close();
            var output = ReadCappedAsync(process.StandardOutput, timeout.Token);
            var error = ReadCappedAsync(process.StandardError, timeout.Token);
            try
            {
                // A reader that hits the output cap stops draining the pipe and the child would block on it until the timeout:
                // watch the readers too and kill the process as soon as one fails.
                var exited = process.WaitForExitAsync(timeout.Token);
                // Task.WhenAll would wait for the other reader too, which never ends while the child blocks on the full pipe.
                var pending = new List<Task> { exited, output, error };
                while (!exited.IsCompleted)
                {
                    var done = await Task.WhenAny(pending).ConfigureAwait(false);
                    if (done == exited) break;
                    await done.ConfigureAwait(false);
                    pending.Remove(done);
                }

                await exited.ConfigureAwait(false);
            }
            catch (ScanUnavailableException)
            {
                try { process.Kill(entireProcessTree: true); } catch (Exception) { /* already gone */ }
                throw;
            }
            catch (OperationCanceledException)
            {
                try { process.Kill(entireProcessTree: true); } catch (Exception) { /* already gone */ }
                if (cancellationToken.IsCancellationRequested) throw;
                throw new ScanUnavailableException("Trivy did not finish in time.");
            }

            var text = await output.ConfigureAwait(false);
            var errors = await error.ConfigureAwait(false);
            if (process.ExitCode != 0)
            {
                throw new ScanUnavailableException("Trivy failed (exit code " + process.ExitCode + "): " + Scrub(FirstLine(errors), options));
            }

            return TrivyReport.Parse(text);
        }
    }

    private static string FirstLine(string text)
    {
        var line = text.Split('\n').Select(l => l.Trim()).LastOrDefault(l => l.Length > 0) ?? "no message";
        return line.Length > 300 ? line.Substring(0, 300) : line;
    }

    private static string Scrub(string text, ScanningOptions options)
        => string.IsNullOrEmpty(options.TrivyRegistryPassword) ? text : text.Replace(options.TrivyRegistryPassword, "***", StringComparison.Ordinal);

    private static async Task<string> ReadCappedAsync(StreamReader reader, CancellationToken cancellationToken)
    {
        var builder = new StringBuilder();
        var buffer = new char[8192];
        int read;
        while ((read = await reader.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false)) > 0)
        {
            if (builder.Length + read > MaxOutputBytes) throw new ScanUnavailableException("Trivy printed more than 64 MB.");
            builder.Append(buffer, 0, read);
        }

        return builder.ToString();
    }
}
