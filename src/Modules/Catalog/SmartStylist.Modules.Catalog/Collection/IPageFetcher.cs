namespace SmartStylist.Modules.Catalog.Collection;

/// <summary>Downloads shop pages and files. One implementation per transport (plain HTTP, headless browser).</summary>
public interface IPageFetcher
{
    Task<string> GetPageAsync(Uri url, CancellationToken cancellationToken);

    Task<byte[]> GetFileAsync(Uri url, CancellationToken cancellationToken);
}
