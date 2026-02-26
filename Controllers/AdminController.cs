using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmartELibrary.Data;
using SmartELibrary.Filters;
using SmartELibrary.Models;
using SmartELibrary.Services;
using SmartELibrary.ViewModels;

namespace SmartELibrary.Controllers;

[RoleAuthorize(UserRole.Admin)]
public class AdminController(ApplicationDbContext dbContext, IProgressService progressService) : Controller
{
    public async Task<IActionResult> Dashboard()
    {
        ViewBag.TotalUsers = await dbContext.Users.CountAsync();
        ViewBag.TotalStudents = await dbContext.Users.CountAsync(x => x.Role == UserRole.Student);
        ViewBag.TotalTeachers = await dbContext.Users.CountAsync(x => x.Role == UserRole.Teacher);
        ViewBag.TotalMaterials = await dbContext.Materials.CountAsync();
        ViewBag.TotalQuizzes = await dbContext.Quizzes.CountAsync();
        return View();
    }

    public async Task<IActionResult> Users(string? role)
    {
        var roleFilter = string.IsNullOrWhiteSpace(role) ? "All" : role.Trim();
        roleFilter = roleFilter.Equals("Admin", StringComparison.OrdinalIgnoreCase)
                     || roleFilter.Equals("Teacher", StringComparison.OrdinalIgnoreCase)
                     || roleFilter.Equals("Student", StringComparison.OrdinalIgnoreCase)
                     || roleFilter.Equals("All", StringComparison.OrdinalIgnoreCase)
            ? roleFilter
            : "All";

        var usersQuery = dbContext.Users.AsQueryable();
        if (!roleFilter.Equals("All", StringComparison.OrdinalIgnoreCase))
        {
            if (Enum.TryParse<UserRole>(roleFilter, out var parsedRole))
            {
                usersQuery = usersQuery.Where(x => x.Role == parsedRole);
            }
        }

        var users = await usersQuery
            .OrderByDescending(x => x.CreatedAtUtc)
            .ToListAsync();

        var userIds = users.Select(x => x.Id).ToList();

        var teacherCodes = await dbContext.Teachers
            .Where(x => userIds.Contains(x.UserId))
            .Select(x => new { x.UserId, x.TeacherId })
            .ToDictionaryAsync(x => x.UserId, x => x.TeacherId);

        var teacherSubjectCounts = await dbContext.TeacherSubjects
            .Where(x => userIds.Contains(x.TeacherId))
            .GroupBy(x => x.TeacherId)
            .Select(g => new { UserId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.UserId, x => x.Count);

        var studentEnrollmentNumbers = await dbContext.Students
            .Where(x => userIds.Contains(x.UserId))
            .Select(x => new { x.UserId, x.EnrollmentNumber })
            .ToDictionaryAsync(x => x.UserId, x => x.EnrollmentNumber);

        var enrollments = await dbContext.StudentEnrollments
            .Include(x => x.Semester)
            .Where(x => userIds.Contains(x.StudentId))
            .OrderByDescending(x => x.EnrolledAtUtc)
            .ToListAsync();

        var enrollmentsByStudent = enrollments
            .GroupBy(x => x.StudentId)
            .ToDictionary(x => x.Key, x => x.ToList());

        var model = new AdminUsersViewModel
        {
            RoleFilter = roleFilter,
            Users = users.Select(user => new AdminUserRowViewModel
            {
                UserId = user.Id,
                Name = user.FullName,
                PhoneNumber = user.PhoneNumber,
                Role = user.Role.ToString(),
                IsApproved = user.IsApproved,
                CreatedAtUtc = user.CreatedAtUtc,
                TeacherId = teacherCodes.TryGetValue(user.Id, out var code) ? code : null,
                AssignedSubjectCount = teacherSubjectCounts.TryGetValue(user.Id, out var count) ? count : null,
                EnrollmentNumber = studentEnrollmentNumbers.TryGetValue(user.Id, out var enrollment) ? enrollment : null,
                Enrollments = enrollmentsByStudent.TryGetValue(user.Id, out var studentEnrollments)
                    ? studentEnrollments.Select(e => new StudentEnrollmentSummaryViewModel
                        {
                            SemesterName = e.Semester?.Name ?? "-",
                            Status = e.ApprovedAtUtc is null ? "Pending" : (e.IsApproved ? "Approved" : "Rejected"),
                            EnrolledAtUtc = e.EnrolledAtUtc,
                            ApprovedAtUtc = e.ApprovedAtUtc
                        })
                        .ToList()
                    : new List<StudentEnrollmentSummaryViewModel>()
            }).ToList()
        };

        return View(model);
    }

    [HttpPost]
    public async Task<IActionResult> ApproveUser(int userId, string? role)
    {
        var user = await dbContext.Users.FindAsync(userId);
        if (user is not null)
        {
            user.IsApproved = true;
            await dbContext.SaveChangesAsync();
        }

        return RedirectToAction(nameof(Users), new { role });
    }

    public async Task<IActionResult> EditUser(int userId)
    {
        var user = await dbContext.Users.FindAsync(userId);
        if (user is null)
        {
            return NotFound();
        }

        if (user.Id == 1)
        {
            TempData["Error"] = "System Admin user cannot be edited.";
            return RedirectToAction(nameof(Users));
        }

        var student = await dbContext.Students.FirstOrDefaultAsync(x => x.UserId == user.Id);

        var model = new EditUserViewModel
        {
            Id = user.Id,
            FullName = user.FullName,
            PhoneNumber = user.PhoneNumber,
            EnrollmentNo = student?.EnrollmentNumber,
            Role = user.Role,
            IsApproved = user.IsApproved
        };

        return View(model);
    }

    [HttpPost]
    public async Task<IActionResult> EditUser(EditUserViewModel model)
    {
        if (model.Id == 1)
        {
            TempData["Error"] = "System Admin user cannot be edited.";
            return RedirectToAction(nameof(Users));
        }

        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var user = await dbContext.Users.FindAsync(model.Id);
        if (user is null)
        {
            return NotFound();
        }

        var phoneInUse = await dbContext.Users.AnyAsync(x => x.PhoneNumber == model.PhoneNumber && x.Id != model.Id);
        if (phoneInUse)
        {
            ModelState.AddModelError(nameof(model.PhoneNumber), "Phone number is already in use.");
            return View(model);
        }

        user.FullName = model.FullName.Trim();
        user.PhoneNumber = model.PhoneNumber.Trim();
        user.Role = model.Role;
        user.IsApproved = model.IsApproved;

        if (model.Role == UserRole.Student)
        {
            var enrollmentNo = model.EnrollmentNo?.Trim() ?? string.Empty;
            var student = await dbContext.Students.FirstOrDefaultAsync(x => x.UserId == user.Id);
            if (student is null)
            {
                dbContext.Students.Add(new Student
                {
                    UserId = user.Id,
                    EnrollmentNumber = enrollmentNo,
                    CreatedAtUtc = DateTime.UtcNow
                });
            }
            else
            {
                student.EnrollmentNumber = enrollmentNo;
            }
        }

        await dbContext.SaveChangesAsync();

        TempData["Success"] = "User updated.";
        return RedirectToAction(nameof(Users), new { role = "All" });
    }

    [HttpPost]
    public async Task<IActionResult> DeleteUser(int userId, string? role)
    {
        if (userId == 1)
        {
            TempData["Error"] = "System Admin user cannot be deleted.";
            return RedirectToAction(nameof(Users), new { role });
        }

        var user = await dbContext.Users.FirstOrDefaultAsync(x => x.Id == userId);
        if (user is null)
        {
            TempData["Error"] = "User not found.";
            return RedirectToAction(nameof(Users), new { role });
        }

        if (user.Role == UserRole.Admin)
        {
            TempData["Error"] = "Admin users cannot be deleted.";
            return RedirectToAction(nameof(Users), new { role });
        }

        try
        {
            await using var tx = await dbContext.Database.BeginTransactionAsync();

            string? enrollmentNumber = null;
            if (user.Role == UserRole.Student)
            {
                enrollmentNumber = await dbContext.Students
                    .Where(x => x.UserId == userId)
                    .Select(x => x.EnrollmentNumber)
                    .FirstOrDefaultAsync();
            }

            var deletedUser = new DeletedUser
            {
                OriginalUserId = user.Id,
                FullName = user.FullName,
                PhoneNumber = user.PhoneNumber,
                Role = user.Role,
                EnrollmentNumber = enrollmentNumber ?? user.EnrollmentNo,
                IsApproved = user.IsApproved,
                CreatedAtUtc = user.CreatedAtUtc,
                DeletedAtUtc = DateTime.UtcNow,
                DeletedByAdminUserId = HttpContext.Session.GetInt32("UserId")
            };

            dbContext.DeletedUsers.Add(deletedUser);
            await dbContext.SaveChangesAsync();

            if (user.Role == UserRole.Student)
            {
                await dbContext.MaterialPageProgress
                    .Where(x => x.StudentId == userId)
                    .ExecuteDeleteAsync();

                await dbContext.ProgressTrackings
                    .Where(x => x.StudentId == userId)
                    .ExecuteDeleteAsync();

                await dbContext.QuizResults
                    .Where(x => x.StudentId == userId)
                    .ExecuteDeleteAsync();

                await dbContext.StudentEnrollments
                    .Where(x => x.StudentId == userId)
                    .ExecuteDeleteAsync();

                await dbContext.Students
                    .Where(x => x.UserId == userId)
                    .ExecuteDeleteAsync();
            }
            else if (user.Role == UserRole.Teacher)
            {
                await dbContext.TeacherSubjects
                    .Where(x => x.TeacherId == userId)
                    .ExecuteDeleteAsync();

                var teacherQuizIds = dbContext.Quizzes
                    .Where(x => x.TeacherId == userId)
                    .Select(x => x.Id);

                await dbContext.QuizQuestions
                    .Where(x => teacherQuizIds.Contains(x.QuizId))
                    .ExecuteDeleteAsync();

                await dbContext.QuizResults
                    .Where(x => teacherQuizIds.Contains(x.QuizId))
                    .ExecuteDeleteAsync();

                await dbContext.Quizzes
                    .Where(x => x.TeacherId == userId)
                    .ExecuteDeleteAsync();

                var teacherMaterialIds = dbContext.Materials
                    .Where(x => x.TeacherId == userId)
                    .Select(x => x.Id);

                await dbContext.ProgressTrackings
                    .Where(x => x.MaterialId != null && teacherMaterialIds.Contains(x.MaterialId.Value))
                    .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.MaterialId, (int?)null));

                await dbContext.Materials
                    .Where(x => x.TeacherId == userId)
                    .ExecuteDeleteAsync();

                await dbContext.Teachers
                    .Where(x => x.UserId == userId)
                    .ExecuteDeleteAsync();
            }

            dbContext.Users.Remove(user);
            await dbContext.SaveChangesAsync();

            await tx.CommitAsync();
            TempData["Success"] = "User deleted.";
        }
        catch
        {
            TempData["Error"] = "Failed to delete user.";
        }

        return RedirectToAction(nameof(Users), new { role });
    }

    public async Task<IActionResult> Semesters()
    {
        var semesters = await dbContext.Semesters.OrderBy(x => x.Name).ToListAsync();
        return View(semesters);
    }

    [HttpPost]
    public async Task<IActionResult> CreateSemester(string name)
    {
        if (!string.IsNullOrWhiteSpace(name))
        {
            dbContext.Semesters.Add(new Semester { Name = name.Trim() });
            await dbContext.SaveChangesAsync();
        }

        return RedirectToAction(nameof(Semesters));
    }

    public async Task<IActionResult> Subjects()
    {
        ViewBag.Semesters = await dbContext.Semesters.OrderBy(x => x.Name).ToListAsync();
        var subjects = await dbContext.Subjects.Include(x => x.Semester).OrderBy(x => x.Name).ToListAsync();
        return View(subjects);
    }

    [HttpPost]
    public async Task<IActionResult> CreateSubject(string name, int semesterId)
    {
        if (!string.IsNullOrWhiteSpace(name) && semesterId > 0)
        {
            dbContext.Subjects.Add(new Subject { Name = name.Trim(), SemesterId = semesterId });
            await dbContext.SaveChangesAsync();
        }

        return RedirectToAction(nameof(Subjects));
    }

    public async Task<IActionResult> AssignTeachers()
    {
        var teachers = await dbContext.Users
            .Where(x => x.Role == UserRole.Teacher && x.IsApproved)
            .OrderBy(x => x.FullName)
            .ToListAsync();

        var teacherUserIds = teachers.Select(x => x.Id).ToList();
        var teacherIdsByUserId = await dbContext.Teachers
            .Where(x => teacherUserIds.Contains(x.UserId))
            .Select(x => new { x.UserId, x.TeacherId })
            .ToDictionaryAsync(x => x.UserId, x => x.TeacherId);

        ViewBag.Teachers = teachers;
        ViewBag.TeacherIdsByUserId = teacherIdsByUserId;
        ViewBag.Subjects = await dbContext.Subjects.Include(x => x.Semester).ToListAsync();
        var mappings = await dbContext.TeacherSubjects
            .Include(x => x.Teacher)
            .Include(x => x.Subject)
            .ThenInclude(x => x!.Semester)
            .OrderBy(x => x.Teacher!.FullName)
            .ToListAsync();

        return View(mappings);
    }

    [HttpPost]
    public async Task<IActionResult> AssignTeacher(int teacherId, int subjectId)
    {
        var exists = await dbContext.TeacherSubjects.AnyAsync(x => x.TeacherId == teacherId && x.SubjectId == subjectId);
        if (!exists)
        {
            dbContext.TeacherSubjects.Add(new TeacherSubject { TeacherId = teacherId, SubjectId = subjectId });
            await dbContext.SaveChangesAsync();
        }

        return RedirectToAction(nameof(AssignTeachers));
    }

    public async Task<IActionResult> EnrollmentRequests()
    {
        var enrollments = await dbContext.StudentEnrollments
            .Include(x => x.Semester)
            .Include(x => x.Student)
            .OrderByDescending(x => x.EnrolledAtUtc)
            .ToListAsync();

        var studentIds = enrollments.Select(x => x.StudentId).Distinct().ToList();
        var enrollmentNumbers = await dbContext.Students
            .Where(x => studentIds.Contains(x.UserId))
            .Select(x => new { x.UserId, x.EnrollmentNumber })
            .ToDictionaryAsync(x => x.UserId, x => x.EnrollmentNumber);

        var rows = enrollments.Select(x => new EnrollmentRequestRowViewModel
        {
            EnrollmentId = x.Id,
            StudentName = x.Student?.FullName ?? "Unknown",
            EnrollmentNumber = enrollmentNumbers.TryGetValue(x.StudentId, out var number) ? number : "-",
            SemesterName = x.Semester?.Name ?? "-",
            Status = x.ApprovedAtUtc is null ? "Pending" : (x.IsApproved ? "Approved" : "Rejected"),
            EnrolledAtUtc = x.EnrolledAtUtc,
            ApprovedAtUtc = x.ApprovedAtUtc
        }).ToList();

        return View(rows);
    }

    public async Task<IActionResult> SemesterStudents()
    {
        var enrollments = await dbContext.StudentEnrollments
            .Include(x => x.Semester)
            .Include(x => x.Student)
            .OrderBy(x => x.Semester!.Name)
            .ThenByDescending(x => x.EnrolledAtUtc)
            .ToListAsync();

        var studentIds = enrollments.Select(x => x.StudentId).Distinct().ToList();
        var enrollmentNumbers = await dbContext.Students
            .Where(x => studentIds.Contains(x.UserId))
            .Select(x => new { x.UserId, x.EnrollmentNumber })
            .ToDictionaryAsync(x => x.UserId, x => x.EnrollmentNumber);

        var groups = enrollments
            .GroupBy(x => new { x.SemesterId, Name = x.Semester?.Name ?? "-" })
            .Select(group => new SemesterStudentsGroupViewModel
            {
                SemesterId = group.Key.SemesterId,
                SemesterName = group.Key.Name,
                Students = group.Select(e => new SemesterStudentRowViewModel
                {
                    EnrollmentId = e.Id,
                    StudentName = e.Student?.FullName ?? "Unknown",
                    PhoneNumber = e.Student?.PhoneNumber ?? "-",
                    EnrollmentNumber = enrollmentNumbers.TryGetValue(e.StudentId, out var number) ? number : "-",
                    Status = e.ApprovedAtUtc is null ? "Pending" : (e.IsApproved ? "Approved" : "Rejected"),
                    EnrolledAtUtc = e.EnrolledAtUtc,
                    ApprovedAtUtc = e.ApprovedAtUtc
                }).ToList()
            })
            .OrderBy(x => x.SemesterName)
            .ToList();

        return View(groups);
    }

    [HttpPost]
    public async Task<IActionResult> ApproveEnrollment(int enrollmentId, string? returnTo)
    {
        var enrollment = await dbContext.StudentEnrollments.FirstOrDefaultAsync(x => x.Id == enrollmentId);
        if (enrollment is null)
        {
            TempData["Error"] = "Enrollment request not found.";
            return RedirectToAction(returnTo == nameof(SemesterStudents) ? nameof(SemesterStudents) : nameof(EnrollmentRequests));
        }

        var adminUserId = HttpContext.Session.GetInt32("UserId");
        var admin = adminUserId.HasValue
            ? await dbContext.Admins.FirstOrDefaultAsync(x => x.UserId == adminUserId.Value)
            : null;

        enrollment.IsApproved = true;
        enrollment.ApprovedAtUtc = DateTime.UtcNow;
        enrollment.ApprovedByAdminId = admin?.Id;

        await dbContext.SaveChangesAsync();
        TempData["Success"] = "Enrollment approved.";
        return RedirectToAction(returnTo == nameof(SemesterStudents) ? nameof(SemesterStudents) : nameof(EnrollmentRequests));
    }

    [HttpPost]
    public async Task<IActionResult> RejectEnrollment(int enrollmentId, string? returnTo)
    {
        var enrollment = await dbContext.StudentEnrollments.FirstOrDefaultAsync(x => x.Id == enrollmentId);
        if (enrollment is null)
        {
            TempData["Error"] = "Enrollment request not found.";
            return RedirectToAction(returnTo == nameof(SemesterStudents) ? nameof(SemesterStudents) : nameof(EnrollmentRequests));
        }

        var adminUserId = HttpContext.Session.GetInt32("UserId");
        var admin = adminUserId.HasValue
            ? await dbContext.Admins.FirstOrDefaultAsync(x => x.UserId == adminUserId.Value)
            : null;

        enrollment.IsApproved = false;
        enrollment.ApprovedAtUtc = DateTime.UtcNow;
        enrollment.ApprovedByAdminId = admin?.Id;

        await dbContext.SaveChangesAsync();
        TempData["Success"] = "Enrollment rejected.";
        return RedirectToAction(returnTo == nameof(SemesterStudents) ? nameof(SemesterStudents) : nameof(EnrollmentRequests));
    }

    public async Task<IActionResult> Reports()
    {
        var totalPages = await dbContext.MaterialPages.CountAsync();

        var pageQuizIds = await dbContext.Quizzes
            .Where(x => x.MaterialPageId != null)
            .Select(x => x.Id)
            .ToListAsync();

        var quizQuestionCounts = pageQuizIds.Count == 0
            ? new Dictionary<int, int>()
            : await dbContext.QuizQuestions
                .Where(x => pageQuizIds.Contains(x.QuizId))
                .GroupBy(x => x.QuizId)
                .Select(g => new { QuizId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.QuizId, x => x.Count);

        var totalQuizQuestions = quizQuestionCounts.Values.Sum();

        var includedSemesterIds = await dbContext.Semesters.Select(x => x.Id).ToListAsync();
        var includedSemesters = await dbContext.Semesters.ToDictionaryAsync(x => x.Id, x => x.Name);

        var pageProgress = await dbContext.MaterialPageProgress
            .Include(x => x.Student)
            .Include(x => x.MaterialPage)
            .ThenInclude(x => x!.Material)
            .ToListAsync();

        var quizResults = await dbContext.QuizResults
            .Include(x => x.Quiz)
            .Where(x => x.Quiz!.MaterialPageId != null)
            .ToListAsync();

        var studentIds = pageProgress.Select(x => x.StudentId)
            .Concat(quizResults.Select(x => x.StudentId))
            .Distinct()
            .ToList();

        var studentEnrollmentNumbers = await dbContext.Students
            .Where(x => studentIds.Contains(x.UserId))
            .Select(x => new { x.UserId, x.EnrollmentNumber })
            .ToDictionaryAsync(x => x.UserId, x => x.EnrollmentNumber);

        var rows = new List<ProgressAnalyticsRowViewModel>();

        foreach (var studentId in studentIds)
        {
            var studentName = pageProgress.FirstOrDefault(x => x.StudentId == studentId)?.Student?.FullName
                              ?? (await dbContext.Users.Where(x => x.Id == studentId).Select(x => x.FullName).FirstOrDefaultAsync())
                              ?? "Unknown";

            var studentEnrollmentNo = studentEnrollmentNumbers.TryGetValue(studentId, out var enrollment)
                ? enrollment
                : null;

            var studentEnrollments = await dbContext.StudentEnrollments
                .Include(x => x.Semester)
                .Where(x => x.StudentId == studentId)
                .OrderBy(x => x.SemesterId)
                .ToListAsync();

            var enrollmentNo = string.IsNullOrWhiteSpace(studentEnrollmentNo) ? "-" : studentEnrollmentNo;

            var semester = studentEnrollments.Count switch
            {
                0 => "-",
                1 => studentEnrollments[0].Semester?.Name ?? (includedSemesters.TryGetValue(studentEnrollments[0].SemesterId, out var name) ? name : "-"),
                _ => "Multiple"
            };

            var studentPageProgress = pageProgress.Where(x => x.StudentId == studentId).ToList();
            var totalTimeSeconds = studentPageProgress.Sum(x => x.TimeSpentSeconds);
            var screenTimeMinutes = totalTimeSeconds / 60d;

            var effectiveTimeSecondsForFormula = studentPageProgress.Sum(p =>
            {
                var depth = Math.Clamp(p.MaxScrollDepthPercent, 0d, 100d);
                var factor = depth < 30d ? (depth / 30d) : 1d;
                return Math.Max(0d, p.TimeSpentSeconds) * Math.Clamp(factor, 0d, 1d);
            });

            var visitedPages = studentPageProgress
                .Select(x => x.MaterialPageId)
                .Distinct()
                .Count();

            var completedPages = studentPageProgress.Count(x => x.IsCompleted);
            var completionPercent = totalPages == 0 ? 0d : (double)completedPages / totalPages * 100d;

            var quizCorrectAnswers = quizResults
                .Where(x => x.StudentId == studentId)
                .GroupBy(x => x.QuizId)
                .Select(g => g.OrderByDescending(r => r.SubmittedAtUtc).First())
                .Select(attempt =>
                {
                    var perQuizTotal = attempt.TotalQuestions > 0
                        ? attempt.TotalQuestions
                        : (quizQuestionCounts.TryGetValue(attempt.QuizId, out var count) ? count : 0);

                    var perQuizCorrect = attempt.CorrectAnswers;
                    if (perQuizCorrect <= 0 && perQuizTotal > 0)
                    {
                        perQuizCorrect = (int)Math.Round((double)attempt.ScorePercent / 100d * perQuizTotal);
                    }

                    return Math.Max(0, perQuizCorrect);
                })
                .Sum();

            var quizScorePercent = totalQuizQuestions <= 0
                ? 0d
                : Math.Round((double)quizCorrectAnswers / totalQuizQuestions * 100d, 2);

            var averageScrollDepth = studentPageProgress.Count == 0
                ? 0d
                : studentPageProgress.Average(x => Math.Clamp(x.MaxScrollDepthPercent, 0d, 100d));

            var breakdown = progressService.CalculateBreakdown(
                totalActiveReadingSeconds: totalTimeSeconds,
                effectiveActiveReadingSecondsForFormula: effectiveTimeSecondsForFormula,
                totalPages: totalPages,
                completedPages: completedPages,
                averageQuizScorePercent: quizScorePercent,
                averageScrollDepthPercent: averageScrollDepth,
                idealMinutesPerPage: 6d);

            var finalProgress = breakdown.FinalProgressPercent;

            var status = progressService.GetProgressStatus(finalProgress);
            var barClass = status switch
            {
                "Skimmer" => "bg-danger",
                "NeedsImprovement" => "bg-warning",
                "Learning" => "bg-warning",
                "Progressing" => "bg-primary",
                "ActiveLearner" => "bg-success",
                "Mastered" => "bg-success",
                _ => "bg-secondary"
            };

            rows.Add(new ProgressAnalyticsRowViewModel
            {
                EnrollmentNo = enrollmentNo,
                StudentName = studentName,
                Semester = semester,
                ScreenTimeMinutes = Math.Round(screenTimeMinutes, 2),
                ScreenTimeHms = DurationFormatter.ToHms(totalTimeSeconds),
                QuizScorePercent = Math.Round(quizScorePercent, 2),
                QuizMarks = $"{quizCorrectAnswers}/{totalQuizQuestions}",
                CompletionPercent = Math.Round(completionPercent, 2),
                FinalProgressPercent = Math.Round(finalProgress, 2),
                Status = status,
                ProgressBarClass = barClass
            });
        }

        rows = rows
            .OrderByDescending(x => x.FinalProgressPercent)
            .ThenBy(x => x.StudentName)
            .ToList();

        var visitedPairsCount = pageProgress
            .Select(x => $"{x.StudentId}:{x.MaterialPageId}")
            .Distinct()
            .Count();

        var model = new ProgressAnalyticsDashboardViewModel
        {
            Rows = rows,
            AverageScreenTimeMinutes = rows.Count == 0 ? 0 : Math.Round(rows.Average(x => x.ScreenTimeMinutes), 2),
            AverageScreenTimePercentForFormula = rows.Count == 0
                ? 0
                : Math.Round(Math.Clamp(pageProgress.Sum(x => x.TimeSpentSeconds) / (Math.Max(visitedPairsCount, 1) * 360d) * 100d, 0d, 100d), 2),
            AverageQuizScorePercent = rows.Count == 0 ? 0 : Math.Round(rows.Average(x => x.QuizScorePercent), 2),
            AverageCompletionPercent = rows.Count == 0 ? 0 : Math.Round(rows.Average(x => x.CompletionPercent), 2)
        };

        return View(model);
    }

    private static (string status, string barClass) GetStatus(double finalProgress)
    {
        if (finalProgress >= 80)
        {
            return ("Excellent", "bg-success");
        }

        if (finalProgress >= 60)
        {
            return ("Good", "bg-primary");
        }

        if (finalProgress >= 40)
        {
            return ("Average", "bg-warning");
        }

        return ("Skimmer", "bg-danger");
    }
}
