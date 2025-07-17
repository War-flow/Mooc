using Microsoft.EntityFrameworkCore;
using Mooc.Components.Pages.Manager.CMS;
using Mooc.Data;
using System.Text.Json;


namespace Mooc.Services
{
    public class BlockService
    {
        private readonly IDbContextFactory<ApplicationDbContext> _contextFactory;
        
        public BlockService(IDbContextFactory<ApplicationDbContext> contextFactory)
        {
            _contextFactory = contextFactory;
        }
        
        public async Task<bool> SaveBlockAsync(CourBuilder.CoursBlock block, int coursId)
        {
            try
            {
                using var context = await _contextFactory.CreateDbContextAsync();
                
                var cours = await context.Courses.FindAsync(coursId);
                if (cours == null)
                    return false;
                
                // Récupérer les blocs existants
                var blocks = new List<CourBuilder.CoursBlock>();
                if (!string.IsNullOrEmpty(cours.Content))
                {
                    blocks = JsonSerializer.Deserialize<List<CourBuilder.CoursBlock>>(cours.Content) ?? new List<CourBuilder.CoursBlock>();
                }
                
                // Trouver et mettre à jour le bloc ou l'ajouter s'il n'existe pas
                var existingBlockIndex = blocks.FindIndex(b => b.Id == block.Id);
                if (existingBlockIndex >= 0)
                {
                    blocks[existingBlockIndex] = block;
                }
                else
                {
                    blocks.Add(block);
                }
                
                // Sauvegarder les modifications
                cours.Content = JsonSerializer.Serialize(blocks);
                cours.UpdatedAt = DateTime.UtcNow;
                
                await context.SaveChangesAsync();
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Erreur lors de la sauvegarde du bloc: {ex.Message}");
                return false;
            }
        }
        
        public async Task<bool> DeleteBlockAsync(Guid blockId, int coursId)
        {
            try
            {
                using var context = await _contextFactory.CreateDbContextAsync();
                
                var cours = await context.Courses.FindAsync(coursId);
                if (cours == null)
                    return false;
                
                // Récupérer les blocs existants
                var blocks = new List<CourBuilder.CoursBlock>();
                if (!string.IsNullOrEmpty(cours.Content))
                {
                    blocks = JsonSerializer.Deserialize<List<CourBuilder.CoursBlock>>(cours.Content) ?? new List<CourBuilder.CoursBlock>();
                }
                
                // Supprimer le bloc
                blocks.RemoveAll(b => b.Id == blockId);
                
                // Sauvegarder les modifications
                cours.Content = JsonSerializer.Serialize(blocks);
                cours.UpdatedAt = DateTime.UtcNow;
                
                await context.SaveChangesAsync();
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Erreur lors de la suppression du bloc: {ex.Message}");
                return false;
            }
        }
    }
}