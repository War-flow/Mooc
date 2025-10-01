using Microsoft.EntityFrameworkCore;
using Mooc.Data;

namespace Mooc.Services
{
    public interface IScoreDisplayService
    {
        Task<UserScoreOverview> GetUserScoreOverviewAsync(string userId);
        Task<SessionDisplayScore> GetSessionDisplayScoreAsync(int sessionId, string userId);
        Task<List<CourseDisplayScore>> GetCoursesDisplayScoresAsync(List<int> courseIds, string userId);
    }

    public class ScoreDisplayService : IScoreDisplayService
    {
        private readonly IScoresCacheService _cacheService;
        private readonly CourseStateService _courseStateService;
        private readonly IDbContextFactory<ApplicationDbContext> _contextFactory;

        public ScoreDisplayService(
            IScoresCacheService cacheService, 
            CourseStateService courseStateService,
            IDbContextFactory<ApplicationDbContext> contextFactory)
        {
            _cacheService = cacheService;
            _courseStateService = courseStateService;
            _contextFactory = contextFactory;
        }

        public async Task<UserScoreOverview> GetUserScoreOverviewAsync(string userId)
        {
            try
            {
                using var context = await _contextFactory.CreateDbContextAsync();
                
                // Récupérer toutes les sessions de l'utilisateur
                var userSessions = await context.Users
                    .Where(u => u.Id == userId)
                    .SelectMany(u => u.EnrolledSessions)
                    .Include(s => s.Courses)
                    .ToListAsync();

                var overview = new UserScoreOverview
                {
                    UserId = userId,
                    SessionScores = new List<SessionDisplayScore>()
                };

                foreach (var session in userSessions)
                {
                    if (session.Courses != null && session.Courses.Any())
                    {
                        var sessionScore = await GetSessionDisplayScoreAsync(session.Id, userId);
                        overview.SessionScores.Add(sessionScore);
                    }
                }

                // Calculs globaux
                overview.TotalEarnedPoints = overview.SessionScores.Sum(s => s.TotalEarnedPoints);
                overview.TotalPossiblePoints = overview.SessionScores.Sum(s => s.TotalPossiblePoints);
                overview.OverallPercentage = overview.TotalPossiblePoints > 0 
                    ? (double)overview.TotalEarnedPoints / overview.TotalPossiblePoints * 100 
                    : 0;

                return overview;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Erreur lors du calcul de l'aperçu utilisateur: {ex.Message}");
                return new UserScoreOverview { UserId = userId };
            }
        }

        public async Task<SessionDisplayScore> GetSessionDisplayScoreAsync(int sessionId, string userId)
        {
            try
            {
                Console.WriteLine($"🔍 DEBUT GetSessionDisplayScoreAsync - Session {sessionId}, User {userId}");
                
                using var context = await _contextFactory.CreateDbContextAsync();
                
                var session = await context.Sessions
                    .Include(s => s.Courses)
                    .FirstOrDefaultAsync(s => s.Id == sessionId);

                if (session?.Courses == null)
                {
                    return new SessionDisplayScore { SessionId = sessionId, SessionTitle = "Session inconnue" };
                }

                var courseIds = session.Courses.Select(c => c.Id).ToList();
                Console.WriteLine($"🔍 Cours dans la session: [{string.Join(", ", courseIds)}]");
                
                // **UTILISONS LE CACHE SERVICE DIRECT**
                var courseScores = await _cacheService.GetMultipleCourseScoresAsync(courseIds, userId);
                
                Console.WriteLine($"🔍 Scores récupérés du cache:");
                foreach (var kvp in courseScores)
                {
                    Console.WriteLine($"  - Cours {kvp.Key}: {kvp.Value.TotalEarnedPoints}/{kvp.Value.TotalPossiblePoints} pts");
                }

                var displayScore = new SessionDisplayScore
                {
                    SessionId = sessionId,
                    SessionTitle = session.Title,
                    CourseScores = courseScores.Select(kvp => new CourseDisplayScore
                    {
                        CourseId = kvp.Key,
                        CourseTitle = session.Courses.FirstOrDefault(c => c.Id == kvp.Key)?.Title ?? "",
                        EarnedPoints = kvp.Value.TotalEarnedPoints,
                        PossiblePoints = kvp.Value.TotalPossiblePoints, // **TRACE ICI**
                        ScorePercentage = kvp.Value.ScorePercentage,     // **TRACE ICI**
                        QuizCount = kvp.Value.QuizCount,                 // **TRACE ICI**
                        CorrectAnswers = kvp.Value.CorrectAnswers,
                        PerformanceLevel = kvp.Value.OverallLevel.ToString()
                    }).ToList()
                };

                // Totaux de session avec les vrais totaux
                displayScore.TotalEarnedPoints = displayScore.CourseScores.Sum(c => c.EarnedPoints);
                displayScore.TotalPossiblePoints = displayScore.CourseScores.Sum(c => c.PossiblePoints); // **TRACE ICI**
                displayScore.ScorePercentage = displayScore.TotalPossiblePoints > 0
                    ? (double)displayScore.TotalEarnedPoints / displayScore.TotalPossiblePoints * 100
                    : 0;

                Console.WriteLine($"🔍 FINAL SESSION RESULT:");
                Console.WriteLine($"  - Total Earned: {displayScore.TotalEarnedPoints}");
                Console.WriteLine($"  - Total Possible: {displayScore.TotalPossiblePoints}");
                Console.WriteLine($"  - Percentage: {displayScore.ScorePercentage:F1}%");

                return displayScore;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Erreur lors du calcul du score de session {sessionId}: {ex.Message}");
                return new SessionDisplayScore { SessionId = sessionId, SessionTitle = "Erreur de chargement" };
            }
        }

        public async Task<List<CourseDisplayScore>> GetCoursesDisplayScoresAsync(List<int> courseIds, string userId)
        {
            try
            {
                Console.WriteLine($"🔍 DEBUT GetCoursesDisplayScoresAsync - Cours [{string.Join(", ", courseIds)}], User {userId}");
                
                // **UTILISONS LE CACHE SERVICE DIRECT**
                var courseScores = await _cacheService.GetMultipleCourseScoresAsync(courseIds, userId);
                
                using var context = await _contextFactory.CreateDbContextAsync();
                var courses = await context.Courses
                    .Where(c => courseIds.Contains(c.Id))
                    .ToDictionaryAsync(c => c.Id, c => c.Title);

                var results = courseScores.Select(kvp => new CourseDisplayScore
                {
                    CourseId = kvp.Key,
                    CourseTitle = courses.GetValueOrDefault(kvp.Key, "Cours inconnu"),
                    EarnedPoints = kvp.Value.TotalEarnedPoints,
                    PossiblePoints = kvp.Value.TotalPossiblePoints, // **TRACE ICI**
                    ScorePercentage = kvp.Value.ScorePercentage,     // **TRACE ICI**
                    QuizCount = kvp.Value.QuizCount,                 // **TRACE ICI**
                    CorrectAnswers = kvp.Value.CorrectAnswers,
                    PerformanceLevel = kvp.Value.OverallLevel.ToString()
                }).ToList();

                Console.WriteLine($"🔍 FINAL COURSES RESULT:");
                foreach (var result in results)
                {
                    Console.WriteLine($"  - Cours {result.CourseId}: {result.EarnedPoints}/{result.PossiblePoints} pts");
                }

                return results;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Erreur lors du calcul des scores de cours: {ex.Message}");
                return courseIds.Select(id => new CourseDisplayScore { CourseId = id }).ToList();
            }
        }
    }

    // Classes pour l'affichage optimisé
    public class UserScoreOverview
    {
        public string UserId { get; set; } = string.Empty;
        public List<SessionDisplayScore> SessionScores { get; set; } = new();
        public int TotalEarnedPoints { get; set; }
        public int TotalPossiblePoints { get; set; }
        public double OverallPercentage { get; set; }
    }

    public class SessionDisplayScore
    {
        public int SessionId { get; set; }
        public string SessionTitle { get; set; } = string.Empty;
        public List<CourseDisplayScore> CourseScores { get; set; } = new();
        public int TotalEarnedPoints { get; set; }
        public int TotalPossiblePoints { get; set; }
        public double ScorePercentage { get; set; }
    }

    public class CourseDisplayScore
    {
        public int CourseId { get; set; }
        public string CourseTitle { get; set; } = string.Empty;
        public int EarnedPoints { get; set; }
        public int PossiblePoints { get; set; }
        public double ScorePercentage { get; set; }
        public int QuizCount { get; set; }
        public int CorrectAnswers { get; set; }
        public string PerformanceLevel { get; set; } = string.Empty;
    }
}