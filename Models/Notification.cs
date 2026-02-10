using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using SkinAI.API.Services;

namespace SkinAI.API.Models
{
    public class Notification
    {
        [Key]
        public int Id { get; set; }

        // ================= USER RELATION =================
        public int UserId { get; set; }

        [ForeignKey(nameof(UserId))]
        public User User { get; set; } = null!;

        // ================= CONTENT =================
        [Required]
        [MaxLength(200)]
        public string Title { get; set; } = null!;

        [Required]
        [MaxLength(1000)]
        public string Message { get; set; } = null!;

        // ================= TYPE & STATUS =================
        public NotificationType Type { get; set; }

        public bool IsRead { get; set; } = false;

        public DateTime? ReadAt { get; set; }

        // ================= OPTIONAL LINKING =================
        // يربط الإشعار بحاجة في السيستم (استشارة – دفع – عرض…)
        public int? RelatedEntityId { get; set; }

        [MaxLength(50)]
        public string? RelatedEntityType { get; set; }
        // مثال: "Consultation", "Payment", "Offer", "Message"

        // ================= METADATA =================
        [MaxLength(100)]
        public string? ActionUrl { get; set; }
        // مثال: /consultations/5  أو deeplink للموبايل

        public bool IsDeleted { get; set; } = false;
        // Soft delete لو حبينا نمسح إشعارات قديمة

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
