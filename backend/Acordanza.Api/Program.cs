using System.Text;
using System.Threading.RateLimiting;
using Acordanza.Api.Data;
using Acordanza.Api.Models;
using Acordanza.Api.Services;
using Fido2NetLib;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("Default")
    ?? throw new InvalidOperationException("Missing ConnectionStrings:Default configuration");

builder.Services.AddDbContext<AppDbContext>(options => options.UseNpgsql(connectionString));

builder.Services
    .AddIdentityCore<ApplicationUser>(options =>
    {
        options.Password.RequiredLength = 8;
        options.Password.RequireNonAlphanumeric = false;
        options.Password.RequireUppercase = false;
        options.Password.RequireDigit = false;
        options.User.RequireUniqueEmail = true;
    })
    .AddEntityFrameworkStores<AppDbContext>()
    .AddDefaultTokenProviders();

// Password-reset tokens embed the user's SecurityStamp, so they're single-use
// (ResetPasswordAsync rotates the stamp on success) — this just tightens the
// window an unused link stays valid from Identity's 1-day default.
builder.Services.Configure<DataProtectionTokenProviderOptions>(options =>
{
    options.TokenLifespan = TimeSpan.FromHours(1);
});

// Resend needs a real API key (production); without one (local dev) fall
// back to logging the email instead, so the forgot-password flow can be
// exercised end-to-end with zero email-provider setup.
if (string.IsNullOrEmpty(builder.Configuration["Resend:ApiKey"]))
{
    builder.Services.AddSingleton<IEmailSender, ConsoleEmailSender>();
}
else
{
    builder.Services.AddHttpClient<ResendEmailSender>();
    builder.Services.AddTransient<IEmailSender>(sp => sp.GetRequiredService<ResendEmailSender>());
}

var jwtSection = builder.Configuration.GetSection("Jwt");
var jwtKey = jwtSection["Key"] ?? throw new InvalidOperationException("Missing Jwt:Key configuration");

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtSection["Issuer"],
            ValidAudience = jwtSection["Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey))
        };
    });

builder.Services.AddAuthorization();
builder.Services.AddScoped<JwtTokenService>();

var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(options =>
{
    options.AddPolicy("Frontend", policy =>
        policy.WithOrigins(allowedOrigins).AllowAnyHeader().AllowAnyMethod());
});

var fido2Section = builder.Configuration.GetSection("Fido2");
builder.Services.AddFido2(options =>
{
    options.ServerDomain = fido2Section["ServerDomain"];
    options.ServerName = fido2Section["ServerName"];
    options.Origins = new HashSet<string>(allowedOrigins);
});
builder.Services.AddMemoryCache();

// Guards unauthenticated, no-body endpoints (currently just the usernameless
// passkey login options request) from being used to flood IMemoryCache with
// challenge entries at no cost to the caller.
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("PasskeyOptions", httpContext => RateLimitPartition.GetFixedWindowLimiter(
        httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1) }));
});

builder.Services.AddControllers();
builder.Services.AddOpenApi();

// Caps how many password-reset emails a single caller (by IP) can trigger —
// the endpoint already avoids leaking whether an account exists, but without
// this an attacker could still flood a known user's inbox (and burn
// Resend's free-tier quota) with a tight request loop.
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("forgot-password", httpContext => RateLimitPartition.GetFixedWindowLimiter(
        partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        factory: _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 3,
            Window = TimeSpan.FromMinutes(15),
            QueueLimit = 0
        }));
});

// The container is never reached directly from the internet — only Azure
// Container Apps' own edge can route to it — so it's safe to trust the
// X-Forwarded-* headers it sets regardless of the (dynamic, unknown)
// internal proxy IP. Without this, UseHttpsRedirection below sees every
// request as plain HTTP and redirect-loops behind the TLS-terminating ingress.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});

// --- Admin CLI: `dotnet run -- create-user <email> <password>` creates an account. ---
// Registration is intentionally not exposed over HTTP; accounts are admin-provisioned.
if (args.Length > 0 && args[0] == "create-user")
{
    if (args.Length != 3)
    {
        Console.WriteLine("Usage: dotnet run -- create-user <email> <password>");
        return 1;
    }

    await using var cliApp = builder.Build();
    using var scope = cliApp.Services.CreateScope();
    var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();

    var user = new ApplicationUser { UserName = args[1], Email = args[1] };
    var result = await userManager.CreateAsync(user, args[2]);
    if (!result.Succeeded)
    {
        Console.WriteLine("Failed to create user:");
        foreach (var error in result.Errors) Console.WriteLine($"  - {error.Description}");
        return 1;
    }

    Console.WriteLine($"Created user '{args[1]}'.");
    return 0;
}

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();
}

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseForwardedHeaders();
app.UseHttpsRedirection();

app.UseCors("Frontend");

app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

app.MapControllers();

app.Run();
return 0;
