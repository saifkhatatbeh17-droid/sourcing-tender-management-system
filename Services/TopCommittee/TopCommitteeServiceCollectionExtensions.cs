using SourcingTenderManagement.Contracts.TopCommittee;

namespace SourcingTenderManagement.Services.TopCommittee;

public static class TopCommitteeServiceCollectionExtensions
{
    public static IServiceCollection AddTopCommitteeFoundation(this IServiceCollection services)
    {
        services.AddScoped<TopCommitteeAccessService>();
        services.AddScoped<TopCommitteeReports>();
        services.AddScoped<TopCommitteeHistory>();
        services.AddScoped<TopCommitteeNotifications>();
        services.AddSingleton<TopCommitteeReportExport>();
        services.AddScoped<ITopCommitteeQueries, TopCommitteeQueryService>();
        services.AddScoped<ITopCommitteeTenderTransaction, TopCommitteeTenderTransaction>();
        services.AddScoped<ITopCommitteeVotingPause, TopCommitteeVotingPause>();
        services.AddScoped<ITopCommitteeClarificationStore, TopCommitteeClarificationStore>();
        services.AddScoped<TopCommitteeClarificationService>();
        services.AddScoped<ITopCommitteeClarifications>(provider => provider.GetRequiredService<TopCommitteeClarificationService>());
        services.AddScoped<ITopCommitteeManagerAnswers>(provider => provider.GetRequiredService<TopCommitteeClarificationService>());
        services.AddScoped<ITopCommitteeClarificationQueries, TopCommitteeClarificationQueries>();
        services.AddScoped<ITopCommitteeVotingStore, TopCommitteeVotingStore>();
        services.AddScoped<ITopCommitteeVoting, TopCommitteeVotingService>();
        services.AddScoped<ITopCommitteeVotingReadiness, TopCommitteeVotingDatabase>();
        return services;
    }
}
