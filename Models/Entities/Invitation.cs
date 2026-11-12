using SourcingTenderManagement.Data;
﻿using System;
using System.Collections.Generic;

namespace SourcingTenderManagement.Models.Entities;

public class Invitation
{
    public int InvitationId { get; set; }

    public int TenderId { get; set; }

    public int SupplierProfileId { get; set; }

    public string InvitedByUserId { get; set; } = null!;

    public string Status { get; set; } = null!;

    public DateTime SentAt { get; set; }

    public DateTime? RespondedAt { get; set; }

    public ICollection<Bid> Bids { get; set; } = new List<Bid>();

    public ApplicationUser InvitedByUser { get; set; } = null!;

    public SupplierProfile SupplierProfile { get; set; } = null!;

    public Tender Tender { get; set; } = null!;
}
