using Microsoft.EntityFrameworkCore;
using Mooc.Data;

namespace Mooc.Services
{
    /// <summary>
    /// Interface pour le service de vérification d'éligibilité aux certificats
    /// </summary>
    public interface ICertificateEligibilityService
    {
        Task<CertificateEligibilityResult> CheckCertificateEligibilityAsync(string userId, int sessionId);
        Task<bool> IsSessionCompletedByUserAsync(string userId, int sessionId);
        Task<double> CalculateSessionScorePercentageAsync(string userId, int sessionId);
        Task<bool> HasExistingCertificateAsync(string userId, int sessionId);
        
        /// <summary>
        /// Vérifie l'existence du certificat et le crée si les conditions sont remplies (score >= 70%)
        /// </summary>
        Task<(Certificate? certificate, bool wasCreated)> EnsureCertificateExistsAsync(string userId, int sessionId);
    }

    /// <summary>
    /// Service tiers pour gérer la vérification d'éligibilité aux certificats
    /// Résout la dépendance circulaire entre CourseStateService et AutomaticCertificateService
    /// </summary>
    public class CertificateEligibilityService : ICertificateEligibilityService
    {
        private readonly IDbContextFactory<ApplicationDbContext> _contextFactory;
        private readonly ILogger<CertificateEligibilityService> _logger;
        
        // Constantes
        private const double MINIMUM_SCORE_PERCENTAGE = 70.0;

        public CertificateEligibilityService(
            IDbContextFactory<ApplicationDbContext> contextFactory,
            ILogger<CertificateEligibilityService> logger)
        {
            _contextFactory = contextFactory;
            _logger = logger;
        }

        /// <summary>
        /// Vérifie si l'utilisateur est éligible pour un certificat
        /// </summary>
        public async Task<CertificateEligibilityResult> CheckCertificateEligibilityAsync(string userId, int sessionId)
        {
            try
            {
                var result = new CertificateEligibilityResult
                {
                    SessionId = sessionId,
                    UserId = userId,
                    IsSessionCompleted = await IsSessionCompletedByUserAsync(userId, sessionId),
                    SessionScorePercentage = await CalculateSessionScorePercentageAsync(userId, sessionId)
                };

                result.HasMinimumScore = result.SessionScorePercentage >= MINIMUM_SCORE_PERCENTAGE;
                result.IsEligible = result.IsSessionCompleted && result.HasMinimumScore;
                result.HasExistingCertificate = await HasExistingCertificateAsync(userId, sessionId);

                _logger.LogInformation(
                    "Éligibilité certificat - Session {SessionId}, User {UserId}: Complétée={IsCompleted}, Score={Score}%, Éligible={IsEligible}, Existe={HasCertificate}",
                    sessionId, userId, result.IsSessionCompleted, result.SessionScorePercentage.ToString("F1"), result.IsEligible, result.HasExistingCertificate);

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur lors de la vérification d'éligibilité au certificat pour la session {SessionId}, utilisateur {UserId}", sessionId, userId);
                return new CertificateEligibilityResult
                {
                    SessionId = sessionId,
                    UserId = userId,
                    IsSessionCompleted = false,
                    SessionScorePercentage = 0,
                    HasMinimumScore = false,
                    IsEligible = false,
                    HasExistingCertificate = false
                };
            }
        }

        /// <summary>
        /// Vérifie si une session est complétée par un utilisateur
        /// </summary>
        public async Task<bool> IsSessionCompletedByUserAsync(string userId, int sessionId)
        {
            try
            {
                using var context = await _contextFactory.CreateDbContextAsync();

                // Récupérer tous les cours obligatoires de la session
                var requiredCourses = await context.Courses
                    .Where(c => c.SessionId == sessionId && c.IsPublished)
                    .Select(c => c.Id)
                    .ToListAsync();

                if (!requiredCourses.Any())
                {
                    _logger.LogInformation("Aucun cours obligatoire trouvé pour la session {SessionId}", sessionId);
                    return false;
                }

                // Vérifier que tous les cours obligatoires sont complétées
                var completedRequiredCourses = await context.CourseProgresses
                    .Where(cp => cp.UserId == userId &&
                                requiredCourses.Contains(cp.CoursId) &&
                                cp.IsCompleted)
                    .CountAsync();

                var isCompleted = completedRequiredCourses == requiredCourses.Count;

                _logger.LogInformation(
                    "Session {SessionId} pour l'utilisateur {UserId}: {CompletedCount}/{TotalCount} cours complétés. Session terminée: {IsCompleted}",
                    sessionId, userId, completedRequiredCourses, requiredCourses.Count, isCompleted);

                return isCompleted;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur lors de la vérification de completion de session {SessionId} pour l'utilisateur {UserId}", sessionId, userId);
                return false;
            }
        }

        /// <summary>
        /// Calcule le pourcentage de score global d'une session pour un utilisateur
        /// </summary>
        public async Task<double> CalculateSessionScorePercentageAsync(string userId, int sessionId)
        {
            try
            {
                _logger.LogInformation("🔍 Calcul du score de session {SessionId} pour l'utilisateur {UserId}", sessionId, userId);

                using var context = await _contextFactory.CreateDbContextAsync();
                
                // Récupérer tous les cours de la session
                var courses = await context.Courses
                    .Where(c => c.SessionId == sessionId && c.IsPublished)
                    .Select(c => c.Id)
                    .ToListAsync();

                if (!courses.Any())
                {
                    _logger.LogWarning("Aucun cours trouvé pour la session {SessionId}", sessionId);
                    return 0;
                }

                var totalEarnedPoints = 0;
                var totalPossiblePoints = 0;

                // Calculer les scores pour chaque cours
                foreach (var courseId in courses)
                {
                    var courseScore = await CalculateCourseScoreAsync(courseId, userId);
                    totalEarnedPoints += courseScore.TotalEarnedPoints;
                    totalPossiblePoints += courseScore.TotalPossiblePoints;
                }

                var scorePercentage = totalPossiblePoints > 0 ? (double)totalEarnedPoints / totalPossiblePoints * 100 : 0;
                
                _logger.LogInformation(
                    "📊 Score session {SessionId}: {EarnedPoints}/{PossiblePoints} pts = {Percentage}%", 
                    sessionId, 
                    totalEarnedPoints, 
                    totalPossiblePoints, 
                    scorePercentage.ToString("F1"));
                
                return scorePercentage;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Erreur lors du calcul du score de session {SessionId} pour l'utilisateur {UserId}", sessionId, userId);
                return 0;
            }
        }

        /// <summary>
        /// Vérifie si un certificat existe déjà pour l'utilisateur et la session
        /// </summary>
        public async Task<bool> HasExistingCertificateAsync(string userId, int sessionId)
        {
            try
            {
                using var context = await _contextFactory.CreateDbContextAsync();
                return await context.Certificates
                    .AnyAsync(c => c.UserId == userId && c.SessionId == sessionId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur lors de la vérification d'existence de certificat pour la session {SessionId}, utilisateur {UserId}", sessionId, userId);
                return false;
            }
        }

        /// <summary>
        /// Vérifie l'existence du certificat et le crée si les conditions sont remplies
        /// </summary>
        /// <param name="userId">Identifiant de l'utilisateur</param>
        /// <param name="sessionId">Identifiant de la session</param>
        /// <returns>Un tuple contenant (certificat existant ou créé, a été créé maintenant)</returns>
        public async Task<(Certificate? certificate, bool wasCreated)> EnsureCertificateExistsAsync(string userId, int sessionId)
        {
            try
            {
                _logger.LogInformation("🔍 Vérification/Création certificat - Session {SessionId}, User {UserId}", sessionId, userId);

                using var context = await _contextFactory.CreateDbContextAsync();

                // 1. Vérifier si un certificat existe déjà
                var existingCertificate = await context.Certificates
                    .FirstOrDefaultAsync(c => c.UserId == userId && c.SessionId == sessionId);

                if (existingCertificate != null)
                {
                    _logger.LogInformation("✅ Certificat déjà existant - Numéro: {CertificateNumber}", existingCertificate.CertificateNumber);
                    return (existingCertificate, false);
                }

                // 2. Vérifier l'éligibilité
                var eligibilityResult = await CheckCertificateEligibilityAsync(userId, sessionId);

                if (!eligibilityResult.IsEligible)
                {
                    var reason = !eligibilityResult.IsSessionCompleted 
                        ? "Session non complétée" 
                        : $"Score insuffisant ({eligibilityResult.SessionScorePercentage:F1}% < 70%)";
                    
                    _logger.LogWarning("🚫 Certificat non créé - {Reason}", reason);
                    return (null, false);
                }

                // 3. Récupérer les informations de la session
                var session = await context.Sessions.FindAsync(sessionId);
                if (session == null)
                {
                    _logger.LogError("❌ Session {SessionId} introuvable", sessionId);
                    return (null, false);
                }

                // 4. Générer un numéro de certificat unique
                var certificateNumber = await GenerateUniqueCertificateNumberAsync(context);

                // 5. Créer le nouveau certificat
                var newCertificate = new Certificate
                {
                    Title = $"Certificat de réussite - {session.Title}",
                    UserId = userId,
                    SessionId = sessionId,
                    DateGenerated = DateTime.UtcNow,
                    DateDelivered = DateTime.UtcNow,
                    Status = "Generated",
                    CertificateNumber = certificateNumber
                };

                context.Certificates.Add(newCertificate);
                await context.SaveChangesAsync();

                _logger.LogInformation(
                    "🎉 Certificat créé avec succès - Numéro: {CertificateNumber}, Score: {Score}%",
                    certificateNumber,
                    eligibilityResult.SessionScorePercentage.ToString("F1"));

                return (newCertificate, true);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Erreur lors de la vérification/création du certificat pour la session {SessionId}, utilisateur {UserId}", sessionId, userId);
                return (null, false);
            }
        }

        /// <summary>
        /// Génère un numéro de certificat unique
        /// </summary>
        private async Task<string> GenerateUniqueCertificateNumberAsync(ApplicationDbContext context)
        {
            string certificateNumber;
            bool exists;

            do
            {
                var timestamp = DateTime.UtcNow.ToString("yyyyMMddHHmmss");
                var random = Random.Shared.Next(1000, 9999);
                certificateNumber = $"CERT-{timestamp}-{random}";

                exists = await context.Certificates
                    .AnyAsync(c => c.CertificateNumber == certificateNumber);
            } while (exists);

            return certificateNumber;
        }

        /// <summary>
        /// Calcule le score d'un cours spécifique (logique simplifiée pour éviter les dépendances)
        /// </summary>
        private async Task<CourseScoreResult> CalculateCourseScoreAsync(int courseId, string userId)
        {
            try
            {
                using var context = await _contextFactory.CreateDbContextAsync();
                
                var progress = await context.CourseProgresses
                    .AsNoTracking()
                    .FirstOrDefaultAsync(cp => cp.CoursId == courseId && cp.UserId == userId);

                if (progress == null || string.IsNullOrEmpty(progress.BlockInteractions))
                {
                    _logger.LogInformation("📊 Aucune interaction trouvée pour le cours {CourseId}, utilisateur {UserId}", courseId, userId);
                    return new CourseScoreResult();
                }

                // ✅ CORRECTION : Désérialiser avec des clés string au lieu d'int
                var interactions = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(progress.BlockInteractions) 
                    ?? new Dictionary<string, string>();

                _logger.LogInformation("📊 {InteractionCount} interactions trouvées pour le cours {CourseId}", interactions.Count, courseId);

                var totalEarnedPoints = 0;
                var totalPossiblePoints = 0;
                var correctAnswers = 0;

                // ✅ Filtrer uniquement les interactions de questionnaire (clés contenant "_q")
                var quizInteractions = interactions.Where(kvp => kvp.Key.Contains("_q"));
                
                _logger.LogInformation("📊 {QuizCount} interactions de questionnaire trouvées", quizInteractions.Count());

                foreach (var interaction in quizInteractions)
                {
                    try
                    {
                        using var document = System.Text.Json.JsonDocument.Parse(interaction.Value);
                        var root = document.RootElement;

                        if (root.TryGetProperty("scoreResult", out var scoreElement))
                        {
                            // ✅ Gérer les différents formats de score
                            int basePoints = 1; // Par défaut 1 point
                            int finalScore = 0;
                            
                            if (scoreElement.TryGetProperty("basePoints", out var basePointsElement))
                            {
                                basePoints = basePointsElement.GetInt32();
                            }
                            
                            if (scoreElement.TryGetProperty("finalScore", out var finalScoreElement))
                            {
                                finalScore = finalScoreElement.GetInt32();
                            }

                            var isCorrect = root.TryGetProperty("correct", out var correctProp) && correctProp.GetBoolean();

                            totalPossiblePoints += basePoints;
                            totalEarnedPoints += finalScore;
                            
                            if (isCorrect)
                            {
                                correctAnswers++;
                            }

                            _logger.LogInformation(
                                "📝 Question {Key}: Correct={IsCorrect}, Points={FinalScore}/{BasePoints}", 
                                interaction.Key, isCorrect, finalScore, basePoints);
                        }
                        else
                        {
                            _logger.LogWarning("⚠️ Pas de scoreResult pour l'interaction {Key}", interaction.Key);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Erreur lors du parsing de l'interaction {Key} pour le cours {CourseId}", interaction.Key, courseId);
                    }
                }

                var scorePercentage = totalPossiblePoints > 0 ? (double)totalEarnedPoints / totalPossiblePoints * 100 : 0;

                _logger.LogInformation(
                    "📊 Score cours {CourseId}: {EarnedPoints}/{PossiblePoints} pts = {Percentage}%", 
                    courseId, totalEarnedPoints, totalPossiblePoints, scorePercentage.ToString("F1"));

                return new CourseScoreResult
                {
                    TotalEarnedPoints = totalEarnedPoints,
                    TotalPossiblePoints = totalPossiblePoints,
                    CorrectAnswers = correctAnswers,
                    ScorePercentage = scorePercentage
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur lors du calcul du score du cours {CourseId} pour l'utilisateur {UserId}", courseId, userId);
                return new CourseScoreResult();
            }
        }
    }
}