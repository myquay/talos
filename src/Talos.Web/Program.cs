using System.Threading.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Talos.Web.Configuration;
using Talos.Web.Data;
using Talos.Web.Services;
using Talos.Web.Services.IdentityProviders;

var builder = WebApplication.CreateBuilder(args);

// Add configuration
builder.Services.Configure<GitHubSettings>(builder.Configuration.GetSection("GitHub"));
builder.Services.Configure<JwtSettings>(builder.Configuration.GetSection("Jwt"));
builder.Services.Configure<IndieAuthSettings>(builder.Configuration.GetSection("IndieAuth"));
builder.Services.Configure<TalosSettings>(builder.Configuration.GetSection("Talos"));

// Add services
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();

// Add rate limiting
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    
    // Global rate limit
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 100,
                Window = TimeSpan.FromMinutes(1),
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 0
            }));
    
    // Stricter limit for auth endpoints
    options.AddPolicy("auth", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 30,
                Window = TimeSpan.FromMinutes(1)
            }));
    
    // Stricter limit for token endpoint
    options.AddPolicy("token", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 20,
                Window = TimeSpan.FromMinutes(1)
            }));
});

// Add database context
builder.Services.AddDbContext<TalosDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("DefaultConnection")));

// Configure HTTP clients for profile discovery and GitHub API
builder.Services.AddHttpClient("ProfileDiscovery", client =>
{
    client.DefaultRequestHeaders.Add("User-Agent", "Talos-IndieAuth");
    client.Timeout = TimeSpan.FromSeconds(30);
});

builder.Services.AddHttpClient("GitHub", client =>
{
    client.BaseAddress = new Uri("https://api.github.com/");
    client.DefaultRequestHeaders.Add("Accept", "application/vnd.github.v3+json");
    client.DefaultRequestHeaders.Add("User-Agent", "Talos-IndieAuth");
});

// Register application services
builder.Services.AddScoped<IProfileDiscoveryService, ProfileDiscoveryService>();
builder.Services.AddScoped<IIdentityProvider, GitHubIdentityProvider>();
builder.Services.AddScoped<IIdentityProviderFactory, IdentityProviderFactory>();
builder.Services.AddScoped<ITokenService, TokenService>();
builder.Services.AddScoped<IAuthorizationService, AuthorizationService>();
builder.Services.AddScoped<IPkceService, PkceService>();

var app = builder.Build();

// Ensure database is created
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<TalosDbContext>();
    db.Database.EnsureCreated();
}

// Security headers middleware
app.Use(async (context, next) =>
{
    var headers = context.Response.Headers;
    
    // Prevent clickjacking
    headers["X-Frame-Options"] = "DENY";
    
    // Prevent MIME sniffing
    headers["X-Content-Type-Options"] = "nosniff";
    
    // XSS protection (legacy browsers)
    headers["X-XSS-Protection"] = "1; mode=block";
    
    // Referrer policy
    headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
    
    // Content Security Policy
    headers["Content-Security-Policy"] = 
        "default-src 'self'; " +
        "script-src 'self'; " +
        "style-src 'self' 'unsafe-inline'; " +
        "img-src 'self' https: data:; " +
        "font-src 'self'; " +
        "connect-src 'self'; " +
        "frame-ancestors 'none';";
    
    await next();
});

// HTTPS redirection in production
if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
    app.UseHsts();
}

// Rate limiting
app.UseRateLimiter();

// Serve static files (built Vue.js app)
app.UseStaticFiles();

app.UseRouting();

app.MapControllers();

// SPA fallback (for Vue Router history mode)
app.MapFallbackToFile("index.html");

app.Run();
