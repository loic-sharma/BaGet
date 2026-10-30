using System;
using System.IO;
using System.Linq;
using BaGet.Core;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MimeKit;
using Xunit;
using BaGet.Identity.Application.Email;

namespace BaGet.Tests;

/// <summary>
/// The contact message as <see cref="SmtpEmailSender"/> builds it, which the integration tests
/// cannot see because they replace the sender.
/// </summary>
public class SmtpEmailSenderTests
{
    private readonly SmtpEmailSender _target = new(
        Options.Create(new EmailOptions { Smtp = new SmtpOptions { Host = "smtp.example.com", From = "noreply@example.com" } }),
        NullLogger<SmtpEmailSender>.Instance);

    private static string Serialize(MimeMessage? message)
    {
        using var stream = new MemoryStream();
        #pragma warning disable CS8602
        message.WriteTo(stream);
        #pragma warning restore CS8602
        return System.Text.Encoding.UTF8.GetString(stream.ToArray());
    }

    private static string[] HeaderLines(string raw)
        => raw.Split(new[] { "\r\n" }, StringSplitOptions.None).TakeWhile(l => l.Length > 0).ToArray();

    [Fact]
    public void ControlCharactersInTheSubjectCannotStartAnotherHeader()
    {
        var message = _target.BuildContactMessage(
            "owner@example.com", "sender@example.com", "Sender",
            "About Pkg.Id\r\nBcc: victim@example.com", "body");

        Assert.Equal("About Pkg.IdBcc: victim@example.com", message.Subject);
        var headers = HeaderLines(Serialize(message));
        Assert.DoesNotContain(headers, h => h.StartsWith("Bcc:", StringComparison.OrdinalIgnoreCase));
        Assert.Empty(message.Bcc);
    }

    [Fact]
    public void ControlCharactersInTheSenderNameCannotStartAnotherHeader()
    {
        var message = _target.BuildContactMessage(
            "owner@example.com", "sender@example.com", "Eve\r\nBcc: victim@example.com",
            "Subject", "body");

        var replyTo = Assert.Single(message.ReplyTo.Mailboxes);
        Assert.Equal("EveBcc: victim@example.com", replyTo.Name);
        Assert.Equal("sender@example.com", replyTo.Address);
        Assert.DoesNotContain(HeaderLines(Serialize(message)), h => h.StartsWith("Bcc:", StringComparison.OrdinalIgnoreCase));
        Assert.Empty(message.Bcc);
    }

    [Fact]
    public void ALineBreakInTheReplyToAddressNeverReachesTheHeaders()
    {
        MimeMessage? message = null;
        var thrown = Record.Exception(() => message = _target.BuildContactMessage(
            "owner@example.com", "sender@example.com\r\nBcc: victim@example.com", "Sender",
            "Subject", "body"));

        if (thrown == null)
        {
            Assert.DoesNotContain(HeaderLines(Serialize(message)), h => h.StartsWith("Bcc:", StringComparison.OrdinalIgnoreCase));
            #pragma warning disable CS8602
            Assert.Empty(message.Bcc);
            #pragma warning restore CS8602
        }
    }

    [Fact]
    public void TheBodyIsPlainTextAndKeepsItsLineBreaks()
    {
        var message = _target.BuildContactMessage(
            "owner@example.com", "sender@example.com", "Sender",
            "Subject", "Hi\r\nBcc: victim@example.com");

        var body = Assert.IsType<TextPart>(message.Body);
        Assert.Equal("plain", body.ContentType.MediaSubtype);
        Assert.Contains("Bcc: victim@example.com", body.Text);
        Assert.Empty(message.Bcc);
        Assert.Equal("owner@example.com", Assert.Single(message.To.Mailboxes).Address);
    }
}
