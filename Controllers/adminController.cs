using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SkinAI.API.Data;
using SkinAI.API.Models;
using SkinAI.API.Services;

namespace SkinAI.API.Controllers
{
    [ApiController]
    [Route("api/admin/doctors")]
    [Authorize(Roles = "Admin")]
    public class AdminDoctorsController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<User> _userManager;
        private readonly INotificationService _notificationService;
        private readonly IWebHostEnvironment _env;

        public AdminDoctorsController(
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

        [HttpGet("pending")]
        public async Task<IActionResult> GetPendingDoctors(CancellationToken ct)
        {
            var doctors = await _context.Doctors
                .AsNoTracking()
                .Include(d => d.User)
                .Where(d => !d.IsApproved)
                .OrderByDescending(d => d.Id)
                .Select(d => new
                {
                    d.Id,
                    d.UserId,
                    d.User.FullName,
                    
                    d.User.Email,
                    d.MedicalLicenseNumber,
                    d.Specialization,
                    d.YearsOfExperience,
                    d.IdCardImagePath,
                    d.IdCardUploadedAt
                })
                .ToListAsync(ct);

            return Ok(new { ok = true, data = doctors });
        }

        // Admin can view the ID card image securely
        [HttpGet("{doctorId:int}/id-card")]
        public async Task<IActionResult> GetDoctorIdCard([FromRoute] int doctorId, CancellationToken ct)
        {
            var path = await _context.Doctors
                .AsNoTracking()
                .Where(d => d.Id == doctorId)
                .Select(d => d.IdCardImagePath)
                .FirstOrDefaultAsync(ct);

            if (string.IsNullOrWhiteSpace(path))
                return NotFound(new { ok = false, error = "ID card image not found." });

            var webRoot = _env.WebRootPath ?? Path.Combine(_env.ContentRootPath, "wwwroot");
            var physical = Path.Combine(webRoot, path.TrimStart('/').Replace("/", Path.DirectorySeparatorChar.ToString()));

            if (!System.IO.File.Exists(physical))
                return NotFound(new { ok = false, error = "File missing on server." });

            var ext = Path.GetExtension(physical).ToLowerInvariant();
            var contentType = ext switch
            {
                ".png" => "image/png",
                ".webp" => "image/webp",
                _ => "image/jpeg"
            };

            return PhysicalFile(physical, contentType);
        }

        [HttpPost("approve/{doctorId:int}")]
        public async Task<IActionResult> ApproveDoctor([FromRoute] int doctorId, CancellationToken ct)
        {
            var doctor = await _context.Doctors
                .Include(d => d.User)
                .FirstOrDefaultAsync(d => d.Id == doctorId, ct);

            if (doctor == null)
                return NotFound(new { ok = false, error = "Doctor not found" });

            if (doctor.IsApproved)
                return Ok(new { ok = true, message = "Doctor already approved" });

            doctor.IsApproved = true;
            doctor.User.IsApproved = true;

            await _context.SaveChangesAsync(ct);

            if (!await _userManager.IsInRoleAsync(doctor.User, "Doctor"))
                await _userManager.AddToRoleAsync(doctor.User, "Doctor");

            await _notificationService.NotifyDoctorApprovalAsync(doctor.Id);

            return Ok(new { ok = true, message = "Doctor approved successfully" });
        }

        public class RejectDoctorDto
        {
            public string Reason { get; set; } = "Not specified";
        }

        [HttpPost("reject/{doctorId:int}")]
        public async Task<IActionResult> RejectDoctor([FromRoute] int doctorId, [FromBody] RejectDoctorDto dto, CancellationToken ct)
        {
            var doctor = await _context.Doctors
                .Include(d => d.User)
                .FirstOrDefaultAsync(d => d.Id == doctorId, ct);

            if (doctor == null)
                return NotFound(new { ok = false, error = "Doctor not found" });

            doctor.IsApproved = false;
            doctor.User.IsApproved = false;

            await _context.SaveChangesAsync(ct);

            await _notificationService.NotifyDoctorRejectionAsync(doctor.Id, dto.Reason);

            return Ok(new { ok = true, message = "Doctor rejected" });
        }
    }
}
