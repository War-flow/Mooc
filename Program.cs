using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.EntityFrameworkCore;
using Microsoft.Net.Http.Headers;
using Mooc.Components;
using Mooc.Components.Account;
using Mooc.Data;
using Mooc.Services;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.RateLimiting;
using System.Threading.RateLimiting;


namespace Mooc
{
    public class Program
    {
        public static async Task Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            // Ajouter des services au conteneur
            builder.Services.AddRazorComponents()
                .AddInteractiveServerComponents();

            // Configuration de l'authentification et de l'identité
            builder.Services.AddCascadingAuthenticationState();
            builder.Services.AddScoped<IdentityUserAccessor>();
            builder.Services.AddScoped<IdentityRedirectManager>();
            builder.Services.AddScoped<AuthenticationStateProvider, IdentityRevalidatingAuthenticationStateProvider>();

            // Ajout du service RoleClaimService
            builder.Services.AddScoped<RoleClaimService>();

            // Ajout du service IdentityDataInitializer
            builder.Services.AddScoped<IdentityDataInitializer>();

            // Ajout du service FileUploadService
            builder.Services.AddScoped<FileUploadService>();

            // Ajout du service antivirus (implémentation factice)
            builder.Services.AddScoped<IAntivirusService, NoOpAntivirusService>();

            // Ajout du service BlockService
            builder.Services.AddScoped<BlockService>();

            // Ajout du service CourseStateService
            builder.Services.AddScoped<CourseStateService>();

            // Ajoutez cette ligne dans votre Program.cs avec les autres services
            builder.Services.AddScoped<ISessionCompletionService, SessionCompletionService>();

            // Configuration de la base de données
            var connectionString = builder.Configuration.GetConnectionString("DefaultConnection") ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
            builder.Services.AddDbContextFactory<ApplicationDbContext>(options =>
                options.UseNpgsql(connectionString));

            builder.Services.AddDatabaseDeveloperPageExceptionFilter();

            // Configuration Identity unifiée
            builder.Services.AddIdentity<ApplicationUser, IdentityRole>(options =>
            {
                // Configurer les exigences de mot de passe
                options.Password.RequireDigit = true;
                options.Password.RequireLowercase = true;
                options.Password.RequireNonAlphanumeric = true;
                options.Password.RequireUppercase = true;
                options.Password.RequiredLength = 12; // Augmentation de la longueur minimale
                options.Password.RequiredUniqueChars = 4; // Plus de caractères uniques

                // Configurer le verrouillage d'utilisateur
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15); // Augmentation du délai
                options.Lockout.MaxFailedAccessAttempts = 5;
                options.Lockout.AllowedForNewUsers = true;

                // Configurer les paramètres d'utilisateur
                options.User.AllowedUserNameCharacters =
                    "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789-._@+";
                options.User.RequireUniqueEmail = true;

                // Configuration OBLIGATOIRE pour la confirmation d'email
                options.SignIn.RequireConfirmedEmail = true;
                
                // Empêcher la connexion tant que l'email n'est pas confirmé
                options.SignIn.RequireConfirmedAccount = true;
            })
            .AddEntityFrameworkStores<ApplicationDbContext>()
            .AddSignInManager()
            .AddDefaultTokenProviders()
            .AddPasswordValidator<CompromisedPasswordValidator<ApplicationUser>>(); // NOUVEAU
            // Supprimez .AddDefaultUI() car il n'est pas compatible avec Blazor

            // Ajoutez la configuration des cookies séparément
            builder.Services.ConfigureApplicationCookie(options =>
            {
                options.ExpireTimeSpan = TimeSpan.FromHours(2);
                options.SlidingExpiration = true;
            });

            // Configuration des politiques
            builder.Services.AddAuthorization(options =>
            {
                options.AddPolicy("GérerUtilisateurs", policy =>
                    policy.RequireClaim("Permission", "GérerUtilisateurs"));

                options.AddPolicy("VoirCours", policy =>
                    policy.RequireClaim("Permission", "VoirCours"));

                options.AddPolicy("RépondreQuiz", policy =>
                     policy.RequireClaim("Permission", "RépondreQuiz"));

                options.AddPolicy("InscritSession", policy =>
                    policy.RequireClaim("Permission", "InscritSession"));

                options.AddPolicy("GérerCompte", policy =>
                     policy.RequireClaim("Permission", "GérerCompte"));

                options.AddPolicy("GestionFormation", policy =>
                    policy.RequireClaim("Permission", "GestionFormation"));

                // Ajoutez d'autres politiques selon vos besoins
            });

            // Configuration du cache
            builder.Services.AddResponseCaching();
            builder.Services.AddMemoryCache();

            // Configuration des services additionnels
            builder.Services.Configure<EmailSettings>(builder.Configuration.GetSection(EmailSettings.SectionName));
            
            // IMPORTANT: Enregistrer le service d'email AVANT d'autres services qui pourraient l'utiliser
            builder.Services.AddScoped<IEmailSender<ApplicationUser>, EmailSender>();
            
            builder.Services.AddSingleton<IUserCacheService, UserCacheService>();

            // Configuration conditionnelle de la compression
            if (!builder.Environment.IsDevelopment())
            {
                builder.Services.AddResponseCompression(options =>
                {
                    options.Providers.Add<BrotliCompressionProvider>();
                    options.Providers.Add<GzipCompressionProvider>();
                    options.MimeTypes = new[]
                    {
                        "text/css",
                        "text/html", 
                        "text/javascript",
                        "application/javascript",
                        "application/wasm",
                        "application/font-woff2",
                        "image/svg+xml"
                    };
                    options.EnableForHttps = true;
                    options.ExcludedMimeTypes = new[] {
                        "application/octet-stream",
                        "application/json",
                        "application/json; charset=utf-8"
                    };
                });
            }

            // Amélioration de la sérialisation JSON
            builder.Services.AddControllers()
                .AddJsonOptions(options =>
                {
                    options.JsonSerializerOptions.PropertyNamingPolicy = null;
                    options.JsonSerializerOptions.ReferenceHandler = ReferenceHandler.IgnoreCycles;
                });

            builder.Services.AddHealthChecks();

            // Ajouter le logging et la configuration
            builder.Services.AddLogging(builder =>
            {
                builder.AddConsole();
                // builder.AddFile("logs/security-{Date}.log"); // NLog ou Serilog
            });

            // Configuration des notifications
            builder.Services.AddScoped<INotificationService, NotificationService>();
            builder.Services.AddHostedService<SessionExpiryService>();

            // Ajout des services SignalR
            builder.Services.AddSignalR(options =>
            {
                options.EnableDetailedErrors = builder.Environment.IsDevelopment();
                options.ClientTimeoutInterval = TimeSpan.FromSeconds(30);
                options.HandshakeTimeout = TimeSpan.FromSeconds(15);
                options.MaximumReceiveMessageSize = 32 * 1024; // 32KB max
            });

            // Ajout du service d'erreurs
            builder.Services.AddScoped<IErrorHandlingService, ErrorHandlingService>();

            // Ajout du service de génération de certificats
            builder.Services.AddScoped<ICertificateGenerationService, CertificateGenerationService>();

            // Ajout du service AutomaticCertificateService
            builder.Services.AddScoped<IAutomaticCertificateService, AutomaticCertificateService>();
            
            builder.Services.Configure<CertificateGenerationOptions>(builder.Configuration.GetSection("CertificateGeneration"));

            // Ajouter HtmlAgilityPack pour la validation HTML
            builder.Services.AddScoped<IContentValidationService, ContentValidationService>();
            
            builder.Services.Configure<ContentValidationSettings>(
                builder.Configuration.GetSection("ContentValidation"));

            builder.Services.AddHttpClient("ValidationClient", client =>
            {
                client.DefaultRequestHeaders.Add("User-Agent", "Mooc-ContentValidator/1.0");
            });

            // Configuration de sécurité renforcée
            builder.Services.Configure<FormOptions>(options =>
            {
                options.ValueLengthLimit = 100000; // 100KB pour les champs de formulaire
                options.MultipartBodyLengthLimit = 5 * 1024 * 1024; // 5MB pour les fichiers
            });

            // Ajouter la protection CSRF
            builder.Services.AddAntiforgery(options =>
            {
                options.HeaderName = "X-CSRF-TOKEN";
                options.Cookie.Name = "__RequestVerificationToken";
                options.Cookie.SameSite = Microsoft.AspNetCore.Http.SameSiteMode.Strict;
                options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
                options.Cookie.HttpOnly = true;
            });

            // Rate limiting pour éviter les attaques DoS
            builder.Services.AddRateLimiter(options =>
            {
                // Limite globale
                options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
                    RateLimitPartition.GetFixedWindowLimiter(
                        partitionKey: GetClientIdentifier(context),
                        factory: _ => new FixedWindowRateLimiterOptions
                        {
                            PermitLimit = 100,
                            Window = TimeSpan.FromMinutes(1)
                        }));
                
                // Limite pour les uploads
                options.AddPolicy("FileUpload", context =>
                    RateLimitPartition.GetFixedWindowLimiter(
                        partitionKey: GetClientIdentifier(context),
                        factory: _ => new FixedWindowRateLimiterOptions
                        {
                            PermitLimit = 5, // 5 uploads par minute max
                            Window = TimeSpan.FromMinutes(1)
                        }));
                
                // Limite pour l'authentification
                options.AddPolicy("Authentication", context =>
                    RateLimitPartition.GetFixedWindowLimiter(
                        partitionKey: GetClientIdentifier(context),
                        factory: _ => new FixedWindowRateLimiterOptions
                        {
                            PermitLimit = 3, // 3 tentatives par 5 minutes
                            Window = TimeSpan.FromMinutes(5)
                        }));
            });

            // Ajout du HttpClient pour la vérification des mots de passe
            builder.Services.AddHttpClient();

            var app = builder.Build();

            // Configuration du pipeline de requêtes HTTP
            if (app.Environment.IsDevelopment())
            {
                app.UseMigrationsEndPoint();
                app.UseExceptionHandler("/Error", createScopeForErrors: true);
                
                app.UseWebSockets(new WebSocketOptions
                {
                    KeepAliveInterval = TimeSpan.FromMinutes(2),
                });
                
                // Pas de compression en développement pour éviter les conflits avec Browser Link
            }
            else
            {
                app.UseExceptionHandler("/Error", createScopeForErrors: true);
                app.UseHsts();
                app.UseWebSockets();
                
                // Compression uniquement en production
                app.UseResponseCompression();
            }

            app.UseHttpsRedirection();
            app.UseStaticFiles(new StaticFileOptions
            {
                OnPrepareResponse = ctx =>
                {
                    // Cache statique pour améliorer les performances
                    const int durationInSeconds = 60 * 60 * 24 * 7; // 7 jours
                    ctx.Context.Response.Headers[HeaderNames.CacheControl] =
                        "public,max-age=" + durationInSeconds;
                }
            });

            app.UseResponseCaching();
            app.UseAntiforgery();
            app.UseRateLimiter(); // Ajout de la protection contre les surcharges

            // ✅ AJOUTER : Middleware de sécurité
            app.Use(async (context, next) =>
            {
                // Headers de sécurité
                context.Response.Headers.Append("X-Content-Type-Options", "nosniff");
                context.Response.Headers.Append("X-Frame-Options", "DENY");
                context.Response.Headers.Append("X-XSS-Protection", "1; mode=block");
                context.Response.Headers.Append("Referrer-Policy", "strict-origin-when-cross-origin");
                context.Response.Headers.Append("Permissions-Policy", "geolocation=(), microphone=(), camera=()");
                
                await next();
            });

            app.MapRazorComponents<App>()
                .AddInteractiveServerRenderMode();

            // Ajout des endpoints pour Identity
            app.MapAdditionalIdentityEndpoints();

            // Ajout d'un health check
            app.MapHealthChecks("/health");

            // Ajout des hubs SignalR
            app.MapHub<SessionHub>("/sessionHub").RequireAuthorization();

            using (var scope = app.Services.CreateScope())
            {
                var initializer = scope.ServiceProvider.GetRequiredService<IdentityDataInitializer>();
                await initializer.InitializeAsync();
            }

            await app.RunAsync();
        }

        private static string GetClientIdentifier(HttpContext context)
        {
            return context.User?.Identity?.Name ?? 
                   context.Connection.RemoteIpAddress?.ToString() ?? 
                   "anonymous";
        }
    }
        
}

// ✅ AJOUTER : Service de validation des mots de passe compromis
public class CompromisedPasswordValidator<TUser> : IPasswordValidator<TUser> where TUser : class
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<CompromisedPasswordValidator<TUser>> _logger;

    public CompromisedPasswordValidator(IHttpClientFactory httpClientFactory, ILogger<CompromisedPasswordValidator<TUser>> logger)
    {
        _httpClient = httpClientFactory.CreateClient();
        _httpClient.Timeout = TimeSpan.FromSeconds(5); // Timeout de 5 secondes
        _logger = logger;
    }

    public async Task<IdentityResult> ValidateAsync(UserManager<TUser> manager, TUser user, string password)
    {
        // Vérifier contre une liste de mots de passe compromis (Have I Been Pwned API)
        var isCompromised = await CheckPasswordBreachAsync(password);
        if (isCompromised)
        {
            return IdentityResult.Failed(new IdentityError 
            { 
                Code = "CompromisedPassword", 
                Description = "Ce mot de passe a été compromis dans une fuite de données. Veuillez en choisir un autre." 
            });
        }
        return IdentityResult.Success;
    }

    private async Task<bool> CheckPasswordBreachAsync(string password)
    {
        try
        {
            // Calculer le hash SHA-1 du mot de passe
            using var sha1 = System.Security.Cryptography.SHA1.Create();
            var hashBytes = sha1.ComputeHash(System.Text.Encoding.UTF8.GetBytes(password));
            var hash = Convert.ToHexString(hashBytes);

            // Prendre les 5 premiers caractères pour l'API k-anonymity
            var prefix = hash[..5];
            var suffix = hash[5..];

            // Appeler l'API Have I Been Pwned avec le préfixe
            var response = await _httpClient.GetAsync($"https://api.pwnedpasswords.com/range/{prefix}");
            
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Échec de la vérification du mot de passe compromis. Statut: {StatusCode}", response.StatusCode);
                return false; // En cas d'erreur, autoriser le mot de passe
            }

            var content = await response.Content.ReadAsStringAsync();
            
            // Vérifier si le suffixe apparaît dans la réponse
            var lines = content.Split('\n');
            foreach (var line in lines)
            {
                if (line.StartsWith(suffix, StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogInformation("Mot de passe compromis détecté");
                    return true; // Mot de passe compromis
                }
            }

            return false; // Mot de passe non compromis
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur lors de la vérification du mot de passe compromis");
            return false; // En cas d'erreur, autoriser le mot de passe
        }
    }
}