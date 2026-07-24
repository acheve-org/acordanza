using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace Acordanza.Api.Services;

public class ResendEmailSender(HttpClient httpClient, IConfiguration configuration) : IEmailSender
{
    public async Task SendAsync(string toEmail, string subject, string htmlBody)
    {
        var apiKey = configuration["Resend:ApiKey"]
            ?? throw new InvalidOperationException("Missing Resend:ApiKey configuration");
        var fromEmail = configuration["Resend:FromEmail"]
            ?? throw new InvalidOperationException("Missing Resend:FromEmail configuration");
        var fromName = configuration["Resend:FromName"] ?? "Acordanza";

        var request = new HttpRequestMessage(HttpMethod.Post, "https://api.resend.com/emails")
        {
            Headers = { Authorization = new AuthenticationHeaderValue("Bearer", apiKey) },
            Content = JsonContent.Create(new
            {
                from = $"{fromName} <{fromEmail}>",
                to = new[] { toEmail },
                subject,
                html = htmlBody,
            }),
        };

        var response = await httpClient.SendAsync(request);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync();
            throw new InvalidOperationException($"Resend request failed ({(int)response.StatusCode}): {body}");
        }
    }
}
