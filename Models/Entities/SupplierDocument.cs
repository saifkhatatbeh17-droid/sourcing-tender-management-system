using SourcingTenderManagement.Data;
﻿using System;
using System.Collections.Generic;

namespace SourcingTenderManagement.Models.Entities;

public class SupplierDocument
{
    public int SupplierDocumentId { get; set; }

    public int SupplierProfileId { get; set; }

    public string DocumentType { get; set; } = null!;

    public string OriginalFileName { get; set; } = null!;

    public string StoredFileName { get; set; } = null!;

    public string ContentType { get; set; } = null!;

    public long FileSizeBytes { get; set; }

    public DateOnly? ExpiresOn { get; set; }

    public DateTime UploadedAt { get; set; }

    public SupplierProfile SupplierProfile { get; set; } = null!;
}
