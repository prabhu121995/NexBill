using System.Net;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;

namespace Kiosk.Services;

/// <summary>
/// Per-client request limits, read from the <c>RateLimiting</c> section of
/// appsettings. Every request counts toward <see cref="Global"/>; sign-in also
/// counts toward the much tighter <see cref="Login"/>, which is what actually
/// slows down password guessing.
/// </summary>
public class RateLimitSettings
{
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Every request, pages and static files included. A cold load of the billing
    /// screen pulls a few dozen assets and the customer display posts on every cart
    /// change, so this is set to catch a runaway client, not a busy till.
    /// </summary>
    public WindowLimit Global { get; set; } = new() { PermitLimit = 600, WindowSeconds = 60 };

    /// <summary>Sign-in attempts, successful or not.</summary>
    public WindowLimit Login { get; set; } = new() { PermitLimit = 5, WindowSeconds = 60 };
}

public class WindowLimit
{
    public int PermitLimit { get; set; }
    public int WindowSeconds { get; set; }
}

/// <summary>
/// Whether the app sits behind IIS, an ALB or another reverse proxy. Off by
/// default: without a proxy, trusting X-Forwarded-For would let any client pick
/// its own IP and step around the rate limits.
/// </summary>
public class ForwardedHeadersSettings
{
    public bool Enabled { get; set; }

    /// <summary>
    /// Proxy addresses to trust. Empty trusts any proxy, which suits an ALB whose
    /// addresses change, but only while the app cannot be reached except through it.
    /// </summary>
    public List<string> KnownProxies { get; set; } = new();
}

public static class RateLimiting
{
    public const string LoginPolicy = "login";

    public static IServiceCollection AddKioskRateLimiting(this IServiceCollection services, IConfiguration config)
    {
        var settings = config.GetSection("RateLimiting").Get<RateLimitSettings>() ?? new RateLimitSettings();
        services.AddSingleton(settings);

        var forwarded = config.GetSection("ForwardedHeaders").Get<ForwardedHeadersSettings>() ?? new ForwardedHeadersSettings();
        services.AddSingleton(forwarded);

        if (forwarded.Enabled)
        {
            services.Configure<ForwardedHeadersOptions>(options =>
            {
                options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
                options.KnownIPNetworks.Clear();
                options.KnownProxies.Clear();
                foreach (var proxy in forwarded.KnownProxies)
                    options.KnownProxies.Add(IPAddress.Parse(proxy));
            });
        }

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(ctx =>
            {
                // The status page is re-executed into after a rejection. Counting it
                // too would reject the page that explains the rejection.
                if (!settings.Enabled || ctx.Request.Path.StartsWithSegments("/error"))
                    return RateLimitPartition.GetNoLimiter("exempt");

                return RateLimitPartition.GetFixedWindowLimiter(ClientKey(ctx), _ => Window(settings.Global));
            });

            options.AddPolicy(LoginPolicy, ctx => settings.Enabled
                ? RateLimitPartition.GetFixedWindowLimiter(ClientKey(ctx), _ => Window(settings.Login))
                : RateLimitPartition.GetNoLimiter("exempt"));

            options.OnRejected = async (context, ct) =>
            {
                var http = context.HttpContext;

                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                    http.Response.Headers.RetryAfter = ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString();

                http.RequestServices.GetRequiredService<ILoggerFactory>()
                    .CreateLogger("Kiosk.RateLimit")
                    .LogWarning("Rate limit hit by {Client} on {Path}", ClientKey(http), http.Request.Path);

                // Pages are left bodiless so the status-page middleware renders /error/429.
                // A fetch gets JSON it can show, in the same { error } shape as every other
                // API failure.
                if (http.Request.Path.StartsWithSegments("/api"))
                {
                    var message = http.Request.Path.StartsWithSegments("/api/account/login")
                        ? "Too many sign-in attempts. Wait a minute and try again."
                        : "Too many requests from this terminal. Wait a moment and try again.";
                    await http.Response.WriteAsJsonAsync(new { error = message }, ct);
                }
            };
        });

        return services;
    }

    /// <summary>
    /// Forwarded headers come first so the limiter partitions on the till's own IP,
    /// not the proxy's: behind IIS or an ALB every till would otherwise share one bucket.
    /// </summary>
    public static IApplicationBuilder UseKioskForwardedHeaders(this IApplicationBuilder app)
    {
        var forwarded = app.ApplicationServices.GetRequiredService<ForwardedHeadersSettings>();
        if (forwarded.Enabled)
            app.UseForwardedHeaders();
        return app;
    }

    private static string ClientKey(HttpContext ctx) =>
        ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown";

    private static FixedWindowRateLimiterOptions Window(WindowLimit limit) => new()
    {
        PermitLimit = limit.PermitLimit,
        Window = TimeSpan.FromSeconds(limit.WindowSeconds),
        QueueLimit = 0,
        AutoReplenishment = true
    };
}
