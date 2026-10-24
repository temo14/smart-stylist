namespace SmartStylist.Modules.Catalog.Collection;

internal sealed class HttpPageFetcher(HttpClient httpClient) : IPageFetcher
{
    public const string ClientName = "catalog";

    public async Task<string> GetPageAsync(Uri url, CancellationToken cancellationToken)
    {
        using var response = await httpClient.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(cancellationToken);
    }

    public async Task<byte[]> GetFileAsync(Uri url, CancellationToken cancellationToken)
    {
        using var response = await httpClient.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsByteArrayAsync(cancellationToken);
    }
}
