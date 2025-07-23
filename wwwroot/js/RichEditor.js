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

    // Créer une div éditable
    element.contentEditable = true;
    element.style.minHeight = options.height + 'px';
    element.style.border = '1px solid #ced4da';
    element.style.borderRadius = '0 0 0.375rem 0.375rem';
    element.style.padding = '0.75rem';
    element.innerHTML = options.initialContent || '';

    // Ajouter le placeholder seulement si vraiment vide
    if (!element.innerHTML.trim() || element.innerHTML === '<br>') {
        element.innerHTML = `<div style="color: #6c757d; font-style: italic;">${options.placeholder}</div>`;
    }

    // Gestionnaire d'événements amélioré
    function handleInput() {
        let content = element.innerHTML;
        const textLength = element.textContent.length;

        // Vérifier si le contenu contient des images, vidéos ou du HTML riche
        const hasRichContent = content.includes('<img') || 
                              content.includes('<video') ||
                              content.includes('<iframe') ||
                              content.includes('data-video-element') ||
                              content.includes('<audio') ||
                              content.includes('<b>') || 
                              content.includes('<i>') || 
                              content.includes('<u>') ||
                              content.includes('<a>') ||
                              content.includes('<ul>') ||
                              content.includes('<ol>') ||
                              content.includes('<strong>') ||
                              content.includes('<em>');

        // Gérer le placeholder seulement si le contenu est vraiment vide
        const hasRealContent = content.trim() && 
                              !content.includes(options.placeholder) &&
                              (textLength > 0 || hasRichContent);

        if (!hasRealContent && !hasRichContent) {
            element.innerHTML = `<div style="color: #6c757d; font-style: italic;">${options.placeholder}</div>`;
            content = ''; // Envoyer un contenu vide au lieu du placeholder
        } else if (content.includes(options.placeholder) && hasRealContent) {
            // Nettoyer le placeholder si du contenu réel existe
            content = content.replace(new RegExp(`<div[^>]*>${options.placeholder}</div>`, 'g'), '');
            element.innerHTML = content;
        }

        // Ne pas limiter la longueur si c'est du contenu HTML riche (images, vidéos, etc.)
        if (textLength > options.maxLength && !hasRichContent) {
            const textContent = element.textContent.substring(0, options.maxLength);
            element.textContent = textContent;
            return;
        }

        // Log pour debug
        if (content.includes('<video') || content.includes('data-video-element')) {
            console.log('🎥 Contenu avec vidéo détecté et envoyé à Blazor');
        }

        dotNetRef.invokeMethodAsync('OnContentChanged', content, textLength);
    }

    function handleFocus() {
        if (element.innerHTML.includes(options.placeholder)) {
            element.innerHTML = '';
        }
    }

    function handleBlur() {
        // Ne remettre le placeholder que si vraiment vide (pas d'images, vidéos ou audios)
        if (!element.textContent.trim() && 
            !element.innerHTML.includes('<img') && 
            !element.innerHTML.includes('<video') &&
            !element.innerHTML.includes('<audio') &&
            !element.innerHTML.includes('<iframe') &&
            !element.innerHTML.includes('data-video-element')) {
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

    // Initialiser la barre d'outils flottante si activée
    if (options.enableStickyToolbar !== false) {
        initializeStickyToolbar(editorId);
    }
}

function initializeStickyToolbar(editorId) {
    const editorElement = document.getElementById(editorId);
    if (!editorElement) return;
    
    // Trouver la barre d'outils associée
    const toolbar = editorElement.closest('.rich-text-editor')?.querySelector('.editor-toolbar');
    if (!toolbar) return;
    
    // Fonction pour gérer le défilement
    function handleScroll() {
        const editorRect = editorElement.getBoundingClientRect();
        const toolbarRect = toolbar.getBoundingClientRect();
        
        // Distance depuis le haut de la fenêtre
        const stickyOffset = 20;
        
        // Vérifier si l'éditeur est visible et si on doit activer le mode sticky
        const shouldBeSticky = editorRect.top <= stickyOffset && 
                              editorRect.bottom > toolbarRect.height + stickyOffset;
        
        if (shouldBeSticky && !toolbar.classList.contains('sticky')) {
            toolbar.classList.add('sticky');
            // Ajouter une marge au contenu pour éviter le chevauchement
            editorElement.style.marginTop = (toolbarRect.height + 10) + 'px';
        } else if (!shouldBeSticky && toolbar.classList.contains('sticky')) {
            toolbar.classList.remove('sticky');
            editorElement.style.marginTop = '';
        }
    }
    
    // Écouter les événements de défilement
    const scrollListener = throttle(handleScroll, 16); // ~60fps
    window.addEventListener('scroll', scrollListener, { passive: true });
    
    // Stocker la référence pour le nettoyage
    stickyToolbars.set(editorId, {
        toolbar: toolbar,
        scrollListener: scrollListener,
        editorElement: editorElement
    });
    
    // Vérification initiale
    handleScroll();
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

// Fonction améliorée pour insérer des vidéos avec support multi-plateforme
export function insertVideo(editorId) {
    const editor = window.richTextEditors[editorId];
    if (!editor) return;
    
    const url = prompt("URL de la vidéo (YouTube, Vimeo, ou lien direct mp4/webm) :");
    if (!url) return;
    
    try {
        const videoElement = createVideoElement(url);
        insertVideoElement(editor, videoElement, url);
    } catch (error) {
        alert("Erreur: URL de vidéo invalide. Veuillez vérifier le lien.");
        console.error("Erreur insertion vidéo:", error);
    }
}

// Fonction pour créer l'élément vidéo approprié selon l'URL
function createVideoElement(url) {
    const urlObj = new URL(url);
    
    // Détection YouTube
    if (isYouTubeUrl(urlObj)) {
        return createYouTubeEmbed(extractYouTubeId(url));
    }
    
    // Détection Vimeo
    if (isVimeoUrl(urlObj)) {
        return createVimeoEmbed(extractVimeoId(url));
    }
    
    // Détection Dailymotion
    if (isDailymotionUrl(urlObj)) {
        return createDailymotionEmbed(extractDailymotionId(url));
    }
    
    // Fichier vidéo direct
    if (isDirectVideoUrl(url)) {
        return createDirectVideoElement(url);
    }
    
    throw new Error("Format de vidéo non supporté");
}

// Fonctions de détection des plateformes
function isYouTubeUrl(urlObj) {
    return ['www.youtube.com', 'youtube.com', 'youtu.be', 'm.youtube.com'].includes(urlObj.hostname);
}

function isVimeoUrl(urlObj) {
    return ['vimeo.com', 'www.vimeo.com', 'player.vimeo.com'].includes(urlObj.hostname);
}

function isDailymotionUrl(urlObj) {
    return ['dailymotion.com', 'www.dailymotion.com', 'dai.ly'].includes(urlObj.hostname);
}

function isDirectVideoUrl(url) {
    const videoExtensions = ['.mp4', '.webm', '.ogg', '.mov', '.avi', '.mkv'];
    return videoExtensions.some(ext => url.toLowerCase().includes(ext));
}

// Extraction des IDs pour les plateformes
function extractYouTubeId(url) {
    const regExp = /^.*(youtu.be\/|v\/|u\/\w\/|embed\/|watch\?v=|&v=)([^#&?]*).*/;
    const match = url.match(regExp);
    return (match && match[2].length === 11) ? match[2] : null;
}

function extractVimeoId(url) {
    const regExp = /(?:vimeo)\.com.*(?:videos|video|channels|)\/([\d]+)/i;
    const match = url.match(regExp);
    return match ? match[1] : null;
}

function extractDailymotionId(url) {
    const regExp = /^.+dailymotion.com\/(video|hub)\/([^_]+)[^#]*(#video=([^_&]+))?/;
    const match = url.match(regExp);
    return match ? match[2] : null;
}

// Création des éléments d'embed
function createYouTubeEmbed(videoId) {
    if (!videoId) throw new Error("ID YouTube invalide");
    
    const iframe = document.createElement('iframe');
    iframe.src = `https://www.youtube.com/embed/${videoId}?rel=0&modestbranding=1`;
    iframe.frameBorder = '0';
    iframe.allowFullscreen = true;
    iframe.allow = 'accelerometer; autoplay; clipboard-write; encrypted-media; gyroscope; picture-in-picture';
    
    return wrapVideoElement(iframe, 'youtube');
}

function createVimeoEmbed(videoId) {
    if (!videoId) throw new Error("ID Vimeo invalide");
    
    const iframe = document.createElement('iframe');
    iframe.src = `https://player.vimeo.com/video/${videoId}?color=ffffff&title=0&byline=0&portrait=0`;
    iframe.frameBorder = '0';
    iframe.allowFullscreen = true;
    iframe.allow = 'autoplay; fullscreen; picture-in-picture';
    
    return wrapVideoElement(iframe, 'vimeo');
}

function createDailymotionEmbed(videoId) {
    if (!videoId) throw new Error("ID Dailymotion invalide");
    
    const iframe = document.createElement('iframe');
    iframe.src = `https://www.dailymotion.com/embed/video/${videoId}`;
    iframe.frameBorder = '0';
    iframe.allowFullscreen = true;
    iframe.allow = 'autoplay; fullscreen';
    
    return wrapVideoElement(iframe, 'dailymotion');
}

function createDirectVideoElement(url) {
    const video = document.createElement('video');
    video.controls = true;
    video.preload = 'metadata';
    
    // Créer l'élément source
    const source = document.createElement('source');
    source.src = url;
    source.type = getVideoMimeType(url);
    
    video.appendChild(source);
    
    // Message de fallback
    video.appendChild(document.createTextNode('Votre navigateur ne supporte pas la lecture de cette vidéo.'));
    
    return wrapVideoElement(video, 'direct');
}

// Fonction pour wrapper les éléments vidéo avec un conteneur responsive
function wrapVideoElement(videoElement, platform) {
    const container = document.createElement('div');
    container.className = `video-container video-${platform}`;
    container.style.cssText = `
        position: relative;
        width: 100%;
        max-width: 100%;
        margin: 15px auto;
        background: #f8f9fa;
        border-radius: 8px;
        overflow: hidden;
        box-shadow: 0 2px 8px rgba(0,0,0,0.1);
    `;
    
    // Style pour les iframes (YouTube, Vimeo, etc.)
    if (videoElement.tagName === 'IFRAME') {
        const aspectRatio = document.createElement('div');
        aspectRatio.style.cssText = `
            position: relative;
            width: 100%;
            padding-bottom: 56.25%; /* 16:9 aspect ratio */
            height: 0;
        `;
        
        videoElement.style.cssText = `
            position: absolute;
            top: 0;
            left: 0;
            width: 100%;
            height: 100%;
        `;
        
        aspectRatio.appendChild(videoElement);
        container.appendChild(aspectRatio);
    } else {
        // Style pour les éléments video directs
        videoElement.style.cssText = `
            width: 100%;
            height: auto;
            display: block;
        `;
        container.appendChild(videoElement);
    }
    
    // Ajouter un attribut pour identifier les vidéos
    container.setAttribute('data-video-type', platform);
    container.setAttribute('data-video-element', 'true');
    
    return container;
}

// Fonction utilitaire pour déterminer le type MIME des vidéos
function getVideoMimeType(videoUrl) {
    const extension = videoUrl.split('.').pop().toLowerCase().split('?')[0];
    switch (extension) {
        case 'mp4':
            return 'video/mp4';
        case 'webm':
            return 'video/webm';
        case 'ogg':
        case 'ogv':
            return 'video/ogg';
        case 'mov':
            return 'video/quicktime';
        case 'avi':
            return 'video/x-msvideo';
        case 'mkv':
            return 'video/x-matroska';
        default:
            return 'video/mp4';
    }
}

// Fonction pour insérer l'élément vidéo dans l'éditeur
function insertVideoElement(editor, videoElement, originalUrl) {
    // Nettoyer le placeholder si présent
    if (editor.element.innerHTML.includes('color: #6c757d')) {
        editor.element.innerHTML = '';
    }
    
    editor.element.focus();
    const selection = window.getSelection();
    
    if (selection.rangeCount > 0) {
        const range = selection.getRangeAt(0);
        range.deleteContents();
        range.insertNode(videoElement);
        range.setStartAfter(videoElement);
        range.setEndAfter(videoElement);
        selection.removeAllRanges();
        selection.addRange(range);
    } else {
        editor.element.appendChild(videoElement);
    }
    
    // Mettre à jour le contenu
    editor.element.dispatchEvent(new Event('input', { bubbles: true }));
    
    console.log('🎥 Vidéo insérée:', originalUrl);
}

// Fonction pour améliorer l'affichage des vidéos (similaire à enhanceImageDisplay)
export function enhanceVideoDisplay() {
    const selectors = ['.text-message .video-container', '.rich-editor-content .video-container', '[contenteditable] .video-container'];
    
    selectors.forEach(selector => {
        document.querySelectorAll(selector).forEach(container => {
            if (!container.classList.contains('enhanced')) {
                container.classList.add('enhanced');
                
                // Ajouter des gestionnaires d'événements pour le debug
                const videoElement = container.querySelector('video, iframe');
                if (videoElement) {
                    if (videoElement.tagName === 'VIDEO') {
                        videoElement.addEventListener('loadstart', () => {
                            console.log('🎥 Chargement vidéo démarré');
                        });
                        
                        videoElement.addEventListener('canplay', () => {
                            console.log('✅ Vidéo prête à être lue');
                        });
                        
                        videoElement.addEventListener('error', (e) => {
                            console.error('❌ Erreur vidéo:', e);
                            container.style.border = '2px solid #dc3545';
                            container.innerHTML = '<p style="padding: 20px; text-align: center; color: #dc3545;">❌ Erreur de chargement de la vidéo</p>';
                        });
                    }
                }
            }
        });
    });
}

// Fonction pour insérer une vidéo depuis une URL (utilisée par le FileUploadService)
export function insertVideoFromUrl(editorId, videoUrl) {
    const editor = window.richTextEditors[editorId];
    if (editor && editor.element) {
        try {
            const videoElement = createDirectVideoElement(videoUrl);
            insertVideoElement(editor, videoElement, videoUrl);
        } catch (error) {
            console.error("Erreur insertion vidéo URL:", error);
        }
    }
}

// Fonction pour insérer l'audio depuis une URL
export function insertAudioFromUrl(editorId, audioUrl) {
    const editor = window.richTextEditors[editorId];
    if (editor && editor.element) {
        // Nettoyer le placeholder si présent
        if (editor.element.innerHTML.includes('color: #6c757d')) {
            editor.element.innerHTML = '';
        }
        
        // Créer l'élément audio
        const audio = document.createElement('audio');
        audio.controls = true;
        audio.style.width = "100%";
        audio.style.maxWidth = "100%";
        audio.style.display = "block";
        audio.style.margin = "10px auto";
        
        // Créer l'élément source
        const source = document.createElement('source');
        source.src = audioUrl;
        source.type = getAudioMimeType(audioUrl);
        
        audio.appendChild(source);
        
        // Ajouter un attribut pour identifier les audios uploadés
        audio.setAttribute('data-uploaded-audio', 'true');
        
        // Insérer l'audio
        editor.element.focus();
        const selection = window.getSelection();
        if (selection.rangeCount > 0) {
            const range = selection.getRangeAt(0);
            range.deleteContents();
            range.insertNode(audio);
            range.setStartAfter(audio);
            range.setEndAfter(audio);
            selection.removeAllRanges();
            selection.addRange(range);
        } else {
            editor.element.appendChild(audio);
        }
        
        // Mettre à jour le contenu
        editor.element.dispatchEvent(new Event('input', { bubbles: true }));
        
        console.log('🎵 Audio URL inséré:', audioUrl);
    }
}

// Fonction utilitaire pour déterminer le type MIME de l'audio
function getAudioMimeType(audioUrl) {
    const extension = audioUrl.split('.').pop().toLowerCase();
    switch (extension) {
        case 'mp3':
            return 'audio/mpeg';
        case 'ogg':
            return 'audio/ogg';
        case 'wav':
            return 'audio/wav';
        case 'm4a':
            return 'audio/mp4';
        default:
            return 'audio/mpeg';
    }
}

// Fonction améliorée pour insérer des images
export function insertImageFromUrl(editorId, imageUrl) {
    const editor = window.richTextEditors[editorId];
    if (editor && editor.element) {
        // Nettoyer le placeholder si présent
        if (editor.element.innerHTML.includes('color: #6c757d')) {
            editor.element.innerHTML = '';
        }
        
        // Créer l'élément image
        const img = document.createElement('img');
        img.src = imageUrl;
        img.style.maxWidth = "100%";
        img.style.height = "auto";
        img.style.display = "block";
        img.style.margin = "10px auto";
        img.alt = "Image insérée";
        img.loading = "lazy";
        
        // Ajouter un attribut pour identifier les images uploadées
        img.setAttribute('data-uploaded-image', 'true');
        
        // Insérer l'image
        editor.element.focus();
        const selection = window.getSelection();
        if (selection.rangeCount > 0) {
            const range = selection.getRangeAt(0);
            range.deleteContents();
            range.insertNode(img);
            range.setStartAfter(img);
            range.setEndAfter(img);
            selection.removeAllRanges();
            selection.addRange(range);
        } else {
            editor.element.appendChild(img);
        }
        
        // Mettre à jour le contenu
        editor.element.dispatchEvent(new Event('input', { bubbles: true }));
        
        console.log('📷 Image URL insérée:', imageUrl);
    }
}

export function insertFileFromUrl(editorId, fileUrl, fileName) {
    const editor = window.richTextEditors[editorId];
    if (editor && editor.element) {
        // Nettoyer le placeholder si présent
        if (editor.element.innerHTML.includes('color: #6c757d')) {
            editor.element.innerHTML = '';
        }
        
        // Créer un lien de téléchargement pour le fichier
        const link = document.createElement('a');
        link.href = fileUrl;
        link.download = fileName || 'fichier';
        link.textContent = `📎 ${fileName || 'Télécharger le fichier'}`;
        link.style.display = 'inline-block';
        link.style.margin = '5px';
        link.style.padding = '8px 12px';
        link.style.backgroundColor = '#f8f9fa';
        link.style.border = '1px solid #dee2e6';
        link.style.borderRadius = '4px';
        link.style.textDecoration = 'none';
        link.style.color = '#495057';
        
        // Ajouter un attribut pour identifier les fichiers uploadés
        link.setAttribute('data-uploaded-file', 'true');
        
        // Insérer le lien
        editor.element.focus();
        const selection = window.getSelection();
        if (selection.rangeCount > 0) {
            const range = selection.getRangeAt(0);
            range.deleteContents();
            range.insertNode(link);
            range.setStartAfter(link);
            range.setEndAfter(link);
            selection.removeAllRanges();
            selection.addRange(range);
        } else {
            editor.element.appendChild(link);
        }
        
        // Mettre à jour le contenu
        editor.element.dispatchEvent(new Event('input', { bubbles: true }));
        
        console.log('📎 Fichier inséré:', fileUrl);
    }
}

export function setContent(editorId, content) {
    const editor = window.richTextEditors[editorId];
    if (editor) {
        editor.element.innerHTML = content;
        // Améliorer immédiatement l'affichage des images et vidéos
        enhanceImageDisplay();
        enhanceVideoDisplay();
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
    
    // Nettoyer la barre d'outils flottante
    const stickyData = stickyToolbars.get(editorId);
    if (stickyData) {
        window.removeEventListener('scroll', stickyData.scrollListener);
        stickyData.toolbar.classList.remove('sticky');
        stickyData.editorElement.style.marginTop = '';
        stickyToolbars.delete(editorId);
    }
}

// Fonction pour réinitialiser toutes les barres d'outils (utile après changement de taille)
export function reinitializeStickyToolbars() {
    stickyToolbars.forEach((data, editorId) => {
        window.removeEventListener('scroll', data.scrollListener);
        data.toolbar.classList.remove('sticky');
        data.editorElement.style.marginTop = '';
    });
    stickyToolbars.clear();
    
    // Réinitialiser toutes les barres d'outils actives
    Object.keys(window.richTextEditors).forEach(editorId => {
        initializeStickyToolbar(editorId);
    });
}

// Fonction pour déclencher le clic sur un input file
export function triggerFileInputClick(selector) {
    const element = document.querySelector(selector);
    if (element && typeof element.click === 'function') {
        element.click();
    } else {
        console.error('Élément input file non trouvé avec le sélecteur:', selector);
    }
}

// Fonction améliorée pour l'affichage des images
export function enhanceImageDisplay() {
    // Améliorer l'affichage des images dans le contenu statique ET éditable
    const selectors = ['.text-message img', '.rich-editor-content img', '[contenteditable] img'];
    
    selectors.forEach(selector => {
        document.querySelectorAll(selector).forEach(img => {
            if (!img.classList.contains('enhanced')) {
                img.classList.add('enhanced');
                
                // Assurer un style cohérent
                if (!img.style.maxWidth) {
                    img.style.maxWidth = "100%";
                    img.style.height = "auto";
                    img.style.display = "block";
                    img.style.margin = "10px auto";
                }
                
                img.addEventListener('load', function() {
                    this.classList.add('loaded');
                    console.log('✅ Image chargée:', this.src.substring(0, 50) + '...');
                });
                
                img.addEventListener('error', function() {
                    console.error('❌ Erreur de chargement de l\'image:', this.src.substring(0, 50) + '...');
                    this.style.display = 'none';
                });
                
                // Si l'image est déjà chargée
                if (img.complete) {
                    img.classList.add('loaded');
                }
            }
        });
    });
}

// Fonction globale accessible depuis Blazor
window.enhanceImageDisplay = enhanceImageDisplay;
window.enhanceVideoDisplay = enhanceVideoDisplay;

// Appeler automatiquement
if (typeof window !== 'undefined') {
    window.addEventListener('DOMContentLoaded', () => {
        enhanceImageDisplay();
        enhanceVideoDisplay();
    });
    
    // Observer les changements dans le DOM pour les images et vidéos ajoutées dynamiquement
    if (window.MutationObserver) {
        const observer = new MutationObserver(function(mutations) {
            mutations.forEach(function(mutation) {
                if (mutation.addedNodes) {
                    mutation.addedNodes.forEach(function(node) {
                        if (node.nodeType === 1) { // Element node
                            if (node.tagName === 'IMG' || (node.querySelector && node.querySelector('img'))) {
                                enhanceImageDisplay();
                            }
                            if (node.tagName === 'VIDEO' || 
                                node.tagName === 'IFRAME' || 
                                node.classList?.contains('video-container') ||
                                (node.querySelector && (node.querySelector('video') || node.querySelector('iframe') || node.querySelector('.video-container')))) {
                                enhanceVideoDisplay();
                            }
                        }
                    });
                }
            });
        });
        
        observer.observe(document.body, {
            childList: true,
            subtree: true
        });
    }
    
    // Écouter les changements de taille de fenêtre
    window.addEventListener('resize', throttle(reinitializeStickyToolbars, 250));
}

