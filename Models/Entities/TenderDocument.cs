using SourcingTenderManagement.Data;
﻿using System;
using System.Collections.Generic;

namespace SourcingTenderManagement.Models.Entities;

public class TenderDocument
{
    public int TenderDocumentId { get; set; }

    public int TenderId { get; set; }

    public string OriginalFileName { get; set; } = null!;

    public string StoredFileName { get; set; } = null!;

    public string ContentType { get; set; } = null!;

    public long FileSizeBytes { get; set; }

    public bool IsMandatory { get; set; }

    public DateTime UploadedAt { get; set; }

    public Tender Tender { get; set; } = null!;
}
