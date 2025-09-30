using Microsoft.EntityFrameworkCore;
using Mooc.Data;
using Mooc.Services;
using Microsoft.AspNetCore.Components.Authorization;
using System.Security.Claims;

namespace Mooc.Services
{
    public interface ICourseBadgeService
    {
        Task<CourseBadge?> EvaluateAndAwardBadgeAsync(string userId, int coursId);
        Task<List<CourseBadge>> GetUserCourseBadgesAsync(string userId);
        Task<CourseBadge?> GetCourseBadgeAsync(string userId, int coursId);
        Task<bool> HasCourseBadgeAsync(string userId, int coursId);
        Task<List<CourseBadge>> GetCourseBadgesForSessionAsync(string userId, int sessionId);
    }

    public class CourseBadgeService : ICourseBadgeService
    {
        private readonly IDbContextFactory<ApplicationDbContext> _contextFactory;
        private readonly AuthenticationStateProvider _authenticationStateProvider;
        private readonly ILogger<CourseBadgeService> _logger;

        // Seuils pour les badges
        private const double BRONZE_THRESHOLD = 70.0;
        private const double SILVER_THRESHOLD = 80.0;
        private const double GOLD_THRESHOLD = 90.0;
        private const double PERFECT_THRESHOLD = 100.0;

        public CourseBadgeService(
            IDbContextFactory<ApplicationDbContext> contextFactory,
            AuthenticationStateProvider authenticationStateProvider,
            ILogger<CourseBadgeService> logger)
        {
            _contextFactory = contextFactory;
            _authenticationStateProvider = authenticationStateProvider;
            _logger = logger;
        }

        /// <summary>
        /// Calcule le score d'un cours directement depuis la base de données
        /// </summary>
        private async Task<CourseScoreResult> CalculateCourseScoreFromDatabaseAsync(string userId, int coursId)
        {
            try
            {
                using var context = await _contextFactory.CreateDbContextAsync();

                var progress = await context.CourseProgresses
                    .AsNoTracking()
                    .FirstOrDefaultAsync(cp => cp.CoursId == coursId && cp.UserId == userId);

                if (progress == null || string.IsNullOrEmpty(progress.BlockInteractions))
                {
                    _logger.LogWarning("Aucune progression trouvée pour l'utilisateur {UserId} et le cours {CoursId}", userId, coursId);
                    return new CourseScoreResult
                    {
                        TotalEarnedPoints = 0,
                        TotalPossiblePoints = 0,
                        QuizCount = 0,
                        CorrectAnswers = 0,
                        ScorePercentage = 0
                    };
                }

                var interactions = System.Text.Json.JsonSerializer.Deserialize<Dictionary<int, string>>(progress.BlockInteractions)
                                   ?? new Dictionary<int, string>();

                var quizResults = new List<QuizScoreResult>();

                foreach (var interaction in interactions.Values)
                {
                    try
                    {
                        using var document = System.Text.Json.JsonDocument.Parse(interaction);
                        var root = document.RootElement;

                        // Vérifier si c'est une interaction de quiz avec scoreResult
                        if (root.TryGetProperty("type", out var typeElement) &&
                            typeElement.GetString() == "quiz" &&
                            root.TryGetProperty("scoreResult", out var scoreElement))
                        {
                            var difficulty = QuizDifficulty.Débutant;
                            if (scoreElement.TryGetProperty("difficulty", out var difficultyElement))
                            {
                                Enum.TryParse<QuizDifficulty>(difficultyElement.GetString(), out difficulty);
                            }

                            var isCorrect = false;
                            if (root.TryGetProperty("correct", out var correctElement))
                            {
                                isCorrect = correctElement.GetBoolean();
                            }

                            var basePoints = 0;
                            if (scoreElement.TryGetProperty("basePoints", out var basePointsElement))
                            {
                                basePoints = basePointsElement.GetInt32();
                            }

                            var finalScore = 0;
                            if (scoreElement.TryGetProperty("finalScore", out var finalScoreElement))
                            {
                                finalScore = finalScoreElement.GetInt32();
                            }

                            var performanceLevel = QuizPerformanceLevel.Average;
                            if (scoreElement.TryGetProperty("performanceLevel", out var perfElement))
                            {
                                Enum.TryParse<QuizPerformanceLevel>(perfElement.GetString(), out performanceLevel);
                            }

                            var performanceMultiplier = 1.0;
                            if (scoreElement.TryGetProperty("performanceMultiplier", out var multiplierElement))
                            {
                                performanceMultiplier = multiplierElement.GetDouble();
                            }

                            var timeSpent = TimeSpan.Zero;
                            if (scoreElement.TryGetProperty("timeSpentSeconds", out var timeElement))
                            {
                                timeSpent = TimeSpan.FromSeconds(timeElement.GetDouble());
                            }

                            var hintsUsed = 0;
                            if (scoreElement.TryGetProperty("hintsUsed", out var hintsElement))
                            {
                                hintsUsed = hintsElement.GetInt32();
                            }

                            var attempts = 1;
                            if (scoreElement.TryGetProperty("attempts", out var attemptsElement))
                            {
                                attempts = attemptsElement.GetInt32();
                            }

                            quizResults.Add(new QuizScoreResult
                            {
                                Difficulty = difficulty,
                                IsCorrect = isCorrect,
                                BasePoints = basePoints,
                                FinalScore = finalScore,
                                PerformanceLevel = performanceLevel,
                                PerformanceMultiplier = performanceMultiplier,
                                TimeSpent = timeSpent,
                                HintsUsed = hintsUsed,
                                Attempts = attempts
                            });
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Erreur lors du parsing d'une interaction pour le cours {CoursId}", coursId);
                    }
                }

                return QuizScoring.CalculateCourseScore(quizResults);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur lors du calcul du score depuis la base de données pour le cours {CoursId}", coursId);
                return new CourseScoreResult
                {
                    TotalEarnedPoints = 0,
                    TotalPossiblePoints = 0,
                    QuizCount = 0,
                    CorrectAnswers = 0,
                    ScorePercentage = 0
                };
            }
        }

        /// <summary>
        /// Évalue les performances d'un utilisateur et attribue un badge si éligible
        /// </summary>
        public async Task<CourseBadge?> EvaluateAndAwardBadgeAsync(string userId, int coursId)
        {
            try
            {
                _logger.LogInformation("🎯 Évaluation pour attribution de badge - UserId: {UserId}, CoursId: {CoursId}", userId, coursId);

                // Vérifier si un badge existe déjà
                if (await HasCourseBadgeAsync(userId, coursId))
                {
                    _logger.LogInformation("ℹ️ Badge déjà attribué pour ce cours");
                    return await GetCourseBadgeAsync(userId, coursId);
                }

                // Calculer le score du cours directement depuis la BD
                var courseScore = await CalculateCourseScoreFromDatabaseAsync(userId, coursId);

                if (courseScore.QuizCount == 0 || courseScore.TotalPossiblePoints == 0)
                {
                    _logger.LogWarning("⚠️ Aucun quiz trouvé ou points possibles = 0 pour le cours {CoursId}", coursId);
                    return null;
                }

                // Vérifier si le score atteint le seuil minimum (70%)
                if (courseScore.ScorePercentage < BRONZE_THRESHOLD)
                {
                    _logger.LogInformation("📊 Score insuffisant ({Score}%) pour obtenir un badge (minimum {Threshold}%)",
                        courseScore.ScorePercentage.ToString("F1"), BRONZE_THRESHOLD);
                    return null;
                }

                // Déterminer le type de badge
                var badgeType = DetermineBadgeType(courseScore);

                // Créer le badge
                var badge = await CreateCourseBadgeAsync(userId, coursId, badgeType, courseScore);

                _logger.LogInformation("🏆 Badge {BadgeType} attribué ! Score: {Score}% ({Points}/{MaxPoints} pts)",
                    badgeType, courseScore.ScorePercentage.ToString("F1"), courseScore.TotalEarnedPoints, courseScore.TotalPossiblePoints);

                return badge;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Erreur lors de l'évaluation pour attribution de badge - UserId: {UserId}, CoursId: {CoursId}", userId, coursId);
                return null;
            }
        }

        /// <summary>
        /// Détermine le type de badge basé sur le score
        /// </summary>
        private CourseBadgeType DetermineBadgeType(CourseScoreResult courseScore)
        {
            // Vérifier d'abord le badge Perfect (100% sans aide)
            if (courseScore.ScorePercentage >= PERFECT_THRESHOLD)
            {
                // Vérifier si tous les quiz ont été réussis sans aide
                if (IsPerformancePerfect(courseScore))
                {
                    return CourseBadgeType.Perfect;
                }
                return CourseBadgeType.Gold;
            }

            // Badges par plage de score
            return courseScore.ScorePercentage switch
            {
                >= GOLD_THRESHOLD => CourseBadgeType.Gold,
                >= SILVER_THRESHOLD => CourseBadgeType.Silver,
                >= BRONZE_THRESHOLD => CourseBadgeType.Bronze,
                _ => CourseBadgeType.Bronze // Par sécurité, bien que ce cas ne devrait pas arriver
            };
        }

        /// <summary>
        /// Vérifie si la performance est parfaite (100% sans aide)
        /// </summary>
        private bool IsPerformancePerfect(CourseScoreResult courseScore)
        {
            // Logique pour vérifier si aucune aide n'a été utilisée
            // Pour l'instant, on considère qu'une performance parfaite = 100% de score
            return courseScore.ScorePercentage >= PERFECT_THRESHOLD &&
                   courseScore.CorrectAnswers == courseScore.QuizCount;
        }

        /// <summary>
        /// Crée et sauvegarde un nouveau badge de cours
        /// </summary>
        private async Task<CourseBadge> CreateCourseBadgeAsync(
            string userId,
            int coursId,
            CourseBadgeType badgeType,
            CourseScoreResult courseScore)
        {
            using var context = await _contextFactory.CreateDbContextAsync();

            // Récupérer les informations du cours
            var cours = await context.Courses.FindAsync(coursId);

            var badge = new CourseBadge
            {
                UserId = userId,
                CoursId = coursId,
                BadgeType = badgeType,
                ScorePercentage = courseScore.ScorePercentage,
                PointsEarned = courseScore.TotalEarnedPoints,
                TotalPointsPossible = courseScore.TotalPossiblePoints,
                CorrectAnswers = courseScore.CorrectAnswers,
                TotalQuestions = courseScore.QuizCount,
                EarnedDate = DateTime.UtcNow,
                CustomTitle = GetBadgeTitle(badgeType, cours?.Title),
                Description = GetBadgeDescription(badgeType, courseScore)
            };

            context.CourseBadges.Add(badge);
            await context.SaveChangesAsync();

            _logger.LogInformation("✅ Badge créé avec succès - ID: {BadgeId}", badge.Id);

            return badge;
        }

        /// <summary>
        /// Génère le titre du badge
        /// </summary>
        private string GetBadgeTitle(CourseBadgeType badgeType, string? coursTitle)
        {
            var badgeName = badgeType switch
            {
                CourseBadgeType.Bronze => "Badge Bronze",
                CourseBadgeType.Silver => "Badge Argent",
                CourseBadgeType.Gold => "Badge Or",
                CourseBadgeType.Perfect => "Badge Perfectionniste",
                _ => "Badge de Réussite"
            };

            return $"{badgeName} - {coursTitle ?? "Cours"}";
        }

        /// <summary>
        /// Génère la description du badge
        /// </summary>
        private string GetBadgeDescription(CourseBadgeType badgeType, CourseScoreResult courseScore)
        {
            return badgeType switch
            {
                CourseBadgeType.Bronze => $"Félicitations ! Vous avez obtenu {courseScore.ScorePercentage:F1}% de réussite dans ce cours.",
                CourseBadgeType.Silver => $"Excellent travail ! Vous avez obtenu {courseScore.ScorePercentage:F1}% de réussite dans ce cours.",
                CourseBadgeType.Gold => $"Performance remarquable ! Vous avez obtenu {courseScore.ScorePercentage:F1}% de réussite dans ce cours.",
                CourseBadgeType.Perfect => $"Performance parfaite ! Vous avez obtenu {courseScore.ScorePercentage:F1}% sans aucune aide.",
                _ => $"Cours terminé avec {courseScore.ScorePercentage:F1}% de réussite."
            };
        }

        /// <summary>
        /// Récupère tous les badges d'un utilisateur
        /// </summary>
        public async Task<List<CourseBadge>> GetUserCourseBadgesAsync(string userId)
        {
            using var context = await _contextFactory.CreateDbContextAsync();

            return await context.CourseBadges
                .Include(cb => cb.Cours)
                .ThenInclude(c => c.Session)
                .Where(cb => cb.UserId == userId)
                .OrderByDescending(cb => cb.EarnedDate)
                .ToListAsync();
        }

        /// <summary>
        /// Récupère le badge d'un cours spécifique pour un utilisateur
        /// </summary>
        public async Task<CourseBadge?> GetCourseBadgeAsync(string userId, int coursId)
        {
            using var context = await _contextFactory.CreateDbContextAsync();

            return await context.CourseBadges
                .Include(cb => cb.Cours)
                .ThenInclude(c => c.Session)
                .FirstOrDefaultAsync(cb => cb.UserId == userId && cb.CoursId == coursId);
        }

        /// <summary>
        /// Vérifie si un utilisateur a déjà un badge pour un cours
        /// </summary>
        public async Task<bool> HasCourseBadgeAsync(string userId, int coursId)
        {
            using var context = await _contextFactory.CreateDbContextAsync();

            return await context.CourseBadges
                .AnyAsync(cb => cb.UserId == userId && cb.CoursId == coursId);
        }

        /// <summary>
        /// Récupère tous les badges d'une session pour un utilisateur
        /// </summary>
        public async Task<List<CourseBadge>> GetCourseBadgesForSessionAsync(string userId, int sessionId)
        {
            using var context = await _contextFactory.CreateDbContextAsync();

            return await context.CourseBadges
                .Include(cb => cb.Cours)
                .ThenInclude(c => c.Session)
                .Where(cb => cb.UserId == userId && cb.Cours.SessionId == sessionId)
                .OrderByDescending(cb => cb.EarnedDate)
                .ToListAsync();
        }
    }
}