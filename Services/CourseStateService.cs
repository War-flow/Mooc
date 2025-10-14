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
        public Dictionary<string, string> BlockInteractions { get; set; } = new();
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
        // ✅ AJOUT : Service automatique de certificat
        private readonly IAutomaticCertificateService? _automaticCertificateService;

        public CourseStateService(
            IDbContextFactory<ApplicationDbContext> contextFactory,
            UserManager<ApplicationUser> userManager,
            AuthenticationStateProvider authenticationStateProvider,
            ICertificateEligibilityService eligibilityService,
            ICourseValidationService courseValidationService,
            ICertificateNotificationService certificateNotificationService,
            ILogger<CourseStateService>? logger = null,
            IAutomaticCertificateService? automaticCertificateService = null,
            ICourseBadgeService? courseBadgeService = null) // ✅ AJOUT : Injection du service de badges
        {
            _contextFactory = contextFactory;
            _userManager = userManager;
            _authenticationStateProvider = authenticationStateProvider;
            _eligibilityService = eligibilityService;
            _courseValidationService = courseValidationService;
            _certificateNotificationService = certificateNotificationService;
            _logger = logger;
            _automaticCertificateService = automaticCertificateService;
            _courseBadgeService = courseBadgeService; // ✅ AJOUT : Initialiser le service
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

                progress.BlockInteractions[blockIndex.ToString()] = jsonData;
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

            if (progress.BlockInteractions.TryGetValue(blockIndex.ToString(), out var data))
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
        /// **CORRIGÉ** : Enregistre le résultat d'une question du questionnaire avec l'index de la question
        /// </summary>
        public async Task SaveQuizResultAsync(
            int coursId,
            int blockIndex,
            int questionIndex, // ✅ AJOUT : Index de la question
            bool isCorrect,
            string? userId = null)
        {
            try
            {
                _logger?.LogInformation("🎯 Sauvegarde réponse questionnaire - CoursId: {CourseId}, Block: {BlockIndex}, Question: {QuestionIndex}", 
                    coursId, blockIndex, questionIndex);

                var scoreResult = QuizScoring.CalculateScore(isCorrect);

                _logger?.LogInformation("🎯 Score calculé: {FinalScore} pts (système questionnaire unique)", scoreResult.FinalScore);

                var quizInteraction = new
                {
                    type = "questionnaire",
                    completed = true,
                    correct = isCorrect,
                    questionIndex = questionIndex, // ✅ AJOUT : Stocker l'index de la question
                    timestamp = DateTime.UtcNow.ToString("O"),
                    scoreResult = new
                    {
                        finalScore = scoreResult.FinalScore,
                        isCorrect = isCorrect
                    }
                };

                // ✅ CORRECTION MAJEURE : Utiliser une clé unique combinant blockIndex et questionIndex
                // Cela évite d'écraser les réponses précédentes
                var interactionKey = $"{blockIndex}_q{questionIndex}";
                
                var progress = await GetOrCreateProgressAsync(coursId, userId);

                var jsonOptions = new JsonSerializerOptions
                {
                    WriteIndented = true,
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                    DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
                };

                var jsonData = JsonSerializer.Serialize(quizInteraction, jsonOptions);

                // ✅ Utiliser une clé unique pour chaque question
                progress.BlockInteractions[interactionKey] = jsonData;
                progress.LastAccessedBlock = blockIndex;
                progress.LastAccessed = DateTime.UtcNow;

                _logger?.LogInformation("📝 Sauvegarde interaction avec clé: {InteractionKey}", interactionKey);

                await SaveProgressAsync(progress);

                _logger?.LogInformation("✅ Réponse questionnaire sauvegardée avec succès");
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "❌ Erreur dans SaveQuizResultAsync pour le cours {CourseId}, bloc {BlockIndex}, question {QuestionIndex}", 
                    coursId, blockIndex, questionIndex);
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

            // ✅ CORRECTION : Filtrer les interactions de type questionnaire
            var questionnaireInteractions = progress.BlockInteractions
                .Where(kvp => kvp.Key.ToString().Contains("_q")) // Clés au format "blockIndex_qQuestionIndex"
                .ToList();

            _logger?.LogInformation("📝 {InteractionCount} interactions de questionnaire trouvées", questionnaireInteractions.Count);

            // Récupérer les réponses depuis les interactions
            foreach (var interaction in questionnaireInteractions)
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
                        
                        _logger?.LogInformation("📝 Question {Key} trouvée: Correct={IsCorrect}, Score={Score}", 
                            interaction.Key, scoreResult.IsCorrect, scoreResult.FinalScore);
                    }
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Erreur lors du parsing du score pour la clé {Key} du cours {CourseId}", 
                        interaction.Key, coursId);
                }
            }

            _logger?.LogInformation("📋 Questions répondues: {AttemptedCount}/{TotalCount}", quizResults.Count, totalQuestions);

            var courseScoreResult = QuizScoring.CalculateCourseScore(quizResults);
            
            // Utiliser les valeurs du questionnaire
            courseScoreResult.TotalPossiblePoints = totalPossiblePoints;
            courseScoreResult.QuizCount = totalQuestions;
            
            // Recalculer le pourcentage avec les bonnes valeurs
            courseScoreResult.ScorePercentage = totalPossiblePoints > 0 
                ? (double)courseScoreResult.TotalEarnedPoints / totalPossiblePoints * 100 
                : 0;

            _logger?.LogInformation("🎯 Score final cours {CourseId}: {EarnedPoints}/{TotalPoints} pts ({Percentage:F1}%)", 
                coursId, courseScoreResult.TotalEarnedPoints, totalPossiblePoints, courseScoreResult.ScorePercentage);

            return courseScoreResult;
        }

        /// <summary>
        /// **SIMPLIFIÉ** : Calcule le score avec le nombre total de questions
        /// </summary>
        public async Task<CourseScoreResultWithTotal> CalculateCourseScoreWithTotalAsync(int coursId, string? userId = null)
        {
            var currentScoreResult = await CalculateCourseScoreAsync(coursId, userId);
            var (totalQuestions, totalPossiblePoints) = await GetQuestionnaireInfoAsync(coursId);
            
            return new CourseScoreResultWithTotal
            {
                TotalEarnedPoints = currentScoreResult.TotalEarnedPoints,   
                TotalPossiblePoints = totalPossiblePoints, // ✅ Utiliser totalPossiblePoints du questionnaire
                ScorePercentage = totalPossiblePoints > 0 
                    ? (double)currentScoreResult.TotalEarnedPoints / totalPossiblePoints * 100 
                    : 0,
                QuizCount = totalQuestions, // ✅ Nombre total de questions
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
                        BlockInteractions = JsonSerializer.Deserialize<Dictionary<string, string>>(progress.BlockInteractions ?? "{}") ?? new Dictionary<string, string>(),
                        LastAccessed = progress.LastAccessed,
                        IsCompleted = progress.IsCompleted,
                        CorrectAnswers = CalculateCorrectAnswersFromInteractions(
                            JsonSerializer.Deserialize<Dictionary<string, string>>(progress.BlockInteractions ?? "{}") ?? new Dictionary<string, string>()
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
                BlockInteractions = new Dictionary<string, string>(),
                LastAccessed = DateTime.UtcNow,
                IsCompleted = false,
                CorrectAnswers = 0
            };
        }

        private int CalculateCorrectAnswersFromInteractions(Dictionary<string, string> interactions)
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
                _logger?.LogInformation("🎓 [COURS-STATE] DÉBUT vérification certificat - User: {UserId}, Cours: {CoursId}", userId, coursId);

                // ✅ CORRECTION PRINCIPALE : Utiliser le service automatique si disponible
                if (_automaticCertificateService != null)
                {
                    // 🔧 Récupérer le sessionId depuis le coursId
                    using var context = await _contextFactory.CreateDbContextAsync();
                    var cours = await context.Courses
                        .AsNoTracking()
                        .FirstOrDefaultAsync(c => c.Id == coursId);

                    if (cours == null)
                    {
                        _logger?.LogWarning("⚠️ [COURS-STATE] Cours {CoursId} introuvable pour la génération de certificat", coursId);
                        return;
                    }

                    var autoSessionId = cours.SessionId; // Renommé pour éviter le conflit

                    _logger?.LogInformation("🔍 [COURS-STATE] Appel du service automatique de certificat - Session: {SessionId}", autoSessionId);

                    // ✅ APPEL DU SERVICE AUTOMATIQUE
                    await _automaticCertificateService.CheckAndGenerateCertificateAsync(userId, autoSessionId);

                    _logger?.LogInformation("🎓 [COURS-STATE] FIN vérification certificat via service automatique");
                    return;
                }

                // ⚠️ FALLBACK : Si le service automatique n'est pas disponible, utiliser l'ancienne méthode
                _logger?.LogWarning("⚠️ [COURS-STATE] Service automatique non disponible, utilisation de la méthode de secours");

                if (_eligibilityService == null)
                {
                    _logger?.LogWarning("⚠️ [COURS-STATE] Service d'éligibilité non disponible pour la génération automatique de certificat");
                    return;
                }

                // 🔧 Récupérer le sessionId depuis le coursId
                using var fallbackContext = await _contextFactory.CreateDbContextAsync();
                var fallbackCours = await fallbackContext.Courses
                    .AsNoTracking()
                    .FirstOrDefaultAsync(c => c.Id == coursId);

                if (fallbackCours == null)
                {
                    _logger?.LogWarning("⚠️ [COURS-STATE] Cours {CoursId} introuvable pour la génération de certificat", coursId);
                    return;
                }

                var fallbackSessionId = fallbackCours.SessionId; // Renommé pour éviter le conflit

                _logger?.LogInformation("🔍 [COURS-STATE] Session trouvée: {SessionId} pour le cours {CoursId}", fallbackSessionId, coursId);

                // ✅ Vérifier l'éligibilité
                var eligibilityResult = await _eligibilityService.CheckCertificateEligibilityAsync(userId, fallbackSessionId);

                _logger?.LogInformation(
                    "📊 [COURS-STATE] Éligibilité - Complétée: {IsCompleted}, Score: {Score}%, MinScore: {HasMinScore}, CertExiste: {HasCert}",
                    eligibilityResult.IsSessionCompleted,
                    eligibilityResult.SessionScorePercentage.ToString("F1"),
                    eligibilityResult.HasMinimumScore,
                    eligibilityResult.HasExistingCertificate);

                if (eligibilityResult.HasExistingCertificate)
                {
                    _logger?.LogInformation("ℹ️ [COURS-STATE] Certificat déjà existant pour la session {SessionId}", fallbackSessionId);
                    return;
                }

                if (!eligibilityResult.IsSessionCompleted)
                {
                    _logger?.LogInformation("ℹ️ [COURS-STATE] Session {SessionId} pas encore complétée pour l'utilisateur {UserId}", fallbackSessionId, userId);
                    return;
                }

                if (!eligibilityResult.HasMinimumScore)
                {
                    _logger?.LogInformation(
                        "ℹ️ [COURS-STATE] Score insuffisant pour la session {SessionId}: {Score}% < 70%",
                        fallbackSessionId,
                        eligibilityResult.SessionScorePercentage.ToString("F1"));
                    return;
                }

                _logger?.LogInformation(
                    "🎉 [COURS-STATE] CONDITIONS REMPLIES pour la génération - Session: {SessionId}, Score: {Score}%",
                    fallbackSessionId,
                    eligibilityResult.SessionScorePercentage.ToString("F1"));

                var (certificate, wasCreated) = await _eligibilityService.EnsureCertificateExistsAsync(userId, fallbackSessionId);

                if (wasCreated && certificate != null)
                {
                    _logger?.LogInformation(
                        "🎓 [COURS-STATE] ✅ CERTIFICAT CRÉÉ - Numéro: {CertificateNumber}, Session: {SessionId}, User: {UserId}",
                        certificate.CertificateNumber,
                        fallbackSessionId,
                        userId);

                    if (_certificateNotificationService != null)
                    {
                        try
                        {
                            await _certificateNotificationService.NotifySessionCompletedAsync(userId, fallbackSessionId);
                            _logger?.LogInformation("📧 [COURS-STATE] Notification envoyée à l'utilisateur {UserId}", userId);
                        }
                        catch (Exception notifEx)
                        {
                            _logger?.LogError(notifEx, "❌ [COURS-STATE] Erreur lors de l'envoi de notification pour {UserId}", userId);
                        }
                    }
                }
                else if (certificate != null && !wasCreated)
                {
                    _logger?.LogInformation(
                        "ℹ️ [COURS-STATE] Certificat déjà créé précédemment - Numéro: {CertificateNumber}",
                        certificate.CertificateNumber);
                }
                else
                {
                    _logger?.LogWarning(
                        "⚠️ [COURS-STATE] Échec de création du certificat malgré les conditions remplies - Session: {SessionId}, User: {UserId}",
                        fallbackSessionId,
                        userId);
                }

                _logger?.LogInformation("🎓 [COURS-STATE] FIN vérification certificat");
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, 
                    "❌ [COURS-STATE] ERREUR lors de la vérification/génération du certificat pour l'utilisateur {UserId}, cours {CoursId}", 
                    userId, coursId);
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

