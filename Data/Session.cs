using System;
using System.Collections.Generic;

namespace Mooc.Data
{
    public class Session
    {
        public int Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public string? Image { get; set; } // Chemin vers l'image de la session
        public string? Description { get; set; }
        public DateTime StartDate { get; set; }
        public DateTime? EndDate { get; set; }
        public int Work { get; set; }
        public bool IsActive { get; set; } = false; // Indique si la session est active

        // Propriété de navigation vers l'utilisateur
        public string? UserId { get; set; }
        public ApplicationUser? User { get; set; }

        // Nouvelle propriété de navigation vers les cours
        public ICollection<Cours>? Courses { get; set; }
    }
}