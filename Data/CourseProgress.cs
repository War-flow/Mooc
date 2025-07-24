using System.ComponentModel.DataAnnotations;

namespace Mooc.Data
{
    public class CourseProgress
    {
        public int Id { get; set; }
        
        [Required]
        public int CoursId { get; set; }
        public Cours? Cours { get; set; }
        
        public string? UserId { get; set; }
        public ApplicationUser? User { get; set; }
        
        public int LastAccessedBlock { get; set; }
        public string? CompletedBlocks { get; set; } // JSON array des blocs complétés
        public string? BlockInteractions { get; set; } // JSON des interactions par bloc
        
        public DateTime LastAccessed { get; set; } = DateTime.UtcNow;
        public bool IsCompleted { get; set; } = false;
    }
}