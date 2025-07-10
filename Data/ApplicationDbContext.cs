using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Mooc.Data
{
    public class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : IdentityDbContext<ApplicationUser>(options)
    {
        public DbSet<Session> Sessions { get; set; } = null!;
        public DbSet<Cours> Cours { get; set; } = null!;
        public DbSet<Quiz> Quizzes { get; set; } = null!;

        // Vous pouvez également configurer des détails supplémentaires si nécessaire
        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);

            // Configurer la table Session avec des types spécifiques
            builder.Entity<Session>(entity =>
            {
                entity.ToTable("Session");

                entity.Property(e => e.Title)
                      .IsRequired()  // Rendre le titre obligatoire
                      .HasColumnType("varchar") // Utiliser varchar pour le titre
                      .HasMaxLength(100);  // Limite à 100 caractères

                entity.Property(e => e.Image)
                      .HasColumnType("varchar") // Utiliser varchar pour l'image
                      .IsRequired();

                entity.Property(e => e.Description)
                      .HasColumnType("varchar") // Utiliser varchar pour le titre
                      .HasMaxLength(300);

                entity.Property(e => e.StartDate)
                      .HasColumnType("date"); // Utiliser date au lieu de timestamp

                entity.Property(e => e.EndDate)
                      .HasColumnType("date");

                entity.Property(e => e.Work)
                      .HasColumnType("int"); // Utiliser int pour le travail

                entity.Property(e => e.IsActive)
                        .HasColumnType("boolean") // Utiliser boolean pour le booléen
                        .HasDefaultValue(false); // Par défaut, la session est active

                entity.HasMany(s => s.Courses)
                      .WithOne(c => c.Session)
                      .HasForeignKey(c => c.SessionId);
            });

            // Configuration de la relation Session-Cours
            builder.Entity<Cours>(entity =>
            {
                entity.ToTable("Cours");

                entity.Property(e => e.Title)
                        .IsRequired()  // Rendre le titre obligatoire
                        .HasColumnType("varchar") // Utiliser varchar pour le titre
                        .HasMaxLength(100);  // Limite à 100 caractères

                entity.HasOne(c => c.Session)
                      .WithMany(s => s.Courses)
                      .HasForeignKey(c => c.SessionId);
            });

            // Configuration de la relation Cours-Quiz (One-to-One)
            builder.Entity<Quiz>(entity =>
            {
                entity.ToTable("Quiz");

                entity.Property(e => e.Title)
                      .IsRequired()  // Rendre le titre obligatoire
                      .HasColumnType("varchar") // Utiliser varchar pour le titre
                      .HasMaxLength(100);  // Limite à 100 caractères

                entity.HasOne(q => q.Cours)
                      .WithOne(c => c.Quiz)
                      .HasForeignKey<Quiz>(q => q.CoursId);
            });
        }
    }
}
