window.richTextEditors = {};

export function initializeEditor(editorId, dotNetRef, options) {
    const element = document.getElementById(editorId);
    if (!element) return;

    // Créer une div éditable
    element.contentEditable = true;
    element.style.minHeight = options.height + 'px';
    element.style.border = '1px solid #ced4da';
    element.style.borderRadius = '0 0 0.375rem 0.375rem';
    element.style.padding = '0.75rem';
    element.innerHTML = options.initialContent || '';

    // Ajouter le placeholder
    if (!element.innerHTML.trim()) {
        element.innerHTML = `<div style="color: #6c757d; font-style: italic;">${options.placeholder}</div>`;
    }

    // Gestionnaire d'événements
    function handleInput() {
        let content = element.innerHTML;
        const textLength = element.textContent.length;

        // Gérer le placeholder
        if (!element.textContent.trim()) {
            element.innerHTML = `<div style="color: #6c757d; font-style: italic;">${options.placeholder}</div>`;
        } else if (element.innerHTML.includes(options.placeholder)) {
            element.innerHTML = '';
        }

        // Limiter la longueur
        if (textLength > options.maxLength) {
            element.textContent = element.textContent.substring(0, options.maxLength);
            return;
        }

        dotNetRef.invokeMethodAsync('OnContentChanged', content, textLength);
    }

    function handleFocus() {
        if (element.innerHTML.includes(options.placeholder)) {
            element.innerHTML = '';
        }
    }

    function handleBlur() {
        if (!element.textContent.trim()) {
            element.innerHTML = `<div style="color: #6c757d; font-style: italic;">${options.placeholder}</div>`;
        }
    }

    function handleKeyDown(e) {
        // Empêcher certains raccourcis clavier par défaut si nécessaire
        if (e.ctrlKey || e.metaKey) {
            switch (e.key) {
                case 'b':
                    e.preventDefault();
                    document.execCommand('bold', false, null);
                    break;
                case 'i':
                    e.preventDefault();
                    document.execCommand('italic', false, null);
                    break;
                case 'u':
                    e.preventDefault();
                    document.execCommand('underline', false, null);
                    break;
            }
        }
    }

    // Ajouter les gestionnaires d'événements
    element.addEventListener('input', handleInput);
    element.addEventListener('focus', handleFocus);
    element.addEventListener('blur', handleBlur);
    element.addEventListener('keydown', handleKeyDown);

    // Stocker les références
    window.richTextEditors[editorId] = {
        element: element,
        dotNetRef: dotNetRef,
        handlers: { handleInput, handleFocus, handleBlur, handleKeyDown }
    };
}

export function executeCommand(editorId, command) {
    const editor = window.richTextEditors[editorId];
    if (editor) {
        editor.element.focus();
        document.execCommand(command, false, null);
        // Déclencher l'événement input pour mettre à jour le contenu
        editor.element.dispatchEvent(new Event('input', { bubbles: true }));
    }
}

export function executeCommandWithValue(editorId, command, value) {
    const editor = window.richTextEditors[editorId];
    if (editor) {
        editor.element.focus();
        document.execCommand(command, false, value);
        // Déclencher l'événement input pour mettre à jour le contenu
        editor.element.dispatchEvent(new Event('input', { bubbles: true }));
    }
}

export function insertLink(editorId) {
    const editor = window.richTextEditors[editorId];
    if (editor) {
        editor.element.focus();
        const url = prompt('Entrez l\'URL du lien:');
        if (url) {
            document.execCommand('createLink', false, url);
            editor.element.dispatchEvent(new Event('input', { bubbles: true }));
        }
    }
}

export function insertImage(editorId) {
    const editor = window.richTextEditors[editorId];
    if (editor) {
        editor.element.focus();
        const url = prompt('Entrez l\'URL de l\'image:');
        if (url) {
            document.execCommand('insertImage', false, url);
            editor.element.dispatchEvent(new Event('input', { bubbles: true }));
        }
    }
}

export function insertVideo(editorId) {
    const url = prompt("URL de la vidéo (mp4, webm, etc.) :");
    if (url) {
        const videoHtml = `<video controls style="max-width:100%"><source src="${url}" type="video/mp4"></video>`;
        document.getElementById(editorId).focus();
        document.execCommand('insertHTML', false, videoHtml);
    }
}

export function insertAudio(editorId) {
    const url = prompt("URL de l'audio (mp3, ogg, etc.) :");
    if (url) {
        const audioHtml = `<audio controls style="width:100%"><source src="${url}" type="audio/mpeg"></audio>`;
        document.getElementById(editorId).focus();
        document.execCommand('insertHTML', false, audioHtml);
    }
}

export function setContent(editorId, content) {
    const editor = window.richTextEditors[editorId];
    if (editor) {
        editor.element.innerHTML = content;
    }
}

export function getContent(editorId) {
    const editor = window.richTextEditors[editorId];
    return editor ? editor.element.innerHTML : '';
}

export function destroyEditor(editorId) {
    const editor = window.richTextEditors[editorId];
    if (editor) {
        const { element, handlers } = editor;
        element.removeEventListener('input', handlers.handleInput);
        element.removeEventListener('focus', handlers.handleFocus);
        element.removeEventListener('blur', handlers.handleBlur);
        element.removeEventListener('keydown', handlers.handleKeyDown);
        delete window.richTextEditors[editorId];
    }
}