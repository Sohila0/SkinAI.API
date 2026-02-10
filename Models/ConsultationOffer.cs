using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using SkinAI.API.Enums;

namespace SkinAI.API.Models
{
    public class ConsultationOffer
    {
        [Key]
        public int Id { get; set; }

        public int ConsultationId { get; set; }

        [ForeignKey(nameof(ConsultationId))]
        public Consultation Consultation { get; set; } = null!;

        public int DoctorId { get; set; }

        [ForeignKey(nameof(DoctorId))]
        public Doctor Doctor { get; set; } = null!;

        public decimal Price { get; set; }

        public string? Notes { get; set; }

        public OfferStatus Status { get; set; } = OfferStatus.ACTIVE;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
