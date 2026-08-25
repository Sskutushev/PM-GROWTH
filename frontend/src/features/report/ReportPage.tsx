import { useMemo, useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { api, type Report } from "../../api";
import { Header } from "../../components/Header";
import { Metric } from "../../components/Metric";
import { ErrorBanner } from "../../components/ErrorBanner";
import { money, number } from "../../lib/format";
import { currentMonth } from "../entries/month";

export function ReportPage() {
  const [month, setMonth] = useState(currentMonth);
  const [year, monthNumber] = month.split("-").map(Number);

  const query = useQuery({
    queryKey: ["report", month],
    queryFn: () => api.report(year, monthNumber),
  });

  // Bars are scaled against the largest percent on the screen, never below 100, so a project
  // at 38% does not look full just because it leads the month.
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
          value={`${number(query.data?.totalHours ?? 0)} ч`}
          loading={query.isLoading}
        />
        <Metric
          label="Стоимость"
          value={money(query.data?.totalAmount ?? 0)}
          loading={query.isLoading}
        />
        <Metric
          label="Проектов"
          value={String(query.data?.items.length ?? 0)}
          loading={query.isLoading}
        />
      </div>
      <section className="panel report">
        <div className="panel-title">
          <h2>Освоение бюджета</h2>
          <span>Порог риска — 80%</span>
        </div>
        {query.data?.items.map((row, index) => (
          <ReportLine
            key={row.projectId}
            row={row}
            max={max}
            delay={Math.min(index, 12) * 40}
          />
        ))}
        {!query.isLoading && query.data?.items.length === 0 && (
          <div className="empty">За выбранный период данных нет</div>
        )}
        <div className="report-total">
          <span>Итого</span>
          <strong>{number(query.data?.totalHours ?? 0)} ч</strong>
          <strong>{money(query.data?.totalAmount ?? 0)}</strong>
        </div>
      </section>
    </>
  );
}

function ReportLine({
  row,
  max,
  delay,
}: {
  row: Report["items"][number];
  max: number;
  delay: number;
}) {
  const width =
    row.percent == null ? 0 : Math.min((row.percent / max) * 100, 100);

  const state = row.isOverspent ? "danger" : row.isAtRisk ? "risk" : "";

  return (
    <div className="report-row">
      <div>
        <span className="code">{row.projectCode}</span>
        <strong>{row.projectName}</strong>
      </div>
      <div
        className="bar"
        role="img"
        aria-label={`Освоено ${row.percent == null ? "неизвестно" : `${number(row.percent)}%`} бюджета`}
      >
        <i
          className={state}
          style={{ width: `${width}%`, transitionDelay: `${delay}ms` }}
        />
      </div>
      <div className="num">
        <strong>{row.percent == null ? "—" : `${number(row.percent)}%`}</strong>
        <small>
          {money(row.amount)} из {money(row.budget)}
        </small>
      </div>
    </div>
  );
}
