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

        /// <summary>
        /// **NOUVEAU** : Méthode pour injecter le service de badges après création
        /// </summary>
        public void SetCourseBadgeService(ICourseBadgeService courseBadgeService)
        {
            // Cette méthode sera appelée depuis Program.cs après la création du service
            var fieldInfo = typeof(CourseStateService).GetField("_courseBadgeService", 
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            fieldInfo?.SetValue(this, courseBadgeService);
        }

        // **MÉTHODE MISE À JOUR**: Récupération automatique de l'utilisateur connecté
        public async Task<CourseProgress> GetOrCreateProgressAsync(int coursId, string? userId = null)
        {
            // Si userId n'est pas fourni, récupérer l'utilisateur connecté
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

        /// <summary>
        /// **AMÉLIORATION** : Récupération sécurisée de l'ID utilisateur avec fallback sur les claims
        /// </summary>
        private async Task<string?> GetCurrentUserIdSafeAsync()
        {
            try
            {
                var authState = await _authenticationStateProvider.GetAuthenticationStateAsync();
                if (authState?.User?.Identity?.IsAuthenticated != true)
                {
                    return null;
                }

                // **PRIORITÉ 1** : Essayer d'obtenir depuis les claims directement (plus sûr)
                var claimUserId = authState.User.FindFirst(ClaimTypes.NameIdentifier)?.Value ??
                                 authState.User.FindFirst("sub")?.Value;

                if (!string.IsNullOrEmpty(claimUserId))
                {
                    return claimUserId;
                }

                // **PRIORITÉ 2** : Fallback sur UserManager seulement si nécessaire
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
                    // On ignore et retourne null, les claims ont déjà été vérifiés
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Erreur lors de la récupération de l'utilisateur connecté");
            }
            return null;
        }

        // **NOUVELLE MÉTHODE**: Récupérer l'état du cours de manière sécurisée
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
            // S'assurer qu'on a un UserId valide
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

                // ⭐ CORRECTION : Sérialisation sécurisée et compatible .NET 9
                var jsonOptions = new JsonSerializerOptions
                {
                    WriteIndented = true,
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                    DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull // Remplace IgnoreNullValues (obsolète)
                };

                var jsonData = JsonSerializer.Serialize(interactionData, jsonOptions);

                progress.BlockInteractions[blockIndex] = jsonData;
                progress.LastAccessedBlock = blockIndex;
                progress.LastAccessed = DateTime.UtcNow;

                // ⭐ LOGS DÉTAILLÉS pour débuggage
                _logger?.LogInformation("📝 Sauvegarde interaction - CoursId: {CourseId}, Block: {BlockIndex}", coursId, blockIndex);
                _logger?.LogDebug("📝 Type données: {DataType}", interactionData.GetType().Name);
                _logger?.LogDebug("📝 JSON généré: {JsonData}", jsonData);

                // Vérifier si c'est un score de quiz
                if (jsonData.Contains("scoreResult"))
                {
                    _logger?.LogInformation("🎯 Score de quiz détecté dans l'interaction");
                }

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
        /// Enregistre le résultat d'un quiz avec calcul de score avancé
        /// </summary>
        public async Task SaveQuizResultAsync(
            int coursId,
            int blockIndex,
            QuizStructure quizData,
            bool isCorrect,
            TimeSpan timeSpent = default,
            int hintsUsed = 0,
            int attempts = 1,
            string? userId = null)
        {
            try
            {
                _logger?.LogInformation("🎯 Début SaveQuizResultAsync - CoursId: {CourseId}, Block: {BlockIndex}", coursId, blockIndex);

                // Calculer le score du quiz
                var scoreResult = QuizScoring.CalculateScore(
                    quizData.Difficulty,
                    isCorrect,
                    timeSpent,
                    hintsUsed,
                    attempts
                );

                _logger?.LogInformation("🎯 Score calculé: {FinalScore}/{BasePoints} pts", scoreResult.FinalScore, scoreResult.BasePoints);

                // Créer l'objet d'interaction enrichi avec une structure plus explicite
                var quizInteraction = new
                {
                    type = "quiz",
                    completed = true,
                    correct = isCorrect,
                    timestamp = DateTime.UtcNow.ToString("O"), // Format ISO 8601
                    difficulty = quizData.Difficulty.ToString(),
                    questionText = quizData.Question ?? "",
                    scoreResult = new
                    {
                        difficulty = scoreResult.Difficulty.ToString(),
                        basePoints = scoreResult.BasePoints,
                        finalScore = scoreResult.FinalScore,
                        performanceLevel = scoreResult.PerformanceLevel.ToString(),
                        performanceMultiplier = scoreResult.PerformanceMultiplier,
                        timeSpentSeconds = scoreResult.TimeSpent.TotalSeconds,
                        hintsUsed = scoreResult.HintsUsed,
                        attempts = scoreResult.Attempts,
                        isCorrect = isCorrect
                    }
                };

                _logger?.LogInformation("🎯 Interaction créée avec score: {FinalScore} pts", quizInteraction.scoreResult.finalScore);

                await SaveBlockInteractionAsync(coursId, blockIndex, quizInteraction, userId);

                _logger?.LogInformation("✅ Quiz result sauvegardé avec succès");
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "❌ Erreur dans SaveQuizResultAsync pour le cours {CourseId}, bloc {BlockIndex}", coursId, blockIndex);
                throw;
            }
        }

        /// <summary>
        /// **AMÉLIORÉ** : Calcule le score total d'un cours avec parsing sécurisé
        /// </summary>
        public async Task<CourseScoreResult> CalculateCourseScoreAsync(int coursId, string? userId = null)
        {
            var progress = await GetOrCreateProgressAsync(coursId, userId);
            var quizResults = new List<QuizScoreResult>();

            foreach (var interaction in progress.BlockInteractions)
            {
                try
                {
                    using var document = JsonDocument.Parse(interaction.Value);
                    var root = document.RootElement;

                    if (root.TryGetProperty("scoreResult", out var scoreElement))
                    {
                        // **CORRECTION** : Parsing sécurisé avec vérifications d'existence des propriétés
                        var scoreResult = new QuizScoreResult();

                        // Propriétés obligatoires avec valeurs par défaut
                        if (scoreElement.TryGetProperty("difficulty", out var difficultyProp))
                        {
                            Enum.TryParse<QuizDifficulty>(difficultyProp.GetString(), out var difficulty);
                            scoreResult.Difficulty = difficulty;
                        }
                        else
                        {
                            scoreResult.Difficulty = QuizDifficulty.Débutant;
                        }

                        if (root.TryGetProperty("correct", out var correctProp))
                        {
                            scoreResult.IsCorrect = correctProp.GetBoolean();
                        }

                        if (scoreElement.TryGetProperty("basePoints", out var basePointsProp))
                        {
                            scoreResult.BasePoints = basePointsProp.GetInt32();
                        }

                        if (scoreElement.TryGetProperty("finalScore", out var finalScoreProp))
                        {
                            scoreResult.FinalScore = finalScoreProp.GetInt32();
                        }

                        if (scoreElement.TryGetProperty("performanceLevel", out var performanceProp))
                        {
                            Enum.TryParse<QuizPerformanceLevel>(performanceProp.GetString(), out var performance);
                            scoreResult.PerformanceLevel = performance;
                        }
                        else
                        {
                            scoreResult.PerformanceLevel = QuizPerformanceLevel.Average;
                        }

                        if (scoreElement.TryGetProperty("performanceMultiplier", out var multiplierProp))
                        {
                            scoreResult.PerformanceMultiplier = multiplierProp.GetDouble();
                        }
                        else
                        {
                            scoreResult.PerformanceMultiplier = 1.0;
                        }

                        if (scoreElement.TryGetProperty("timeSpentSeconds", out var timeProp))
                        {
                            scoreResult.TimeSpent = TimeSpan.FromSeconds(timeProp.GetDouble());
                        }

                        if (scoreElement.TryGetProperty("hintsUsed", out var hintsProp))
                        {
                            scoreResult.HintsUsed = hintsProp.GetInt32();
                        }

                        if (scoreElement.TryGetProperty("attempts", out var attemptsProp))
                        {
                            scoreResult.Attempts = attemptsProp.GetInt32();
                        }
                        else
                        {
                            scoreResult.Attempts = 1;
                        }

                        quizResults.Add(scoreResult);
                    }
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Erreur lors du parsing du score pour le cours {CourseId}", coursId);
                    // Continue avec les autres interactions au lieu de faire planter le calcul
                }
            }

            return QuizScoring.CalculateCourseScore(quizResults);
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

        // Ajoutez le mot-clé 'private' à la méthode pour éviter toute ambiguïté
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

        // **NOUVELLE MÉTHODE** : Vérifier et attribuer un badge si éligible
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
        /// **NOUVELLE MÉTHODE** : Calcule le score d'un cours avec analyse de contenu
        /// </summary>
        public async Task<CourseScoreInfo> CalculateCourseScoreWithContentAnalysisAsync(int coursId, string? userId = null)
        {
            try
            {
                // Utiliser la méthode existante pour obtenir le résultat détaillé
                var courseScoreResult = await CalculateCourseScoreAsync(coursId, userId);
                
                // Convertir en CourseScoreInfo pour compatibilité avec MesSessions.razor
                return new CourseScoreInfo
                {
                    TotalEarnedPoints = courseScoreResult.TotalEarnedPoints,
                    TotalPossiblePoints = courseScoreResult.TotalPossiblePoints
                };
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "❌ Erreur lors du calcul du score avec analyse de contenu pour le cours {CourseId}", coursId);
                return new CourseScoreInfo
                {
                    TotalEarnedPoints = 0,
                    TotalPossiblePoints = 0
                };
            }
        }

        /// <summary>
        /// Classe simple pour retourner les informations de score
        /// </summary>
        public class CourseScoreInfo
        {
            public int TotalEarnedPoints { get; set; }
            public int TotalPossiblePoints { get; set; }
        }
    }
}

