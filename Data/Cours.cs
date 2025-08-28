using System;
using System.ComponentModel.DataAnnotations;

namespace Mooc.Data
{
    public class Cours
    {
        public int Id { get; set; }
        
        [Required]
        [StringLength(200)]
        public string Title { get; set; } = string.Empty;
        
        public string? Description { get; set; }
        
        // Contenu du cours au format JSON (liste de CoursBlock)
        public string? Content { get; set; }    
        
        // Ordre d'affichage dans la session
        public int Order { get; set; } = 1;

        public int Duration { get; set; } = 0; // Durée en minutes

        // Indique si le cours est publié
        public bool IsPublished { get; set; } = false;
        
        // Dates de création et mise à jour
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        // Propriété de navigation vers Session
        public int SessionId { get; set; }
        public Session? Session { get; set; }
    }
}