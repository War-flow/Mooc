using Microsoft.EntityFrameworkCore;
using Mooc.Data;

namespace Mooc.Services
{
    public class PreRegistrationNotificationService : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<PreRegistrationNotificationService> _logger;
        private readonly TimeSpan _checkInterval = TimeSpan.FromHours(6); // Vérifier toutes les 6 heures

        public PreRegistrationNotificationService(
            IServiceProvider serviceProvider,
            ILogger<PreRegistrationNotificationService> logger)
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
                    await ProcessPreRegistrationNotifications();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Erreur lors du traitement des notifications de préinscription");
                }

                await Task.Delay(_checkInterval, stoppingToken);
            }
        }

        private async Task ProcessPreRegistrationNotifications()
        {
            using var scope = _serviceProvider.CreateScope();
            var preRegistrationService = scope.ServiceProvider.GetRequiredService<IPreRegistrationService>();
            var notificationService = scope.ServiceProvider.GetRequiredService<INotificationService>();
            var contextFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<ApplicationDbContext>>();

            var preRegistrationsToNotify = await preRegistrationService.GetPreRegistrationsForNotificationAsync();

            foreach (var preRegistration in preRegistrationsToNotify)
            {
                try
                {
                    // Convertir automatiquement en inscription
                    var conversionSuccess = await preRegistrationService.ConvertPreRegistrationToEnrollmentAsync(
                        preRegistration.UserId, preRegistration.SessionId);

                    if (conversionSuccess)
                    {
                        // Envoyer email de confirmation d'inscription
                        await notificationService.SendRegistrationConfirmationEmailAsync(
                            preRegistration.User, preRegistration.Session.Title);

                        // Envoyer notification navigateur
                        await notificationService.ShowBrowserNotificationAsync(
                            preRegistration.User.Id,
                            "Inscription confirmée",
                            $"Bonjour {preRegistration.User.FirstName}, votre inscription à \"{preRegistration.Session.Title}\" est confirmée !",
                            "success"
                        );

                        // Marquer comme notifié
                        using var context = await contextFactory.CreateDbContextAsync();
                        var preReg = await context.PreRegistrations.FindAsync(preRegistration.Id);
                        if (preReg != null)
                        {
                            preReg.NotificationSent = DateTime.UtcNow;
                            await context.SaveChangesAsync();
                        }

                        _logger.LogInformation("Préinscription convertie et email de confirmation envoyé pour {Email} sur la session {SessionTitle}",
                            preRegistration.User.Email, preRegistration.Session.Title);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Erreur lors de la conversion de préinscription pour {Email}",
                        preRegistration.User.Email);
                }
            }
        }
    }
}