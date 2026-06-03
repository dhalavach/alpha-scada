using Wolverine;
using Wolverine.Nats;

namespace Alpha.Scada.ServiceDefaults.Messaging;

public static class AlphaMessagingTopology
{
    public static WolverineOptions PublishDomainEvent<T>(this WolverineOptions options, string subject)
    {
        options.PublishMessage<T>().ToNatsSubject(subject).UseJetStream(Topics.DomainStream);
        return options;
    }

    public static WolverineOptions ListenForDomainEvent(this WolverineOptions options, string subject, string durableName)
    {
        options.ListenToNatsSubject(subject).UseJetStream(Topics.DomainStream, durableName);
        return options;
    }

    public static WolverineOptions PublishReportRequest<T>(this WolverineOptions options)
    {
        options.PublishMessage<T>().ToNatsSubject(Topics.ReportRequested).UseJetStream(Topics.JobsStream);
        return options;
    }

    public static WolverineOptions ListenForReportRequests(this WolverineOptions options, string durableName)
    {
        options.ListenToNatsSubject(Topics.ReportRequested).UseJetStream(Topics.JobsStream, durableName);
        return options;
    }
}
