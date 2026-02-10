using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SkinAI.API.Data;
using SkinAI.API.Dtos;
using SkinAI.API.Models;
using SkinAI.API.Services;
using System.ComponentModel.DataAnnotations;
using System.Security.Claims;

namespace SkinAI.API.Controllers
{
    [ApiController]
    [Route("api/doctors")]
    public class DoctorsController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<User> _userManager;
        private readonly INotificationService _notificationService;
        private readonly IWebHostEnvironment _env;

        public DoctorsController(
            ApplicationDbContext context,
            UserManager<User> userManager,
            INotificationService notificationService,
            IWebHostEnvironment env)
        {
            _context = context;
            _userManager = userManager;
            _notificationService = notificationService;
            _env = env;
        }

        // ========================= REGISTER (multipart/form-data) =========================
        [HttpPost("register")]
        [AllowAnonymous]
        [Consumes("multipart/form-data")]
        public async Task<IActionResult> RegisterDoctor([FromForm] DoctorRegistrationFormDto dto, CancellationToken ct)
        {
            if (!ModelState.IsValid)
                return BadRequest(new { ok = false, error = "Invalid data", details = ModelState });

            dto.Email = dto.Email.Trim();
            dto.FullName = dto.FullName.Trim();
            
            dto.MedicalLicenseNumber = dto.MedicalLicenseNumber.Trim();
            dto.Specialization = dto.Specialization?.Trim();

            var existing = await _userManager.FindByEmailAsync(dto.Email);
            if (existing != null)
                return BadRequest(new { ok = false, error = "Email already exists" });

            // file required
            if (dto.IdCardImage == null || dto.IdCardImage.Length == 0)
                return BadRequest(new { ok = false, error = "IdCardImage is required" });

            // limit size (5MB)
            const long maxBytes = 5 * 1024 * 1024;
            if (dto.IdCardImage.Length > maxBytes)
                return BadRequest(new { ok = false, error = "Image too large. Max 5MB." });

            // validate extension + content type
            var ext = Path.GetExtension(dto.IdCardImage.FileName).ToLowerInvariant();
            var contentType = (dto.IdCardImage.ContentType ?? "").ToLowerInvariant();

            var allowedExt = new HashSet<string> { ".jpg", ".jpeg", ".png" };
            if (!allowedExt.Contains(ext))
                return BadRequest(new { ok = false, error = "Only JPG/PNG images are allowed" });

            if (!(contentType.Contains("jpeg") || contentType.Contains("jpg") || contentType.Contains("png")))
                return BadRequest(new { ok = false, error = "Invalid image content type" });

            // create user (pending)
            var user = new User
            {
                UserName = dto.Email,
                Email = dto.Email,
                FullName = dto.FullName,
               
                PhoneNumber = dto.PhoneNumber.ToString(),
                Role = "Doctor",
                IsApproved = false,
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            };

            var create = await _userManager.CreateAsync(user, dto.Password);
            if (!create.Succeeded)
                return BadRequest(new { ok = false, error = "Failed", errors = create.Errors.Select(e => e.Description) });

            // DO NOT add Identity role Doctor now (only after admin approval)

            // save id card image
            var webRoot = _env.WebRootPath ?? Path.Combine(_env.ContentRootPath, "wwwroot");
            var folder = Path.Combine(webRoot, "uploads", "doctor-ids");
            Directory.CreateDirectory(folder);

            var fileName = $"{Guid.NewGuid():N}{ext}";
            var fullPath = Path.Combine(folder, fileName);

            await using (var fs = new FileStream(fullPath, FileMode.Create))
            {
                await dto.IdCardImage.CopyToAsync(fs, ct);
            }

            var publicPath = $"/uploads/doctor-ids/{fileName}";

            var doctor = new Doctor
            {
                UserId = user.Id,
                MedicalLicenseNumber = dto.MedicalLicenseNumber,
                Specialization = dto.Specialization,
                YearsOfExperience = dto.YearsOfExperience,
                IsApproved = false,
                IdCardImagePath = publicPath,
                IdCardUploadedAt = DateTime.UtcNow
            };

            _context.Doctors.Add(doctor);
            await _context.SaveChangesAsync(ct);

            await _notificationService.NotifyAdminsOfNewDoctorAsync(doctor.Id);

            return Ok(new
            {
                ok = true,
                message = "Doctor registered. Waiting for admin approval.",
                doctorId = doctor.Id,
                userId = user.Id,
                idCardImagePath = doctor.IdCardImagePath
            });
        }

        // ========================= GET MY PROFILE =========================
        [HttpGet("me")]
        [Authorize(Roles = "Doctor")]
        public async Task<IActionResult> GetMyProfile(CancellationToken ct)
        {
            var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrWhiteSpace(userIdStr))
                return Unauthorized(new { ok = false, error = "Missing user id." });

            var userId = int.Parse(userIdStr);

            var d = await _context.Doctors
                .AsNoTracking()
                .Include(x => x.User)
                .FirstOrDefaultAsync(x => x.UserId == userId, ct);

            if (d == null)
                return NotFound(new { ok = false, error = "Doctor not found" });

            var idCardImageUrl = string.IsNullOrWhiteSpace(d.IdCardImagePath)
                ? null
                : $"{Request.Scheme}://{Request.Host}{d.IdCardImagePath}";

            return Ok(new
            {
                ok = true,
                d.Id,
                d.UserId,
                d.IsApproved,
                firstName = d.User.FullName,
                
                email = d.User.Email,
                phoneNumber = d.User.PhoneNumber,
                d.MedicalLicenseNumber,
                d.Specialization,
                d.YearsOfExperience,
                d.ConsultationPrice,
                d.AverageRating,
                d.TotalReviews,
                idCardImagePath = d.IdCardImagePath,
                idCardImageUrl,
                d.IdCardUploadedAt
            });
        }

        // ========================= EDIT PROFILE (Doctor) =========================
        public class UpdateDoctorProfileDto
        {
            [MaxLength(100)] public string? FirstName { get; set; }
            [MaxLength(100)] public string? LastName { get; set; }
            [MaxLength(30)] public string? PhoneNumber { get; set; }

            [MaxLength(100)] public string? Specialization { get; set; }
            public int? YearsOfExperience { get; set; }
            public decimal? ConsultationPrice { get; set; }
        }

        [HttpPut("me")]
        [Authorize(Roles = "Doctor")]
        public async Task<IActionResult> UpdateMyProfile([FromBody] UpdateDoctorProfileDto dto, CancellationToken ct)
        {
            if (dto == null)
                return BadRequest(new { ok = false, error = "Missing body." });

            var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrWhiteSpace(userIdStr))
                return Unauthorized(new { ok = false, error = "Missing user id." });

            var userId = int.Parse(userIdStr);

            var doctor = await _context.Doctors
                .Include(d => d.User)
                .FirstOrDefaultAsync(d => d.UserId == userId, ct);

            if (doctor == null)
                return NotFound(new { ok = false, error = "Doctor not found." });

            if (!string.IsNullOrWhiteSpace(dto.FirstName))
                doctor.User.FullName = dto.FirstName.Trim();

            

            if (!string.IsNullOrWhiteSpace(dto.PhoneNumber))
                doctor.User.PhoneNumber = dto.PhoneNumber.Trim();

            if (!string.IsNullOrWhiteSpace(dto.Specialization))
                doctor.Specialization = dto.Specialization.Trim();

            if (dto.YearsOfExperience.HasValue && dto.YearsOfExperience.Value >= 0)
                doctor.YearsOfExperience = dto.YearsOfExperience.Value;

            if (dto.ConsultationPrice.HasValue && dto.ConsultationPrice.Value >= 0)
                doctor.ConsultationPrice = dto.ConsultationPrice.Value;

            await _context.SaveChangesAsync(ct);

            return Ok(new { ok = true, message = "Profile updated successfully." });
        }

        // ========================= DOCTOR RECORDS (Approved only) =========================
        [HttpGet("records")]
        [Authorize(Roles = "Doctor")]
        public async Task<IActionResult> DoctorRecords(CancellationToken ct)
        {
            var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrWhiteSpace(userIdStr))
                return Unauthorized(new { ok = false, error = "Missing user id." });

            var userId = int.Parse(userIdStr);

            var doctor = await _context.Doctors.FirstOrDefaultAsync(d => d.UserId == userId, ct);
            if (doctor == null)
                return BadRequest(new { ok = false, error = "Doctor not found." });

            if (!doctor.IsApproved)
                return Forbid();

            var records = await _context.Consultations
                .AsNoTracking()
                .Where(x => x.DoctorId == doctor.Id)
                .OrderByDescending(x => x.CreatedAt)
                .Select(x => new
                {
                    x.Id,
                    x.PatientId,
                    x.FinalDiagnosis,
                    x.CreatedAt
                })
                .ToListAsync(ct);

            return Ok(new { ok = true, data = records });
        }
    }
}
