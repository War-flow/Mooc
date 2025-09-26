using Microsoft.Extensions.DependencyInjection;
using Mooc.Data;

namespace Mooc.Services
{
    public interface ICertificateNotificationService
    {
        Task NotifySessionCompletedAsync(string userId, int sessionId);
    }

    public class CertificateNotificationService : ICertificateNotificationService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<CertificateNotificationService> _logger;

        public CertificateNotificationService(
            IServiceProvider serviceProvider,
            ILogger<CertificateNotificationService> logger)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
        }

        public async Task NotifySessionCompletedAsync(string userId, int sessionId)
        {
            try
            {
                _logger.LogInformation("🔔 Notification session terminée - Session {SessionId}, User {UserId}", sessionId, userId);
                
                // Utiliser un scope séparé pour éviter les dépendances circulaires
                using var scope = _serviceProvider.CreateScope();
                var automaticCertificateService = scope.ServiceProvider.GetRequiredService<IAutomaticCertificateService>();
                
                await automaticCertificateService.CheckAndGenerateCertificateAsync(userId, sessionId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Erreur lors de la notification de session terminée - Session {SessionId}, User {UserId}", sessionId, userId);
            }
        }
    }
}