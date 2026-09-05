using System.Globalization;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using WEBTechnologies_Final.Data;
using WEBTechnologies_Final.Models.Dtos;
using WEBTechnologies_Final.Services;
using WEBTechnologies_Final.Services.Auth;
using WEBTechnologies_Final.Services.Storage;
using WEBTechnologies_Final.Services.Marketplace;
using Serilog;
using Serilog.Events;
using Serilog.Formatting.Compact;
using Microsoft.Extensions.Caching.Distributed;
using WEBTechnologies_Final.Services.Caching;

var builder = WebApplication.CreateBuilder(args);

// ---------------------------------------------------------------------------
// Logging
// ---------------------------------------------------------------------------
// Console logging alone meant that when something failed in production there was no way to
// find out that it had, let alone why. Three things fix that, and all three are configured
// rather than hard-coded so the host decides:
//
//   * Serilog with structured properties, so "which user hit this" is a query and not a grep.
//   * A rolling FILE sink, because console output is gone the moment the process restarts -
//     which is exactly when you most want to read it.
//   * Compact JSON in production, so any log aggregator can parse it without a custom regex.
//
// Sentry is wired but INERT until Sentry:Dsn is set. No DSN, no network calls, no cost.
builder.Host.UseSerilog((context, services, config) =>
{
    config
        .ReadFrom.Configuration(context.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext()
        .Enrich.WithProperty("Application", "PremiumMotors")
        // EF logs every SQL statement at Information in development, which buries everything
        // else. Warnings and errors from it are still wanted.
        .MinimumLevel.Override("Microsoft.EntityFrameworkCore.Database.Command", LogEventLevel.Warning)
        .MinimumLevel.Override("Microsoft.AspNetCore.StaticFiles", LogEventLevel.Warning)
        // The framework logs four Information lines per request describing its own routing.
        // UseSerilogRequestLogging already emits one line with the method, path, status and
        // duration, which is the line anyone actually reads; the rest is noise that buries
        // the application's own logs.
        .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
        .MinimumLevel.Override("Microsoft.AspNetCore.Hosting.Diagnostics", LogEventLevel.Warning);

    if (context.HostingEnvironment.IsDevelopment())
    {
        config.WriteTo.Console();
    }
    else
    {
        config.WriteTo.Console(new CompactJsonFormatter());
    }

    var logPath = context.Configuration["Logging:FilePath"] ?? "logs/premiummotors-.log";
    config.WriteTo.File(
        new CompactJsonFormatter(),
        logPath,
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 14,
        // A runaway loop must not fill the disk and take the site down with it.
        fileSizeLimitBytes: 64L * 1024 * 1024,
        rollOnFileSizeLimit: true,
        shared: true);
});

// Error tracking. Reads Sentry:Dsn from configuration; with no DSN the SDK initialises
// disabled and does nothing at all.
var sentryDsn = builder.Configuration["Sentry:Dsn"];
if (!string.IsNullOrWhiteSpace(sentryDsn))
{
    builder.WebHost.UseSentry(options =>
    {
        options.Dsn = sentryDsn;
        options.Environment = builder.Environment.EnvironmentName;
        // Never ship request bodies: they contain passwords on the login and register posts.
        options.MaxRequestBodySize = Sentry.Extensibility.RequestSize.None;
        options.SendDefaultPii = false;
        options.TracesSampleRate = builder.Configuration.GetValue("Sentry:TracesSampleRate", 0.0);
    });
}

var appCulture = new CultureInfo("en-GB");
appCulture.NumberFormat.CurrencySymbol = "€";
CultureInfo.DefaultThreadCurrentCulture = appCulture;
CultureInfo.DefaultThreadCurrentUICulture = appCulture;

// Every instant in the database is UTC; this is the zone users see it in.
AppTime.Configure(builder.Configuration["App:DisplayTimeZone"]);

// ---------------------------------------------------------------------------
// MVC + API
// ---------------------------------------------------------------------------
builder.Services.AddControllersWithViews()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.ReferenceHandler = ReferenceHandler.IgnoreCycles;

        // Enums travel as names ("Sedan"), not ordinals. A React Native client should never
        // have to hardcode integers that silently change meaning if the enum is reordered.
        // Numbers are still accepted on input, so existing callers keep working.
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    });

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "PremiumMotors API",
        Version = "v1",
        Description = "Accounts, listings, sealed offers and favourites for the web and mobile clients."
    });

    var scheme = new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Paste the accessToken returned by POST /api/v1/auth/login."
    };

    c.AddSecurityDefinition("Bearer", scheme);
    c.AddSecurityRequirement(_ => new OpenApiSecurityRequirement
    {
        [new OpenApiSecuritySchemeReference("Bearer")] = new List<string>()
    });
});

// ---------------------------------------------------------------------------
// Supabase Postgres
// ---------------------------------------------------------------------------
var connectionString = SupabaseConnection.Build(
    builder.Configuration.GetConnectionString("DefaultConnection"),
    "ConnectionStrings:DefaultConnection");

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(connectionString, npgsql =>
    {
        // Supabase is a remote host, so a dropped connection is normal rather than fatal.
        npgsql.EnableRetryOnFailure(maxRetryCount: 5, maxRetryDelay: TimeSpan.FromSeconds(10), errorCodesToAdd: null);
    }));

// ---------------------------------------------------------------------------
// Authentication: JWT bearer for API clients, session cookie for the website
// ---------------------------------------------------------------------------
builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection("Jwt"));

var jwt = builder.Configuration.GetSection("Jwt").Get<JwtOptions>() ?? new JwtOptions();
if (string.IsNullOrWhiteSpace(jwt.Key) || Encoding.UTF8.GetByteCount(jwt.Key) < 32)
{
    throw new InvalidOperationException(
        "Jwt:Key is missing or shorter than 32 bytes. Generate one and store it as a secret:\n" +
        "  dotnet user-secrets set \"Jwt:Key\" \"<64+ random characters>\"\n" +
        "or set the Jwt__Key environment variable. See docs/SUPABASE_SETUP.md.");
}

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        // Keep claim names exactly as issued instead of remapping them to long WS-* URIs,
        // so the mobile client and the server agree on what a claim is called.
        options.MapInboundClaims = false;

        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwt.Issuer,
            ValidateAudience = true,
            ValidAudience = jwt.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.Key)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30),
            NameClaimType = TokenService.NameClaim,
            RoleClaimType = TokenService.RoleClaim
        };
    });

builder.Services.AddAuthorization();

// ---------------------------------------------------------------------------
// Rate limiting - brute-force and mass-signup protection
// ---------------------------------------------------------------------------
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    // Tight window on the credential endpoints.
    options.AddPolicy(RateLimitPolicies.Auth, ctx =>
        RateLimitPartition.GetFixedWindowLimiter(
            ClientKey(ctx),
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            }));

    // Generous catch-all so a runaway client cannot exhaust the database pool.
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(ctx =>
        RateLimitPartition.GetFixedWindowLimiter(
            ClientKey(ctx),
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 300,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            }));

    options.OnRejected = async (context, ct) =>
    {
        context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
        {
            context.HttpContext.Response.Headers.RetryAfter =
                ((int)retryAfter.TotalSeconds).ToString(NumberFormatInfo.InvariantInfo);
        }

        // Same error envelope as everything else, so mobile clients parse it uniformly.
        await context.HttpContext.Response.WriteAsJsonAsync(
            new ApiError("Too many requests. Please slow down and try again shortly.", "rate_limited"), ct);
    };

    static string ClientKey(HttpContext ctx) =>
        ctx.User?.FindFirst(TokenService.SubClaim)?.Value
        ?? ctx.Connection.RemoteIpAddress?.ToString()
        ?? "unknown";
});

// ---------------------------------------------------------------------------
// CORS (browser clients only; native mobile apps are not subject to it)
// ---------------------------------------------------------------------------
const string ApiCorsPolicy = "ApiClients";
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];

builder.Services.AddCors(options =>
{
    options.AddPolicy(ApiCorsPolicy, policy =>
    {
        if (allowedOrigins.Length > 0)
        {
            policy.WithOrigins(allowedOrigins).AllowAnyHeader().AllowAnyMethod().AllowCredentials();
        }
        else
        {
            // No origins configured: allow token-bearing calls from anywhere, but never
            // cookies, so a misconfiguration cannot turn into a session-riding hole.
            policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod();
        }
    });
});

// ---------------------------------------------------------------------------
// Application services
// ---------------------------------------------------------------------------
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, CurrentUserAccessor>();
builder.Services.AddScoped<IMediaUrlResolver, MediaUrlResolver>();
builder.Services.AddScoped<AccountService>();
builder.Services.AddScoped<TokenService>();
builder.Services.AddScoped<UserTokenService>();
builder.Services.AddScoped<AccountDataService>();
builder.Services.AddScoped<ApiClient>();
builder.Services.AddScoped<ProfileNavService>();

// Marketplace domain services. Shared by the MVC site, the mobile API and the seller API,
// so all three enforce identical offer and messaging rules.
builder.Services.AddScoped<ConversationService>();
builder.Services.AddScoped<OfferService>();
builder.Services.AddScoped<SellerService>();
builder.Services.AddScoped<DealershipService>();
builder.Services.AddScoped<ReviewService>();
builder.Services.AddScoped<SellerAnalyticsService>();
builder.Services.AddScoped<ListingViewService>();
builder.Services.AddScoped<ListingExtrasService>();

builder.Services.Configure<PayPalOptions>(builder.Configuration.GetSection("PayPal"));
builder.Services.Configure<ListingOptions>(builder.Configuration.GetSection("Listing"));
builder.Services.Configure<StorageOptions>(builder.Configuration.GetSection("Storage"));
builder.Services.Configure<EmailOptions>(builder.Configuration.GetSection("Email"));

// Photo storage: local disk in development, Supabase Storage anywhere real. A cloud host
// wipes the local filesystem on every deploy, which would delete every uploaded photo.
var storageOptions = builder.Configuration.GetSection("Storage").Get<StorageOptions>() ?? new StorageOptions();
if (storageOptions.IsSupabase)
{
    builder.Services.AddHttpClient<IPhotoStorage, SupabasePhotoStorage>();
}
else
{
    builder.Services.AddScoped<IPhotoStorage, LocalDiskPhotoStorage>();
}

// Email: a real sender when configured, otherwise one that logs so development never
// silently swallows a password-reset link.
var emailOptions = builder.Configuration.GetSection("Email").Get<EmailOptions>() ?? new EmailOptions();
if (emailOptions.IsConfigured)
{
    builder.Services.AddHttpClient<IEmailSender, ResendEmailSender>();
}
else
{
    builder.Services.AddScoped<IEmailSender, LoggingEmailSender>();
}

builder.Services.AddHttpClient<IPaymentProvider, PayPalProvider>();

builder.Services.AddHealthChecks()
    .AddCheck<DatabaseHealthCheck>("database", tags: new[] { "ready" });

// Website sessions. AddDistributedMemoryCache is NOT distributed despite the name — it is a
// dictionary in the process — so every restart signed every web user out, and two instances
// behind a load balancer would have logged people out at random as requests bounced between
// them. The mobile app was never affected (JWT is stateless), which is exactly why the fault
// could sit there unnoticed.
//
// Sessions:Store picks the implementation. "Postgres" is the default and the one that is
// correct for a real deployment; "Memory" is there for a developer who wants to run with the
// database down, and for the tests.
if (string.Equals(builder.Configuration["Sessions:Store"] ?? "Postgres", "Memory",
        StringComparison.OrdinalIgnoreCase))
{
    builder.Services.AddDistributedMemoryCache();
}
else
{
    builder.Services.AddSingleton<IDistributedCache, PostgresDistributedCache>();
    builder.Services.AddHostedService<ExpiredSessionSweeper>();
}

builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromHours(2);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
    options.Cookie.SameSite = SameSiteMode.Lax;

    // Always HTTPS outside development, so the session cookie cannot leak over plain HTTP.
    options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
        ? CookieSecurePolicy.SameAsRequest
        : CookieSecurePolicy.Always;
});

var app = builder.Build();

// ---------------------------------------------------------------------------
// Swagger - development only. In production it would publish the whole API surface.
// Set Swagger:EnabledInProduction=true deliberately if you need it there.
// ---------------------------------------------------------------------------
if (app.Environment.IsDevelopment() || app.Configuration.GetValue<bool>("Swagger:EnabledInProduction"))
{
    app.UseSwagger();
    app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "PremiumMotors API v1"));
}

// ---------------------------------------------------------------------------
// Migrations. Automatic on boot is convenient locally and risky in production: two
// instances starting together race, and a bad migration takes the app down instead of
// failing a deploy step. Set Database:AutoMigrate=false and run
// "dotnet ef database update" as an explicit deploy step.
// ---------------------------------------------------------------------------
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();

    if (SupabaseConnection.IsDirectConnection(connectionString))
    {
        logger.LogWarning(
            "Using the direct Supabase endpoint (db.*.supabase.co). On the free plan that host " +
            "has no IPv4 address, so it is unreachable from IPv4-only networks. If this fails to " +
            "connect, switch to the session pooler: aws-0-<region>.pooler.supabase.com port 5432.");
    }

    if (app.Configuration.GetValue("Database:AutoMigrate", true))
    {
        await db.Database.MigrateAsync();
    }
    else
    {
        var pending = (await db.Database.GetPendingMigrationsAsync()).ToList();
        if (pending.Count > 0)
        {
            logger.LogError(
                "Database:AutoMigrate is off and {Count} migration(s) are pending: {Migrations}. " +
                "Run 'dotnet ef database update' before serving traffic.",
                pending.Count, string.Join(", ", pending));
        }
    }

    await DbSeeder.SeedAsync(
        db,
        app.Configuration,
        scope.ServiceProvider.GetRequiredService<IWebHostEnvironment>(),
        logger);

    // Business accounts created before dealerships existed have no shopfront, and the
    // directory would silently omit them. Idempotent, so it is a no-op on every boot after
    // the first.
    var backfilled = await scope.ServiceProvider
        .GetRequiredService<DealershipService>()
        .BackfillAsync();
    if (backfilled > 0)
        logger.LogInformation("Created {Count} dealership profile(s) for existing business accounts.", backfilled);

    // Port 5432 is the SESSION pooler: one server-side connection held per client, which is
    // right for running migrations and wrong for serving traffic. 6543 is the transaction
    // pooler, which multiplexes and is what a web app under concurrency wants. The Npgsql
    // setup already disables prepared statements automatically when it sees 6543, so this is
    // purely a configuration change.
    if (!app.Environment.IsDevelopment()
        && connectionString is not null
        && connectionString.Contains("pooler.supabase.com", StringComparison.OrdinalIgnoreCase)
        && connectionString.Contains("Port=5432", StringComparison.OrdinalIgnoreCase))
    {
        logger.LogWarning(
            "Connected through the Supabase SESSION pooler (port 5432) in {Environment}. That " +
            "is the right port for migrations but it holds one connection per client and will " +
            "exhaust the pool under load. Switch the runtime connection to port 6543 (the " +
            "transaction pooler); prepared statements are disabled automatically when you do.",
            app.Environment.EnvironmentName);
    }

    if (!storageOptions.IsConfigured)
    {
        logger.LogWarning(
            "Storage:Provider is Supabase but SupabaseUrl/SupabaseServiceKey are not set. " +
            "Photo uploads will fail until they are configured.");
    }
    else if (!storageOptions.IsSupabase && !app.Environment.IsDevelopment())
    {
        logger.LogWarning(
            "Photo storage is writing to local disk outside development. Most cloud hosts wipe " +
            "that filesystem on deploy, which would delete every uploaded photo. " +
            "Set Storage:Provider=Supabase.");
    }
}

// Behind a reverse proxy the app sees the proxy's IP and http, not the client's IP and
// https. That breaks HTTPS redirection, the Secure cookie policy and - most consequentially -
// rate limiting, which would then bucket the entire internet under one proxy address.
//
// OFF BY DEFAULT, and that is the point: trusting forwarded headers from an untrusted source
// lets anyone spoof their IP and walk straight through the rate limiter. Turn it on only once
// you know a proxy is actually in front of the app, and say which one.
//
//   "Proxy": { "Enabled": true, "KnownProxies": [ "10.0.0.4" ], "KnownNetworks": [ "10.0.0.0/8" ] }
//
// With Enabled true and neither list set, the well-known-proxy list is CLEARED and all
// forwarded headers are accepted. That is correct only on a host where nothing but the
// platform's own load balancer can reach the container (Azure App Service, Fly, Railway and
// most PaaS). It is wrong anywhere the port is publicly reachable.
if (app.Configuration.GetValue("Proxy:Enabled", false))
{
    var forwarding = new ForwardedHeadersOptions
    {
        ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
        ForwardLimit = app.Configuration.GetValue<int?>("Proxy:ForwardLimit") ?? 1
    };

    forwarding.KnownProxies.Clear();
    forwarding.KnownNetworks.Clear();

    foreach (var proxy in app.Configuration.GetSection("Proxy:KnownProxies").Get<string[]>() ?? Array.Empty<string>())
        if (System.Net.IPAddress.TryParse(proxy, out var ip)) forwarding.KnownProxies.Add(ip);

    foreach (var network in app.Configuration.GetSection("Proxy:KnownNetworks").Get<string[]>() ?? Array.Empty<string>())
    {
        var parts = network.Split('/');
        if (parts.Length == 2 && System.Net.IPAddress.TryParse(parts[0], out var prefix)
            && int.TryParse(parts[1], out var length))
            forwarding.KnownNetworks.Add(new Microsoft.AspNetCore.HttpOverrides.IPNetwork(prefix, length));
    }

    app.UseForwardedHeaders(forwarding);

    if (forwarding.KnownProxies.Count == 0 && forwarding.KnownNetworks.Count == 0)
        app.Logger.LogWarning(
            "Proxy:Enabled is on with no KnownProxies or KnownNetworks, so forwarded headers " +
            "are accepted from any caller. Only do this where the app is unreachable except " +
            "through your load balancer.");
}

// One structured line per request: method, path, status, duration. This is the log you
// actually read when someone says "the site was slow this morning".
app.UseSerilogRequestLogging(options =>
{
    options.MessageTemplate =
        "{RequestMethod} {RequestPath} responded {StatusCode} in {Elapsed:0.0} ms";
});

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

var localizationOptions = new RequestLocalizationOptions
{
    DefaultRequestCulture = new RequestCulture(appCulture),
    SupportedCultures = new[] { appCulture },
    SupportedUICultures = new[] { appCulture }
};
localizationOptions.RequestCultureProviders.Clear();
app.UseRequestLocalization(localizationOptions);

app.UseHttpsRedirection();
app.UseRouting();

app.UseCors(ApiCorsPolicy);
app.UseRateLimiter();

app.UseSession();
app.UseAuthentication();
app.UseAuthorization();

app.MapStaticAssets();

// Liveness: the process is up. Readiness: it can actually reach the database.
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = c => c.Tags.Contains("ready") });

app.MapControllers();
// "/" is the consumer front page. The marketplace lives at /Cars and is reached from it —
// deliberately a second click, so a first-time visitor is told what this is before being
// handed a grid of cars.
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();

app.Run();
