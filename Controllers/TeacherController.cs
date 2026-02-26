using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmartELibrary.Data;
using SmartELibrary.Filters;
using SmartELibrary.Models;
using SmartELibrary.Services;
using SmartELibrary.ViewModels;

namespace SmartELibrary.Controllers;

[RoleAuthorize(UserRole.Teacher)]
public class TeacherController(ApplicationDbContext dbContext, IProgressService progressService) : Controller
{
    // Soft validation: approximate "one structured unit" per page.
    // Rule: ~60 lines OR equivalent character length (plain text extracted from rich text).
    private const int MaxPagePlainTextChars = RichTextContentLengthValidator.MaxPlainTextChars;

    public async Task<IActionResult> Dashboard()
    {
        var teacherId = HttpContext.Session.GetInt32("UserId") ?? 0;
        ViewBag.MaterialCount = await dbContext.Materials.CountAsync(x => x.TeacherId == teacherId);
        ViewBag.QuizCount = await dbContext.Quizzes.CountAsync(x => x.TeacherId == teacherId);

        var subjectIds = await dbContext.TeacherSubjects
            .Where(x => x.TeacherId == teacherId)
            .Select(x => x.SubjectId)
            .ToListAsync();

        ViewBag.LowEngagementCount = await dbContext.ProgressTrackings
            .CountAsync(x => subjectIds.Contains(x.SubjectId) && x.IsLowEngagementAlert);

        return View();
    }

    [HttpGet]
    public async Task<IActionResult> UploadMaterial()
    {
        var teacherId = HttpContext.Session.GetInt32("UserId") ?? 0;
        ViewBag.Subjects = await dbContext.Subjects
            .Where(x => dbContext.TeacherSubjects.Any(ts => ts.TeacherId == teacherId && ts.SubjectId == x.Id))
            .Include(x => x.Semester)
            .ToListAsync();

        ViewBag.MaxPageContentLength = MaxPagePlainTextChars;

        return View(new ChapterUploadViewModel());
    }

    [HttpPost]
    public async Task<IActionResult> UploadMaterial(ChapterUploadViewModel model)
    {
        var teacherId = HttpContext.Session.GetInt32("UserId") ?? 0;
        var teacherSubjects = await dbContext.Subjects
            .Where(x => dbContext.TeacherSubjects.Any(ts => ts.TeacherId == teacherId && ts.SubjectId == x.Id))
            .Include(x => x.Semester)
            .ToListAsync();

        var selectedSubject = teacherSubjects.FirstOrDefault(x => x.Id == model.SubjectId);
        if (selectedSubject is null)
        {
            ModelState.AddModelError(nameof(model.SubjectId), "Please select a valid assigned subject.");
        }
        else
        {
            model.SemesterId = selectedSubject.SemesterId;
        }

        if (model.Pages.Count < 2)
        {
            ModelState.AddModelError(nameof(model.Pages), "Add at least 2 pages.");
        }

        for (var i = 0; i < model.Pages.Count; i++)
        {
            var page = model.Pages[i];
            if (string.IsNullOrWhiteSpace(page.PageTitle))
            {
                ModelState.AddModelError($"Pages[{i}].PageTitle", "Page title is required.");
            }

            if (string.IsNullOrWhiteSpace(page.HtmlContent))
            {
                ModelState.AddModelError($"Pages[{i}].HtmlContent", "Page content is required.");
            }
            else if (!RichTextContentLengthValidator.IsWithinLimit(page.HtmlContent, out _))
            {
                ModelState.AddModelError($"Pages[{i}].HtmlContent", RichTextContentLengthValidator.TooLongMessage);
            }

            if (page.Questions.Count > 0)
            {
                for (var q = 0; q < page.Questions.Count; q++)
                {
                    var question = page.Questions[q];
                    if (string.IsNullOrWhiteSpace(question.QuestionText) ||
                        string.IsNullOrWhiteSpace(question.OptionA) ||
                        string.IsNullOrWhiteSpace(question.OptionB) ||
                        string.IsNullOrWhiteSpace(question.OptionC) ||
                        string.IsNullOrWhiteSpace(question.OptionD))
                    {
                        ModelState.AddModelError(string.Empty, $"Quiz details are required for page {i + 1}, question {q + 1}.");
                    }

                    var correct = (question.CorrectOption ?? string.Empty).Trim().ToUpperInvariant();
                    if (correct is not ("A" or "B" or "C" or "D"))
                    {
                        ModelState.AddModelError(string.Empty, $"Correct option must be A, B, C, or D for page {i + 1}, question {q + 1}.");
                    }
                }
            }
        }

        if (!ModelState.IsValid)
        {
            ViewBag.Subjects = teacherSubjects;
            ViewBag.MaxPageContentLength = MaxPagePlainTextChars;
            return View(model);
        }

        var material = new Material
        {
            Title = model.Title,
            Description = model.Description,
            MaterialType = MaterialType.Notes,
            SemesterId = model.SemesterId,
            SubjectId = model.SubjectId,
            TopicId = model.TopicId,
            IsPublic = model.IsPublic,
            TeacherId = teacherId,
            FilePathOrUrl = "RICH_TEXT"
        };

        dbContext.Materials.Add(material);
        await dbContext.SaveChangesAsync();

        var pages = new List<MaterialPage>();
        for (var i = 0; i < model.Pages.Count; i++)
        {
            var input = model.Pages[i];
            var page = new MaterialPage
            {
                MaterialId = material.Id,
                PageNumber = i + 1,
                Title = input.PageTitle.Trim(),
                HtmlContent = input.HtmlContent
            };
            pages.Add(page);
            dbContext.MaterialPages.Add(page);
        }

        await dbContext.SaveChangesAsync();

        for (var i = 0; i < model.Pages.Count; i++)
        {
            var input = model.Pages[i];
            if (input.Questions.Count == 0)
            {
                continue;
            }

            var quizTitle = string.IsNullOrWhiteSpace(input.QuizTitle)
                ? $"Quiz - {material.Title} - Page {i + 1}"
                : input.QuizTitle.Trim();

            var quiz = new Quiz
            {
                Title = quizTitle,
                SubjectId = material.SubjectId,
                TopicId = material.TopicId,
                MaterialId = material.Id,
                MaterialPageId = pages[i].Id,
                TeacherId = teacherId,
                QuizQuestions = input.Questions.Select(question => new QuizQuestion
                {
                    QuestionText = question.QuestionText,
                    OptionA = question.OptionA,
                    OptionB = question.OptionB,
                    OptionC = question.OptionC,
                    OptionD = question.OptionD,
                    CorrectOption = (question.CorrectOption ?? "A").Trim().ToUpperInvariant()
                }).ToList()
            };

            dbContext.Quizzes.Add(quiz);
        }

        await dbContext.SaveChangesAsync();

        return RedirectToAction(nameof(Materials));
    }

    [HttpGet]
    public async Task<IActionResult> EditMaterial(int materialId)
    {
        var teacherId = HttpContext.Session.GetInt32("UserId") ?? 0;
        var material = await dbContext.Materials
            .Include(x => x.Pages)
            .FirstOrDefaultAsync(x => x.Id == materialId && x.TeacherId == teacherId);

        if (material is null)
        {
            return NotFound();
        }

        if (!string.Equals(material.FilePathOrUrl, "RICH_TEXT", StringComparison.OrdinalIgnoreCase))
        {
            TempData["Error"] = "Only rich text materials can be edited as chapters.";
            return RedirectToAction(nameof(Materials));
        }

        var pageIds = material.Pages.Select(x => x.Id).ToList();
        var quizzes = await dbContext.Quizzes
            .Include(x => x.QuizQuestions)
            .Where(x => x.MaterialPageId.HasValue && pageIds.Contains(x.MaterialPageId.Value))
            .ToListAsync();

        var quizLookup = quizzes.ToDictionary(x => x.MaterialPageId!.Value, x => x);

        var model = new ChapterUploadViewModel
        {
            Title = material.Title,
            Description = material.Description,
            SubjectId = material.SubjectId,
            SemesterId = material.SemesterId,
            TopicId = material.TopicId,
            IsPublic = material.IsPublic,
            Pages = material.Pages
                .OrderBy(x => x.PageNumber)
                .Select(x => new ChapterPageInputViewModel
                {
                    PageId = x.Id,
                    PageTitle = x.Title,
                    HtmlContent = x.HtmlContent,
                    QuizTitle = quizLookup.TryGetValue(x.Id, out var quiz) ? quiz.Title : string.Empty,
                    Questions = quizLookup.TryGetValue(x.Id, out var pageQuiz)
                        ? pageQuiz.QuizQuestions
                            .OrderBy(q => q.Id)
                            .Select(q => new ChapterPageQuestionInputViewModel
                            {
                                QuestionText = q.QuestionText,
                                OptionA = q.OptionA,
                                OptionB = q.OptionB,
                                OptionC = q.OptionC,
                                OptionD = q.OptionD,
                                CorrectOption = q.CorrectOption
                            })
                            .ToList()
                        : new List<ChapterPageQuestionInputViewModel>()
                })
                .ToList()
        };

        ViewBag.Subjects = await dbContext.Subjects
            .Where(x => dbContext.TeacherSubjects.Any(ts => ts.TeacherId == teacherId && ts.SubjectId == x.Id))
            .Include(x => x.Semester)
            .ToListAsync();
        ViewBag.MaxPageContentLength = MaxPagePlainTextChars;
        ViewBag.IsEdit = true;
        ViewBag.MaterialId = materialId;

        return View("UploadMaterial", model);
    }

    [HttpPost]
    public async Task<IActionResult> EditMaterial(int materialId, ChapterUploadViewModel model)
    {
        var teacherId = HttpContext.Session.GetInt32("UserId") ?? 0;
        var material = await dbContext.Materials
            .Include(x => x.Pages)
            .FirstOrDefaultAsync(x => x.Id == materialId && x.TeacherId == teacherId);

        if (material is null)
        {
            return NotFound();
        }

        var teacherSubjects = await dbContext.Subjects
            .Where(x => dbContext.TeacherSubjects.Any(ts => ts.TeacherId == teacherId && ts.SubjectId == x.Id))
            .Include(x => x.Semester)
            .ToListAsync();

        var selectedSubject = teacherSubjects.FirstOrDefault(x => x.Id == model.SubjectId);
        if (selectedSubject is null)
        {
            ModelState.AddModelError(nameof(model.SubjectId), "Please select a valid assigned subject.");
        }
        else
        {
            model.SemesterId = selectedSubject.SemesterId;
        }

        if (model.Pages.Count < 2)
        {
            ModelState.AddModelError(nameof(model.Pages), "Add at least 2 pages.");
        }

        for (var i = 0; i < model.Pages.Count; i++)
        {
            var page = model.Pages[i];
            if (string.IsNullOrWhiteSpace(page.PageTitle))
            {
                ModelState.AddModelError($"Pages[{i}].PageTitle", "Page title is required.");
            }

            if (string.IsNullOrWhiteSpace(page.HtmlContent))
            {
                ModelState.AddModelError($"Pages[{i}].HtmlContent", "Page content is required.");
            }
            else if (!RichTextContentLengthValidator.IsWithinLimit(page.HtmlContent, out _))
            {
                ModelState.AddModelError($"Pages[{i}].HtmlContent", RichTextContentLengthValidator.TooLongMessage);
            }

            if (page.Questions.Count > 0)
            {
                for (var q = 0; q < page.Questions.Count; q++)
                {
                    var question = page.Questions[q];
                    if (string.IsNullOrWhiteSpace(question.QuestionText) ||
                        string.IsNullOrWhiteSpace(question.OptionA) ||
                        string.IsNullOrWhiteSpace(question.OptionB) ||
                        string.IsNullOrWhiteSpace(question.OptionC) ||
                        string.IsNullOrWhiteSpace(question.OptionD))
                    {
                        ModelState.AddModelError(string.Empty, $"Quiz details are required for page {i + 1}, question {q + 1}.");
                    }

                    var correct = (question.CorrectOption ?? string.Empty).Trim().ToUpperInvariant();
                    if (correct is not ("A" or "B" or "C" or "D"))
                    {
                        ModelState.AddModelError(string.Empty, $"Correct option must be A, B, C, or D for page {i + 1}, question {q + 1}.");
                    }
                }
            }
        }

        var incomingIds = model.Pages
            .Where(x => x.PageId.HasValue)
            .Select(x => x.PageId!.Value)
            .ToHashSet();

        var removedIds = material.Pages
            .Where(x => !incomingIds.Contains(x.Id))
            .Select(x => x.Id)
            .ToList();

        if (removedIds.Count > 0)
        {
            var quizIds = await dbContext.Quizzes
                .Where(x => x.MaterialPageId.HasValue && removedIds.Contains(x.MaterialPageId.Value))
                .Select(x => x.Id)
                .ToListAsync();

            if (quizIds.Count > 0)
            {
                var hasResults = await dbContext.QuizResults.AnyAsync(x => quizIds.Contains(x.QuizId));
                if (hasResults)
                {
                    ModelState.AddModelError(string.Empty, "Cannot remove a page that has quiz attempts.");
                }
            }
        }

        if (!ModelState.IsValid)
        {
            ViewBag.Subjects = teacherSubjects;
            ViewBag.MaxPageContentLength = MaxPagePlainTextChars;
            ViewBag.IsEdit = true;
            ViewBag.MaterialId = materialId;
            return View("UploadMaterial", model);
        }

        material.Title = model.Title;
        material.Description = model.Description;
        material.SubjectId = model.SubjectId;
        material.SemesterId = model.SemesterId;
        material.TopicId = model.TopicId;
        material.IsPublic = model.IsPublic;

        var newPageQuizzes = new List<(int pageNumber, ChapterPageInputViewModel input)>();

        for (var i = 0; i < model.Pages.Count; i++)
        {
            var input = model.Pages[i];
            var pageNumber = i + 1;
            MaterialPage? targetPage = null;
            if (input.PageId.HasValue)
            {
                targetPage = material.Pages.FirstOrDefault(x => x.Id == input.PageId.Value);
                if (targetPage is not null)
                {
                    targetPage.Title = input.PageTitle.Trim();
                    targetPage.HtmlContent = input.HtmlContent;
                    targetPage.PageNumber = pageNumber;
                }
            }
            else
            {
                targetPage = new MaterialPage
                {
                    MaterialId = material.Id,
                    PageNumber = pageNumber,
                    Title = input.PageTitle.Trim(),
                    HtmlContent = input.HtmlContent
                };
                dbContext.MaterialPages.Add(targetPage);
            }

            if (targetPage is null)
            {
                continue;
            }

            if (targetPage.Id == 0)
            {
                if (input.Questions.Count > 0)
                {
                    newPageQuizzes.Add((pageNumber, input));
                }
                continue;
            }

            var existingQuiz = await dbContext.Quizzes
                .Include(x => x.QuizQuestions)
                .FirstOrDefaultAsync(x => x.MaterialPageId == targetPage.Id);

            if (input.Questions.Count == 0)
            {
                if (existingQuiz is not null)
                {
                    var hasResults = await dbContext.QuizResults.AnyAsync(x => x.QuizId == existingQuiz.Id);
                    if (hasResults)
                    {
                        ModelState.AddModelError(string.Empty, "Cannot remove quiz from a page with quiz attempts.");
                    }
                    else
                    {
                        dbContext.QuizQuestions.RemoveRange(existingQuiz.QuizQuestions);
                        dbContext.Quizzes.Remove(existingQuiz);
                    }
                }

                continue;
            }

            var quizTitle = string.IsNullOrWhiteSpace(input.QuizTitle)
                ? $"Quiz - {material.Title} - Page {pageNumber}"
                : input.QuizTitle.Trim();

            if (existingQuiz is null)
            {
                existingQuiz = new Quiz
                {
                    Title = quizTitle,
                    SubjectId = material.SubjectId,
                    TopicId = material.TopicId,
                    MaterialId = material.Id,
                    MaterialPageId = targetPage.Id,
                    TeacherId = teacherId
                };
                dbContext.Quizzes.Add(existingQuiz);
            }
            else
            {
                existingQuiz.Title = quizTitle;
            }

            if (existingQuiz.QuizQuestions.Count > 0)
            {
                dbContext.QuizQuestions.RemoveRange(existingQuiz.QuizQuestions);
            }

            existingQuiz.QuizQuestions = input.Questions.Select(question => new QuizQuestion
            {
                QuestionText = question.QuestionText,
                OptionA = question.OptionA,
                OptionB = question.OptionB,
                OptionC = question.OptionC,
                OptionD = question.OptionD,
                CorrectOption = (question.CorrectOption ?? "A").Trim().ToUpperInvariant()
            }).ToList();
        }

        if (removedIds.Count > 0)
        {
            var quizzesToRemove = await dbContext.Quizzes
                .Where(x => x.MaterialPageId.HasValue && removedIds.Contains(x.MaterialPageId.Value))
                .Include(x => x.QuizQuestions)
                .ToListAsync();

            if (quizzesToRemove.Count > 0)
            {
                var questions = quizzesToRemove.SelectMany(x => x.QuizQuestions).ToList();
                dbContext.QuizQuestions.RemoveRange(questions);
                dbContext.Quizzes.RemoveRange(quizzesToRemove);
            }

            var toRemove = material.Pages.Where(x => removedIds.Contains(x.Id)).ToList();
            dbContext.MaterialPages.RemoveRange(toRemove);
        }

        if (!ModelState.IsValid)
        {
            ViewBag.Subjects = teacherSubjects;
            ViewBag.MaxPageContentLength = MaxPagePlainTextChars;
            ViewBag.IsEdit = true;
            ViewBag.MaterialId = materialId;
            return View("UploadMaterial", model);
        }

        await dbContext.SaveChangesAsync();

        if (newPageQuizzes.Count > 0)
        {
            foreach (var (pageNumber, input) in newPageQuizzes)
            {
                var page = await dbContext.MaterialPages
                    .FirstOrDefaultAsync(x => x.MaterialId == material.Id && x.PageNumber == pageNumber);

                if (page is null)
                {
                    continue;
                }

                var quizTitle = string.IsNullOrWhiteSpace(input.QuizTitle)
                    ? $"Quiz - {material.Title} - Page {pageNumber}"
                    : input.QuizTitle.Trim();

                var quiz = new Quiz
                {
                    Title = quizTitle,
                    SubjectId = material.SubjectId,
                    TopicId = material.TopicId,
                    MaterialId = material.Id,
                    MaterialPageId = page.Id,
                    TeacherId = teacherId,
                    QuizQuestions = input.Questions.Select(question => new QuizQuestion
                    {
                        QuestionText = question.QuestionText,
                        OptionA = question.OptionA,
                        OptionB = question.OptionB,
                        OptionC = question.OptionC,
                        OptionD = question.OptionD,
                        CorrectOption = (question.CorrectOption ?? "A").Trim().ToUpperInvariant()
                    }).ToList()
                };

                dbContext.Quizzes.Add(quiz);
            }

            await dbContext.SaveChangesAsync();
        }

        TempData["Success"] = "Material updated.";
        return RedirectToAction(nameof(Materials));
    }

    public async Task<IActionResult> Materials()
    {
        var teacherId = HttpContext.Session.GetInt32("UserId") ?? 0;
        var materials = await dbContext.Materials
            .Include(x => x.Semester)
            .Include(x => x.Subject)
            .Include(x => x.Pages)
            .Where(x => x.TeacherId == teacherId)
            .OrderByDescending(x => x.UploadedAtUtc)
            .ToListAsync();

        return View(materials);
    }

    [HttpGet]
    public async Task<IActionResult> PreviewChapter(int materialId, int pageNumber = 1)
    {
        var material = await dbContext.Materials
            .FirstOrDefaultAsync(x => x.Id == materialId);

        if (material is null)
        {
            return NotFound();
        }

        var totalPages = await dbContext.MaterialPages.CountAsync(x => x.MaterialId == materialId);
        if (totalPages == 0)
        {
            return RedirectToAction(nameof(Materials));
        }

        pageNumber = Math.Clamp(pageNumber, 1, totalPages);

        var page = await dbContext.MaterialPages
            .Where(x => x.MaterialId == materialId && x.PageNumber == pageNumber)
            .FirstOrDefaultAsync();

        if (page is null)
        {
            return NotFound();
        }

        var model = new ReadMaterialPageViewModel
        {
            MaterialId = materialId,
            MaterialTitle = material.Title,
            PageId = page.Id,
            PageNumber = page.PageNumber,
            TotalPages = totalPages,
            PageTitle = page.Title,
            HtmlContent = page.HtmlContent,
            HasMandatoryQuizAfterThisPage = false,
            QuizPassed = true,
            QuizId = null
        };

        return View(model);
    }

    [HttpGet]
    public async Task<IActionResult> CreateQuiz()
    {
        var teacherId = HttpContext.Session.GetInt32("UserId") ?? 0;
        ViewBag.Subjects = await dbContext.Subjects
            .Where(x => dbContext.TeacherSubjects.Any(ts => ts.TeacherId == teacherId && ts.SubjectId == x.Id))
            .Include(x => x.Semester)
            .ToListAsync();
        return View();
    }

    [HttpPost]
    public async Task<IActionResult> CreateQuiz(string title, int subjectId, int? topicId, int? materialId,
        string questionText, string optionA, string optionB, string optionC, string optionD, string correctOption)
    {
        var teacherId = HttpContext.Session.GetInt32("UserId") ?? 0;

        if (string.IsNullOrWhiteSpace(questionText))
        {
            TempData["Error"] = "Quiz question is required.";
            return RedirectToAction(nameof(CreateQuiz));
        }

        var quizTitle = string.IsNullOrWhiteSpace(title)
            ? $"Quiz - {DateTime.UtcNow:yyyyMMddHHmm}"
            : title.Trim();

        var quiz = new Quiz
        {
            Title = quizTitle,
            SubjectId = subjectId,
            TopicId = topicId,
            MaterialId = materialId,
            TeacherId = teacherId,
            QuizQuestions =
            [
                new QuizQuestion
                {
                    QuestionText = questionText,
                    OptionA = optionA,
                    OptionB = optionB,
                    OptionC = optionC,
                    OptionD = optionD,
                    CorrectOption = correctOption.ToUpperInvariant()
                }
            ]
        };

        dbContext.Quizzes.Add(quiz);
        await dbContext.SaveChangesAsync();
        return RedirectToAction(nameof(Quizzes));
    }

    public async Task<IActionResult> Quizzes()
    {
        var teacherId = HttpContext.Session.GetInt32("UserId") ?? 0;
        var quizzes = await dbContext.Quizzes
            .Include(x => x.Subject)
            .Include(x => x.QuizQuestions)
            .Where(x => x.TeacherId == teacherId)
            .OrderByDescending(x => x.Id)
            .ToListAsync();

        return View(quizzes);
    }

    public async Task<IActionResult> Progress()
    {
        var teacherId = HttpContext.Session.GetInt32("UserId") ?? 0;

        var materialIds = await dbContext.Materials
            .Where(x => x.TeacherId == teacherId)
            .Select(x => x.Id)
            .ToListAsync();

        var totalPages = await dbContext.MaterialPages
            .Where(p => materialIds.Contains(p.MaterialId))
            .CountAsync();

        var pageProgress = await dbContext.MaterialPageProgress
            .Include(x => x.Student)
            .Include(x => x.MaterialPage)
            .ThenInclude(x => x!.Material)
            .Where(x => materialIds.Contains(x.MaterialPage!.MaterialId))
            .ToListAsync();

        var quizResults = await dbContext.QuizResults
            .Include(x => x.Quiz)
            .Where(x => x.Quiz!.MaterialPageId != null && materialIds.Contains(x.Quiz.MaterialId ?? 0))
            .ToListAsync();

        var pageQuizIds = await dbContext.Quizzes
            .Where(x => x.MaterialPageId != null && materialIds.Contains(x.MaterialId ?? 0))
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

        var studentIds = pageProgress.Select(x => x.StudentId)
            .Concat(quizResults.Select(x => x.StudentId))
            .Distinct()
            .ToList();

        var includedSemesters = await dbContext.Semesters
            .ToDictionaryAsync(x => x.Id, x => x.Name);

        var students = await dbContext.Users
            .Where(x => studentIds.Contains(x.Id))
            .Select(x => new { x.Id, x.FullName })
            .ToDictionaryAsync(x => x.Id, x => x);

        var studentEnrollmentNumbers = await dbContext.Students
            .Where(x => studentIds.Contains(x.UserId))
            .Select(x => new { x.UserId, x.EnrollmentNumber })
            .ToDictionaryAsync(x => x.UserId, x => x.EnrollmentNumber);

        var enrollments = await dbContext.StudentEnrollments
            .Include(x => x.Semester)
            .Where(x => studentIds.Contains(x.StudentId))
            .OrderBy(x => x.SemesterId)
            .ToListAsync();

        var enrollmentsByStudentId = enrollments
            .GroupBy(x => x.StudentId)
            .ToDictionary(x => x.Key, x => x.ToList());

        var rows = new List<ProgressAnalyticsRowViewModel>();

        foreach (var studentId in studentIds)
        {
            var studentName = pageProgress.FirstOrDefault(x => x.StudentId == studentId)?.Student?.FullName
                              ?? (students.TryGetValue(studentId, out var student) ? student.FullName : null)
                              ?? "Unknown";

            var studentEnrollmentNo = studentEnrollmentNumbers.TryGetValue(studentId, out var enrollment)
                ? enrollment
                : null;

            var enrollmentNo = string.IsNullOrWhiteSpace(studentEnrollmentNo) ? "-" : studentEnrollmentNo;

            enrollmentsByStudentId.TryGetValue(studentId, out var studentEnrollments);
            studentEnrollments ??= [];

            var semester = studentEnrollments.Count switch
            {
                0 => "-",
                1 => studentEnrollments[0].Semester?.Name
                     ?? (includedSemesters.TryGetValue(studentEnrollments[0].SemesterId, out var name) ? name : "-"),
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

            // If quiz not attempted -> correct remains 0, total is based on created questions.
            // If no quiz exists -> total=0 and score=0.
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

        return ("Needs Improvement", "bg-danger");
    }
}
