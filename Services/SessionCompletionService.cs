using Microsoft.EntityFrameworkCore;
using Mooc.Data;

namespace Mooc.Services
{
    public interface ISessionCompletionService
    {
        Task<bool> IsSessionCompletedByUserAsync(string userId, int sessionId);
        Task<SessionCompletionInfo> GetSessionCompletionInfoAsync(string userId, int sessionId);
        Task<Dictionary<int, SessionCompletionInfo>> GetUserSessionsCompletionInfoAsync(string userId);
        Task<double> GetSessionCompletionPercentageAsync(string userId, int sessionId);
    }

    public class SessionCompletionInfo
    {
        public int SessionId { get; set; }
        public bool IsCompleted { get; set; }
        public double CompletionPercentage { get; set; }
        public int CompletedCoursesCount { get; set; }
        public int TotalCoursesCount { get; set; }
        public DateTime? CompletionDate { get; set; }
        public bool HasCertificate { get; set; }
        public string CompletionStatus => GetCompletionStatus();

        private string GetCompletionStatus()
        {
            if (IsCompleted) return "Terminée";
            if (CompletionPercentage > 0) return "En cours";
            return "Non commencée";
        }
    }

    public class SessionCompletionService : ISessionCompletionService
    {
        private readonly IDbContextFactory<ApplicationDbContext> _contextFactory;
        private readonly ILogger<SessionCompletionService> _logger;

        public SessionCompletionService(
            IDbContextFactory<ApplicationDbContext> contextFactory,
            ILogger<SessionCompletionService> logger)
        {
            _contextFactory = contextFactory;
            _logger = logger;
        }

        public async Task<bool> IsSessionCompletedByUserAsync(string userId, int sessionId)
        {
            var info = await GetSessionCompletionInfoAsync(userId, sessionId);
            return info.IsCompleted;
        }

        public async Task<SessionCompletionInfo> GetSessionCompletionInfoAsync(string userId, int sessionId)
        {
            try
            {
                using var context = await _contextFactory.CreateDbContextAsync();

                // Récupérer tous les cours publiés de la session
                var sessionCourses = await context.Courses
                    .Where(c => c.SessionId == sessionId && c.IsPublished)
                    .Select(c => c.Id)
                    .ToListAsync();

                if (!sessionCourses.Any())
                {
                    return new SessionCompletionInfo
                    {
                        SessionId = sessionId,
                        IsCompleted = false,
                        CompletionPercentage = 0,
                        CompletedCoursesCount = 0,
                        TotalCoursesCount = 0
                    };
                }

                // Récupérer les progrès des cours
                var courseProgresses = await context.CourseProgresses
                    .Where(cp => cp.UserId == userId && sessionCourses.Contains(cp.CoursId))
                    .ToListAsync();

                var completedCourses = courseProgresses.Where(cp => cp.IsCompleted).ToList();
                var completedCount = completedCourses.Count;
                var totalCount = sessionCourses.Count;

                // Calculer la date de completion (dernière date de completion d'un cours)
                DateTime? completionDate = null;
                if (completedCount == totalCount && completedCourses.Any())
                {
                    completionDate = completedCourses.Max(cp => cp.LastAccessed);
                }

                // Vérifier s'il y a un certificat
                var hasCertificate = await context.Certificates
                    .AnyAsync(c => c.UserId == userId && c.SessionId == sessionId);

                return new SessionCompletionInfo
                {
                    SessionId = sessionId,
                    IsCompleted = completedCount == totalCount && totalCount > 0,
                    CompletionPercentage = totalCount > 0 ? (double)completedCount / totalCount * 100 : 0,
                    CompletedCoursesCount = completedCount,
                    TotalCoursesCount = totalCount,
                    CompletionDate = completionDate,
                    HasCertificate = hasCertificate
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur lors du calcul de completion pour la session {SessionId} et l'utilisateur {UserId}", sessionId, userId);
                return new SessionCompletionInfo
                {
                    SessionId = sessionId,
                    IsCompleted = false,
                    CompletionPercentage = 0,
                    CompletedCoursesCount = 0,
                    TotalCoursesCount = 0
                };
            }
        }

        public async Task<Dictionary<int, SessionCompletionInfo>> GetUserSessionsCompletionInfoAsync(string userId)
        {
            try
            {
                using var context = await _contextFactory.CreateDbContextAsync();

                // Récupérer toutes les sessions de l'utilisateur
                var userSessionIds = await context.Users
                    .Where(u => u.Id == userId)
                    .SelectMany(u => u.EnrolledSessions)
                    .Select(s => s.Id)
                    .ToListAsync();

                var completionInfos = new Dictionary<int, SessionCompletionInfo>();

                foreach (var sessionId in userSessionIds)
                {
                    var info = await GetSessionCompletionInfoAsync(userId, sessionId);
                    completionInfos[sessionId] = info;
                }

                return completionInfos;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur lors du calcul de completion des sessions pour l'utilisateur {UserId}", userId);
                return new Dictionary<int, SessionCompletionInfo>();
            }
        }

        public async Task<double> GetSessionCompletionPercentageAsync(string userId, int sessionId)
        {
            var info = await GetSessionCompletionInfoAsync(userId, sessionId);
            return info.CompletionPercentage;
        }
    }
}