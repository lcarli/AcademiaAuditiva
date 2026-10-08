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
            ConfigureBadges(modelBuilder);
            ConfigureTutorials(modelBuilder);
            ConfigureGames(modelBuilder);
            ConfigureEmails(modelBuilder);
        }

        private static void ConfigureEmails(ModelBuilder modelBuilder)
        {
            // ApplicationUser shares AspNetUsers with IdentityUser, so its columns are nullable:
            // the default fills rows inserted without the column, e.g. by the previous revision
            // while a deployment rolls out.
            modelBuilder.Entity<ApplicationUser>(b =>
                b.Property(u => u.RoutineEmailsOff).HasDefaultValue(false));

            modelBuilder.Entity<EmailDailyCount>(b =>
            {
                b.HasKey(c => c.Day);
                b.Property(c => c.Sent).IsConcurrencyToken();
            });
        }

        private static void ConfigureGames(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<GameRun>(b =>
            {
                b.Property(r => r.Mode).HasMaxLength(GameRun.ModeMaxLength);
                b.Property(r => r.FilterJson).HasMaxLength(ScoreSnapshot.FilterJsonMaxLength);
                b.HasIndex(r => new { r.UserId, r.Mode, r.ExerciseId });
                b.HasOne(r => r.User).WithMany().HasForeignKey(r => r.UserId)
                    .OnDelete(DeleteBehavior.Cascade);
                b.HasOne(r => r.Exercise).WithMany().HasForeignKey(r => r.ExerciseId)
                    .OnDelete(DeleteBehavior.Restrict);
            });
        }

        // The unique index keeps one row per user and tour when two tabs close it at once.
        private static void ConfigureTutorials(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<UserTutorial>(b =>
            {
                b.Property(t => t.TutorialKey).HasMaxLength(32);
                b.HasIndex(t => new { t.UserId, t.TutorialKey }).IsUnique();
                b.HasOne(t => t.User).WithMany().HasForeignKey(t => t.UserId)
                    .OnDelete(DeleteBehavior.Cascade);
            });
        }

        // BadgeKey is the foreign key to Badge (EF had added a shadow BadgeKey1
        // column for the navigation), and a user earns each badge once.
        private static void ConfigureBadges(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<BadgesEarned>(b =>
            {
                b.Property(e => e.BadgeKey).HasMaxLength(50);
                b.HasIndex(e => new { e.UserId, e.BadgeKey }).IsUnique();
                b.HasOne(e => e.Badge).WithMany().HasForeignKey(e => e.BadgeKey)
                    .OnDelete(DeleteBehavior.Restrict);
            });
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

            modelBuilder.Entity<RoutineAssignmentStudent>(b =>
            {
                b.HasKey(s => new { s.RoutineAssignmentId, s.StudentId });
                b.HasIndex(s => s.StudentId);
                b.HasOne(s => s.RoutineAssignment)
                    .WithMany(a => a.ChosenStudents)
                    .HasForeignKey(s => s.RoutineAssignmentId)
                    .OnDelete(DeleteBehavior.Cascade);
                b.HasOne(s => s.Student)
                    .WithMany()
                    .HasForeignKey(s => s.StudentId)
                    .OnDelete(DeleteBehavior.Restrict);
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

            // Score model split (expand-then-contract — see Migration
            // AddScoreSnapshotAndAggregate). The legacy `Score` table is
            // still written and read for now; the next migration in the
            // contract phase will drop it once readers move over.
            modelBuilder.Entity<ScoreSnapshot>(b =>
            {
                b.Property(s => s.FilterJson).HasMaxLength(ScoreSnapshot.FilterJsonMaxLength);
                b.HasIndex(s => new { s.UserId, s.ExerciseId, s.Timestamp });
                // One answer per question of a routine item, even when two tabs answer
                // the last question at once.
                b.HasIndex(s => new { s.UserId, s.RoutineAssignmentId, s.RoutineItemId, s.RoutineQuestion })
                    .IsUnique()
                    .HasFilter("[RoutineAssignmentId] IS NOT NULL");
                b.HasIndex(s => s.GameRunId).HasFilter("[GameRunId] IS NOT NULL");
                b.HasOne(s => s.User).WithMany().HasForeignKey(s => s.UserId)
                    .OnDelete(DeleteBehavior.Cascade);
                b.HasOne(s => s.Exercise).WithMany().HasForeignKey(s => s.ExerciseId)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            modelBuilder.Entity<ScoreAggregate>(b =>
            {
                b.HasKey(a => new { a.UserId, a.ExerciseId });
                b.HasOne(a => a.User).WithMany().HasForeignKey(a => a.UserId)
                    .OnDelete(DeleteBehavior.Cascade);
                b.HasOne(a => a.Exercise).WithMany().HasForeignKey(a => a.ExerciseId)
                    .OnDelete(DeleteBehavior.Restrict);
            });
        }


        public DbSet<Exercise> Exercises { get; set; }
        public DbSet<Score> Scores { get; set; }
        public DbSet<ScoreSnapshot> ScoreSnapshots => Set<ScoreSnapshot>();
        public DbSet<ScoreAggregate> ScoreAggregates => Set<ScoreAggregate>();
        public DbSet<Badge> Badges { get; set; }
        public DbSet<BadgesEarned> BadgesEarned { get; set; }
        public DbSet<ExerciseType> ExerciseTypes { get; set; }
        public DbSet<ExerciseCategory> ExerciseCategories { get; set; }
        public DbSet<DifficultyLevel> DifficultyLevels { get; set; }
        public DbSet<Subscription> Subscriptions { get; set; }
        public DbSet<UserTutorial> UserTutorials => Set<UserTutorial>();
        public DbSet<GameRun> GameRuns => Set<GameRun>();
        public DbSet<EmailDailyCount> EmailDailyCounts => Set<EmailDailyCount>();

        // Teaching domain
        public DbSet<Classroom> Classrooms => Set<Classroom>();
        public DbSet<ClassroomMember> ClassroomMembers => Set<ClassroomMember>();
        public DbSet<ClassroomInvite> ClassroomInvites => Set<ClassroomInvite>();
        public DbSet<Routine> Routines => Set<Routine>();
        public DbSet<RoutineItem> RoutineItems => Set<RoutineItem>();
        public DbSet<RoutineAssignment> RoutineAssignments => Set<RoutineAssignment>();
        public DbSet<RoutineAssignmentStudent> RoutineAssignmentStudents => Set<RoutineAssignmentStudent>();
        public DbSet<RoutineAssignmentOverride> RoutineAssignmentOverrides => Set<RoutineAssignmentOverride>();
    }
}