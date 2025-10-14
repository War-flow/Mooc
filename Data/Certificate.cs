using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Mooc.Data
{
    public class Certificate
    {
        public int Id { get; set; }

        [Required]
        [StringLength(100)]
        public string Title { get; set; } = string.Empty;

        [Required]
        public string UserId { get; set; } = string.Empty;

        [ForeignKey(nameof(UserId))]
        public ApplicationUser User { get; set; } = null!;

        // ✅ MODIFICATION : Nullable pour permettre l'orphelinement
        public int? SessionId { get; set; }

        [ForeignKey(nameof(SessionId))]
        public Session? Session { get; set; }

        // ✅ NOUVEAU : Propriétés d'archivage
        [StringLength(200)]
        public string? ArchivedSessionTitle { get; set; }

        public DateTime? ArchivedSessionStartDate { get; set; }
        
        public DateTime? ArchivedSessionEndDate { get; set; }

        public DateTime DateGenerated { get; set; } = DateTime.UtcNow;
        
        public DateTime DateDelivered { get; set; }

        [StringLength(50)]
        public string Status { get; set; } = "Generated";

        [StringLength(255)]
        public string? FilePath { get; set; }

        [StringLength(100)]
        public string CertificateNumber { get; set; } = string.Empty;

        // ✅ NOUVEAU : Propriété calculée pour l'affichage
        [NotMapped]
        public string DisplaySessionTitle => Session?.Title ?? ArchivedSessionTitle ?? "Session supprimée";
    }
}