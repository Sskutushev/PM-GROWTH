import { ApiError } from "../api";

// Domain codes the user can act on. Anything else keeps the message the API sent, and a
// non-API failure gets the generic line: inventing a reason would mislead.
const hints: Record<string, string> = {
  PERIOD_CLOSED: "Месяц закрыт — правки в нём запрещены.",
  DAILY_HOURS_EXCEEDED: "За день нельзя списать больше 24 часов.",
  CONCURRENCY_CONFLICT: "Запись изменил кто-то ещё. Обновите страницу.",
  RATE_NOT_FOUND: "На эту дату нет ставки сотрудника.",
  DATE_OUTSIDE_PROJECT_PERIOD: "Дата вне срока проекта.",
};

export function ErrorBanner({ error }: { error: Error }) {
  if (!(error instanceof ApiError)) {
    return (
      <div className="error" role="alert">
        Не удалось выполнить запрос. Попробуйте ещё раз.
      </div>
    );
  }

  const hint = hints[error.code];

  return (
    <div className="error" role="alert">
      <strong>{error.message}</strong>
      {hint && <span>{hint}</span>}
    </div>
  );
}
