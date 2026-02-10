using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SkinAI.API.Models
{
    public class DoctorReview
    {
        [Key]
        public int Id { get; set; }

        // ===== Relations =====
        public int ConsultationId { get; set; }

        [ForeignKey(nameof(ConsultationId))]
        public Consultation Consultation { get; set; } = null!;

        public int DoctorId { get; set; }

        [ForeignKey(nameof(DoctorId))]
        public Doctor Doctor { get; set; } = null!;

        public int PatientId { get; set; }

        [ForeignKey(nameof(PatientId))]
        public Patient Patient { get; set; } = null!;

        // ===== Review Data =====
        [Range(1, 5)]
        public int Rating { get; set; }  // 1..5

        [MaxLength(1000)]
        public string? Comment { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
