using Kiosk.Data;
using Kiosk.Services;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

// Add services to the container.
builder.Services.AddSingleton<Kiosk.Services.DisplayHub>();
builder.Services.AddSingleton<Kiosk.Services.BusinessClock>();
builder.Services.AddSingleton<Kiosk.Services.TerminalIdentity>();
builder.Services.AddSingleton<Kiosk.Services.KioskSession>();
builder.Services.AddScoped<Kiosk.Services.Attendance>();
builder.Services.AddScoped<Kiosk.Services.AppBranding>();
builder.Services.AddControllersWithViews();
builder.Services.AddKioskRateLimiting(builder.Configuration);

// Staff sign-in. Cookie auth only: there is no external identity provider and the
// till is a single browser, so a cookie is the whole session. A long expiry suits
// a shift; sliding renewal keeps a busy till from being signed out mid-service.
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = "kiosk.auth";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.LoginPath = "/login";
        options.AccessDeniedPath = "/login";
        options.ExpireTimeSpan = TimeSpan.FromHours(12);
        options.SlidingExpiration = true;

        // An expired cookie on an API call must not be answered with the login
        // page's HTML, or the fetch would parse it as a successful response.
        options.Events.OnRedirectToLogin = context =>
        {
            if (context.Request.Path.StartsWithSegments("/api"))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return Task.CompletedTask;
            }

            context.Response.Redirect(context.RedirectUri);
            return Task.CompletedTask;
        };
    });

var app = builder.Build();

// The shop's country (Pos:Country) decides the clock and how dates read - not the
// server's. Resolved here so a missing or unknown country stops startup instead of
// failing on the first bill, and set as the app's culture so nothing formatted
// server-side picks up the host's language or calendar.
var shopClock = app.Services.GetRequiredService<Kiosk.Services.BusinessClock>();
System.Globalization.CultureInfo.DefaultThreadCurrentCulture = shopClock.Culture;
System.Globalization.CultureInfo.DefaultThreadCurrentUICulture = shopClock.Culture;
System.Globalization.CultureInfo.CurrentCulture = shopClock.Culture;
System.Globalization.CultureInfo.CurrentUICulture = shopClock.Culture;

app.UseKioskForwardedHeaders();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

// Turn bare status codes into the frameless status page. Kiosk windows have no
// address bar, so Chrome's own 404 would be a dead end with no way back.
// /api is excluded deliberately: a fetch must keep getting a bare code to branch
// on, not a page of HTML that happens to carry a 401.
app.UseWhen(
    ctx => !ctx.Request.Path.StartsWithSegments("/api"),
    branch => branch.UseStatusCodePagesWithReExecute("/error/{0}"));

app.UseHttpsRedirection();
app.UseRouting();

// After routing so [EnableRateLimiting] on an action is visible to the limiter.
app.UseRateLimiter();

app.UseAuthentication();
app.UseAuthorization();

app.MapStaticAssets();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();


using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<Kiosk.Data.AppDbContext>();
    var seedLog = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("Kiosk.Seed");
    await Kiosk.Data.DbSeeder.SeedAsync(db, seedLog);
}

app.Run();
