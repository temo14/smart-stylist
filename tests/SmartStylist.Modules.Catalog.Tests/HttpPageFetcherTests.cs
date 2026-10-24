using System.Net;
using System.Net.Http.Headers;
using SmartStylist.Modules.Catalog.Collection;

namespace SmartStylist.Modules.Catalog.Tests;

public class HttpPageFetcherTests
{
    private sealed class Sequence(params HttpStatusCode[] codes) : HttpMessageHandler
    {
        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var code = codes[Math.Min(Calls++, codes.Length - 1)];
            var response = new HttpResponseMessage(code) { Content = new StringContent("ok") };
            response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.Zero);
            return Task.FromResult(response);
        }
    }

    [Fact]
    public async Task Retries_once_after_429()
    {
        var handler = new Sequence(HttpStatusCode.TooManyRequests, HttpStatusCode.OK);
        var fetcher = new HttpPageFetcher(new HttpClient(handler));

        Assert.Equal("ok", await fetcher.GetPageAsync(new Uri("https://shop.example.com/p/1"), CancellationToken.None));
        Assert.Equal(2, handler.Calls);
    }

    [Fact]
    public async Task Gives_up_after_a_second_429()
    {
        var handler = new Sequence(HttpStatusCode.TooManyRequests);
        var fetcher = new HttpPageFetcher(new HttpClient(handler));

        await Assert.ThrowsAsync<HttpRequestException>(() => fetcher.GetPageAsync(new Uri("https://shop.example.com/p/1"), CancellationToken.None));
        Assert.Equal(2, handler.Calls);
    }
}
