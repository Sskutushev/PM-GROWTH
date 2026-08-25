import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { ApiError, type Entry, type Page } from "../../api";
import { EntriesPage } from "./EntriesPage";

const { entries, remove } = vi.hoisted(() => ({
  entries: vi.fn(),
  remove: vi.fn(),
}));

vi.mock("../../api", async (importOriginal) => ({
  ...(await importOriginal<typeof import("../../api")>()),
  api: {
    entries,
    remove,
    employees: () =>
      Promise.resolve([{ id: "ivanov", code: "", name: "Иванов И. И." }]),
    projects: () =>
      Promise.resolve([
        { id: "p001", code: "П-001", name: "Реконструкция цеха" },
      ]),
    report: () => Promise.resolve({ items: [], totalHours: 0, totalAmount: 0 }),
    save: () => Promise.resolve({} as Entry),
  },
}));

const entry = (id: string, date: string): Entry => ({
  id,
  employeeId: "ivanov",
  employeeName: "Иванов И. И.",
  projectId: "p001",
  projectCode: "П-001",
  date,
  hours: 8,
  appliedRate: 600,
  amount: 4800,
  comment: "",
  isOvertime: false,
  dailyHours: 8,
  version: 3,
});

const page = (items: Entry[], totalCount: number): Page => ({
  items,
  page: 1,
  pageSize: 25,
  totalCount,
  totalHours: 8 * items.length,
  totalAmount: 4800 * items.length,
});

const renderPage = () => {
  const client = new QueryClient({
    defaultOptions: { queries: { retry: false } },
  });

  return render(
    <QueryClientProvider client={client}>
      <EntriesPage />
    </QueryClientProvider>,
  );
};

// The employee also appears in the filter dropdown, so a row is looked for inside the table.
const findRow = () =>
  within(screen.getByRole("table")).findByText("Иванов И. И.");

beforeEach(() => {
  vi.clearAllMocks();
  entries.mockResolvedValue(page([entry("e1", "2026-03-05")], 1));
  remove.mockResolvedValue(undefined);
});

describe("EntriesPage", () => {
  it("shows the rows the API returned", async () => {
    renderPage();

    expect(await findRow()).toBeInTheDocument();
    expect(
      within(screen.getByRole("table")).getByText("П-001"),
    ).toBeInTheDocument();
  });

  it("asks for the selected page and page size", async () => {
    entries.mockResolvedValue(page([entry("e1", "2026-03-05")], 60));

    renderPage();

    await findRow();
    await userEvent.click(screen.getByLabelText("Следующая страница"));

    await waitFor(() =>
      expect(entries).toHaveBeenLastCalledWith(
        expect.objectContaining({ page: 2, pageSize: 25 }),
      ),
    );

    await userEvent.selectOptions(screen.getByLabelText("На странице"), "50");

    // A new page size renumbers the pages, so the list goes back to the first one.
    await waitFor(() =>
      expect(entries).toHaveBeenLastCalledWith(
        expect.objectContaining({ page: 1, pageSize: 50 }),
      ),
    );
  });

  it("deletes a row through a confirmation dialog", async () => {
    renderPage();

    await findRow();
    await userEvent.click(
      screen.getByLabelText(/^Удалить запись от/, { selector: "button" }),
    );

    const dialog = screen.getByRole("dialog");
    expect(within(dialog).getByText("Удалить запись?")).toBeInTheDocument();

    await userEvent.click(
      within(dialog).getByRole("button", { name: "Удалить" }),
    );

    // The version travels with the delete: the API rejects a stale one.
    await waitFor(() => expect(remove).toHaveBeenCalledWith("e1", 3));
  });

  it("keeps the row when the dialog is dismissed", async () => {
    renderPage();

    await findRow();
    await userEvent.click(
      screen.getByLabelText(/^Удалить запись от/, { selector: "button" }),
    );
    await userEvent.click(screen.getByRole("button", { name: "Отмена" }));

    expect(remove).not.toHaveBeenCalled();
    expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
  });

  it("reports an empty month instead of an empty table", async () => {
    entries.mockResolvedValue(page([], 0));

    renderPage();

    expect(
      await screen.findByText("За выбранный период записей нет"),
    ).toBeInTheDocument();
  });

  it("sends a chosen filter and goes back to the first page", async () => {
    entries.mockResolvedValue(page([entry("e1", "2026-03-05")], 60));

    renderPage();

    await findRow();
    await userEvent.click(screen.getByLabelText("Следующая страница"));
    await waitFor(() =>
      expect(entries).toHaveBeenLastCalledWith(
        expect.objectContaining({ page: 2 }),
      ),
    );

    await userEvent.selectOptions(screen.getByLabelText("Проект"), "p001");

    await waitFor(() =>
      expect(entries).toHaveBeenLastCalledWith(
        expect.objectContaining({ page: 1, projectId: "p001" }),
      ),
    );
  });

  it("offers to clear the filters and clears them", async () => {
    renderPage();

    await findRow();
    expect(screen.queryByText("Сбросить фильтры")).not.toBeInTheDocument();

    await userEvent.selectOptions(screen.getByLabelText("Сотрудник"), "ivanov");
    await userEvent.click(screen.getByText("Сбросить фильтры"));

    await waitFor(() =>
      expect(entries).toHaveBeenLastCalledWith(
        expect.objectContaining({ employeeId: "", projectId: "" }),
      ),
    );
  });

  it("tells an over-filtered month apart from an empty one", async () => {
    entries.mockResolvedValue(page([], 0));

    renderPage();

    await screen.findByText("За выбранный период записей нет");

    await userEvent.selectOptions(screen.getByLabelText("Сотрудник"), "ivanov");

    expect(
      await screen.findByText("Под выбранные фильтры записей нет"),
    ).toBeInTheDocument();
  });

  it("shows the failure and lets the user retry it", async () => {
    entries.mockRejectedValueOnce(
      new ApiError("REQUEST_FAILED", "База недоступна."),
    );
    entries.mockResolvedValue(page([entry("e1", "2026-03-05")], 1));

    renderPage();

    expect(await screen.findByText("База недоступна.")).toBeInTheDocument();

    await userEvent.click(screen.getByText("Повторить запрос"));

    expect(await findRow()).toBeInTheDocument();
  });
});
