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

    // Ajouter le placeholder seulement si vraiment vide
    if (!element.innerHTML.trim() || element.innerHTML === '<br>') {
        element.innerHTML = `<div style="color: #6c757d; font-style: italic;">${options.placeholder}</div>`;
    }

    // Gestionnaire d'événements amélioré
    function handleInput() {
        let content = element.innerHTML;
        const textLength = element.textContent.length;

        // Vérifier si le contenu contient des images ou du HTML riche
        const hasRichContent = content.includes('<img') || 
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

        // Ne pas limiter la longueur si c'est du contenu HTML riche (images, etc.)
        if (textLength > options.maxLength && !hasRichContent) {
            const textContent = element.textContent.substring(0, options.maxLength);
            element.textContent = textContent;
            return;
        }

        // Log pour debug
        if (content.includes('<img')) {
            console.log('📷 Contenu avec image détecté et envoyé à Blazor');
        }

        dotNetRef.invokeMethodAsync('OnContentChanged', content, textLength);
    }

    function handleFocus() {
        if (element.innerHTML.includes(options.placeholder)) {
            element.innerHTML = '';
        }
    }

    function handleBlur() {
        // Ne remettre le placeholder que si vraiment vide (pas d'images)
        if (!element.textContent.trim() && !element.innerHTML.includes('<img')) {
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

export function insertVideo(editorId) {
    const url = prompt("URL de la vidéo (mp4, webm, etc.) :");
    if (url) {
        const videoHtml = `<video controls style="max-width:100%"><source src="${url}" type="video/mp4"></video>`;
        document.getElementById(editorId).focus();
        document.execCommand('insertHTML', false, videoHtml);
    }
}

export function insertAudio(editorId) {
    const url = prompt("URL de l'audio (mp3, ogg, etc.) :");
    if (url) {
        const audioHtml = `<audio controls style="width:100%"><source src="${url}" type="audio/mpeg"></audio>`;
        document.getElementById(editorId).focus();
        document.execCommand('insertHTML', false, audioHtml);
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

export function setContent(editorId, content) {
    const editor = window.richTextEditors[editorId];
    if (editor) {
        editor.element.innerHTML = content;
        // Améliorer immédiatement l'affichage des images
        enhanceImageDisplay();
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

// Appeler automatiquement
if (typeof window !== 'undefined') {
    window.addEventListener('DOMContentLoaded', enhanceImageDisplay);
    
    // Observer les changements dans le DOM pour les images ajoutées dynamiquement
    if (window.MutationObserver) {
        const observer = new MutationObserver(function(mutations) {
            mutations.forEach(function(mutation) {
                if (mutation.addedNodes) {
                    mutation.addedNodes.forEach(function(node) {
                        if (node.nodeType === 1) { // Element node
                            if (node.tagName === 'IMG' || (node.querySelector && node.querySelector('img'))) {
                                enhanceImageDisplay();
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
}