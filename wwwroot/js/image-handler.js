// Gestionnaire d'images amélioré pour prévenir les corruptions
window.ImageHandler = {
    setupAutoResize: function(imgElement) {
        if (!imgElement) return;
        
        // ⭐ Ajouter un délai avant le traitement pour s'assurer que l'image est prête
        setTimeout(() => {
            imgElement.style.maxWidth = '100%';
            imgElement.style.height = 'auto';
            imgElement.style.display = 'block';
            
            // ⭐ Gestionnaire d'erreur amélioré
            imgElement.onerror = function(e) {
                console.error('Erreur de chargement d\'image:', this.src, e);
                
                this.style.display = 'none';
                
                // ⭐ Créer un message d'erreur informatif
                const errorDiv = document.createElement('div');
                errorDiv.className = 'image-error';
                errorDiv.style.cssText = `
                    border: 2px dashed #dc3545;
                    background-color: #f8d7da;
                    color: #721c24;
                    text-align: center;
                    padding: 20px;
                    border-radius: 4px;
                    margin: 10px 0;
                `;
                errorDiv.innerHTML = `
                    <i class="bi bi-exclamation-triangle" style="font-size: 24px; display: block; margin-bottom: 10px;"></i>
                    <strong>Image non disponible</strong><br>
                    <small>L'image est corrompue ou inaccessible</small>
                `;
                
                // Remplacer l'image par le message d'erreur
                if (this.parentNode) {
                    this.parentNode.insertBefore(errorDiv, this);
                    this.parentNode.removeChild(this);
                }
            };
            
            // ⭐ Gestionnaire de chargement réussi
            imgElement.onload = function() {
                console.log('Image chargée avec succès:', this.src);
                this.style.display = 'block';
                
                const placeholder = this.parentNode?.querySelector('.image-placeholder');
                if (placeholder) {
                    placeholder.style.display = 'none';
                }
                
                // ⭐ Marquer l'image comme validée
                this.classList.add('loaded', 'validated');
            };
            
            // ⭐ Vérifier si l'image est déjà chargée
            if (imgElement.complete) {
                if (imgElement.naturalWidth > 0) {
                    imgElement.onload();
                } else {
                    imgElement.onerror();
                }
            }
        }, 100);
    },
    
    initializeAllImages: function() {
        console.log('Initialisation de toutes les images...');
        
        // ⭐ Sélectionner spécifiquement les images qui ne sont pas encore traitées
        const images = document.querySelectorAll('img:not(.validated):not(.error)');
        console.log(`${images.length} images à traiter`);
        
        images.forEach((img, index) => {
            // ⭐ Traiter les images avec un délai pour éviter la surcharge
            setTimeout(() => {
                this.setupAutoResize(img);
            }, index * 50); // Délai échelonné
        });
    },
    
    // ⭐ NOUVELLE FONCTION : Validation d'image avant affichage
    validateAndDisplayImage: function(imgSrc, container) {
        const img = new Image();
        
        img.onload = function() {
            console.log('Image validée:', imgSrc);
            if (container) {
                container.innerHTML = '';
                container.appendChild(this);
                window.ImageHandler.setupAutoResize(this);
            }
        };
        
        img.onerror = function() {
            console.error('Image invalide:', imgSrc);
            if (container) {
                container.innerHTML = `
                    <div class="image-error" style="
                        border: 2px dashed #dc3545;
                        background-color: #f8d7da;
                        color: #721c24;
                        text-align: center;
                        padding: 20px;
                        border-radius: 4px;
                    ">
                        <i class="bi bi-exclamation-triangle"></i>
                        <br>Image corrompue
                    </div>
                `;
            }
        };
        
        img.src = imgSrc;
    },
    
    previewImage: function(input, previewContainer) {
        if (input.files && input.files[0]) {
            const file = input.files[0];
            
            // ⭐ Validation du fichier avant lecture
            if (!file.type.startsWith('image/')) {
                console.error('Le fichier n\'est pas une image:', file.type);
                return;
            }
            
            if (file.size > 3 * 1024 * 1024) { // 3MB
                console.error('Fichier trop volumineux:', file.size);
                return;
            }
            
            const reader = new FileReader();
            
            reader.onload = function(e) {
                // ⭐ Validation de l'URL de données avant affichage
                const result = e.target.result;
                if (result && result.startsWith('data:image/')) {
                    window.ImageHandler.validateAndDisplayImage(result, previewContainer);
                    
                    // Masquer le placeholder
                    const placeholder = previewContainer.querySelector('.image-placeholder');
                    if (placeholder) {
                        placeholder.style.display = 'none';
                    }
                } else {
                    console.error('Données d\'image invalides');
                }
            };
            
            reader.onerror = function() {
                console.error('Erreur lors de la lecture du fichier');
            };
            
            reader.readAsDataURL(file);
        }
    }
};

// ⭐ Initialisation automatique améliorée
document.addEventListener('DOMContentLoaded', function() {
    console.log('DOM chargé, initialisation des images...');
    window.ImageHandler.initializeAllImages();
});

// ⭐ Réinitialiser après les mises à jour Blazor
window.addEventListener('blazor:enhancedload', function() {
    console.log('Blazor enhanced load, réinitialisation des images...');
    setTimeout(() => {
        window.ImageHandler.initializeAllImages();
    }, 200);
});

// ⭐ Observer les changements du DOM pour traiter les nouvelles images
if (window.MutationObserver) {
    const observer = new MutationObserver(function(mutations) {
        let hasNewImages = false;
        
        mutations.forEach(function(mutation) {
            if (mutation.type === 'childList') {
                mutation.addedNodes.forEach(function(node) {
                    if (node.nodeType === Node.ELEMENT_NODE) {
                        if (node.tagName === 'IMG' || node.querySelector && node.querySelector('img')) {
                            hasNewImages = true;
                        }
                    }
                });
            }
        });
        
        if (hasNewImages) {
            setTimeout(() => {
                window.ImageHandler.initializeAllImages();
            }, 100);
        }
    });
    
    observer.observe(document.body, {
        childList: true,
        subtree: true
    });
}