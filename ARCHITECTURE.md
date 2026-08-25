# Архитектура

```text
React UI → typed API client → ASP.NET endpoints → TimesheetService
                                              ↓
Domain policies ← Application contracts → ITimesheetStore
                                              ↓
                                  Mongo pipelines + indexes
```

- `Timesheet.Domain` — сущности и детерминированные правила, без внешних зависимостей.
- `Timesheet.Application` — use cases, DTO и порт хранения.
- `Timesheet.Infrastructure` — MongoDB, агрегации, индексы, seed и пересчёт ставок.
- `Timesheet.Api` — HTTP-маршруты, Swagger, health и единый ProblemDetails middleware.
- `frontend` — типизированный transport, React Query и feature UI.

Поток записи: загрузить employee/project → проверить закрытие → формат часов → границы проекта → effective rate → дневной лимит → вычислить и округлить сумму → atomic insert/replace. Порядок проверок является частью контракта.
