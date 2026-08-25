// Исправленное направление: handler не знает о Mongo и не вычисляет деньги в памяти.
public sealed record ProjectReportRow(string ProjectId, string ProjectName, decimal Hours, decimal Amount, decimal Budget, decimal? Percent, bool Overspent);
public sealed record GetProjectReportQuery(int Year, int Month);
public interface IProjectReportQuery
{
    Task<IReadOnlyList<ProjectReportRow>> Execute(int year, int month, CancellationToken cancellationToken);
}
public sealed class TimesheetReportHandler(IProjectReportQuery report)
{
    public Task<IReadOnlyList<ProjectReportRow>> Handle(GetProjectReportQuery request, CancellationToken cancellationToken)
    {
        if (request.Month is < 1 or > 12) throw new ArgumentOutOfRangeException(nameof(request));
        return report.Execute(request.Year, request.Month, cancellationToken);
    }
}
