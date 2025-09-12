using Microsoft.EntityFrameworkCore;
using Mooc.Data;

namespace Mooc.Services
{
    public interface IEnrollmentService
    {
        Task<EnrollmentResult> EnrollUserAsync(string userId, int sessionId, bool fromPreRegistration = false);
        Task<UnsubscribeResult> UnsubscribeUserAsync(string userId, int sessionId);
        Task<List<Session>> GetUserEnrollmentsAsync(string userId);
        Task<bool> IsUserEnrolledAsync(string userId, int sessionId);
        Task<int> GetSessionEnrollmentCountAsync(int sessionId);
        Task<List<UnsubscribeRestriction>> GetUnsubscribeRestrictionsAsync(string userId, int sessionId);
        Task<bool> CanUserReEnrollAsync(string userId, int sessionId);
        Task<EnrollmentHistory> GetUserEnrollmentHistoryAsync(string userId, int sessionId);
    }

    public class EnrollmentService : IEnrollmentService
    {
        private readonly IDbContextFactory<ApplicationDbContext> _contextFactory;
        private readonly ILogger<EnrollmentService> _logger;
        private readonly IPreRegistrationService _preRegistrationService;
        private readonly INotificationService _notificationService;

        public EnrollmentService(
            IDbContextFactory<ApplicationDbContext> contextFactory,
            ILogger<EnrollmentService> logger,
            IPreRegistrationService preRegistrationService,
            INotificationService notificationService)
        {
            _contextFactory = contextFactory;
            _logger = logger;
            _preRegistrationService = preRegistrationService;
            _notificationService = notificationService;
        }

        public async Task<EnrollmentResult> EnrollUserAsync(string userId, int sessionId, bool fromPreRegistration = false)
        {
            try
            {
                using var context = await _contextFactory.CreateDbContextAsync();
                using var transaction = await context.Database.BeginTransactionAsync();

                var session = await context.Sessions
                    .Include(s => s.EnrolledUsers)
                    .FirstOrDefaultAsync(s => s.Id == sessionId);

                if (session == null)
                    return EnrollmentResult.Failed("Session introuvable");

                if (!session.IsActive)
                    return EnrollmentResult.Failed("Session non disponible");

                var now = DateTime.Now;
                if (now < session.StartDate || now > session.EndDate)
                    return EnrollmentResult.Failed("Session en dehors de la période d'inscription");

                // Vérifier si déjà inscrit
                var isAlreadyEnrolled = session.EnrolledUsers.Any(u => u.Id == userId);
                if (isAlreadyEnrolled)
                    return EnrollmentResult.Failed("Utilisateur déjà inscrit");

                var user = await context.Users.FindAsync(userId);
                if (user == null)
                    return EnrollmentResult.Failed("Utilisateur introuvable");

                // Ajouter l'inscription
                session.EnrolledUsers.Add(user);

                // Créer un historique
                var history = new EnrollmentHistory
                {
                    UserId = userId,
                    SessionId = sessionId,
                    Action = "Enrollment",
                    Date = DateTime.UtcNow,
                    FromPreRegistration = fromPreRegistration,
                    Notes = fromPreRegistration ? "Conversion automatique depuis une préinscription" : "Inscription directe"
                };

                context.EnrollmentHistories.Add(history);
                await context.SaveChangesAsync();
                await transaction.CommitAsync();

                // Convertir préinscription si applicable
                if (fromPreRegistration)
                {
                    await _preRegistrationService.ConvertPreRegistrationToEnrollmentAsync(userId, sessionId);
                }

                // Notification
                await _notificationService.SendRegistrationConfirmationEmailAsync(user, session.Title);

                _logger.LogInformation("Inscription réussie pour l'utilisateur {UserId} à la session {SessionId}", userId, sessionId);
                return EnrollmentResult.Success("Inscription confirmée avec succès");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur lors de l'inscription de l'utilisateur {UserId} à la session {SessionId}", userId, sessionId);
                return EnrollmentResult.Failed($"Erreur technique : {ex.Message}");
            }
        }

        public async Task<UnsubscribeResult> UnsubscribeUserAsync(string userId, int sessionId)
        {
            try
            {
                using var context = await _contextFactory.CreateDbContextAsync();
                using var transaction = await context.Database.BeginTransactionAsync();

                // Vérifier les restrictions
                var restrictions = await GetUnsubscribeRestrictionsAsync(userId, sessionId);
                if (restrictions.Any(r => r.IsBlocking))
                {
                    var blockingRestriction = restrictions.First(r => r.IsBlocking);
                    return UnsubscribeResult.Failed(blockingRestriction.Message);
                }

                var session = await context.Sessions
                    .Include(s => s.EnrolledUsers)
                    .FirstOrDefaultAsync(s => s.Id == sessionId);

                if (session == null)
                    return UnsubscribeResult.Failed("Session introuvable");

                var user = session.EnrolledUsers.FirstOrDefault(u => u.Id == userId);
                if (user == null)
                    return UnsubscribeResult.Failed("Utilisateur non inscrit à cette session");

                // Retirer l'inscription
                session.EnrolledUsers.Remove(user);

                // Créer un historique
                var history = new EnrollmentHistory
                {
                    UserId = userId,
                    SessionId = sessionId,
                    Action = "Unsubscribe",
                    Date = DateTime.UtcNow,
                    Notes = "Désinscription volontaire"
                };

                context.EnrollmentHistories.Add(history);

                // Supprimer les progrès (optionnel - à discuter selon les besoins métier)
                var courseProgresses = await context.CourseProgresses
                    .Where(cp => cp.UserId == userId && 
                                cp.Cours.SessionId == sessionId)
                    .ToListAsync();

                if (courseProgresses.Any())
                {
                    context.CourseProgresses.RemoveRange(courseProgresses);
                }

                await context.SaveChangesAsync();
                await transaction.CommitAsync();

                _logger.LogInformation("Désinscription réussie pour l'utilisateur {UserId} de la session {SessionId}", userId, sessionId);
                return UnsubscribeResult.Success("Désinscription effectuée avec succès");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur lors de la désinscription de l'utilisateur {UserId} de la session {SessionId}", userId, sessionId);
                return UnsubscribeResult.Failed($"Erreur technique : {ex.Message}");
            }
        }

        public async Task<List<UnsubscribeRestriction>> GetUnsubscribeRestrictionsAsync(string userId, int sessionId)
        {
            var restrictions = new List<UnsubscribeRestriction>();
            
            try
            {
                using var context = await _contextFactory.CreateDbContextAsync();
                
                var session = await context.Sessions.FindAsync(sessionId);
                if (session == null) return restrictions;

                var now = DateTime.Now;

                // Restriction : Session déjà terminée depuis plus de 30 jours
                if (session.EndDate < now.AddDays(-30))
                {
                    restrictions.Add(new UnsubscribeRestriction
                    {
                        Type = "SessionTooOld",
                        Message = "Impossible de se désinscrire d'une session terminée depuis plus de 30 jours",
                        IsBlocking = true
                    });
                }

                // Restriction : Certificat déjà délivré
                var hasCertificate = await context.Certificates
                    .AnyAsync(c => c.UserId == userId && c.SessionId == sessionId && c.Status == "Delivered");

                if (hasCertificate)
                {
                    restrictions.Add(new UnsubscribeRestriction
                    {
                        Type = "CertificateDelivered",
                        Message = "Impossible de se désinscrire : un certificat a déjà été délivré pour cette session",
                        IsBlocking = true
                    });
                }

                // Avertissement : Progrès perdu
                var hasProgress = await context.CourseProgresses
                    .AnyAsync(cp => cp.UserId == userId && cp.Cours.SessionId == sessionId);

                if (hasProgress)
                {
                    restrictions.Add(new UnsubscribeRestriction
                    {
                        Type = "ProgressLoss",
                        Message = "Attention : votre progression dans les cours sera définitivement perdue",
                        IsBlocking = false
                    });
                }

                return restrictions;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur lors de la vérification des restrictions de désinscription");
                return restrictions;
            }
        }

        public async Task<bool> CanUserReEnrollAsync(string userId, int sessionId)
        {
            try
            {
                using var context = await _contextFactory.CreateDbContextAsync();
                
                var session = await context.Sessions.FindAsync(sessionId);
                if (session == null || !session.IsActive) return false;

                var now = DateTime.Now;
                if (now < session.StartDate || now > session.EndDate) return false;

                // Vérifier l'historique - permettre la réinscription si pas de certificat délivré
                var lastHistory = await context.EnrollmentHistories
                    .Where(h => h.UserId == userId && h.SessionId == sessionId)
                    .OrderByDescending(h => h.Date)
                    .FirstOrDefaultAsync();

                if (lastHistory?.Action == "Unsubscribe")
                {
                    var hasCertificate = await context.Certificates
                        .AnyAsync(c => c.UserId == userId && c.SessionId == sessionId && c.Status == "Delivered");
                    
                    return !hasCertificate;
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        public async Task<EnrollmentHistory> GetUserEnrollmentHistoryAsync(string userId, int sessionId)
        {
            using var context = await _contextFactory.CreateDbContextAsync();
            
            var histories = await context.EnrollmentHistories
                .Where(h => h.UserId == userId && h.SessionId == sessionId)
                .OrderBy(h => h.Date)
                .ToListAsync();

            return new EnrollmentHistory
            {
                EnrollmentCount = histories.Count(h => h.Action == "Enrollment"),
                UnsubscribeCount = histories.Count(h => h.Action == "Unsubscribe"),
                LastAction = histories.LastOrDefault()?.Action,
                LastActionDate = histories.LastOrDefault()?.Date
            };
        }

        public async Task<List<Session>> GetUserEnrollmentsAsync(string userId)
        {
            using var context = await _contextFactory.CreateDbContextAsync();
            
            return await context.Users
                .Where(u => u.Id == userId)
                .SelectMany(u => u.EnrolledSessions)
                .Include(s => s.Courses)
                .AsNoTracking()
                .ToListAsync();
        }

        public async Task<bool> IsUserEnrolledAsync(string userId, int sessionId)
        {
            using var context = await _contextFactory.CreateDbContextAsync();
            
            return await context.Sessions
                .Where(s => s.Id == sessionId)
                .SelectMany(s => s.EnrolledUsers)
                .AnyAsync(u => u.Id == userId);
        }

        public async Task<int> GetSessionEnrollmentCountAsync(int sessionId)
        {
            using var context = await _contextFactory.CreateDbContextAsync();
            
            return await context.Sessions
                .Where(s => s.Id == sessionId)
                .SelectMany(s => s.EnrolledUsers)
                .CountAsync();
        }
    }

    // Modèles de résultat
    public class EnrollmentResult
    {
        public bool IsSuccess { get; set; }
        public string Message { get; set; } = string.Empty;
        public static EnrollmentResult Success(string message) => new() { IsSuccess = true, Message = message };
        public static EnrollmentResult Failed(string message) => new() { IsSuccess = false, Message = message };
    }

    public class UnsubscribeResult
    {
        public bool IsSuccess { get; set; }
        public string Message { get; set; } = string.Empty;
        public static UnsubscribeResult Success(string message) => new() { IsSuccess = true, Message = message };
        public static UnsubscribeResult Failed(string message) => new() { IsSuccess = false, Message = message };
    }

    public class UnsubscribeRestriction
    {
        public string Type { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public bool IsBlocking { get; set; }
    }
}