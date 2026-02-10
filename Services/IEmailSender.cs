namespace SkinAI.API.Services
{
    public interface IEmailSender
    {
        Task SendAsync(string toEmail, string subject, string body, CancellationToken ct);
    }
}
