# Code review

Приоритет: `P0` — риск остановки сервиса или неверных денег; `P1` — серьёзная ошибка корректности/надёжности; `P2` — сопровождаемость и UX. Пункты отсортированы по ущербу в production.

## TimesheetReportHandler.cs

| № | Ранг | Фрагмент | Риск в production | Исправление |
|---:|:---:|---|---|---|
| 1 | P0 | `Find(Empty).ToListAsync()` | Вся коллекция уходит в память: OOM, большой сетевой трафик, минуты ожидания на миллионах строк. | `$match` по полуинтервалу месяца и `$group` в MongoDB. |
| 2 | P0 | `Where(e.Date.Year...)` | Фильтрация выполняется после materialization; индекс не участвует. | `date >= start && date < nextMonth`. |
| 3 | P0 | запрос employee внутри цикла | N+1: миллион записей создаёт миллион round-trip. | Денормализованные `appliedRate/amount`; справочники загружать batch/lookup после group. |
| 4 | P0 | `.FirstOrDefaultAsync().Result` | Sync-over-async блокирует пул потоков и вызывает каскадные таймауты. | Полностью async, передавать cancellation token. |
| 5 | P0 | `Rates.FirstOrDefault()` | Берётся первый элемент, а не ставка с максимальной `From <= entry.Date`; отчёт врёт деньгами. | Отсортированный effective-date resolver. |
| 6 | P0 | `double Amount/Budget` | Двоичная погрешность в деньгах и накопление ошибки. | `decimal` в C#, Decimal128 в Mongo. |
| 7 | P0 | `Math.Round(..., 2)` | По умолчанию midpoint-to-even, правило не определено. | Единый `Money.Round(...AwayFromZero)`. |
| 8 | P0 | `row.Amount / row.Budget` | Нулевой бюджет создаёт `Infinity/NaN` либо исключение. | `percent = null` для нулевого бюджета. |
| 9 | P0 | `employee.Rates` без guards | Пустая ставка/битая ссылка валит весь отчёт с 500. | Явные domain errors и диагностика повреждённых ссылок. |
| 10 | P1 | `DateTime` и `.Month` | Полночь MSK после UTC-конвертации может попасть в прошлый месяц. | `DateOnly`, хранение UTC midnight, ISO `YYYY-MM-DD`. |
| 11 | P1 | token не используется | Отменённый клиентом тяжёлый запрос продолжает нагружать Mongo. | Пробросить token во все async API. |
| 12 | P1 | нет проверки Year/Month | `month=13` выглядит как пустой отчёт вместо ошибки контракта. | Валидатор запроса, 400 ProblemDetails. |
| 13 | P1 | `Percent` округлён до флага | 100,004% становится 100,00%, перерасход скрывается. | Флаги по raw percent, round только для UI. |
| 14 | P1 | округление не закреплено | Итог может отличаться от суммы видимых строк. | Округлять каждую запись, затем суммировать. |
| 15 | P1 | последовательный `await` в loop | Латентность растёт линейно даже без N+1. | Одна aggregation pipeline. |
| 16 | P1 | нет индекса | Report деградирует в COLLSCAN. | `{date:1,projectId:1}` и explain-проверка. |
| 17 | P2 | строки коллекций в handler | Опечатки ловятся только в runtime. | Централизованные collection names/store. |
| 18 | P2 | сущности рядом с handler | Смешаны domain, application и persistence. | Разнести слои и DTO. |
| 19 | P2 | handler зависит от `IMongoDatabase` | Use case нельзя юнит-тестировать без Mongo. | Зависимость от query-port. |
| 20 | P2 | `ContainsKey` + индексатор | Два поиска в словаре. | `TryGetValue`. |
| 21 | P2 | mutable response classes | DTO можно случайно изменить после построения. | Immutable records. |
| 22 | P2 | нет метрик/логов | Регрессию времени отчёта замечает пользователь. | Duration, row count, trace id, slow-query metric. |

## TimeEntriesPage.tsx

| № | Ранг | Фрагмент | Риск в production | Исправление |
|---:|:---:|---|---|---|
| 23 | P0 | `useEffect(...);` без deps | Каждый render делает новый GET: self-DDoS API. | `[year, month]` или React Query key. |
| 24 | P0 | `entries.push(body)` | State мутируется; React может не перерисовать. | Immutable update или invalidate query. |
| 25 | P0 | в state кладётся request body | Нет id/amount/names; `toFixed` падает, delete получает undefined. | Использовать ответ сервера или refetch. |
| 26 | P0 | response.ok не проверяется | После 400/409 показывается «Сохранено». | Typed client, ProblemDetails, visible error. |
| 27 | P1 | `toLocaleDateString()` | Контракт зависит от locale и неоднозначен. | `<input type=date>`, передавать ISO как есть. |
| 28 | P1 | hours — string | Сервер получает неверный тип; арифметика может дать NaN. | Decimal-compatible JSON number + schema validation. |
| 29 | P1 | `any[]` | TypeScript не защищает контракт и скрывает дефекты пункта 25. | Явные DTO и generated/openapi types. |
| 30 | P1 | loading не в finally | При ошибке индикатор остаётся навсегда. | Query state / `finally`. |
| 31 | P1 | нет cancellation | Ответ старого месяца может перетереть новый. | AbortSignal из queryFn. |
| 32 | P1 | client-side filter/total | При пагинации итог считается только по странице. | Фильтры, totals и pagination на сервере. |
| 33 | P1 | `==` | Неявное преобразование типов скрывает контрактные ошибки. | `===`. |
| 34 | P1 | нет защиты submit | Двойной клик создаёт дубли. | Disable while pending, idempotency key при необходимости. |
| 35 | P1 | нет валидации | 0 и 3,7 доходят до API; ошибка не связана с полем. | Formik/Yup + обязательная серверная проверка. |
| 36 | P1 | projectId — text input | Пользователь создаёт битые ссылки. | Справочник проектов. |
| 37 | P1 | delete без error handling | UI расходится с сервером, особенно в закрытом периоде. | Confirm, 409 message, invalidate on success. |
| 38 | P2 | `key={index}` | React переиспользует неправильные строки после удаления. | Stable `entry.id`. |
| 39 | P2 | `alert()` | Блокирующий и недоступный UX. | Inline banner/toast с focus management. |
| 40 | P2 | один компонент | Data access, form и presentation невозможно тестировать отдельно. | API client + feature components + server state. |
| 41 | P2 | нет thead/labels | Таблица и форма плохо доступны screen reader. | Semantic headers, labels, dialog attributes. |
| 42 | P2 | `toFixed` | Нет валюты и локализованных разделителей. | `Intl.NumberFormat('ru-RU',{currency:'RUB'})`. |

## Структурные изменения

В реализации код разделён на `Domain → Application → Infrastructure → Api`. Домен не зависит от Mongo или ASP.NET; application работает через `ITimesheetStore`; инфраструктура владеет pipelines и индексами; middleware централизует ProblemDetails. На клиенте API-контракт и разбор ошибок отделены от экранов, server-state ведёт React Query, форма не мутирует кэш.

Исправленные демонстрационные файлы находятся рядом с оригиналами. Они показывают направление исправления; production-реализация находится в `backend/` и `frontend/`.
