using System.Reflection;
using FluentAssertions;
using Talos.Web.Services;

namespace Talos.Web.Tests.Services;

public class ProfileDiscoveryServiceNormalizationTests
{
    [Fact]
    public void NormalizeProfileUrl_LowercasesSchemeAndHostOnly()
    {
        var method = typeof(ProfileDiscoveryService).GetMethod(
            "NormalizeProfileUrl",
            BindingFlags.NonPublic | BindingFlags.Static);

        method.Should().NotBeNull();

        var result = (string)method!.Invoke(null, ["HTTPS://Example.COM/User/CaseSensitive?Token=ABCdef"])!;

        result.Should().Be("https://example.com/User/CaseSensitive?Token=ABCdef");
    }

    [Fact]
    public void NormalizeProfileUrl_AddsRootPathForDomainOnlyUrl()
    {
        var method = typeof(ProfileDiscoveryService).GetMethod(
            "NormalizeProfileUrl",
            BindingFlags.NonPublic | BindingFlags.Static);

        var result = (string)method!.Invoke(null, ["https://Example.COM"])!;

        result.Should().Be("https://example.com/");
    }
}
