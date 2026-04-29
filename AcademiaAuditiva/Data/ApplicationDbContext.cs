using AcademiaAuditiva.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using System.Reflection.Emit;

namespace AcademiaAuditiva.Data
{
    public class ApplicationDbContext : IdentityDbContext
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options)
        {
        }


        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // The bootstrap admin user is created at runtime via
            // BootstrapAdminInitializer (see Program.cs). Seeding it here would
            // bake a password hash into EF migrations and source control, and
            // would also tie the migration to a single tenant identity.
        }


        public DbSet<Exercise> Exercises { get; set; }
        public DbSet<Score> Scores { get; set; }
        public DbSet<Badge> Badges { get; set; }
        public DbSet<BadgesEarned> BadgesEarned { get; set; }
        public DbSet<ExerciseType> ExerciseTypes { get; set; }
        public DbSet<ExerciseCategory> ExerciseCategories { get; set; }
        public DbSet<DifficultyLevel> DifficultyLevels { get; set; }
        public DbSet<Subscription> Subscriptions { get; set; }

    }
}