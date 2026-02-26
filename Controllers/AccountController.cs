using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmartELibrary.Data;
using SmartELibrary.Models;
using SmartELibrary.Services;
using SmartELibrary.ViewModels;

namespace SmartELibrary.Controllers;

[Route("Account")]
public class AccountController(ApplicationDbContext dbContext, IOtpService otpService, ITeacherCodeGenerator teacherCodeGenerator) : Controller
{
    [HttpGet("RegisterTeacher")]
    public IActionResult RegisterTeacher() => View(new RegisterTeacherViewModel());

    [HttpPost("RegisterTeacher")]
    public async Task<IActionResult> RegisterTeacher(RegisterTeacherViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var existingUser = await dbContext.Users.FirstOrDefaultAsync(x => x.PhoneNumber == model.PhoneNumber);
        if (existingUser is not null)
        {
            ModelState.AddModelError(nameof(model.PhoneNumber), "Phone number already registered.");
            return View(model);
        }

        var user = new User
        {
            FullName = model.FullName.Trim(),
            PhoneNumber = model.PhoneNumber.Trim(),
            PasswordHash = PasswordService.HashPassword(model.Password),
            Role = UserRole.Teacher,
            IsApproved = false,
            CreatedAtUtc = DateTime.UtcNow
        };

        dbContext.Users.Add(user);
        await dbContext.SaveChangesAsync();

        dbContext.Teachers.Add(new Teacher
        {
            UserId = user.Id,
            TeacherId = await teacherCodeGenerator.GenerateNextAsync(),
            AssignedAtUtc = DateTime.UtcNow
        });

        await dbContext.SaveChangesAsync();

        var otp = await otpService.GenerateOtpAsync(model.PhoneNumber);
        TempData["DebugOtp"] = otp;
        TempData["PhoneNumber"] = model.PhoneNumber;
        TempData["Success"] = "Registration submitted. Wait for Admin approval.";

        return RedirectToAction("VerifyOtp", "Auth");
    }

    [HttpGet("RegisterStudent")]
    public IActionResult RegisterStudent() => View(new RegisterStudentViewModel());

    [HttpPost("RegisterStudent")]
    public async Task<IActionResult> RegisterStudent(RegisterStudentViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        if (string.IsNullOrWhiteSpace(model.EnrollmentNumber))
        {
            ModelState.AddModelError(nameof(model.EnrollmentNumber), "Enrollment Number is required.");
            return View(model);
        }

        var existingUser = await dbContext.Users.FirstOrDefaultAsync(x => x.PhoneNumber == model.PhoneNumber);
        if (existingUser is not null)
        {
            ModelState.AddModelError(nameof(model.PhoneNumber), "Phone number already registered.");
            return View(model);
        }

        var user = new User
        {
            FullName = model.FullName.Trim(),
            PhoneNumber = model.PhoneNumber.Trim(),
            PasswordHash = PasswordService.HashPassword(model.Password),
            Role = UserRole.Student,
            IsApproved = false,
            CreatedAtUtc = DateTime.UtcNow
        };

        dbContext.Users.Add(user);
        await dbContext.SaveChangesAsync();

        dbContext.Students.Add(new Student
        {
            UserId = user.Id,
            EnrollmentNumber = model.EnrollmentNumber.Trim(),
            CreatedAtUtc = DateTime.UtcNow
        });

        await dbContext.SaveChangesAsync();

        var otp = await otpService.GenerateOtpAsync(model.PhoneNumber);
        TempData["DebugOtp"] = otp;
        TempData["PhoneNumber"] = model.PhoneNumber;
        TempData["Success"] = "Registration submitted. Wait for Admin approval.";

        return RedirectToAction("VerifyOtp", "Auth");
    }
}
