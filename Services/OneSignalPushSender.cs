using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SkinAI.API.Data;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace SkinAI.API.Services
{
    public class OneSignalPushSender : IPushSender
    {
        private readonly ApplicationDbContext _db;
        private readonly OneSignalSettings _settings;
        private readonly HttpClient _http;

        public OneSignalPushSender(
            ApplicationDbContext db,
            IOptions<OneSignalSettings> options,
            IHttpClientFactory httpFactory)
        {
            _db = db;
            _settings = options.Value;
            _http = httpFactory.CreateClient();
        }

        public async Task SendToUserAsync(int userId, string title, string message, Dictionary<string, string>? data = null, CancellationToken ct = default)
        {
            await SendToUsersAsync(new[] { userId }, title, message, data, ct);
        }

        public async Task SendToUsersAsync(IEnumerable<int> userIds, string title, string message, Dictionary<string, string>? data = null, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(_settings.AppId) || string.IsNullOrWhiteSpace(_settings.RestApiKey))
                return; // مش مكوَّن

            var ids = userIds.Distinct().ToList();
            if (ids.Count == 0) return;

            var playerIds = await _db.UserPushTokens
                .AsNoTracking()
                .Where(x => ids.Contains(x.UserId))
                .Select(x => x.OneSignalPlayerId)
                .Distinct()
                .ToListAsync(ct);

            if (playerIds.Count == 0) return;

            var payload = new Dictionary<string, object?>
            {
                ["app_id"] = _settings.AppId,
                ["include_player_ids"] = playerIds,
                ["headings"] = new Dictionary<string, string> { ["en"] = title },
                ["contents"] = new Dictionary<string, string> { ["en"] = message },
            };

            // ✅ Data لتوجيه التطبيق (deeplink)
            if (data != null && data.Count > 0)
                payload["data"] = data;

            var json = JsonSerializer.Serialize(payload);

            using var req = new HttpRequestMessage(HttpMethod.Post, "https://onesignal.com/api/v1/notifications");
            req.Headers.Authorization = new AuthenticationHeaderValue("Basic", _settings.RestApiKey);
            req.Content = new StringContent(json, Encoding.UTF8, "application/json");

            using var resp = await _http.SendAsync(req, ct);
            // لو فشل مش هنكسر السيستم
            _ = await resp.Content.ReadAsStringAsync(ct);
        }
    }
}
