using Microsoft.AspNetCore.SignalR;

namespace WebLoginDemo2.Hubs
{
    public class SensorHub : Hub
    {
        public override async Task OnConnectedAsync()
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, "Dashboard");
            await base.OnConnectedAsync();
        }

        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, "Dashboard");
            await base.OnDisconnectedAsync(exception);
        }
    }
}