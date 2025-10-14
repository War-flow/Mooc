using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Mooc.Data
{
    /// <summary>
    /// Badge attribué pour un cours spécifique basé sur les performances
    /// </summary>
    public class CourseBadge
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public string UserId { get; set; } = string.Empty;

        [ForeignKey(nameof(UserId))]
        public ApplicationUser User { get; set; } = null!;

        // ✅ MODIFICATION : Nullable pour permettre l'orphelinement
        public int? CoursId { get; set; }

        [ForeignKey(nameof(CoursId))]
        public Cours? Cours { get; set; }

        // ✅ NOUVEAU : Propriétés d'archivage
        [StringLength(200)]
        public string? ArchivedCoursTitle { get; set; }

        [StringLength(200)]
        public string? ArchivedSessionTitle { get; set; }

        /// <summary>
        /// Type de badge obtenu
        /// </summary>
        [Required]
        public CourseBadgeType BadgeType { get; set; }

        /// <summary>
        /// Score obtenu (en pourcentage)
        /// </summary>
        [Range(0, 100)]
        public double ScorePercentage { get; set; }

        /// <summary>
        /// Nombre de points obtenus
        /// </summary>
        public int PointsEarned { get; set; }

        /// <summary>
        /// Nombre total de points possibles
        /// </summary>
        public int TotalPointsPossible { get; set; }

        /// <summary>
        /// Nombre de bonnes réponses
        /// </summary>
        public int CorrectAnswers { get; set; }

        /// <summary>
        /// Nombre total de questions
        /// </summary>
        public int TotalQuestions { get; set; }

        /// <summary>
        /// Date d'obtention du badge
        /// </summary>
        public DateTime EarnedDate { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Titre personnalisé du badge
        /// </summary>
        [StringLength(200)]
        public string? CustomTitle { get; set; }

        /// <summary>
        /// Description du badge
        /// </summary>
        [StringLength(500)]
        public string? Description { get; set; }

        // ✅ CORRECTION : Propriétés calculées avec getter (au lieu de champs)
        /// <summary>
        /// Titre du cours pour l'affichage (utilise le titre archivé si le cours est supprimé)
        /// </summary>
        [NotMapped]
        public string DisplayCoursTitle => 
            Cours?.Title ?? ArchivedCoursTitle ?? "Cours supprimé";

        /// <summary>
        /// Titre de la session pour l'affichage (utilise le titre archivé si la session est supprimée)
        /// </summary>
        [NotMapped]
        public string DisplaySessionTitle => 
            Cours?.Session?.Title ?? ArchivedSessionTitle ?? "Session supprimée";
    }

    /// <summary>
    /// Types de badges disponibles selon les performances
    /// </summary>
    public enum CourseBadgeType
    {
        /// <summary>
        /// Badge Bronze : 70-79% de réussite
        /// </summary>
        Bronze,

        /// <summary>
        /// Badge Argent : 80-89% de réussite
        /// </summary>
        Silver,

        /// <summary>
        /// Badge Or : 90-99% de réussite
        /// </summary>
        Gold,

        /// <summary>
        /// Badge Perfectionniste : 100% de réussite
        /// </summary>
        Perfect
    }
}