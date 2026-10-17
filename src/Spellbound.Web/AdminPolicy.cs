using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;

namespace Spellbound.Web;

/// <summary>
/// Admins are the accounts whose email is listed in configuration ("Admins": ["you@example.com"], or Admins__0 as an
/// environment variable). Read on every check, so a config reload adds or removes admins without a restart.
/// </summary>
public static class AdminPolicy
{
    public const string Name = "Admin";

    public static bool IsAdmin(ClaimsPrincipal user, IReadOnlyCollection<string> adminEmails) =>
        user.Identity is { IsAuthenticated: true, Name: { } email }
        && adminEmails.Contains(email, StringComparer.OrdinalIgnoreCase);
}

public sealed class AdminRequirement : IAuthorizationRequirement;

public sealed class AdminHandler(IConfiguration configuration) : AuthorizationHandler<AdminRequirement>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, AdminRequirement requirement)
    {
        if (AdminPolicy.IsAdmin(context.User, configuration.GetSection("Admins").Get<string[]>() ?? []))
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}
