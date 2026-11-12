using SourcingTenderManagement.Data;
﻿using System;
using System.Collections.Generic;

namespace SourcingTenderManagement.Models.Entities;

public class PurchaseRequestItem
{
    public int PurchaseRequestItemId { get; set; }

    public int PurchaseRequestId { get; set; }

    public string ItemName { get; set; } = null!;

    public decimal Quantity { get; set; }

    public string Unit { get; set; } = null!;

    public string Specifications { get; set; } = null!;

    public ICollection<BidItem> BidItems { get; set; } = new List<BidItem>();

    public PurchaseRequest PurchaseRequest { get; set; } = null!;
}
