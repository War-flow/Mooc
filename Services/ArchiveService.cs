using Microsoft.EntityFrameworkCore;
using Mooc.Data;

namespace Mooc.Services
{
    public interface IArchiveService
    {
        Task ArchiveSessionDataAsync(int sessionId);
    }

    public class ArchiveService : IArchiveService
    {
        private readonly IDbContextFactory<ApplicationDbContext> _contextFactory;
        private readonly ILogger<ArchiveService> _logger;

        public ArchiveService(
            IDbContextFactory<ApplicationDbContext> contextFactory,
            ILogger<ArchiveService> logger)
        {
            _contextFactory = contextFactory;
            _logger = logger;
        }

        public async Task ArchiveSessionDataAsync(int sessionId)
        {
            try
            {
                using var context = await _contextFactory.CreateDbContextAsync();

                // 1. Récupérer la session avec toutes ses données
                var session = await context.Sessions
                    .Include(s => s.Courses)
                    .AsNoTracking()
                    .FirstOrDefaultAsync(s => s.Id == sessionId);

                if (session == null)
                {
                    _logger.LogWarning("Session {SessionId} introuvable pour archivage", sessionId);
                    return;
                }

                _logger.LogInformation("📦 Début archivage session {SessionId} - {Title}", sessionId, session.Title);

                // 2. Archiver les certificats
                var certificates = await context.Certificates
                    .Where(c => c.SessionId == sessionId)
                    .ToListAsync();

                foreach (var certificate in certificates)
                {
                    certificate.ArchivedSessionTitle = session.Title;
                    certificate.ArchivedSessionStartDate = session.StartDate;
                    certificate.ArchivedSessionEndDate = session.EndDate;
                }

                _logger.LogInformation("✅ {Count} certificats archivés", certificates.Count);

                // 3. Archiver les badges des cours de cette session
                if (session.Courses?.Any() == true)
                {
                    var courseIds = session.Courses.Select(c => c.Id).ToList();
                    
                    var badges = await context.CourseBadges
                        .Include(cb => cb.Cours)
                        .Where(cb => cb.CoursId.HasValue && courseIds.Contains(cb.CoursId.Value))
                        .ToListAsync();

                    foreach (var badge in badges)
                    {
                        // ✅ CORRECTION : S'assurer que les deux propriétés sont remplies
                        badge.ArchivedCoursTitle = badge.Cours?.Title ?? badge.ArchivedCoursTitle ?? "Cours inconnu";
                        badge.ArchivedSessionTitle = session.Title;
                        
                        _logger.LogInformation("📝 Badge {BadgeId} archivé - Cours: {CoursTitle}, Session: {SessionTitle}", 
                            badge.Id, badge.ArchivedCoursTitle, badge.ArchivedSessionTitle);
                    }

                    _logger.LogInformation("✅ {Count} badges archivés", badges.Count);
                }

                // 4. Sauvegarder les modifications
                var savedChanges = await context.SaveChangesAsync();

                _logger.LogInformation("🎉 Archivage session {SessionId} terminé avec succès ({SavedChanges} entités modifiées)", 
                    sessionId, savedChanges);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Erreur lors de l'archivage de la session {SessionId}", sessionId);
                throw;
            }
        }
    }
}