namespace SourcingTenderManagement.Models;

public sealed record TeamMember(int Id, string Name, string Role, string Department, string Bio)
{
    public string Portrait => $"~/images/team/member-{Id:D2}.jpg";

    public static IReadOnlyList<TeamMember> Roster { get; } =
    [
        new(1, "Lina Haddad", "Quality Assurance", "QA", "Lina builds quality into everyday operations. She turns procedures and review findings into clear checks teams can apply consistently."),
        new(2, "Ahmad Nasser", "Quality Control", "QC", "Ahmad focuses on precise testing and reliable records. He helps teams trace each result back to its sample, method, and acceptance criteria."),
        new(3, "Sara Khalil", "Production", "Manufacturing", "Sara translates requirements into practical production routines. She works with quality and operations to make handoffs clear from planning through release."),
        new(4, "Omar Salim", "Procurement", "Supply Chain", "Omar connects business needs with qualified suppliers. He looks at specifications, availability, and lead times so teams can compare offers on the same basis."),
        new(5, "Rania Mansour", "Regulatory Affairs", "Regulatory", "Rania keeps requirements and records aligned throughout the product lifecycle. She helps teams understand which documents they need at each stage."),
        new(6, "Yousef Darwish", "Research & Development", "R&D", "Yousef explores new ideas through structured experiments. He documents what worked, what needs another test, and what production teams need to know."),
        new(7, "Maha Ibrahim", "Pharmaceutical Operations", "Operations", "Maha brings people, timelines, and processes together. She helps each team see what is ready, what is blocked, and who owns the next decision.")
    ];
}
