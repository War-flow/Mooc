using Mooc.Services; // Remplacez par le namespace réel de IContentValidationService

namespace Mooc.Middleware
{
    public class ContentValidationMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<ContentValidationMiddleware> _logger;

        public ContentValidationMiddleware(RequestDelegate next, ILogger<ContentValidationMiddleware> logger)
        {
            _next = next;
            _logger = logger;
        }

        public async Task InvokeAsync(HttpContext context, IContentValidationService validationService)
        {
            // Valider les requêtes contenant du contenu HTML
            if (context.Request.Method == "POST" && 
                context.Request.ContentType?.Contains("application/json") == true)
            {
                // Logique de validation ici si nécessaire
            }

            await _next(context);
        }
    }
}