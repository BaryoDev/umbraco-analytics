using Umbraco.Cms.Core.Services;

namespace BaryoDev.Umbraco.Analytics.Collect;

/// <summary>Finds the published content a path points to, for script mode where the request is only a beacon.</summary>
public sealed class ContentKeyResolver(IDocumentUrlService urls)
{
    public Guid? Resolve(string path)
    {
        try
        {
            return urls.GetDocumentKeyByRoute(path.TrimStart('/'), null, null, false);
        }
        catch
        {
            return null;
        }
    }
}
