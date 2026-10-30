using System;
using System.Collections.Generic;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace BaGet.Core.Tests.Telemetry;

public class TelemetryOptionsTests
{
    [Fact]
    public void DefaultsAreOffAndValid()
    {
        var options = new TelemetryOptions();

        Assert.False(options.Enabled);
        Assert.Equal("baget", options.ServiceName);
        Assert.Null(options.Otlp.Endpoint);
        Assert.Equal("Grpc", options.Otlp.Protocol);
        Assert.Equal(TimeSpan.FromSeconds(10), options.Otlp.Timeout);
        Assert.True(options.Traces.Enabled);
        Assert.Equal(1.0, options.Traces.SamplingRatio);
        Assert.True(options.Traces.Database);
        Assert.Equal(TimeSpan.FromSeconds(60), options.Metrics.ExportInterval);
        Assert.Equal("Information", options.Logs.MinimumLevel);
        Assert.Empty(options.Validate());
    }

    [Fact]
    public void BindsFromTheTelemetrySectionOfTheRootOptions()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Telemetry:Enabled"] = "true",
            ["Telemetry:Otlp:Endpoint"] = "http://jaeger:4317",
            ["Telemetry:Otlp:Timeout"] = "00:00:03",
            ["Telemetry:Traces:SamplingRatio"] = "0.25",
            ["Telemetry:Metrics:Endpoint"] = "http://collector:4317",
            ["Telemetry:Logs:MinimumLevel"] = "Warning",
        }).Build();

        var options = configuration.Get<BaGetOptions>()!.Telemetry!;

        Assert.True(options.Enabled);
        Assert.Equal("http://jaeger:4317", options.Otlp.Endpoint);
        Assert.Equal(TimeSpan.FromSeconds(3), options.Otlp.Timeout);
        Assert.Equal(0.25, options.Traces.SamplingRatio);
        Assert.Equal("http://collector:4317", options.Metrics.Endpoint);
        Assert.Equal("Warning", options.Logs.MinimumLevel);
        Assert.Empty(options.Validate());
    }

    [Theory]
    [InlineData("http://jaeger:4317")]
    [InlineData("https://otel.example.com")]
    [InlineData("")]
    [InlineData(null)]
    public void AcceptsAbsoluteHttpEndpointsAndEmpty(string? endpoint)
    {
        var options = new TelemetryOptions();
        options.Otlp.Endpoint = endpoint;
        options.Metrics.Endpoint = endpoint;
        options.Logs.Endpoint = endpoint;

        Assert.Empty(options.Validate());
    }

    [Theory]
    [InlineData("jaeger:4317")]
    [InlineData("/relative")]
    [InlineData("ftp://jaeger")]
    [InlineData("not a url")]
    public void RejectsABadEndpointWithoutEchoingIt(string endpoint)
    {
        var options = new TelemetryOptions();
        options.Otlp.Endpoint = endpoint;
        options.Metrics.Endpoint = endpoint;
        options.Logs.Endpoint = endpoint;

        var failures = options.Validate();

        Assert.Equal(3, failures.Count);
        Assert.Contains("Telemetry:Otlp:Endpoint", failures[0]);
        Assert.Contains("Telemetry:Metrics:Endpoint", failures[1]);
        Assert.Contains("Telemetry:Logs:Endpoint", failures[2]);
        Assert.All(failures, f => Assert.DoesNotContain(endpoint, f));
    }

    [Theory]
    [InlineData(-0.1)]
    [InlineData(1.01)]
    [InlineData(double.NaN)]
    public void RejectsASamplingRatioOutsideZeroToOne(double ratio)
    {
        var options = new TelemetryOptions();
        options.Traces.SamplingRatio = ratio;

        Assert.Contains("SamplingRatio", Assert.Single(options.Validate()));
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(0.5)]
    [InlineData(1.0)]
    public void AcceptsSamplingRatiosInRange(double ratio)
    {
        var options = new TelemetryOptions();
        options.Traces.SamplingRatio = ratio;

        Assert.Empty(options.Validate());
    }

    [Fact]
    public void RejectsAnUnknownProtocolAndLevelAndNonPositiveDurations()
    {
        var options = new TelemetryOptions();
        options.Otlp.Protocol = "Thrift";
        options.Otlp.Timeout = TimeSpan.Zero;
        options.Metrics.ExportInterval = TimeSpan.FromSeconds(-1);
        options.Logs.MinimumLevel = "Loud";

        var failures = options.Validate();

        Assert.Equal(4, failures.Count);
        Assert.Contains(failures, f => f.Contains("Protocol"));
        Assert.Contains(failures, f => f.Contains("Timeout"));
        Assert.Contains(failures, f => f.Contains("ExportInterval"));
        Assert.Contains(failures, f => f.Contains("MinimumLevel"));
    }

    [Fact]
    public void ProtocolIsCaseInsensitive()
    {
        var options = new TelemetryOptions();
        options.Otlp.Protocol = "httpprotobuf";

        Assert.Empty(options.Validate());
    }
}
