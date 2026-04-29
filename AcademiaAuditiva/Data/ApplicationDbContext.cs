using AcademiaAuditiva.Models;
using AcademiaAuditiva.Models.Teaching;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

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
            // IdentityBootstrapper (see Program.cs). Seeding it here would
            // bake a password hash into EF migrations and source control, and
            // would also tie the migration to a single tenant identity.

            ConfigureTeachingDomain(modelBuilder);
        }

        private static void ConfigureTeachingDomain(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Classroom>(b =>
            {
                b.HasIndex(c => c.OwnerId);
                b.HasOne(c => c.Owner)
                    .WithMany()
                    .HasForeignKey(c => c.OwnerId)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            modelBuilder.Entity<ClassroomMember>(b =>
            {
                b.HasIndex(m => new { m.ClassroomId, m.StudentId }).IsUnique();
                b.HasOne(m => m.Classroom)
                    .WithMany(c => c.Members)
                    .HasForeignKey(m => m.ClassroomId)
                    .OnDelete(DeleteBehavior.Cascade);
                b.HasOne(m => m.Student)
                    .WithMany()
                    .HasForeignKey(m => m.StudentId)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            modelBuilder.Entity<ClassroomInvite>(b =>
            {
                b.HasIndex(i => i.Token).IsUnique();
                b.HasIndex(i => new { i.ClassroomId, i.Email });
                b.HasOne(i => i.Classroom)
                    .WithMany(c => c.Invites)
                    .HasForeignKey(i => i.ClassroomId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<Routine>(b =>
            {
                b.HasIndex(r => r.OwnerId);
                b.HasOne(r => r.Owner)
                    .WithMany()
                    .HasForeignKey(r => r.OwnerId)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            modelBuilder.Entity<RoutineItem>(b =>
            {
                b.HasIndex(i => new { i.RoutineId, i.Order });
                b.HasOne(i => i.Routine)
                    .WithMany(r => r.Items)
                    .HasForeignKey(i => i.RoutineId)
                    .OnDelete(DeleteBehavior.Cascade);
                b.HasOne(i => i.Exercise)
                    .WithMany()
                    .HasForeignKey(i => i.ExerciseId)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            modelBuilder.Entity<RoutineAssignment>(b =>
            {
                b.HasIndex(a => a.RoutineId);
                b.HasIndex(a => a.ClassroomId);
                b.HasIndex(a => a.StudentId);
                b.HasOne(a => a.Routine)
                    .WithMany()
                    .HasForeignKey(a => a.RoutineId)
                    .OnDelete(DeleteBehavior.Restrict);
                b.HasOne(a => a.Classroom)
                    .WithMany()
                    .HasForeignKey(a => a.ClassroomId)
                    .OnDelete(DeleteBehavior.SetNull);
                b.HasOne(a => a.Student)
                    .WithMany()
                    .HasForeignKey(a => a.StudentId)
                    .OnDelete(DeleteBehavior.SetNull);
            });

            modelBuilder.Entity<RoutineAssignmentOverride>(b =>
            {
                b.HasIndex(o => new { o.RoutineAssignmentId, o.StudentId, o.RoutineItemId }).IsUnique();
                b.HasOne(o => o.RoutineAssignment)
                    .WithMany(a => a.Overrides)
                    .HasForeignKey(o => o.RoutineAssignmentId)
                    .OnDelete(DeleteBehavior.Cascade);
                b.HasOne(o => o.Student)
                    .WithMany()
                    .HasForeignKey(o => o.StudentId)
                    .OnDelete(DeleteBehavior.Restrict);
                b.HasOne(o => o.RoutineItem)
                    .WithMany()
                    .HasForeignKey(o => o.RoutineItemId)
                    .OnDelete(DeleteBehavior.Restrict);
            });
        }


        public DbSet<Exercise> Exercises { get; set; }
        public DbSet<Score> Scores { get; set; }
        public DbSet<Badge> Badges { get; set; }
        public DbSet<BadgesEarned> BadgesEarned { get; set; }
        public DbSet<ExerciseType> ExerciseTypes { get; set; }
        public DbSet<ExerciseCategory> ExerciseCategories { get; set; }
        public DbSet<DifficultyLevel> DifficultyLevels { get; set; }
        public DbSet<Subscription> Subscriptions { get; set; }

        // Teaching domain
        public DbSet<Classroom> Classrooms => Set<Classroom>();
        public DbSet<ClassroomMember> ClassroomMembers => Set<ClassroomMember>();
        public DbSet<ClassroomInvite> ClassroomInvites => Set<ClassroomInvite>();
        public DbSet<Routine> Routines => Set<Routine>();
        public DbSet<RoutineItem> RoutineItems => Set<RoutineItem>();
        public DbSet<RoutineAssignment> RoutineAssignments => Set<RoutineAssignment>();
        public DbSet<RoutineAssignmentOverride> RoutineAssignmentOverrides => Set<RoutineAssignmentOverride>();
    }
}