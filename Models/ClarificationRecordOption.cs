namespace SourcingTenderManagement.Models;

// One record (request, tender or offer) a user can pick when starting a clarification.
public record ClarificationRecordOption(string Kind, int Id, string Title, string Detail);
