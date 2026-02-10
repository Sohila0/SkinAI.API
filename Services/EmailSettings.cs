namespace SkinAI.API.Services
{
    public class EmailSettings
    {
        public string Host { get; set; } = "";
        public int Port { get; set; } = 587;
        public bool EnableSsl { get; set; } = true;

        public string Username { get; set; } = ""; // SMTP username
        public string Password { get; set; } = ""; // SMTP password / app password

        public string FromEmail { get; set; } = "";
        public string FromName { get; set; } = "SkinAI";

        // Deeplink used by Flutter
        public string ResetDeepLinkBase { get; set; } = "skinai://reset-password";
    }
}
