using System.Collections.Concurrent;

namespace SkinAI.API.Services
{
    public class PresenceTracker
    {
        private readonly ConcurrentDictionary<int, int> _online = new();

        public void UserConnected(int userId)
        {
            _online.AddOrUpdate(userId, 1, (_, count) => count + 1);
        }

        public void UserDisconnected(int userId)
        {
            _online.AddOrUpdate(userId, 0, (_, count) => Math.Max(0, count - 1));
            if (_online.TryGetValue(userId, out var c) && c == 0)
                _online.TryRemove(userId, out _);
        }

        public bool IsOnline(int userId) => _online.ContainsKey(userId);
    }
}
