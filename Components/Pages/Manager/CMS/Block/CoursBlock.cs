namespace Mooc.Components.Pages.Manager.CMS.Block
{
    public class CoursBlock
    {
        public required string Type { get; set; } // "texte", "image", "quiz", etc.
        public object? Content { get; set; } // Contenu du bloc, peut être un texte, une image, etc.
        public string Text { get; set; } = string.Empty; // Texte du bloc, utilisé si le type est "texte"
        public int Order { get; set; } // Ordre d'affichage du bloc
        public required string Title { get; set; } // Titre du bloc
        public string? Message { get; set; } // Message du bloc
        public string? Url { get; set; } // Lien associé au bloc, utilisé si le type est "lien"
        public string? ImageUrl { get; set; } // URL de l'image, utilisé si le type est "image"
        public string? QuizId { get; set; } // ID du quiz, utilisé si le type est "quiz"
        public string? VideoUrl { get; set; } // URL de la vidéo, utilisé si le type est "video"
        public string? AudioUrl { get; set; } // URL de l'audio, utilisé si le type est "audio"
        public string? FileUrl { get; set; } // URL du fichier, utilisé si le type est "fichier"

        // Ajoutez d'autres propriétés selon le type
    }
}
