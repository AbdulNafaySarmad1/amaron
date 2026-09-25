using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Commerce.Application;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Commerce.Infrastructure;

/// <summary>
/// Server-side Turnstile verification: the browser's widget result is never trusted on its own. Missing, oversized or
/// rejected tokens fail. If Cloudflare itself cannot be reached the check lets the request through and logs it, so a
/// vendor outage cannot block every order; an attacker still needs tokens Cloudflare accepts whenever it is up.
/// </summary>
public sealed class CloudflareTurnstile(HttpClient http, IConfiguration configuration, ILogger<CloudflareTurnstile> logger) : IHumanVerification
{
    private const string SiteVerify = "https://challenges.cloudflare.com/turnstile/v0/siteverify";
    private readonly string? secret = configuration["TURNSTILE_SECRET_KEY"];

    public bool Enabled => !string.IsNullOrWhiteSpace(secret);

    public async Task<bool> VerifyAsync(string? token, CancellationToken cancellationToken)
    {
        if (!Enabled) return true;
        if (string.IsNullOrWhiteSpace(token) || token.Length > 2048) return false;
        try
        {
            using var response = await http.PostAsync(SiteVerify, new FormUrlEncodedContent(new Dictionary<string, string> { ["secret"] = secret!, ["response"] = token }), cancellationToken);
            if ((int)response.StatusCode >= 500)
            {
                logger.LogWarning("Turnstile verification unavailable ({Status}); allowing the request", (int)response.StatusCode);
                return true;
            }
            var result = await response.Content.ReadFromJsonAsync<SiteVerifyResult>(cancellationToken);
            return result?.Success == true;
        }
        catch (Exception exception) when (exception is HttpRequestException || exception is TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(exception, "Turnstile verification unreachable; allowing the request");
            return true;
        }
    }

    private sealed record SiteVerifyResult([property: JsonPropertyName("success")] bool Success);
}
