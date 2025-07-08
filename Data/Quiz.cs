using System;

namespace Mooc.Data
{
    public class Quiz
    {
        public int Id { get; set; }
        public string Title { get; set; } = string.Empty;

        // Propriété de navigation vers Cours
        public int CoursId { get; set; }
        public Cours? Cours { get; set; }

    }
}
