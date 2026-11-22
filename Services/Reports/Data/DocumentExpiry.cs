namespace SourcingTenderManagement.Services.Reports.Data;

public static class DocumentExpiry
{
    public static string State(DateOnly? expires, DateOnly today, int warningDays) =>
        expires is null ? "No expiry date" : expires < today ? "Expired" :
        expires.Value.DayNumber - today.DayNumber <= Math.Max(0, warningDays) ? "Expiring soon" : "Valid";
}
