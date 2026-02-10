using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SkinAI.API.Data;
using System.Security.Claims;

namespace SkinAI.API.Controllers
{
    [ApiController]
    [Route("api/notifications")]
    [Authorize]
    public class NotificationsController : ControllerBase
    {
        private readonly ApplicationDbContext _context;

        public NotificationsController(ApplicationDbContext context)
        {
            _context = context;
        }

        private int GetUserId()
        {
            var idStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrWhiteSpace(idStr))
                throw new UnauthorizedAccessException("Missing user id claim.");

            return int.Parse(idStr);
        }

        // ✅ GET: api/notifications?page=1&pageSize=20&unreadOnly=false
        [HttpGet]
        public async Task<IActionResult> GetMyNotifications(
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20,
            [FromQuery] bool unreadOnly = false,
            CancellationToken ct = default)
        {
            if (page <= 0) page = 1;
            if (pageSize <= 0) pageSize = 20;
            if (pageSize > 50) pageSize = 50; // protect DB

            var userId = GetUserId();

            var q = _context.Notifications
                .AsNoTracking()
                .Where(n => n.UserId == userId && !n.IsDeleted);

            if (unreadOnly)
                q = q.Where(n => !n.IsRead);

            var total = await q.CountAsync(ct);

            var data = await q
                .OrderByDescending(n => n.CreatedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(n => new
                {
                    n.Id,
                    n.Title,
                    n.Message,
                    type = n.Type,
                    n.IsRead,
                    n.ReadAt,
                    n.CreatedAt,
                    n.RelatedEntityId,
                    n.RelatedEntityType,
                    n.ActionUrl
                })
                .ToListAsync(ct);

            return Ok(new
            {
                ok = true,
                page,
                pageSize,
                total,
                data
            });
        }

        // ✅ GET: api/notifications/unread-count
        [HttpGet("unread-count")]
        public async Task<IActionResult> GetUnreadCount(CancellationToken ct)
        {
            var userId = GetUserId();

            var count = await _context.Notifications
                .AsNoTracking()
                .Where(n => n.UserId == userId && !n.IsDeleted && !n.IsRead)
                .CountAsync(ct);

            return Ok(new { ok = true, unreadCount = count });
        }

        // ✅ PATCH: api/notifications/{id}/read
        [HttpPatch("{id:int}/read")]
        public async Task<IActionResult> MarkAsRead(int id, CancellationToken ct)
        {
            var userId = GetUserId();

            var n = await _context.Notifications
                .FirstOrDefaultAsync(x => x.Id == id && x.UserId == userId && !x.IsDeleted, ct);

            if (n == null)
                return NotFound(new { ok = false, error = "Notification not found." });

            if (!n.IsRead)
            {
                n.IsRead = true;
                n.ReadAt = DateTime.UtcNow;
                await _context.SaveChangesAsync(ct);
            }

            return Ok(new { ok = true });
        }

        // ✅ PATCH: api/notifications/read-all
        [HttpPatch("read-all")]
        public async Task<IActionResult> MarkAllAsRead(CancellationToken ct)
        {
            var userId = GetUserId();

            var list = await _context.Notifications
                .Where(n => n.UserId == userId && !n.IsDeleted && !n.IsRead)
                .ToListAsync(ct);

            if (list.Count == 0)
                return Ok(new { ok = true, updated = 0 });

            foreach (var n in list)
            {
                n.IsRead = true;
                n.ReadAt = DateTime.UtcNow;
            }

            await _context.SaveChangesAsync(ct);
            return Ok(new { ok = true, updated = list.Count });
        }

        // ✅ DELETE: api/notifications/{id}
        // soft delete (safe)
        [HttpDelete("{id:int}")]
        public async Task<IActionResult> DeleteNotification(int id, CancellationToken ct)
        {
            var userId = GetUserId();

            var n = await _context.Notifications
                .FirstOrDefaultAsync(x => x.Id == id && x.UserId == userId, ct);

            if (n == null)
                return NotFound(new { ok = false, error = "Notification not found." });

            if (!n.IsDeleted)
            {
                n.IsDeleted = true;
                await _context.SaveChangesAsync(ct);
            }

            return Ok(new { ok = true });
        }
    }
}
