using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Modules.Ocr.Application;
using Modules.Ocr.Application.Ports;
using Modules.Ocr.Infrastructure;
using OppOcr.Web.Components;
using OppOcr.Web.Extensions;
using OppOcr.Web.Features.Mockups;
using OppOcr.Web.Features.Mockups.Approved;
using OppOcr.Web.Features.Mockups.Word;
using OppOcr.Web.Features.Ocr;
using OppOcr.Web.Localization;
using Serilog;

var builder = WebApplication.CreateBuilder(args);
builder.AddDotEnvInDevelopment();

builder.Services.AddSerilog((services, logger) => logger
    .ReadFrom.Configuration(builder.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext());

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.AddProblemDetails();
builder.Services.AddAppLocalization();

builder.Services.AddOcrApplication();
builder.Services.AddOcrInfrastructure(builder.Configuration);
builder.Services.AddSingleton<OcrJobNotifications>();
builder.Services.AddSingleton<IOcrJobNotifier>(sp => sp.GetRequiredService<OcrJobNotifications>());
builder.Services.AddMockups(builder.Configuration);

builder.Services.AddHealthChecks()
    .AddOcrHealthChecks(HealthCheckTags.Ready);

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseSerilogRequestLogging();
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();
app.UseRequestLocalization();

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.MapOcrEndpoints();
app.MapWordEditorEndpoints();
app.MapApprovedDocumentEndpoints();
app.MapCultureEndpoint();

// Liveness only proves the process responds; readiness also checks dependencies.
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = c => c.Tags.Contains(HealthCheckTags.Ready) });

await app.RunAsync();

internal static class HealthCheckTags
{
    public const string Ready = "ready";
}

/// <summary>Exposed for WebApplicationFactory-based tests.</summary>
public partial class Program;
