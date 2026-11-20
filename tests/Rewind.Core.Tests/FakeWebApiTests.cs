namespace Rewind.Core.Tests;

public sealed class FakeWebApiTests
{
    // TEST-1: a client test that holds the client's next step back on a RendererThread can fail without
    // its fix only because of this. The failure the test gives reaches HttpClient at once, while the token
    // is still live, as a real handler's does from the network. If it waited on the renderer's thread as
    // well, HttpClient would see it only after the test cancels, and would make it a cancellation itself.
    [Fact]
    public void HoldAnswer_FailedWhileTheCallWaitsOnTheRenderer_FailsHttpClientsCallAtOnce()
    {
        var api = new FakeWebApi();
        using var http = new HttpClient(api);
        var answer = api.HoldAnswer("/movie");
        var sending = new RendererThread().Start(() => http.GetAsync(new Uri("https://api.test/movie"), TestContext.Current.CancellationToken));

        answer.SetException(new HttpRequestException("No such host is known."));

        Assert.IsType<HttpRequestException>(sending.Exception?.InnerException);
    }
}
