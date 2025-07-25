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

        private static string GetTimeText(TimeSpan timeSpan)
        {
            if (timeSpan.TotalDays >= 1)
                return $"{(int)timeSpan.TotalDays} jour(s)";
            if (timeSpan.TotalHours >= 1)
                return $"{(int)timeSpan.TotalHours} heure(s)";
            return $"{(int)timeSpan.TotalMinutes} minute(s)";
        }
    }
}