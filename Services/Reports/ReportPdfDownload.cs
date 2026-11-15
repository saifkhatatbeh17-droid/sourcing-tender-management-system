using Microsoft.AspNetCore.Mvc;

namespace SourcingTenderManagement.Services.Reports;

internal static class ReportPdfDownload
{
    // Ordinary FileResult: the existing global watermark filter runs exactly once.
    public static IActionResult Create(Controller controller,Func<byte[]> render,string name)
    {
        try { return controller.File(render(),"application/pdf",name); }
        catch(Exception error) when(error is IOException or InvalidOperationException or ArgumentException
            or NotSupportedException or UnauthorizedAccessException)
        {
            controller.HttpContext.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("ReportPdf")
                .LogWarning("Report PDF rendering failed ({FailureType}).",error.GetType().Name);
            return controller.StatusCode(503,new ProblemDetails { Status=503,Title="The complete report could not be exported.",
                Detail="No partial PDF was returned. Check the report font configuration and try again." });
        }
    }
}
