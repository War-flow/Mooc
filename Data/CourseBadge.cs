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

        [Required]
        public int CoursId { get; set; }

        [ForeignKey(nameof(CoursId))]
        public Cours Cours { get; set; } = null!;

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
    }

    /// <summary>
    /// Types de badges de cours disponibles
    /// </summary>
    public enum CourseBadgeType
    {
        /// <summary>
        /// Badge Bronze : 70-79% de réussite
        /// </summary>
        Bronze = 1,

        /// <summary>
        /// Badge Argent : 80-89% de réussite
        /// </summary>
        Silver = 2,

        /// <summary>
        /// Badge Or : 90-100% de réussite
        /// </summary>
        Gold = 3,

        /// <summary>
        /// Badge Perfectionniste
        /// </summary>
        Perfect = 4
    }
}