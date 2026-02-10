using Microsoft.AspNetCore.Identity;
using System.ComponentModel.DataAnnotations;

namespace SkinAI.API.Models
{
    public class User : IdentityUser<int>
    {
        [Required]
        [MaxLength(100)]
        public string FullName { get; set; }

        [MaxLength(20)]
        public string? Role { get; set; } // Admin, Doctor, Patient

        public bool IsApproved { get; set; } = false;
        // ✅ Patient & Admin = true
        // ✅ Doctor = false لحد ما يتوافق عليه

        public bool IsActive { get; set; } = true;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? LastLoginAt { get; set; }

        public virtual Doctor? Doctor { get; set; }
        public virtual Patient? Patient { get; set; }
        public ICollection<Notification> Notifications { get; set; } = new List<Notification>();
        public string? Address { get; set; }
        public double? Latitude { get; set; }
        public double? Longitude { get; set; }
    }
}
