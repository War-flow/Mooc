using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Mooc.Data
{
    public class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : IdentityDbContext<ApplicationUser>(options)
    {
        public DbSet<Session> Sessions { get; set; } = null!;
        public DbSet<Cours> Courses { get; set; }
        public DbSet<CourseProgress> CourseProgresses { get; set; } = null!;
        public DbSet<Certificate> Certificates { get; set; }

        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);

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
                      
                entity.Property(e => e.StartDate)
                      .HasColumnType("date");
                      
                entity.Property(e => e.EndDate)
                      .HasColumnType("date");
                      
                entity.Property(e => e.Work)
                      .HasColumnType("int");
                     
                entity.Property(e => e.IsActive)
                      .HasColumnType("boolean")
                      .HasDefaultValue(true);
                
                // **AJOUT MANQUANT** : Configuration de la relation one-to-one avec le créateur
                entity.HasOne(s => s.User)
                      .WithMany()
                      .HasForeignKey(s => s.UserId)
                      .OnDelete(DeleteBehavior.SetNull);
                
                // Relation one-to-many avec Cours
                entity.HasMany(s => s.Courses)
                      .WithOne(c => c.Session)
                      .HasForeignKey(c => c.SessionId);
                
                // **CONFIGURATION EXISTANTE** : Relation many-to-many avec ApplicationUser pour les inscriptions
                entity.HasMany(s => s.EnrolledUsers)
                      .WithMany(u => u.EnrolledSessions)
                      .UsingEntity<Dictionary<string, object>>(
                          "SessionEnrollments", // Nom de la table de liaison
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
        }
    }
}
