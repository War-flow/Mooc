using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using Mooc.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Components.Authorization;
using System.Security.Claims;

namespace Mooc.Services
{
    public class CourseProgress
    {
        public int Id { get; set; }
        public int CoursId { get; set; }
        public string? UserId { get; set; }
        public int LastAccessedBlock { get; set; }
        public HashSet<int> CompletedBlocks { get; set; } = new();
        public Dictionary<int, string> BlockInteractions { get; set; } = new();
        public DateTime LastAccessed { get; set; }
        public bool IsCompleted { get; set; }
        public int CorrectAnswers { get; set; } = 0;
    }

    public partial class CourseStateService
    {
        private readonly IDbContextFactory<ApplicationDbContext> _contextFactory;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly AuthenticationStateProvider _authenticationStateProvider;
        private readonly Dictionary<string, CourseProgress> _courseProgresses = new();
        private readonly ICertificateEligibilityService _eligibilityService;
        private readonly ICourseValidationService _courseValidationService;
        private readonly ILogger<CourseStateService>? _logger;
        private readonly ICertificateNotificationService _certificateNotificationService;
        private readonly ICourseBadgeService? _courseBadgeService;

        public CourseStateService(
            IDbContextFactory<ApplicationDbContext> contextFactory,
            UserManager<ApplicationUser> userManager,
            AuthenticationStateProvider authenticationStateProvider,
            ICertificateEligibilityService eligibilityService,
            ICourseValidationService courseValidationService,
            ICertificateNotificationService certificateNotificationService,
            ILogger<CourseStateService>? logger = null)
        {
            _contextFactory = contextFactory;
            _userManager = userManager;
            _authenticationStateProvider = authenticationStateProvider;
            _eligibilityService = eligibilityService;
            _courseValidationService = courseValidationService;
            _certificateNotificationService = certificateNotificationService;
            _logger = logger;
        }

        public void SetCourseBadgeService(ICourseBadgeService courseBadgeService)
        {
            var fieldInfo = typeof(CourseStateService).GetField("_courseBadgeService", 
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            fieldInfo?.SetValue(this, courseBadgeService);
        }

        public async Task<CourseProgress> GetOrCreateProgressAsync(int coursId, string? userId = null)
        {
            if (string.IsNullOrEmpty(userId))
            {
                userId = await GetCurrentUserIdSafeAsync();
                if (string.IsNullOrEmpty(userId))
                {
                    throw new InvalidOperationException("Aucun utilisateur connecté trouvé");
                }
            }

            var key = $"{coursId}_{userId}";

            if (!_courseProgresses.ContainsKey(key))
            {
                var progress = await LoadProgressFromDatabaseAsync(coursId, userId);
                _courseProgresses[key] = progress;
            }

            return _courseProgresses[key];
        }

        private async Task<string?> GetCurrentUserIdSafeAsync()
        {
            try
            {
                var authState = await _authenticationStateProvider.GetAuthenticationStateAsync();
                if (authState?.User?.Identity?.IsAuthenticated != true)
                {
                    return null;
                }

                var claimUserId = authState.User.FindFirst(ClaimTypes.NameIdentifier)?.Value ??
                                 authState.User.FindFirst("sub")?.Value;

                if (!string.IsNullOrEmpty(claimUserId))
                {
                    return claimUserId;
                }

                try
                {
                    if (_userManager != null)
                    {
                        var user = await _userManager.GetUserAsync(authState.User);
                        return user?.Id;
                    }
                }
                catch (ObjectDisposedException ex)
                {
                    _logger?.LogWarning(ex, "UserManager disposé, utilisation des claims uniquement");
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Erreur lors de la récupération de l'utilisateur connecté");
            }
            return null;
        }

        public async Task<CourseProgress?> GetOrCreateProgressSafeAsync(int coursId, string? userId = null)
        {
            try
            {
                if (string.IsNullOrEmpty(userId))
                {
                    userId = await GetCurrentUserIdSafeAsync();
                    if (string.IsNullOrEmpty(userId))
                    {
                        _logger?.LogWarning("Aucun utilisateur connecté disponible pour le cours {CourseId}", coursId);
                        return null;
                    }
                }

                var key = $"{coursId}_{userId}";

                if (!_courseProgresses.ContainsKey(key))
                {
                    var progress = await LoadProgressFromDatabaseAsync(coursId, userId);
                    _courseProgresses[key] = progress;
                }

                return _courseProgresses[key];
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Erreur lors de la récupération du progrès pour le cours {CourseId}", coursId);
                return null;
            }
        }

        public async Task SaveProgressAsync(CourseProgress progress)
        {
            if (string.IsNullOrEmpty(progress.UserId))
            {
                progress.UserId = await GetCurrentUserIdSafeAsync();
                if (string.IsNullOrEmpty(progress.UserId))
                {
                    throw new InvalidOperationException("Impossible de sauvegarder le progrès sans utilisateur connecté");
                }
            }

            var key = $"{progress.CoursId}_{progress.UserId}";

            progress.CorrectAnswers = CalculateCorrectAnswersFromInteractions(progress.BlockInteractions);
            _courseProgresses[key] = progress;

            await SaveProgressToDatabaseAsync(progress);
        }

        public async Task SaveBlockInteractionAsync(int coursId, int blockIndex, object interactionData, string? userId = null)
        {
            try
            {
                var progress = await GetOrCreateProgressAsync(coursId, userId);

                var jsonOptions = new JsonSerializerOptions
                {
                    WriteIndented = true,
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                    DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
                };

                var jsonData = JsonSerializer.Serialize(interactionData, jsonOptions);

                progress.BlockInteractions[blockIndex] = jsonData;
                progress.LastAccessedBlock = blockIndex;
                progress.LastAccessed = DateTime.UtcNow;

                _logger?.LogInformation("📝 Sauvegarde interaction - CoursId: {CourseId}, Block: {BlockIndex}", coursId, blockIndex);

                await SaveProgressAsync(progress);

                _logger?.LogInformation("✅ Interaction sauvegardée avec succès");
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "❌ Erreur SaveBlockInteractionAsync pour le cours {CourseId}, bloc {BlockIndex}", coursId, blockIndex);
                throw;
            }
        }

        public async Task<T?> GetBlockInteractionAsync<T>(int coursId, int blockIndex, string? userId = null)
        {
            var progress = await GetOrCreateProgressAsync(coursId, userId);

            if (progress.BlockInteractions.TryGetValue(blockIndex, out var data))
            {
                try
                {
                    return JsonSerializer.Deserialize<T>(data);
                }
                catch
                {
                    return default(T);
                }
            }

            return default(T);
        }

        /// <summary>
        /// **SIMPLIFIÉ** : Enregistre le résultat d'une question du questionnaire (1 point si correct, 0 sinon)
        /// </summary>
        public async Task SaveQuizResultAsync(
            int coursId,
            int blockIndex,
            bool isCorrect,
            string? userId = null)
        {
            try
            {
                _logger?.LogInformation("🎯 Sauvegarde réponse questionnaire - CoursId: {CourseId}, Block: {BlockIndex}", coursId, blockIndex);

                var scoreResult = QuizScoring.CalculateScore(isCorrect);

                _logger?.LogInformation("🎯 Score calculé: {FinalScore} pts (système questionnaire unique)", scoreResult.FinalScore);

                var quizInteraction = new
                {
                    type = "questionnaire",  // ⚠️ Changement ici
                    completed = true,
                    correct = isCorrect,
                    timestamp = DateTime.UtcNow.ToString("O"),
                    scoreResult = new
                    {
                        finalScore = scoreResult.FinalScore,
                        isCorrect = isCorrect
                    }
                };

                await SaveBlockInteractionAsync(coursId, blockIndex, quizInteraction, userId);

                _logger?.LogInformation("✅ Réponse questionnaire sauvegardée avec succès");
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "❌ Erreur dans SaveQuizResultAsync pour le cours {CourseId}, bloc {BlockIndex}", coursId, blockIndex);
                throw;
            }
        }

        /// <summary>
        /// **NOUVEAU** : Compte le nombre de questions dans le bloc questionnaire unique d'un cours
        /// </summary>
        public async Task<(int totalQuestions, int totalPossiblePoints)> GetQuestionnaireInfoAsync(int coursId)
        {
            try
            {
                using var context = await _contextFactory.CreateDbContextAsync();

                var cours = await context.Courses
                    .AsNoTracking()
                    .FirstOrDefaultAsync(c => c.Id == coursId);

                if (cours == null || string.IsNullOrEmpty(cours.Content))
                {
                    _logger?.LogInformation("📊 Cours {CourseId}: Aucun contenu trouvé", coursId);
                    return (0, 0);
                }

                var blocks = JsonSerializer.Deserialize<List<JsonElement>>(cours.Content);
                if (blocks == null)
                {
                    _logger?.LogWarning("📊 Cours {CourseId}: Impossible de désérialiser le contenu", coursId);
                    return (0, 0);
                }

                // Chercher le bloc questionnaire unique
                foreach (var block in blocks)
                {
                    if (block.TryGetProperty("Type", out var typeProperty))
                    {
                        var blockType = typeProperty.GetString()?.ToLowerInvariant();
                        _logger?.LogInformation("📊 Cours {CourseId}: Bloc trouvé avec type '{BlockType}'", coursId, blockType);

                        if (blockType == "questionnaire" || blockType == "quiz" || blockType == "questions")
                        {
                            JsonElement questionsProperty = default;
                            bool foundQuestions = false;

                            // **CORRECTION PRINCIPALE** : Les questions sont dans la propriété "Content" (JSON encodé)
                            if (block.TryGetProperty("Content", out var contentProperty) && 
                                contentProperty.ValueKind == JsonValueKind.String)
                            {
                                try
                                {
                                    var contentJson = contentProperty.GetString();
                                    if (!string.IsNullOrEmpty(contentJson))
                                    {
                                        _logger?.LogInformation("📊 Cours {CourseId}: Désérialisation de la propriété Content", coursId);
                                        
                                        // Désérialiser le JSON imbriqué
                                        var contentObject = JsonSerializer.Deserialize<JsonElement>(contentJson);
                                        
                                        // Chercher les questions dans le contenu désérialisé
                                        if (contentObject.TryGetProperty("Questions", out questionsProperty) && 
                                            questionsProperty.ValueKind == JsonValueKind.Array)
                                        {
                                            foundQuestions = true;
                                            _logger?.LogInformation("📊 Cours {CourseId}: ✅ Questions trouvées dans Content.Questions", coursId);
                                        }
                                        else if (contentObject.TryGetProperty("questions", out questionsProperty) && 
                                                 questionsProperty.ValueKind == JsonValueKind.Array)
                                        {
                                            foundQuestions = true;
                                            _logger?.LogInformation("📊 Cours {CourseId}: ✅ Questions trouvées dans Content.questions", coursId);
                                        }
                                    }
                                }
                                catch (JsonException ex)
                                {
                                    _logger?.LogError(ex, "❌ Erreur lors de la désérialisation de Content pour le cours {CourseId}", coursId);
                                }
                            }
                            
                            // **FALLBACK** : Essayer les autres méthodes (pour rétrocompatibilité)
                            if (!foundQuestions)
                            {
                                // Chercher directement dans le bloc
                                if (block.TryGetProperty("Questions", out questionsProperty) && 
                                    questionsProperty.ValueKind == JsonValueKind.Array)
                                {
                                    foundQuestions = true;
                                    _logger?.LogInformation("📊 Cours {CourseId}: Questions trouvées directement dans le bloc", coursId);
                                }
                                else if (block.TryGetProperty("questions", out questionsProperty) && 
                                         questionsProperty.ValueKind == JsonValueKind.Array)
                                {
                                    foundQuestions = true;
                                    _logger?.LogInformation("📊 Cours {CourseId}: Questions trouvées (minuscules) directement dans le bloc", coursId);
                                }
                                // Chercher dans "Data"
                                else if (block.TryGetProperty("Data", out var dataProperty))
                                {
                                    _logger?.LogInformation("📊 Cours {CourseId}: Recherche dans la propriété 'Data'", coursId);
                                    
                                    if (dataProperty.TryGetProperty("Questions", out questionsProperty) && 
                                        questionsProperty.ValueKind == JsonValueKind.Array)
                                    {
                                        foundQuestions = true;
                                        _logger?.LogInformation("📊 Cours {CourseId}: Questions trouvées dans Data.Questions", coursId);
                                    }
                                    else if (dataProperty.TryGetProperty("questions", out questionsProperty) && 
                                             questionsProperty.ValueKind == JsonValueKind.Array)
                                    {
                                        foundQuestions = true;
                                        _logger?.LogInformation("📊 Cours {CourseId}: Questions trouvées dans Data.questions", coursId);
                                    }
                                }
                            }

                            if (foundQuestions)
                            {
                                int questionCount = questionsProperty.GetArrayLength();
                                int totalPoints = questionCount * QuizScoring.PointsPerQuiz; // 1 point par question
                                
                                _logger?.LogInformation("📊 Cours {CourseId}: ✅ {QuestionCount} questions trouvées = {TotalPoints} points possibles", 
                                    coursId, questionCount, totalPoints);
                                
                                return (questionCount, totalPoints);
                            }
                            else
                            {
                                _logger?.LogWarning("📊 Cours {CourseId}: ❌ Bloc questionnaire trouvé mais aucune propriété de questions détectée", coursId);
                            }
                        }
                    }
                }

                _logger?.LogInformation("📊 Cours {CourseId}: Aucun bloc questionnaire trouvé", coursId);
                return (0, 0);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "❌ Erreur lors de l'analyse du questionnaire pour le cours {CourseId}", coursId);
                return (0, 0);
            }
        }

        /// <summary>
        /// **SIMPLIFIÉ** : Calcule le score total d'un cours (1 point par question réussie)
        /// </summary>
        public async Task<CourseScoreResult> CalculateCourseScoreAsync(int coursId, string? userId = null)
        {
            var progress = await GetOrCreateProgressAsync(coursId, userId);
            var quizResults = new List<QuizScoreResult>();

            // Récupérer les informations du questionnaire
            var (totalQuestions, totalPossiblePoints) = await GetQuestionnaireInfoAsync(coursId);
            
            _logger?.LogInformation("📊 Analyse cours {CourseId}: {TotalQuestions} questions trouvées", 
                coursId, totalQuestions);

            // Récupérer les réponses depuis les interactions
            foreach (var interaction in progress.BlockInteractions)
            {
                try
                {
                    using var document = JsonDocument.Parse(interaction.Value);
                    var root = document.RootElement;

                    if (root.TryGetProperty("scoreResult", out var scoreElement))
                    {
                        var scoreResult = new QuizScoreResult();

                        if (root.TryGetProperty("correct", out var correctProp))
                        {
                            scoreResult.IsCorrect = correctProp.GetBoolean();
                        }

                        if (scoreElement.TryGetProperty("finalScore", out var finalScoreProp))
                        {
                            scoreResult.FinalScore = finalScoreProp.GetInt32();
                        }

                        quizResults.Add(scoreResult);
                    }
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Erreur lors du parsing du score pour le cours {CourseId}", coursId);
                }
            }

            _logger?.LogInformation("📋 Questions répondues: {AttemptedCount}/{TotalCount}", quizResults.Count, totalQuestions);

            var courseScoreResult = QuizScoring.CalculateCourseScore(quizResults);
            
            // Utiliser le vrai nombre total de questions
            courseScoreResult.TotalPossiblePoints = totalPossiblePoints;
            courseScoreResult.QuizCount = totalQuestions;
            
            // Recalculer le pourcentage
            courseScoreResult.ScorePercentage = totalQuestions > 0 
                ? (double)courseScoreResult.TotalEarnedPoints / courseScoreResult.TotalPossiblePoints * 100 
                : 0;

            _logger?.LogInformation("🎯 Score final cours {CourseId}: {EarnedPoints}/{TotalPoints} pts ({Percentage:F1}%)", 
                coursId, courseScoreResult.TotalEarnedPoints, courseScoreResult.TotalPossiblePoints, courseScoreResult.ScorePercentage);

            return courseScoreResult;
        }

        /// <summary>
        /// **SIMPLIFIÉ** : Calcule le score avec le nombre total de questions
        /// </summary>
        public async Task<CourseScoreResultWithTotal> CalculateCourseScoreWithTotalAsync(int coursId, string? userId = null)
        {
            var currentScoreResult = await CalculateCourseScoreAsync(coursId, userId);
            var (totalQuestions, _) = await GetQuestionnaireInfoAsync(coursId);
            
            return new CourseScoreResultWithTotal
            {
                TotalEarnedPoints = currentScoreResult.TotalEarnedPoints,
                TotalPossiblePoints = currentScoreResult.TotalPossiblePoints,
                ScorePercentage = currentScoreResult.ScorePercentage,
                QuizCount = currentScoreResult.QuizCount,
                TotalQuizCount = totalQuestions,
                CorrectAnswers = currentScoreResult.CorrectAnswers,
                QuizResults = currentScoreResult.QuizResults,
                AttemptedQuizCount = currentScoreResult.QuizResults.Count
            };
        }

        /// <summary>
        /// **OBSOLÈTE** : Compte le nombre total de quiz dans un cours (ancienne méthode)
        /// Conservée pour compatibilité descendante
        /// </summary>
        [Obsolete("Utilisez GetQuestionnaireInfoAsync à la place")]
        public async Task<int> CountQuizzesInCourseAsync(int coursId)
        {
            var (totalQuestions, _) = await GetQuestionnaireInfoAsync(coursId);
            return totalQuestions;
        }

        private async Task<CourseProgress> LoadProgressFromDatabaseAsync(int coursId, string userId)
        {
            try
            {
                using var context = await _contextFactory.CreateDbContextAsync();
                var progress = await context.CourseProgresses
                    .FirstOrDefaultAsync(cp => cp.CoursId == coursId && cp.UserId == userId);

                if (progress != null)
                {
                    return new CourseProgress
                    {
                        Id = progress.Id,
                        CoursId = progress.CoursId,
                        UserId = progress.UserId,
                        LastAccessedBlock = progress.LastAccessedBlock,
                        CompletedBlocks = JsonSerializer.Deserialize<HashSet<int>>(progress.CompletedBlocks ?? "[]") ?? new HashSet<int>(),
                        BlockInteractions = JsonSerializer.Deserialize<Dictionary<int, string>>(progress.BlockInteractions ?? "{}") ?? new Dictionary<int, string>(),
                        LastAccessed = progress.LastAccessed,
                        IsCompleted = progress.IsCompleted,
                        CorrectAnswers = CalculateCorrectAnswersFromInteractions(
                            JsonSerializer.Deserialize<Dictionary<int, string>>(progress.BlockInteractions ?? "{}") ?? new Dictionary<int, string>()
                        )
                    };
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Erreur lors du chargement du progrès pour le cours {CourseId}, utilisateur {UserId}", coursId, userId);
            }

            return new CourseProgress
            {
                CoursId = coursId,
                UserId = userId,
                LastAccessedBlock = 0,
                CompletedBlocks = new HashSet<int>(),
                BlockInteractions = new Dictionary<int, string>(),
                LastAccessed = DateTime.UtcNow,
                IsCompleted = false,
                CorrectAnswers = 0
            };
        }

        private int CalculateCorrectAnswersFromInteractions(Dictionary<int, string> interactions)
        {
            int correctCount = 0;

            foreach (var interaction in interactions)
            {
                try
                {
                    using var document = JsonDocument.Parse(interaction.Value);
                    if (document.RootElement.TryGetProperty("correct", out var correctProperty) &&
                        correctProperty.GetBoolean())
                    {
                        correctCount++;
                    }
                }
                catch
                {
                    // Ignorer les interactions mal formées
                }
            }

            return correctCount;
        }

        private async Task SaveProgressToDatabaseAsync(CourseProgress progress)
        {
            try
            {
                using var context = await _contextFactory.CreateDbContextAsync();

                var dbProgress = await context.CourseProgresses
                    .FirstOrDefaultAsync(cp => cp.CoursId == progress.CoursId && cp.UserId == progress.UserId);

                if (dbProgress == null)
                {
                    dbProgress = new Data.CourseProgress
                    {
                        CoursId = progress.CoursId,
                        UserId = progress.UserId!
                    };
                    context.CourseProgresses.Add(dbProgress);
                }

                var wasCompleted = dbProgress.IsCompleted;

                dbProgress.LastAccessedBlock = progress.LastAccessedBlock;
                dbProgress.CompletedBlocks = JsonSerializer.Serialize(progress.CompletedBlocks);
                dbProgress.BlockInteractions = JsonSerializer.Serialize(progress.BlockInteractions, new JsonSerializerOptions
                {
                    WriteIndented = true,
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                });
                dbProgress.LastAccessed = progress.LastAccessed;
                dbProgress.IsCompleted = progress.IsCompleted;

                _logger?.LogDebug("💾 Sauvegarde BD - CoursId: {CourseId}, UserId: {UserId}", progress.CoursId, progress.UserId);

                var changes = await context.SaveChangesAsync();
                _logger?.LogDebug("✅ {Changes} entité(s) sauvegardée(s) en BD", changes);

                // **NOUVEAU** : Vérifier et attribuer un badge si le cours vient d'être complété
                if (!wasCompleted && progress.IsCompleted && !string.IsNullOrEmpty(progress.UserId))
                {
                    await CheckForBadgeAward(progress.UserId, progress.CoursId);
                    await CheckForAutomaticCertificateGeneration(progress.UserId, progress.CoursId);
                }
                else if (!string.IsNullOrEmpty(progress.UserId))
                {
                    await CheckForAutomaticCertificateGeneration(progress.UserId, progress.CoursId);
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "❌ Erreur critique lors de la sauvegarde du progrès pour le cours {CourseId}, utilisateur {UserId}", progress.CoursId, progress.UserId);
                throw;
            }
        }

        private async Task CheckForBadgeAward(string userId, int coursId)
        {
            try
            {
                if (_courseBadgeService != null)
                {
                    _logger?.LogInformation("🎯 Vérification d'éligibilité au badge - CoursId: {CoursId}, UserId: {UserId}", coursId, userId);
                    
                    var badge = await _courseBadgeService.EvaluateAndAwardBadgeAsync(userId, coursId);
                    
                    if (badge != null)
                    {
                        _logger?.LogInformation("🏆 Badge {BadgeType} attribué avec succès pour le cours {CoursId}!", badge.BadgeType, coursId);
                    }
                }
                else
                {
                    _logger?.LogWarning("⚠️ CourseBadgeService non disponible pour l'attribution de badge");
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "❌ Erreur lors de la vérification d'attribution de badge pour le cours {CourseId}, utilisateur {UserId}", coursId, userId);
            }
        }

        private async Task CheckForAutomaticCertificateGeneration(string userId, int coursId)
        {
            try
            {
                if (_eligibilityService != null && _certificateNotificationService != null)
                {
                    // Remplacer NotifyCertificateAsync par NotifySessionCompletedAsync
                    var eligibilityResult = await _eligibilityService.CheckCertificateEligibilityAsync(userId, coursId);
                    if (eligibilityResult.IsEligible)
                    {
                        await _certificateNotificationService.NotifySessionCompletedAsync(userId, coursId);
                        _logger?.LogInformation("🎓 Certificat généré et notification envoyée pour l'utilisateur {UserId}, cours {CoursId}", userId, coursId);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "❌ Erreur lors de la génération automatique du certificat pour l'utilisateur {UserId}, cours {CoursId}", userId, coursId);
            }
        }

        /// <summary>
        /// Classe pour retourner toutes les informations de score
        /// </summary>
        public class CourseScoreResultWithTotal : CourseScoreResult
        {
            public int TotalQuizCount { get; set; }
            public int AttemptedQuizCount { get; set; }
        }
    }
}

