import { useState } from "react";
import { keepPreviousData, useQuery } from "@tanstack/react-query";
import { api, type Entry } from "../../api";
import { Header } from "../../components/Header";
import { Metric } from "../../components/Metric";
import { Pagination } from "../../components/Pagination";
import { SelectFilter } from "../../components/SelectFilter";
import { ErrorBanner } from "../../components/ErrorBanner";
import { entriesLabel, formatDate, money, number } from "../../lib/format";
import { EntryModal } from "./EntryModal";
import { DeleteEntryDialog } from "./DeleteEntryDialog";
import { currentMonth } from "./month";

export function EntriesPage() {
  const [month, setMonth] = useState(currentMonth);
  const [employeeId, setEmployee] = useState("");
  const [projectId, setProject] = useState("");
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(25);
  const [editing, setEditing] = useState<Entry | "new" | null>(null);
  const [removing, setRemoving] = useState<Entry | null>(null);

  const [year, monthNumber] = month.split("-").map(Number);

  const query = useQuery({
    queryKey: ["entries", month, employeeId, projectId, page, pageSize],
    queryFn: () =>
      api.entries({
        year,
        month: monthNumber,
        employeeId,
        projectId,
        page,
        pageSize,
      }),
    // Without this the table empties out on every page change, and the row count it is
    // clamped against would briefly read zero.
    placeholderData: keepPreviousData,
  });

  const employees = useQuery({
    queryKey: ["employees"],
    queryFn: api.employees,
  });
  const projects = useQuery({ queryKey: ["projects"], queryFn: api.projects });

  const totalCount = query.data?.totalCount ?? 0;
  const totalPages = Math.max(1, Math.ceil(totalCount / pageSize));

  // Removing the last row of a page would leave the user staring at an empty one.
  const stepBackIfPageEmpties = () => {
    if (page > 1 && query.data?.items.length === 1) setPage(page - 1);
  };

  // Every filter narrows the result set, so the page the user was on rarely exists any more.
  const filter = <T,>(set: (value: T) => void) => {
    return (value: T) => {
      set(value);
      setPage(1);
    };
  };

  return (
    <>
      <Header
        eyebrow="Учёт времени"
        title="Табель"
        action={
          <button className="primary" onClick={() => setEditing("new")}>
            <span aria-hidden="true">＋</span> Добавить запись
          </button>
        }
      />
      <section className="filters">
        <label>
          Месяц
          <input
            type="month"
            value={month}
            onChange={(e) => filter(setMonth)(e.target.value)}
          />
        </label>
        <SelectFilter
          label="Сотрудник"
          value={employeeId}
          onChange={filter(setEmployee)}
          options={employees.data ?? []}
          all="Все сотрудники"
        />
        <SelectFilter
          label="Проект"
          value={projectId}
          onChange={filter(setProject)}
          options={projects.data ?? []}
          all="Все проекты"
        />
      </section>
      <div className="stats">
        <Metric
          label="Всего часов"
          value={number(query.data?.totalHours ?? 0)}
          hint="за месяц с учётом фильтров"
          loading={query.isLoading}
        />
        <Metric
          label="Стоимость работ"
          value={money(query.data?.totalAmount ?? 0)}
          hint="сумма по всем страницам"
          loading={query.isLoading}
        />
        <Metric
          label="Записей"
          value={String(totalCount)}
          hint={`страница ${page} из ${totalPages}`}
          loading={query.isLoading}
        />
      </div>
      <section className="panel">
        <div className="panel-title">
          <h2>Записи за месяц</h2>
          <span>
            {query.isFetching ? "Обновляем…" : entriesLabel(totalCount)}
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
                <th>
                  <span className="sr-only">Действия</span>
                </th>
              </tr>
            </thead>
            <tbody>
              {query.data?.items.map((entry, index) => (
                <tr
                  key={entry.id}
                  className="row-in"
                  style={{ animationDelay: `${Math.min(index, 12) * 25}ms` }}
                >
                  <td>{formatDate(entry.date)}</td>
                  <td>
                    <strong>{entry.employeeName}</strong>
                  </td>
                  <td>
                    <span className="code">{entry.projectCode}</span>
                  </td>
                  <td>
                    {number(entry.hours)}{" "}
                    {entry.isOvertime && (
                      <span
                        className="overtime"
                        title={`За день ${number(entry.dailyHours)} ч`}
                      >
                        Переработка
                      </span>
                    )}
                  </td>
                  <td>{money(entry.appliedRate)}</td>
                  <td>
                    <strong>{money(entry.amount)}</strong>
                  </td>
                  <td className="muted">{entry.comment || "—"}</td>
                  <td className="row-actions">
                    <button
                      className="icon"
                      aria-label={`Редактировать запись от ${formatDate(entry.date)}`}
                      onClick={() => setEditing(entry)}
                    >
                      ✎
                    </button>
                    <button
                      className="icon danger"
                      aria-label={`Удалить запись от ${formatDate(entry.date)}`}
                      onClick={() => setRemoving(entry)}
                    >
                      ✕
                    </button>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
        {query.isLoading && <div className="empty">Загружаем записи…</div>}
        {!query.isLoading && totalCount === 0 && (
          <div className="empty">За выбранный период записей нет</div>
        )}
        {totalCount > 0 && (
          <Pagination
            page={page}
            pageSize={pageSize}
            totalCount={totalCount}
            onPage={setPage}
            onPageSize={filter(setPageSize)}
          />
        )}
      </section>
      {editing && (
        <EntryModal
          entry={editing === "new" ? null : editing}
          employees={employees.data ?? []}
          projects={projects.data ?? []}
          month={month}
          onClose={() => setEditing(null)}
        />
      )}
      {removing && (
        <DeleteEntryDialog
          entry={removing}
          onDeleted={stepBackIfPageEmpties}
          onClose={() => setRemoving(null)}
        />
      )}
    </>
  );
}
