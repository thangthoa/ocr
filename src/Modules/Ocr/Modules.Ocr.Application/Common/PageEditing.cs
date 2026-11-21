using Modules.Ocr.Application.Contracts;
using Modules.Ocr.Application.Ports;
using Modules.Ocr.Domain.Jobs;
using OppOcr.BuildingBlocks.Application.Results;

namespace Modules.Ocr.Application.Common;

/// <summary>What inserting, replacing and deleting pages share (API doc §3.8).</summary>
internal static class PageEditing
{
    /// <summary>
    /// The job as the service has it now, when its pages may change. Checked before a file is sent: the upload
    /// can be slow, and the service would refuse it anyway. The service still has the last word.
    /// </summary>
    public static async Task<Result<OcrJob>> LoadEditableAsync(IOcrApiClient api, OcrJobId id, CancellationToken cancellationToken)
    {
        var result = await api.GetAsync(id, cancellationToken);
        if (result.IsFailure)
            return OcrErrors.FromService(result.Error!);

        var job = result.Value;
        if (!job.AllowsPageEdits)
            return OcrErrors.PagesNotEditable;
        if (job.IsBusyWithPages)
            return OcrErrors.JobBusy;
        return job;
    }

    /// <summary>Maps the service's answer to a page change; its own codes become this module's errors.</summary>
    public static Error FromService(Error error) => error.Code switch
    {
        OcrServiceErrorCodes.JobBusy => OcrErrors.JobBusy,
        OcrServiceErrorCodes.LastPage => OcrErrors.LastPage,
        _ => OcrErrors.FromService(error),
    };

    /// <summary>
    /// Follows the job again until it is done (it is queued again after an insert or a replace), even when it
    /// was submitted before this instance started. A new status is announced (queued again); a deleted page
    /// leaves a done job done, and announcing it would read as "OCR finished".
    /// </summary>
    public static async Task TrackAsync(OcrJob before, OcrJob job, IOcrJobRegistry registry, IOcrJobNotifier notifier, CancellationToken cancellationToken)
    {
        if (registry.Update(job) is null)
            registry.Add(job);
        if (job.Status != before.Status)
            await notifier.NotifyAsync(new OcrJobChange(job.Id, job.FileName, job.Status, OcrJobChangeKind.Updated, job.ErrorCode), cancellationToken);
    }
}
