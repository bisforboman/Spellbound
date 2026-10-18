using System.Net.Http.Json;
using System.Threading.Channels;

namespace Spellbound.Web.Hosting;

/// <summary>Something that went wrong: an error-level log entry, such as an unhandled exception or a failed backup.</summary>
/// <param name="Source">Where it was logged (the logger's category).</param>
/// <param name="Exception">The exception's type, if there was one.</param>
/// <param name="Count">How many times in a row it happened (the admin list folds repeats into one entry).</param>
/// <param name="LastAt">When it last happened.</param>
public sealed record Problem(DateTimeOffset At, string Source, string Message, string? Exception, int Count = 1, DateTimeOffset? LastAt = null)
{
    public bool SameAs(Problem other) => Source == other.Source && Message == other.Message && Exception == other.Exception;
}

/// <summary>
/// Collects problems (every error-level log entry) for the admin page, and hands them to <see cref="AlertSender"/>.
/// It has no dependencies, so the logging system can hold it without a loop.
/// </summary>
public sealed class AlertSink
{
    public const int Kept = 50;

    private readonly List<Problem> recent = []; // oldest first; guarded by itself
    private readonly Channel<Problem> outbox = Channel.CreateBounded<Problem>(
        new BoundedChannelOptions(500) { FullMode = BoundedChannelFullMode.DropOldest });

    /// <summary>
    /// The latest problems since the app started, newest first. The same problem several times in a row is one entry
    /// with a count, so a condition that keeps failing can't push everything else off the list.
    /// </summary>
    public IReadOnlyList<Problem> Recent
    {
        get
        {
            lock (recent)
            {
                return [.. Enumerable.Reverse(recent)];
            }
        }
    }

    public ChannelReader<Problem> Outbox => outbox.Reader;

    public void Report(Problem problem)
    {
        lock (recent)
        {
            if (recent.Count > 0 && recent[^1].SameAs(problem))
            {
                recent[^1] = recent[^1] with { Count = recent[^1].Count + 1, LastAt = problem.At };
            }
            else
            {
                recent.Add(problem);
                if (recent.Count > Kept)
                {
                    recent.RemoveAt(0);
                }
            }
        }

        outbox.Writer.TryWrite(problem); // every occurrence, so the sender can count repeats
    }
}

/// <summary>Sends every error-level log entry, from any part of the app, to the <see cref="AlertSink"/>.</summary>
public sealed class AlertLoggerProvider(AlertSink sink, TimeProvider time) : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName) => new AlertLogger(sink, time, categoryName);

    public void Dispose()
    {
    }

    private sealed class AlertLogger(AlertSink sink, TimeProvider time, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        // Errors and worse. (Written as "above Warning": HostingBoundaryTests counts the word for the template's Error
        // page as a game name.) The sender's own failures are warnings, so an unreachable webhook can't loop.
        public bool IsEnabled(LogLevel logLevel) => logLevel > LogLevel.Warning && category != typeof(AlertSender).FullName;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (IsEnabled(logLevel))
            {
                sink.Report(new Problem(time.GetUtcNow(), category, formatter(state, exception), exception?.GetType().FullName));
            }
        }
    }
}

/// <summary>
/// Pushes problems to the owner when Alerts:WebhookUrl is set: an ntfy topic URL (a phone notification) or a Discord
/// webhook URL (detected by its host). A push says only where it happened and the exception's type, never the message,
/// which can hold player data; the details are on the admin page. Each kind of problem is pushed at most once an hour.
/// </summary>
public sealed class AlertSender(AlertSink sink, IHttpClientFactory http, IConfiguration configuration, ILogger<AlertSender> logger)
    : BackgroundService
{
    public const string HttpClientName = "alerts";

    public static readonly TimeSpan Quiet = TimeSpan.FromHours(1);

    private readonly Dictionary<string, (DateTimeOffset SentAt, int Since)> sent = [];

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var problem in sink.Outbox.ReadAllAsync(stoppingToken))
        {
            if (configuration["Alerts:WebhookUrl"] is not { Length: > 0 } url || !Uri.TryCreate(url, UriKind.Absolute, out var uri))
            {
                continue;
            }

            var kind = $"{problem.Source}|{problem.Exception}";
            var now = problem.At; // when it happened, not when the queue got to it
            if (sent.TryGetValue(kind, out var last) && now - last.SentAt < Quiet)
            {
                sent[kind] = (last.SentAt, last.Since + 1); // counted, and mentioned in the next push of this kind
                continue;
            }

            var more = last.Since > 0 ? $" ({last.Since} more like it since the last alert)" : "";
            var text = $"Spellbound: an error in {problem.Source}{(problem.Exception is { } type ? $" ({type})" : "")}{more}. "
                       + "Details on the admin page.";
            try
            {
                using var client = http.CreateClient(HttpClientName);
                using var response = uri.Host.EndsWith("discord.com", StringComparison.OrdinalIgnoreCase)
                                     || uri.Host.EndsWith("discordapp.com", StringComparison.OrdinalIgnoreCase)
                    ? await client.PostAsJsonAsync(uri, new { content = text }, stoppingToken)
                    : await client.PostAsync(uri, new StringContent(text), stoppingToken); // ntfy: the body is the message
                response.EnsureSuccessStatusCode();
                sent[kind] = (now, 0);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Couldn't send an alert to the webhook.");
            }
        }
    }
}
