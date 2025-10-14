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
        
        // ✅ NOUVELLE MÉTHODE : Vérification et création automatique des badges manquants
        Task<List<CourseBadge>> CheckAndCreateMissingBadgesAsync(string userId);
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
        /// Adapté pour le nouveau système de questionnaire unique
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

                // ✅ CORRECTION : Les clés sont des strings, pas des int
                var interactions = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(progress.BlockInteractions)
                           ?? new Dictionary<string, string>();

                var quizResults = new List<QuizScoreResult>();

                // ✅ CORRECTION : Filtrer uniquement les interactions de questionnaire
                var questionnaireInteractions = interactions
                    .Where(kvp => kvp.Key.Contains("_q")) // Clés au format "blockIndex_qQuestionIndex"
                    .ToList();

                _logger.LogInformation("📝 {InteractionCount} interactions de questionnaire trouvées", questionnaireInteractions.Count);

                // ✅ NOUVEAU : Recherche des interactions de type "questionnaire"
                foreach (var interaction in questionnaireInteractions)
                {
                    try
                    {
                        using var document = System.Text.Json.JsonDocument.Parse(interaction.Value);
                        var root = document.RootElement;

                        // Vérifier si c'est une interaction de questionnaire avec scoreResult
                        if (root.TryGetProperty("type", out var typeElement) &&
                            typeElement.GetString() == "questionnaire" &&
                            root.TryGetProperty("scoreResult", out var scoreElement))
                        {
                            var isCorrect = false;
                            if (root.TryGetProperty("correct", out var correctElement))
                            {
                                isCorrect = correctElement.GetBoolean();
                            }

                            var finalScore = 0;
                            if (scoreElement.TryGetProperty("finalScore", out var finalScoreElement))
                            {
                                finalScore = finalScoreElement.GetInt32();
                            }

                            quizResults.Add(new QuizScoreResult
                            {
                                IsCorrect = isCorrect,
                                FinalScore = finalScore
                            });

                            _logger.LogInformation("📝 Question {Key} trouvée: Correct={IsCorrect}, Score={Score}", 
                                interaction.Key, isCorrect, finalScore);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Erreur lors du parsing d'une interaction pour la clé {Key} du cours {CoursId}", 
                            interaction.Key, coursId);
                    }
                }

                // ✅ NOUVEAU : Utiliser la méthode simplifiée de calcul
                var courseScoreResult = QuizScoring.CalculateCourseScore(quizResults);

                // Récupérer le nombre total de questions du questionnaire
                var (totalQuestions, totalPossiblePoints) = await GetQuestionnaireInfoAsync(coursId);
                
                // Mettre à jour avec les vraies valeurs totales
                courseScoreResult.TotalPossiblePoints = totalPossiblePoints;
                courseScoreResult.QuizCount = totalQuestions;
                
                // Recalculer le pourcentage avec les vraies valeurs
                courseScoreResult.ScorePercentage = totalQuestions > 0 
                    ? (double)courseScoreResult.TotalEarnedPoints / courseScoreResult.TotalPossiblePoints * 100 
                    : 0;

                _logger.LogInformation("🎯 Score final cours {CourseId}: {EarnedPoints}/{TotalPoints} pts ({Percentage:F1}%)", 
                    coursId, courseScoreResult.TotalEarnedPoints, totalPossiblePoints, courseScoreResult.ScorePercentage);

                return courseScoreResult;
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
        /// ✅ CORRIGÉ : Compte le nombre de questions dans le bloc questionnaire unique d'un cours
        /// Version synchronisée avec CourseStateService
        /// </summary>
        private async Task<(int totalQuestions, int totalPossiblePoints)> GetQuestionnaireInfoAsync(int coursId)
        {
            try
            {
                using var context = await _contextFactory.CreateDbContextAsync();

                var cours = await context.Courses
                    .AsNoTracking()
                    .FirstOrDefaultAsync(c => c.Id == coursId);

                if (cours == null || string.IsNullOrEmpty(cours.Content))
                {
                    _logger.LogInformation("📊 Cours {CourseId}: Aucun contenu trouvé", coursId);
                    return (0, 0);
                }

                var blocks = System.Text.Json.JsonSerializer.Deserialize<List<System.Text.Json.JsonElement>>(cours.Content);
                if (blocks == null)
                {
                    _logger.LogWarning("📊 Cours {CourseId}: Impossible de désérialiser le contenu", coursId);
                    return (0, 0);
                }

                // Chercher le bloc questionnaire unique
                foreach (var block in blocks)
                {
                    if (block.TryGetProperty("Type", out var typeProperty))
                    {
                        var blockType = typeProperty.GetString()?.ToLowerInvariant();
                        _logger.LogInformation("📊 Cours {CourseId}: Bloc trouvé avec type '{BlockType}'", coursId, blockType);

                        if (blockType == "questionnaire" || blockType == "quiz" || blockType == "questions")
                        {
                            System.Text.Json.JsonElement questionsProperty = default;
                            bool foundQuestions = false;

                            // **CORRECTION PRINCIPALE** : Les questions sont dans la propriété "Content" (JSON encodé)
                            if (block.TryGetProperty("Content", out var contentProperty) && 
                                contentProperty.ValueKind == System.Text.Json.JsonValueKind.String)
                            {
                                try
                                {
                                    var contentJson = contentProperty.GetString();
                                    if (!string.IsNullOrEmpty(contentJson))
                                    {
                                        _logger.LogInformation("📊 Cours {CourseId}: Désérialisation de la propriété Content", coursId);
                                        
                                        // Désérialiser le JSON imbriqué
                                        var contentObject = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(contentJson);
                                        
                                        // Chercher les questions dans le contenu désérialisé
                                        if (contentObject.TryGetProperty("Questions", out questionsProperty) && 
                                            questionsProperty.ValueKind == System.Text.Json.JsonValueKind.Array)
                                        {
                                            foundQuestions = true;
                                            _logger.LogInformation("📊 Cours {CourseId}: ✅ Questions trouvées dans Content.Questions", coursId);
                                        }
                                        else if (contentObject.TryGetProperty("questions", out questionsProperty) && 
                                                 questionsProperty.ValueKind == System.Text.Json.JsonValueKind.Array)
                                        {
                                            foundQuestions = true;
                                            _logger.LogInformation("📊 Cours {CourseId}: ✅ Questions trouvées dans Content.questions", coursId);
                                        }
                                    }
                                }
                                catch (System.Text.Json.JsonException ex)
                                {
                                    _logger.LogError(ex, "❌ Erreur lors de la désérialisation de Content pour le cours {CourseId}", coursId);
                                }
                            }
                            
                            // **FALLBACK** : Essayer les autres méthodes (pour rétrocompatibilité)
                            if (!foundQuestions)
                            {
                                // Chercher directement dans le bloc
                                if (block.TryGetProperty("Questions", out questionsProperty) && 
                                    questionsProperty.ValueKind == System.Text.Json.JsonValueKind.Array)
                                {
                                    foundQuestions = true;
                                    _logger.LogInformation("📊 Cours {CourseId}: Questions trouvées directement dans le bloc", coursId);
                                }
                                else if (block.TryGetProperty("questions", out questionsProperty) && 
                                         questionsProperty.ValueKind == System.Text.Json.JsonValueKind.Array)
                                {
                                    foundQuestions = true;
                                    _logger.LogInformation("📊 Cours {CourseId}: Questions trouvées (minuscules) directement dans le bloc", coursId);
                                }
                                // Chercher dans "Data"
                                else if (block.TryGetProperty("Data", out var dataProperty))
                                {
                                    _logger.LogInformation("📊 Cours {CourseId}: Recherche dans la propriété 'Data'", coursId);
                                    
                                    if (dataProperty.TryGetProperty("Questions", out questionsProperty) && 
                                        questionsProperty.ValueKind == System.Text.Json.JsonValueKind.Array)
                                    {
                                        foundQuestions = true;
                                        _logger.LogInformation("📊 Cours {CourseId}: Questions trouvées dans Data.Questions", coursId);
                                    }
                                    else if (dataProperty.TryGetProperty("questions", out questionsProperty) && 
                                             questionsProperty.ValueKind == System.Text.Json.JsonValueKind.Array)
                                    {
                                        foundQuestions = true;
                                        _logger.LogInformation("📊 Cours {CourseId}: Questions trouvées dans Data.questions", coursId);
                                    }
                                }
                            }

                            if (foundQuestions)
                            {
                                int questionCount = questionsProperty.GetArrayLength();
                                int totalPoints = questionCount * QuizScoring.PointsPerQuiz; // 1 point par question
                                
                                _logger.LogInformation("📊 Cours {CourseId}: ✅ {QuestionCount} questions trouvées = {TotalPoints} points possibles", 
                                    coursId, questionCount, totalPoints);
                                
                                return (questionCount, totalPoints);
                            }
                            else
                            {
                                _logger.LogWarning("📊 Cours {CourseId}: ❌ Bloc questionnaire trouvé mais aucune propriété de questions détectée", coursId);
                            }
                        }
                    }
                }

                _logger.LogInformation("📊 Cours {CourseId}: Aucun bloc questionnaire trouvé", coursId);
                return (0, 0);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Erreur lors de l'analyse du questionnaire pour le cours {CourseId}", coursId);
                return (0, 0);
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
                    _logger.LogWarning("⚠️ Aucune question trouvée ou points possibles = 0 pour le cours {CoursId}", coursId);
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
        /// ✅ Simplifié pour le nouveau système
        /// </summary>
        private CourseBadgeType DetermineBadgeType(CourseScoreResult courseScore)
        {
            // Vérifier d'abord le badge Perfect (100%)
            if (courseScore.ScorePercentage >= PERFECT_THRESHOLD)
            {
                // Dans le nouveau système simplifié, Perfect = 100% de réussite
                if (courseScore.CorrectAnswers == courseScore.QuizCount)
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
                _ => CourseBadgeType.Bronze // Par sécurité
            };
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
                CourseBadgeType.Bronze => $"Félicitations ! Vous avez obtenu {courseScore.ScorePercentage:F1}% de réussite au questionnaire.",
                CourseBadgeType.Silver => $"Excellent travail ! Vous avez obtenu {courseScore.ScorePercentage:F1}% de réussite au questionnaire.",
                CourseBadgeType.Gold => $"Performance remarquable ! Vous avez obtenu {courseScore.ScorePercentage:F1}% de réussite au questionnaire.",
                CourseBadgeType.Perfect => $"Performance parfaite ! Vous avez répondu correctement à toutes les questions du questionnaire.",
                _ => $"Questionnaire terminé avec {courseScore.ScorePercentage:F1}% de réussite."
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

        /// <summary>
        /// Vérifie et crée automatiquement les badges manquants pour un utilisateur
        /// </summary>
        public async Task<List<CourseBadge>> CheckAndCreateMissingBadgesAsync(string userId)
        {
            var createdBadges = new List<CourseBadge>();

            try
            {
                _logger.LogInformation("🔍 [BADGE-CHECK] Début vérification badges manquants - UserId: {UserId}", userId);

                using var context = await _contextFactory.CreateDbContextAsync();

                // 1. Récupérer tous les cours complétés par l'utilisateur
                var completedCourses = await context.CourseProgresses
                    .AsNoTracking()
                    .Include(cp => cp.Cours)
                    .Where(cp => cp.UserId == userId && cp.IsCompleted)
                    .Select(cp => new { cp.CoursId, cp.Cours.Title })
                    .ToListAsync();

                _logger.LogInformation("📊 [BADGE-CHECK] {Count} cours complétés trouvés", completedCourses.Count);

                if (!completedCourses.Any())
                {
                    _logger.LogInformation("ℹ️ [BADGE-CHECK] Aucun cours complété pour cet utilisateur");
                    return createdBadges;
                }

                // 2. Récupérer tous les badges existants pour cet utilisateur
                var existingBadges = await context.CourseBadges
                    .AsNoTracking()
                    .Where(cb => cb.UserId == userId)
                    .Select(cb => cb.CoursId)
                    .ToHashSetAsync();

                _logger.LogInformation("🏆 [BADGE-CHECK] {Count} badges déjà existants", existingBadges.Count);

                // 3. Identifier les cours complétés sans badge
                var coursesWithoutBadge = completedCourses
                    .Where(c => !existingBadges.Contains(c.CoursId))
                    .ToList();

                if (!coursesWithoutBadge.Any())
                {
                    _logger.LogInformation("✅ [BADGE-CHECK] Tous les badges sont déjà créés");
                    return createdBadges;
                }

                _logger.LogInformation("⚠️ [BADGE-CHECK] {Count} cours sans badge détectés", coursesWithoutBadge.Count);

                // 4. Créer les badges manquants
                foreach (var course in coursesWithoutBadge)
                {
                    try
                    {
                        _logger.LogInformation("🎯 [BADGE-CHECK] Évaluation cours {CoursId} - {Title}", 
                            course.CoursId, course.Title);

                        var badge = await EvaluateAndAwardBadgeAsync(userId, course.CoursId);

                        if (badge != null)
                        {
                            createdBadges.Add(badge);
                            _logger.LogInformation("✅ [BADGE-CHECK] Badge {BadgeType} créé pour le cours {CoursId}", 
                                badge.BadgeType, course.CoursId);
                        }
                        else
                        {
                            _logger.LogWarning("⚠️ [BADGE-CHECK] Aucun badge créé pour le cours {CoursId} (score insuffisant?)", 
                                course.CoursId);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "❌ [BADGE-CHECK] Erreur création badge pour cours {CoursId}", course.CoursId);
                        // Continuer avec les autres cours
                    }
                }

                _logger.LogInformation("🎉 [BADGE-CHECK] Vérification terminée - {Count} badges créés", createdBadges.Count);

                return createdBadges;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ [BADGE-CHECK] Erreur lors de la vérification des badges manquants pour {UserId}", userId);
                return createdBadges;
            }
        }
    }
}