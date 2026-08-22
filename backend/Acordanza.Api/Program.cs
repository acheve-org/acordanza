using System.Security.Claims;
using System.Text;
using System.Threading.RateLimiting;
using Acordanza.Api.Authorization;
using Acordanza.Core.Data;
using Acordanza.Core.Models;
using Acordanza.Api.Services;
using Acordanza.Core.Services;
using Azure.Identity;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Queues;
using Fido2NetLib;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using Npgsql;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

var builder = WebApplication.CreateBuilder(args);

// Traces + metrics + app logs all export via OTLP to whatever
// OTEL_EXPORTER_OTLP_ENDPOINT/HEADERS point at (Grafana Cloud in
// production — see infra/terraform/container_app.tf); unset locally, so
// the exporters just have nowhere to send and dev is unaffected.
var otelResource = ResourceBuilder.CreateDefault().AddService("acordanza-api");

builder.Logging.AddOpenTelemetry(options =>
{
    options.SetResourceBuilder(otelResource);
    options.IncludeFormattedMessage = true;
    options.IncludeScopes = true;
    options.AddOtlpExporter();
});

builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing
        .SetResourceBuilder(otelResource)
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddNpgsql()
        .AddOtlpExporter())
    .WithMetrics(metrics => metrics
        .SetResourceBuilder(otelResource)
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddRuntimeInstrumentation()
        .AddNpgsqlInstrumentation()
        .AddOtlpExporter());

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
    .AddRoles<IdentityRole>()
    .AddEntityFrameworkStores<AppDbContext>()
    .AddDefaultTokenProviders()
    .AddTokenProvider<InviteTokenProvider>(InviteTokens.ProviderName);

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
        // The token embeds the security stamp at issue time (JwtTokenService), so a
        // password change/reset or email change — all of which already rotate
        // ApplicationUser.SecurityStamp — revokes every outstanding token for that user
        // immediately instead of leaving them valid until they expire on their own
        // (SEC-4a). One extra lookup per authenticated request, which is the same
        // "no premature caching" posture the rest of this API takes.
        options.Events = new JwtBearerEvents
        {
            OnTokenValidated = async context =>
            {
                var userId = context.Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
                var tokenStamp = context.Principal?.FindFirstValue(JwtTokenService.SecurityStampClaim);
                if (userId is null || tokenStamp is null)
                {
                    context.Fail("Token missing required claims.");
                    return;
                }

                var userManager = context.HttpContext.RequestServices
                    .GetRequiredService<UserManager<ApplicationUser>>();
                var user = await userManager.FindByIdAsync(userId);
                if (user is null || user.SecurityStamp != tokenStamp)
                {
                    context.Fail("Token has been revoked.");
                }
            }
        };
    });

builder.Services.AddAuthorization(options =>
{
    foreach (var permission in Permissions.All)
    {
        options.AddPolicy(permission, policy => policy.RequireClaim("permission", permission));
    }
});
builder.Services.AddScoped<JwtTokenService>();
builder.Services.AddScoped<AreaHierarchyService>();
builder.Services.AddScoped<AreaExpertAuthorizer>();
builder.Services.AddSingleton<BlobStorageService>();
builder.Services.AddSingleton<MediaUploadService>();

// Data Protection keys default to local disk, which doesn't survive Container
// Apps' scale-to-zero restarts — a fresh key ring on every cold start would
// invalidate any password-reset token issued before the previous restart.
// Persisted to the same Blob Storage account as topo images (a dedicated
// container, since it's unrelated data), via the same dual-credential
// pattern as BlobStorageService: Azurite locally through a connection
// string, the real account via the container app's managed identity in
// production. The container-existence check (Azurite only — see below) is
// deferred until the full app startup block further down, so it doesn't
// turn the `create-user` CLI branch into something that also needs Azurite
// reachable.
var storageSection = builder.Configuration.GetSection("Storage");
var dataProtectionContainerName = storageSection["DataProtectionContainerName"]
    ?? throw new InvalidOperationException("Missing Storage:DataProtectionContainerName configuration");
var storageConnectionString = storageSection["ConnectionString"];

BlobContainerClient dataProtectionContainer;
if (!string.IsNullOrEmpty(storageConnectionString))
{
    dataProtectionContainer = new BlobContainerClient(storageConnectionString, dataProtectionContainerName);
}
else
{
    var accountName = storageSection["AccountName"]
        ?? throw new InvalidOperationException("Missing Storage:AccountName configuration");
    var managedIdentityClientId = storageSection["ManagedIdentityClientId"]
        ?? throw new InvalidOperationException("Missing Storage:ManagedIdentityClientId configuration");

    var credential = new DefaultAzureCredential(
        new DefaultAzureCredentialOptions { ManagedIdentityClientId = managedIdentityClientId });
    dataProtectionContainer = new BlobContainerClient(
        new Uri($"https://{accountName}.blob.core.windows.net/{dataProtectionContainerName}"), credential);
}

// No .ProtectKeysWithAzureKeyVault(...) — the "No XML encryptor configured"
// warning this produces is accepted, not an oversight. See "Decisions made
// and why" in infra/README.md for the trade-off (Key Vault would encrypt
// the key material at rest, but the private container + managed-identity-only
// RBAC already gates access; a second Azure resource for this app's blast
// radius isn't worth it).
builder.Services.AddDataProtection()
    .SetApplicationName("Acordanza")
    .PersistKeysToAzureBlobStorage(dataProtectionContainer.GetBlobClient("keys.xml"));

// Route enrichment queue (Storage Queue on the same account topo images use). Same
// dual-credential pattern as BlobStorageService: Azurite locally via a connection
// string, the container app's managed identity in production.
var enrichmentQueueName = storageSection["EnrichmentQueueName"]
    ?? throw new InvalidOperationException("Missing Storage:EnrichmentQueueName configuration");
var queueClientOptions = new QueueClientOptions { MessageEncoding = QueueMessageEncoding.Base64 };

QueueClient enrichmentQueueClient;
if (!string.IsNullOrEmpty(storageConnectionString))
{
    enrichmentQueueClient = new QueueClient(storageConnectionString, enrichmentQueueName, queueClientOptions);
}
else
{
    var accountName = storageSection["AccountName"]
        ?? throw new InvalidOperationException("Missing Storage:AccountName configuration");
    var managedIdentityClientId = storageSection["ManagedIdentityClientId"]
        ?? throw new InvalidOperationException("Missing Storage:ManagedIdentityClientId configuration");

    var queueCredential = new DefaultAzureCredential(
        new DefaultAzureCredentialOptions { ManagedIdentityClientId = managedIdentityClientId });
    enrichmentQueueClient = new QueueClient(
        new Uri($"https://{accountName}.queue.core.windows.net/{enrichmentQueueName}"), queueCredential, queueClientOptions);
}

builder.Services.AddSingleton(enrichmentQueueClient);
builder.Services.AddSingleton<RouteEnrichmentQueue>();

// Route import (plans 30 and 31) rides a second queue on the same account, not a second message
// type on the enrichment one: EnrichmentQueueProcessor treats an undeserializable
// message as poison, which a second shape would trip, and each queue wants its own
// visibility timeout. Registered under a DI key so it can't shadow the unkeyed
// enrichment client above.
var routeImportQueueName = storageSection["RouteImportQueueName"]
    ?? throw new InvalidOperationException("Missing Storage:RouteImportQueueName configuration");

QueueClient routeImportQueueClient;
if (!string.IsNullOrEmpty(storageConnectionString))
{
    routeImportQueueClient = new QueueClient(storageConnectionString, routeImportQueueName, queueClientOptions);
}
else
{
    var accountName = storageSection["AccountName"]
        ?? throw new InvalidOperationException("Missing Storage:AccountName configuration");
    var managedIdentityClientId = storageSection["ManagedIdentityClientId"]
        ?? throw new InvalidOperationException("Missing Storage:ManagedIdentityClientId configuration");

    var routeImportCredential = new DefaultAzureCredential(
        new DefaultAzureCredentialOptions { ManagedIdentityClientId = managedIdentityClientId });
    routeImportQueueClient = new QueueClient(
        new Uri($"https://{accountName}.queue.core.windows.net/{routeImportQueueName}"), routeImportCredential, queueClientOptions);
}

builder.Services.AddKeyedSingleton(RouteImportKeys.QueueServiceKey, routeImportQueueClient);
builder.Services.AddSingleton(new RouteImportQueue(routeImportQueueClient));

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
builder.Services.AddOpenApi(options =>
{
    // Controllers deserialize with JsonSerializerDefaults.Web, which sets
    // NumberHandling = AllowReadingFromString (so query/form-bound numbers tolerate
    // quoted JSON); the schema generator reflects that leniency by typing every
    // numeric property as `integer|number` OR `string`, which turns every id/count
    // in the generated frontend types into a `number | string` union. Responses
    // never actually serialize numbers as strings, so trim the `string` alternative
    // back off — this only affects the published schema, not runtime (de)serialization.
    options.AddSchemaTransformer((schema, context, cancellationToken) =>
    {
        if (schema.Type is { } type &&
            type.HasFlag(JsonSchemaType.String) &&
            (type.HasFlag(JsonSchemaType.Integer) || type.HasFlag(JsonSchemaType.Number)))
        {
            schema.Type = type & ~JsonSchemaType.String;
            schema.Pattern = null;
        }

        return Task.CompletedTask;
    });
});

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

// Caps how many email-change requests an authenticated caller can trigger —
// without this, an authenticated JWT is an easy way to flood an arbitrary
// inbox (the new address, attacker-chosen) and burn Resend's quota. Runs
// after UseAuthentication (see below), so the user id claim is populated by
// the time this partitions; falls back to IP only for the (never expected in
// practice, since the endpoint is [Authorize]-wide) case it isn't.
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("change-email", httpContext => RateLimitPartition.GetFixedWindowLimiter(
        partitionKey: httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        factory: _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 3,
            Window = TimeSpan.FromHours(1),
            QueueLimit = 0
        }));
});

// Caps how many route imports a single caller (by IP) can trigger — both kinds, one
// bucket. This is the whole bound on the feature's spend: unlike route.enrich, POST
// /route-imports is open to anyone who can create a route (see RouteImportsController),
// so what stops a loop is a rate limit rather than a role. It is also what bounds the
// URL kind's outbound fetches, which is a second reason the two kinds share one bucket
// rather than getting one each. 10/hour against ~$0.10 an import (both extractions run
// on claude-opus-5, not plans 30/31's sonnet-5 — see infra/terraform/ai_foundry.tf) is a
// ceiling of about $1 an hour per caller, and comfortably above what a real session needs.
//
// Configurable only so the integration suite can raise it: the limiter's window is
// process-global and partitions on an IP that's always "unknown" under
// WebApplicationFactory, so every route-import test in the assembly shares one bucket and
// the suite would start 429ing on its ninth test rather than on anything real.
var routeImportPermitLimit = builder.Configuration.GetValue("RateLimits:RouteImportPerHour", 10);
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("route-import", httpContext => RateLimitPartition.GetFixedWindowLimiter(
        partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        factory: _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = routeImportPermitLimit,
            Window = TimeSpan.FromHours(1),
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

// --- Admin CLI: `dotnet run -- create-user <email> <password> <name> [role]` creates an account. ---
// Registration is intentionally not exposed over HTTP; accounts are admin-provisioned.
if (args.Length > 0 && args[0] == "create-user")
{
    if (args.Length is < 4 or > 5)
    {
        Console.WriteLine("Usage: dotnet run -- create-user <email> <password> <name> [role]");
        Console.WriteLine($"Roles: {string.Join(", ", Roles.All)} (default: {Roles.User})");
        return 1;
    }

    var name = args[3];
    if (string.IsNullOrWhiteSpace(name) || name.Length > 20)
    {
        Console.WriteLine("Name must be between 1 and 20 characters.");
        return 1;
    }

    var role = args.Length == 5 ? args[4] : Roles.User;
    if (!Roles.All.Contains(role))
    {
        Console.WriteLine($"Unknown role '{role}'. Roles: {string.Join(", ", Roles.All)}");
        return 1;
    }

    await using var cliApp = builder.Build();
    using var scope = cliApp.Services.CreateScope();
    var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();
    await RoleSeeder.EnsureSeededAsync(scope.ServiceProvider);

    var user = new ApplicationUser
    {
        UserName = args[1], Email = args[1], Name = name, ConfirmedAt = DateTimeOffset.UtcNow
    };
    var result = await userManager.CreateAsync(user, args[2]);
    if (!result.Succeeded)
    {
        Console.WriteLine("Failed to create user:");
        foreach (var error in result.Errors) Console.WriteLine($"  - {error.Description}");
        return 1;
    }

    var roleResult = await userManager.AddToRoleAsync(user, role);
    if (!roleResult.Succeeded)
    {
        Console.WriteLine("Failed to assign role:");
        foreach (var error in roleResult.Errors) Console.WriteLine($"  - {error.Description}");
        return 1;
    }

    Console.WriteLine($"Created user '{args[1]}' with role '{role}'.");
    return 0;
}

// --- `dotnet run -- enqueue-enrichment <routeId> [--force]` — the local smoke-test path
// and operator escape hatch, bypassing the HTTP endpoint and its permission check
// (server/CLI access is a higher trust bar than route.enrich). No batch mode by design —
// enriching a whole area in one command is the unbounded-spend shape the manual-trigger
// decision exists to avoid.
if (args.Length > 0 && args[0] == "enqueue-enrichment")
{
    if (args.Length is < 2 or > 3 || !int.TryParse(args[1], out var enqueueRouteId))
    {
        Console.WriteLine("Usage: dotnet run -- enqueue-enrichment <routeId> [--force]");
        return 1;
    }

    var enqueueForce = args.Length == 3 && args[2] == "--force";

    await using var cliApp = builder.Build();
    using var scope = cliApp.Services.CreateScope();
    var queue = scope.ServiceProvider.GetRequiredService<RouteEnrichmentQueue>();
    await queue.EnqueueAsync(enqueueRouteId, enqueueForce);

    Console.WriteLine($"Enqueued enrichment for route {enqueueRouteId} (force={enqueueForce}).");
    return 0;
}

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();

    // Must run on every boot, not just in the create-user CLI branch above: besides
    // creating the role catalog, this is what backfills the `user` role onto accounts
    // holding none and retires the legacy first-ascensionist role. Those only take
    // effect on deploy if they run here. Idempotent — see RoleSeeder.
    await RoleSeeder.EnsureSeededAsync(scope.ServiceProvider);

    var blobStorage = scope.ServiceProvider.GetRequiredService<BlobStorageService>();
    await blobStorage.EnsureContainerExistsAsync();

    // Only relevant locally (Azurite) — in production the container is
    // provisioned by Terraform, matching BlobStorageService's topos container.
    if (!string.IsNullOrEmpty(storageConnectionString))
    {
        await dataProtectionContainer.CreateIfNotExistsAsync(PublicAccessType.None);
        await enrichmentQueueClient.CreateIfNotExistsAsync();
        await routeImportQueueClient.CreateIfNotExistsAsync();
    }
}

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseForwardedHeaders();

// Skipped in Development, where the API is plain HTTP on localhost and an HSTS
// header would pin the whole of localhost to HTTPS in the developer's browser —
// including any other project on another port. UseHttpsRedirection above is what
// makes this honest in production; the max-age is a year, without
// includeSubDomains (the API, the app and the landing page are three separate
// hostnames under one apex, and this header should only speak for its own).
if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

app.UseHttpsRedirection();

// Response hardening for an API that only ever returns JSON. Set before the
// pipeline runs so they also land on 401/403/404 and on anything the exception
// handler produces, not just on successful actions.
//
// The CSP is the strictest one there is: nothing here is a document, so nothing
// here may load a script, a frame or an image. It matters because a JSON
// response is still renderable if a browser can be talked into treating it as a
// document — nosniff and this between them close that off. frame-ancestors
// stops the API being framed for clickjacking; the frontend's own headers (see
// frontend/nginx.conf) say the same for the app itself.
app.Use(async (context, next) =>
{
    var headers = context.Response.Headers;
    headers["Content-Security-Policy"] = "default-src 'none'; frame-ancestors 'none'; base-uri 'none'";
    headers["X-Content-Type-Options"] = "nosniff";
    headers["X-Frame-Options"] = "DENY";
    // no-referrer rather than the frontend's strict-origin-when-cross-origin:
    // this app's URLs carry ids and slugs, and an API request has no legitimate
    // reason to tell a third party which one it was.
    headers["Referrer-Policy"] = "no-referrer";
    await next();
});

app.UseCors("Frontend");

app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

app.MapControllers();

app.Run();
return 0;
