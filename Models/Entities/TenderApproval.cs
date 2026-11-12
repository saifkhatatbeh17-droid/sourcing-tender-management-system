using SourcingTenderManagement.Data;
﻿using System;
using System.Collections.Generic;

namespace SourcingTenderManagement.Models.Entities;

public class TenderApproval
{
    public int TenderApprovalId { get; set; }

    public int TenderId { get; set; }

    public string UserId { get; set; } = null!;

    public int? CommitteeAssignmentId { get; set; }

    public string Stage { get; set; } = null!;

    public string Decision { get; set; } = null!;

    public string? Notes { get; set; }

    public DateTime DecidedAt { get; set; }

    public CommitteeAssignment? CommitteeAssignment { get; set; }

    public CommitteeAssignment? CommitteeAssignmentNavigation { get; set; }

    public Tender Tender { get; set; } = null!;

    public ApplicationUser User { get; set; } = null!;
}
