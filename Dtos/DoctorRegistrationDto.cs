using Microsoft.AspNetCore.Http;
using System.ComponentModel.DataAnnotations;

namespace SkinAI.API.Dtos
{
    public class DoctorRegistrationFormDto
    {
        [Required] public string Email { get; set; } = "";
        [Required] public string Password { get; set; } = "";
        [Required] public string FullName { get; set; } = "";
        public long PhoneNumber { get; set; }

        [Required] public string MedicalLicenseNumber { get; set; } = "";
        public string? Specialization { get; set; }
        public int YearsOfExperience { get; set; }

        // ✅ صورة الكارنيه (مطلوبة)
        [Required] public IFormFile IdCardImage { get; set; } = null!;
    }
}
