using System.ComponentModel.DataAnnotations;

namespace SkinAI.API.Dtos.Chat
{
    public class SendMessageDto
    {
        [Required]
        [MaxLength(2000)]
        public string MessageText { get; set; } = "";
    }
}
