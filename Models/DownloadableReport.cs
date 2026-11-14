namespace SourcingTenderManagement.Models;

public record DownloadableReport(string Title, string[] Columns, List<string[]> Rows);
