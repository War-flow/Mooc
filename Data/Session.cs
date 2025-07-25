using System;
using System.Collections.Generic;

namespace Mooc.Data
{
    public class Session
    {
        public int Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public string? Image { get; set; }
        public string? Description { get; set; }
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public int Work { get; set; }
        public bool IsActive { get; set; } = true;
        
        // Propriété pour le créateur de la session
        public string? UserId { get; set; }
        public ApplicationUser? User { get; set; }
        
        // Relation one-to-many avec les cours
        public ICollection<Cours>? Courses { get; set; }
        
        // **CORRECTION** : Remplacer object par ICollection<ApplicationUser>
        public virtual ICollection<ApplicationUser> EnrolledUsers { get; set; } = new List<ApplicationUser>();

        // Propriétés pour les notifications
        public bool NotificationSent24h { get; set; } = false;
        public bool NotificationSent1h { get; set; } = false;
    }
}