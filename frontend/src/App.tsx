import { useMemo, useState } from "react";
import {
  QueryClient,
  QueryClientProvider,
  useMutation,
  useQuery,
  useQueryClient,
} from "@tanstack/react-query";
import { Form, Formik, Field } from "formik";
import * as yup from "yup";
import { api, ApiError, type Entry, type Lookup, type Report } from "./api";
import "./App.css";
const queryClient = new QueryClient({
  defaultOptions: { queries: { staleTime: 20_000, retry: 1 } },
});
const money = new Intl.NumberFormat("ru-RU", {
  style: "currency",
  currency: "RUB",
  maximumFractionDigits: 2,
});
const number = new Intl.NumberFormat("ru-RU", { maximumFractionDigits: 2 });
const current = "2026-03";
export default function App() {
  return (
    <QueryClientProvider client={queryClient}>
      <Workspace />
    </QueryClientProvider>
  );
}
function Workspace() {
  const [screen, setScreen] = useState<"entries" | "report">("entries");
  return (
    <div className="shell">
      <aside>
        <div className="brand">
          <span>PM</span>
          <strong>GROWTH</strong>
        </div>
        <p className="kicker">Проектный контроль</p>
        <nav>
          <button
            className={screen === "entries" ? "active" : ""}
            onClick={() => setScreen("entries")}
          >
            ◫ <span>Табель</span>
          </button>
          <button
            className={screen === "report" ? "active" : ""}
            onClick={() => setScreen("report")}
          >
            ↗ <span>Отчёт по проектам</span>
          </button>
        </nav>
        <div className="aside-foot">
          <i /> Система работает штатно
        </div>
      </aside>
      <main>{screen === "entries" ? <Entries /> : <ProjectReport />}</main>
    </div>
  );
}
function Entries() {
  const [month, setMonth] = useState(current);
  const [employeeId, setEmployee] = useState("");
  const [projectId, setProject] = useState("");
  const [editing, setEditing] = useState<Entry | null | "new">(null);
  const [year, monthNumber] = month.split("-").map(Number);
  const query = useQuery({
    queryKey: ["entries", month, employeeId, projectId],
    queryFn: () => api.entries(year, monthNumber, employeeId, projectId),
  });
  const employees = useQuery({
    queryKey: ["employees"],
    queryFn: api.employees,
  });
  const projects = useQuery({ queryKey: ["projects"], queryFn: api.projects });
  return (
    <>
      <Header
        eyebrow="Учёт времени"
        title="Табель"
        action={
          <button className="primary" onClick={() => setEditing("new")}>
            ＋ Добавить запись
          </button>
        }
      />
      <section className="filters">
        <label>
          Месяц
          <input
            type="month"
            value={month}
            onChange={(e) => setMonth(e.target.value)}
          />
        </label>
        <Select
          label="Сотрудник"
          value={employeeId}
          onChange={setEmployee}
          options={employees.data ?? []}
          all="Все сотрудники"
        />
        <Select
          label="Проект"
          value={projectId}
          onChange={setProject}
          options={projects.data ?? []}
          all="Все проекты"
        />
      </section>
      <div className="stats">
        <Metric
          label="Всего часов"
          value={number.format(query.data?.totalHours ?? 0)}
        />
        <Metric
          label="Стоимость работ"
          value={money.format(query.data?.totalAmount ?? 0)}
        />
        <Metric label="Записей" value={String(query.data?.totalCount ?? 0)} />
      </div>
      <section className="panel">
        <div className="panel-title">
          <h2>Записи за месяц</h2>
          <span>
            {query.isFetching
              ? "Обновляем…"
              : `${query.data?.totalCount ?? 0} записей`}
          </span>
        </div>
        {query.error && <ErrorBanner error={query.error} />}
        <div className="table-wrap">
          <table>
            <thead>
              <tr>
                <th>Дата</th>
                <th>Сотрудник</th>
                <th>Проект</th>
                <th>Часы</th>
                <th>Ставка</th>
                <th>Стоимость</th>
                <th>Комментарий</th>
                <th />
              </tr>
            </thead>
            <tbody>
              {query.data?.items.map((entry) => (
                <tr key={entry.id}>
                  <td>{formatDate(entry.date)}</td>
                  <td>
                    <strong>{entry.employeeName}</strong>
                  </td>
                  <td>
                    <span className="code">{entry.projectCode}</span>
                  </td>
                  <td>
                    {number.format(entry.hours)}{" "}
                    {entry.isOvertime && (
                      <span
                        className="overtime"
                        title={`За день ${entry.dailyHours} ч`}
                      >
                        Переработка
                      </span>
                    )}
                  </td>
                  <td>{money.format(entry.appliedRate)}</td>
                  <td>
                    <strong>{money.format(entry.amount)}</strong>
                  </td>
                  <td className="muted">{entry.comment || "—"}</td>
                  <td>
                    <button
                      className="icon"
                      aria-label="Редактировать"
                      onClick={() => setEditing(entry)}
                    >
                      •••
                    </button>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
        {!query.isLoading && query.data?.items.length === 0 && (
          <div className="empty">За выбранный период записей нет</div>
        )}
      </section>
      {editing && (
        <EntryModal
          entry={editing === "new" ? null : editing}
          employees={employees.data ?? []}
          projects={projects.data ?? []}
          onClose={() => setEditing(null)}
        />
      )}
    </>
  );
}
function EntryModal({
  entry,
  employees,
  projects,
  onClose,
}: {
  entry: Entry | null;
  employees: Lookup[];
  projects: Lookup[];
  onClose: () => void;
}) {
  const client = useQueryClient();
  const mutation = useMutation({
    mutationFn: (values: FormValues) =>
      api.save(entry?.id, {
        ...values,
        hours: Number(values.hours),
        version: entry?.version,
      }),
    onSuccess: async () => {
      await client.invalidateQueries({ queryKey: ["entries"] });
      onClose();
    },
  });
  const schema = yup.object({
    employeeId: yup.string().required("Выберите сотрудника"),
    projectId: yup.string().required("Выберите проект"),
    date: yup.string().required("Укажите дату"),
    hours: yup
      .number()
      .positive()
      .max(24)
      .test(
        "step",
        "Шаг — 0,5 часа",
        (value) => value != null && value % 0.5 === 0,
      ),
    comment: yup.string().max(300),
  });
  return (
    <div
      className="backdrop"
      role="presentation"
      onMouseDown={(e) => e.target === e.currentTarget && onClose()}
    >
      <div className="modal" role="dialog" aria-modal="true">
        <div className="modal-head">
          <div>
            <span className="kicker">Рабочее время</span>
            <h2>{entry ? "Изменить запись" : "Новая запись"}</h2>
          </div>
          <button className="close" onClick={onClose}>
            ×
          </button>
        </div>
        <Formik<FormValues>
          initialValues={{
            employeeId: entry?.employeeId ?? "",
            projectId: entry?.projectId ?? "",
            date: entry?.date ?? current + "-05",
            hours: entry?.hours ?? 8,
            comment: entry?.comment ?? "",
          }}
          validationSchema={schema}
          onSubmit={(v) => mutation.mutate(v)}
        >
          {({ errors, touched }) => (
            <Form>
              <div className="form-grid">
                <FormField
                  label="Сотрудник"
                  name="employeeId"
                  as="select"
                  error={touched.employeeId && errors.employeeId}
                >
                  <option value="">Выберите</option>
                  {employees.map((x) => (
                    <option key={x.id} value={x.id}>
                      {x.name}
                    </option>
                  ))}
                </FormField>
                <FormField
                  label="Проект"
                  name="projectId"
                  as="select"
                  error={touched.projectId && errors.projectId}
                >
                  <option value="">Выберите</option>
                  {projects.map((x) => (
                    <option key={x.id} value={x.id}>
                      {x.code} · {x.name}
                    </option>
                  ))}
                </FormField>
                <FormField
                  label="Дата"
                  name="date"
                  type="date"
                  error={touched.date && errors.date}
                />
                <FormField
                  label="Часы"
                  name="hours"
                  type="number"
                  step="0.5"
                  error={touched.hours && errors.hours}
                />
                <label className="wide">
                  Комментарий
                  <Field as="textarea" name="comment" rows={3} />
                </label>
              </div>
              {mutation.error && <ErrorBanner error={mutation.error} />}
              <div className="actions">
                <button type="button" onClick={onClose}>
                  Отмена
                </button>
                <button
                  className="primary"
                  type="submit"
                  disabled={mutation.isPending}
                >
                  {mutation.isPending ? "Сохраняем…" : "Сохранить"}
                </button>
              </div>
            </Form>
          )}
        </Formik>
      </div>
    </div>
  );
}
function ProjectReport() {
  const [month, setMonth] = useState(current);
  const [year, monthNumber] = month.split("-").map(Number);
  const query = useQuery({
    queryKey: ["report", month],
    queryFn: () => api.report(year, monthNumber),
  });
  const max = useMemo(
    () =>
      Math.max(...(query.data?.items.map((x) => x.percent ?? 0) ?? [0]), 100),
    [query.data],
  );
  return (
    <>
      <Header
        eyebrow="Финансовая аналитика"
        title="Стоимость по проектам"
        action={
          <label className="month-top">
            Период
            <input
              type="month"
              value={month}
              onChange={(e) => setMonth(e.target.value)}
            />
          </label>
        }
      />
      {query.error && <ErrorBanner error={query.error} />}
      <div className="stats">
        <Metric
          label="Трудозатраты"
          value={`${number.format(query.data?.totalHours ?? 0)} ч`}
        />
        <Metric
          label="Стоимость"
          value={money.format(query.data?.totalAmount ?? 0)}
        />
        <Metric
          label="Проектов"
          value={String(query.data?.items.length ?? 0)}
        />
      </div>
      <section className="panel report">
        <div className="panel-title">
          <h2>Освоение бюджета</h2>
          <span>Порог риска — 80%</span>
        </div>
        {query.data?.items.map((row) => (
          <ReportLine key={row.projectId} row={row} max={max} />
        ))}
        <div className="report-total">
          <span>Итого</span>
          <strong>{number.format(query.data?.totalHours ?? 0)} ч</strong>
          <strong>{money.format(query.data?.totalAmount ?? 0)}</strong>
        </div>
      </section>
    </>
  );
}
function ReportLine({
  row,
  max,
}: {
  row: Report["items"][number];
  max: number;
}) {
  const width =
    row.percent == null ? 0 : Math.min((row.percent / max) * 100, 100);
  return (
    <div className="report-row">
      <div>
        <span className="code">{row.projectCode}</span>
        <strong>{row.projectName}</strong>
      </div>
      <div className="bar">
        <i
          className={row.isOverspent ? "danger" : row.isAtRisk ? "risk" : ""}
          style={{ width: `${width}%` }}
        />
      </div>
      <div className="num">
        <strong>
          {row.percent == null ? "—" : `${number.format(row.percent)}%`}
        </strong>
        <small>
          {money.format(row.amount)} из {money.format(row.budget)}
        </small>
      </div>
    </div>
  );
}
function Header({
  eyebrow,
  title,
  action,
}: {
  eyebrow: string;
  title: string;
  action: React.ReactNode;
}) {
  return (
    <header>
      <div>
        <span className="kicker">{eyebrow}</span>
        <h1>{title}</h1>
      </div>
      {action}
    </header>
  );
}
function Metric({ label, value }: { label: string; value: string }) {
  return (
    <div className="metric">
      <span>{label}</span>
      <strong>{value}</strong>
    </div>
  );
}
function Select({
  label,
  value,
  onChange,
  options,
  all,
}: {
  label: string;
  value: string;
  onChange: (v: string) => void;
  options: Lookup[];
  all: string;
}) {
  return (
    <label>
      {label}
      <select value={value} onChange={(e) => onChange(e.target.value)}>
        <option value="">{all}</option>
        {options.map((x) => (
          <option key={x.id} value={x.id}>
            {x.code ? `${x.code} · ` : ""}
            {x.name}
          </option>
        ))}
      </select>
    </label>
  );
}
function FormField({
  label,
  error,
  children,
  ...props
}: {
  label: string;
  name: string;
  error?: string | false;
  children?: React.ReactNode;
  [key: string]: unknown;
}) {
  return (
    <label>
      {label}
      <Field {...props}>{children}</Field>
      {error && <small className="field-error">{error}</small>}
    </label>
  );
}
function ErrorBanner({ error }: { error: Error }) {
  return (
    <div className="error">
      {error instanceof ApiError
        ? error.message
        : "Не удалось выполнить запрос. Попробуйте ещё раз."}
    </div>
  );
}
function formatDate(value: string) {
  return new Intl.DateTimeFormat("ru-RU", {
    day: "2-digit",
    month: "short",
  }).format(new Date(`${value}T00:00:00`));
}
type FormValues = {
  employeeId: string;
  projectId: string;
  date: string;
  hours: number;
  comment: string;
};
