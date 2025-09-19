class ErrorHandler {
    constructor() {
        this.setupGlobalErrorHandlers();
        this.errorCount = 0;
        this.maxErrors = 10;
    }

    setupGlobalErrorHandlers() {
        // Erreurs JavaScript globales
        window.addEventListener('error', (event) => {
            this.handleError({
                type: 'javascript',
                message: event.message,
                filename: event.filename,
                lineno: event.lineno,
                colno: event.colno,
                error: event.error
            });
        });

        // Promesses rejetées non gérées
        window.addEventListener('unhandledrejection', (event) => {
            this.handleError({
                type: 'promise',
                message: event.reason?.message || 'Unhandled promise rejection',
                error: event.reason
            });
        });

        // Erreurs de ressources (images, scripts, etc.)
        window.addEventListener('error', (event) => {
            if (event.target !== window) {
                this.handleError({
                    type: 'resource',
                    message: `Failed to load ${event.target.tagName}: ${event.target.src || event.target.href}`,
                    element: event.target.tagName
                });
            }
        }, true);
    }

    handleError(errorInfo) {
        this.errorCount++;

        if (this.errorCount > this.maxErrors) {
            console.warn('Trop d\'erreurs détectées, arrêt du logging côté client');
            return;
        }

        const errorData = {
            ...errorInfo,
            timestamp: new Date().toISOString(),
            userAgent: navigator.userAgent,
            url: window.location.href,
            userId: window.userId || 'anonymous',
            errorCount: this.errorCount
        };

        // Log local
        console.error('Client Error:', errorData);

        // Envoyer au serveur si possible
        this.sendErrorToServer(errorData);
    }

    async sendErrorToServer(errorData) {
        try {
            await fetch('/api/errors/client', {
                method: 'POST',
                headers: {
                    'Content-Type': 'application/json',
                    'X-CSRF-TOKEN': this.getCsrfToken()
                },
                body: JSON.stringify(errorData)
            });
        } catch (e) {
            console.warn('Impossible d\'envoyer l\'erreur au serveur:', e);
        }
    }

    getCsrfToken() {
        return document.querySelector('meta[name="csrf-token"]')?.content || '';
    }
}

// Gestionnaire d'erreurs côté client
window.addEventListener('error', function(event) {
    logClientError('javascript', event.message, event.filename, event.lineno, event.colno);
});

window.addEventListener('unhandledrejection', function(event) {
    logClientError('promise', event.reason.message || event.reason, window.location.href);
});

function logClientError(type, message, url = '', line = 0, col = 0) {
    try {
        const errorData = {
            type: type,
            message: message,
            error: { line: line, column: col },
            timestamp: new Date().toISOString(),
            userAgent: navigator.userAgent,
            url: url || window.location.href,
            userId: 'anonymous', // Vous pouvez récupérer l'ID utilisateur si disponible
            errorCount: 1
        };

        console.error('Client Error:', errorData);

        // Envoyer l'erreur au serveur
        fetch('/api/errors/client', {
            method: 'POST',
            headers: {
                'Content-Type': 'application/json',
            },
            body: JSON.stringify(errorData)
        }).catch(err => {
            console.error('Failed to log error to server:', err);
        });
    } catch (err) {
        console.error('Error in error handler:', err);
    }
}

// Exposer la fonction globalement
window.logClientError = logClientError;

// Initialiser le gestionnaire d'erreurs
window.errorHandler = new ErrorHandler();