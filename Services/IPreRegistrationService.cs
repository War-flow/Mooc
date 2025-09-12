using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;

using Mooc.Data;

namespace Mooc.Services
{
    public interface IPreRegistrationService
    {
        Task<bool> PreRegisterUserAsync(string userId, int sessionId);
        Task<bool> CancelPreRegistrationAsync(string userId, int sessionId);
        Task<bool> IsUserPreRegisteredAsync(string userId, int sessionId);
        Task<List<PreRegistration>> GetUserPreRegistrationsAsync(string userId);
        Task<List<PreRegistration>> GetSessionPreRegistrationsAsync(int sessionId);
        Task<bool> ConvertPreRegistrationToEnrollmentAsync(string userId, int sessionId);
        Task<List<PreRegistration>> GetPreRegistrationsForNotificationAsync();
        Task<int> GetPreRegistrationCountAsync(int sessionId);
    }

    public class PreRegistrationService : IPreRegistrationService
    {
        private readonly IDbContextFactory<ApplicationDbContext> _contextFactory;
        private readonly ILogger<PreRegistrationService> _logger;
        private readonly INotificationService _notificationService;

        public PreRegistrationService(
            IDbContextFactory<ApplicationDbContext> contextFactory,
            ILogger<PreRegistrationService> logger,
            INotificationService notificationService)
        {
            _contextFactory = contextFactory;
            _logger = logger;
            _notificationService = notificationService;
        }

        public async Task<bool> PreRegisterUserAsync(string userId, int sessionId)
        {
            try
            {
                using var context = await _contextFactory.CreateDbContextAsync();
                
                // Vérifier si l'utilisateur n'est pas déjà inscrit
                var isAlreadyEnrolled = await context.Sessions
                    .Where(s => s.Id == sessionId)
                    .SelectMany(s => s.EnrolledUsers)
                    .AnyAsync(u => u.Id == userId);

                if (isAlreadyEnrolled)
                {
                    return false; // Déjà inscrit
                }

                // Vérifier si une préinscription existe déjà (peu importe le statut)
                var existingPreRegistration = await context.PreRegistrations
                    .FirstOrDefaultAsync(pr => pr.UserId == userId && pr.SessionId == sessionId);

                if (existingPreRegistration != null)
                {
                    // Si elle existe et est active, on retourne false
                    if (existingPreRegistration.Status == "Active")
                    {
                        return false; // Déjà préinscrit
                    }
                    
                    // Si elle existe mais n'est pas active, on la réactive
                    existingPreRegistration.Status = "Active";
                    existingPreRegistration.PreRegistrationDate = DateTime.UtcNow;
                    existingPreRegistration.Notes = null; // Réinitialiser les notes si nécessaire
                    
                    await context.SaveChangesAsync();
                    _logger.LogInformation("Préinscription réactivée pour l'utilisateur {UserId} sur la session {SessionId}", userId, sessionId);
                    return true;
                }

                // Créer une nouvelle préinscription
                var preRegistration = new PreRegistration
                {
                    UserId = userId,
                    SessionId = sessionId,
                    PreRegistrationDate = DateTime.UtcNow,
                    Status = "Active"
                };

                context.PreRegistrations.Add(preRegistration);
                await context.SaveChangesAsync();

                _logger.LogInformation("Préinscription créée pour l'utilisateur {UserId} sur la session {SessionId}", userId, sessionId);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur lors de la préinscription pour l'utilisateur {UserId} sur la session {SessionId}", userId, sessionId);
                return false;
            }
        }

        public async Task<bool> CancelPreRegistrationAsync(string userId, int sessionId)
        {
            try
            {
                using var context = await _contextFactory.CreateDbContextAsync();
                
                var preRegistration = await context.PreRegistrations
                    .FirstOrDefaultAsync(pr => pr.UserId == userId && pr.SessionId == sessionId && pr.Status == "Active");

                if (preRegistration != null)
                {
                    preRegistration.Status = "Cancelled";
                    await context.SaveChangesAsync();
                    return true;
                }

                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur lors de l'annulation de préinscription pour l'utilisateur {UserId} sur la session {SessionId}", userId, sessionId);
                return false;
            }
        }

        public async Task<bool> IsUserPreRegisteredAsync(string userId, int sessionId)
        {
            try
            {
                using var context = await _contextFactory.CreateDbContextAsync();
                
                return await context.PreRegistrations
                    .AnyAsync(pr => pr.UserId == userId && pr.SessionId == sessionId && pr.Status == "Active");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur lors de la vérification de préinscription pour l'utilisateur {UserId} sur la session {SessionId}", userId, sessionId);
                return false;
            }
        }

        public async Task<List<PreRegistration>> GetUserPreRegistrationsAsync(string userId)
        {
            try
            {
                using var context = await _contextFactory.CreateDbContextAsync();
                
                return await context.PreRegistrations
                    .Include(pr => pr.Session)
                    .Where(pr => pr.UserId == userId && pr.Status == "Active")
                    .OrderBy(pr => pr.PreRegistrationDate)
                    .ToListAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur lors de la récupération des préinscriptions pour l'utilisateur {UserId}", userId);
                return new List<PreRegistration>();
            }
        }

        public async Task<List<PreRegistration>> GetSessionPreRegistrationsAsync(int sessionId)
        {
            try
            {
                using var context = await _contextFactory.CreateDbContextAsync();
                
                return await context.PreRegistrations
                    .Include(pr => pr.User)
                    .Where(pr => pr.SessionId == sessionId && pr.Status == "Active")
                    .OrderBy(pr => pr.PreRegistrationDate)
                    .ToListAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur lors de la récupération des préinscriptions pour la session {SessionId}", sessionId);
                return new List<PreRegistration>();
            }
        }

        public async Task<bool> ConvertPreRegistrationToEnrollmentAsync(string userId, int sessionId)
        {
            try
            {
                using var context = await _contextFactory.CreateDbContextAsync();
                using var transaction = await context.Database.BeginTransactionAsync();

                var preRegistration = await context.PreRegistrations
                    .FirstOrDefaultAsync(pr => pr.UserId == userId && pr.SessionId == sessionId && pr.Status == "Active");

                if (preRegistration == null)
                    return false;

                // Ajouter à la session
                var user = await context.Users
                    .Include(u => u.EnrolledSessions)
                    .FirstOrDefaultAsync(u => u.Id == userId);

                var session = await context.Sessions.FindAsync(sessionId);

                if (user != null && session != null)
                {
                    if (user.EnrolledSessions == null)
                        user.EnrolledSessions = new List<Session>();

                    if (!user.EnrolledSessions.Any(s => s.Id == sessionId))
                    {
                        user.EnrolledSessions.Add(session);
                    }

                    // Marquer la préinscription comme convertie
                    preRegistration.Status = "Converted";
                    
                    await context.SaveChangesAsync();
                    await transaction.CommitAsync();

                    _logger.LogInformation("Préinscription convertie en inscription pour l'utilisateur {UserId} sur la session {SessionId}", userId, sessionId);
                    return true;
                }

                await transaction.RollbackAsync();
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur lors de la conversion de préinscription pour l'utilisateur {UserId} sur la session {SessionId}", userId, sessionId);
                return false;
            }
        }

        public async Task<List<PreRegistration>> GetPreRegistrationsForNotificationAsync()
        {
            try
            {
                using var context = await _contextFactory.CreateDbContextAsync();
                
                var tomorrow = DateTime.UtcNow.AddDays(1); // Changé vers UTC
                var now = DateTime.UtcNow; // Changé vers UTC
                
                return await context.PreRegistrations
                    .Include(pr => pr.User)
                    .Include(pr => pr.Session)
                    .Where(pr => pr.Status == "Active" 
                                && pr.Session.StartDate <= tomorrow 
                                && pr.Session.StartDate > now // Utilise la variable now UTC
                                && pr.NotificationSent == null)
                    .ToListAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur lors de la récupération des préinscriptions pour notification");
                return new List<PreRegistration>();
            }
        }

        public async Task<int> GetPreRegistrationCountAsync(int sessionId)
        {
            try
            {
                using var context = await _contextFactory.CreateDbContextAsync();
                
                return await context.PreRegistrations
                    .CountAsync(pr => pr.SessionId == sessionId && pr.Status == "Active");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur lors du comptage des préinscriptions pour la session {SessionId}", sessionId);
                return 0;
            }
        }
    }
}