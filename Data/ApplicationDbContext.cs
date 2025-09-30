using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Mooc.Data.Converters;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion; // ⭐ AJOUTÉ


namespace Mooc.Data
{
    public class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : IdentityDbContext<ApplicationUser>(options)
    {
        public DbSet<Session> Sessions { get; set; } = null!;
        public DbSet<Cours> Courses { get; set; } = null!;
        public DbSet<CourseProgress> CourseProgresses { get; set; } = null!;
        public DbSet<Certificate> Certificates { get; set; } = null!;
        public DbSet<PreRegistration> PreRegistrations { get; set; } = null!; // NOUVEAU
        public DbSet<EnrollmentHistory> EnrollmentHistories { get; set; } = null!;
        public DbSet<CourseBadge> CourseBadges { get; set; } = null!;

        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);

            // ⭐ NOUVEAU : Configuration globale pour convertir automatiquement les DateTime en UTC
            foreach (var entityType in builder.Model.GetEntityTypes())
            {
                foreach (var property in entityType.GetProperties())
                {
                    if (property.ClrType == typeof(DateTime) || property.ClrType == typeof(DateTime?))
                    {
                        property.SetValueConverter(new ValueConverter<DateTime, DateTime>(
                            v => v.Kind == DateTimeKind.Utc ? v : DateTime.SpecifyKind(v, DateTimeKind.Utc),
                            v => DateTime.SpecifyKind(v, DateTimeKind.Utc)));
                    }
                }
            }

            // Configuration de Session
            builder.Entity<Session>(entity =>
            {
                entity.ToTable("Session");
                
                entity.Property(e => e.Title)
                      .IsRequired()
                      .HasColumnType("varchar")
                      .HasMaxLength(100);
                      
                entity.Property(e => e.Image)
                      .HasColumnType("varchar")
                      .IsRequired();
                      
                entity.Property(e => e.Description)
                      .HasColumnType("varchar")
                      .HasMaxLength(300);
                      
                // ⭐ CORRECTION : Utiliser timestamp avec time zone pour les dates
                entity.Property(e => e.StartDate)
                      .HasColumnType("timestamp with time zone");
                      
                entity.Property(e => e.EndDate)
                      .HasColumnType("timestamp with time zone");
                      
                entity.Property(e => e.Work)
                      .HasColumnType("int");
                     
                entity.Property(e => e.IsActive)
                      .HasColumnType("boolean")
                      .HasDefaultValue(true);
                
                // Configuration de la relation one-to-one avec le créateur
                entity.HasOne(s => s.User)
                      .WithMany()
                      .HasForeignKey(s => s.UserId)
                      .OnDelete(DeleteBehavior.SetNull);
                
                // Relation one-to-many avec Cours
                entity.HasMany(s => s.Courses)
                      .WithOne(c => c.Session)
                      .HasForeignKey(c => c.SessionId);
                
                // Configuration de la relation many-to-many avec ApplicationUser pour les inscriptions
                entity.HasMany(s => s.EnrolledUsers)
                      .WithMany(u => u.EnrolledSessions)
                      .UsingEntity<Dictionary<string, object>>(
                          "SessionEnrollments",
                          j => j
                              .HasOne<ApplicationUser>()
                              .WithMany()
                              .HasForeignKey("UserId")
                              .OnDelete(DeleteBehavior.Cascade),
                          j => j
                              .HasOne<Session>()
                              .WithMany()
                              .HasForeignKey("SessionId")
                              .OnDelete(DeleteBehavior.Cascade),
                          j =>
                          {
                              j.HasKey("SessionId", "UserId");
                              j.ToTable("SessionEnrollments");
                              j.HasIndex("UserId");
                              j.HasIndex("SessionId");
                          });
            });

            // Configuration de Cours
            builder.Entity<Cours>(entity =>
            {
                entity.ToTable("Cours");
                
                entity.Property(e => e.Title)
                      .IsRequired()
                      .HasColumnType("varchar")
                      .HasMaxLength(100);

                entity.Property(e => e.Description)
                      .HasColumnType("varchar")
                      .HasMaxLength(300);

                entity.Property(e => e.Content)
                        .HasColumnType("text");

                entity.Property(e => e.Order)
                        .HasColumnType("int")
                        .HasDefaultValue(1);

                entity.Property(e => e.IsPublished)
                        .HasColumnType("boolean")
                        .HasDefaultValue(false);

                entity.Property(e => e.CreatedAt)
                        .HasColumnType("timestamp with time zone")
                        .HasDefaultValueSql("CURRENT_TIMESTAMP");

                entity.Property(e => e.UpdatedAt)
                        .HasColumnType("timestamp with time zone")
                        .HasDefaultValueSql("CURRENT_TIMESTAMP");

                entity.HasOne(c => c.Session)
                      .WithMany(s => s.Courses)
                      .HasForeignKey(c => c.SessionId);
            });

            // Configuration de CourseProgress
            builder.Entity<CourseProgress>(entity =>
            {
                entity.ToTable("CourseProgress");
                
                // Contrainte unique sur CoursId + UserId
                entity.HasIndex(e => new { e.CoursId, e.UserId })
                      .IsUnique()
                      .HasDatabaseName("IX_CourseProgress_CoursId_UserId");
                
                // Relations
                entity.HasOne(cp => cp.Cours)
                      .WithMany()
                      .HasForeignKey(cp => cp.CoursId)
                      .OnDelete(DeleteBehavior.Cascade);
                
                entity.HasOne(cp => cp.User)
                      .WithMany()
                      .HasForeignKey(cp => cp.UserId)
                      .OnDelete(DeleteBehavior.Cascade);
            });

            // Configuration pour PreRegistration - CONFIGURATION COMPLÈTE
            builder.Entity<PreRegistration>(entity =>
            {
                entity.ToTable("PreRegistrations");
                
                entity.Property(e => e.UserId)
                      .IsRequired()
                      .HasColumnType("varchar")
                      .HasMaxLength(450); // Taille standard pour les ID d'Identity

                entity.Property(e => e.SessionId)
                      .IsRequired()
                      .HasColumnType("int");

                entity.Property(e => e.PreRegistrationDate)
                      .IsRequired()
                      .HasColumnType("timestamp with time zone")
                      .HasDefaultValueSql("CURRENT_TIMESTAMP");

                entity.Property(e => e.Status)
                      .IsRequired()
                      .HasColumnType("varchar")
                      .HasMaxLength(50)
                      .HasDefaultValue("Active");

                entity.Property(e => e.NotificationSent)
                      .HasColumnType("timestamp with time zone");

                entity.Property(e => e.Notes)
                      .HasColumnType("text");

                entity.Property(e => e.Priority)
                      .HasColumnType("int")
                      .HasDefaultValue(0);

                // Relations
                entity.HasOne(pr => pr.User)
                      .WithMany()
                      .HasForeignKey(pr => pr.UserId)
                      .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne(pr => pr.Session)
                      .WithMany()
                      .HasForeignKey(pr => pr.SessionId)
                      .OnDelete(DeleteBehavior.Cascade);

                // Index unique sur UserId + SessionId
                entity.HasIndex(pr => new { pr.UserId, pr.SessionId })
                      .IsUnique()
                      .HasDatabaseName("IX_PreRegistrations_UserId_SessionId");

                // Index sur SessionId pour les requêtes
                entity.HasIndex(pr => pr.SessionId)
                      .HasDatabaseName("IX_PreRegistrations_SessionId");

                // Index sur Status pour les requêtes filtrées
                entity.HasIndex(pr => pr.Status)
                      .HasDatabaseName("IX_PreRegistrations_Status");
            });

            // Configuration pour EnrollmentHistory
            builder.Entity<EnrollmentHistory>(entity =>
            {
                entity.HasIndex(e => new { e.UserId, e.SessionId, e.Date })
                      .HasDatabaseName("IX_EnrollmentHistory_User_Session_Date");
                      
                entity.HasIndex(e => e.Date)
                      .HasDatabaseName("IX_EnrollmentHistory_Date");

                // ⭐ NOUVEAU : Configuration explicite pour les propriétés DateTime
                entity.Property(e => e.Date)
                      .HasColumnType("timestamp with time zone")
                      .HasDefaultValueSql("CURRENT_TIMESTAMP");
                      
                entity.Property(e => e.EnrollmentDate)
                      .HasColumnType("timestamp with time zone");
                      
                entity.Property(e => e.UnenrollmentDate)
                      .HasColumnType("timestamp with time zone");
            });

            // Configuration pour CourseBadge
            builder.Entity<CourseBadge>(entity =>
            {
                entity.HasKey(cb => cb.Id);
                
                entity.HasOne(cb => cb.User)
                      .WithMany()
                      .HasForeignKey(cb => cb.UserId)
                      .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne(cb => cb.Cours)
                      .WithMany()
                      .HasForeignKey(cb => cb.CoursId)
                      .OnDelete(DeleteBehavior.Cascade);

                // Index unique pour éviter les doublons
                entity.HasIndex(cb => new { cb.UserId, cb.CoursId })
                      .IsUnique()
                      .HasDatabaseName("IX_CourseBadge_User_Cours");

                entity.Property(cb => cb.ScorePercentage)
                      .HasPrecision(5, 2);
            });
        }

        // ⭐ NOUVEAU : Configuration supplémentaire pour les conventions PostgreSQL
        protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
        {
            // Convertir automatiquement tous les DateTime en UTC
            configurationBuilder.Properties<DateTime>()
                .HaveConversion<UtcDateTimeConverter>();
                
            configurationBuilder.Properties<DateTime?>()
                .HaveConversion<NullableUtcDateTimeConverter>();
        }
    }
}
