using FluentAssertions;
using Talos.Web.Services;

namespace Talos.Web.Tests.Services;

/// <summary>
/// Tests for PkceService per RFC 7636
/// https://tools.ietf.org/html/rfc7636
/// </summary>
public class PkceServiceTests
{
    private readonly PkceService _sut = new();

    // ===== S256 Code Challenge Tests (RFC 7636 Appendix B) =====
    
    [Fact]
    public void ValidateCodeVerifier_ValidS256Challenge_ReturnsTrue()
    {
        // Example from RFC 7636 Appendix B
        var codeVerifier = "dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk";
        var codeChallenge = "E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM";
        
        var result = _sut.ValidateCodeVerifier(codeVerifier, codeChallenge, "S256");
        
        result.Should().BeTrue();
    }

    [Fact]
    public void ValidateCodeVerifier_WrongVerifier_ReturnsFalse()
    {
        var codeVerifier = "wrong-verifier-value";
        var codeChallenge = "E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM";
        
        var result = _sut.ValidateCodeVerifier(codeVerifier, codeChallenge, "S256");
        
        result.Should().BeFalse();
    }

    [Fact]
    public void ValidateCodeVerifier_PlainMethod_ReturnsFalse()
    {
        // Plain method is not supported - only S256
        var codeVerifier = "dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk";
        
        var result = _sut.ValidateCodeVerifier(codeVerifier, codeVerifier, "plain");
        
        result.Should().BeFalse();
    }

    [Theory]
    [InlineData(null, "challenge", "S256")]
    [InlineData("verifier", null, "S256")]
    [InlineData("", "challenge", "S256")]
    [InlineData("verifier", "", "S256")]
    public void ValidateCodeVerifier_NullOrEmpty_ReturnsFalse(string? verifier, string? challenge, string method)
    {
        var result = _sut.ValidateCodeVerifier(verifier!, challenge!, method);
        
        result.Should().BeFalse();
    }

    // ===== Code Verifier Length Tests =====
    
    [Theory]
    [InlineData(42)]  // Too short (min is 43)
    [InlineData(129)] // Too long (max is 128)
    public void ValidateCodeVerifier_InvalidLength_ReturnsFalse(int length)
    {
        var codeVerifier = new string('a', length);
        var codeChallenge = _sut.GenerateCodeChallenge(codeVerifier);
        
        var result = _sut.ValidateCodeVerifier(codeVerifier, codeChallenge, "S256");
        
        result.Should().BeFalse();
    }

    [Theory]
    [InlineData(43)]  // Minimum valid length
    [InlineData(64)]  // Common length
    [InlineData(128)] // Maximum valid length
    public void ValidateCodeVerifier_ValidLength_ReturnsTrue(int length)
    {
        var codeVerifier = new string('a', length);
        var codeChallenge = _sut.GenerateCodeChallenge(codeVerifier);
        
        var result = _sut.ValidateCodeVerifier(codeVerifier, codeChallenge, "S256");
        
        result.Should().BeTrue();
    }

    // ===== Code Verifier Character Set Tests (RFC 7636 §4.1) =====
    
    [Theory]
    [InlineData("abcdefghijklmnopqrstuvwxyz1234567890ABCDEFGH")] // Alpha + digits
    [InlineData("abcdefghijklmnopqrstuvwxyz-._~1234567890ABCDE")] // All unreserved chars
    public void ValidateCodeVerifier_ValidCharacters_ReturnsTrue(string codeVerifier)
    {
        var codeChallenge = _sut.GenerateCodeChallenge(codeVerifier);
        
        var result = _sut.ValidateCodeVerifier(codeVerifier, codeChallenge, "S256");
        
        result.Should().BeTrue();
    }

    [Theory]
    [InlineData("abcdefghijklmnopqrstuvwxyz+1234567890ABCDEFGH")] // + not allowed
    [InlineData("abcdefghijklmnopqrstuvwxyz/1234567890ABCDEFGH")] // / not allowed
    [InlineData("abcdefghijklmnopqrstuvwxyz=1234567890ABCDEFGH")] // = not allowed
    [InlineData("abcdefghijklmnopqrstuvwxyz 1234567890ABCDEFGH")] // space not allowed
    public void ValidateCodeVerifier_InvalidCharacters_ReturnsFalse(string codeVerifier)
    {
        var codeChallenge = _sut.GenerateCodeChallenge(codeVerifier);
        
        var result = _sut.ValidateCodeVerifier(codeVerifier, codeChallenge, "S256");
        
        result.Should().BeFalse();
    }

    // ===== Generate Code Challenge Tests =====
    
    [Fact]
    public void GenerateCodeChallenge_ReturnsBase64UrlEncoded()
    {
        var codeVerifier = "dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk";
        
        var challenge = _sut.GenerateCodeChallenge(codeVerifier);
        
        challenge.Should().NotContain("+");
        challenge.Should().NotContain("/");
        challenge.Should().NotEndWith("=");
    }

    [Fact]
    public void GenerateCodeChallenge_IsDeterministic()
    {
        var codeVerifier = "test-code-verifier-for-determinism-check";
        
        var challenge1 = _sut.GenerateCodeChallenge(codeVerifier);
        var challenge2 = _sut.GenerateCodeChallenge(codeVerifier);
        
        challenge1.Should().Be(challenge2);
    }
}

