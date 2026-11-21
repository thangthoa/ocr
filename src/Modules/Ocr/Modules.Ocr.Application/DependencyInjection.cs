using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Modules.Ocr.Application.Features.DeleteOcrJob;
using Modules.Ocr.Application.Features.DeleteOcrPage;
using Modules.Ocr.Application.Features.DownloadOcrResult;
using Modules.Ocr.Application.Features.GetOcrJob;
using Modules.Ocr.Application.Features.GetOcrPageImage;
using Modules.Ocr.Application.Features.GetOcrSourceFile;
using Modules.Ocr.Application.Features.InsertOcrPages;
using Modules.Ocr.Application.Features.ListOcrJobs;
using Modules.Ocr.Application.Features.ReceiveOcrJobCallback;
using Modules.Ocr.Application.Features.ReplaceOcrPage;
using Modules.Ocr.Application.Features.SyncTrackedOcrJobs;
using Modules.Ocr.Application.Features.UploadOcrFile;
using Modules.Ocr.Application.Features.UploadOcrPages;

namespace Modules.Ocr.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddOcrApplication(this IServiceCollection services)
    {
        services.AddSingleton<IValidator<UploadOcrFileCommand>, UploadOcrFileValidator>();
        services.AddSingleton<IValidator<UploadOcrPagesCommand>, UploadOcrPagesValidator>();
        services.AddSingleton<IValidator<InsertOcrPagesCommand>, InsertOcrPagesValidator>();
        services.AddSingleton<IValidator<ReplaceOcrPageCommand>, ReplaceOcrPageValidator>();

        services.AddScoped<UploadOcrFileHandler>();
        services.AddScoped<UploadOcrPagesHandler>();
        services.AddScoped<GetOcrJobHandler>();
        services.AddScoped<ListOcrJobsHandler>();
        services.AddScoped<DeleteOcrJobHandler>();
        services.AddScoped<GetOcrPageImageHandler>();
        services.AddScoped<DownloadOcrResultHandler>();
        services.AddScoped<SyncTrackedOcrJobsHandler>();
        services.AddScoped<GetOcrSourceFileHandler>();
        services.AddScoped<ReceiveOcrJobCallbackHandler>();
        services.AddScoped<InsertOcrPagesHandler>();
        services.AddScoped<ReplaceOcrPageHandler>();
        services.AddScoped<DeleteOcrPageHandler>();
        return services;
    }
}
