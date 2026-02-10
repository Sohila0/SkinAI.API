using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.SignalR;
using SkinAI.API.Data;
using SkinAI.API.Dtos.Chat;
using SkinAI.API.Enums;
using SkinAI.API.Hubs;
using SkinAI.API.Models;
using SkinAI.API.Services;
using System.Security.Claims;

namespace SkinAI.API.Controllers
{
    [ApiController]
    [Route("api/chat")]
    [Authorize]
    public class ChatController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly INotificationService _notificationService;
        private readonly IWebHostEnvironment _env;
        private readonly IHubContext<ChatHub> _hub;

        public ChatController(
            ApplicationDbContext context,
            INotificationService notificationService,
            IWebHostEnvironment env,
            IHubContext<ChatHub> hub)
        {
            _context = context;
            _notificationService = notificationService;
            _env = env;
            _hub = hub;
        }

        private int? GetUserId()
        {
            var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrWhiteSpace(userIdStr)) return null;
            return int.Parse(userIdStr);
        }

        // ===================== Helper: validate + get receiver =====================
        private async Task<(Consultation consultation, bool isPatientOwner, int receiverUserId)> ValidateAndGetReceiver(
            int consultationId,
            int senderUserId,
            CancellationToken ct)
        {
            var consultation = await _context.Consultations
                .FirstOrDefaultAsync(c => c.Id == consultationId, ct);

            if (consultation == null)
                throw new KeyNotFoundException("Consultation not found.");
            if (consultation.Status == ConsultationStatus.CLOSED)
                throw new InvalidOperationException("Chat was closed.");

            if (consultation.Status != ConsultationStatus.IN_CHAT)
                throw new InvalidOperationException("Chat is not active yet. Please complete payment first.");

            // permission check
            var patient = await _context.Patients.FirstOrDefaultAsync(p => p.UserId == senderUserId, ct);
            var doctor = await _context.Doctors.FirstOrDefaultAsync(d => d.UserId == senderUserId, ct);

            var isPatientOwner = patient != null && consultation.PatientId == patient.Id;
            var isDoctorAssigned = doctor != null && consultation.DoctorId == doctor.Id;

            if (!isPatientOwner && !isDoctorAssigned)
                throw new UnauthorizedAccessException("No permission.");

            int receiverUserId;

            if (isPatientOwner)
            {
                if (consultation.DoctorId == null)
                    throw new InvalidOperationException("No doctor assigned yet.");

                receiverUserId = await _context.Doctors
                    .Where(d => d.Id == consultation.DoctorId.Value)
                    .Select(d => d.UserId)
                    .FirstAsync(ct);
            }
            else
            {
                receiverUserId = await _context.Patients
                    .Where(p => p.Id == consultation.PatientId)
                    .Select(p => p.UserId)
                    .FirstAsync(ct);
            }

            return (consultation, isPatientOwner, receiverUserId);
        }

        // ===================== GET messages =====================
        [HttpGet("consultations/{consultationId:int}/messages")]
        public async Task<IActionResult> GetMessages(int consultationId, CancellationToken ct)
        {
            var userId = GetUserId();
            if (userId == null) return Unauthorized();

            // validate permission by calling helper (receiver not used هنا)
            try
            {
                await ValidateAndGetReceiver(consultationId, userId.Value, ct);
            }
            catch (UnauthorizedAccessException) { return Forbid(); }
            catch (KeyNotFoundException e) { return NotFound(e.Message); }

            // mark unread as read
            var unread = await _context.ConsultationMessages
                .Where(m => m.ConsultationId == consultationId &&
                            m.SenderId != userId.Value &&
                            !m.IsRead)
                .ToListAsync(ct);

            if (unread.Count > 0)
            {
                foreach (var m in unread) m.IsRead = true;
                await _context.SaveChangesAsync(ct);
            }

            var messages = await _context.ConsultationMessages
                .AsNoTracking()
                .Include(m => m.Sender)
                .Where(m => m.ConsultationId == consultationId)
                .OrderBy(m => m.Timestamp)
                .Select(m => new
                {
                    m.Id,
                    m.ConsultationId,
                    m.SenderId,
                    senderName = (m.Sender.FullName ?? m.Sender.UserName),
                    type = m.Type,
                    m.MessageText,
                    m.VoiceUrl,
                    voiceFullUrl = m.VoiceUrl == null ? null : $"{Request.Scheme}://{Request.Host}{m.VoiceUrl}",
                    m.VoiceDurationSec,
                    m.Timestamp,
                    m.IsRead,
                    m.FileUrl,
                    fileFullUrl = m.FileUrl == null ? null : $"{Request.Scheme}://{Request.Host}{m.FileUrl}",
                    m.FileName,
                    m.FileSize,

                })
                .ToListAsync(ct);

            return Ok(new { ok = true, data = messages });
        }

        // ===================== POST send TEXT =====================
        [HttpPost("consultations/{consultationId:int}/messages")]
        public async Task<IActionResult> SendMessage(int consultationId, [FromBody] SendMessageDto dto, CancellationToken ct)
        {
            var userId = GetUserId();
            if (userId == null) return Unauthorized();

            if (dto == null || string.IsNullOrWhiteSpace(dto.MessageText))
                return BadRequest(new { ok = false, error = "MessageText is required." });

            (Consultation consultation, bool isPatientOwner, int receiverUserId) result;

            try
            {
                result = await ValidateAndGetReceiver(consultationId, userId.Value, ct);
            }
            catch (UnauthorizedAccessException) { return Forbid(); }
            catch (KeyNotFoundException e) { return NotFound(e.Message); }
            catch (InvalidOperationException e) { return BadRequest(e.Message); }

            var msg = new ConsultationMessage
            {
                ConsultationId = consultationId,
                SenderId = userId.Value,
                Type = MessageType.Text,
                MessageText = dto.MessageText.Trim(),
                Timestamp = DateTime.UtcNow,
                IsRead = false
            };

            _context.ConsultationMessages.Add(msg);
            await _context.SaveChangesAsync(ct);

            // DB notification + OneSignal (لو مقفول)
            await _notificationService.CreateNotificationAsync(
                result.receiverUserId,
                "New message",
                "You have new message",
                NotificationType.NewMessage,
                relatedEntityId: consultationId,
                relatedEntityType: "Consultation",
                actionUrl: $"/consultations/{consultationId}/chat"
            );

            // SignalR realtime (لو مفتوح)
            await _hub.Clients.Group($"consultation:{consultationId}")
                .SendAsync("message", new
                {
                    msg.Id,
                    msg.ConsultationId,
                    msg.SenderId,
                    type = msg.Type,
                    msg.MessageText,
                    msg.Timestamp
                }, ct);

            return Ok(new { ok = true, msg.Id });
        }

        // ===================== POST send VOICE NOTE =====================
        [HttpPost("consultations/{consultationId:int}/voice")]
        [Consumes("multipart/form-data")]
        public async Task<IActionResult> SendVoice(int consultationId, [FromForm] SendVoiceDto dto, CancellationToken ct)
        {
            var userId = GetUserId();
            if (userId == null) return Unauthorized();

            if (dto.VoiceFile == null || dto.VoiceFile.Length == 0)
                return BadRequest(new { ok = false, error = "VoiceFile is required." });

            (Consultation consultation, bool isPatientOwner, int receiverUserId) result;

            try
            {
                result = await ValidateAndGetReceiver(consultationId, userId.Value, ct);
            }
            catch (UnauthorizedAccessException) { return Forbid(); }
            catch (KeyNotFoundException e) { return NotFound(e.Message); }
            catch (InvalidOperationException e) { return BadRequest(e.Message); }

            // validate audio
            var contentType = (dto.VoiceFile.ContentType ?? "").ToLower();
            var allowed = contentType.Contains("audio") || contentType.Contains("mpeg") || contentType.Contains("wav") || contentType.Contains("mp4");

            if (!allowed)
                return BadRequest(new { ok = false, error = "Only audio files are allowed." });

            const long maxBytes = 10 * 1024 * 1024;
            if (dto.VoiceFile.Length > maxBytes)
                return BadRequest(new { ok = false, error = "Audio too large. Max 10MB." });

            // save file
            var webRoot = _env.WebRootPath ?? Path.Combine(_env.ContentRootPath, "wwwroot");
            var folder = Path.Combine(webRoot, "uploads", "voice");
            Directory.CreateDirectory(folder);

            var ext = Path.GetExtension(dto.VoiceFile.FileName).ToLower();
            if (ext is not (".m4a" or ".mp3" or ".wav" or ".aac"))
                ext = ".m4a";

            var fileName = $"{Guid.NewGuid():N}{ext}";
            var fullPath = Path.Combine(folder, fileName);

            await using (var fs = new FileStream(fullPath, FileMode.Create))
            {
                await dto.VoiceFile.CopyToAsync(fs, ct);
            }

            var publicPath = $"/uploads/voice/{fileName}";
            var fullUrl = $"{Request.Scheme}://{Request.Host}{publicPath}";

            var msg = new ConsultationMessage
            {
                ConsultationId = consultationId,
                SenderId = userId.Value,
                Type = MessageType.Voice,
                VoiceUrl = publicPath,
                VoiceDurationSec = dto.DurationSec,
                Timestamp = DateTime.UtcNow,
                IsRead = false
            };

            _context.ConsultationMessages.Add(msg);
            await _context.SaveChangesAsync(ct);

            // Notification
            await _notificationService.CreateNotificationAsync(
                result.receiverUserId,
                " Voice note",
                "You received a new voice note in the consultation.",
                NotificationType.NewMessage,
                relatedEntityId: consultationId,
                relatedEntityType: "Consultation",
                actionUrl: $"/consultations/{consultationId}/chat"
            );

            // SignalR realtime
            await _hub.Clients.Group($"consultation:{consultationId}")
                .SendAsync("message", new
                {
                    msg.Id,
                    msg.ConsultationId,
                    msg.SenderId,
                    type = msg.Type,
                    voiceUrl = msg.VoiceUrl,
                    voiceFullUrl = fullUrl,
                    durationSec = msg.VoiceDurationSec,
                    msg.Timestamp
                }, ct);

            return Ok(new
            {
                ok = true,
                msg.Id,
                msg.ConsultationId,
                msg.SenderId,
                type = msg.Type,
                voiceUrl = msg.VoiceUrl,
                voiceFullUrl = fullUrl,
                durationSec = msg.VoiceDurationSec,
                msg.Timestamp
            });

        }
        // ===================== POST send FILE =====================
        [HttpPost("consultations/{consultationId:int}/file")]
        [Consumes("multipart/form-data")]
        public async Task<IActionResult> SendFile(int consultationId, [FromForm] SendFileDto dto, CancellationToken ct)
        {
            var userId = GetUserId();
            if (userId == null) return Unauthorized();

            if (dto.File == null || dto.File.Length == 0)
                return BadRequest(new { ok = false, error = "File is required." });

            (Consultation consultation, bool isPatientOwner, int receiverUserId) result;

            try
            {
                result = await ValidateAndGetReceiver(consultationId, userId.Value, ct);
            }
            catch (UnauthorizedAccessException) { return Forbid(); }
            catch (KeyNotFoundException e) { return NotFound(e.Message); }
            catch (InvalidOperationException e) { return BadRequest(e.Message); }

            const long maxBytes = 20 * 1024 * 1024;
            if (dto.File.Length > maxBytes)
                return BadRequest(new { ok = false, error = "File too large. Max 20MB." });

            var ext = Path.GetExtension(dto.File.FileName).ToLower();
            var allowedExtensions = new[] { ".pdf", ".jpg", ".png", ".jpeg", ".docx" };

            if (!allowedExtensions.Contains(ext))
                return BadRequest(new { ok = false, error = "File type not allowed." });

            var webRoot = _env.WebRootPath ?? Path.Combine(_env.ContentRootPath, "wwwroot");
            var folder = Path.Combine(webRoot, "uploads", "files");
            Directory.CreateDirectory(folder);

            var fileName = $"{Guid.NewGuid():N}{ext}";
            var fullPath = Path.Combine(folder, fileName);

            await using (var fs = new FileStream(fullPath, FileMode.Create))
            {
                await dto.File.CopyToAsync(fs, ct);
            }

            var publicPath = $"/uploads/files/{fileName}";
            var fullUrl = $"{Request.Scheme}://{Request.Host}{publicPath}";

            var msg = new ConsultationMessage
            {
                ConsultationId = consultationId,
                SenderId = userId.Value,
                Type = MessageType.File,
                FileUrl = publicPath,
                FileName = dto.File.FileName,
                FileSize = dto.File.Length,
                Timestamp = DateTime.UtcNow,
                IsRead = false
            };

            _context.ConsultationMessages.Add(msg);
            await _context.SaveChangesAsync(ct);

            await _notificationService.CreateNotificationAsync(
                result.receiverUserId,
                "New file",
                "You received a new file in the consultation.",
                NotificationType.NewMessage,
                relatedEntityId: consultationId,
                relatedEntityType: "Consultation",
                actionUrl: $"/consultations/{consultationId}/chat"
            );

            await _hub.Clients.Group($"consultation:{consultationId}")
                .SendAsync("message", new
                {
                    msg.Id,
                    msg.ConsultationId,
                    msg.SenderId,
                    type = msg.Type,
                    fileUrl = msg.FileUrl,
                    fileFullUrl = fullUrl,
                    fileName = msg.FileName,
                    fileSize = msg.FileSize,
                    msg.Timestamp
                }, ct);

            return Ok(new
            {
                ok = true,
                msg.Id,
                fileUrl = msg.FileUrl,
                fileFullUrl = fullUrl,
                fileName = msg.FileName,
                fileSize = msg.FileSize
            });
        }

    }
}
