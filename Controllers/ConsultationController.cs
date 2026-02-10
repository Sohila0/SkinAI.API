using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SkinAI.API.Data;
using SkinAI.API.Dtos;
using SkinAI.API.Dtos.Consultations;
using SkinAI.API.Enums;
using SkinAI.API.Models;
using SkinAI.API.Services;
using System.Security.Claims;

namespace SkinAI.API.Controllers
{
    [ApiController]
    [Route("api/consultations")]
    [Authorize]
    public class ConsultationsController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly INotificationService _notificationService;

        public ConsultationsController(ApplicationDbContext context, INotificationService notificationService)
        {
            _context = context;
            _notificationService = notificationService;
        }

        // =============== (اختياري) Get consultation details ===============
        [HttpGet("{id:int}")]
        public async Task<IActionResult> GetById(int id)
        {
            var con = await _context.Consultations
                .Include(c => c.DiseaseCase)
                .Include(c => c.Patient)
                .Include(c => c.Doctor)
                .FirstOrDefaultAsync(c => c.Id == id);

            if (con == null) return NotFound("Consultation not found.");
            return Ok(con);
        }

        // =============== 1) Patient creates request (OPEN) ===============
        [HttpPost("request")]
        [Authorize(Roles = "Patient")]
        public async Task<IActionResult> CreateRequest([FromBody] CreateConsultationRequestDto dto)
        {
            var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrWhiteSpace(userIdStr)) return Unauthorized();
            var userId = int.Parse(userIdStr);

            var patient = await _context.Patients.FirstOrDefaultAsync(p => p.UserId == userId);
            if (patient == null) return BadRequest("Patient not found.");

            var diseaseCase = await _context.DiseaseCases
                .FirstOrDefaultAsync(dc => dc.Id == dto.DiseaseCaseId && dc.PatientId == patient.Id);

            if (diseaseCase == null) return BadRequest("Case not found for this patient.");

            // (اختياري) امنعي تكرار طلب استشارة لنفس الحالة لو فيه واحدة مش مقفولة
            var existing = await _context.Consultations.AnyAsync(c =>
                c.DiseaseCaseId == dto.DiseaseCaseId &&
                (c.Status == ConsultationStatus.OPEN ||
                 c.Status == ConsultationStatus.OFFERING ||
                 c.Status == ConsultationStatus.OFFER_SELECTED ||
                 c.Status == ConsultationStatus.PAID ||
                 c.Status == ConsultationStatus.IN_CHAT));

            if (existing)
                return BadRequest("There is already an active consultation request for this case.");

            var con = new Consultation
            {
                PatientId = patient.Id,
                DiseaseCaseId = dto.DiseaseCaseId,
                Notes = null,
                Status = ConsultationStatus.OPEN,
                DoctorId = null,
                Price = null,
                IsPaid = false,
                CreatedAt = DateTime.UtcNow
            };

            _context.Consultations.Add(con);

            // تحديث حالة الكيس
            diseaseCase.Status = CaseStatus.REQUESTED_DOCTOR;

            await _context.SaveChangesAsync();

            // ✅ إشعار لكل الأطباء: طلب جديد
            var doctorUserIds = await _context.Doctors.Select(d => d.UserId).ToListAsync();
            foreach (var doctorUserId in doctorUserIds)
            {
                await _notificationService.CreateNotificationAsync(
                    doctorUserId,
                    "New consultation request.",
                    "There is a new consultation request available for dermatology cases.",
                    NotificationType.NewConsultationRequest
                );
            }

            return Ok(new
            {
                con.Id,
                con.PatientId,
                con.DiseaseCaseId,
                con.Status,
                con.CreatedAt,
                con.Notes,
                con.DoctorId,
                con.Price,
                con.IsPaid
            });

        }

        // =============== 2) Doctors view open requests ===============
        [HttpGet("open")]
        [Authorize(Roles = "Doctor")]
        public async Task<IActionResult> GetOpenRequests()
        {
            var baseUrl = $"{Request.Scheme}://{Request.Host}";

            var open = await _context.Consultations
                .AsNoTracking()
                .Where(c => c.Status == ConsultationStatus.OPEN || c.Status == ConsultationStatus.OFFERING)
                .OrderByDescending(c => c.CreatedAt)
                .Select(c => new
                {
                    c.Id,
                    c.Status,
                    c.CreatedAt,
                    c.Notes,
                    c.PatientId,
                    c.DiseaseCaseId,

                    // patient name
                    patientName = _context.Patients
                        .Where(p => p.Id == c.PatientId)
                        .Select(p => p.User.FullName)
                        .FirstOrDefault(),

                    // AI result (from DiseaseCase)
                    ai_diagnosis = _context.DiseaseCases
                        .Where(dc => dc.Id == c.DiseaseCaseId)
                        .Select(dc => dc.AiDiagnosis)
                        .FirstOrDefault(),

                    confidence = _context.DiseaseCases
                        .Where(dc => dc.Id == c.DiseaseCaseId)
                        .Select(dc => dc.Confidence)
                        .FirstOrDefault(),

                    imageUrl = _context.DiseaseCases
                        .Where(dc => dc.Id == c.DiseaseCaseId)
                        .Select(dc => baseUrl + dc.ImagePath)
                        .FirstOrDefault()
                })
                .ToListAsync();

            return Ok(open);

        }

        // =============== 3) Patient view his consultations ===============
        [HttpGet("mine")]
        [Authorize(Roles = "Patient")]
        public async Task<IActionResult> GetMyConsultations()
        {
            var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrWhiteSpace(userIdStr)) return Unauthorized();
            var userId = int.Parse(userIdStr);

            var patient = await _context.Patients.FirstOrDefaultAsync(p => p.UserId == userId);
            if (patient == null) return BadRequest("Patient not found.");

            var list = await _context.Consultations
                .Include(c => c.DiseaseCase)
                .Include(c => c.Doctor)
                .Where(c => c.PatientId == patient.Id)
                .OrderByDescending(c => c.CreatedAt)
                .ToListAsync();

            return Ok(list);
        }

        // =============== 4) Doctor view consultations assigned to him ===============
        [HttpGet("assigned")]
        [Authorize(Roles = "Doctor")]
        public async Task<IActionResult> GetAssignedToMe()
        {
            var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrWhiteSpace(userIdStr)) return Unauthorized();
            var userId = int.Parse(userIdStr);

            var doctor = await _context.Doctors.FirstOrDefaultAsync(d => d.UserId == userId);
            if (doctor == null) return BadRequest("Doctor not found.");

            var list = await _context.Consultations
                .Include(c => c.DiseaseCase)
                .Include(c => c.Patient)
                .Where(c => c.DoctorId == doctor.Id)
                .OrderByDescending(c => c.CreatedAt)
                .ToListAsync();

            return Ok(list);
        }

        // =============== 5) Doctor finalize diagnosis (CLOSE) ===============
        [HttpPost("{id:int}/finalize")]
        [Authorize(Roles = "Doctor")]
        public async Task<IActionResult> Finalize(int id, [FromBody] FinalizeConsultationDto dto)
        {
            if (dto == null || string.IsNullOrWhiteSpace(dto.FinalDiagnosis))
                return BadRequest("FinalDiagnosis is required.");

            var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrWhiteSpace(userIdStr)) return Unauthorized();
            var userId = int.Parse(userIdStr);

            var doctor = await _context.Doctors.FirstOrDefaultAsync(d => d.UserId == userId);
            if (doctor == null) return BadRequest("Doctor not found.");

            var con = await _context.Consultations
                .Include(c => c.DiseaseCase)
                .FirstOrDefaultAsync(c => c.Id == id);

            if (con == null) return NotFound("Consultation not found.");

            // لازم يكون الدكتور هو اللي اتحدد في الجلسة
            if (con.DoctorId != doctor.Id) return Forbid();

            // لازم تكون في الشات أو مدفوعة
            if (con.Status != ConsultationStatus.IN_CHAT && con.Status != ConsultationStatus.PAID)
                return BadRequest("Consultation is not active.");

            con.FinalDiagnosis = dto.FinalDiagnosis.Trim();
            con.DoctorFinalNotes = dto.DoctorFinalNotes?.Trim();
            con.Status = ConsultationStatus.CLOSED;
            con.ClosedAt = DateTime.UtcNow;

            // تحديث الكيس
            con.DiseaseCase.Status = CaseStatus.CLOSED;

            await _context.SaveChangesAsync();

            // ✅ إشعار للمريض: التشخيص النهائي جاهز
            var patientUserId = await _context.Patients
                .Where(p => p.Id == con.PatientId)
                .Select(p => p.UserId)
                .FirstAsync();

            await _notificationService.CreateNotificationAsync(
                patientUserId,
                "The final diagnosis has been made.",
                "The doctor has added the final diagnosis to your case",
                NotificationType.DiagnosisCompleted
            );

            return Ok(new { message = "Final diagnosis saved and consultation closed." });
        }
        [HttpGet("recent")]
        [Authorize(Roles = "Patient")]
        public async Task<IActionResult> Recent()
        {
            var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrWhiteSpace(userIdStr)) return Unauthorized();
            var userId = int.Parse(userIdStr);

            var patient = await _context.Patients.AsNoTracking().FirstOrDefaultAsync(p => p.UserId == userId);
            if (patient == null) return BadRequest("Patient not found.");

            var list = await _context.Consultations
                .AsNoTracking()
                .Include(c => c.DiseaseCase)
                .Include(c => c.Doctor)
                .Where(c => c.PatientId == patient.Id)
                .OrderByDescending(c => c.CreatedAt)
                .Select(c => new
                {
                    consultationId = c.Id,
                    doctorName = c.DoctorId == null ? "Finding Doctor..." : c.Doctor!.FullName,
                    aiDiagnosis = c.DiseaseCase.AiDiagnosis,
                    confidencePercent = Math.Round(c.DiseaseCase.Confidence , 0),
                    createdAt = c.CreatedAt,
                    status = c.Status.ToString(),
                    badge =
                        c.Status == ConsultationStatus.CLOSED ? "COMPLETED" :
                        c.Status == ConsultationStatus.IN_CHAT ? "ACTIVE" :
                        c.Status == ConsultationStatus.OFFER_SELECTED ? "WAITING_PAYMENT" :
                        c.Status == ConsultationStatus.OFFERING ? "OFFERS" :
                        c.Status == ConsultationStatus.OPEN ? "FINDING_DOCTOR" :
                        c.Status == ConsultationStatus.CANCELLED ? "CANCELLED" : "UNKNOWN"
                })
                .ToListAsync();

            return Ok(list);
        }

    }
}
