using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Mooc.Data
{
    public class EnrollmentHistory
    {
        public int Id { get; set; }
        
        [Required]
        public string UserId { get; set; } = string.Empty;
        
        [ForeignKey(nameof(UserId))]
        public ApplicationUser User { get; set; } = null!;
        
        [Required]
        public int SessionId { get; set; }
        
        [ForeignKey(nameof(SessionId))]
        public Session Session { get; set; } = null!;
        
        [Required]
        [StringLength(50)]
        public string Action { get; set; } = string.Empty; // "Enrollment", "Unsubscribe"
        
        public DateTime Date { get; set; } = DateTime.UtcNow;
        
        public bool FromPreRegistration { get; set; } = false;
        
        public string? Notes { get; set; }

        public DateTime EnrollmentDate { get; set; }
        public DateTime UnenrollmentDate { get; set; }
        public string UnenrollmentReason { get; set; }
        public bool IsCompleted { get; set; }

        // Propriétés calculées pour les statistiques
        [NotMapped]
        public int EnrollmentCount { get; set; }
        
        [NotMapped]
        public int UnsubscribeCount { get; set; }
        
        [NotMapped]
        public string? LastAction { get; set; }
        
        [NotMapped]
        public DateTime? LastActionDate { get; set; }
    }
}