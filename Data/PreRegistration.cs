using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Mooc.Data
{
    public class PreRegistration
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
        
        public DateTime PreRegistrationDate { get; set; } = DateTime.UtcNow; // Changé de DateTime.Now vers DateTime.UtcNow
        
        [StringLength(50)]
        public string Status { get; set; } = "Active"; // Active, Converted, Cancelled
        
        public DateTime? NotificationSent { get; set; }
        
        public string? Notes { get; set; }
        
        // Propriété pour indiquer la priorité (optionnel)
        public int Priority { get; set; } = 0;
    }
}