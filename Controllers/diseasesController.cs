using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SkinAI.API.Data;
using SkinAI.API.Models;
using System.Security.Claims;

namespace SkinAI.API.Controllers
{
    [Authorize]
    [ApiController]
    [Route("api/[controller]")]
    public class CasesController : ControllerBase
    {
        private readonly ApplicationDbContext _context;

        public CasesController(ApplicationDbContext context)
        {
            _context = context;
        }

        // POST: api/cases
        // Create case for the currently logged-in patient
        [HttpPost]
        public async Task<IActionResult> CreateCase([FromBody] CreateDiseaseCaseDto dto)
        {
            // userId from JWT
            var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrWhiteSpace(userIdStr))
                return Unauthorized(new { message = "Invalid token" });

            if (!int.TryParse(userIdStr, out var userId))
                return Unauthorized(new { message = "Invalid user id" });

            // Find patient profile by userId
            var patient = await _context.Patients.FirstOrDefaultAsync(p => p.UserId == userId);

            if (patient == null)
                return BadRequest(new { message = "You are not registered as a patient" });

            var dc = new DiseaseCase
            {
                PatientId = patient.Id,
                ImagePath = dto.ImagePath,
                AiDiagnosis = dto.AiDiagnosis,
                Confidence = (double)dto.Confidence,
                Notes = dto.Notes,
                CreatedAt = DateTime.UtcNow
            };

            _context.DiseaseCases.Add(dc);
            await _context.SaveChangesAsync();

            return CreatedAtAction(nameof(GetCaseById), new { id = dc.Id }, dc);
        }

        // GET: api/cases/5
        [HttpGet("{id:int}")]
        public async Task<IActionResult> GetCaseById(int id)
        {
            var dc = await _context.DiseaseCases.FirstOrDefaultAsync(c => c.Id == id);
            if (dc == null) return NotFound();

            return Ok(dc);
        }

        // GET: api/cases/my
        // Get cases for the currently logged-in patient
        [HttpGet("my")]
        public async Task<IActionResult> GetMyCases()
        {
            var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrWhiteSpace(userIdStr))
                return Unauthorized(new { message = "Invalid token" });

            if (!int.TryParse(userIdStr, out var userId))
                return Unauthorized(new { message = "Invalid user id" });

            var patient = await _context.Patients.FirstOrDefaultAsync(p => p.UserId == userId);

            if (patient == null)
                return BadRequest(new { message = "You are not registered as a patient" });

            var cases = await _context.DiseaseCases
                .Where(c => c.PatientId == patient.Id)
                .OrderByDescending(c => c.CreatedAt)
                .ToListAsync();

            return Ok(cases);
        }

        // GET: api/cases/patient/3  (Admin/Doctor usage) - optional
        [HttpGet("patient/{patientId:int}")]
        public async Task<IActionResult> GetPatientCases(int patientId)
        {
            var cases = await _context.DiseaseCases
                .Where(c => c.PatientId == patientId)
                .OrderByDescending(c => c.CreatedAt)
                .ToListAsync();

            return Ok(cases);
        }
    }

    // DTO local (أو حطيه في Dtos/CreateDiseaseCaseDto.cs)
    public class CreateDiseaseCaseDto
    {
        public string? ImagePath { get; set; }
        public string? AiDiagnosis { get; set; }
        public double? Confidence { get; set; }
        public string? Notes { get; set; }
    }
}
