namespace SkinAI.API.Services
{
    public interface IPushSender
    {
        Task SendToUserAsync(int userId, string title, string message, Dictionary<string, string>? data = null, CancellationToken ct = default);
        Task SendToUsersAsync(IEnumerable<int> userIds, string title, string message, Dictionary<string, string>? data = null, CancellationToken ct = default);
    }
}
