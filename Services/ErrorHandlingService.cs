using Microsoft.AspNetCore.Components;
using System.Text.Json;

namespace Mooc.Services
{
    public interface IErrorHandlingService
    {
        void NavigateToError(int statusCode, string? customMessage = null, string? exceptionDetails = null);
        void NavigateToError(Exception exception, string? customMessage = null);
        Task<string> LogErrorAsync(Exception exception, string? context = null, Dictionary<string, object>? additionalData = null);
        Task NotifyAdministratorsAsync(Exception exception, string errorId);
    }

    public class ErrorHandlingService : IErrorHandlingService
    {
        private readonly NavigationManager _navigationManager;
        private readonly ILogger<ErrorHandlingService> _logger;
        private readonly IServiceProvider _serviceProvider;

        public ErrorHandlingService(
            NavigationManager navigationManager, 
            ILogger<ErrorHandlingService> logger,
            IServiceProvider serviceProvider)
        {
            _navigationManager = navigationManager;
            _logger = logger;
            _serviceProvider = serviceProvider;
        }

        public void NavigateToError(int statusCode, string? customMessage = null, string? exceptionDetails = null)
        {
            try
            {
                var parameters = new Dictionary<string, object?>
                {
                    ["StatusCode"] = statusCode
                };

                if (!string.IsNullOrEmpty(customMessage))
                    parameters["CustomMessage"] = customMessage;

                if (!string.IsNullOrEmpty(exceptionDetails))
                    parameters["ExceptionDetails"] = exceptionDetails;

                var uri = _navigationManager.GetUriWithQueryParameters("/Error", parameters);
                _navigationManager.NavigateTo(uri);
            }
            catch (Exception ex)
            {
                _logger.LogCritical(ex, "Failed to navigate to error page");
                // Fallback ultime
                _navigationManager.NavigateTo("/Error", true);
            }
        }

        public void NavigateToError(Exception exception, string? customMessage = null)
        {
            Task.Run(async () =>
            {
                var errorId = await LogErrorAsync(exception, "NavigateToError", new Dictionary<string, object>
                {
                    ["CustomMessage"] = customMessage ?? "",
                    ["NavigationUri"] = _navigationManager.Uri
                });

                // Notification asynchrone des administrateurs pour les erreurs critiques
                if (IsCriticalError(exception))
                {
                    await NotifyAdministratorsAsync(exception, errorId);
                }
            });

            var exceptionDetails = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") == "Development" 
                ? exception.ToString() 
                : null;

            NavigateToError(500, customMessage, exceptionDetails);
        }

        public async Task<string> LogErrorAsync(Exception exception, string? context = null, Dictionary<string, object>? additionalData = null)
        {
            var errorId = Guid.NewGuid().ToString();
            
            var errorData = new
            {
                ErrorId = errorId,
                Timestamp = DateTime.UtcNow,
                Exception = new
                {
                    Type = exception.GetType().FullName,
                    Message = exception.Message,
                    StackTrace = exception.StackTrace,
                    InnerException = exception.InnerException?.Message
                },
                Context = context,
                AdditionalData = additionalData,
                Environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT"),
                MachineName = Environment.MachineName
            };

            _logger.LogError(exception, 
                "Error logged with ID {ErrorId} in context {Context}. Data: {@ErrorData}",
                errorId, context, errorData);

            return errorId;
        }

        public async Task NotifyAdministratorsAsync(Exception exception, string errorId)
        {
            try
            {
                // Récupérer le service de notification s'il existe
                var notificationService = _serviceProvider.GetService<INotificationService>();
                if (notificationService != null)
                {
                    var message = $"Erreur critique détectée (ID: {errorId})\n" +
                                  $"Type: {exception.GetType().Name}\n" +
                                  $"Message: {exception.Message}\n" +
                                  $"Heure: {DateTime.Now:yyyy-MM-dd HH:mm:ss}";

                    // Cette méthode devrait être implémentée dans votre NotificationService
                    // await notificationService.NotifyAdministratorsAsync("Erreur Critique", message);
                }
            }
            catch (Exception notificationEx)
            {
                _logger.LogError(notificationEx, "Failed to notify administrators about error {ErrorId}", errorId);
            }
        }

        private static bool IsCriticalError(Exception exception)
        {
            return exception is OutOfMemoryException ||
                   exception is StackOverflowException ||
                   exception is AccessViolationException ||
                   exception.Message.Contains("database", StringComparison.OrdinalIgnoreCase) ||
                   exception.Message.Contains("connection", StringComparison.OrdinalIgnoreCase);
        }
    }
}