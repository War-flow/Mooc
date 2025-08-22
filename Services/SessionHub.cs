using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using System.Security.Claims;

// ✅ CRÉER : Hub sécurisé

// Renommer la classe pour éviter le conflit de nom
[Authorize]
public class SecureSessionHub : Hub
{
    public override async Task OnConnectedAsync()
    {
        // Vérifier les permissions utilisateur
        var user = Context.User;
        if (user?.Identity?.IsAuthenticated != true)
        {
            Context.Abort();
            return;
        }
        
        // Logger les connexions
        var logger = Context.GetHttpContext()?.RequestServices.GetService<ILogger<SecureSessionHub>>();
        logger?.LogInformation("Utilisateur {UserId} connecté au hub", user.FindFirst(ClaimTypes.NameIdentifier)?.Value);
        
        await base.OnConnectedAsync();
    }
}