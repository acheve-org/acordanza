namespace Acordanza.Api.Services;

// Fallback used when Resend:ApiKey isn't configured (e.g. local dev), so the
// forgot-password flow can be exercised without a Resend account — the
// email body just lands in the console log instead of an inbox.
public class ConsoleEmailSender(ILogger<ConsoleEmailSender> logger) : IEmailSender
{
    public Task SendAsync(string toEmail, string subject, string htmlBody)
    {
        logger.LogInformation(
            "Resend:ApiKey not configured — logging email instead of sending it.\nTo: {ToEmail}\nSubject: {Subject}\n{HtmlBody}",
            toEmail, subject, htmlBody);
        return Task.CompletedTask;
    }
}
