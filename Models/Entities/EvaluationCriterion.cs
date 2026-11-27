namespace SourcingTenderManagement.Models.Entities;

public class EvaluationCriterion
{
    public int EvaluationCriterionId { get; set; }

    public int TenderId { get; set; }

    public string Name { get; set; } = null!;

    public string? Description { get; set; }

    public string Category { get; set; } = null!;

    public decimal Weight { get; set; }

    public bool IsMandatory { get; set; }

    public ICollection<EvaluationScore> EvaluationScores { get; set; } = new List<EvaluationScore>();

    public Tender Tender { get; set; } = null!;
}
