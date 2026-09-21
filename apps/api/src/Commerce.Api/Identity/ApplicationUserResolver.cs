using System.Security.Claims;
using Commerce.Application;
using Commerce.Domain;
using Commerce.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Commerce.Api.Identity;

public sealed class ApplicationUserResolver(CommerceDbContext db, IHttpContextAccessor accessor, TimeProvider clock)
{
    public async Task<string> GetRequiredUserIdAsync(CancellationToken cancellationToken)
    {
        var principal = accessor.HttpContext?.User;
        var subject = principal?.FindFirstValue("sub");
        var issuer = principal?.FindFirstValue("iss");
        if (string.IsNullOrWhiteSpace(subject) || string.IsNullOrWhiteSpace(issuer))
            throw new CommerceException("invalid_identity", "The authenticated identity is incomplete.", StatusCodes.Status401Unauthorized);
        if (subject.Length > 200 || issuer.Length > 500)
            throw new CommerceException("invalid_identity", "The authenticated identity exceeds supported limits.", StatusCodes.Status401Unauthorized);

        var existing = await db.ApplicationUsers.AsNoTracking().SingleOrDefaultAsync(x => x.IdentityIssuer == issuer && x.ExternalSubject == subject, cancellationToken);
        if (existing is not null) return existing.Id.ToString("N");

        var user = new ApplicationUser
        {
            Id = Guid.CreateVersion7(),
            IdentityIssuer = issuer,
            ExternalSubject = subject,
            DisplayName = Limited(principal?.FindFirstValue("name") ?? principal?.FindFirstValue("preferred_username"), 200),
            Email = Limited(principal?.FindFirstValue("email"), 320),
            CreatedAt = clock.GetUtcNow()
        };
        db.ApplicationUsers.Add(user);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return user.Id.ToString("N");
        }
        catch (DbUpdateException)
        {
            db.Entry(user).State = EntityState.Detached;
            var concurrent = await db.ApplicationUsers.AsNoTracking().SingleOrDefaultAsync(x => x.IdentityIssuer == issuer && x.ExternalSubject == subject, cancellationToken);
            if (concurrent is null) throw;
            return concurrent.Id.ToString("N");
        }
    }

    private static string? Limited(string? value, int maxLength) => string.IsNullOrWhiteSpace(value) ? null : value.Trim()[..Math.Min(value.Trim().Length, maxLength)];
}
