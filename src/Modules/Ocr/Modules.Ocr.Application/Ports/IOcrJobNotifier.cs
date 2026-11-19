using Modules.Ocr.Application.Contracts;

namespace Modules.Ocr.Application.Ports;

/// <summary>Tells interested parties (e.g. open browser sessions) that a job changed.</summary>
public interface IOcrJobNotifier
{
    Task NotifyAsync(OcrJobChange change, CancellationToken cancellationToken);
}
