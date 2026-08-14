using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;

namespace Acordanza.Worker.Services;

// What came back, if anything. Uri is the *final* URL after redirects — the one relative
// image URLs on the page must resolve against, not the submitted one.
public record FetchedPage(Uri Uri, string Body, string ContentType);

public record FetchedImage(byte[] Bytes, string ContentType);

// The worker's own fetch, and the feature's security boundary (plan 31 §3).
//
// We fetch rather than handing the URL to the model's web-fetch tool for three reasons, in
// descending order of weight: we need the raw HTML to find images at all; we need to
// control what gets connected to; and we already own this pattern —
// RouteEnrichmentService.IsReachableHtmlPageAsync is the same shape.
//
// The same client downloads the images. An <img src> on a fetched page is exactly as
// attacker-supplied as the page URL was, so routing image downloads through a second,
// unguarded client would reopen everything this class closes. One client, one guard, both
// callers.
public class PageFetcher(IHttpClientFactory httpClientFactory, ILogger<PageFetcher> logger)
{
    public const string HttpClientName = "import-fetch";

    private const int MaxRedirects = 3;
    private const int MaxPageBytes = 2 * 1024 * 1024;
    private const int MaxImageBytes = 5 * 1024 * 1024;
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    // The named client's handler is where the guard lives. Registered in
    // ServiceCollectionExtensions; built here so the policy and its reasoning sit in one
    // file with the code that depends on it.
    public static SocketsHttpHandler CreateHandler() => new()
    {
        // Followed by hand instead (max 3 hops, each re-validated): a redirect the stack
        // follows for us is a connection we never got to vet, and "public host 302s to
        // 169.254.169.254" is the first thing anyone tries.
        AllowAutoRedirect = false,
        ConnectTimeout = TimeSpan.FromSeconds(5),
        AutomaticDecompression = DecompressionMethods.All,

        // The load-bearing part. A plain "resolve, check, then request" would be wrong:
        // checking DNS separately from connecting leaves a rebinding window — a hostname
        // that answers with a public address for our check and 169.254.169.254 for the
        // connection defeats it entirely, and the attacker controls the TTL. Connecting to
        // an IPAddress we selected ourselves closes that window, because the stack never
        // gets a second chance to resolve.
        ConnectCallback = async (ctx, ct) =>
        {
            var addresses = await Dns.GetHostAddressesAsync(ctx.DnsEndPoint.Host, ct);
            var allowed = addresses.Where(PrivateAddresses.IsPublic).ToArray();
            if (allowed.Length == 0)
            {
                throw new HttpRequestException($"blocked address for {ctx.DnsEndPoint.Host}");
            }

            var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
            try
            {
                await socket.ConnectAsync(allowed, ctx.DnsEndPoint.Port, ct);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch
            {
                socket.Dispose();
                throw;
            }
        }
    };

    // Null on anything that isn't a readable HTML page we were allowed to reach: a blocked
    // address, a redirect loop, a non-2xx, a PDF or a video page, a body over the cap. The
    // caller turns that into a Discarded row with a reason; the distinctions that matter to
    // the user are made there, not here.
    public async Task<FetchedPage?> FetchPageAsync(string url, CancellationToken cancellationToken)
    {
        var response = await SendFollowingRedirectsAsync(url, cancellationToken);
        if (response is null) return null;

        using var _ = response;
        var contentType = response.Content.Headers.ContentType?.MediaType;
        if (!string.Equals(contentType, "text/html", StringComparison.OrdinalIgnoreCase))
        {
            logger.LogInformation("page fetch {Url}: content type {ContentType} is not text/html", url, contentType);
            return null;
        }

        var bytes = await ReadCappedAsync(response, MaxPageBytes, cancellationToken);
        if (bytes is null) return null;

        var encoding = ResolveEncoding(response.Content.Headers.ContentType);
        var body = encoding.GetString(bytes);
        return new FetchedPage(response.RequestMessage!.RequestUri!, body, contentType);
    }

    // Same client, same guard — see the class comment. Content type is checked against the
    // image allowlist by the caller (it owns the "what may become a Topo" rule); here we
    // only enforce the size cap and that something came back.
    public async Task<FetchedImage?> FetchImageAsync(Uri url, CancellationToken cancellationToken)
    {
        var response = await SendFollowingRedirectsAsync(url.ToString(), cancellationToken);
        if (response is null) return null;

        using var _ = response;
        var contentType = response.Content.Headers.ContentType?.MediaType;
        if (string.IsNullOrEmpty(contentType)) return null;

        var bytes = await ReadCappedAsync(response, MaxImageBytes, cancellationToken);
        return bytes is null ? null : new FetchedImage(bytes, contentType);
    }

    private async Task<HttpResponseMessage?> SendFollowingRedirectsAsync(string url, CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || !IsHttp(uri)) return null;

        var client = httpClientFactory.CreateClient(HttpClientName);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(Timeout);

        for (var hop = 0; hop <= MaxRedirects; hop++)
        {
            HttpResponseMessage response;
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, uri);
                // Some of the long-tail hosts plan 29 kept in scope (blogspot, wordpress)
                // serve a different, emptier page to a client that sends no Accept.
                request.Headers.Accept.ParseAdd("text/html,application/xhtml+xml,image/*;q=0.8,*/*;q=0.5");
                response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException)
            {
                // A blocked address arrives here as an HttpRequestException out of the
                // ConnectCallback, which is exactly the same non-answer as a timeout: we
                // never read a page. Logged, never surfaced verbatim — the user gets the
                // Spanish outcome, not our network topology.
                logger.LogInformation(ex, "page fetch {Url} failed", uri);
                return null;
            }

            if (response.StatusCode is HttpStatusCode.MovedPermanently or HttpStatusCode.Found
                or HttpStatusCode.SeeOther or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect)
            {
                var location = response.Headers.Location;
                response.Dispose();
                if (location is null) return null;

                // Relative Locations are legal and common; resolved against the hop we are
                // on, then re-checked for scheme. The address check happens again inside
                // the ConnectCallback on the next SendAsync, which is the point of doing
                // this by hand.
                uri = location.IsAbsoluteUri ? location : new Uri(uri, location);
                if (!IsHttp(uri))
                {
                    logger.LogInformation("page fetch redirect to non-http scheme {Url}, stopping", uri);
                    return null;
                }
                continue;
            }

            if (!response.IsSuccessStatusCode)
            {
                logger.LogInformation("page fetch {Url}: status {Status}", uri, (int)response.StatusCode);
                response.Dispose();
                return null;
            }

            return response;
        }

        logger.LogInformation("page fetch {Url}: more than {Max} redirects, stopping", url, MaxRedirects);
        return null;
    }

    private static bool IsHttp(Uri uri) => uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps;

    // Enforced *while reading*, not from Content-Length: a server can lie about the header
    // or omit it entirely, and CopyToAsync into an unbounded MemoryStream is the failure
    // that follows. Over the cap is a null, not a truncation — half a page is a worse input
    // than no page, and half an image isn't decodable anyway.
    private async Task<byte[]?> ReadCappedAsync(HttpResponseMessage response, int cap, CancellationToken cancellationToken)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var buffer = new byte[81920];
        using var accumulated = new MemoryStream();

        while (true)
        {
            int read;
            try
            {
                read = await stream.ReadAsync(buffer, cancellationToken);
            }
            catch (Exception ex) when (ex is IOException or HttpRequestException or TaskCanceledException)
            {
                logger.LogInformation(ex, "page fetch body read failed");
                return null;
            }

            if (read == 0) break;

            if (accumulated.Length + read > cap)
            {
                logger.LogInformation("page fetch body exceeded {Cap} bytes, stopping", cap);
                return null;
            }
            accumulated.Write(buffer, 0, read);
        }

        return accumulated.ToArray();
    }

    // Honour the charset when the server states one it knows, fall back to UTF-8. Spanish
    // climbing blogs on old hosting are the reason: a latin-1 page decoded as UTF-8 turns
    // «Peña» into mojibake, and the route name is the one field that must survive verbatim.
    private static System.Text.Encoding ResolveEncoding(MediaTypeHeaderValue? contentType)
    {
        var charset = contentType?.CharSet?.Trim('"');
        if (string.IsNullOrEmpty(charset)) return System.Text.Encoding.UTF8;
        try
        {
            return System.Text.Encoding.GetEncoding(charset);
        }
        catch (ArgumentException)
        {
            return System.Text.Encoding.UTF8;
        }
    }
}
