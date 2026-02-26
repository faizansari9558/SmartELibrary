using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmartELibrary.Data;
using SmartELibrary.Filters;
using SmartELibrary.Models;
using SmartELibrary.Services;
using SmartELibrary.ViewModels;

namespace SmartELibrary.Controllers;

[RoleAuthorize(UserRole.Student)]
public class StudentController(ApplicationDbContext dbContext, IProgressService progressService, IStudentSemesterApprovalService approvalService) : Controller
{
    public async Task<IActionResult> Dashboard()
    {
        var studentId = HttpContext.Session.GetInt32("UserId") ?? 0;
        var enrollments = await dbContext.StudentEnrollments
            .Include(x => x.Semester)
            .Where(x => x.StudentId == studentId)
            .OrderByDescending(x => x.EnrolledAtUtc)
            .ToListAsync();

        var approvedSemesterIds = enrollments
            .Where(x => x.IsApproved)
            .Select(x => x.SemesterId)
            .Distinct()
            .ToList();

        ViewBag.EnrollmentStatuses = enrollments.Select(x => new StudentEnrollmentStatusViewModel
        {
            SemesterName = x.Semester?.Name ?? "-",
            Status = x.ApprovedAtUtc is null ? "Pending" : (x.IsApproved ? "Approved" : "Rejected"),
            EnrolledAtUtc = x.EnrolledAtUtc,
            ApprovedAtUtc = x.ApprovedAtUtc
        }).ToList();

        ViewBag.HasApprovedEnrollment = approvedSemesterIds.Count > 0;

        var subjects = await dbContext.Subjects
            .Include(x => x.Semester)
            .Where(x => approvedSemesterIds.Contains(x.SemesterId))
            .OrderBy(x => x.Semester!.Name)
            .ThenBy(x => x.Name)
            .Select(x => new StudentEnrolledSubjectRowViewModel
            {
                SemesterName = x.Semester!.Name,
                SubjectName = x.Name
            })
            .ToListAsync();

        var model = new StudentDashboardViewModel
        {
            Subjects = subjects
        };

        return View(model);
    }

    [HttpGet]
    public async Task<IActionResult> EnrollSemester()
    {
        ViewBag.Semesters = await dbContext.Semesters.Where(x => x.IsActive).OrderBy(x => x.Name).ToListAsync();
        return View(new SemesterEnrollViewModel());
    }

    [HttpPost]
    public async Task<IActionResult> EnrollSemester(SemesterEnrollViewModel model)
    {
        var studentId = HttpContext.Session.GetInt32("UserId") ?? 0;
        if (!ModelState.IsValid)
        {
            ViewBag.Semesters = await dbContext.Semesters.Where(x => x.IsActive).OrderBy(x => x.Name).ToListAsync();
            return View(model);
        }

        var exists = await dbContext.StudentEnrollments.AnyAsync(x => x.StudentId == studentId && x.SemesterId == model.SemesterId);
        if (!exists)
        {
            dbContext.StudentEnrollments.Add(new StudentEnrollment
            {
                StudentId = studentId,
                SemesterId = model.SemesterId,
                IsApproved = false,
                ApprovedAtUtc = null,
                ApprovedByAdminId = null,
                EnrolledAtUtc = DateTime.UtcNow
            });
            await dbContext.SaveChangesAsync();
            TempData["Success"] = "Enrollment submitted. Waiting for admin approval.";
        }

        return RedirectToAction(nameof(Dashboard));
    }

    public IActionResult WaitingForSemesterApproval()
    {
        return View();
    }

    [ServiceFilter(typeof(StudentSemesterApprovalFilter))]
    public async Task<IActionResult> Library(int? semesterId, int? subjectId, string? search)
    {
        var studentId = HttpContext.Session.GetInt32("UserId") ?? 0;
        var semesterIds = await dbContext.StudentEnrollments
            .Where(x => x.StudentId == studentId && x.IsApproved)
            .Select(x => x.SemesterId)
            .ToListAsync();

        ViewBag.Semesters = await dbContext.Semesters.Where(x => semesterIds.Contains(x.Id)).ToListAsync();
        ViewBag.Subjects = await dbContext.Subjects.Where(x => !semesterId.HasValue || x.SemesterId == semesterId).ToListAsync();

        var query = dbContext.Materials
            .Include(x => x.Subject)
            .Include(x => x.Semester)
            .AsQueryable();

        query = query.Where(x => x.IsPublic || semesterIds.Contains(x.SemesterId));

        if (semesterId.HasValue)
        {
            query = query.Where(x => x.SemesterId == semesterId.Value);
        }

        if (subjectId.HasValue)
        {
            query = query.Where(x => x.SubjectId == subjectId.Value);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            query = query.Where(x => x.Title.Contains(search) || x.Description.Contains(search));
        }

        var materials = await query.OrderByDescending(x => x.UploadedAtUtc).ToListAsync();
        return View(materials);
    }

    [ServiceFilter(typeof(StudentSemesterApprovalFilter))]
    public async Task<IActionResult> ReadMaterial(int id)
    {
        var studentId = HttpContext.Session.GetInt32("UserId") ?? 0;

        var pageCount = await dbContext.MaterialPages.CountAsync(x => x.MaterialId == id);
        if (pageCount > 0)
        {
            return RedirectToAction(nameof(ReadChapter), new { materialId = id, pageNumber = 1 });
        }

        var material = await dbContext.Materials
            .Include(x => x.Subject)
            .Include(x => x.Semester)
            .FirstOrDefaultAsync(x => x.Id == id);

        if (material is null)
        {
            return NotFound();
        }

        ViewBag.NextQuizId = await dbContext.Quizzes
            .Where(x => x.SubjectId == material.SubjectId)
            .OrderBy(x => x.Id)
            .Select(x => (int?)x.Id)
            .FirstOrDefaultAsync();

        return View(material);
    }

    [HttpGet]
    [ServiceFilter(typeof(StudentSemesterApprovalFilter))]
    public async Task<IActionResult> ReadChapter(int materialId, int pageNumber = 1)
    {
        var studentId = HttpContext.Session.GetInt32("UserId") ?? 0;

        var material = await dbContext.Materials
            .Include(x => x.Subject)
            .FirstOrDefaultAsync(x => x.Id == materialId);

        if (material is null)
        {
            return NotFound();
        }

        var totalPages = await dbContext.MaterialPages.CountAsync(x => x.MaterialId == materialId);
        if (totalPages == 0)
        {
            return RedirectToAction(nameof(ReadMaterial), new { id = materialId });
        }

        pageNumber = Math.Clamp(pageNumber, 1, totalPages);

        // Silent engagement tracking: do not block navigation based on completion.

        var page = await dbContext.MaterialPages
            .Where(x => x.MaterialId == materialId && x.PageNumber == pageNumber)
            .FirstOrDefaultAsync();

        if (page is null)
        {
            return NotFound();
        }

        var pageProgress = await dbContext.MaterialPageProgress
            .FirstOrDefaultAsync(x => x.StudentId == studentId && x.MaterialPageId == page.Id);

        if (pageProgress is null)
        {
            pageProgress = new MaterialPageProgress
            {
                StudentId = studentId,
                MaterialPageId = page.Id,
                StartedAtUtc = DateTime.UtcNow,
                IsCompleted = false
            };
            dbContext.MaterialPageProgress.Add(pageProgress);
            await dbContext.SaveChangesAsync();
        }
        else if (!pageProgress.IsCompleted && pageProgress.StartedAtUtc is null)
        {
            pageProgress.StartedAtUtc = DateTime.UtcNow;
            pageProgress.LastUpdatedUtc = DateTime.UtcNow;
            await dbContext.SaveChangesAsync();
        }

        var quiz = await dbContext.Quizzes
            .Include(x => x.QuizQuestions)
            .FirstOrDefaultAsync(x => x.MaterialPageId == page.Id);

        var quizId = quiz?.Id;
        var hasQuestions = quiz is not null && quiz.QuizQuestions.Count > 0;

        var model = new ReadMaterialPageViewModel
        {
            MaterialId = materialId,
            MaterialTitle = material.Title,
            PageId = page.Id,
            PageNumber = page.PageNumber,
            TotalPages = totalPages,
            PageTitle = page.Title,
            HtmlContent = page.HtmlContent,
            HasMandatoryQuizAfterThisPage = hasQuestions,
            QuizPassed = false,
            QuizId = quizId,
            TimeSpentSeconds = pageProgress.TimeSpentSeconds,
            StartedAtUtc = pageProgress.StartedAtUtc
        };

        return View("ReadChapter", model);
    }

    [HttpPost]
    [ServiceFilter(typeof(StudentSemesterApprovalFilter))]
    public async Task<IActionResult> CompletePage(int pageId, int activeSeconds, double maxScrollDepthPercent)
    {
        var studentId = HttpContext.Session.GetInt32("UserId") ?? 0;

        var page = await dbContext.MaterialPages
            .Include(x => x.Material)
            .FirstOrDefaultAsync(x => x.Id == pageId);

        if (page is null)
        {
            return NotFound();
        }

        var progress = await dbContext.MaterialPageProgress
            .FirstOrDefaultAsync(x => x.StudentId == studentId && x.MaterialPageId == pageId);

        // Progress/time/scroll must be recorded ONLY on first completion. Re-visits must not update anything.
        if (progress is not null && progress.IsCompleted)
        {
            var totalPagesExisting = await dbContext.MaterialPages.CountAsync(x => x.MaterialId == page.MaterialId);
            var nextExisting = Url.Action(nameof(ReadChapter), new
            {
                materialId = page.MaterialId,
                pageNumber = Math.Clamp(page.PageNumber + 1, 1, totalPagesExisting)
            });

            return Json(new
            {
                ok = true,
                requiresQuiz = false,
                quizId = (int?)null,
                nextUrl = nextExisting,
                isLast = page.PageNumber >= totalPagesExisting,
                recordedSeconds = Math.Round(progress.TimeSpentSeconds, 0)
            });
        }

        if (progress is null)
        {
            progress = new MaterialPageProgress
            {
                StudentId = studentId,
                MaterialPageId = pageId,
                StartedAtUtc = DateTime.UtcNow,
                IsCompleted = false
            };
            dbContext.MaterialPageProgress.Add(progress);
        }

        var now = DateTime.UtcNow;
        progress.StartedAtUtc ??= now;
        progress.CompletedAtUtc ??= now;

        var elapsedSeconds = progress.StartedAtUtc.HasValue
            ? (now - progress.StartedAtUtc.Value).TotalSeconds
            : 0d;

        // Keep time spent for analytics; do not block navigation.
        // Prefer client "active" time (focused/visible) when available; fallback to server elapsed.
        var effectiveSeconds = activeSeconds > 0
            ? Math.Max(0d, (double)activeSeconds)
            : Math.Max(0d, elapsedSeconds);
        if (effectiveSeconds > 0)
        {
            // First-time completion only: set once.
            progress.TimeSpentSeconds = effectiveSeconds;
        }

        // Store max scroll depth reached (0-100). This is used for engagement validation.
        progress.MaxScrollDepthPercent = Math.Clamp(maxScrollDepthPercent, 0d, 100d);

        // Stored per-page engagement flag (teacher/admin analytics only).
        progress.LowEngagement = effectiveSeconds > 0 && effectiveSeconds < 30;

        progress.IsCompleted = true;
        progress.LastUpdatedUtc = now;

        var quiz = await dbContext.Quizzes
            .Include(x => x.QuizQuestions)
            .FirstOrDefaultAsync(x => x.MaterialPageId == pageId);

        // Backward/teacher-UI compatibility: quizzes created via Teacher/CreateQuiz can be linked to a material
        // (MaterialId) without being linked to a specific page. Show such quiz at the end of the chapter.
        if (quiz is null || quiz.QuizQuestions.Count == 0)
        {
            var totalPagesForQuizFallback = await dbContext.MaterialPages.CountAsync(x => x.MaterialId == page.MaterialId);
            var isLastPage = page.PageNumber >= totalPagesForQuizFallback;
            if (isLastPage)
            {
                quiz = await dbContext.Quizzes
                    .Include(x => x.QuizQuestions)
                    .Where(x => x.MaterialId == page.MaterialId && x.MaterialPageId == null)
                    .OrderByDescending(x => x.Id)
                    .FirstOrDefaultAsync();
            }
        }

        var requiresQuiz = quiz is not null && quiz.QuizQuestions.Count > 0;
        var quizId = quiz?.Id;

        if (requiresQuiz && quizId.HasValue)
        {
            // Quiz popup must appear ONLY on first-time page completion AND only if not already attempted.
            var alreadyAttempted = await dbContext.QuizResults
                .AnyAsync(x => x.StudentId == studentId && x.QuizId == quizId.Value);
            requiresQuiz = !alreadyAttempted;
        }

        await dbContext.SaveChangesAsync();

        await UpdateMaterialProgressTrackingAsync(studentId, page.MaterialId);

        var nextPageNumber = page.PageNumber + 1;
        var totalPages = await dbContext.MaterialPages.CountAsync(x => x.MaterialId == page.MaterialId);
        var nextUrl = Url.Action(nameof(ReadChapter), new { materialId = page.MaterialId, pageNumber = Math.Clamp(nextPageNumber, 1, totalPages) });

        return Json(new
        {
            ok = true,
            requiresQuiz,
            quizId,
            nextUrl,
            isLast = page.PageNumber >= totalPages,
            recordedSeconds = Math.Round(progress.TimeSpentSeconds, 0)
        });
    }

    [HttpGet]
    [ServiceFilter(typeof(StudentSemesterApprovalFilter))]
    public async Task<IActionResult> GetPageQuiz(int pageId)
    {
        var quiz = await dbContext.Quizzes
            .Include(x => x.QuizQuestions)
            .FirstOrDefaultAsync(x => x.MaterialPageId == pageId);

        if (quiz is null || quiz.QuizQuestions.Count == 0)
        {
            return NotFound();
        }

        var questions = quiz.QuizQuestions
            .OrderBy(x => x.Id)
            .Select(x => new PageQuizQuestionDto
            {
                QuestionId = x.Id,
                QuestionText = x.QuestionText,
                OptionA = x.OptionA,
                OptionB = x.OptionB,
                OptionC = x.OptionC,
                OptionD = x.OptionD
            })
            .ToList();

        return Json(new PageQuizDetailsDto
        {
            QuizId = quiz.Id,
            Questions = questions
        });
    }

    [HttpPost]
    [ServiceFilter(typeof(StudentSemesterApprovalFilter))]
    public async Task<IActionResult> SubmitPageQuiz([FromBody] PageQuizAttemptViewModel model)
    {
        var studentId = HttpContext.Session.GetInt32("UserId") ?? 0;

        var page = await dbContext.MaterialPages
            .Include(x => x.Material)
            .FirstOrDefaultAsync(x => x.Id == model.PageId);

        if (page is null)
        {
            return NotFound();
        }

        var quiz = await dbContext.Quizzes
            .Include(x => x.QuizQuestions)
            .FirstOrDefaultAsync(x => x.Id == model.QuizId);

        if (quiz is null)
        {
            return NotFound();
        }

        // Quiz must belong to this page OR to this material (material-level quiz shown at chapter end).
        if (quiz.MaterialPageId != model.PageId && quiz.MaterialId != page.MaterialId)
        {
            return NotFound();
        }

        // Idempotency: if the student already submitted this page quiz once, do not re-score or update progress.
        var alreadySubmitted = await dbContext.QuizResults
            .AnyAsync(x => x.StudentId == studentId && x.QuizId == quiz.Id);

        if (alreadySubmitted)
        {
            var totalPagesExisting = await dbContext.MaterialPages.CountAsync(x => x.MaterialId == page.MaterialId);
            var isLastExisting = page.PageNumber >= totalPagesExisting;
            var nextExisting = isLastExisting
                ? Url.Action(nameof(Library))
                : Url.Action(nameof(ReadChapter), new
                {
                    materialId = page.MaterialId,
                    pageNumber = Math.Clamp(page.PageNumber + 1, 1, totalPagesExisting)
                });

            return Json(new { ok = true, nextUrl = nextExisting });
        }

        var total = quiz.QuizQuestions.Count;
        if (total == 0)
        {
            return NotFound();
        }

        var correct = quiz.QuizQuestions.Count(question =>
            model.Answers.TryGetValue(question.Id, out var answer) &&
            answer.Equals(question.CorrectOption, StringComparison.OrdinalIgnoreCase));

        var score = total == 0 ? 0m : (decimal)correct / total * 100m;

        dbContext.QuizResults.Add(new QuizResult
        {
            QuizId = quiz.Id,
            StudentId = studentId,
            CorrectAnswers = correct,
            TotalQuestions = total,
            ScorePercent = score
        });

        await dbContext.SaveChangesAsync();

        await UpdateMaterialProgressTrackingAsync(studentId, page.MaterialId);
        var totalPages = await dbContext.MaterialPages.CountAsync(x => x.MaterialId == page.MaterialId);
        var isLast = page.PageNumber >= totalPages;
        var nextUrl = isLast
            ? Url.Action(nameof(Library))
            : Url.Action(nameof(ReadChapter), new { materialId = page.MaterialId, pageNumber = Math.Clamp(page.PageNumber + 1, 1, totalPages) });

        // Silent quiz: score is stored for analytics but not shown to the student.
        return Json(new { ok = true, nextUrl });
    }

    [HttpGet]
    [ServiceFilter(typeof(StudentSemesterApprovalFilter))]
    public async Task<IActionResult> AttemptQuiz(int quizId)
    {
        var quiz = await dbContext.Quizzes
            .Include(x => x.QuizQuestions)
            .Include(x => x.Subject)
            .FirstOrDefaultAsync(x => x.Id == quizId);

        if (quiz is null)
        {
            return NotFound();
        }

        return View(quiz);
    }

    [HttpPost]
    [ServiceFilter(typeof(StudentSemesterApprovalFilter))]
    public async Task<IActionResult> AttemptQuiz(QuizAttemptViewModel model)
    {
        var quiz = await dbContext.Quizzes.Include(x => x.QuizQuestions).FirstOrDefaultAsync(x => x.Id == model.QuizId);
        if (quiz is null)
        {
            return NotFound();
        }

        var total = quiz.QuizQuestions.Count;
        var correct = quiz.QuizQuestions.Count(question =>
            model.Answers.TryGetValue(question.Id, out var answer) &&
            answer.Equals(question.CorrectOption, StringComparison.OrdinalIgnoreCase));

        var score = total == 0 ? 0 : (decimal)correct / total * 100;
        var studentId = HttpContext.Session.GetInt32("UserId") ?? 0;

        dbContext.QuizResults.Add(new QuizResult
        {
            QuizId = quiz.Id,
            StudentId = studentId,
            CorrectAnswers = correct,
            TotalQuestions = total,
            ScorePercent = score
        });

        await dbContext.SaveChangesAsync();

        TempData["QuizScore"] = Math.Round(score, 2).ToString();
        return RedirectToAction(nameof(Dashboard));
    }


    private async Task UpdateMaterialProgressTrackingAsync(int studentId, int materialId)
    {
        var materialInfo = await dbContext.Materials
            .Where(x => x.Id == materialId)
            .Select(x => new { x.Id, x.SubjectId, x.TopicId })
            .FirstOrDefaultAsync();

        if (materialInfo is null)
        {
            return;
        }

        var pageIds = await dbContext.MaterialPages
            .Where(x => x.MaterialId == materialId)
            .Select(x => x.Id)
            .ToListAsync();

        if (pageIds.Count == 0)
        {
            return;
        }

        var totalPages = pageIds.Count;

        var studentPageProgress = await dbContext.MaterialPageProgress
            .Where(x => x.StudentId == studentId && pageIds.Contains(x.MaterialPageId))
            .ToListAsync();

        var totalTimeSeconds = studentPageProgress.Sum(x => x.TimeSpentSeconds);

        // Scroll-depth validation: if a page's scroll depth is very low (<30%), reduce time contribution.
        // This never blocks navigation; it only impacts the analytics percentage.
        var effectiveTimeSecondsForFormula = studentPageProgress.Sum(p =>
        {
            var depth = Math.Clamp(p.MaxScrollDepthPercent, 0d, 100d);
            var factor = depth < 30d ? (depth / 30d) : 1d;
            return Math.Max(0d, p.TimeSpentSeconds) * Math.Clamp(factor, 0d, 1d);
        });

        var averageScrollDepth = studentPageProgress.Count == 0
            ? 0d
            : studentPageProgress.Average(x => Math.Clamp(x.MaxScrollDepthPercent, 0d, 100d));
        var completedPages = studentPageProgress.Count(x => x.IsCompleted);
        var completionPercent = totalPages == 0 ? 0d : (double)completedPages / totalPages * 100d;

        var quizIds = await dbContext.Quizzes
            .Where(x => x.MaterialId == materialId && x.MaterialPageId != null)
            .Select(x => x.Id)
            .ToListAsync();

        var quizQuestionCounts = quizIds.Count == 0
            ? new Dictionary<int, int>()
            : await dbContext.QuizQuestions
                .Where(x => quizIds.Contains(x.QuizId))
                .GroupBy(x => x.QuizId)
                .Select(g => new { QuizId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.QuizId, x => x.Count);

        var quizTotalQuestions = quizQuestionCounts.Values.Sum();

        var latestQuizAttempts = new List<(int QuizId, int CorrectAnswers, int TotalQuestions, decimal ScorePercent)>();

        if (quizIds.Count > 0)
        {
            // EF Core provider translation for GroupBy(...).OrderBy(...).First() can fail on MySQL.
            // Pull results into memory, then compute latest attempt per quiz.
            var quizResults = await dbContext.QuizResults
                .Where(x => x.StudentId == studentId && quizIds.Contains(x.QuizId))
                .OrderByDescending(x => x.SubmittedAtUtc)
                .ToListAsync();

            latestQuizAttempts = quizResults
                .GroupBy(x => x.QuizId)
                .Select(g => g.First())
                .Select(x => (x.QuizId, x.CorrectAnswers, x.TotalQuestions, x.ScorePercent))
                .ToList();
        }

        var quizCorrectAnswers = 0;
        foreach (var attempt in latestQuizAttempts)
        {
            var perQuizTotal = attempt.TotalQuestions > 0
                ? attempt.TotalQuestions
                : (quizQuestionCounts.TryGetValue(attempt.QuizId, out var count) ? count : 0);

            var perQuizCorrect = attempt.CorrectAnswers;
            if (perQuizCorrect <= 0 && perQuizTotal > 0)
            {
                // Backward compatibility: older rows may not have correct/total stored.
                perQuizCorrect = (int)Math.Round((double)attempt.ScorePercent / 100d * perQuizTotal);
            }

            quizCorrectAnswers += Math.Max(0, perQuizCorrect);
        }

        // If quiz not attempted -> correct remains 0, total remains based on created questions.
        // If no quiz exists -> total=0 and score=0.
        var quizScorePercent = quizTotalQuestions <= 0
            ? 0d
            : Math.Round((double)quizCorrectAnswers / quizTotalQuestions * 100d, 2);

        var breakdown = progressService.CalculateBreakdown(
            totalActiveReadingSeconds: totalTimeSeconds,
            effectiveActiveReadingSecondsForFormula: effectiveTimeSecondsForFormula,
            totalPages: totalPages,
            completedPages: completedPages,
            averageQuizScorePercent: quizScorePercent,
            averageScrollDepthPercent: averageScrollDepth,
            idealMinutesPerPage: 6d);

        var minCompletedPageSeconds = studentPageProgress
            .Where(x => x.IsCompleted)
            .Select(x => x.TimeSpentSeconds)
            .DefaultIfEmpty(0d)
            .Min();

        var tracking = await dbContext.ProgressTrackings
            .FirstOrDefaultAsync(x => x.StudentId == studentId && x.MaterialId == materialId);

        if (tracking is null)
        {
            tracking = new ProgressTracking
            {
                StudentId = studentId,
                SubjectId = materialInfo.SubjectId,
                TopicId = materialInfo.TopicId,
                MaterialId = materialInfo.Id
            };
            dbContext.ProgressTrackings.Add(tracking);
        }

        tracking.ScreenTimeSeconds = totalTimeSeconds;
        tracking.ScrollDepthPercent = breakdown.ScrollDepthPercent;
        tracking.CompletionPercent = breakdown.CompletionPercent;
        tracking.QuizScorePercent = quizScorePercent;
        tracking.QuizCorrectAnswers = quizCorrectAnswers;
        tracking.QuizTotalQuestions = quizTotalQuestions;
        tracking.ProgressPercent = breakdown.FinalProgressPercent;
        tracking.LastUpdatedUtc = DateTime.UtcNow;
        tracking.IsLowEngagementAlert = studentPageProgress.Any(x => x.IsCompleted && x.LowEngagement)
                                      || progressService.IsLowEngagement(minCompletedPageSeconds);

        await dbContext.SaveChangesAsync();
    }

    [HttpGet]
    [ServiceFilter(typeof(StudentSemesterApprovalFilter))]
    public async Task<IActionResult> GetProgressBreakdown(int materialId)
    {
        var studentId = HttpContext.Session.GetInt32("UserId") ?? 0;

        var pageIds = await dbContext.MaterialPages
            .Where(x => x.MaterialId == materialId)
            .Select(x => x.Id)
            .ToListAsync();

        var totalPages = pageIds.Count;

        var studentPageProgress = await dbContext.MaterialPageProgress
            .Where(x => x.StudentId == studentId && pageIds.Contains(x.MaterialPageId))
            .ToListAsync();

        var completedPages = studentPageProgress.Count(x => x.IsCompleted);
        var totalTimeSeconds = studentPageProgress.Sum(x => x.TimeSpentSeconds);

        var completionPercent = totalPages <= 0 ? 0d : (double)completedPages / totalPages * 100d;

        var effectiveTimeSecondsForFormula = studentPageProgress.Sum(p =>
        {
            var depth = Math.Clamp(p.MaxScrollDepthPercent, 0d, 100d);
            var factor = depth < 30d ? (depth / 30d) : 1d;
            return Math.Max(0d, p.TimeSpentSeconds) * Math.Clamp(factor, 0d, 1d);
        });

        var averageScrollDepth = studentPageProgress.Count == 0
            ? 0d
            : studentPageProgress.Average(x => Math.Clamp(x.MaxScrollDepthPercent, 0d, 100d));

        var quizIds = await dbContext.Quizzes
            .Where(x => x.MaterialId == materialId && x.MaterialPageId != null)
            .Select(x => x.Id)
            .ToListAsync();

        var quizQuestionCounts = quizIds.Count == 0
            ? new Dictionary<int, int>()
            : await dbContext.QuizQuestions
                .Where(x => quizIds.Contains(x.QuizId))
                .GroupBy(x => x.QuizId)
                .Select(g => new { QuizId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.QuizId, x => x.Count);

        var quizTotalQuestions = quizQuestionCounts.Values.Sum();

        var latestQuizAttempts = new List<(int QuizId, int CorrectAnswers, int TotalQuestions, decimal ScorePercent)>();

        if (quizIds.Count > 0)
        {
            var quizResults = await dbContext.QuizResults
                .Where(x => x.StudentId == studentId && quizIds.Contains(x.QuizId))
                .OrderByDescending(x => x.SubmittedAtUtc)
                .ToListAsync();

            latestQuizAttempts = quizResults
                .GroupBy(x => x.QuizId)
                .Select(g => g.First())
                .Select(x => (x.QuizId, x.CorrectAnswers, x.TotalQuestions, x.ScorePercent))
                .ToList();
        }

        var quizCorrectAnswers = 0;
        foreach (var attempt in latestQuizAttempts)
        {
            var perQuizTotal = attempt.TotalQuestions > 0
                ? attempt.TotalQuestions
                : (quizQuestionCounts.TryGetValue(attempt.QuizId, out var count) ? count : 0);

            var perQuizCorrect = attempt.CorrectAnswers;
            if (perQuizCorrect <= 0 && perQuizTotal > 0)
            {
                // Backward compatibility: older rows may not have correct/total stored.
                perQuizCorrect = (int)Math.Round((double)attempt.ScorePercent / 100d * perQuizTotal);
            }

            quizCorrectAnswers += Math.Max(0, perQuizCorrect);
        }

        var quizScorePercent = quizTotalQuestions <= 0
            ? 0d
            : Math.Round((double)quizCorrectAnswers / quizTotalQuestions * 100d, 2);

        var hasAnyActivity = studentPageProgress.Count > 0 || latestQuizAttempts.Count > 0;
        var status = completionPercent >= 100d
            ? "Completed"
            : hasAnyActivity
                ? "In Progress"
                : "Not Started";

        var breakdown = progressService.CalculateBreakdown(
            totalActiveReadingSeconds: totalTimeSeconds,
            effectiveActiveReadingSecondsForFormula: effectiveTimeSecondsForFormula,
            totalPages: totalPages,
            completedPages: completedPages,
            averageQuizScorePercent: quizScorePercent,
            averageScrollDepthPercent: averageScrollDepth,
            idealMinutesPerPage: 6d);

        return Json(new
        {
            ok = true,
            breakdown.ScreenTimeMinutes,
            ScreenTimeHms = DurationFormatter.ToHms(totalTimeSeconds),
            breakdown.IdealReadingReferenceMinutes,
            breakdown.ScrollDepthPercent,
            breakdown.QuizScorePercent,
            QuizMarks = $"{quizCorrectAnswers}/{quizTotalQuestions}",
            QuizCorrectAnswers = quizCorrectAnswers,
            QuizTotalQuestions = quizTotalQuestions,
            breakdown.CompletionPercent,
            breakdown.FinalProgressPercent,
            breakdown.ScreenTimePercentage
            ,Status = status
        });
    }
}
