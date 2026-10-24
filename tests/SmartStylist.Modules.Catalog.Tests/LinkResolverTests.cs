using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using SmartStylist.Modules.Catalog.Newsletters;

namespace SmartStylist.Modules.Catalog.Tests;

public class LinkResolverTests
{
    private static readonly ShopDomains JCrew = new("jcrew", "https://www.jcrew.com", []);

    private sealed class Redirects(Dictionary<string, string> hops) : HttpMessageHandler
    {
        public List<string> Requested { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri!.ToString();
            Requested.Add(url);
            var response = hops.TryGetValue(url, out var next)
                ? new HttpResponseMessage(HttpStatusCode.Found) { Headers = { Location = new Uri(next, UriKind.RelativeOrAbsolute) } }
                : new HttpResponseMessage(HttpStatusCode.OK);
            return Task.FromResult(response);
        }
    }

    [Fact]
    public async Task Follows_tracking_redirects_and_stops_before_the_shop_page()
    {
        var web = new Redirects(new()
        {
            ["https://click.e.jcrew.com/?qs=1"] = "https://t.example.net/r?id=9",
            ["https://t.example.net/r?id=9"] = "https://www.jcrew.com/p/mens/chino/BE123/?color=navy&utm_source=email#top",
        });
        var resolver = new LinkResolver(new HttpClient(web), NullLogger<LinkResolver>.Instance);

        var page = await resolver.ResolveAsync("https://click.e.jcrew.com/?qs=1", JCrew, CancellationToken.None);

        Assert.Equal("https://www.jcrew.com/p/mens/chino/BE123", page?.AbsoluteUri);
        Assert.DoesNotContain(web.Requested, u => u.Contains("www.jcrew.com"));
    }

    [Fact]
    public async Task Returns_null_when_the_link_ends_somewhere_else()
    {
        var resolver = new LinkResolver(new HttpClient(new Redirects([])), NullLogger<LinkResolver>.Instance);

        Assert.Null(await resolver.ResolveAsync("https://click.e.jcrew.com/?qs=unsub", JCrew, CancellationToken.None));
        Assert.Null(await resolver.ResolveAsync("not a url", JCrew, CancellationToken.None));
    }
}
