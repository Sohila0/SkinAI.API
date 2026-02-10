using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SkinAI.API.Data;
using SkinAI.API.Models;
using SkinAI.API.Services;
using System.Security.Claims;
using System.Text.Json;

namespace SkinAI.API.Controllers
{
    [ApiController]
    [Route("api/skin")]
    [Authorize]
    public class SkinAnalysisController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly IWebHostEnvironment _env;
        private readonly INotificationService _notificationService;
        private readonly AiHttpService _aiHttp;

        public SkinAnalysisController(
            ApplicationDbContext context,
            IWebHostEnvironment env,
            INotificationService notificationService,
            AiHttpService aiHttp)
        {
            _context = context;
            _env = env;
            _notificationService = notificationService;
            _aiHttp = aiHttp;
        }

        // POST /api/skin/analyze
        [HttpPost("analyze")]
        [Consumes("multipart/form-data")]
        [RequestSizeLimit(10 * 1024 * 1024)] // 10MB
        public async Task<IActionResult> AnalyzeSkin([FromForm] AnalysisRequest request, CancellationToken ct)
        {
            if (request.File == null || request.File.Length == 0)
                return BadRequest(new { ok = false, error = "Please upload an image file." });

            // basic type validation
            var allowed = new[] { "image/jpeg", "image/png", "image/jpg" };
            var ctType = (request.File.ContentType ?? "").ToLower();
            if (!allowed.Contains(ctType))
                return BadRequest(new { ok = false, error = "Unsupported file type. Use jpg/png." });

            var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrWhiteSpace(userIdStr))
                return Unauthorized(new { ok = false, error = "Unauthorized." });

            if (!int.TryParse(userIdStr, out var userId))
                return Unauthorized(new { ok = false, error = "Invalid user id." });

            var patient = await _context.Patients.FirstOrDefaultAsync(p => p.UserId == userId, ct);
            if (patient == null)
                return BadRequest(new { ok = false, error = "Patient not found." });

            // 1) Save image to wwwroot/uploads/cases
            var webRoot = _env.WebRootPath ?? Path.Combine(_env.ContentRootPath, "wwwroot");
            var uploadsFolder = Path.Combine(webRoot, "uploads", "cases");
            Directory.CreateDirectory(uploadsFolder);

            var ext = Path.GetExtension(request.File.FileName);
            if (string.IsNullOrWhiteSpace(ext)) ext = ".jpg";

            // normalize extension
            ext = ext.ToLower();
            if (ext != ".jpg" && ext != ".jpeg" && ext != ".png") ext = ".jpg";

            var fileName = $"{Guid.NewGuid():N}{ext}";
            var filePath = Path.Combine(uploadsFolder, fileName);

            await using (var stream = new FileStream(filePath, FileMode.Create))
            {
                await request.File.CopyToAsync(stream, ct);
            }

            var imagePath = $"/uploads/cases/{fileName}";
            var imageUrl = $"{Request.Scheme}://{Request.Host}{imagePath}";

            // 2) Call FastAPI (AI)
            var aiJson = await _aiHttp.PredictAsync(request.File, ct);

            // 3) Parse required fields to store
            string diagnosis = "";
            double confidence = 0.0;
            string status = "unknown";

            using (var doc = JsonDocument.Parse(aiJson))
            {
                var root = doc.RootElement;

                if (root.TryGetProperty("diagnosis", out var diagProp))
                    diagnosis = diagProp.GetString() ?? "";

                if (root.TryGetProperty("confidence", out var confProp))
                    confidence = confProp.GetDouble();

                if (root.TryGetProperty("status", out var statusProp))
                    status = statusProp.GetString() ?? "unknown";
            }

            // 4) Save in DB
            var diseaseCase = new DiseaseCase
            {
                ImagePath = imagePath,
                AiDiagnosis = diagnosis,
                Confidence = confidence, // 0..1
                PatientId = patient.Id,
                
                CreatedAt = DateTime.UtcNow
            };

            _context.DiseaseCases.Add(diseaseCase);
            await _context.SaveChangesAsync(ct);

            // 5) Notification (English)
            await _notificationService.CreateNotificationAsync(
                userId,
                "Analysis completed",
                $"Result: {diagnosis} (status: {status})",
                NotificationType.DiagnosisReady);

            // Return: caseId + imageUrl + ai object
            return Content(
                JsonSerializer.Serialize(new
                {
                    ok = true,
                    caseId = diseaseCase.Id,
                    imagePath,
                    imageUrl,
                    ai = JsonDocument.Parse(aiJson).RootElement,
                     diagnosis = diagnosis,
                    confidence = Math.Round(confidence * 100, 0),
                }),
                "application/json"
            );
           

        }

        // GET /api/skin/my-records
        [HttpGet("my-records")]
        public async Task<IActionResult> MyRecords(CancellationToken ct)
        {
            var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrWhiteSpace(userIdStr))
                return Unauthorized(new { ok = false, error = "Unauthorized." });

            var userId = int.Parse(userIdStr);

            var patient = await _context.Patients.FirstOrDefaultAsync(p => p.UserId == userId, ct);
            if (patient == null)
                return BadRequest(new { ok = false, error = "Patient not found." });

            var baseUrl = $"{Request.Scheme}://{Request.Host}";

            var records = await _context.DiseaseCases
                .Where(c => c.PatientId == patient.Id)
                .OrderByDescending(c => c.CreatedAt)
                .Select(c => new
                {
                    caseId = c.Id,
                    diagnosis = c.AiDiagnosis,
                    confidence = Math.Round(c.Confidence * 100, 0),
                    createdAt = c.CreatedAt,
                    imagePath = c.ImagePath,
                    imageUrl = string.IsNullOrEmpty(c.ImagePath) ? "" : (baseUrl + c.ImagePath)
                })
                .ToListAsync(ct);

            return Ok(new { ok = true, data = records });
        }
    }

    public class AnalysisRequest
    {
        // Flutter MUST send: form-data key = "file"
        public IFormFile File { get; set; } = default!;
        
    }
}
