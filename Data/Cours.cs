using System;

namespace Mooc.Data
{
    public class Cours
    {
        public int Id { get; set; }
        public string Title { get; set; } = string.Empty;

        // Propriété de navigation vers Session
        public int SessionId { get; set; }
        public Session? Session { get; set; }

        // Propriété de navigation vers Quiz
        public Quiz? Quiz { get; set; }
    }
}