using AcademiaAuditiva.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using System.Reflection.Emit;

namespace AcademiaAuditiva.Data
{
    public class ApplicationDbContext : IdentityDbContext<ApplicationUser, IdentityRole, string>
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options)
        {
        }


        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            //Seed Roles
            const string ADMIN_ROLE_ID = "b1d9f8d5-7f5b-4c2c-9a1d-111111111111";
            const string PROFESSOR_ROLE_ID = "b1d9f8d5-7f5b-4c2c-9a1d-222222222222";
            const string STUDENT_ROLE_ID = "b1d9f8d5-7f5b-4c2c-9a1d-333333333333";
            const string ADMIN_USER_ID = "a0a0a0a0-1234-4bcd-9abc-999999999999";

            var roles = new[]
            {
                new IdentityRole
                {
                    Id = ADMIN_ROLE_ID,
                    Name = "Admin",
                    NormalizedName = "ADMIN",
                    ConcurrencyStamp = ADMIN_ROLE_ID
                },
                new IdentityRole
                {
                    Id = PROFESSOR_ROLE_ID,
                    Name = "Professor",
                    NormalizedName = "PROFESSOR",
                    ConcurrencyStamp = PROFESSOR_ROLE_ID
                },
                new IdentityRole
                {
                    Id = STUDENT_ROLE_ID,
                    Name = "Student",
                    NormalizedName = "STUDENT",
                    ConcurrencyStamp = STUDENT_ROLE_ID
                }
            };
            modelBuilder.Entity<IdentityRole>().HasData(roles);


            //Seed Admin User
            ApplicationUser rootUser = new ApplicationUser
            {
                UserName = "lucas.decarli.ca@gmail.com",
                NormalizedUserName = "LUCAS.DECARLI.CA@GMAIL.COM",
                Email = "lucas.decarli.ca@gmail.com",
                NormalizedEmail = "LUCAS.DECARLI.CA@GMAIL.COM",
                EmailConfirmed = true,
                Id = ADMIN_USER_ID,
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

            //Link User and Role
            modelBuilder.Entity<IdentityUserRole<string>>().HasData(
                new IdentityUserRole<string>
                {
                    UserId = ADMIN_USER_ID,
                    RoleId = ADMIN_ROLE_ID
                }
            );
            
            modelBuilder.Entity<BadgesEarned>()
                .HasOne(be => be.Badge)
                .WithMany()
                .HasForeignKey(be => be.BadgeKey)
                .HasPrincipalKey(b => b.BadgeKey);

            modelBuilder.Entity<BadgesEarned>()
                .HasOne(be => be.User)
                .WithMany(u => u.EarnedBadges)
                .HasForeignKey(be => be.UserId);
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