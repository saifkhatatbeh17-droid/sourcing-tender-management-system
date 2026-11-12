using SourcingTenderManagement.Data;
﻿using System;
using System.Collections.Generic;

namespace SourcingTenderManagement.Models.Entities;

public class EvaluationReport
{
    public int EvaluationReportId { get; set; }

    public int TenderId { get; set; }

    public int? RecommendedBidId { get; set; }

    public string? CommitteeSummary { get; set; }

    public string? OfficerSummary { get; set; }

    public string? PreparedByUserId { get; set; }

    public string Status { get; set; } = null!;

    public DateTime? SubmittedAt { get; set; }

    public string? ManagerSummary { get; set; }

    public Bid? Bid { get; set; }

    public ApplicationUser? PreparedByUser { get; set; }

    public Tender Tender { get; set; } = null!;
}
