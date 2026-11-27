using SourcingTenderManagement.Data;

namespace SourcingTenderManagement.Models.Entities;

// Database table: a company department that can send purchase requests.
public class Department
{
    public int DepartmentId { get; set; }

    public string Name { get; set; } = null!;

    public bool IsActive { get; set; }

    public ICollection<ApplicationUser> Users { get; set; } = new List<ApplicationUser>();

    public ICollection<PurchaseRequest> PurchaseRequests { get; set; } = new List<PurchaseRequest>();
}
