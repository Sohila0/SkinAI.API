using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SkinAI.API.Data;
using SkinAI.API.Dtos.Offers;
using SkinAI.API.Enums;
using SkinAI.API.Models;
using SkinAI.API.Services;
using System.Security.Claims;

namespace SkinAI.API.Controllers
{
    [ApiController]
    [Route("api/offers")]
    [Authorize]
    public class ConsultationOffersController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly INotificationService _notificationService;

        public ConsultationOffersController(ApplicationDbContext context, INotificationService notificationService)
        {
            _context = context;
            _notificationService = notificationService;
        }

        // ================= DOCTOR CREATES OFFER =================
        [HttpPost("{consultationId:int}")]
        [Authorize(Roles = "Doctor")]
        public async Task<IActionResult> CreateOffer(int consultationId, [FromBody] CreateOfferDto dto)
        {
            if (dto == null || dto.Price <= 0)
                return BadRequest("Price must be greater than 0.");

            var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrWhiteSpace(userIdStr)) return Unauthorized();
            var userId = int.Parse(userIdStr);

            var doctor = await _context.Doctors.FirstOrDefaultAsync(d => d.UserId == userId);
            if (doctor == null) return BadRequest("Doctor not found.");

            var consultation = await _context.Consultations.FirstOrDefaultAsync(c => c.Id == consultationId);
            if (consultation == null) return NotFound("Consultation not found.");

            if (consultation.Status != ConsultationStatus.OPEN &&
                consultation.Status != ConsultationStatus.OFFERING)
                return BadRequest("Consultation is not open for offers.");

            // (اختياري) امنعي الدكتور يكرر نفس العرض لنفس الاستشارة
            var alreadyOffered = await _context.ConsultationOffers.AnyAsync(o =>
                o.ConsultationId == consultationId &&
                o.DoctorId == doctor.Id &&
                o.Status == OfferStatus.ACTIVE);

            if (alreadyOffered)
                return BadRequest("You already have an active offer for this consultation.");

            var offer = new ConsultationOffer
            {
                ConsultationId = consultationId,
                DoctorId = doctor.Id,
                Price = dto.Price,
                Notes = dto.Notes,
                Status = OfferStatus.ACTIVE,
                CreatedAt = DateTime.UtcNow
            };

            consultation.Status = ConsultationStatus.OFFERING;

            _context.ConsultationOffers.Add(offer);
            await _context.SaveChangesAsync();

            // ✅ notify patient: new offer
            var patientUserId = await _context.Patients
                .Where(p => p.Id == consultation.PatientId)
                .Select(p => p.UserId)
                .FirstAsync();

            await _notificationService.CreateNotificationAsync(
                patientUserId,
                "New offer from a doctor.",
                "A new offer has been submitted for your case.",
                NotificationType.NewOffer,
                relatedEntityId: consultation.Id,
                relatedEntityType: "Consultation",
                actionUrl: $"/consultations/{consultation.Id}"
            );

            return Ok(offer);
        }

        // ================= PATIENT GET OFFERS =================
        [HttpGet("consultation/{consultationId:int}")]
        [Authorize(Roles = "Patient")]
        public async Task<IActionResult> GetOffers(int consultationId)
        {
            var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrWhiteSpace(userIdStr)) return Unauthorized();
            var userId = int.Parse(userIdStr);

            var patient = await _context.Patients.AsNoTracking().FirstOrDefaultAsync(p => p.UserId == userId);
            if (patient == null) return BadRequest("Patient not found.");

            // لازم الاستشارة تخص المريض
            var consultation = await _context.Consultations.AsNoTracking().FirstOrDefaultAsync(c => c.Id == consultationId);
            if (consultation == null) return NotFound("Consultation not found.");
            if (consultation.PatientId != patient.Id) return Forbid();

            var offers = await _context.ConsultationOffers
                .AsNoTracking()
                .Include(o => o.Doctor)
                .Where(o => o.ConsultationId == consultationId && o.Status == OfferStatus.ACTIVE)
                .OrderBy(o => o.Price)
                .Select(o => new
                {
                    o.Id,
                    o.Price,
                    o.Notes,
                    doctorId = o.DoctorId,
                    doctorName = o.Doctor.FullName ?? "",
                    averageRating = o.Doctor.AverageRating,
                    totalReviews = o.Doctor.TotalReviews,
                    casesCount = _context.Consultations.Count(c => c.DoctorId == o.DoctorId && c.Status == ConsultationStatus.CLOSED),
                    o.CreatedAt
                })

                .ToListAsync();

            return Ok(offers);
        }

        // ================= PATIENT SELECT OFFER =================
        [HttpPost("select/{offerId:int}")]
        [Authorize(Roles = "Patient")]
        public async Task<IActionResult> SelectOffer(int offerId)
        {
            var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrWhiteSpace(userIdStr)) return Unauthorized();
            var userId = int.Parse(userIdStr);

            var patient = await _context.Patients.FirstOrDefaultAsync(p => p.UserId == userId);
            if (patient == null) return BadRequest("Patient not found.");

            var offer = await _context.ConsultationOffers
                .Include(o => o.Consultation)
                .FirstOrDefaultAsync(o => o.Id == offerId);

            if (offer == null) return NotFound("Offer not found.");

            var consultation = offer.Consultation;

            // ✅ المريض لازم يكون صاحب الاستشارة
            if (consultation.PatientId != patient.Id) return Forbid();

            if (consultation.Status != ConsultationStatus.OFFERING)
                return BadRequest("Consultation not in offering state.");

            if (consultation.IsPaid)
                return BadRequest("Cannot select offer after payment.");

            // Reject other offers
            var otherOffers = await _context.ConsultationOffers
                .Where(o => o.ConsultationId == consultation.Id && o.Id != offer.Id)
                .ToListAsync();

            foreach (var o in otherOffers)
                o.Status = OfferStatus.REJECTED;

            offer.Status = OfferStatus.SELECTED;

            // Update consultation
            consultation.DoctorId = offer.DoctorId;
            consultation.Price = offer.Price;
            consultation.SelectedOfferId = offer.Id;
            consultation.Status = ConsultationStatus.OFFER_SELECTED;

            await _context.SaveChangesAsync();

            // ✅ notify doctor: offer accepted
            var doctorUserId = await _context.Doctors
                .Where(d => d.Id == offer.DoctorId)
                .Select(d => d.UserId)
                .FirstAsync();

            await _notificationService.CreateNotificationAsync(
                doctorUserId,
                "Your offer has been selected.",
                "The patient has selected your offer and can now proceed with payment.",
                NotificationType.OfferAccepted,
                relatedEntityId: consultation.Id,
                relatedEntityType: "Consultation",
                actionUrl: $"/consultations/{consultation.Id}"
            );

            return Ok(new { message = "Offer selected. You can proceed to payment.", consultationId = consultation.Id });
        }
    }
}
