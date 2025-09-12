using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Mooc.Data;

namespace Mooc.Services
{
    public interface INotificationService
    {
        Task NotifySessionEndingAsync(int sessionId, TimeSpan timeBeforeEnd);
        Task NotifySessionEndedAsync(int sessionId);
        Task ShowBrowserNotificationAsync(string userId, string title, string message, string type = "info");
        Task SendRegistrationConfirmationEmailAsync(ApplicationUser user, string sessionTitle);
    }

    public class NotificationService : INotificationService
    {
        private readonly IDbContextFactory<ApplicationDbContext> _dbContextFactory;
        private readonly IEmailSender<ApplicationUser> _emailSender;
        private readonly ILogger<NotificationService> _logger;
        private readonly UserManager<ApplicationUser> _userManager;

        public NotificationService(
            IDbContextFactory<ApplicationDbContext> dbContextFactory,
            IEmailSender<ApplicationUser> emailSender,
            ILogger<NotificationService> logger,
            UserManager<ApplicationUser> userManager)
        {
            _dbContextFactory = dbContextFactory;
            _emailSender = emailSender;
            _logger = logger;
            _userManager = userManager;
        }

        public async Task NotifySessionEndingAsync(int sessionId, TimeSpan timeBeforeEnd)
        {
            using var context = await _dbContextFactory.CreateDbContextAsync();
            
            var session = await context.Sessions
                .Include(s => s.EnrolledUsers)
                .FirstOrDefaultAsync(s => s.Id == sessionId);

            if (session?.EnrolledUsers == null) return;

            var subject = $"🔔 Session '{session.Title}' se termine bientôt";
            var timeText = GetTimeText(timeBeforeEnd);
            
            foreach (var user in (IEnumerable<ApplicationUser>)session.EnrolledUsers)
            {
                var emailBody = $@"
                    <h3>Bonjour {user.FirstName},</h3>
                    <p>Nous vous informons que la session <strong>'{session.Title}'</strong> se termine dans <strong>{timeText}</strong>.</p>
                    <p><strong>Date de fin :</strong> {session.EndDate:dd/MM/yyyy à HH:mm}</p>
                    <p>Assurez-vous de terminer tous vos cours avant cette date.</p>
                    <p>Cordialement,<br/>L'équipe MOOC</p>
                ";

                try
                {
                    await _emailSender.SendConfirmationLinkAsync(user, user.Email, "Lien de confirmation");
                    _logger.LogInformation($"Notification d'expiration envoyée à {user.Email} pour la session {sessionId}");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Erreur lors de l'envoi de notification à {user.Email}");
                }
            }
        }

        public async Task NotifySessionEndedAsync(int sessionId)
        {
            using var context = await _dbContextFactory.CreateDbContextAsync();
            
            var session = await context.Sessions
                .Include(s => s.EnrolledUsers)
                .FirstOrDefaultAsync(s => s.Id == sessionId);

            if (session?.EnrolledUsers == null) return;

            var subject = $"📋 Session '{session.Title}' terminée";
            
            foreach (var user in (IEnumerable<ApplicationUser>)session.EnrolledUsers)
            {
                var emailBody = $@"
                    <h3>Bonjour {user.FirstName},</h3>
                    <p>La session <strong>'{session.Title}'</strong> s'est terminée le {session.EndDate:dd/MM/yyyy à HH:mm}.</p>
                    <p>Vous pouvez toujours consulter les cours, mais vous ne pouvez plus progresser dans cette session.</p>
                    <p>Merci d'avoir participé à cette formation !</p>
                    <p>Cordialement,<br/>L'équipe MOOC</p>
                ";

                try
                {
                    await _emailSender.SendConfirmationLinkAsync(user, user.Email, "Lien de confirmation");
                    _logger.LogInformation($"Notification de fin de session envoyée à {user.Email} pour la session {sessionId}");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Erreur lors de l'envoi de notification à {user.Email}");
                }
            }
        }

        public async Task ShowBrowserNotificationAsync(string userId, string title, string message, string type = "info")
        {
            // Cette méthode sera utilisée pour les notifications en temps réel (voir SignalR plus bas)
            _logger.LogInformation($"Notification navigateur pour {userId}: {title} - {message}");
        }

        public async Task SendRegistrationConfirmationEmailAsync(ApplicationUser user, string sessionTitle)
        {
            var subject = "✅ Inscription confirmée - " + sessionTitle;
            var emailBody = $@"
                <!DOCTYPE html>
                <html>
                <head>
                    <meta charset='utf-8'>
                    <meta name='viewport' content='width=device-width, initial-scale=1.0'>
                    <title>Inscription confirmée</title>
                </head>
                <body style='font-family: Arial, sans-serif; line-height: 1.6; color: #333; max-width: 600px; margin: 0 auto; padding: 20px;'>
                    <div style='background-color: #f8f9fa; padding: 30px; border-radius: 10px;'>
                        <h2 style='color: #28a745; margin-bottom: 20px;'>🎉 Inscription confirmée !</h2>
                        <p style='margin-bottom: 15px;'>Bonjour {user.FirstName ?? user.UserName},</p>
                        <p style='margin-bottom: 25px;'>
                            Félicitations ! Votre inscription à la session <strong>'{sessionTitle}'</strong> a été confirmée avec succès.
                        </p>
                        <div style='background-color: #d4edda; border: 1px solid #c3e6cb; border-radius: 5px; padding: 15px; margin: 20px 0;'>
                            <h4 style='color: #155724; margin: 0 0 10px 0;'>📚 Prochaines étapes :</h4>
                            <ul style='color: #155724; margin: 0; padding-left: 20px;'>
                                <li>Connectez-vous à votre compte pour accéder aux cours</li>
                                <li>Consultez le programme de la session</li>
                                <li>Préparez-vous pour le début de la formation</li>
                            </ul>
                        </div>
                        <div style='text-align: center; margin: 30px 0;'>
                            <a href='{GetLoginUrl()}' style='background-color: #007bff; color: white; padding: 15px 30px; text-decoration: none; border-radius: 5px; display: inline-block; font-weight: bold;'>
                                Accéder à mon compte
                            </a>
                        </div>
                        <p style='margin-top: 25px; font-size: 14px; color: #666;'>
                            Si vous avez des questions, n'hésitez pas à nous contacter.
                        </p>
                        <hr style='border: none; border-top: 1px solid #eee; margin: 20px 0;'>
                        <p style='font-size: 12px; color: #999; text-align: center;'>
                            Cordialement,<br>L'équipe MOOC Platform
                        </p>
                    </div>
                </body>
                </html>";

            try
            {
                await _emailSender.SendConfirmationLinkAsync(user, user.Email, "Lien de confirmation");
                _logger.LogInformation("Email de confirmation d'inscription envoyé à {Email} pour la session {SessionTitle}", 
                    user.Email, sessionTitle);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur lors de l'envoi de l'email de confirmation à {Email}", user.Email);
                throw;
            }
        }

        private static string GetTimeText(TimeSpan timeSpan)
        {
            if (timeSpan.TotalDays >= 1)
                return $"{(int)timeSpan.TotalDays} jour(s)";
            if (timeSpan.TotalHours >= 1)
                return $"{(int)timeSpan.TotalHours} heure(s)";
            return $"{(int)timeSpan.TotalMinutes} minute(s)";
        }

        private string GetLoginUrl()
        {
            // Vous pouvez configurer cette URL selon votre environnement
            return "https://votre-domaine.com/Account/Login"; // À adapter selon votre configuration
        }
    }
}