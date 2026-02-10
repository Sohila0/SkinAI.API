using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SkinAI.API.Data;
using SkinAI.API.Models;
using System.Security.Claims;

namespace SkinAI.API.Controllers
{
    [ApiController]
    [Route("api/push")]
    [Authorize]
    public class PushController : ControllerBase
    {
        private readonly ApplicationDbContext _db;

        public PushController(ApplicationDbContext db)
        {
            _db = db;
        }

        // ✅ نفس اسم الموديل: OneSignalPlayerId
        public class RegisterPushDto
        {
            public string OneSignalPlayerId { get; set; } = "";
        }

        public class UnregisterPushDto
        {
            public string OneSignalPlayerId { get; set; } = "";
        }

        // ===================== Register/Update token =====================
        // Flutter calls this after login (and sometimes on app start)
        [HttpPost("register")]
        public async Task<IActionResult> Register([FromBody] RegisterPushDto dto, CancellationToken ct)
        {
            var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrWhiteSpace(userIdStr))
                return Unauthorized(new { ok = false, error = "Missing user id." });

            var userId = int.Parse(userIdStr);

            var playerId = (dto.OneSignalPlayerId ?? "").Trim();
            if (string.IsNullOrWhiteSpace(playerId))
                return BadRequest(new { ok = false, error = "OneSignalPlayerId required" });

            // ✅ Upsert
            var tokenRow = await _db.UserPushTokens
                .FirstOrDefaultAsync(x => x.UserId == userId && x.OneSignalPlayerId == playerId, ct);

            if (tokenRow == null)
            {
                _db.UserPushTokens.Add(new UserPushToken
                {
                    UserId = userId,
                    OneSignalPlayerId = playerId,
                    CreatedAt = DateTime.UtcNow,
                    LastSeenAt = DateTime.UtcNow
                });
            }
            else
            {
                tokenRow.LastSeenAt = DateTime.UtcNow;
            }

            await _db.SaveChangesAsync(ct);
            return Ok(new { ok = true });
        }

        // ===================== Unregister token (logout) =====================
        // Flutter calls this on logout (optional but recommended)
        [HttpPost("unregister")]
        public async Task<IActionResult> Unregister([FromBody] UnregisterPushDto dto, CancellationToken ct)
        {
            var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrWhiteSpace(userIdStr))
                return Unauthorized(new { ok = false, error = "Missing user id." });

            var userId = int.Parse(userIdStr);

            var playerId = (dto.OneSignalPlayerId ?? "").Trim();
            if (string.IsNullOrWhiteSpace(playerId))
                return BadRequest(new { ok = false, error = "OneSignalPlayerId required" });

            var row = await _db.UserPushTokens
                .FirstOrDefaultAsync(x => x.UserId == userId && x.OneSignalPlayerId == playerId, ct);

            if (row == null)
                return Ok(new { ok = true, message = "Already removed." });

            _db.UserPushTokens.Remove(row);
            await _db.SaveChangesAsync(ct);

            return Ok(new { ok = true });
        }

        // ===================== Get my registered tokens (debug) =====================
        // Useful for testing during development
        [HttpGet("me")]
        public async Task<IActionResult> MyTokens(CancellationToken ct)
        {
            var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrWhiteSpace(userIdStr))
                return Unauthorized(new { ok = false, error = "Missing user id." });

            var userId = int.Parse(userIdStr);

            var tokens = await _db.UserPushTokens
                .AsNoTracking()
                .Where(x => x.UserId == userId)
                .OrderByDescending(x => x.LastSeenAt ?? x.CreatedAt)
                .Select(x => new
                {
                    x.Id,
                    x.OneSignalPlayerId,
                    x.CreatedAt,
                    x.LastSeenAt
                })
                .ToListAsync(ct);

            return Ok(new { ok = true, data = tokens });
        }
    }
}
