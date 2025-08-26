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
        margin: '10px 0' 
    });
    container.appendChild(audio);
    
    return container;
}

// Création d'un élément embed optimisé
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

// Création d'un élément media wrapper optimisé
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

// Fonction pour créer un élément image
function createImageElement(url) {
    return Object.assign(document.createElement('img'), {
        src: url, alt: 'Image insérée', loading: 'lazy',
        style: 'max-width: 100%; height: auto; display: block; margin: 10px auto;'
    });
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

// Fonction récursive pour nettoyer les nœuds
function cleanNode(node) {
    // Traiter les nœuds enfants d'abord (en sens inverse pour éviter les problèmes d'index)
    for (let i = node.childNodes.length - 1; i >= 0; i--) {
        const child = node.childNodes[i];
        
        if (child.nodeType === Node.ELEMENT_NODE) {
            const tagName = child.tagName.toLowerCase();
            
            // Vérifier si la balise est autorisée
            const allowedTags = ['p', 'br', 'strong', 'em', 'u', 'a', 'ul', 'ol', 'li', 'img', 'h1', 'h2', 'h3', 'h4', 'h5', 'h6', 'b', 'i', 'div', 'iframe', 'font', 'span', 'table', 'thead', 'tbody', 'tr', 'td', 'th', 'video', 'audio', 'source', 'strike', 'blockquote', 'pre', 'code'];
            
            if (!allowedTags.includes(tagName)) {
                // Remplacer la balise non autorisée par son contenu texte
                const textNode = document.createTextNode(child.textContent);
                node.replaceChild(textNode, child);
            } else {
                // Nettoyer les attributs
                const allowedAttributes = {
                    'a': ['href', 'title', 'target', 'rel', 'class', 'style'],
                    'img': ['src', 'alt', 'width', 'height', 'loading', 'style', 'class'],
                    'iframe': ['src', 'width', 'height', 'frameborder', 'allowfullscreen', 'style', 'class', 'title', 'allow'],
                    'video': ['src', 'controls', 'width', 'height', 'preload', 'style', 'class', 'poster', 'autoplay', 'loop', 'muted'],
                    'audio': ['src', 'controls', 'preload', 'style', 'class', 'autoplay', 'loop', 'muted'],
                    'source': ['src', 'type'],
                    'div': ['class', 'style', 'data-video-element', 'data-preserved-video', 'align', 'id'],
                    'span': ['class', 'style'],
                    'table': ['class', 'style', 'border', 'cellpadding', 'cellspacing'],
                    'td': ['style', 'contenteditable', 'class', 'colspan', 'rowspan'],
                    'th': ['style', 'contenteditable', 'class', 'colspan', 'rowspan'],
                    'p': ['class', 'style', 'align'],
                    'font': ['color', 'size', 'face', 'style'],
                    'blockquote': ['class', 'style', 'cite'],
                    'strong': ['class', 'style'],
                    'em': ['class', 'style'],
                    'b': ['class', 'style'],
                    'i': ['class', 'style'],
                    'u': ['class', 'style'],
                    'strike': ['class', 'style'],
                    'h1': ['class', 'style'],
                    'h2': ['class', 'style'],
                    'h3': ['class', 'style'],
                    'h4': ['class', 'style'],
                    'h5': ['class', 'style'],
                    'h6': ['class', 'style']
                };
                
                const allowedAttrs = allowedAttributes[tagName] || [];
                const attrs = Array.from(child.attributes);
                
                attrs.forEach(attr => {
                    if (!allowedAttrs.includes(attr.name)) {
                        child.removeAttribute(attr.name);
                    } else {
                        // Valider les valeurs des attributs
                        const cleanValue = sanitizeAttributeValue(attr.name, attr.value, tagName);
                        if (cleanValue !== false) {
                            child.setAttribute(attr.name, cleanValue);
                        } else {
                            child.removeAttribute(attr.name);
                        }
                    }
                });
                
                // Nettoyer récursivement les enfants
                cleanNode(child);
            }
        }
    }
}

// Fonction pour valider et nettoyer les valeurs d'attributs
function sanitizeAttributeValue(attrName, value, tagName) {
    switch (attrName) {
        case 'href':
        case 'src':
            // Valider les URLs
            try {
                const url = new URL(value, window.location.href);
                // Autoriser uniquement certains protocoles
                const allowedProtocols = ['http:', 'https:', 'mailto:', 'tel:'];
                
                // Pour les iframes de vidéo, autoriser seulement les domaines de confiance
                if (tagName === 'iframe') {
                    const trustedVideoHosts = [
                        'youtube.com', 'www.youtube.com',
                        'vimeo.com', 'player.vimeo.com',
                        'dailymotion.com', 'www.dailymotion.com'
                    ];
                    if (!trustedVideoHosts.some(host => url.hostname.includes(host))) {
                        return false;
                    }
                }
                
                if (!allowedProtocols.includes(url.protocol)) {
                    return false;
                }
                return value;
            } catch {
                return false;
            }
            
        case 'style':
            // Nettoyer les styles dangereux
            return sanitizeStyles(value);
            
        case 'target':
            // Autoriser seulement _blank et _self
            return ['_blank', '_self'].includes(value) ? value : '_self';
            
        case 'rel':
            // Forcer noopener noreferrer pour les liens externes
            return 'noopener noreferrer';
            
        default:
            return value;
    }
}

// Fonction pour nettoyer les styles CSS (améliorer la fonction existante)
function sanitizeStyles(styleString) {
    const allowedProperties = [
        'color', 'background-color', 'font-size', 'font-weight', 'font-style',
        'text-align', 'text-decoration', 'padding', 'margin', 'border',
        'width', 'height', 'max-width', 'max-height', 'display', 'position',
        'top', 'left', 'right', 'bottom', 'float', 'clear',
        // Ajouter plus de propriétés de couleur
        'border-color', 'outline-color', 'text-shadow', 'box-shadow'
    ];
    
    const styles = styleString.split(';').filter(s => s.trim());
    const cleanedStyles = [];
    
    styles.forEach(style => {
        const [property, value] = style.split(':').map(s => s.trim());
        
        if (property && value && allowedProperties.includes(property)) {
            // Vérifier que la valeur ne contient pas de JavaScript
            if (!value.includes('javascript:') && !value.includes('expression(')) {
                cleanedStyles.push(`${property}: ${value}`);
            }
        }
    });
    
    return cleanedStyles.join('; ');
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