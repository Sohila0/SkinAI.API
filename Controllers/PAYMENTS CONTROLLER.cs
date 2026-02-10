using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SkinAI.API.Data;
using SkinAI.API.Dtos.Payments;
using SkinAI.API.Enums;
using SkinAI.API.Models;
using SkinAI.API.Services;
using System.Security.Claims;

namespace SkinAI.API.Controllers
{
    [ApiController]
    [Route("api/payments")]
    [Authorize(Roles = "Patient")]
    public class PaymentsController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly INotificationService _notificationService;

        public PaymentsController(ApplicationDbContext context, INotificationService notificationService)
        {
            _context = context;
            _notificationService = notificationService;
        }

        // ✅ Simulation Pay
        [HttpPost("simulate")]
        public async Task<IActionResult> SimulatePay([FromBody] SimulatePaymentDto dto)
        {
            if (dto == null) return BadRequest("Invalid request.");

            var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrWhiteSpace(userIdStr)) return Unauthorized();
            var userId = int.Parse(userIdStr);

            var patient = await _context.Patients.FirstOrDefaultAsync(p => p.UserId == userId);
            if (patient == null) return BadRequest("Patient not found.");

            var con = await _context.Consultations.FirstOrDefaultAsync(c => c.Id == dto.ConsultationId);
            if (con == null) return NotFound("Consultation not found.");

            // لازم المريض صاحب الاستشارة
            if (con.PatientId != patient.Id) return Forbid();

            // لازم المريض يكون اختار عرض
            if (con.Status != ConsultationStatus.OFFER_SELECTED || con.DoctorId == null || con.Price == null)
                return BadRequest("You must select a doctor offer before paying.");

            // منع الدفع مرتين
            if (con.IsPaid || con.Status == ConsultationStatus.PAID || con.Status == ConsultationStatus.IN_CHAT)
                return BadRequest("Consultation already paid.");

            // ✅ Create Payment (Simulation)
            var payment = new Payment
            {
                ConsultationId = con.Id,
                PatientId = con.PatientId,
                DoctorId = con.DoctorId.Value,
                Amount = con.Price.Value,
                Method = dto.Method,
                Status = PaymentStatus.SUCCESS,
                Provider = "SIMULATED",
                ReferenceNo = $"SIM-{Guid.NewGuid():N}".Substring(0, 12),
                CreatedAt = DateTime.UtcNow
            };

            _context.Payments.Add(payment);

            // ✅ Update consultation
            con.IsPaid = true;
            con.Status = ConsultationStatus.IN_CHAT; // ✅ opens chat immediately
            var dc = await _context.DiseaseCases.FirstAsync(x => x.Id == con.DiseaseCaseId);
            dc.Status = CaseStatus.IN_CONSULTATION;


            await _context.SaveChangesAsync();

            // ✅ استخدمي الجاهز: يبعت للمريض + الدكتور
            await _notificationService.NotifyPaymentSuccessAsync(payment.Id);

            return Ok(new
            {
                message = "Payment simulated successfully.",
                paymentId = payment.Id,
                referenceNo = payment.ReferenceNo,
                amount = payment.Amount,
                consultationId = con.Id
            });
        }

        // ✅ Get my payments
        [HttpGet("mine")]
        public async Task<IActionResult> GetMyPayments()
        {
            var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrWhiteSpace(userIdStr)) return Unauthorized();
            var userId = int.Parse(userIdStr);

            var patient = await _context.Patients.FirstOrDefaultAsync(p => p.UserId == userId);
            if (patient == null) return BadRequest("Patient not found.");

            var list = await _context.Payments
                .AsNoTracking()
                .Where(p => p.PatientId == patient.Id)
                .OrderByDescending(p => p.CreatedAt)
                .Select(p => new
                {
                    p.Id,
                    p.ConsultationId,
                    p.Amount,
                    p.Method,
                    p.Status,
                    p.ReferenceNo,
                    p.CreatedAt
                })
                .ToListAsync();

            return Ok(list);
        }
    }
}
