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

                // Vérifier toutes les 30 minutes
                await Task.Delay(TimeSpan.FromMinutes(30), stoppingToken);
            }
        }

        private async Task CheckSessionsAsync()
        {
            using var scope = _serviceProvider.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var notificationService = scope.ServiceProvider.GetRequiredService<INotificationService>();

            var now = DateTime.Now;

            // 1. Notifier les sessions qui se terminent dans 24h
            var sessionsEndingIn24h = await context.Sessions
                .Where(s => s.IsActive && 
                           s.EndDate > now && 
                           s.EndDate <= now.AddHours(24) &&
                           !s.NotificationSent24h) // Supposons qu'on ajoute ce champ
                .ToListAsync();

            foreach (var session in sessionsEndingIn24h)
            {
                await notificationService.NotifySessionEndingAsync(session.Id, TimeSpan.FromHours(24));
                session.NotificationSent24h = true;
            }

            // 2. Notifier les sessions qui se terminent dans 1h
            var sessionsEndingIn1h = await context.Sessions
                .Where(s => s.IsActive && 
                           s.EndDate > now && 
                           s.EndDate <= now.AddHours(1) &&
                           !s.NotificationSent1h) // Supposons qu'on ajoute ce champ
                .ToListAsync();

            foreach (var session in sessionsEndingIn1h)
            {
                await notificationService.NotifySessionEndingAsync(session.Id, TimeSpan.FromHours(1));
                session.NotificationSent1h = true;
            }

            // 3. Désactiver et notifier les sessions expirées
            var expiredSessions = await context.Sessions
                .Where(s => s.IsActive && s.EndDate < now)
                .ToListAsync();

            foreach (var session in expiredSessions)
            {
                session.IsActive = false;
                await notificationService.NotifySessionEndedAsync(session.Id);
                _logger.LogInformation($"Session '{session.Title}' (ID: {session.Id}) automatiquement désactivée");
            }

            if (sessionsEndingIn24h.Any() || sessionsEndingIn1h.Any() || expiredSessions.Any())
            {
                await context.SaveChangesAsync();
            }
        }
    }
}