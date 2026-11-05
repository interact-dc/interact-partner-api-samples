using System.Net;
using System.Text;

namespace Interact.PartnerApi.Tests;

/// <summary>A clock the test moves by hand.</summary>
public sealed class ManualTime(DateTimeOffset start) : TimeProvider
{
    private DateTimeOffset _now = start;

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan by) => _now += by;
}

/// <summary>What a request carried, read before the client disposes it.</summary>
public sealed record SentRequest(HttpMethod Method, Uri Uri, Dictionary<string, string> Headers, string Body,
                                 long? ContentLength = null);

/// <summary>Answers each request with the next queued response, and records what was sent.</summary>
public sealed class FakeHandler : HttpMessageHandler
{
    private readonly Queue<Func<HttpResponseMessage>> _responses = new();

    public List<SentRequest> Sent { get; } = [];

    public FakeHandler Then(HttpStatusCode status, string json = "{}", string contentType = "application/json",
                            params (string Name, string Value)[] headers)
    {
        _responses.Enqueue(() =>
        {
            var response = new HttpResponseMessage(status)
            {
                Content = new StringContent(json, Encoding.UTF8, contentType),
            };
            foreach (var (name, value) in headers)
            {
                response.Headers.TryAddWithoutValidation(name, value);
            }
            return response;
        });
        return this;
    }

    public FakeHandler Token(string accessToken = "token-1", int expiresIn = 600) =>
        Then(HttpStatusCode.OK,
             $$"""{"access_token":"{{accessToken}}","token_type":"Bearer","expires_in":{{expiresIn}},"scope":"tenants:manage"}""");

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var headers = request.Headers.ToDictionary(h => h.Key, h => string.Join(",", h.Value));
        // Read the declared length before the body: reading buffers it and would fill the length in.
        var length = request.Content?.Headers.ContentLength;
        var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
        Sent.Add(new SentRequest(request.Method, request.RequestUri!, headers, body, length));
        if (_responses.Count == 0)
        {
            throw new InvalidOperationException($"No response queued for {request.Method} {request.RequestUri}");
        }
        return _responses.Dequeue()();
    }
}
