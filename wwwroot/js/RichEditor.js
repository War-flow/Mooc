window.richTextEditors = {};
let stickyToolbars = new Map();

// Fonction utilitaire pour limiter la fréquence des appels
function throttle(func, limit) {
    let inThrottle;
    return function() {
        const args = arguments;
        const context = this;
        if (!inThrottle) {
            func.apply(context, args);
            inThrottle = true;
            setTimeout(() => inThrottle = false, limit);
        }
    }
}

export function initializeEditor(editorId, dotNetRef, options) {
    const element = document.getElementById(editorId);
    if (!element) return;

    // Configuration de base
    Object.assign(element.style, {
        contentEditable: true,
        minHeight: options.height + 'px',
        border: '1px solid #ced4da',
        borderRadius: '0 0 0.375rem 0.375rem',
        padding: '0.75rem'
    });
    
    element.innerHTML = options.initialContent || '';
    updatePlaceholder(element, options.placeholder);

    // Gestionnaires d'événements consolidés
    const handlers = createEventHandlers(element, options, dotNetRef);
    Object.entries(handlers).forEach(([event, handler]) => {
        element.addEventListener(event, handler);
    });

    // Stockage optimisé
    window.richTextEditors[editorId] = { element, dotNetRef, handlers };

    if (options.enableStickyToolbar !== false) {
        initializeStickyToolbar(editorId);
    }
}

function createEventHandlers(element, options, dotNetRef) {
    const hasRichContent = (content) => 
        /<(?:img|video|iframe|audio|b|i|u|a|ul|ol|strong|em)\b/.test(content) ||
        content.includes('data-video-element');

    const handleInput = () => {
        let content = element.innerHTML;
        const textLength = element.textContent.length;
        const isRich = hasRichContent(content);
        
        const hasRealContent = content.trim() && 
                              !content.includes(options.placeholder) &&
                              (textLength > 0 || isRich);

        if (!hasRealContent && !isRich) {
            updatePlaceholder(element, options.placeholder);
            content = '';
        } else if (content.includes(options.placeholder) && hasRealContent) {
            content = content.replace(new RegExp(`<div[^>]*>${options.placeholder}</div>`, 'g'), '');
            element.innerHTML = content;
        }

        if (textLength > options.maxLength && !isRich) {
            element.textContent = element.textContent.substring(0, options.maxLength);
            return;
        }

        dotNetRef.invokeMethodAsync('OnContentChanged', content, textLength);
    };

    const handleFocus = () => {
        if (element.innerHTML.includes(options.placeholder)) {
            element.innerHTML = '';
        }
    };

    const handleBlur = () => {
        if (!element.textContent.trim() && !hasRichContent(element.innerHTML)) {
            updatePlaceholder(element, options.placeholder);
        }
    };

    const handleKeyDown = (e) => {
        if (e.ctrlKey || e.metaKey) {
            const commands = { b: 'bold', i: 'italic', u: 'underline' };
            if (commands[e.key]) {
                e.preventDefault();
                document.execCommand(commands[e.key], false, null);
            }
        }
    };

    return { input: handleInput, focus: handleFocus, blur: handleBlur, keydown: handleKeyDown };
}

function updatePlaceholder(element, placeholder) {
    element.innerHTML = `<div style="color: #6c757d; font-style: italic;">${placeholder}</div>`;
}

// Configuration des plateformes vidéo optimisée
const VIDEO_PLATFORMS = {
    youtube: {
        hosts: ['www.youtube.com', 'youtube.com', 'youtu.be', 'm.youtube.com'],
        embed: id => `https://www.youtube.com/embed/${id}?rel=0&modestbranding=1`,
        idRegex: /^.*(youtu.be\/|v\/|u\/\w\/|embed\/|watch\?v=|&v=)([^#&?]*).*/
    },
    vimeo: {
        hosts: ['vimeo.com', 'www.vimeo.com', 'player.vimeo.com'],
        embed: id => `https://player.vimeo.com/video/${id}?color=ffffff&title=0&byline=0&portrait=0`,
        idRegex: /(?:vimeo)\.com.*(?:videos|video|channels|)\/([\d]+)/i
    },
    dailymotion: {
        hosts: ['dailymotion.com', 'www.dailymotion.com', 'dai.ly'],
        embed: id => `https://www.dailymotion.com/embed/video/${id}`,
        idRegex: /^.+dailymotion.com\/(video|hub)\/([^_]+)[^#]*(#video=([^_&]+))?/
    }
};

const MIME_TYPES = {
    video: { mp4: 'video/mp4', webm: 'video/webm', ogg: 'video/ogg', mov: 'video/quicktime', avi: 'video/x-msvideo', mkv: 'video/x-matroska' },
    audio: { mp3: 'audio/mpeg', ogg: 'audio/ogg', wav: 'audio/wav', m4a: 'audio/mp4' }
};

// Fonctions simplifiées pour les commandes
export function executeCommand(editorId, command) {
    executeEditorCommand(editorId, command);
}

export function executeCommandWithValue(editorId, command, value) {
    executeEditorCommand(editorId, command, value);
}

function executeEditorCommand(editorId, command, value = null) {
    const editor = window.richTextEditors[editorId];
    if (editor) {
        editor.element.focus();
        document.execCommand(command, false, value);
        editor.element.dispatchEvent(new Event('input', { bubbles: true }));
    }
}

export function insertLink(editorId) {
    const url = prompt('Entrez l\'URL du lien:');
    if (url) executeEditorCommand(editorId, 'createLink', url);
}

// Insertion de média optimisée
export function insertVideo(editorId) {
    const url = prompt("URL de la vidéo (YouTube, Vimeo, ou lien direct mp4/webm) :");
    if (url) insertMediaElement(editorId, url, 'video');
}

function insertMediaElement(editorId, url, type) {
    const editor = window.richTextEditors[editorId];
    if (!editor) return;

    try {
        const element = type === 'video' ? createVideoElement(url) : createAudioElement(url);
        insertIntoEditor(editor, element);
    } catch (error) {
        alert(`Erreur: URL de ${type} invalide.`);
        console.error(`Erreur insertion ${type}:`, error);
    }
}

function createVideoElement(url) {
    const urlObj = new URL(url);
    
    // Vérifier les plateformes supportées
    for (const [platform, config] of Object.entries(VIDEO_PLATFORMS)) {
        if (config.hosts.includes(urlObj.hostname)) {
            const match = url.match(config.idRegex);
            const id = match ? (match[2] || match[1]) : null;
            if (id) return createEmbed(config.embed(id), platform);
        }
    }
    
    // Vidéo directe
    if (/\.(mp4|webm|ogg|mov|avi|mkv)/i.test(url)) {
        return createDirectVideo(url);
    }
    
    throw new Error("Format non supporté");
}

function createEmbed(src, platform) {
    const iframe = Object.assign(document.createElement('iframe'), {
        src, frameBorder: '0', allowFullscreen: true
    });
    return wrapMedia(iframe, platform);
}

function createDirectVideo(url) {
    const video = Object.assign(document.createElement('video'), {
        controls: true, preload: 'metadata'
    });
    const source = Object.assign(document.createElement('source'), {
        src: url, type: getMimeType(url, 'video')
    });
    video.appendChild(source);
    return wrapMedia(video, 'direct');
}

function wrapMedia(element, platform) {
    const container = document.createElement('div');
    container.className = `video-container video-${platform}`;
    container.setAttribute('data-video-element', 'true');
    
    // Styles optimisés
    const isIframe = element.tagName === 'IFRAME';
    if (isIframe) {
        const aspectRatio = document.createElement('div');
        Object.assign(aspectRatio.style, {
            position: 'relative', width: '100%', paddingBottom: '56.25%', height: '0'
        });
        Object.assign(element.style, {
            position: 'absolute', top: '0', left: '0', width: '100%', height: '100%'
        });
        aspectRatio.appendChild(element);
        container.appendChild(aspectRatio);
    } else {
        Object.assign(element.style, { width: '100%', height: 'auto', display: 'block' });
        container.appendChild(element);
    }
    
    return container;
}

function getMimeType(url, type) {
    const ext = url.split('.').pop().toLowerCase().split('?')[0];
    return MIME_TYPES[type]?.[ext] || (type === 'video' ? 'video/mp4' : 'audio/mpeg');
}

// Fonctions d'insertion simplifiées
export function insertVideoFromUrl(editorId, videoUrl) {
    insertMediaFromUrl(editorId, videoUrl, 'video');
}

export function insertAudioFromUrl(editorId, audioUrl) {
    insertMediaFromUrl(editorId, audioUrl, 'audio');
}

export function insertImageFromUrl(editorId, imageUrl) {
    insertMediaFromUrl(editorId, imageUrl, 'image');
}

function insertMediaFromUrl(editorId, url, type) {
    const editor = window.richTextEditors[editorId];
    if (!editor) return;

    const element = createMediaElement(url, type);
    insertIntoEditor(editor, element);
}

function createMediaElement(url, type) {
    switch (type) {
        case 'video': return createDirectVideo(url);
        case 'audio': return createAudioElement(url);
        case 'image': return createImageElement(url);
        default: throw new Error('Type non supporté');
    }
}

function createAudioElement(url) {
    return Object.assign(document.createElement('audio'), {
        controls: true, src: url,
        style: 'width: 100%; display: block; margin: 10px auto;'
    });
}

function createImageElement(url) {
    return Object.assign(document.createElement('img'), {
        src: url, alt: 'Image insérée', loading: 'lazy',
        style: 'max-width: 100%; height: auto; display: block; margin: 10px auto;'
    });
}

function insertIntoEditor(editor, element) {
    if (editor.element.innerHTML.includes('color: #6c757d')) {
        editor.element.innerHTML = '';
    }
    
    editor.element.focus();
    const selection = window.getSelection();
    
    if (selection.rangeCount > 0) {
        const range = selection.getRangeAt(0);
        range.deleteContents();
        range.insertNode(element);
        range.setStartAfter(element);
        selection.removeAllRanges();
        selection.addRange(range);
    } else {
        editor.element.appendChild(element);
    }
    
    editor.element.dispatchEvent(new Event('input', { bubbles: true }));
}

// Fonctions utilitaires consolidées
export function setContent(editorId, content) {
    const editor = window.richTextEditors[editorId];
    if (editor) {
        editor.element.innerHTML = content;
        enhanceDisplay();
    }
}

export function getContent(editorId) {
    return window.richTextEditors[editorId]?.element.innerHTML || '';
}

export function destroyEditor(editorId) {
    const editor = window.richTextEditors[editorId];
    if (editor) {
        Object.entries(editor.handlers).forEach(([event, handler]) => {
            editor.element.removeEventListener(event, handler);
        });
        delete window.richTextEditors[editorId];
    }
    
    // Nettoyage toolbar
    const stickyData = stickyToolbars.get(editorId);
    if (stickyData) {
        window.removeEventListener('scroll', stickyData.scrollListener);
        stickyData.toolbar.classList.remove('sticky');
        stickyData.editorElement.style.marginTop = '';
        stickyToolbars.delete(editorId);
    }
}

// Fonction d'amélioration consolidée
function enhanceDisplay() {
    const selectors = ['.text-message img', '.text-message .video-container', '.rich-editor-content img', '.rich-editor-content .video-container'];
    
    selectors.forEach(selector => {
        document.querySelectorAll(selector).forEach(element => {
            if (!element.classList.contains('enhanced')) {
                element.classList.add('enhanced');
                setupMediaHandlers(element);
            }
        });
    });
}

function setupMediaHandlers(element) {
    if (element.tagName === 'IMG') {
        element.addEventListener('load', () => element.classList.add('loaded'));
        element.addEventListener('error', () => element.style.display = 'none');
        if (element.complete) element.classList.add('loaded');
    }
}

// Exports globaux et initialisation
window.enhanceImageDisplay = window.enhanceVideoDisplay = enhanceDisplay;

if (typeof window !== 'undefined') {
    window.addEventListener('DOMContentLoaded', enhanceDisplay);
    
    // Observer optimisé
    if (window.MutationObserver) {
        new MutationObserver(mutations => {
            if (mutations.some(m => m.addedNodes.length)) enhanceDisplay();
        }).observe(document.body, { childList: true, subtree: true });
    }
}

// Fonctions de toolbar (simplifiées)
function initializeStickyToolbar(editorId) {
    const editorElement = document.getElementById(editorId);
    const toolbar = editorElement?.closest('.rich-text-editor')?.querySelector('.editor-toolbar');
    if (!toolbar) return;
    
    const handleScroll = throttle(() => {
        const editorRect = editorElement.getBoundingClientRect();
        const shouldBeSticky = editorRect.top <= 20 && editorRect.bottom > 70;
        
        toolbar.classList.toggle('sticky', shouldBeSticky);
        editorElement.style.marginTop = shouldBeSticky ? '70px' : '';
    }, 16);
    
    window.addEventListener('scroll', handleScroll, { passive: true });
    stickyToolbars.set(editorId, { toolbar, scrollListener: handleScroll, editorElement });
}

// Fonctions utilitaires restantes
export function insertFileFromUrl(editorId, fileUrl, fileName) {
    const editor = window.richTextEditors[editorId];
    if (editor) {
        const link = Object.assign(document.createElement('a'), {
            href: fileUrl, download: fileName || 'fichier',
            textContent: `📎 ${fileName || 'Télécharger le fichier'}`,
            style: 'display: inline-block; margin: 5px; padding: 8px 12px; background: #f8f9fa; border: 1px solid #dee2e6; border-radius: 4px; text-decoration: none; color: #495057;'
        });
        insertIntoEditor(editor, link);
    }
}

export function triggerFileInputClick(selector) {
    document.querySelector(selector)?.click();
}

export function reinitializeStickyToolbars() {
    stickyToolbars.forEach((data, editorId) => {
        window.removeEventListener('scroll', data.scrollListener);
        data.toolbar.classList.remove('sticky');
        data.editorElement.style.marginTop = '';
    });
    stickyToolbars.clear();
    Object.keys(window.richTextEditors).forEach(initializeStickyToolbar);
}

