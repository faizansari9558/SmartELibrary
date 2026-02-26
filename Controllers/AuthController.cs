using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmartELibrary.Data;
using SmartELibrary.Models;
using SmartELibrary.Services;
using SmartELibrary.ViewModels;

namespace SmartELibrary.Controllers;

public class AuthController(ApplicationDbContext dbContext, IOtpService otpService) : Controller
{
    private const string StudentSessionKey = "StudentSessionId";
    private const string PendingStudentPhoneKey = "PendingStudentPhone";

    [HttpGet]
    public IActionResult VerifyOtp()
    {
        ViewBag.PhoneNumber = TempData["PhoneNumber"]?.ToString();
        return View();
    }

    [HttpPost]
    public async Task<IActionResult> VerifyOtp(string phoneNumber, string otpCode)
    {
        var isValid = await otpService.VerifyOtpAsync(phoneNumber, otpCode);
        if (!isValid)
        {
            ModelState.AddModelError(string.Empty, "Invalid or expired OTP.");
            ViewBag.PhoneNumber = phoneNumber;
            return View();
        }

        TempData["Success"] = "Phone verified. Wait for admin approval if required.";
        return RedirectToAction("Index", "Home");
    }

    [HttpGet("/Admin/Login")]
    public IActionResult AdminLogin() => View(new LoginViewModel());

    [HttpPost("/Admin/Login")]
    public async Task<IActionResult> AdminLogin(LoginViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var user = await dbContext.Users.FirstOrDefaultAsync(x => x.PhoneNumber == model.PhoneNumber && x.Role == UserRole.Admin);
        if (user is null)
        {
            ModelState.AddModelError(string.Empty, "Invalid credentials.");
            return View(model);
        }

        if (!PasswordService.VerifyPassword(model.Password, user.PasswordHash))
        {
            ModelState.AddModelError(string.Empty, "Invalid credentials.");
            return View(model);
        }

        SignInUser(user);
        return RedirectToAction("Dashboard", "Admin");
    }

    [HttpGet("/Teacher/Login")]
    public IActionResult TeacherLogin() => View(new LoginViewModel());

    [HttpPost("/Teacher/Login")]
    public async Task<IActionResult> TeacherLogin(LoginViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var user = await dbContext.Users.FirstOrDefaultAsync(x => x.PhoneNumber == model.PhoneNumber && x.Role == UserRole.Teacher);
        if (user is null || !PasswordService.VerifyPassword(model.Password, user.PasswordHash))
        {
            ModelState.AddModelError(string.Empty, "Invalid credentials.");
            return View(model);
        }

        SignInUser(user);
        return RedirectToAction("Dashboard", "Teacher");
    }

    [HttpGet("/Student/Login")]
    public IActionResult StudentLogin() => View(new StudentLoginViewModel());

    [HttpPost("/Student/Login")]
    public async Task<IActionResult> StudentLogin(StudentLoginViewModel model, [FromServices] IStudentSessionTracker sessionTracker)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var user = await dbContext.Users.FirstOrDefaultAsync(x => x.PhoneNumber == model.PhoneNumber && x.Role == UserRole.Student);
        if (user is null)
        {
            ModelState.AddModelError(string.Empty, "Invalid credentials.");
            return View(model);
        }

        if (!PasswordService.VerifyPassword(model.Password, user.PasswordHash))
        {
            ModelState.AddModelError(string.Empty, "Invalid credentials.");
            return View(model);
        }

        var studentSessionId = GetOrCreateStudentSessionId();
        if (!sessionTracker.CanStartSession(user.Id, studentSessionId))
        {
            ModelState.AddModelError(string.Empty, "Student is already logged in on another device.");
            return View(model);
        }

        var otp = await otpService.GenerateOtpAsync(model.PhoneNumber);
        await otpService.SendOtpSmsPlaceholderAsync(model.PhoneNumber, otp);
        TempData["Success"] = "OTP generated and sent via SMS placeholder.";
        TempData["DebugOtp"] = otp;
        TempData[PendingStudentPhoneKey] = model.PhoneNumber;

        return RedirectToAction(nameof(StudentVerifyOtp));
    }

    [HttpGet("/Student/VerifyOtp")]
    public IActionResult StudentVerifyOtp()
    {
        var phoneNumber = TempData.Peek(PendingStudentPhoneKey)?.ToString();
        var model = new StudentVerifyOtpViewModel
        {
            PhoneNumber = phoneNumber ?? string.Empty
        };

        return View(model);
    }

    [HttpPost("/Student/VerifyOtp")]
    public async Task<IActionResult> StudentVerifyOtp(StudentVerifyOtpViewModel model, [FromServices] IStudentSessionTracker sessionTracker)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var user = await dbContext.Users.FirstOrDefaultAsync(x => x.PhoneNumber == model.PhoneNumber && x.Role == UserRole.Student);
        if (user is null)
        {
            ModelState.AddModelError(string.Empty, "Student account not found.");
            return View(model);
        }

        var studentSessionId = GetOrCreateStudentSessionId();
        if (!sessionTracker.CanStartSession(user.Id, studentSessionId))
        {
            ModelState.AddModelError(string.Empty, "Student is already logged in on another device.");
            return View(model);
        }

        var isValid = await otpService.VerifyOtpAsync(model.PhoneNumber, model.OtpCode);
        if (!isValid)
        {
            ModelState.AddModelError(string.Empty, "Invalid or expired OTP.");
            return View(model);
        }

        if (!sessionTracker.TryStartSession(user.Id, studentSessionId))
        {
            ModelState.AddModelError(string.Empty, "Student is already logged in on another device.");
            return View(model);
        }

        SignInUser(user);
        return RedirectToAction("Dashboard", "Student");
    }

    public IActionResult PendingApproval() => View();

    public IActionResult AccessDenied() => View();

    public IActionResult Logout()
    {
        var userId = HttpContext.Session.GetInt32("UserId");
        var role = HttpContext.Session.GetString("Role");
        if (userId.HasValue && role == UserRole.Student.ToString())
        {
            var sessionTracker = HttpContext.RequestServices.GetRequiredService<IStudentSessionTracker>();
            var studentSessionId = HttpContext.Session.GetString(StudentSessionKey);
            sessionTracker.EndSession(userId.Value, studentSessionId);
        }

        HttpContext.Session.Clear();
        return RedirectToAction("Index", "Home");
    }

    private void SignInUser(User user)
    {
        HttpContext.Session.SetInt32("UserId", user.Id);
        HttpContext.Session.SetString("Role", user.Role.ToString());
        HttpContext.Session.SetString("Name", user.FullName);
        HttpContext.Session.SetString("IsApproved", user.IsApproved.ToString());
    }

    private string GetOrCreateStudentSessionId()
    {
        var sessionId = HttpContext.Session.GetString(StudentSessionKey);
        if (!string.IsNullOrWhiteSpace(sessionId))
        {
            return sessionId;
        }

        sessionId = Guid.NewGuid().ToString("N");
        HttpContext.Session.SetString(StudentSessionKey, sessionId);
        return sessionId;
    }
}
