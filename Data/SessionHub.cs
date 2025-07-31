using Microsoft.AspNetCore.SignalR;

public class SessionHub : Hub
{
    // Méthode appelée pour notifier tous les clients d'une mise à jour
    public async Task NotifyEnrolledCountChanged(int sessionId, int count)
    {
        await Clients.All.SendAsync("EnrolledCountChanged", sessionId, count);
    }
}