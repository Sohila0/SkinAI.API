using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SkinAI.API.Data;
using System.ComponentModel.DataAnnotations;
using System.Security.Claims;

namespace SkinAI.API.Controllers
{
    [ApiController]
    [Route("api/patient")]
    [Authorize(Roles = "Patient")]
    public class PatientController : ControllerBase
    {
        private readonly ApplicationDbContext _context;

        public PatientController(ApplicationDbContext context)
        {
            _context = context;
        }

        // GET: api/patient/me
        [HttpGet("me")]
        public async Task<IActionResult> GetMyProfile(CancellationToken ct)
        {
            var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrWhiteSpace(userIdStr))
                return Unauthorized(new { ok = false, error = "Missing user id." });

            var userId = int.Parse(userIdStr);

            var patient = await _context.Patients
                .AsNoTracking()
                .Include(p => p.User)
                .Where(p => p.UserId == userId)
                .Select(p => new
                {
                    ok = true,
                    patientId = p.Id,
                    userId = p.UserId,
                    fullName = p.User.FullName,
                    
                    email = p.User.Email,
                    phoneNumber = p.User.PhoneNumber,
                    dateOfBirth = p.DateOfBirth,
                    gender = p.Gender,
                    createdAt = p.CreatedAt
                })
                .FirstOrDefaultAsync(ct);

            if (patient == null)
                return NotFound(new { ok = false, error = "Patient profile not found." });

            return Ok(patient);
        }

        public class UpdatePatientProfileDto
        {
            [MaxLength(100)] public string? FirstName { get; set; }
            [MaxLength(100)] public string? LastName { get; set; }
            [MaxLength(30)] public string? PhoneNumber { get; set; }

            public DateTime? DateOfBirth { get; set; }
            [MaxLength(20)] public string? Gender { get; set; }
        }

        // PUT: api/patient/me
        [HttpPut("me")]
        public async Task<IActionResult> UpdateMyProfile([FromBody] UpdatePatientProfileDto dto, CancellationToken ct)
        {
            if (dto == null)
                return BadRequest(new { ok = false, error = "Missing body." });

            var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrWhiteSpace(userIdStr))
                return Unauthorized(new { ok = false, error = "Missing user id." });

            var userId = int.Parse(userIdStr);

            var patient = await _context.Patients
                .Include(p => p.User)
                .FirstOrDefaultAsync(p => p.UserId == userId, ct);

            if (patient == null)
                return NotFound(new { ok = false, error = "Patient profile not found." });

            if (!string.IsNullOrWhiteSpace(dto.FirstName))
                patient.User.FullName = dto.FirstName.Trim();

            

            if (!string.IsNullOrWhiteSpace(dto.PhoneNumber))
                patient.User.PhoneNumber = dto.PhoneNumber.Trim();

            if (dto.DateOfBirth.HasValue)
                patient.DateOfBirth = dto.DateOfBirth.Value;

           

            await _context.SaveChangesAsync(ct);

            return Ok(new { ok = true, message = "Profile updated successfully." });
        }
    }
}
