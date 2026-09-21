using Commerce.Application;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Http;

namespace Commerce.Api.Identity;

public sealed class SecurityAuthorizationResultHandler(ILogger<SecurityAuthorizationResultHandler> logger) : IAuthorizationMiddlewareResultHandler
{
    private readonly AuthorizationMiddlewareResultHandler fallback = new();

    public Task HandleAsync(RequestDelegate next, HttpContext context, AuthorizationPolicy policy, PolicyAuthorizationResult authorizeResult)
    {
        if (authorizeResult.Challenged)
        {
            CommerceTelemetry.AuthenticationFailures.Add(1);
            logger.LogWarning("Authentication challenge for {Endpoint}", context.GetEndpoint()?.DisplayName);
        }
        else if (authorizeResult.Forbidden)
        {
            CommerceTelemetry.AuthorizationDenials.Add(1);
            logger.LogWarning("Authorization denied for {Endpoint}", context.GetEndpoint()?.DisplayName);
        }
        return fallback.HandleAsync(next, context, policy, authorizeResult);
    }
}
