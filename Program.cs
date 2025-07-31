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

            // Ajout du service BlockService
            builder.Services.AddScoped<BlockService>();

            // Ajout du service CourseStateService
            builder.Services.AddScoped<CourseStateService>();

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

                // Ne pas inclure les configurations Cookies ici
            })
            .AddEntityFrameworkStores<ApplicationDbContext>()
            .AddSignInManager()
            .AddDefaultTokenProviders();
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
            builder.Services.AddSingleton<IEmailSender<ApplicationUser>, IdentityNoOpEmailSender>();
            builder.Services.AddSingleton<IUserCacheService, UserCacheService>();

            // Ajout de la compression des réponses
            builder.Services.AddResponseCompression(options =>
            {
                options.Providers.Add<BrotliCompressionProvider>();
                options.Providers.Add<GzipCompressionProvider>();

                // Inclure uniquement les types MIME spécifiques pour les fichiers statiques
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

                // Exclure clairement tous les types MIME WebSocket
                options.ExcludedMimeTypes = new[] {
                    "application/octet-stream",
                    "application/json",
                    "application/json; charset=utf-8"
                };
            });

            // Amélioration de la sérialisation JSON
            builder.Services.AddControllers()
                .AddJsonOptions(options =>
                {
                    options.JsonSerializerOptions.PropertyNamingPolicy = null;
                    options.JsonSerializerOptions.ReferenceHandler = ReferenceHandler.IgnoreCycles;
                });

            builder.Services.AddHealthChecks();

            // Ajouter le logging et la configuration
            builder.Services.AddLogging();

            // Configuration des notifications
            builder.Services.AddScoped<INotificationService, NotificationService>();
            builder.Services.AddHostedService<SessionExpiryService>();

            // Ajout des services SignalR
            builder.Services.AddSignalR();

            var app = builder.Build();

            // Configuration du pipeline de requêtes HTTP
            if (app.Environment.IsDevelopment())
            {
                app.UseMigrationsEndPoint();

                // Configuration spécifique aux WebSockets pour le développement
                app.UseWebSockets(new WebSocketOptions
                {
                    KeepAliveInterval = TimeSpan.FromMinutes(2),
                    // Dans .NET 9, ReceiveBufferSize est obsolète et a été supprimé
                });

            }
            else
            {
                app.UseExceptionHandler("/Error", createScopeForErrors: true);
                app.UseHsts();

                // Configuration plus simple pour la production
                app.UseWebSockets();
            }

            // Important: ResponseCompression après WebSockets
            app.UseResponseCompression();
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

            app.MapRazorComponents<App>()
                .AddInteractiveServerRenderMode();

            // Ajout des endpoints pour Identity
            app.MapAdditionalIdentityEndpoints();

            // Ajout d'un health check
            app.MapHealthChecks("/health");

            // Ajout des hubs SignalR
            app.MapHub<SessionHub>("/sessionHub");

            using (var scope = app.Services.CreateScope())
            {
                var initializer = scope.ServiceProvider.GetRequiredService<IdentityDataInitializer>();
                await initializer.InitializeAsync();
            }

            await app.RunAsync();
        }
    }
        
}