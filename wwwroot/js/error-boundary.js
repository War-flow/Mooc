window.getBrowserInfo = () => {
    try {
        return {
            userAgent: navigator.userAgent || 'Unknown',
            viewportSize: `${window.innerWidth}x${window.innerHeight}`,
            connectionType: navigator.connection?.effectiveType || 'Unknown',
            language: navigator.language || 'Unknown',
            platform: navigator.platform || 'Unknown',
            cookieEnabled: navigator.cookieEnabled,
            onLine: navigator.onLine,
            memoryInfo: performance.memory ? {
                usedJSHeapSize: performance.memory.usedJSHeapSize,
                totalJSHeapSize: performance.memory.totalJSHeapSize
            } : null
        };
    } catch (error) {
        console.warn('Error getting browser info:', error);
        return {
            userAgent: 'Error',
            viewportSize: 'Error',
            connectionType: 'Error',
            language: 'Error',
            platform: 'Error',
            cookieEnabled: false,
            onLine: false,
            memoryInfo: null
        };
    }
};