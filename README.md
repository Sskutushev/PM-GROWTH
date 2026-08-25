# PM Growth — учёт трудозатрат

Fullstack-система для табеля и контроля стоимости проектов: .NET 8, MongoDB 7, React + TypeScript. Деньги рассчитываются по ставке, действовавшей в дату записи; закрытые периоды и конкурентные изменения защищены бизнес-правилами.

## Запуск — 5 шагов

1. Установите Docker Desktop и Git.
2. Выполните `git clone https://github.com/Sskutushev/PM-GROWTH.git && cd PM-GROWTH`.
3. Запустите `docker compose up -d --build`.
4. Загрузите контрольные данные: `curl -X POST http://localhost:8080/api/seed` (PowerShell: `Invoke-RestMethod -Method Post http://localhost:8080/api/seed`).
5. Откройте интерфейс: http://localhost:5173.

Swagger доступен на http://localhost:8080/swagger, health check — http://localhost:8080/health/live.

## Проверки

```bash
make verify   # сверяет отчёты за февраль и март с цифрами из задания
make quality  # build + tests + typecheck + lint + format
make test     # backend и frontend tests
```

Ожидаемый март: П-001 — 12 ч / 7 600 ₽ / 38%; П-002 — 10 ч / 7 000 ₽ / 140%; итог — 22 ч / 14 600 ₽.

## Навигация

- [REVIEW.md](REVIEW.md) — 42 проблемы исходного кода по приоритетам.
- [NOTES.md](NOTES.md) — допущения и инженерные решения.
- [ARCHITECTURE.md](ARCHITECTURE.md) — слои и поток данных.
- [AI-USAGE.md](AI-USAGE.md) — применение и критическая проверка ИИ.

Оригинальное задание сохранено в `test-task.pdf`; эталонные файлы для code review — в `code-review/`.
