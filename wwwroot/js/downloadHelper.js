export function downloadFile(byteArray, fileName, contentType) {
    try {
        const blob = new Blob([new Uint8Array(byteArray)], { type: contentType });
        const url = window.URL.createObjectURL(blob);
        const a = document.createElement('a');
        a.href = url;
        a.download = fileName;
        a.style.display = 'none';
        document.body.appendChild(a);
        a.click();
        window.URL.revokeObjectURL(url);
        document.body.removeChild(a);
    } catch (error) {
        console.error('Erreur lors du téléchargement:', error);
        throw error;
    }
}