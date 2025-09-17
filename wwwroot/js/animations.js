// Intersection Observer pour animations au scroll
document.addEventListener('DOMContentLoaded', function() {
    // Animations au scroll
    const observerOptions = {
        threshold: 0.1,
        rootMargin: '0px 0px -50px 0px'
    };

    const observer = new IntersectionObserver((entries) => {
        entries.forEach(entry => {
            if (entry.isIntersecting) {
                entry.target.classList.add('is-visible');
            }
        });
    }, observerOptions);

    // Observer tous les éléments avec la classe fade-in-on-scroll
    document.querySelectorAll('.fade-in-on-scroll').forEach(el => {
        observer.observe(el);
    });

    // Animation des compteurs
    const animateCounters = () => {
        document.querySelectorAll('.counter').forEach(counter => {
            const target = parseInt(counter.getAttribute('data-target'));
            const current = parseInt(counter.innerText);
            const increment = target / 100;
            
            if (current < target) {
                counter.innerText = Math.ceil(current + increment);
                setTimeout(animateCounters, 10);
            } else {
                counter.innerText = target;
            }
        });
    };

    // Démarrer l'animation des compteurs quand ils sont visibles
    const counterObserver = new IntersectionObserver((entries) => {
        entries.forEach(entry => {
            if (entry.isIntersecting) {
                animateCounters();
                counterObserver.unobserve(entry.target);
            }
        });
    });

    document.querySelectorAll('.counter').forEach(counter => {
        counterObserver.observe(counter);
    });

    // Effet ripple sur les boutons
    document.querySelectorAll('.btn-ripple').forEach(button => {
        button.addEventListener('click', function(e) {
            const ripple = document.createElement('span');
            const rect = this.getBoundingClientRect();
            const size = Math.max(rect.width, rect.height);
            const x = e.clientX - rect.left - size / 2;
            const y = e.clientY - rect.top - size / 2;
            
            ripple.style.width = ripple.style.height = size + 'px';
            ripple.style.left = x + 'px';
            ripple.style.top = y + 'px';
            ripple.classList.add('ripple');
            
            this.appendChild(ripple);
            
            setTimeout(() => {
                ripple.remove();
            }, 600);
        });
    });

    // Parallax simple pour les héros
    window.addEventListener('scroll', () => {
        const scrolled = window.pageYOffset;
        const parallaxElements = document.querySelectorAll('.parallax');
        
        parallaxElements.forEach(element => {
            const speed = element.dataset.speed || 0.5;
            element.style.transform = `translateY(${scrolled * speed}px)`;
        });
    });

    // Animation des barres de progression
    const progressBars = document.querySelectorAll('.progress-bar[data-width]');
    const progressObserver = new IntersectionObserver((entries) => {
        entries.forEach(entry => {
            if (entry.isIntersecting) {
                const bar = entry.target;
                const width = bar.getAttribute('data-width');
                bar.style.width = width + '%';
                progressObserver.unobserve(bar);
            }
        });
    });

    progressBars.forEach(bar => {
        progressObserver.observe(bar);
    });
});

// Fonction utilitaire pour ajouter des animations aux nouveaux éléments
window.addAnimations = function(element) {
    const animatedElements = element.querySelectorAll('.fade-in-on-scroll');
    animatedElements.forEach(el => {
        if (observer) observer.observe(el);
    });
};

// Gestionnaire d'erreurs avec animation
window.showErrorWithAnimation = function(element) {
    element.classList.add('glitch');
    setTimeout(() => {
        element.classList.remove('glitch');
    }, 300);
};

// Animation de succès
window.showSuccessAnimation = function(element) {
    element.classList.add('success-checkmark');
    setTimeout(() => {
        element.classList.remove('success-checkmark');
    }, 600);
};