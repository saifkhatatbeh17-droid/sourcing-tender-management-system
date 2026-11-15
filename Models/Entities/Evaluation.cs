using SourcingTenderManagement.Data;
﻿using System;
using System.Collections.Generic;

namespace SourcingTenderManagement.Models.Entities;

public class Evaluation
{
    public int EvaluationId { get; set; }

    public int TenderId { get; set; }

    public int BidId { get; set; }

    public int CommitteeAssignmentId { get; set; }

    public string? Notes { get; set; }

    public string Status { get; set; } = null!;

    public DateTime? SubmittedAt { get; set; }

    public DateTime? ApprovedAt { get; set; }

    public decimal? FinancialMark { get; set; }
    public decimal? TechnicalMark { get; set; }

    public Bid Bid { get; set; } = null!;

    public CommitteeAssignment CommitteeAssignment { get; set; } = null!;

    public ICollection<EvaluationScore> EvaluationScores { get; set; } = new List<EvaluationScore>();
}
