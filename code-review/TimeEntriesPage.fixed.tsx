// Corrected version of TimeEntriesPage.tsx. The numbers in the comments point at the rows of
// REVIEW.md, so every fix can be traced back to the defect it answers.
//
// The shape of the fix: the API contract is typed and lives outside the screen, server state
// belongs to React Query, filters and totals are computed by the server, and the form neither
// mutates the cache nor announces success it did not get. Production code lives in frontend/.

import { useState } from "react";
import {
  keepPreviousData,
  useMutation,
  useQuery,
  useQueryClient,
} from "@tanstack/react-query";

// (29) Explicit DTOs instead of any[]: the compiler now catches the contract drift that hid
// defect 25 in the original.
type Entry = {
  id: string;
  employeeId: string;
  employeeName: string;
  projectId: string;
  projectCode: string;
  date: string;
  hours: number;
  amount: number;
  version: number;
};

type Page = {
  items: Entry[];
  page: number;
  pageSize: number;
  totalCount: number;
  totalHours: number;
  totalAmount: number;
};

type Lookup = { id: string; code: string; name: string };

// (26) A failed request becomes a typed error carrying the machine-readable code, so the UI can
// react to PERIOD_CLOSED differently from a network failure instead of showing "Сохранено".
class ApiError extends Error {
  constructor(
    readonly code: string,
    message: string,
  ) {
    super(message);
  }
}

const request = async <T,>(path: string, init?: RequestInit): Promise<T> => {
  const response = await fetch(`/api${path}`, {
    ...init,
    headers: { "Content-Type": "application/json", ...init?.headers },
  });

  if (!response.ok) {
    const problem = await response.json().catch(() => ({}));
    throw new ApiError(
      problem.code ?? "REQUEST_FAILED",
      problem.title ?? "Не удалось выполнить запрос.",
    );
  }

  return response.status === 204 ? (undefined as T) : response.json();
};

// (32) Filtering, totals and pagination are query parameters: the totals below are the month's,
// not the visible page's.
const api = {
  entries: (
    year: number,
    month: number,
    employeeId: string,
    page: number,
    signal: AbortSignal,
  ) =>
    request<Page>(
      `/time-entries?${new URLSearchParams({
        year: String(year),
        month: String(month),
        page: String(page),
        pageSize: "25",
        ...(employeeId ? { employeeId } : {}),
      })}`,
      { signal },
    ),
  employees: (signal: AbortSignal) => request<Lookup[]>("/employees", { signal }),
  projects: (signal: AbortSignal) => request<Lookup[]>("/projects", { signal }),
  create: (body: NewEntry) =>
    request<Entry>("/time-entries", { method: "PUT", body: JSON.stringify(body) }),
  // (37) The version travels with the delete, so a stale row cannot remove somebody else's edit.
  remove: (id: string, version: number) =>
    request<void>(`/time-entries/${id}?version=${version}`, { method: "DELETE" }),
};

type NewEntry = {
  employeeId: string;
  projectId: string;
  date: string;
  hours: number;
  comment: string;
};

// (42) One formatter per kind of value, with the currency and the Russian separators.
const money = new Intl.NumberFormat("ru-RU", {
  style: "currency",
  currency: "RUB",
});
const number = new Intl.NumberFormat("ru-RU", { maximumFractionDigits: 2 });

export function TimeEntriesPage({
  year,
  month,
}: {
  year: number;
  month: number;
}) {
  const [employeeId, setEmployeeId] = useState("");
  const [page, setPage] = useState(1);

  // (23) The query key is the dependency list: a render does not refetch, a changed month does.
  // (31) The signal cancels a superseded request, so an old month cannot overwrite a new one.
  // (30) Loading state belongs to the query, so it cannot get stuck after an error.
  const entries = useQuery({
    queryKey: ["time-entries", year, month, employeeId, page],
    queryFn: ({ signal }) => api.entries(year, month, employeeId, page, signal),
    placeholderData: keepPreviousData,
  });

  const employees = useQuery({
    queryKey: ["employees"],
    queryFn: ({ signal }) => api.employees(signal),
  });

  const totalPages = Math.max(
    1,
    Math.ceil((entries.data?.totalCount ?? 0) / (entries.data?.pageSize ?? 25)),
  );

  return (
    <section>
      <h2>
        Табель за {month}.{year}
      </h2>

      {/* (41) Every control has a label, so a screen reader announces what it changes. */}
      <label>
        Сотрудник
        <select
          value={employeeId}
          onChange={(event) => {
            setEmployeeId(event.target.value);
            setPage(1);
          }}
        >
          <option value="">Все сотрудники</option>
          {employees.data?.map((employee) => (
            <option key={employee.id} value={employee.id}>
              {employee.name}
            </option>
          ))}
        </select>
      </label>

      <EntryForm year={year} month={month} />

      {entries.isPending && <p>Загрузка…</p>}
      {entries.error && (
        <p role="alert">{(entries.error as ApiError).message}</p>
      )}

      <table>
        {/* (41) A real header row: the table is no longer a grid of anonymous cells. */}
        <thead>
          <tr>
            <th scope="col">Дата</th>
            <th scope="col">Сотрудник</th>
            <th scope="col">Проект</th>
            <th scope="col">Часы</th>
            <th scope="col">Стоимость</th>
            <th scope="col">
              <span hidden>Действия</span>
            </th>
          </tr>
        </thead>
        <tbody>
          {/* (38) The stable id as the key: deleting a row no longer shifts the rest. */}
          {entries.data?.items.map((entry) => (
            <EntryRow key={entry.id} entry={entry} />
          ))}
        </tbody>
      </table>

      <nav aria-label="Постраничная навигация">
        <button onClick={() => setPage(page - 1)} disabled={page <= 1}>
          Назад
        </button>
        <span>
          Страница {page} из {totalPages}
        </span>
        <button onClick={() => setPage(page + 1)} disabled={page >= totalPages}>
          Вперёд
        </button>
      </nav>

      <p>
        Итого за месяц: {number.format(entries.data?.totalHours ?? 0)} ч ·{" "}
        {money.format(entries.data?.totalAmount ?? 0)}
      </p>
    </section>
  );
}

function EntryRow({ entry }: { entry: Entry }) {
  const client = useQueryClient();
  const [confirming, setConfirming] = useState(false);

  const remove = useMutation({
    mutationFn: () => api.remove(entry.id, entry.version),
    onSuccess: () => client.invalidateQueries({ queryKey: ["time-entries"] }),
  });

  return (
    <tr>
      <td>{entry.date}</td>
      <td>{entry.employeeName}</td>
      <td>{entry.projectCode}</td>
      <td>{number.format(entry.hours)}</td>
      <td>{money.format(entry.amount)}</td>
      <td>
        {/* (37, 39) Deletion asks first and reports the refusal inline; no blocking alert. */}
        {confirming ? (
          <>
            <button onClick={() => remove.mutate()} disabled={remove.isPending}>
              {remove.isPending ? "Удаляем…" : "Точно удалить"}
            </button>
            <button onClick={() => setConfirming(false)}>Отмена</button>
          </>
        ) : (
          <button onClick={() => setConfirming(true)}>Удалить</button>
        )}
        {remove.error && (
          <span role="alert">{(remove.error as ApiError).message}</span>
        )}
      </td>
    </tr>
  );
}

function EntryForm({ year, month }: { year: number; month: number }) {
  const client = useQueryClient();
  const [values, setValues] = useState<NewEntry>({
    employeeId: "",
    projectId: "",
    // (27) ISO from an input type=date: no locale-dependent string ever reaches the API.
    date: `${year}-${String(month).padStart(2, "0")}-01`,
    hours: 8,
    comment: "",
  });

  const employees = useQuery({
    queryKey: ["employees"],
    queryFn: ({ signal }) => api.employees(signal),
  });
  const projects = useQuery({
    queryKey: ["projects"],
    queryFn: ({ signal }) => api.projects(signal),
  });

  const create = useMutation({
    mutationFn: () => api.create(values),
    // (24, 25) The cache is invalidated instead of being patched with the request body, so the
    // list shows what the server stored: id, applied rate, amount and names included.
    onSuccess: () => client.invalidateQueries({ queryKey: ["time-entries"] }),
  });

  // (35) The obvious mistakes are caught next to the field; the server still validates.
  const error =
    !values.employeeId || !values.projectId
      ? "Выберите сотрудника и проект"
      : values.hours <= 0 || values.hours > 24 || values.hours % 0.5 !== 0
        ? "Часы: больше нуля, не больше 24, шаг 0,5"
        : null;

  return (
    <form
      onSubmit={(event) => {
        event.preventDefault();
        if (!error) create.mutate();
      }}
    >
      <label>
        Сотрудник
        <select
          value={values.employeeId}
          onChange={(e) => setValues({ ...values, employeeId: e.target.value })}
        >
          <option value="">Выберите</option>
          {employees.data?.map((employee) => (
            <option key={employee.id} value={employee.id}>
              {employee.name}
            </option>
          ))}
        </select>
      </label>

      {/* (36) The project comes from the catalogue, so the user cannot type a broken reference. */}
      <label>
        Проект
        <select
          value={values.projectId}
          onChange={(e) => setValues({ ...values, projectId: e.target.value })}
        >
          <option value="">Выберите</option>
          {projects.data?.map((project) => (
            <option key={project.id} value={project.id}>
              {project.code} · {project.name}
            </option>
          ))}
        </select>
      </label>

      <label>
        Дата
        <input
          type="date"
          value={values.date}
          onChange={(e) => setValues({ ...values, date: e.target.value })}
        />
      </label>

      {/* (28) Hours are a number in state and a number in JSON, never a string. */}
      <label>
        Часы
        <input
          type="number"
          step="0.5"
          value={values.hours}
          onChange={(e) =>
            setValues({ ...values, hours: Number(e.target.value) })
          }
        />
      </label>

      {error && <small role="alert">{error}</small>}
      {create.error && (
        <p role="alert">{(create.error as ApiError).message}</p>
      )}

      {/* (34) Disabled while the request is in flight: a double click cannot create duplicates. */}
      <button type="submit" disabled={create.isPending || error !== null}>
        {create.isPending ? "Сохраняем…" : "Добавить"}
      </button>
    </form>
  );
}
