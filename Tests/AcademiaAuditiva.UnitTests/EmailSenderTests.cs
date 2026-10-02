using AcademiaAuditiva.Services;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace AcademiaAuditiva.UnitTests;

public class EmailSenderTests
{
    // Production's shape: Resend signs in as "resend" with an API key.
    private static SmtpOptions Resend(string fromAddress = "no-reply@academiaauditiva.com") => new()
    {
        Host = "smtp.resend.com",
        Port = 465,
        User = "resend",
        Password = "x",
        FromAddress = fromAddress,
    };

    [Fact]
    public void Resend_IsConfigured_AndSendsFromTheFromAddress()
    {
        var options = Resend();

        options.IsConfigured.Should().BeTrue();
        options.SenderAddress.Should().Be("no-reply@academiaauditiva.com");
    }

    [Theory]
    [InlineData("")]
    [InlineData("no-reply")]
    [InlineData("Academia Auditiva <no-reply@academiaauditiva.com>")]
    public void UserNameSignIn_WithoutAPlainFromAddress_IsNotConfigured(string fromAddress)
    {
        // "resend" is no address to send from, so mail would be rejected.
        Resend(fromAddress).IsConfigured.Should().BeFalse();
    }

    [Fact]
    public void EmailSignIn_SendsFromTheUser_WhenThereIsNoFromAddress()
    {
        var options = new SmtpOptions { Host = "smtp.example.test", User = "me@example.test", Password = "x" };

        options.SenderAddress.Should().Be("me@example.test");
        options.IsConfigured.Should().BeTrue();
    }

    [Theory]
    [InlineData("", "resend", "x")]
    [InlineData("smtp.resend.com", "", "x")]
    [InlineData("smtp.resend.com", "resend", " ")]
    public void MissingServerOrCredentials_IsNotConfigured(string host, string user, string password)
    {
        var options = Resend();
        options.Host = host;
        options.User = user;
        options.Password = password;

        options.IsConfigured.Should().BeFalse();
    }

    [Fact]
    public async Task SendEmailAsync_SkipsSending_WhenThereIsNoSenderAddress()
    {
        // The .invalid host never resolves, so trying to connect would throw.
        var options = Resend(fromAddress: "");
        options.Host = "smtp.invalid";
        var sender = new EmailSender(Options.Create(options), NullLogger<EmailSender>.Instance);

        var send = () => sender.SendEmailAsync("student@example.test", "Subject", "<p>Body</p>");

        await send.Should().NotThrowAsync();
    }

    [Fact]
    public async Task TrySendEmailAsync_ReportsAFailedSend_WithoutThrowing()
    {
        var sender = new Mock<IEmailSender>();
        sender.Setup(s => s.SendEmailAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ThrowsAsync(new InvalidOperationException("SMTP is down"));

        (await sender.Object.TrySendEmailAsync("student@example.test", "Subject", "<p>Body</p>")).Should().BeFalse();
    }

    [Fact]
    public async Task TrySendEmailAsync_ReportsASuccessfulSend()
    {
        var sender = new Mock<IEmailSender>();
        sender.Setup(s => s.SendEmailAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .Returns(Task.CompletedTask);

        (await sender.Object.TrySendEmailAsync("student@example.test", "Subject", "<p>Body</p>")).Should().BeTrue();
        sender.Verify(s => s.SendEmailAsync("student@example.test", "Subject", "<p>Body</p>"), Times.Once);
    }
}
