using SourcingTenderManagement.Data;
﻿using System;
using System.Collections.Generic;

namespace SourcingTenderManagement.Models.Entities;

public class Tender
{
    public int TenderId { get; set; }

    public int PurchaseRequestId { get; set; }

    public string CreatedByUserId { get; set; } = null!;

    public string? Category { get; set; }
    public string Title { get; set; } = null!;

    public string Description { get; set; } = null!;

    public string? Terms { get; set; }

    public DateTime OpensAt { get; set; }

    public DateTime ClosesAt { get; set; }

    public string Status { get; set; } = null!;

    public bool IsAcceptingOffers(DateTime now) =>
        Status is ("Open" or "Published") && OpensAt <= now && now < ClosesAt;

    public bool CanCancel(DateTime now) =>
        Status is ("Draft" or "Open" or "Published") && now < ClosesAt;

    public int? AwardedBidId { get; set; }

    public string? AwardNotes { get; set; }

    public DateTime? AwardedAt { get; set; }

    public DateTime CreatedAt { get; set; }

    public ICollection<AiAnalysis> AiAnalyses { get; set; } = new List<AiAnalysis>();

    public Bid? Bid { get; set; }

    public ICollection<Clarification> Clarifications { get; set; } = new List<Clarification>();

    public ICollection<CommitteeAssignment> CommitteeAssignments { get; set; } = new List<CommitteeAssignment>();

    public ApplicationUser CreatedByUser { get; set; } = null!;

    public ICollection<EvaluationCriterion> EvaluationCriteria { get; set; } = new List<EvaluationCriterion>();

    public EvaluationReport? EvaluationReport { get; set; }

    public ICollection<Invitation> Invitations { get; set; } = new List<Invitation>();

    public PurchaseRequest PurchaseRequest { get; set; } = null!;

    public ICollection<TenderApproval> TenderApprovals { get; set; } = new List<TenderApproval>();

    public ICollection<TenderDocument> TenderDocuments { get; set; } = new List<TenderDocument>();
}
