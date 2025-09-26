window.richTextEditors = {};
let stickyToolbars = new Map();

// Configuration des dimensions par défaut pour les médias
const DEFAULT_MEDIA_DIMENSIONS = {
    width: 800,
    height: 600,
    maxWidth: '100%',
    aspectRatio: 4/3 // 800/600
};

// Fonction utilitaire pour limiter la fréquence des appels
function throttle(func, limit) {
    let inThrottle;
    return function () {
        const args = arguments;
        const context = this;
        if (!inThrottle) {
            func.apply(context, args);
            inThrottle = true;
            setTimeout(() => inThrottle = false, limit);
        }
    }
}

// Fonction d'initialisation de l'éditeur
export function initializeEditor(editorId, dotNetRef, options) {
    const element = document.getElementById(editorId);
    if (!element) {
        console.error(`Élément avec l'ID ${editorId} introuvable`);
        return;
    }

    // Configuration de base améliorée
    Object.assign(element.style, {
        minHeight: options.height + 'px',
        border: '1px solid #ced4da',
        borderRadius: '0 0 0.375rem 0.375rem',
        padding: '0.75rem',
        outline: 'none',
        cursor: 'text'
    });

    // Activer contentEditable APRÈS la configuration du style
    element.contentEditable = true;

    // Initialiser le contenu
    const initialContent = options.initialContent || '';

    if (initialContent.trim()) {
        element.innerHTML = initialContent;
        // ⭐ CORRECTION : Améliorer l'affichage après l'initialisation
        setTimeout(() => enhanceDisplay(), 100);
    } else {
        setPlaceholder(element, options.placeholder);
    }

    // Gestionnaires d'événements optimisés
    const handlers = createEventHandlers(element, options, dotNetRef);
    Object.entries(handlers).forEach(([event, handler]) => {
        element.addEventListener(event, handler);
    });

    // Stockage optimisé
    window.richTextEditors[editorId] = { element, dotNetRef, handlers, options };

    // Assurer le focus et la capacité d'écriture
    element.addEventListener('click', () => {
        if (!element.contains(document.activeElement)) {
            element.focus();
        }
    });

    // Assurer que le focus est sur l'éditeur au chargement
    if (options.enableStickyToolbar !== false) {
        initializeStickyToolbar(editorId);
    }

    console.log(`Éditeur ${editorId} initialisé avec succès`);
}

// ⭐ NOUVELLE FONCTION : setContent
export function setContent(editorId, content) {
    const editor = window.richTextEditors[editorId];
    if (!editor) {
        console.error(`Éditeur ${editorId} non trouvé`);
        return;
    }

    try {
        if (content && content.trim()) {
            editor.element.innerHTML = content;
            // ⭐ CORRECTION : Améliorer l'affichage après définition du contenu
            setTimeout(() => enhanceDisplay(), 100);
        } else {
            setPlaceholder(editor.element, editor.options.placeholder);
        }
        
        // Déclencher l'événement input pour notifier les changements
        editor.element.dispatchEvent(new Event('input', { bubbles: true }));
        
        console.log(`Contenu défini pour l'éditeur ${editorId}`);
    } catch (error) {
        console.error(`Erreur lors de la définition du contenu pour ${editorId}:`, error);
    }
}

// ⭐ NOUVELLE FONCTION : getContent
export function getContent(editorId) {
    const editor = window.richTextEditors[editorId];
    if (!editor) {
        console.error(`Éditeur ${editorId} non trouvé`);
        return '';
    }

    try {
        let content = editor.element.innerHTML;
        
        // Si c'est un placeholder, retourner une chaîne vide
        if (editor.element.querySelector('.placeholder-text')) {
            return '';
        }
        
        return content;
    } catch (error) {
        console.error(`Erreur lors de la récupération du contenu pour ${editorId}:`, error);
        return '';
    }
}

// Fonction pour créer les gestionnaires d'événements
function createEventHandlers(element, options, dotNetRef) {
    const hasRichContent = (content) =>
        /<(?:img|video|iframe|audio|b|i|u|a|ul|ol|strong|em)\b/.test(content) ||
        content.includes('data-video-element');

    // Vérifie si le contenu contient des éléments riches
    const handleInput = () => {
        let content = element.innerHTML;
        
        // Vérifier la taille avant de traiter
        if (!validateContentSize(content)) {
            alert('Le contenu est trop volumineux');
            return;
        }
        
        const textLength = element.textContent.length;
        const isRich = hasRichContent(content);

        // Nettoyer le placeholder si nécessaire
        if (content.includes('class="placeholder-text"')) {
            clearPlaceholder(element);
            content = element.innerHTML;
        }

        // Supprimer les espaces superflus
        const hasRealContent = content.trim() &&
            textLength > 0 || isRich;

        // Si le contenu est vide ou ne contient que des espaces, remettre le placeholder
        if (!hasRealContent) {
            setPlaceholder(element, options.placeholder);
        } else {
            // ⭐ CORRECTION : Améliorer l'affichage après chaque modification
            setTimeout(() => enhanceDisplay(), 50);
        }
        
        // Appeler la méthode .NET avec le contenu et la longueur du texte
        try {
            dotNetRef.invokeMethodAsync('OnContentChanged', content, textLength, getCsrfToken());
        } catch (error) {
            console.error('Erreur lors de l\'appel .NET:', error);
        }
    };

    const handleFocus = () => {
        // Supprimer le placeholder au focus
        if (element.querySelector('.placeholder-text')) {
            clearPlaceholder(element);
        }
        element.classList.add('focused');
    };

    const handleBlur = () => {
        element.classList.remove('focused');
        // Remettre le placeholder si vide
        if (!element.textContent.trim() && !hasRichContent(element.innerHTML)) {
            setPlaceholder(element, options.placeholder);
        }
    };

    const handleKeyDown = (e) => {
        // Supprimer le placeholder dès la première frappe
        if (element.querySelector('.placeholder-text')) {
            clearPlaceholder(element);
        }

        if (e.ctrlKey || e.metaKey) {
            const commands = { b: 'bold', i: 'italic', u: 'underline' };
            if (commands[e.key]) {
                e.preventDefault();
                document.execCommand(commands[e.key], false, null);
            }
        }
    };

    const handlePaste = (e) => {
        // Supprimer le placeholder lors du collage
        if (element.querySelector('.placeholder-text')) {
            clearPlaceholder(element);
        }
        
        // ⭐ CORRECTION : Améliorer l'affichage après le collage
        setTimeout(() => enhanceDisplay(), 100);
    };

    return {
        input: handleInput,
        focus: handleFocus,
        blur: handleBlur,
        keydown: handleKeyDown,
        paste: handlePaste
    };
}

// Fonction pour définir le placeholder
function setPlaceholder(element, placeholder) {
    element.innerHTML = `<div class="placeholder-text" style="color: #6c757d; font-style: italic; pointer-events: none;">${placeholder}</div>`;
}

// Fonction pour supprimer le placeholder
function clearPlaceholder(element) {
    const placeholderEl = element.querySelector('.placeholder-text');
    if (placeholderEl) {
        element.innerHTML = '';
    }
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

// Types MIME pour les vidéos et audios
const MIME_TYPES = {
    video: { mp4: 'video/mp4', webm: 'video/webm', ogg: 'video/ogg', mov: 'video/quicktime', avi: 'video/x-msvideo', mkv: 'video/x-matroska' },
    audio: { mp3: 'audio/mpeg', ogg: 'audio/ogg', wav: 'audio/wav', m4a: 'audio/mp4' }
};

// Fonctions simplifiées pour les commandes
export function executeCommand(editorId, command) {
    executeEditorCommand(editorId, command);
}

// Exécute une commande avec une valeur optionnelle
export function executeCommandWithValue(editorId, command, value) {
    executeEditorCommand(editorId, command, value);
}

// Exécute une commande de l'éditeur avec un ID spécifique
function executeEditorCommand(editorId, command, value = null) {
    const editor = window.richTextEditors[editorId];
    if (editor) {
        // Supprimer le placeholder avant d'exécuter la commande
        if (editor.element.querySelector('.placeholder-text')) {
            clearPlaceholder(editor.element);
        }
        // Exécuter la commande
        editor.element.focus();
        document.execCommand(command, false, value);
        editor.element.dispatchEvent(new Event('input', { bubbles: true }));
    }
}

// Améliorer la validation des URLs
function validateUrl(url, allowedProtocols = ['https:', 'http:']) {
    try {
        const urlObj = new URL(url);
        
        // Vérifier le protocole
        if (!allowedProtocols.includes(urlObj.protocol)) {
            throw new Error('Protocole non autorisé');
        }
        
        // Vérifier contre une liste noire de domaines
        const blacklistedDomains = ['malicious.com', 'phishing.net'];
        if (blacklistedDomains.some(domain => urlObj.hostname.includes(domain))) {
            throw new Error('Domaine non autorisé');
        }
        
        return true;
    } catch (error) {
        console.error('URL invalide:', error);
        return false;
    }
}

// Modifier la fonction insertVideo
export function insertVideo(editorId) {
    const url = prompt("URL de la vidéo (YouTube, Vimeo, ou lien direct mp4/webm) :");
    if (url && validateUrl(url)) {
        insertMediaElement(editorId, url, 'video');
    } else if (url) {
        alert('URL invalide ou non autorisée');
    }
}

// Insertion d'audio optimisée
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

// ⭐ MODIFIÉ : Création d'un élément vidéo avec dimensions par défaut
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

// Création d'un élément audio optimisé
function createAudioElement(url) {
    const audio = Object.assign(document.createElement('audio'), {
        controls: true,
        preload: 'metadata'
    });
    
    const source = Object.assign(document.createElement('source'), {
        src: url,
        type: getMimeType(url, 'audio')
    });
    
    audio.appendChild(source);
    
    // Wrapper pour l'audio
    const container = document.createElement('div');
    container.className = 'audio-container';
    container.setAttribute('data-audio-element', 'true');
    Object.assign(audio.style, { 
        width: '100%', 
        display: 'block', 
        margin: '10px auto' 
    });
    container.appendChild(audio);
    
    return container;
}

// ⭐ MODIFIÉ : Création d'un élément embed avec dimensions par défaut 800x600
function createEmbed(src, platform) {
    const iframe = Object.assign(document.createElement('iframe'), {
        src, 
        frameBorder: '0', 
        allowFullscreen: true,
        width: DEFAULT_MEDIA_DIMENSIONS.width,
        height: DEFAULT_MEDIA_DIMENSIONS.height
    });
    return wrapMedia(iframe, platform);
}

// ⭐ MODIFIÉ : Création d'une vidéo directe avec dimensions par défaut 800x600
function createDirectVideo(url) {
    const video = Object.assign(document.createElement('video'), {
        controls: true, 
        preload: 'metadata',
        width: DEFAULT_MEDIA_DIMENSIONS.width,
        height: DEFAULT_MEDIA_DIMENSIONS.height
    });
    const source = Object.assign(document.createElement('source'), {
        src: url, 
        type: getMimeType(url, 'video')
    });
    video.appendChild(source);
    return wrapMedia(video, 'direct');
}

// ⭐ MODIFIÉ : Wrapper media avec centrage et dimensions optimisées
function wrapMedia(element, platform) {
    const container = document.createElement('div');
    container.className = `video-container video-${platform}`;
    container.setAttribute('data-video-element', 'true');

    // Styles optimisés avec centrage et dimensions par défaut
    const containerStyles = {
        display: 'flex',
        justifyContent: 'center',
        alignItems: 'center',
        margin: '20px auto',
        maxWidth: '100%',
        width: 'fit-content'
    };

    Object.assign(container.style, containerStyles);

    const isIframe = element.tagName === 'IFRAME';
    if (isIframe) {
        const aspectRatio = document.createElement('div');
        Object.assign(aspectRatio.style, {
            position: 'relative',
            width: `${DEFAULT_MEDIA_DIMENSIONS.width}px`,
            maxWidth: DEFAULT_MEDIA_DIMENSIONS.maxWidth,
            height: `${DEFAULT_MEDIA_DIMENSIONS.height}px`,
            margin: '0 auto'
        });
        
        Object.assign(element.style, {
            position: 'absolute',
            top: '0',
            left: '0',
            width: '100%',
            height: '100%',
            borderRadius: '8px',
            boxShadow: '0 4px 12px rgba(0,0,0,0.15)'
        });
        
        aspectRatio.appendChild(element);
        container.appendChild(aspectRatio);
    } else {
        Object.assign(element.style, {
            width: `${DEFAULT_MEDIA_DIMENSIONS.width}px`,
            height: `${DEFAULT_MEDIA_DIMENSIONS.height}px`,
            maxWidth: DEFAULT_MEDIA_DIMENSIONS.maxWidth,
            display: 'block',
            margin: '0 auto',
            borderRadius: '8px',
            boxShadow: '0 4px 12px rgba(0,0,0,0.15)'
        });
        container.appendChild(element);
    }

    return container;
}

// Fonction pour obtenir le type MIME basé sur l'URL
function getMimeType(url, type) {
    const ext = url.split('.').pop().toLowerCase().split('?')[0];
    return MIME_TYPES[type]?.[ext] || (type === 'video' ? 'video/mp4' : 'audio/mpeg');
}

// Fonction pour insérer un média à partir d'une URL
function insertMediaFromUrl(editorId, url, type) {
    const editor = window.richTextEditors[editorId];
    if (!editor) return;

    const element = createMediaElement(url, type);
    insertIntoEditor(editor, element);
}

// Fonction pour créer un élément média basé sur le type
function createMediaElement(url, type) {
    switch (type) {
        case 'video': return createDirectVideo(url);
        case 'audio': return createAudioElement(url);
        case 'image': return createImageElement(url);
        default: throw new Error('Type non supporté');
    }
}

// ⭐ MODIFIÉ : Fonction pour créer un élément image avec dimensions par défaut 800x600 et centrage
function createImageElement(url) {
    const container = document.createElement('div');
    container.className = 'image-container';
    
    // Styles du conteneur pour le centrage
    Object.assign(container.style, {
        display: 'flex',
        justifyContent: 'center',
        alignItems: 'center',
        margin: '20px auto',
        maxWidth: '100%',
        width: 'fit-content'
    });

    const img = document.createElement('img');
    img.src = url;
    img.alt = 'Image insérée';
    img.loading = 'lazy';
    
    // Appliquer les dimensions par défaut avec styles améliorés
    Object.assign(img.style, {
        width: `${DEFAULT_MEDIA_DIMENSIONS.width}px`,
        height: `${DEFAULT_MEDIA_DIMENSIONS.height}px`,
        maxWidth: DEFAULT_MEDIA_DIMENSIONS.maxWidth,
        objectFit: 'cover', // Maintient les proportions en remplissant l'espace
        display: 'block',
        borderRadius: '8px',
        boxShadow: '0 4px 12px rgba(0,0,0,0.15)',
        transition: 'transform 0.2s ease-in-out'
    });
    
    // ⭐ Ajouter des gestionnaires d'événements pour l'affichage
    img.addEventListener('load', () => {
        img.classList.add('loaded');
        console.log('Image chargée avec succès:', url);
    });
    
    img.addEventListener('error', (e) => {
        console.error('Erreur de chargement de l\'image:', url, e);
        
        // Créer un conteneur d'erreur avec dimensions par défaut
        const errorContainer = document.createElement('div');
        Object.assign(errorContainer.style, {
            width: `${DEFAULT_MEDIA_DIMENSIONS.width}px`,
            height: `${DEFAULT_MEDIA_DIMENSIONS.height}px`,
            maxWidth: DEFAULT_MEDIA_DIMENSIONS.maxWidth,
            border: '2px dashed #dc3545',
            backgroundColor: '#f8d7da',
            color: '#721c24',
            display: 'flex',
            flexDirection: 'column',
            alignItems: 'center',
            justifyContent: 'center',
            gap: '10px',
            borderRadius: '8px',
            fontSize: '14px'
        });
        
        errorContainer.innerHTML = `
            <i class="bi bi-exclamation-triangle" style="font-size: 24px;"></i>
            <span>Impossible de charger l'image</span>
            <small style="opacity: 0.7;">Vérifiez que l'URL est correcte et accessible</small>
        `;
        
        // Remplacer l'image défaillante par le conteneur d'erreur
        container.replaceChild(errorContainer, img);
    });
    
    // Ajouter un effet de survol
    img.addEventListener('mouseenter', () => {
        img.style.transform = 'scale(1.02)';
    });
    
    img.addEventListener('mouseleave', () => {
        img.style.transform = 'scale(1)';
    });
    
    container.appendChild(img);
    return container;
}

// Fonction pour insérer un élément dans l'éditeur
function insertIntoEditor(editor, element) {
    // Supprimer le placeholder
    if (editor.element.querySelector('.placeholder-text')) {
        clearPlaceholder(editor.element);
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
    
    // ⭐ CORRECTION : Améliorer l'affichage après insertion
    setTimeout(() => enhanceDisplay(), 100);
}

// Fonction pour détruire l'éditeur
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

// ⭐ CORRECTION : Fonction d'amélioration consolidée et renforcée
function enhanceDisplay() {
    try {
        const selectors = [
            '.text-message img',
            '.text-message .video-container', 
            '.rich-editor-content img',
            '.rich-editor-content .video-container',
            '.text-content img', // Ajouter ce sélecteur
            '.text-block img'    // Ajouter ce sélecteur
        ];

        selectors.forEach(selector => {
            document.querySelectorAll(selector).forEach(element => {
                if (!element.classList.contains('enhanced')) {
                    element.classList.add('enhanced');
                    setupMediaHandlers(element);
                }
            });
        });
        
        // ⭐ Vérifier spécifiquement les images uploadées
        document.querySelectorAll('img[src*="/uploads/images/"]').forEach(img => {
            if (!img.classList.contains('enhanced')) {
                img.classList.add('enhanced');
                setupImageHandlers(img);
                console.log('Image uploadée améliorée:', img.src);
            }
        });
        
    } catch (error) {
        console.error('Erreur lors de l\'amélioration de l\'affichage:', error);
    }
}

// ⭐ CORRECTION : Fonction pour configurer les gestionnaires d'événements pour les médias améliorée
function setupMediaHandlers(element) {
    if (element.tagName === 'IMG') {
        setupImageHandlers(element);
    }
}

// ⭐ NOUVELLE FONCTION : Gestionnaires spécifiques pour les images
function setupImageHandlers(img) {
    // Supprimer les anciens gestionnaires pour éviter les doublons
    const newImg = img.cloneNode(true);
    img.parentNode?.replaceChild(newImg, img);
    
    newImg.addEventListener('load', () => {
        newImg.classList.add('loaded');
        console.log('Image chargée:', newImg.src);
    });
    
    newImg.addEventListener('error', (e) => {
        console.error('Erreur de chargement de l\'image:', newImg.src);
        // ⭐ CORRECTION : Améliorer le message d'erreur et le style
        newImg.style.border = '2px dashed #dc3545';
        newImg.style.backgroundColor = '#f8d7da';
        newImg.style.color = '#721c24';
        newImg.style.textAlign = 'center';
        newImg.style.padding = '20px';
        newImg.style.borderRadius = '4px';
        newImg.style.fontSize = '14px';
        
        // ⭐ CORRECTION : Changer le message d'erreur
        newImg.alt = 'Erreur de chargement de l\'image';
        
        // ⭐ CORRECTION : Créer un conteneur d'erreur avec icône
        const errorContainer = document.createElement('div');
        errorContainer.style.cssText = `
            border: 2px dashed #dc3545;
            background-color: #f8d7da;
            color: #721c24;
            text-align: center;
            padding: 20px;
            border-radius: 4px;
            font-size: 14px;
            display: flex;
            flex-direction: column;
            align-items: center;
            gap: 10px;
            max-width: 100%;
            margin: 10px auto;
        `;
        
        errorContainer.innerHTML = `
            <i class="bi bi-exclamation-triangle" style="font-size: 24px;"></i>
            <span>Impossible de charger l'image</span>
            <small style="opacity: 0.7;">Vérifiez que l'URL est correcte et accessible</small>
        `;
        
        // Remplacer l'image défaillante par le conteneur d'erreur
        newImg.parentNode?.replaceChild(errorContainer, newImg);
    });
    
    // Si l'image est déjà chargée
    if (newImg.complete && newImg.naturalWidth > 0) {
        newImg.classList.add('loaded');
    } else if (newImg.complete && newImg.naturalWidth === 0) {
        // L'image a fini de charger mais a échoué
        newImg.dispatchEvent(new Event('error'));
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

// Exports globaux et initialisation
window.enhanceImageDisplay = window.enhanceVideoDisplay = enhanceDisplay;

if (typeof window !== 'undefined') {
    window.addEventListener('DOMContentLoaded', enhanceDisplay);

    // Observer optimisé
    if (window.MutationObserver) {
        new MutationObserver(mutations => {
            if (mutations.some(m => m.addedNodes.length)) {
                setTimeout(() => enhanceDisplay(), 50);
            }
        }).observe(document.body, { childList: true, subtree: true });
    }
}

// Ajouter un mécanisme pour inclure les tokens CSRF
function getCsrfToken() {
    return document.querySelector('meta[name="csrf-token"]')?.content || '';
}

// Ajouter des limites de taille
const MAX_CONTENT_LENGTH = 100000; // 100KB
const MAX_FILE_SIZE = 5 * 1024 * 1024; // 5MB

function validateContentSize(content) {
    if (content.length > MAX_CONTENT_LENGTH) {
        console.warn('Contenu trop volumineux');
        return false;
    }
    return true;
}

// Fonction pour déclencher le clic sur un input file
export function triggerFileInputClick(selector) {
    try {
        const fileInput = document.querySelector(selector);
        if (fileInput) {
            fileInput.click();
        } else {
            console.error(`Élément non trouvé avec le sélecteur: ${selector}`);
        }
    } catch (error) {
        console.error('Erreur lors du déclenchement du clic sur l\'input file:', error);
    }
}

// ⭐ CORRECTION : Fonction pour insérer une image à partir d'une URL améliorée
export function insertImageFromUrl(editorId, url) {
    const editor = window.richTextEditors[editorId];
    if (!editor) {
        console.error('Éditeur non trouvé:', editorId);
        return;
    }

    try {
        console.log('Insertion d\'une image:', url);
        const img = createImageElement(url);
        insertIntoEditor(editor, img);
        
        // ⭐ Force l'amélioration de l'affichage après insertion
        setTimeout(() => {
            enhanceDisplay();
            console.log('Image insérée et affichage amélioré');
        }, 100);
        
    } catch (error) {
        console.error('Erreur lors de l\'insertion de l\'image:', error);
    }
}

// Fonction pour insérer un audio à partir d'une URL
export function insertAudioFromUrl(editorId, url) {
    const editor = window.richTextEditors[editorId];
    if (!editor) return;

    try {
        const audio = createAudioElement(url);
        insertIntoEditor(editor, audio);
    } catch (error) {
        console.error('Erreur lors de l\'insertion de l\'audio:', error);
    }
}

// Fonction pour insérer un fichier à partir d'une URL
export function insertFileFromUrl(editorId, url, fileName) {
    const editor = window.richTextEditors[editorId];
    if (!editor) return;

    try {
        const link = createFileLink(url, fileName);
        insertIntoEditor(editor, link);
    } catch (error) {
        console.error('Erreur lors de l\'insertion du fichier:', error);
    }
}

// Fonction pour créer un lien de fichier
function createFileLink(url, fileName) {
    const link = document.createElement('a');
    link.href = url;
    link.textContent = fileName;
    link.target = '_blank';
    link.rel = 'noopener noreferrer';
    link.style.cssText = 'color: #007bff; text-decoration: underline; margin: 0 5px;';
    
    // Ajouter une icône selon le type de fichier
    const icon = document.createElement('i');
    const extension = fileName.split('.').pop()?.toLowerCase();
    
    switch (extension) {
        case 'pdf':
            icon.className = 'bi bi-file-earmark-pdf';
            break;
        case 'doc':
        case 'docx':
            icon.className = 'bi bi-file-earmark-word';
            break;
        case 'xls':
        case 'xlsx':
            icon.className = 'bi bi-file-earmark-excel';
            break;
        case 'ppt':
        case 'pptx':
            icon.className = 'bi bi-file-earmark-ppt';
            break;
        case 'zip':
        case 'rar':
            icon.className = 'bi bi-file-earmark-zip';
            break;
        default:
            icon.className = 'bi bi-file-earmark';
    }
    
    icon.style.marginRight = '5px';
    link.insertBefore(icon, link.firstChild);
    
    return link;
}

// Fonction pour insérer un lien
export function insertLink(editorId) {
    const editor = window.richTextEditors[editorId];
    if (!editor) return;

    const url = prompt("URL du lien :");
    if (url && validateUrl(url)) {
        const text = prompt("Texte du lien (optionnel):") || url;
        
        // Supprimer le placeholder si nécessaire
        if (editor.element.querySelector('.placeholder-text')) {
            clearPlaceholder(editor.element);
        }

        editor.element.focus();
        const selection = window.getSelection();
        
        if (selection.rangeCount > 0) {
            const range = selection.getRangeAt(0);
            const selectedText = range.toString();
            
            if (selectedText) {
                // Il y a du texte sélectionné, créer un lien avec ce texte
                const link = document.createElement('a');
                link.href = url;
                link.textContent = selectedText;
                link.target = '_blank';
                link.rel = 'noopener noreferrer';
                
                range.deleteContents();
                range.insertNode(link);
            } else {
                // Pas de texte sélectionné, insérer un nouveau lien
                const link = document.createElement('a');
                link.href = url;
                link.textContent = text;
                link.target = '_blank';
                link.rel = 'noopener noreferrer';
                
                range.insertNode(link);
                range.setStartAfter(link);
            }
            
            selection.removeAllRanges();
            selection.addRange(range);
        }
        
        editor.element.dispatchEvent(new Event('input', { bubbles: true }));
    } else if (url) {
        alert('URL invalide');
    }
}

// Exports globaux pour Blazor
if (typeof window !== 'undefined') {
    // Exposer toutes les fonctions exportées dans l'objet window
    window.initializeEditor = initializeEditor;
    window.setContent = setContent;
    window.getContent = getContent;
    window.executeCommand = executeCommand;
    window.executeCommandWithValue = executeCommandWithValue;
    window.insertVideo = insertVideo;
    window.destroyEditor = destroyEditor;
    window.triggerFileInputClick = triggerFileInputClick;
    window.insertImageFromUrl = insertImageFromUrl;
    window.insertAudioFromUrl = insertAudioFromUrl;
    window.insertFileFromUrl = insertFileFromUrl;
    window.insertLink = insertLink;
    
    // Fonctions utilitaires déjà exposées
    window.enhanceImageDisplay = window.enhanceVideoDisplay = enhanceDisplay;
}