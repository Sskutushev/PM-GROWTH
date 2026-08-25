// Учебный проект. Обработчик отчёта "стоимость трудозатрат по проектам за месяц".
// Код рабочий: на небольшой базе отчёт строится и цифры выглядят правдоподобно.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using MongoDB.Driver;

namespace Demo.Api.Queries.Reports
{
    public class ProjectReportRow
    {
        public string ProjectId { get; set; }
        public string ProjectName { get; set; }
        public double Hours { get; set; }
        public double Amount { get; set; }
        public double Budget { get; set; }
        public double Percent { get; set; }
        public bool Overspent { get; set; }
    }

    public class GetProjectReportQuery : IRequest<List<ProjectReportRow>>
    {
        public int Year { get; set; }
        public int Month { get; set; }
    }

    public class TimesheetReportHandler : IRequestHandler<GetProjectReportQuery, List<ProjectReportRow>>
    {
        private readonly IMongoDatabase _db;

        public TimesheetReportHandler(IMongoDatabase db)
        {
            _db = db;
        }

        public async Task<List<ProjectReportRow>> Handle(GetProjectReportQuery request, CancellationToken token)
        {
            var entries = await _db.GetCollection<TimeEntry>("time_entries")
                .Find(FilterDefinition<TimeEntry>.Empty)
                .ToListAsync();

            var monthEntries = entries
                .Where(e => e.Date.Year == request.Year && e.Date.Month == request.Month)
                .ToList();

            var rows = new Dictionary<string, ProjectReportRow>();

            foreach (var entry in monthEntries)
            {
                var employee = _db.GetCollection<Employee>("employees")
                    .Find(e => e.Id == entry.EmployeeId)
                    .FirstOrDefaultAsync().Result;

                var rate = employee.Rates.FirstOrDefault().Value;

                var amount = Math.Round(entry.Hours * rate, 2);

                if (!rows.ContainsKey(entry.ProjectId))
                {
                    var project = await _db.GetCollection<Project>("projects")
                        .Find(p => p.Id == entry.ProjectId)
                        .FirstOrDefaultAsync();

                    rows[entry.ProjectId] = new ProjectReportRow
                    {
                        ProjectId = project.Id,
                        ProjectName = project.Name,
                        Budget = project.Budget
                    };
                }

                rows[entry.ProjectId].Hours += entry.Hours;
                rows[entry.ProjectId].Amount += amount;
            }

            foreach (var row in rows.Values)
            {
                row.Percent = Math.Round(row.Amount / row.Budget * 100, 2);
                row.Overspent = row.Percent > 100;
            }

            return rows.Values.OrderBy(r => r.ProjectName).ToList();
        }
    }

    // --- сущности (упрощённо) ---

    public class TimeEntry
    {
        public string Id { get; set; }
        public string EmployeeId { get; set; }
        public string ProjectId { get; set; }
        public DateTime Date { get; set; }
        public double Hours { get; set; }
        public string Comment { get; set; }
    }

    public class Employee
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public List<Rate> Rates { get; set; }
    }

    public class Rate
    {
        public DateTime From { get; set; }
        public double Value { get; set; }
    }

    public class Project
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public double Budget { get; set; }
    }
}
