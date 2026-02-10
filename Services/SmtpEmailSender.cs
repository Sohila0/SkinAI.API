using Microsoft.Extensions.Options;
using SkinAI.API.Models;
using System.Net;
using System.Net.Mail;

namespace SkinAI.API.Services
{
    public interface IEmailService
    {
        Task SendAsync(string toEmail, string subject, string htmlBody, CancellationToken ct = default);
    }

    public class EmailService : IEmailService
    {
        private readonly EmailSettings _settings;

        public EmailService(IOptions<EmailSettings> options)
        {
            _settings = options.Value;
        }

        public async Task SendAsync(string toEmail, string subject, string htmlBody, CancellationToken ct = default)
        {
            using var msg = new MailMessage();
            msg.From = new MailAddress(_settings.FromEmail, _settings.FromName);
            msg.To.Add(toEmail);
            msg.Subject = subject;
            msg.Body = htmlBody;
            msg.IsBodyHtml = true;

            using var smtp = new SmtpClient(_settings.Host, _settings.Port);
            smtp.EnableSsl = _settings.EnableSsl;
            smtp.Credentials = new NetworkCredential(_settings.Username, _settings.Password);

            // SmtpClient مفيهوش CancellationToken رسمي، بس ده كفاية للمشروع
            await smtp.SendMailAsync(msg);
        }
    }
}
