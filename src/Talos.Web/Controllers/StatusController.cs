using Microsoft.AspNetCore.Mvc;
using Talos.Web.Data;

namespace Talos.Web.Controllers;

[ApiController]
[Route("api/[controller]")]
public class StatusController : ControllerBase
{
    private readonly IConfiguration _configuration;
    private readonly TalosDbContext _dbContext;
    private readonly IWebHostEnvironment _environment;

    public StatusController(
        IConfiguration configuration,
        TalosDbContext dbContext,
        IWebHostEnvironment environment)
    {
        _configuration = configuration;
        _dbContext = dbContext;
        _environment = environment;
    }

    [HttpGet]
    public async Task<IActionResult> GetStatus()
    {
        var issues = new List<ConfigurationIssue>();

        // GitHub checks
        var github = _configuration.GetSection("GitHub");
        if (string.IsNullOrEmpty(github["ClientId"]))
            issues.Add(new ConfigurationIssue("github_client_id_missing", "GitHub", "GitHub Client ID is not configured", "error"));
        if (string.IsNullOrEmpty(github["ClientSecret"]))
            issues.Add(new ConfigurationIssue("github_client_secret_missing", "GitHub", "GitHub Client Secret is not configured", "error"));

        // JWT checks
        var jwt = _configuration.GetSection("Jwt");
        var secretKey = jwt["SecretKey"] ?? "";
        if (string.IsNullOrEmpty(secretKey))
            issues.Add(new ConfigurationIssue("jwt_secret_key_missing", "JWT", "JWT Secret Key is not configured", "error"));
        else if (secretKey.Length < 32)
            issues.Add(new ConfigurationIssue("jwt_secret_key_too_short", "JWT", "JWT Secret Key must be at least 32 characters", "error"));
        
        if (string.IsNullOrEmpty(jwt["Issuer"]))
            issues.Add(new ConfigurationIssue("jwt_issuer_missing", "JWT", "JWT Issuer is not configured", "error"));
        if (string.IsNullOrEmpty(jwt["Audience"]))
            issues.Add(new ConfigurationIssue("jwt_audience_missing", "JWT", "JWT Audience is not configured", "error"));
        
        var accessExpStr = jwt["AccessTokenExpirationMinutes"];
        if (!string.IsNullOrEmpty(accessExpStr) && int.TryParse(accessExpStr, out var expMin) && expMin <= 0)
            issues.Add(new ConfigurationIssue("jwt_expiration_invalid", "JWT", "JWT Access Token Expiration must be greater than 0", "error"));

        // IndieAuth checks
        var indieAuth = _configuration.GetSection("IndieAuth");
        
        var codeExpStr = indieAuth["AuthorizationCodeExpirationMinutes"];
        if (!string.IsNullOrEmpty(codeExpStr) && int.TryParse(codeExpStr, out var codeExp) && codeExp <= 0)
            issues.Add(new ConfigurationIssue("indieauth_code_expiration_invalid", "IndieAuth", "Authorization Code Expiration must be greater than 0", "error"));
        
        var refreshExpStr = indieAuth["RefreshTokenExpirationDays"];
        if (!string.IsNullOrEmpty(refreshExpStr) && int.TryParse(refreshExpStr, out var refreshExp) && refreshExp <= 0)
            issues.Add(new ConfigurationIssue("indieauth_refresh_expiration_invalid", "IndieAuth", "Refresh Token Expiration must be greater than 0", "error"));
        
        var pendingExpStr = indieAuth["PendingAuthenticationExpirationMinutes"];
        if (!string.IsNullOrEmpty(pendingExpStr) && int.TryParse(pendingExpStr, out var pendingExp) && pendingExp <= 0)
            issues.Add(new ConfigurationIssue("indieauth_pending_expiration_invalid", "IndieAuth", "Pending Authentication Expiration must be greater than 0", "error"));

        // Talos checks
        var talos = _configuration.GetSection("Talos");
        if (string.IsNullOrEmpty(talos["BaseUrl"]))
            issues.Add(new ConfigurationIssue("baseurl_missing", "Talos", "Base URL is not configured", "error"));

        // Database checks
        var connectionString = _configuration.GetConnectionString("DefaultConnection");
        if (string.IsNullOrEmpty(connectionString))
        {
            issues.Add(new ConfigurationIssue("database_connection_missing", "Database", "Database connection string is not configured", "error"));
        }
        else
        {
            try
            {
                await _dbContext.Database.CanConnectAsync();
            }
            catch (Exception)
            {
                issues.Add(new ConfigurationIssue("database_connection_failed", "Database", "Cannot connect to the database", "error"));
            }
        }

        var hasErrors = issues.Any(i => i.Severity == "error");

        return Ok(new StatusResponse
        {
            Configured = !hasErrors,
            Environment = _environment.EnvironmentName,
            Issues = issues
        });
    }
}

public class StatusResponse
{
    public bool Configured { get; set; }
    public string Environment { get; set; } = "";
    public List<ConfigurationIssue> Issues { get; set; } = new();
}

public class ConfigurationIssue
{
    public string Code { get; set; }
    public string Category { get; set; }
    public string Message { get; set; }
    public string Severity { get; set; }

    public ConfigurationIssue(string code, string category, string message, string severity)
    {
        Code = code;
        Category = category;
        Message = message;
        Severity = severity;
    }
}

