using System.Globalization;
using Modules.Ocr.Application.Contracts;
using Modules.Ocr.Application.Ports;

namespace OppOcr.Web.Features.Ocr;

/// <summary>
/// In-process fan-out of job changes to open Blazor circuits. Single-instance only; a
/// multi-instance deployment would need a shared channel (see ADR 0002).
/// </summary>
public sealed partial class OcrJobNotifications(ILogger<OcrJobNotifications> logger) : IOcrJobNotifier
{
    private readonly Lock _lock = new();
    private ImmutableSubscribers _subscribers = ImmutableSubscribers.Empty;

    /// <summary>
    /// Subscribes a component. Changes are raised from the background worker, whose culture is
    /// the server default, so the subscriber's culture is captured here and restored for each
    /// call; otherwise text rendered in response would ignore the user's language.
    /// </summary>
    public IDisposable Subscribe(Func<OcrJobChange, Task> handler)
    {
        var subscriber = new Subscriber(handler, CultureInfo.CurrentCulture, CultureInfo.CurrentUICulture);
        lock (_lock)
            _subscribers = _subscribers.Add(subscriber);
        return new Subscription(this, subscriber);
    }

    public async Task NotifyAsync(OcrJobChange change, CancellationToken cancellationToken)
    {
        foreach (var subscriber in _subscribers.Items)
        {
            try
            {
                await subscriber.InvokeAsync(change);
            }
            catch (Exception ex)
            {
                // A disconnected circuit must not break notifications for everyone else or the worker.
                LogSubscriberFailed(ex, change.JobId.Value);
            }
        }
    }

    private void Unsubscribe(Subscriber subscriber)
    {
        lock (_lock)
            _subscribers = _subscribers.Remove(subscriber);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Ocr notifications: a subscriber failed for job {JobId}")]
    private partial void LogSubscriberFailed(Exception exception, string jobId);

    private sealed record Subscriber(Func<OcrJobChange, Task> Handler, CultureInfo Culture, CultureInfo UICulture)
    {
        public async Task InvokeAsync(OcrJobChange change)
        {
            // Culture is async-local: setting it here flows into the handler and anything it dispatches.
            CultureInfo.CurrentCulture = Culture;
            CultureInfo.CurrentUICulture = UICulture;
            await Handler(change);
        }
    }

    private sealed record ImmutableSubscribers(IReadOnlyList<Subscriber> Items)
    {
        public static readonly ImmutableSubscribers Empty = new([]);

        public ImmutableSubscribers Add(Subscriber subscriber) => new([.. Items, subscriber]);

        public ImmutableSubscribers Remove(Subscriber subscriber) => new([.. Items.Where(s => !ReferenceEquals(s, subscriber))]);
    }

    private sealed class Subscription(OcrJobNotifications owner, Subscriber subscriber) : IDisposable
    {
        public void Dispose() => owner.Unsubscribe(subscriber);
    }
}
