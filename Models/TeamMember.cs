namespace SourcingTenderManagement.Models;

// The people shown on the public Team page.
public sealed record TeamMember(int Id, string Name, string Role, string Department, string Bio)
{
    public string? PortraitFile { get; init; }
    public string? LinkedInUrl { get; init; }
    public string? GitHubUrl { get; init; }
    public string? PortfolioUrl { get; init; }
    public bool IsFictional { get; init; } = true;
    // Face position inside the photo (CSS object-position) and crop zoom for the team reel.
    public string PortraitFocus { get; init; } = "50% 25%";
    public double PortraitZoom { get; init; } = 1;
    public string Portrait => $"~/images/team/{PortraitFile ?? $"member-{Id:D2}.jpg"}";

    public static IReadOnlyList<TeamMember> Roster { get; } =
    [
        new(1, "Amani Azzam", "Developer", "", "")
        {
            IsFictional = false,
            PortraitFile = "amani-azzam.jpg",
            PortraitFocus = "52% 30%",
            PortraitZoom = 1.35,
            LinkedInUrl = "https://www.linkedin.com/in/amani-alazzam-57359936a/",
            GitHubUrl = "https://github.com/amanialazzam04"
        },
        new(2, "Gaith Ammoureh", "Developer", "", "")
        {
            IsFictional = false,
            PortraitFile = "gaith.jpg",
            PortraitFocus = "50% 22%",
            GitHubUrl = "https://github.com/gh2ith",
            PortfolioUrl = "https://gh2ith.github.io/Portfolio/"
        },
        new(3, "Anas Ghawanmeh", "QA", "", "")
        {
            IsFictional = false,
            PortraitFile = "anas.jpg",
            PortraitFocus = "58% 20%",
            LinkedInUrl = "https://www.linkedin.com/in/anas-ghawanmeh-4047902b5/",
            GitHubUrl = "https://github.com/aghawanmeh857-alt"
        },
        new(4, "Yasmeen Talafha", "Developer", "", "")
        {
            IsFictional = false,
            PortraitFile = "yasmin.jpg",
            PortraitFocus = "52% 26%",
            LinkedInUrl = "https://www.linkedin.com/in/yasmeentalafha/",
            GitHubUrl = "https://github.com/yasmeenht"
        },
        new(5, "Taimaa Freahat", "Product Owner", "", "")
        {
            IsFictional = false,
            PortraitFile = "taimaa.jpg",
            PortraitFocus = "50% 24%",
            PortfolioUrl = "https://taimaafreahat4.github.io/project-portfolio/"
        },
        new(6, "Mohammad Mustafa", "Developer", "", "")
        {
            IsFictional = false,
            PortraitFile = "mohammad-mustafa.jpg",
            PortraitFocus = "84% 46%",
            PortraitZoom = 2.4,
            LinkedInUrl = "https://www.linkedin.com/in/mohammad-mustafa-71a9193b1/",
            PortfolioUrl = "https://moazzam9444-wq.github.io/Portfolio/"
        },
        new(7, "Saif Khatatbeh", "Scrum Master", "", "")
        {
            IsFictional = false,
            PortraitFile = "saif-khatatbeh.jpg",
            PortraitFocus = "54% 24%",
            LinkedInUrl = "https://www.linkedin.com/in/saif-khatatbeh-1a6ab1317/",
            GitHubUrl = "https://github.com/saifkhatatbeh17-droid"
        }
    ];
}
