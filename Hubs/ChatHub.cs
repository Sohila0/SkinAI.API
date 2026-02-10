using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace SkinAI.API.Hubs
{
    [Authorize]
    public class ChatHub : Hub
    {
        // join a consultation room
        public async Task JoinConsultation(int consultationId)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, $"consultation:{consultationId}");
        }

        public async Task LeaveConsultation(int consultationId)
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"consultation:{consultationId}");
        }
    }
}
