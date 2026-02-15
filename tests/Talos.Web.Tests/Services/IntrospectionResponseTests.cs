using System.Text.Json;
using FluentAssertions;
using Talos.Web.Controllers;

namespace Talos.Web.Tests.Services;

/// <summary>
/// Tests that IntrospectionResponse serializes correctly per RFC 7662 §2.2 and IndieAuth §6.2.
/// Covers GAP-16: the "active" field MUST be a JSON boolean, not a string.
/// </summary>
public class IntrospectionResponseTests
{
    // ===== Inactive response =====

    [Fact]
    public void InactiveResponse_ActiveIsBooleanFalse()
    {
        var response = new IntrospectionResponse { Active = false };
        var json = JsonSerializer.Serialize(response);
        using var doc = JsonDocument.Parse(json);

        var active = doc.RootElement.GetProperty("active");
        active.ValueKind.Should().Be(JsonValueKind.False,
            "per RFC 7662 §2.2 and IndieAuth §6.2, 'active' MUST be a JSON boolean");
    }

    [Fact]
    public void InactiveResponse_ContainsOnlyActive()
    {
        var response = new IntrospectionResponse { Active = false };
        var json = JsonSerializer.Serialize(response);
        using var doc = JsonDocument.Parse(json);

        var properties = doc.RootElement.EnumerateObject().Select(p => p.Name).ToList();
        properties.Should().ContainSingle()
            .Which.Should().Be("active",
                "inactive introspection response should contain only 'active: false'");
    }

    // ===== Active response =====

    [Fact]
    public void ActiveResponse_ActiveIsBooleanTrue()
    {
        var response = new IntrospectionResponse
        {
            Active = true,
            Me = "https://example.com/",
            ClientId = "https://app.example.com/",
            Scope = "profile email",
            Exp = 1700000000
        };
        var json = JsonSerializer.Serialize(response);
        using var doc = JsonDocument.Parse(json);

        var active = doc.RootElement.GetProperty("active");
        active.ValueKind.Should().Be(JsonValueKind.True,
            "'active' MUST be a JSON boolean true, not a string");
    }

    [Fact]
    public void ActiveResponse_ContainsAllRequiredFields()
    {
        var response = new IntrospectionResponse
        {
            Active = true,
            Me = "https://example.com/",
            ClientId = "https://app.example.com/",
            Scope = "profile email",
            Exp = 1700000000
        };
        var json = JsonSerializer.Serialize(response);
        using var doc = JsonDocument.Parse(json);

        var properties = doc.RootElement.EnumerateObject().Select(p => p.Name).ToList();
        properties.Should().Contain("active");
        properties.Should().Contain("me");
        properties.Should().Contain("client_id");
        properties.Should().Contain("scope");
        properties.Should().Contain("exp");
    }

    [Fact]
    public void ActiveResponse_PropertyNamesAreSnakeCase()
    {
        var response = new IntrospectionResponse
        {
            Active = true,
            Me = "https://example.com/",
            ClientId = "https://app.example.com/",
            Scope = "profile",
            Exp = 1700000000
        };
        var json = JsonSerializer.Serialize(response);

        // Verify exact snake_case property names via [JsonPropertyName] attributes
        json.Should().Contain("\"active\"");
        json.Should().Contain("\"me\"");
        json.Should().Contain("\"client_id\"");
        json.Should().Contain("\"scope\"");
        json.Should().Contain("\"exp\"");

        // Must NOT contain PascalCase or camelCase equivalents
        json.Should().NotContain("\"Active\"");
        json.Should().NotContain("\"ClientId\"");
        json.Should().NotContain("\"clientId\"");
        json.Should().NotContain("\"Scope\"");
        json.Should().NotContain("\"Exp\"");
    }

    [Fact]
    public void ActiveResponse_ExpIsNumber()
    {
        var response = new IntrospectionResponse
        {
            Active = true,
            Me = "https://example.com/",
            ClientId = "https://app.example.com/",
            Scope = "profile",
            Exp = 1700000000
        };
        var json = JsonSerializer.Serialize(response);
        using var doc = JsonDocument.Parse(json);

        var exp = doc.RootElement.GetProperty("exp");
        exp.ValueKind.Should().Be(JsonValueKind.Number,
            "'exp' must be a numeric Unix timestamp, not a string");
        exp.GetInt64().Should().Be(1700000000);
    }
}
