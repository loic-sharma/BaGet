using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Core;
using BaGet.Identity.Extensions;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;
using BaGet.Identity.Application.Email;
using BaGet.Identity.Application.Services.Accounts;

namespace BaGet.Tests;

public class EmailTests
{
    private readonly ITestOutputHelper _output;

    public EmailTests(ITestOutputHelper output) => _output = output;

    [Theory]
    [InlineData(null, null, false)]
    [InlineData("smtp.example.com", null, false)]
    [InlineData(null, "baget@example.com", false)]
    [InlineData("", "baget@example.com", false)]
    [InlineData("smtp.example.com", "baget@example.com", true)]
    public void IsConfiguredNeedsAHostAndASender(string? host, string? from, bool expected)
    {
        var options = new EmailOptions { Smtp = { Host = host, From = from } };

        Assert.Equal(expected, options.IsConfigured);
    }

    [Fact]
    public void SmtpDefaultsToTheSubmissionPortWithTls()
    {
        var smtp = new EmailOptions().Smtp;

        Assert.Equal(587, smtp.Port);
        Assert.True(smtp.EnableSsl);
    }

    [Fact]
    public void ConfirmedEmailIsRequiredOnlyWhenEmailIsConfigured()
    {
        Assert.False(RequireConfirmedEmail(new()));
        Assert.True(RequireConfirmedEmail(new()
        {
            { "Email:Smtp:Host", "smtp.example.com" },
            { "Email:Smtp:From", "baget@example.com" },
            { "Site:PublicUrl", "https://baget.example.com" },
        }));
    }

    [Fact]
    public void UnconfiguredEmailRegistersASenderThatSendsNothing()
    {
        using var provider = BuildProvider(new());

        Assert.IsType<NoOpEmailSender>(provider.GetRequiredService<IEmailSender<BaGetUser>>());
        Assert.False(provider.GetRequiredService<IOptions<EmailOptions>>().Value.IsConfigured);
    }

    [Fact]
    public void ConfiguredEmailRegistersTheSmtpSender()
    {
        using var provider = BuildProvider(new()
        {
            { "Email:Smtp:Host", "smtp.example.com" },
            { "Email:Smtp:From", "baget@example.com" },
            { "Site:PublicUrl", "https://baget.example.com" },
        });

        Assert.IsType<SmtpEmailSender>(provider.GetRequiredService<IEmailSender<BaGetUser>>());
    }

    [Fact]
    public void AccountsDisabledRegistersNoSender()
    {
        using var provider = BuildProvider(new() { { "Accounts:Enabled", "false" } });

        Assert.Null(provider.GetService<IEmailSender<BaGetUser>>());
    }

    [Fact]
    public void TheMessageCarriesTheConfiguredSenderRecipientAndLink()
    {
        var sender = NewSender(new EmailOptions { Smtp = { Host = "h", From = "BaGet <baget@example.com>" } });

        var message = sender.BuildMessage("alice@example.com", "Confirm", "<a href=\"https://x/c\">go</a>");

        Assert.Equal("baget@example.com", message.From.Mailboxes.Single().Address);
        Assert.Equal("alice@example.com", message.To.Mailboxes.Single().Address);
        Assert.Equal("Confirm", message.Subject);
        Assert.Contains("https://x/c", message.HtmlBody);
    }

    [Fact]
    public void ALinkIsHtmlEncodedIntoTheBody()
    {
        var body = SmtpEmailSender.LinkBody("Click", "https://x/?a=1&b=<2>");

        Assert.Contains("a=1&amp;b=&lt;2&gt;", body);
    }

    [Fact]
    public async Task ASendFailureIsLoggedAndReachesTheCaller()
    {
        var logs = new List<string>();
        var sender = new SmtpEmailSender(
            Options.Create(new EmailOptions
            {
                // Nothing listens on port 1, so the connection is refused.
                Smtp = { Host = "127.0.0.1", Port = 1, From = "baget@example.com", EnableSsl = false },
            }),
            new ListLogger<SmtpEmailSender>(logs));

        await Assert.ThrowsAnyAsync<Exception>(
            () => sender.SendConfirmationLinkAsync(new BaGetUser { UserName = "alice" }, "a@example.com", "https://x/c"));

        Assert.Contains(logs, l => l.StartsWith("Error"));
    }

    [Fact]
    public async Task TheHarnessCapturesWhatWouldHaveBeenSent()
    {
        using var app = new BaGetApplication(_output, configOverrides: new() { { "Accounts:Enabled", "true" } });
        using var client = app.CreateClient();
        using var scope = app.Services.CreateScope();
        var sender = scope.ServiceProvider.GetRequiredService<IEmailSender<BaGetUser>>();

        await sender.SendConfirmationLinkAsync(new BaGetUser { UserName = "alice" }, "a@example.com", "https://x/c");

        var sent = Assert.Single(app.Services.GetRequiredService<CapturingEmailSender>().Sent);
        Assert.Equal(("confirmation", "a@example.com", "https://x/c"), (sent.Kind, sent.To, sent.Payload));
    }

    [Fact]
    public async Task ResettingAPasswordGivesAWorkingOneAndClearsTwoFactorAndLockout()
    {
        using var app = new BaGetApplication(_output, configOverrides: new() { { "Accounts:Enabled", "true" } });
        using var client = app.CreateClient();

        using (var scope = app.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<BaGetUser>>();
            var created = await users.CreateAsync(
                new BaGetUser { UserName = "alice", Email = "a@example.com", Created = DateTime.UtcNow, TwoFactorEnabled = true },
                "Old-Passw0rd!");
            Assert.True(created.Succeeded);
            var alice = await users.FindByNameAsync("alice");
            #pragma warning disable CS8604
            await users.SetLockoutEndDateAsync(alice, DateTimeOffset.UtcNow.AddDays(1));
            #pragma warning restore CS8604
        }

        string? password;
        using (var scope = app.Services.CreateScope())
        {
            var recovery = scope.ServiceProvider.GetRequiredService<AccountRecoveryService>();
            password = await recovery.ResetPasswordAsync("alice", CancellationToken.None);
        }

        using (var scope = app.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<BaGetUser>>();
            var alice = await users.FindByNameAsync("alice");

            Assert.NotNull(password);
            #pragma warning disable CS8604
            Assert.True(await users.CheckPasswordAsync(alice, password));
            #pragma warning restore CS8604
            Assert.False(await users.CheckPasswordAsync(alice, "Old-Passw0rd!"));
            Assert.False(alice.TwoFactorEnabled);
            Assert.False(await users.IsLockedOutAsync(alice));
        }
    }

    [Fact]
    public async Task ResettingAnUnknownUserReturnsNull()
    {
        using var app = new BaGetApplication(_output, configOverrides: new() { { "Accounts:Enabled", "true" } });
        using var client = app.CreateClient();
        using var scope = app.Services.CreateScope();

        var recovery = scope.ServiceProvider.GetRequiredService<AccountRecoveryService>();

        Assert.Null(await recovery.ResetPasswordAsync("nobody", CancellationToken.None));
    }

    [Fact]
    public void GeneratedPasswordsSatisfyTheDefaultPolicyAndDiffer()
    {
        var first = AccountRecoveryService.GeneratePassword();
        var second = AccountRecoveryService.GeneratePassword();

        Assert.NotEqual(first, second);
        Assert.True(first.Length >= 16);
        Assert.Contains(first, char.IsUpper);
        Assert.Contains(first, char.IsLower);
        Assert.Contains(first, char.IsDigit);
        Assert.Contains(first, c => !char.IsLetterOrDigit(c));
    }

    private static SmtpEmailSender NewSender(EmailOptions options)
        => new(Options.Create(options), new ListLogger<SmtpEmailSender>(new List<string>()));

    private static bool RequireConfirmedEmail(Dictionary<string, string>? settings)
    {
        using var provider = BuildProvider(settings);

        return provider.GetRequiredService<IOptions<IdentityOptions>>().Value.SignIn.RequireConfirmedEmail;
    }

    private static ServiceProvider BuildProvider(Dictionary<string, string>? settings)
    {
        #pragma warning disable CS8602
        settings.TryAdd("Accounts:Enabled", "true");
        #pragma warning restore CS8602
        #pragma warning disable CS8620
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        #pragma warning restore CS8620

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddBaGetOptions<EmailOptions>("Email");
        services.AddBaGetAccounts(configuration);

        return services.BuildServiceProvider();
    }

    private sealed class ListLogger<T> : ILogger<T>
    {
        private readonly List<string> _lines;

        public ListLogger(List<string> lines) => _lines = lines;

        #pragma warning disable CS8633
        public IDisposable? BeginScope<TState>(TState state) => null;
        #pragma warning restore CS8633

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => _lines.Add($"{logLevel}: {formatter(state, exception)}");
    }
}
