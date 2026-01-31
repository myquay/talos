namespace Talos.Web.Services.IdentityProviders;

public interface IIdentityProviderFactory
{
    IIdentityProvider? GetProviderForUrl(string url);
    IIdentityProvider? GetProviderByType(string providerType);
    IEnumerable<IIdentityProvider> GetAllProviders();
}

public class IdentityProviderFactory(IEnumerable<IIdentityProvider> providers) : IIdentityProviderFactory
{
    public IIdentityProvider? GetProviderForUrl(string url)
    {
        return providers.FirstOrDefault(p => p.CanHandle(url));
    }

    public IIdentityProvider? GetProviderByType(string providerType)
    {
        return providers.FirstOrDefault(p => 
            p.ProviderType.Equals(providerType, StringComparison.OrdinalIgnoreCase));
    }

    public IEnumerable<IIdentityProvider> GetAllProviders()
    {
        return providers;
    }
}

