namespace SourcingTenderManagement.Data;
public static class RoleNames
{
    public const string Admin = "Admin";
    public const string Department = "Department";
    public const string SourcingOfficer = "SourcingOfficer";
    public const string EvaluationCommittee = "EvaluationCommittee";
    public const string SourcingManager = "SourcingManager";
    public const string TopCommittee = "TopCommittee";
    public const string Supplier = "Supplier";
    public const string EvaluationSourcingManager = "EvaluationSourcingManager";
    public const string EvaluationDepartment = "EvaluationDepartment";
    public const string EvaluationFinancial = "EvaluationFinancial";
    public const string EvaluationTechnical = "EvaluationTechnical";
    public const string TopCommitteeMember1 = "TopCommitteeMember1";
    public const string TopCommitteeMember2 = "TopCommitteeMember2";
    public const string TopCommitteeMember3 = "TopCommitteeMember3";
    public const string TopCommitteeMember4 = "TopCommitteeMember4";
    public static readonly string[] Primary = [
        Admin,         Department,         SourcingOfficer,         EvaluationCommittee,         SourcingManager,         TopCommittee,         Supplier];
    public static readonly string[] EvaluationPositions = [EvaluationSourcingManager, EvaluationDepartment, EvaluationFinancial, EvaluationTechnical];
    public static readonly string[] TopPositions = [TopCommitteeMember1, TopCommitteeMember2, TopCommitteeMember3, TopCommitteeMember4];
    public static IEnumerable<string> All => Primary.Concat(EvaluationPositions).Concat(TopPositions);
}
