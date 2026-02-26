using Microsoft.EntityFrameworkCore;
using SmartELibrary.Models;

namespace SmartELibrary.Data;

public class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<DeletedUser> DeletedUsers => Set<DeletedUser>();
    public DbSet<Admin> Admins => Set<Admin>();
    public DbSet<Teacher> Teachers => Set<Teacher>();
    public DbSet<Student> Students => Set<Student>();
    public DbSet<Semester> Semesters => Set<Semester>();
    public DbSet<Subject> Subjects => Set<Subject>();
    public DbSet<Topic> Topics => Set<Topic>();
    public DbSet<TeacherSubject> TeacherSubjects => Set<TeacherSubject>();
    public DbSet<StudentEnrollment> StudentEnrollments => Set<StudentEnrollment>();
    public DbSet<Material> Materials => Set<Material>();
    public DbSet<MaterialPage> MaterialPages => Set<MaterialPage>();
    public DbSet<Quiz> Quizzes => Set<Quiz>();
    public DbSet<QuizQuestion> QuizQuestions => Set<QuizQuestion>();
    public DbSet<QuizResult> QuizResults => Set<QuizResult>();
    public DbSet<ProgressTracking> ProgressTrackings => Set<ProgressTracking>();
    public DbSet<MaterialPageProgress> MaterialPageProgress => Set<MaterialPageProgress>();
    public DbSet<OtpVerification> OtpVerifications => Set<OtpVerification>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<User>().HasIndex(x => x.PhoneNumber).IsUnique();

        modelBuilder.Entity<DeletedUser>()
            .HasIndex(x => x.OriginalUserId)
            .IsUnique();

        // Keep default column naming for compatibility with existing migrations/schema:
        // Users.FullName, Users.CreatedAtUtc

        modelBuilder.Entity<Admin>()
            .HasIndex(x => x.UserId)
            .IsUnique();

        modelBuilder.Entity<Teacher>()
            .HasIndex(x => x.UserId)
            .IsUnique();

        modelBuilder.Entity<Teacher>()
            .HasIndex(x => x.TeacherId)
            .IsUnique();

        modelBuilder.Entity<Student>()
            .HasIndex(x => x.UserId)
            .IsUnique();

        modelBuilder.Entity<TeacherSubject>()
            .HasIndex(x => new { x.TeacherId, x.SubjectId })
            .IsUnique();

        modelBuilder.Entity<StudentEnrollment>()
            .HasIndex(x => new { x.StudentId, x.SemesterId })
            .IsUnique();

        modelBuilder.Entity<StudentEnrollment>()
            .Property(x => x.ApprovedAtUtc)
            .HasColumnName("ApprovedAt");

        modelBuilder.Entity<QuizResult>()
            .HasIndex(x => new { x.QuizId, x.StudentId });

        modelBuilder.Entity<MaterialPage>()
            .HasIndex(x => new { x.MaterialId, x.PageNumber })
            .IsUnique();

        modelBuilder.Entity<MaterialPageProgress>()
            .HasIndex(x => new { x.StudentId, x.MaterialPageId })
            .IsUnique();

        modelBuilder.Entity<Material>()
            .HasOne(x => x.Teacher)
            .WithMany(x => x.Materials)
            .HasForeignKey(x => x.TeacherId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<Admin>()
            .HasOne(x => x.User)
            .WithOne(x => x.Admin)
            .HasForeignKey<Admin>(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<Teacher>()
            .HasOne(x => x.User)
            .WithOne(x => x.Teacher)
            .HasForeignKey<Teacher>(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<Student>()
            .HasOne(x => x.User)
            .WithOne(x => x.Student)
            .HasForeignKey<Student>(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<MaterialPage>()
            .HasOne(x => x.Material)
            .WithMany(x => x.Pages)
            .HasForeignKey(x => x.MaterialId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<MaterialPageProgress>()
            .HasOne(x => x.Student)
            .WithMany()
            .HasForeignKey(x => x.StudentId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<MaterialPageProgress>()
            .HasOne(x => x.MaterialPage)
            .WithMany(x => x.PageProgress)
            .HasForeignKey(x => x.MaterialPageId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<TeacherSubject>()
            .HasOne(x => x.Teacher)
            .WithMany(x => x.TeacherSubjects)
            .HasForeignKey(x => x.TeacherId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<StudentEnrollment>()
            .HasOne(x => x.Student)
            .WithMany()
            .HasForeignKey(x => x.StudentId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<StudentEnrollment>()
            .HasOne(x => x.ApprovedByAdmin)
            .WithMany()
            .HasForeignKey(x => x.ApprovedByAdminId)
            .OnDelete(DeleteBehavior.SetNull);

        modelBuilder.Entity<QuizResult>()
            .HasOne(x => x.Student)
            .WithMany(x => x.QuizResults)
            .HasForeignKey(x => x.StudentId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<ProgressTracking>()
            .HasOne(x => x.Student)
            .WithMany(x => x.ProgressTrackings)
            .HasForeignKey(x => x.StudentId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<User>().HasData(
            new User
            {
                Id = 1,
                FullName = "System Admin",
                PhoneNumber = "9999999999",
                PasswordHash = "pqp7RUbHAaT3rE1FB2yLYA==.DDvLiDdTGAeURgKOJzbSL8PIJIMsve3hWpUHfv9JOrk=",
                Role = UserRole.Admin,
                IsApproved = true,
                CreatedAtUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            });
    }
}
