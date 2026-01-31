using System.Security.Cryptography;
using System.Text;

namespace Talos.Web.Services;

public class PkceService : IPkceService
{
    public bool ValidateCodeVerifier(string codeVerifier, string codeChallenge, string codeChallengeMethod)
    {
        if (string.IsNullOrEmpty(codeVerifier) || string.IsNullOrEmpty(codeChallenge))
            return false;

        // Validate code_verifier format (43-128 characters, unreserved characters only)
        if (codeVerifier.Length is < 43 or > 128)
            return false;

        if (!IsValidCodeVerifier(codeVerifier))
            return false;

        if (codeChallengeMethod != "S256")
            return false;

        var expectedChallenge = GenerateCodeChallenge(codeVerifier);
        return string.Equals(expectedChallenge, codeChallenge, StringComparison.Ordinal);
    }

    public string GenerateCodeChallenge(string codeVerifier)
    {
        var bytes = SHA256.HashData(Encoding.ASCII.GetBytes(codeVerifier));
        return Base64UrlEncode(bytes);
    }

    private static bool IsValidCodeVerifier(string codeVerifier)
    {
        // RFC 7636: code_verifier = 43*128unreserved
        // unreserved = ALPHA / DIGIT / "-" / "." / "_" / "~"
        foreach (var c in codeVerifier)
        {
            if (!char.IsLetterOrDigit(c) && c != '-' && c != '.' && c != '_' && c != '~')
                return false;
        }
        return true;
    }

    private static string Base64UrlEncode(byte[] bytes)
    {
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }
}

