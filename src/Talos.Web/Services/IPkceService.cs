namespace Talos.Web.Services;

public interface IPkceService
{
    bool ValidateCodeVerifier(string codeVerifier, string codeChallenge, string codeChallengeMethod);
    string GenerateCodeChallenge(string codeVerifier);
}

