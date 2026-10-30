using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Xunit;

namespace BaGet.Core.Tests.Scanning;

/// <summary>
/// <see cref="ProcessTrivyRunner"/> against a script that stands in for the <c>trivy</c> executable: the command line it builds,
/// the credentials travelling in the environment, the report it reads back, and what it does with a failing or stuck process.
/// </summary>
public class TrivyRunnerTests : IDisposable
{
    private const string Report = "{\"Results\":[{\"Vulnerabilities\":[{\"VulnerabilityID\":\"CVE-1\",\"PkgName\":\"openssl\",\"InstalledVersion\":\"1.0\",\"Severity\":\"HIGH\"}]}]}";

    private readonly string _directory = Directory.CreateTempSubdirectory("trivy-script").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private static bool Windows => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

    // A script that records its arguments and the Trivy variables, then does what the test asks.
    private string Script(string body)
    {
        var path = Path.Combine(_directory, Windows ? "trivy.cmd" : "trivy");
        var log = Path.Combine(_directory, "call.txt");
        var text = Windows
            ? "@echo off\r\necho %* > \"" + log + "\"\r\necho user=%TRIVY_USERNAME% password=%TRIVY_PASSWORD% nonssl=%TRIVY_NON_SSL% >> \"" + log + "\"\r\n" + body
            : "#!/bin/sh\necho \"$@\" > \"" + log + "\"\necho \"user=$TRIVY_USERNAME password=$TRIVY_PASSWORD nonssl=$TRIVY_NON_SSL\" >> \"" + log + "\"\n" + body;
        File.WriteAllText(path, text);
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

        return path;
    }

    private string Call => File.ReadAllText(Path.Combine(_directory, "call.txt"));

    private static ScanningOptions Options(string path) => new()
    {
        TrivyPath = path,
        TrivyRegistryHost = "registry.test",
        TrivyRegistryUser = "ci",
        TrivyRegistryPassword = "s3cret-key",
    };

    [Fact]
    public async Task TheReportOfTheExecutableIsReadAndTheCommandLineNamesTheImage()
    {
        var script = Script(Windows ? "echo " + Report : "echo '" + Report + "'");
        var options = Options(script);
        options.TrivyServerUrl = "http://trivy:4954";
        options.TrivyInsecure = true;

        var hits = await new ProcessTrivyRunner().ScanImageAsync(options, "registry.test/libs/app@sha256:abc", TestContext.Current.CancellationToken);

        var hit = Assert.Single(hits);
        Assert.Equal("CVE-1", hit.Id);
        Assert.Equal(2, hit.Severity);
        var call = Call;
        Assert.Contains("image --quiet --no-progress --format json --scanners vuln --server http://trivy:4954 --insecure registry.test/libs/app@sha256:abc", call);
        // The credentials travel in the environment, never on the command line.
        Assert.Contains("user=ci password=s3cret-key nonssl=true", call);
        Assert.DoesNotContain("s3cret-key", call.Split('\n')[0]);
    }

    [Fact]
    public async Task AFailingExecutableIsReportedWithoutThePassword()
    {
        var script = Script(Windows ? "echo fatal: login s3cret-key rejected 1>&2\r\nexit /b 2" : "echo 'fatal: login s3cret-key rejected' >&2\nexit 2");

        var error = await Assert.ThrowsAsync<ScanUnavailableException>(
            () => new ProcessTrivyRunner().ScanImageAsync(Options(script), "registry.test/libs/app@sha256:abc", TestContext.Current.CancellationToken));

        Assert.Contains("exit code 2", error.Message);
        Assert.Contains("login ***", error.Message);
        Assert.DoesNotContain("s3cret-key", error.Message);
    }

    [Fact]
    public async Task OutputThatIsNotAReportIsReported()
    {
        var script = Script(Windows ? "echo hello" : "echo hello");

        await Assert.ThrowsAsync<ScanUnavailableException>(
            () => new ProcessTrivyRunner().ScanImageAsync(Options(script), "registry.test/libs/app@sha256:abc", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task AnExecutableThatDoesNotFinishInTimeIsStopped()
    {
        var script = Script(Windows ? "ping -n 30 127.0.0.1 >nul" : "sleep 30");
        var options = Options(script);
        options.TrivyTimeout = TimeSpan.FromMilliseconds(500);

        var error = await Assert.ThrowsAsync<ScanUnavailableException>(
            () => new ProcessTrivyRunner().ScanImageAsync(options, "registry.test/libs/app@sha256:abc", TestContext.Current.CancellationToken));

        Assert.Contains("in time", error.Message);
    }

    [Fact]
    public async Task OutputOverTheCapStopsTheProcessAtOnceInsteadOfWaitingForTheTimeout()
    {
        // Prints more than the cap on stdout, then keeps stderr (and the process) open.
        var script = Script(Windows
            ? "powershell -NoProfile -Command \"$s='x'*1048576; 1..70|foreach{[Console]::Out.Write($s)}; Start-Sleep 60\""
            : "yes | head -c 70000000\nsleep 60");
        var options = Options(script);
        options.TrivyTimeout = TimeSpan.FromSeconds(50);
        var clock = System.Diagnostics.Stopwatch.StartNew();

        var error = await Assert.ThrowsAsync<ScanUnavailableException>(
            () => new ProcessTrivyRunner().ScanImageAsync(options, "registry.test/libs/app@sha256:abc", TestContext.Current.CancellationToken));

        Assert.Contains("64 MB", error.Message);
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(40), "the cap should end the scan, not the timeout");
    }

    [Fact]
    public async Task AMissingExecutableIsReported()
    {
        var options = Options(Path.Combine(_directory, "no-such-trivy"));

        await Assert.ThrowsAsync<ScanUnavailableException>(
            () => new ProcessTrivyRunner().ScanImageAsync(options, "registry.test/libs/app@sha256:abc", TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData(null, "registry.test", false)]
    [InlineData("trivy", null, false)]
    [InlineData("trivy", "  ", false)]
    [InlineData("trivy", "registry.test", true)]
    public void TrivyIsConfiguredWithAnExecutableAndTheRegistryHost(string? path, string? host, bool configured)
    {
        Assert.Equal(configured, new ProcessTrivyRunner().IsConfigured(new ScanningOptions { TrivyPath = path, TrivyRegistryHost = host }));
    }
}
