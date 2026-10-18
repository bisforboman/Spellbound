using System.Net;
using System.Threading.Channels;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Spellbound.Web.Hosting;

namespace Spellbound.Web.Tests;

/// <summary>Error-level log entries reach the admin page and, with a webhook set, the owner's phone or Discord.</summary>
public sealed class AlertTests : TestDatabase
{
    private readonly ManualTime time = new(new DateTimeOffset(2026, 10, 18, 12, 0, 0, TimeSpan.Zero));
    private readonly Webhook webhook = new();

    private WebApplicationFactory<Program> App(string? webhookUrl) => CreateApp(b =>
    {
        if (webhookUrl is not null)
        {
            b.UseSetting("Alerts:WebhookUrl", webhookUrl);
        }

        b.ConfigureTestServices(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(time);
            services.AddHttpClient(AlertSender.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => webhook);
        });
    });

    [Fact]
    public void Errors_are_listed_for_the_admin_and_warnings_are_not()
    {
        using var app = App(webhookUrl: null);
        var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Spellbound.Test");

        logger.LogWarning("Just a warning.");
        logger.LogError(new InvalidOperationException("boom"), "Something broke for {Who}.", "merlin");

        var problem = Assert.Single(app.Services.GetRequiredService<AlertSink>().Recent);
        Assert.Equal("Spellbound.Test", problem.Source);
        Assert.Equal("Something broke for merlin.", problem.Message);
        Assert.Equal(typeof(InvalidOperationException).FullName, problem.Exception);
    }

    [Fact]
    public void The_same_problem_in_a_row_is_one_entry_with_a_count()
    {
        using var app = App(webhookUrl: null);
        var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Spellbound.Test");

        for (var i = 0; i < 3; i++)
        {
            logger.LogError("Backup failed.");
        }

        logger.LogError("Something else broke.");

        var recent = app.Services.GetRequiredService<AlertSink>().Recent;
        Assert.Equal(["Something else broke.", "Backup failed."], recent.Select(p => p.Message));
        Assert.Equal(3, recent[1].Count);
    }

    [Fact]
    public async Task An_unhandled_error_in_a_request_is_listed()
    {
        // A page whose dependency throws, as a bug in it would: the privacy page reads its settings.
        using var app = CreateApp(b => b.ConfigureTestServices(services =>
        {
            services.RemoveAll<IOptions<PrivacyOptions>>();
            services.AddSingleton<IOptions<PrivacyOptions>>(new Broken());
        }));
        using var client = app.CreateClient();

        var response = await client.GetAsync(new Uri("/privacy", UriKind.Relative));

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        var problem = Assert.Single(app.Services.GetRequiredService<AlertSink>().Recent);
        Assert.Equal(typeof(InvalidOperationException).FullName, problem.Exception);
        Assert.StartsWith("Microsoft.AspNetCore", problem.Source, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_push_names_where_it_happened_but_never_the_message_and_repeats_wait_an_hour()
    {
        using var app = App("https://ntfy.example/spellbound-secret-topic");
        _ = app.Services; // starts the app and its alert sender
        var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Spellbound.Test");

        for (var i = 0; i < 3; i++)
        {
            logger.LogError(new InvalidOperationException("boom"), "Player merlin@spellbound.test broke it.");
        }

        var first = await webhook.NextAsync();
        time.Advance(AlertSender.Quiet + TimeSpan.FromMinutes(1));
        logger.LogError(new InvalidOperationException("boom"), "Player merlin@spellbound.test broke it again.");
        var second = await webhook.NextAsync();

        Assert.Equal("https://ntfy.example/spellbound-secret-topic", first.Url);
        Assert.Contains("Spellbound.Test", first.Body, StringComparison.Ordinal);
        Assert.Contains(nameof(InvalidOperationException), first.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("merlin", first.Body, StringComparison.Ordinal); // messages can hold player data
        Assert.Contains("2 more like it", second.Body, StringComparison.Ordinal);
        Assert.False(webhook.HasMore);
    }

    [Fact]
    public async Task A_Discord_webhook_gets_a_Discord_message()
    {
        using var app = App("https://discord.com/api/webhooks/1/token");
        _ = app.Services;

        app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Spellbound.Test").LogError("Broken.");

        var push = await webhook.NextAsync();
        Assert.StartsWith("{\"content\":\"Spellbound: an error in Spellbound.Test", push.Body, StringComparison.Ordinal);
    }

    private sealed record Push(string Url, string Body);

    private sealed class Broken : IOptions<PrivacyOptions>
    {
        public PrivacyOptions Value => throw new InvalidOperationException("a bug");
    }

    /// <summary>Stands in for ntfy or Discord: remembers each request and answers OK.</summary>
    private sealed class Webhook : HttpMessageHandler
    {
        private readonly Channel<Push> pushes = Channel.CreateUnbounded<Push>();

        public bool HasMore => pushes.Reader.TryPeek(out _);

        public async Task<Push> NextAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            return await pushes.Reader.ReadAsync(timeout.Token);
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await pushes.Writer.WriteAsync(new Push(request.RequestUri!.ToString(), await request.Content!.ReadAsStringAsync(cancellationToken)), cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }

    private sealed class ManualTime(DateTimeOffset start) : TimeProvider
    {
        private DateTimeOffset now = start;

        public override DateTimeOffset GetUtcNow() => now;

        public void Advance(TimeSpan by) => now += by;
    }
}
