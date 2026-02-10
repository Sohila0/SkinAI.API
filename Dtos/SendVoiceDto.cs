using Microsoft.AspNetCore.Http;
using System.ComponentModel.DataAnnotations;

namespace SkinAI.API.Dtos.Chat
{
    public class SendVoiceDto
    {
        [Required]
        public IFormFile VoiceFile { get; set; } = null!;

        public int? DurationSec { get; set; }
    }
}
