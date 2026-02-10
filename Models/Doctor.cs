using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SkinAI.API.Models
{
    public class Doctor
    {
        [Key]
        public int Id { get; set; }

        public int UserId { get; set; }

        [ForeignKey(nameof(UserId))]
        public User User { get; set; } = null!;

        [Required]
        [MaxLength(100)]
        public string MedicalLicenseNumber { get; set; } = string.Empty;

        [MaxLength(100)]
        public string? Specialization { get; set; }

        public int YearsOfExperience { get; set; }

        public bool IsApproved { get; set; } = false;

        public decimal ConsultationPrice { get; set; } = 0;
        public string FullName { get; set; } = "";
        public double AverageRating { get; set; } = 0;
        public int TotalReviews { get; set; } = 0;
        public string? VerificationFileUrl { get; set; }     // /uploads/doctor-verification/xxx.jpg
        public string? VerificationFileName { get; set; }    // original name (optional)
        public DateTime? VerificationUploadedAt { get; set; }
        public string? IdCardImagePath { get; set; }
        public DateTime?  IdCardUploadedAt { get; set; }

    }
}
