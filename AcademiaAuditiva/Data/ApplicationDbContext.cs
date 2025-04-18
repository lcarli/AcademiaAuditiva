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

            ApplicationUser rootUser = new ApplicationUser
            {
                UserName = "lucas.decarli.ca@gmail.com",
                NormalizedUserName = "LUCAS.DECARLI.CA@GMAIL.COM",
                Email = "lucas.decarli.ca@gmail.com",
                NormalizedEmail = "LUCAS.DECARLI.CA@GMAIL.COM",
                EmailConfirmed = true,
                Id = Guid.NewGuid().ToString(),
                FirstName = "Lucas",
                LastName = "De Carli",
                SecurityStamp = "UTUTEH5FUQ6C2MUTMB3CCICNLIBN6CAO",
                ConcurrencyStamp = "37f979fc-85e9-42c0-bc5c-3321d0b9cad6",
                TwoFactorEnabled = false,
                LockoutEnd = null,
                LockoutEnabled = true,
                AccessFailedCount = 0,
                PhoneNumber = "+15817456586",
                PhoneNumberConfirmed = true,
            };

            var password = new PasswordHasher<ApplicationUser>();
            var hashed = password.HashPassword(rootUser, "Lorenzo*181013");
            rootUser.PasswordHash = hashed;

            modelBuilder.Entity<ApplicationUser>().HasData(rootUser);
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