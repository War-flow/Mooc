using Microsoft.AspNetCore.Components;

namespace Mooc.Services
{
    public interface IErrorHandlingService
    {
        void NavigateToError(int statusCode, string? customMessage = null, string? exceptionDetails = null);
        void NavigateToError(Exception exception, string? customMessage = null);
    }

    public class ErrorHandlingService : IErrorHandlingService
    {
        private readonly NavigationManager _navigationManager;
        private readonly ILogger<ErrorHandlingService> _logger;

        public ErrorHandlingService(NavigationManager navigationManager, ILogger<ErrorHandlingService> logger)
        {
            _navigationManager = navigationManager;
            _logger = logger;
        }

        public void NavigateToError(int statusCode, string? customMessage = null, string? exceptionDetails = null)
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

        public void NavigateToError(Exception exception, string? customMessage = null)
        {
            _logger.LogError(exception, "Exception capturée: {Message}", exception.Message);
            
            var exceptionDetails = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") == "Development" 
                ? exception.ToString() 
                : null;

            NavigateToError(500, customMessage, exceptionDetails);
        }
    }
}