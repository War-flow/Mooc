using Microsoft.EntityFrameworkCore;
using Mooc.Data;

namespace Mooc.Services
{
    public class SessionExpiryService : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<SessionExpiryService> _logger;

        public SessionExpiryService(IServiceProvider serviceProvider, ILogger<SessionExpiryService> logger)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("SessionExpiryService démarré");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await CheckSessionsAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Erreur lors de la vérification des sessions");
                }

                // Vérifier toutes les 5 minutes pour plus de réactivité
                await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
            }
        }

        private async Task CheckSessionsAsync()
        {
            using var scope = _serviceProvider.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            
            var now = DateTime.Now;

            // **PRINCIPAL : Désactiver les sessions expirées**
            var expiredSessions = await context.Sessions
                .Where(s => s.IsActive && s.EndDate < now)
                .ToListAsync();

            if (expiredSessions.Any())
            {
                foreach (var session in expiredSessions)
                {
                    session.IsActive = false;
                    _logger.LogInformation($"Session '{session.Title}' (ID: {session.Id}) automatiquement mise hors ligne - terminée le {session.EndDate:dd/MM/yyyy HH:mm}");
                }

                await context.SaveChangesAsync();
                _logger.LogInformation($"{expiredSessions.Count} session(s) mise(s) hors ligne automatiquement");
            }

            // Optionnel : Notifier les sessions qui vont se terminer
            try
            {
                var notificationService = scope.ServiceProvider.GetService<INotificationService>();
                if (notificationService != null)
                {
                    await SendExpirationNotifications(context, notificationService, now);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Erreur lors de l'envoi des notifications");
                // Ne pas faire échouer le processus principal
            }
        }

        private async Task SendExpirationNotifications(ApplicationDbContext context, INotificationService notificationService, DateTime now)
        {
            // Sessions se terminant dans 24h
            var sessionsEndingIn24h = await context.Sessions
                .Where(s => s.IsActive && 
                           s.EndDate > now && 
                           s.EndDate <= now.AddHours(24) &&
                           !s.NotificationSent24h)
                .ToListAsync();

            foreach (var session in sessionsEndingIn24h)
            {
                await notificationService.NotifySessionEndingAsync(session.Id, TimeSpan.FromHours(24));
                session.NotificationSent24h = true;
            }

            // Sessions se terminant dans 1h
            var sessionsEndingIn1h = await context.Sessions
                .Where(s => s.IsActive && 
                           s.EndDate > now && 
                           s.EndDate <= now.AddHours(1) &&
                           !s.NotificationSent1h)
                .ToListAsync();

            foreach (var session in sessionsEndingIn1h)
            {
                await notificationService.NotifySessionEndingAsync(session.Id, TimeSpan.FromHours(1));
                session.NotificationSent1h = true;
            }

            if (sessionsEndingIn24h.Any() || sessionsEndingIn1h.Any())
            {
                await context.SaveChangesAsync();
            }
        }
    }
}