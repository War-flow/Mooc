window.richTextEditors = {};
let stickyToolbars = new Map();

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
// Ajouter un throttle plus sécurisé pour éviter les attaques par déni de service
function secureThrottle(func, limit, maxQueueSize = 10) {
    let inThrottle;
    let queueSize = 0;
    
    return function() {
        const args = arguments;
        const context = this;
        
        if (queueSize >= maxQueueSize) {
            console.warn('Trop de requêtes en attente');
            return;
        }
        
        if (!inThrottle) {
            func.apply(context, args);
            inThrottle = true;
            queueSize++;
            
            setTimeout(() => {
                inThrottle = false;
                queueSize--;
            }, limit);
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
        outline: 'none', // Supprime le contour par défaut
        cursor: 'text'    // Curseur de texte
    });

    // Activer contentEditable APRÈS la configuration du style
    element.contentEditable = true;

    // Initialiser le contenu
    const initialContent = options.initialContent || '';

    if (initialContent.trim()) {
        element.innerHTML = initialContent;
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
        }
        // Appeler la méthode .NET avec le contenu et la longueur du texte
        dotNetRef.invokeMethodAsync('OnContentChanged', content, textLength, getCsrfToken());
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

// Insertion d'image optimisée
export function insertLink(editorId) {
    const url = prompt('Entrez l\'URL du lien:');
    if (url) executeEditorCommand(editorId, 'createLink', url);
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
// Création d'un élément vidéo optimisé
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
function createEmbed(src, platform) {
    const iframe = Object.assign(document.createElement('iframe'), {
        src, frameBorder: '0', allowFullscreen: true
    });
    return wrapMedia(iframe, platform);
}

// Création d'une vidéo directe optimisée
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

// Création d'un élément audio optimisé
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

// Fonction pour obtenir le type MIME basé sur l'URL
function getMimeType(url, type) {
    const ext = url.split('.').pop().toLowerCase().split('?')[0];
    return MIME_TYPES[type]?.[ext] || (type === 'video' ? 'video/mp4' : 'audio/mpeg');
}

// Fonctions d'insertion simplifiées
export function insertVideoFromUrl(editorId, videoUrl) {
    insertMediaFromUrl(editorId, videoUrl, 'video');
}

// Fonction pour insérer un audio à partir d'une URL
export function insertAudioFromUrl(editorId, audioUrl) {
    insertMediaFromUrl(editorId, audioUrl, 'audio');
}

// Fonction pour insérer une image à partir d'une URL
export function insertImageFromUrl(editorId, imageUrl) {
    insertMediaFromUrl(editorId, imageUrl, 'image');
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

// Fonction pour créer un élément audio
function createAudioElement(url) {
    return Object.assign(document.createElement('audio'), {
        controls: true, src: url,
        style: 'width: 100%; display: block; margin: 10px auto;'
    });
}

// Fonction pour créer un élément image
function createImageElement(url) {
    return Object.assign(document.createElement('img'), {
        src: url, alt: 'Image insérée', loading: 'lazy',
        style: 'max-width: 100%; height: auto; display: block; margin: 10px auto;'
    });
}

// Fonction pour insérer un élément dans l'editor
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
}

// Fonctions utilitaires consolidées
export function setContent(editorId, content) {
    const editor = window.richTextEditors[editorId];
    if (editor) {
        if (content && content.trim()) {
            // Sanitiser le contenu avant de l'injecter
            editor.element.innerHTML = sanitizeHtml(content);
        } else {
            setPlaceholder(editor.element, editor.options.placeholder);
        }
        enhanceDisplay();
    }
}
// Fonction pour obtenir le contenu de l'éditeur
export function getContent(editorId) {
    const editor = window.richTextEditors[editorId];
    if (editor) {
        // Ne pas retourner le placeholder
        if (editor.element.querySelector('.placeholder-text')) {
            return '';
        }
        return editor.element.innerHTML;
    }
    return '';
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

// Fonction pour configurer les gestionnaires d'événements pour les médias
function setupMediaHandlers(element) {
    if (element.tagName === 'IMG') {
        element.addEventListener('load', () => element.classList.add('loaded'));
        element.addEventListener('error', () => element.style.display = 'none');
        if (element.complete) element.classList.add('loaded');
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
            if (mutations.some(m => m.addedNodes.length)) enhanceDisplay();
        }).observe(document.body, { childList: true, subtree: true });
    }
}

// Fonctions utilitaires restantes
export function insertFileFromUrl(editorId, fileUrl, fileName) {
    const editor = window.richTextEditors[editorId];
    if (editor) {
        // Valider l'extension du fichier
        const allowedExtensions = ['.pdf', '.doc', '.docx', '.txt', '.zip'];
        const fileExtension = fileName.substring(fileName.lastIndexOf('.')).toLowerCase();
        
        if (!allowedExtensions.includes(fileExtension)) {
            alert('Type de fichier non autorisé');
            return;
        }
        
        // Échapper le nom du fichier
        const safeFileName = escapeHtml(fileName);
        
        const link = Object.assign(document.createElement('a'), {
            href: fileUrl,
            download: safeFileName || 'fichier',
            textContent: `📎 ${safeFileName || 'Télécharger le fichier'}`,
            style: 'display: inline-block; margin: 5px; padding: 8px 12px; background: #f8f9fa; border: 1px solid #dee2e6; border-radius: 4px; text-decoration: none; color: #495057;'
        });
        
        // Ajouter rel="noopener noreferrer" pour la sécurité
        link.rel = 'noopener noreferrer';
        
        insertIntoEditor(editor, link);
    }
}

// Fonction d'échappement HTML
function escapeHtml(text) {
    const map = {
        '&': '&amp;',
        '<': '&lt;',
        '>': '&gt;',
        '"': '&quot;',
        "'": '&#039;'
    };
    return text.replace(/[&<>"']/g, m => map[m]);
}

// Fonction pour déclencher le clic sur un input de fichier
export function triggerFileInputClick(selector) {
    document.querySelector(selector)?.click();
}

// Fonction pour initialiser les toolbars collantes
export function reinitializeStickyToolbars() {
    stickyToolbars.forEach((data, editorId) => {
        window.removeEventListener('scroll', data.scrollListener);
        data.toolbar.classList.remove('sticky');
        data.editorElement.style.marginTop = '';
    });
    stickyToolbars.clear();
    Object.keys(window.richTextEditors).forEach(initializeStickyToolbar);
}

// Fonction pour insérer un tableau
export function insertTable(editorId) {
    const rows = prompt('Nombre de lignes (1-10):');
    const cols = prompt('Nombre de colonnes (1-10):');

    if (!rows || !cols) return;

    const numRows = parseInt(rows);
    const numCols = parseInt(cols);

    if (isNaN(numRows) || isNaN(numCols) || numRows < 1 || numRows > 10 || numCols < 1 || numCols > 10) {
        alert('Veuillez entrer des nombres valides entre 1 et 10');
        return;
    }

    const table = createTableElement(numRows, numCols);
    const editor = window.richTextEditors[editorId];
    if (editor) {
        insertIntoEditor(editor, table);
    }
}

// Fonction pour créer l'élément tableau
function createTableElement(rows, cols) {
    const table = document.createElement('table');
    table.className = 'table table-bordered table-striped';
    table.style.cssText = 'width: 100%; margin: 10px 0; border-collapse: collapse;';

    // Créer l'en-tête
    const thead = document.createElement('thead');
    const headerRow = document.createElement('tr');

    for (let j = 0; j < cols; j++) {
        const th = document.createElement('th');
        th.innerHTML = `En-tête ${j + 1}`;
        th.style.cssText = 'padding: 8px; background-color: #f8f9fa; border: 1px solid #dee2e6; font-weight: bold;';
        th.contentEditable = true;
        headerRow.appendChild(th);
    }

    thead.appendChild(headerRow);
    table.appendChild(thead);

    // Créer le corps du tableau
    const tbody = document.createElement('tbody');

    for (let i = 0; i < rows; i++) {
        const row = document.createElement('tr');

        for (let j = 0; j < cols; j++) {
            const td = document.createElement('td');
            td.innerHTML = '&nbsp;';
            td.style.cssText = 'padding: 8px; border: 1px solid #dee2e6; min-height: 20px;';
            td.contentEditable = true;
            row.appendChild(td);
        }

        tbody.appendChild(row);
    }

    table.appendChild(tbody);

    // Ajouter les gestionnaires d'événements pour l'édition
    setupTableEditHandlers(table);

    return table;
}

// Fonction pour gérer la navigation dans le tableau
function handleTableNavigation(e, table) {
    const currentCell = e.target;
    if (!currentCell.matches('td, th')) return;

    const cells = Array.from(table.querySelectorAll('td, th'));
    const currentIndex = cells.indexOf(currentCell);

    let targetIndex = currentIndex;

    switch (e.key) {
        case 'Tab':
            e.preventDefault();
            targetIndex = e.shiftKey ? currentIndex - 1 : currentIndex + 1;
            break;
        case 'ArrowRight':
            targetIndex = currentIndex + 1;
            break;
        case 'ArrowLeft':
            targetIndex = currentIndex - 1;
            break;
        case 'ArrowDown':
            const colsCount = table.querySelector('tr').children.length;
            targetIndex = currentIndex + colsCount;
            break;
        case 'ArrowUp':
            const colsCountUp = table.querySelector('tr').children.length;
            targetIndex = currentIndex - colsCountUp;
            break;
    }

    if (targetIndex >= 0 && targetIndex < cells.length) {
        cells[targetIndex].focus();
        e.preventDefault();
    }
}

// Les autres fonctions de tableaux restent inchangées...
function setupTableEditHandlers(table) {
    table.addEventListener('keydown', (e) => handleTableNavigation(e, table));
    table.addEventListener('contextmenu', (e) => {
        e.preventDefault();
        showTableContextMenu(e, table);
    });
}

// Fonction pour afficher le menu contextuel du tableau
function showTableContextMenu(e, table) {
    // Retirer les anciens menus
    document.querySelectorAll('.table-context-menu').forEach(menu => menu.remove());

    const menu = document.createElement('div');
    menu.className = 'table-context-menu';
    menu.style.cssText = `
        position: absolute;
        top: ${e.pageY}px;
        left: ${e.pageX}px;
        background: white;
        border: 1px solid #ccc;
        border-radius: 4px;
        box-shadow: 0 2px 10px rgba(0,0,0,0.1);
        z-index: 1000;
        padding: 5px 0;
        min-width: 150px;
    `;

    const menuItems = [
        { label: 'Ajouter une ligne', action: () => addTableRow(table) },
        { label: 'Ajouter une colonne', action: () => addTableColumn(table) },
        { label: 'Supprimer la ligne', action: () => deleteTableRow(table, e.target) },
        { label: 'Supprimer la colonne', action: () => deleteTableColumn(table, e.target) },
        { label: 'Supprimer le tableau', action: () => deleteTable(table) }
    ];

    menuItems.forEach(item => {
        const menuItem = document.createElement('div');
        menuItem.textContent = item.label;
        menuItem.style.cssText = 'padding: 8px 12px; cursor: pointer; font-size: 14px;';
        menuItem.addEventListener('mouseenter', () => menuItem.style.backgroundColor = '#f5f5f5');
        menuItem.addEventListener('mouseleave', () => menuItem.style.backgroundColor = 'transparent');
        menuItem.addEventListener('click', () => {
            item.action();
            menu.remove();
        });
        menu.appendChild(menuItem);
    });

    document.body.appendChild(menu);

    // Fermer le menu en cliquant ailleurs
    setTimeout(() => {
        document.addEventListener('click', function closeMenu() {
            menu.remove();
            document.removeEventListener('click', closeMenu);
        });
    }, 10);
}

// Fonctions utilitaires pour la manipulation des tableaux
function addTableRow(table) {
    const tbody = table.querySelector('tbody');
    const firstRow = tbody.querySelector('tr');
    const colsCount = firstRow.children.length;

    const newRow = document.createElement('tr');
    for (let i = 0; i < colsCount; i++) {
        const td = document.createElement('td');
        td.innerHTML = '&nbsp;';
        td.style.cssText = 'padding: 8px; border: 1px solid #dee2e6; min-height: 20px;';
        td.contentEditable = true;
        newRow.appendChild(td);
    }

    tbody.appendChild(newRow);
}

// Fonction pour ajouter une colonne au tableau
function addTableColumn(table) {
    const rows = table.querySelectorAll('tr');

    rows.forEach((row, index) => {
        const cell = document.createElement(index === 0 ? 'th' : 'td');
        cell.innerHTML = index === 0 ? 'Nouvelle colonne' : '&nbsp;';
        cell.style.cssText = index === 0 ?
            'padding: 8px; background-color: #f8f9fa; border: 1px solid #dee2e6; font-weight: bold;' :
            'padding: 8px; border: 1px solid #dee2e6; min-height: 20px;';
        cell.contentEditable = true;
        row.appendChild(cell);
    });
}

// Fonctions pour supprimer des lignes, colonnes ou le tableau entier
function deleteTableRow(table, targetCell) {
    const row = targetCell.closest('tr');
    const tbody = table.querySelector('tbody');

    if (tbody.children.length > 1) {
        row.remove();
    } else {
        alert('Impossible de supprimer la dernière ligne');
    }
}

// Fonction pour supprimer une colonne du tableau
function deleteTableColumn(table, targetCell) {
    const cellIndex = Array.from(targetCell.parentElement.children).indexOf(targetCell);
    const rows = table.querySelectorAll('tr');

    if (rows[0].children.length > 1) {
        rows.forEach(row => {
            if (row.children[cellIndex]) {
                row.children[cellIndex].remove();
            }
        });
    } else {
        alert('Impossible de supprimer la dernière colonne');
    }
}

// Fonction pour supprimer le tableau entier
function deleteTable(table) {
    if (confirm('Êtes-vous sûr de vouloir supprimer ce tableau ?')) {
        table.remove();
    }
}

// Ajouter une fonction de sanitisation
function sanitizeHtml(html) {
    // Créer un élément temporaire pour parser le HTML
    const temp = document.createElement('div');
    temp.textContent = html;
    
    // Liste blanche des balises autorisées
    const allowedTags = ['p', 'br', 'strong', 'em', 'u', 'a', 'ul', 'ol', 'li', 'img', 'h1', 'h2', 'h3', 'h4', 'h5', 'h6'];
    const allowedAttributes = {
        'a': ['href', 'title'],
        'img': ['src', 'alt', 'width', 'height']
    };
    
    // Implémenter la logique de nettoyage
    // Utiliser une bibliothèque comme DOMPurify serait idéal
    return html; // Retourner le HTML nettoyé
}

// Modifier la fonction setContent
export function setContent(editorId, content) {
    const editor = window.richTextEditors[editorId];
    if (editor) {
        if (content && content.trim()) {
            // Sanitiser le contenu avant de l'injecter
            editor.element.innerHTML = sanitizeHtml(content);
        } else {
            setPlaceholder(editor.element, editor.options.placeholder);
        }
        enhanceDisplay();
    }
}

// Ajouter un mécanisme pour inclure les tokens CSRF
function getCsrfToken() {
    return document.querySelector('meta[name="csrf-token"]')?.content || '';
}

// Modifier les appels .NET pour inclure le token
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
    }
    // Appeler la méthode .NET avec le contenu et la longueur du texte
    dotNetRef.invokeMethodAsync('OnContentChanged', content, textLength, getCsrfToken());
};

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