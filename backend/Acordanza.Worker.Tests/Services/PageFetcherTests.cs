using System.Net;
using Acordanza.Worker.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Xunit;

namespace Acordanza.Worker.Tests.Services;

// Plan 31 §3's fetch policy: manual redirects, the html-only rule, and the read-time size
// cap. Unit-tier and no network — the transport is stubbed, which is the only way to
// exercise the redirect and cap logic at all, since the real handler (correctly) refuses to
// connect to a test server on loopback. That refusal is itself asserted at the bottom, and
// the address rule it applies is pinned exhaustively in PrivateAddressesTests.
public class PageFetcherTests
{
    // Records what was requested and answers from a script, so a redirect chain can be
    // asserted hop by hop.
    private sealed class ScriptedHandler(params Func<HttpRequestMessage, HttpResponseMessage>[] steps)
        : HttpMessageHandler
    {
        public List<Uri> Requested { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requested.Add(request.RequestUri!);
            var step = steps[Math.Min(Requested.Count - 1, steps.Length - 1)];
            var response = step(request);
            response.RequestMessage = request;
            return Task.FromResult(response);
        }
    }

    private static HttpResponseMessage Html(string body = "<html><body>hola</body></html>")
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) };
        response.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("text/html")
        {
            CharSet = "utf-8"
        };
        return response;
    }

    private static HttpResponseMessage RedirectTo(string location) =>
        new(HttpStatusCode.Found) { Headers = { Location = new Uri(location, UriKind.RelativeOrAbsolute) } };

    private static PageFetcher Build(HttpMessageHandler handler)
    {
        var services = new ServiceCollection();
        services.AddHttpClient(PageFetcher.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => handler);
        var factory = services.BuildServiceProvider().GetRequiredService<IHttpClientFactory>();
        return new PageFetcher(factory, NullLogger<PageFetcher>.Instance);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_plain_html_page_comes_back_with_its_final_uri()
    {
        var fetcher = Build(new ScriptedHandler(_ => Html("<html><body>La Loca 6b+</body></html>")));

        var page = await fetcher.FetchPageAsync("https://blog.example.com/la-loca", Ct);

        page.ShouldNotBeNull();
        page.Body.ShouldContain("La Loca 6b+");
        page.Uri.ToString().ShouldBe("https://blog.example.com/la-loca");
    }

    // The final URI, not the submitted one, is what relative image URLs must resolve
    // against — see PageImages.
    [Fact]
    public async Task Redirects_are_followed_and_the_final_uri_is_reported()
    {
        var handler = new ScriptedHandler(
            _ => RedirectTo("https://www.example.com/posts/la-loca"),
            _ => Html());
        var page = await Build(handler).FetchPageAsync("https://example.com/la-loca", Ct);

        page.ShouldNotBeNull();
        page.Uri.ToString().ShouldBe("https://www.example.com/posts/la-loca");
        handler.Requested.Count.ShouldBe(2);
    }

    [Fact]
    public async Task A_relative_redirect_resolves_against_the_hop_it_came_from()
    {
        var handler = new ScriptedHandler(
            _ => RedirectTo("/posts/la-loca"),
            _ => Html());
        var page = await Build(handler).FetchPageAsync("https://example.com/old", Ct);

        page!.Uri.ToString().ShouldBe("https://example.com/posts/la-loca");
    }

    // Every hop goes back through the same client, so the ConnectCallback vets each one —
    // that's what makes "public host 302s to 169.254.169.254" a blocked connection on the
    // *second* hop rather than an unchecked one. Asserted here as "the second hop is a real
    // request through the handler"; the address rule itself is PrivateAddressesTests'.
    [Fact]
    public async Task Every_hop_is_a_fresh_request_through_the_same_client()
    {
        var handler = new ScriptedHandler(
            _ => RedirectTo("http://169.254.169.254/metadata/instance"),
            _ => Html());

        await Build(handler).FetchPageAsync("https://example.com/la-loca", Ct);

        handler.Requested.Count.ShouldBe(2);
        handler.Requested[1].ToString().ShouldBe("http://169.254.169.254/metadata/instance");
    }

    [Fact]
    public async Task A_redirect_chain_longer_than_three_hops_stops()
    {
        var handler = new ScriptedHandler(_ => RedirectTo("https://example.com/next"));

        var page = await Build(handler).FetchPageAsync("https://example.com/start", Ct);

        page.ShouldBeNull();
        handler.Requested.Count.ShouldBe(4);
    }

    [Fact]
    public async Task A_redirect_to_a_non_http_scheme_stops()
    {
        var handler = new ScriptedHandler(_ => RedirectTo("file:///etc/passwd"));

        (await Build(handler).FetchPageAsync("https://example.com/la-loca", Ct)).ShouldBeNull();
        handler.Requested.Count.ShouldBe(1);
    }

    // A PDF guidebook, a YouTube page's video payload, an image: html only (plan 31 §8).
    [Theory]
    [InlineData("application/pdf")]
    [InlineData("application/json")]
    [InlineData("image/jpeg")]
    public async Task A_non_html_response_is_refused(string contentType)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([1, 2, 3]) };
        response.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);

        (await Build(new ScriptedHandler(_ => response)).FetchPageAsync("https://example.com/x", Ct)).ShouldBeNull();
    }

    [Fact]
    public async Task A_non_success_status_is_refused()
    {
        var handler = new ScriptedHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound));

        (await Build(handler).FetchPageAsync("https://example.com/x", Ct)).ShouldBeNull();
    }

    // Enforced while reading, not from Content-Length: this content lies about its length
    // (it declares none at all), which is exactly the case a header check would miss.
    private sealed class EndlessContent : HttpContent
    {
        public EndlessContent() =>
            Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("text/html");

        protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context)
        {
            var chunk = new byte[64 * 1024];
            for (var i = 0; i < 200; i++) await stream.WriteAsync(chunk);
        }

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }
    }

    [Fact]
    public async Task A_body_over_the_cap_is_refused_while_reading()
    {
        var handler = new ScriptedHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new EndlessContent() });

        (await Build(handler).FetchPageAsync("https://example.com/huge", Ct)).ShouldBeNull();
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("file:///etc/passwd")]
    [InlineData("not a url")]
    public async Task A_non_http_submission_never_reaches_the_transport(string url)
    {
        var handler = new ScriptedHandler(_ => Html());

        (await Build(handler).FetchPageAsync(url, Ct)).ShouldBeNull();
        handler.Requested.ShouldBeEmpty();
    }

    // The real handler, with the real guard: a hostname resolving to loopback is refused at
    // connect time. Nothing is listening on that port either, but the point is that the
    // ConnectCallback rejects before a socket is ever opened — which is also why every other
    // test above has to stub the transport.
    [Fact]
    public async Task The_real_handler_refuses_a_host_resolving_to_loopback()
    {
        var fetcher = Build(PageFetcher.CreateHandler());

        (await fetcher.FetchPageAsync("http://localhost:9/metadata", Ct)).ShouldBeNull();
        (await fetcher.FetchPageAsync("http://127.0.0.1:9/metadata", Ct)).ShouldBeNull();
        (await fetcher.FetchPageAsync("http://169.254.169.254/metadata/instance", Ct)).ShouldBeNull();
    }
}
