// Stockage des instances d'éditeurs
window.editors = {};

// Initialisation de l'éditeur riche avec gestion d'événements
window.initRichEditor = function (elementId, content) {
    // Si l'éditeur existe déjà, mettre à jour son contenu
    if (window.editors[elementId]) {
        window.editors[elementId].root.innerHTML = content;
        return;
    }

    // Attendre que l'élément DOM soit disponible
    const element = document.getElementById(elementId);
    if (!element) {
        console.error(`Élément #${elementId} non trouvé`);
        return;
    }

    // Options de l'éditeur Quill
    const options = {
        theme: 'snow',
        modules: {
            toolbar: [
                ['bold', 'italic', 'underline', 'strike'],
                [{ 'header': 1 }, { 'header': 2 }],
                [{ 'size': ['small', false, 'large', 'huge'] }], // Nouvelle ligne pour la taille
                [{ 'list': 'ordered' }, { 'list': 'bullet' }],
                [{ 'indent': '-1' }, { 'indent': '+1' }],
                [{ 'color': [] }, { 'background': [] }],
            ]
        },
        placeholder: 'Rédiger votre texte'
    };

    // Créer l'instance Quill
    window.editors[elementId] = new Quill(`#${elementId}`, options);

    // Définir le contenu initial
    if (content) {
        window.editors[elementId].root.innerHTML = content;
    }

    // Attacher l'événement de changement avec debounce pour limiter les appels
    let debounceTimer;
    window.editors[elementId].on('text-change', function () {
        clearTimeout(debounceTimer);
        debounceTimer = setTimeout(function () {
            const content = window.getRichEditorContent(elementId);
            // Appeler la méthode .NET avec le contenu mis à jour
            DotNet.invokeMethodAsync('Mooc', 'OnEditorContentChanged', elementId, content);
        }, 300); // Attendre 300ms après la dernière modification
    });
};

// Récupérer le contenu HTML de l'éditeur
window.getRichEditorContent = function (elementId) {
    if (window.editors && window.editors[elementId]) {
        return window.editors[elementId].root.innerHTML;
    }
    return "";
};

// Détruire une instance d'éditeur
window.destroyRichEditor = function (elementId) {
    if (window.editors && window.editors[elementId]) {
        delete window.editors[elementId];
    }
};

// Activer/désactiver l'édition
window.setRichEditorReadOnly = function (elementId, isReadOnly) {
    if (window.editors && window.editors[elementId]) {
        window.editors[elementId].enable(!isReadOnly);
    }
};

// Vérifier si un éditeur existe déjà
window.editorExists = function (elementId) {
    return window.editors && window.editors[elementId] ? true : false;
};