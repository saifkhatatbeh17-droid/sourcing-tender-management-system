using SourcingTenderManagement.Data;
﻿using System;
using System.Collections.Generic;

namespace SourcingTenderManagement.Models.Entities;

public class ClarificationResponse
{
    public int ClarificationResponseId { get; set; }

    public int ClarificationId { get; set; }

    public string RespondedByUserId { get; set; } = null!;

    public string Message { get; set; } = null!;

    public DateTime CreatedAt { get; set; }

    public Clarification Clarification { get; set; } = null!;

    public ApplicationUser RespondedByUser { get; set; } = null!;
}
