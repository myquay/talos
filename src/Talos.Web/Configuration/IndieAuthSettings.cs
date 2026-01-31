namespace Talos.Web.Configuration;

public class IndieAuthSettings
{
    public int AuthorizationCodeExpirationMinutes { get; set; } = 10;
    public int RefreshTokenExpirationDays { get; set; } = 30;
    public int PendingAuthenticationExpirationMinutes { get; set; } = 30;
}

