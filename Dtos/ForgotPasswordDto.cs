using System.ComponentModel.DataAnnotations;

namespace SkinAI.API.Dtos.Auth
{
    public class ForgotPasswordDto
    {
        [Required, EmailAddress]
        public string Email { get; set; } = "";
    }
}
