using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using SmartELibrary.Models;

namespace SmartELibrary.Filters;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public class RoleAuthorizeAttribute(params UserRole[] allowedRoles) : Attribute, IAsyncAuthorizationFilter
{
    public Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        var session = context.HttpContext.Session;
        var roleValue = session.GetString("Role");
        var isApproved = session.GetString("IsApproved");
        var userId = session.GetInt32("UserId");

        if (userId is null || string.IsNullOrWhiteSpace(roleValue))
        {
            context.Result = new RedirectToActionResult("Index", "Home", null);
            return Task.CompletedTask;
        }

        if (!Enum.TryParse<UserRole>(roleValue, out var role) || !allowedRoles.Contains(role))
        {
            context.Result = new RedirectToActionResult("AccessDenied", "Auth", null);
            return Task.CompletedTask;
        }

        if (isApproved != bool.TrueString && role != UserRole.Admin)
        {
            context.Result = new RedirectToActionResult("PendingApproval", "Auth", null);
            return Task.CompletedTask;
        }

        return Task.CompletedTask;
    }
}
