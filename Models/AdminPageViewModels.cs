using SourcingTenderManagement.Data;
using System.ComponentModel.DataAnnotations;
using SourcingTenderManagement.Models.Entities;

namespace SourcingTenderManagement.Models;

public class UsersPageViewModel
{
    public List<ApplicationUser> Users { get; set; } = [];
    public CreateUserViewModel CreateUser { get; set; } = new();
}

public class EditUserViewModel
{
    [Required] public string Id { get; set; } = "";
    [Required, StringLength(150)] public string FullName { get; set; } = "";
    public int? DepartmentId { get; set; }
    public bool IsActive { get; set; }
}

public class EditDepartmentViewModel
{
    public int DepartmentId { get; set; }
    [Required, StringLength(100)] public string Name { get; set; } = "";
    public bool IsActive { get; set; }
}

public class NotificationBellViewModel
{
    public int UnreadCount { get; set; }
    public List<Notification> Items { get; set; } = [];
}
