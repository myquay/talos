namespace Talos.Web.Services;

/// <summary>
/// Service for parsing microformats data from HTML content.
/// </summary>
public interface IMicroformatsService
{
    /// <summary>
    /// Parses microformats data from HTML content.
    /// </summary>
    /// <param name="html">The HTML content to parse.</param>
    /// <param name="baseUrl">The base URL for resolving relative URLs.</param>
    /// <returns>A result containing extracted microformats data.</returns>
    MicroformatsResult Parse(string html, Uri baseUrl);
}

