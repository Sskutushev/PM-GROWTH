import { entriesLabel } from "../lib/format";

const sizes = [10, 25, 50, 100];

export function Pagination({
  page,
  pageSize,
  totalCount,
  onPage,
  onPageSize,
}: {
  page: number;
  pageSize: number;
  totalCount: number;
  onPage: (page: number) => void;
  onPageSize: (pageSize: number) => void;
}) {
  const totalPages = Math.max(1, Math.ceil(totalCount / pageSize));
  const from = totalCount === 0 ? 0 : (page - 1) * pageSize + 1;
  const to = Math.min(page * pageSize, totalCount);

  return (
    <nav className="pagination" aria-label="Постраничная навигация">
      <span className="muted">
        {from}–{to} из {entriesLabel(totalCount)}
      </span>
      <div className="pager">
        <button
          onClick={() => onPage(page - 1)}
          disabled={page <= 1}
          aria-label="Предыдущая страница"
        >
          ←
        </button>
        <span aria-live="polite">
          Страница {page} из {totalPages}
        </span>
        <button
          onClick={() => onPage(page + 1)}
          disabled={page >= totalPages}
          aria-label="Следующая страница"
        >
          →
        </button>
      </div>
      <label className="page-size">
        На странице
        <select
          value={pageSize}
          onChange={(e) => onPageSize(Number(e.target.value))}
        >
          {sizes.map((size) => (
            <option key={size} value={size}>
              {size}
            </option>
          ))}
        </select>
      </label>
    </nav>
  );
}
