using System.Net;
using System.Text;

namespace Interact.PartnerApi.Tests;

public class WebhookVerifierTests
{
    // The Standard Webhooks specification's own example.
    private const string Secret = "whsec_MfKQ9r8GKYqrTwjUPD8ILPZIo2LaLaSw";
    private const string Id = "msg_p5jXN8AQM9LWM0D4loKWxJek";
    private const string Timestamp = "1614265330";
    private const string Signature = "v1,g0hM9SsE+OTPJTGt/tmIKtSyZlE3uFJELVlNIOLJ1OE=";
    private static readonly DateTimeOffset SentAt = DateTimeOffset.FromUnixTimeSeconds(1614265330);

    private static byte[] Event(string body = """{"type":"run.completed","timestamp":"2021-02-25T15:02:10Z","data":{"run_version":1}}""") =>
        Encoding.UTF8.GetBytes(body);

    [Fact]
    public void The_specifications_example_verifies()
    {
        var verifier = new WebhookVerifier(Secret, new ManualTime(SentAt));

        // The spec's body isn't one of our events, so only the signature is checked here.
        var error = Record.Exception(() =>
            verifier.Verify(Id, Timestamp, Signature, Encoding.UTF8.GetBytes("""{"test": 2432232314}""")));

        Assert.IsNotType<WebhookVerificationException>(error);
    }

    [Fact]
    public void One_of_ours_verifies_and_reads()
    {
        var verifier = new WebhookVerifier(Secret, new ManualTime(SentAt));
        var body = Event();
        var signature = Sign(body);

        var received = verifier.Verify(Id, Timestamp, $"v1,bogus= {signature}", body);

        Assert.Equal(("run.completed", 1), (received.Type, received.Data.GetProperty("run_version").GetInt32()));
    }

    [Fact]
    public void A_changed_body_a_wrong_secret_or_an_old_delivery_is_refused()
    {
        var body = Event();
        var signature = Sign(body);
        var verifier = new WebhookVerifier(Secret, new ManualTime(SentAt));

        Assert.Throws<WebhookVerificationException>(() => verifier.Verify(Id, Timestamp, signature, Event("""{"type":"x"}""")));
        Assert.Throws<WebhookVerificationException>(() =>
            new WebhookVerifier("whsec_" + Convert.ToBase64String(new byte[24]), new ManualTime(SentAt))
                .Verify(Id, Timestamp, signature, body));
        Assert.Throws<WebhookVerificationException>(() =>
            new WebhookVerifier(Secret, new ManualTime(SentAt.AddMinutes(6))).Verify(Id, Timestamp, signature, body));
        Assert.Throws<WebhookVerificationException>(() => verifier.Verify(Id, Timestamp, null, body));
    }

    private static string Sign(byte[] body)
    {
        var key = Convert.FromBase64String(Secret["whsec_".Length..]);
        byte[] signed = [.. Encoding.UTF8.GetBytes($"{Id}.{Timestamp}."), .. body];
        var mac = System.Security.Cryptography.HMACSHA256.HashData(key, signed);
        return "v1," + Convert.ToBase64String(mac);
    }
}

public class WebhookSettingsTests : ClientTestBase
{
    [Fact]
    public async Task The_address_is_set_and_a_secret_made_as_your_system()
    {
        Handler.Token()
               .Then(HttpStatusCode.OK, """{"url":"https://hooks.example/x","has_secret":false,"events":["run.completed"]}""")
               .Then(HttpStatusCode.Created, """{"secret":"whsec_abc"}""");
        var client = Client();

        var settings = await client.SetWebhookAsync("https://hooks.example/x");
        var secret = await client.MakeWebhookSecretAsync();

        Assert.Equal(HttpMethod.Put, Handler.Sent[1].Method);
        Assert.EndsWith("/v1/webhooks", Handler.Sent[1].Uri.ToString());
        Assert.Contains("\"url\":\"https://hooks.example/x\"", Handler.Sent[1].Body);
        Assert.Equal(("https://hooks.example/x", "whsec_abc"), (settings.Url, secret));
    }
}
