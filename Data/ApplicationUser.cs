using Microsoft.AspNetCore.Identity;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Mooc.Data
{
    public class ApplicationUser : IdentityUser
    {
        [PersonalData]
        [Required]
        [StringLength(100)]
        [Column(TypeName = "varchar(100)")]
        public string FirstName { get; set; } = string.Empty;

        [PersonalData]
        [Required]
        [StringLength(100)]
        [Column(TypeName = "varchar(100)")]
        public string LastName { get; set; } = string.Empty;

        [Required]
        [StringLength(100)]
        [Column(TypeName = "varchar(100)")]
        public string Pseudo { get; set; } = string.Empty;

        [NotMapped]
        public string FullName => $"{FirstName} {LastName}";

        // **Navigation property pour les sessions auxquelles l'utilisateur est inscrit**
        public virtual ICollection<Session> EnrolledSessions { get; set; } = new List<Session>();

        [PersonalData]
        public bool ShowInTrombinoscope { get; set; } = false;

    }
}
