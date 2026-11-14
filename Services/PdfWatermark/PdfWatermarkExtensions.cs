using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace SourcingTenderManagement.Services.PdfWatermark;

public static class PdfWatermarkExtensions
{
    public static IServiceCollection AddPdfWatermarks(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<IPdfWatermarkService, PdfWatermarkService>();
        services.AddScoped<PdfWatermarkResultFilter>();
        services.Configure<MvcOptions>(options => options.Filters.AddService<PdfWatermarkResultFilter>());
        return services;
    }
}
