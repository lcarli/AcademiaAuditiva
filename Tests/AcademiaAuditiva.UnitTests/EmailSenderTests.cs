using AcademiaAuditiva.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MimeKit;
using Moq;

namespace AcademiaAuditiva.UnitTests;

public class EmailSenderTests
{
    private static readonly EmailMessage Message = new("Subject", "<p>Body</p>", "Body\n");

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

        await sender.Invoking(s => s.SendEmailAsync("student@example.test", Message)).Should().NotThrowAsync();
        await sender.Invoking(s => s.SendEmailAsync("student@example.test", "Subject", "<p>Body</p>"))
            .Should().NotThrowAsync("Identity's default UI sends through IEmailSender");
    }

    [Fact]
    public void Messages_CarryTheHtmlAndThePlainText()
    {
        var sender = new EmailSender(Options.Create(Resend()), NullLogger<EmailSender>.Instance);

        var mime = sender.CreateMessage("student@example.test", Message);

        mime.Body.Should().BeOfType<MultipartAlternative>("mail programs pick the HTML or the text");
        mime.HtmlBody.Should().Be("<p>Body</p>");
        mime.TextBody.ReplaceLineEndings("\n").Should().Be("Body\n", "MIME sends lines ending in CRLF");
        mime.Subject.Should().Be("Subject");
        mime.From.Mailboxes.Should().ContainSingle().Which.Should()
            .BeEquivalentTo(new { Name = "Academia Auditiva", Address = "no-reply@academiaauditiva.com" });
        mime.To.Mailboxes.Should().ContainSingle().Which.Address.Should().Be("student@example.test");
    }

    [Fact]
    public async Task TrySendEmailAsync_ReportsAFailedSend_WithoutThrowing()
    {
        var sender = new Mock<IEmailMessageSender>();
        sender.Setup(s => s.SendEmailAsync(It.IsAny<string>(), It.IsAny<EmailMessage>()))
            .ThrowsAsync(new InvalidOperationException("SMTP is down"));

        (await sender.Object.TrySendEmailAsync("student@example.test", Message)).Should().BeFalse();
    }

    [Fact]
    public async Task TrySendEmailAsync_ReportsASuccessfulSend()
    {
        var sender = new Mock<IEmailMessageSender>();
        sender.Setup(s => s.SendEmailAsync(It.IsAny<string>(), It.IsAny<EmailMessage>()))
            .Returns(Task.CompletedTask);

        (await sender.Object.TrySendEmailAsync("student@example.test", Message)).Should().BeTrue();
        sender.Verify(s => s.SendEmailAsync("student@example.test", Message), Times.Once);
    }
}
