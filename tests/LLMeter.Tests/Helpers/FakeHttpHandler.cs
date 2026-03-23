using System.Net;

namespace LLMeter.Tests.Helpers;

public class FakeHttpHandler : HttpMessageHandler
{
    private readonly Dictionary<string, string> _responses;

    public FakeHttpHandler(Dictionary<string, string> responses)
    {
        _responses = responses;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var path = request.RequestUri!.AbsolutePath;
        var match = _responses.FirstOrDefault(kvp => path.StartsWith(kvp.Key));
        var content = match.Value ?? "{}";
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(content, System.Text.Encoding.UTF8, "application/json")
        });
    }
}
