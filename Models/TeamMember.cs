namespace SourcingTenderManagement.Models;

public sealed record TeamMember(int Id, string Name, string Role, string Department, string Bio)
{
    public string? PortraitFile { get; init; }
    public string? LinkedInUrl { get; init; }
    public string? GitHubUrl { get; init; }
    public string? PortfolioUrl { get; init; }
    public bool IsFictional { get; init; } = true;
    public string Portrait => $"~/images/team/{PortraitFile ?? $"member-{Id:D2}.jpg"}";

    public static IReadOnlyList<TeamMember> Roster { get; } =
    [
        new(1, "Amani Azzam", "Developer", "", "")
        {
            IsFictional = false,
            PortraitFile = "amani-azzam.jpg",
            LinkedInUrl = "https://www.linkedin.com/in/amani-alazzam-57359936a/",
            GitHubUrl = "https://github.com/amanialazzam04"
        },
        new(2, "Gaith", "Developer", "", "")
        {
            IsFictional = false,
            PortraitFile = "gaith.png",
            GitHubUrl = "https://github.com/gh2ith",
            PortfolioUrl = "https://gh2ith.github.io/Portfolio/"
        },
        new(3, "Anas Ghawanmeh", "QA", "", "")
        {
            IsFictional = false,
            PortraitFile = "anas.jpg",
            LinkedInUrl = "https://www.linkedin.com/in/anas-ghawanmeh-4047902b5/",
            GitHubUrl = "https://github.com/aghawanmeh857-alt"
        },
        new(4, "Yasmeen Talafha", "Team member", "", "")
        {
            IsFictional = false,
            PortraitFile = "yasmin.jpg",
            LinkedInUrl = "https://www.linkedin.com/in/yasmeentalafha/",
            GitHubUrl = "https://github.com/yasmeenht"
        },
        new(5, "Taimaa Freahat", "Product Owner", "", "")
        {
            IsFictional = false,
            PortraitFile = "taimaa.jpg",
            LinkedInUrl = "https://www.linkedin.com/feed/foryou/?highlightedUpdateUrn=urn%3Ali%3Aactivity%3A7526703320338669569&highlightedUpdateType=REACTIONS_BY_YOUR_NETWORK&origin=inapp",
            PortfolioUrl = "https://taimaafreahat4.github.io/project-portfolio/"
        },
        new(6, "Mohammad Mustafa", "Developer", "", "")
        {
            IsFictional = false,
            PortraitFile = "mohammad-mustafa.png",
            LinkedInUrl = "https://www.linkedin.com/in/mohammad-mustafa-71a9193b1/",
            PortfolioUrl = "https://moazzam9444-wq.github.io/Portfolio/"
        },
        new(7, "Saif Khatatbeh", "Scrum Master", "", "")
        {
            IsFictional = false,
            PortraitFile = "saif-khatatbeh.png",
            LinkedInUrl = "https://www.linkedin.com/in/saif-khatatbeh-1a6ab1317/",
            GitHubUrl = "https://github.com/saifkhatatbeh17-droid/new-portfolio"
        }
    ];
}
