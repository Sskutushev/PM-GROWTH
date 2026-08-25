export type Lookup = { id: string; code: string; name: string };
export type Entry = {
  id: string;
  employeeId: string;
  employeeName: string;
  projectId: string;
  projectCode: string;
  date: string;
  hours: number;
  appliedRate: number;
  amount: number;
  comment: string;
  isOvertime: boolean;
  dailyHours: number;
  version: number;
};
export type Page = {
  items: Entry[];
  page: number;
  pageSize: number;
  totalCount: number;
  totalHours: number;
  totalAmount: number;
};
export type Report = {
  items: {
    projectId: string;
    projectCode: string;
    projectName: string;
    hours: number;
    amount: number;
    budget: number;
    percent: number | null;
    isAtRisk: boolean;
    isOverspent: boolean;
  }[];
  totalHours: number;
  totalAmount: number;
};
export class ApiError extends Error {
  code: string;
  details: Record<string, unknown>;
  constructor(
    code: string,
    message: string,
    details: Record<string, unknown> = {},
  ) {
    super(message);
    this.code = code;
    this.details = details;
  }
}
const request = async <T>(path: string, init?: RequestInit): Promise<T> => {
  const response = await fetch(`/api${path}`, {
    ...init,
    headers: { "Content-Type": "application/json", ...init?.headers },
  });
  if (!response.ok) {
    const p = await response.json().catch(() => ({}));
    throw new ApiError(
      p.code ?? "REQUEST_FAILED",
      p.title ?? "Не удалось выполнить запрос.",
      p.details,
    );
  }
  if (response.status === 204) return undefined as T;
  return response.json();
};
export const api = {
  entries: (
    year: number,
    month: number,
    employeeId: string,
    projectId: string,
  ) =>
    request<Page>(
      `/time-entries?${new URLSearchParams({ year: String(year), month: String(month), page: "1", pageSize: "50", ...(employeeId ? { employeeId } : {}), ...(projectId ? { projectId } : {}) })}`,
    ),
  employees: () => request<Lookup[]>("/employees"),
  projects: () => request<Lookup[]>("/projects"),
  report: (year: number, month: number) =>
    request<Report>(`/reports/projects?year=${year}&month=${month}`),
  save: (id: string | undefined, body: object) =>
    request<Entry>(id ? `/time-entries/${id}` : "/time-entries", {
      method: id ? "POST" : "PUT",
      body: JSON.stringify(body),
    }),
};
