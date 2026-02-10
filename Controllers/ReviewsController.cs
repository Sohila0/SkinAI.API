using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SkinAI.API.Data;
using SkinAI.API.Dtos.Reviews;
using SkinAI.API.Enums;
using SkinAI.API.Models;
using System.Security.Claims;

namespace SkinAI.API.Controllers
{
    [ApiController]
    [Route("api/reviews")]
    [Authorize]
    public class ReviewsController : ControllerBase
    {
        private readonly ApplicationDbContext _context;

        public ReviewsController(ApplicationDbContext context)
        {
            _context = context;
        }

        // ===================== Patient creates review =====================
        // POST: /api/reviews/consultations/{consultationId}/doctor
        [HttpPost("consultations/{consultationId:int}/doctor")]
        [Authorize(Roles = "Patient")]
        public async Task<IActionResult> CreateDoctorReview(int consultationId, [FromBody] CreateReviewDto dto)
        {
            if (dto == null) return BadRequest("Invalid request.");

            var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrWhiteSpace(userIdStr)) return Unauthorized();
            var userId = int.Parse(userIdStr);

            var patient = await _context.Patients.FirstOrDefaultAsync(p => p.UserId == userId);
            if (patient == null) return BadRequest("Patient not found.");

            var consultation = await _context.Consultations
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == consultationId);

            if (consultation == null) return NotFound("Consultation not found.");

            // لازم الاستشارة تخص المريض
            if (consultation.PatientId != patient.Id) return Forbid();

            // لازم تكون مقفولة
            if (consultation.Status != ConsultationStatus.CLOSED)
                return BadRequest("You can only review after the consultation is closed.");

            // لازم يكون فيه دكتور متحدد
            if (consultation.DoctorId == null)
                return BadRequest("No doctor assigned to this consultation.");

            // منع التقييم مرتين
            var exists = await _context.DoctorReviews.AnyAsync(r => r.ConsultationId == consultationId);
            if (exists) return BadRequest("You already reviewed this consultation.");

            var review = new DoctorReview
            {
                ConsultationId = consultationId,
                DoctorId = consultation.DoctorId.Value,
                PatientId = patient.Id,
                Rating = dto.Rating,
                Comment = dto.Comment?.Trim(),
                CreatedAt = DateTime.UtcNow
            };

            _context.DoctorReviews.Add(review);
            await _context.SaveChangesAsync();
            var doctorEntity = await _context.Doctors.FirstAsync(d => d.Id == review.DoctorId);

            var ratings = await _context.DoctorReviews
                .Where(r => r.DoctorId == doctorEntity.Id)
                .Select(r => r.Rating)
                .ToListAsync();

            doctorEntity.TotalReviews = ratings.Count;
            doctorEntity.AverageRating = ratings.Count == 0 ? 0 : ratings.Average();

            await _context.SaveChangesAsync();

            return Ok(new
            {
                message = "Review submitted successfully.",
                reviewId = review.Id,
                review.Rating,
                review.Comment,
                review.CreatedAt
            });
        }

        // ===================== Get doctor reviews (public) =====================
        // GET: /api/reviews/doctors/{doctorId}
        [HttpGet("doctors/{doctorId:int}")]
        [AllowAnonymous]
        public async Task<IActionResult> GetDoctorReviews(int doctorId)
        {
            var reviews = await _context.DoctorReviews
                .AsNoTracking()
                .Where(r => r.DoctorId == doctorId)
                .OrderByDescending(r => r.CreatedAt)
                .Select(r => new
                {
                    r.Id,
                    r.Rating,
                    r.Comment,
                    r.CreatedAt,
                    r.ConsultationId
                })
                .ToListAsync();

            // average
            double avg = reviews.Count == 0 ? 0 : reviews.Average(r => r.Rating);

            return Ok(new
            {
                doctorId,
                totalReviews = reviews.Count,
                averageRating = Math.Round(avg, 2),
                reviews
            });
        }

        // ===================== Get my reviews (patient) =====================
        // GET: /api/reviews/mine
        [HttpGet("mine")]
        [Authorize(Roles = "Patient")]
        public async Task<IActionResult> GetMyReviews()
        {
            var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrWhiteSpace(userIdStr)) return Unauthorized();
            var userId = int.Parse(userIdStr);

            var patient = await _context.Patients.FirstOrDefaultAsync(p => p.UserId == userId);
            if (patient == null) return BadRequest("Patient not found.");

            var reviews = await _context.DoctorReviews
                .AsNoTracking()
                .Where(r => r.PatientId == patient.Id)
                .OrderByDescending(r => r.CreatedAt)
                .Select(r => new
                {
                    r.Id,
                    r.DoctorId,
                    r.ConsultationId,
                    r.Rating,
                    r.Comment,
                    r.CreatedAt
                })
                .ToListAsync();

            return Ok(reviews);
        }
    }
}
