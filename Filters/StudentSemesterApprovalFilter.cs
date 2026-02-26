using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using SmartELibrary.Services;

namespace SmartELibrary.Filters;

public class StudentSemesterApprovalFilter(IStudentSemesterApprovalService approvalService) : IAsyncAuthorizationFilter
{
    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        var session = context.HttpContext.Session;
        var userId = session.GetInt32("UserId");
        var role = session.GetString("Role");

        if (userId is null || string.IsNullOrWhiteSpace(role))
        {
            context.Result = new RedirectToActionResult("Index", "Home", null);
            return;
        }

        if (!string.Equals(role, "Student", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var hasApproved = await approvalService.HasApprovedEnrollmentAsync(userId.Value);
        if (!hasApproved)
        {
            context.Result = new RedirectToActionResult("WaitingForSemesterApproval", "Student", null);
        }
    }
}
