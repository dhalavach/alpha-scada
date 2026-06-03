using Alpha.Scada.Reporting.Contracts;
using Alpha.Scada.ServiceDefaults.Messaging;
using Wolverine;

namespace Alpha.Scada.Reporting;

public static class MessagingTopology
{
    public static void Configure(WolverineOptions options)
    {
        options.ListenForReportRequests("reporting-report-requested");
        options.PublishDomainEvent<ReportCompleted>(Topics.ReportCompleted);
    }
}
