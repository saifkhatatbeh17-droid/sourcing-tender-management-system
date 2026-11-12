using SourcingTenderManagement.Models.Entities;

namespace SourcingTenderManagement.Models;

public class TendersPageViewModel
{
    public List<Tender> Tenders { get; set; } = [];
    public Tender? SelectedTender { get; set; }
}
