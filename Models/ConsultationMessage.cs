using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using SkinAI.API.Enums;

namespace SkinAI.API.Models
{
    public class ConsultationMessage
    {
        [Key]
        public int Id { get; set; }

        public int ConsultationId { get; set; }

        [ForeignKey(nameof(ConsultationId))]
        public Consultation Consultation { get; set; } = null!;

        // Sender = أي User (Doctor أو Patient)
        public int SenderId { get; set; }

        [ForeignKey(nameof(SenderId))]
        public User Sender { get; set; } = null!;

        // ✅ نوع الرسالة
        public MessageType Type { get; set; } = MessageType.Text;

        // ✅ Text (اختياري لو Voice)
        [MaxLength(2000)]
        public string? MessageText { get; set; }

        // ✅ Voice note (اختياري لو Text)
        [MaxLength(300)]
        public string? VoiceUrl { get; set; }     // /uploads/voice/xxx.m4a

        public int? VoiceDurationSec { get; set; } // optional
        public string? FileUrl { get; set; }
        public string? FileName { get; set; }
        public long? FileSize { get; set; }

        public DateTime Timestamp { get; set; } = DateTime.UtcNow;

        // read receipts
        public bool IsRead { get; set; } = false;
    }
}
