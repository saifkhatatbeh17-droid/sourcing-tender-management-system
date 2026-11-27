namespace SourcingTenderManagement.Models.Entities;

public class EvaluationScore
{
    public int EvaluationScoreId { get; set; }

    public int TenderId { get; set; }

    public int EvaluationId { get; set; }

    public int EvaluationCriterionId { get; set; }

    public decimal Score { get; set; }

    public string? Notes { get; set; }

    public Evaluation Evaluation { get; set; } = null!;

    public EvaluationCriterion EvaluationCriterion { get; set; } = null!;
}
