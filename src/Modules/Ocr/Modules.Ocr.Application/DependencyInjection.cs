using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Modules.Ocr.Application.Features.DeleteOcrJob;
using Modules.Ocr.Application.Features.DownloadOcrResult;
using Modules.Ocr.Application.Features.GetOcrJob;
using Modules.Ocr.Application.Features.GetOcrPageImage;
using Modules.Ocr.Application.Features.ListOcrJobs;
using Modules.Ocr.Application.Features.SyncTrackedOcrJobs;
using Modules.Ocr.Application.Features.UploadOcrFile;

namespace Modules.Ocr.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddOcrApplication(this IServiceCollection services)
    {
        services.AddSingleton<IValidator<UploadOcrFileCommand>, UploadOcrFileValidator>();

        services.AddScoped<UploadOcrFileHandler>();
        services.AddScoped<GetOcrJobHandler>();
        services.AddScoped<ListOcrJobsHandler>();
        services.AddScoped<DeleteOcrJobHandler>();
        services.AddScoped<GetOcrPageImageHandler>();
        services.AddScoped<DownloadOcrResultHandler>();
        services.AddScoped<SyncTrackedOcrJobsHandler>();
        return services;
    }
}
