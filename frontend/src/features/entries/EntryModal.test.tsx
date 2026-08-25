import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { ApiError, type Entry, type Lookup } from "../../api";
import { EntryModal } from "./EntryModal";

const { save } = vi.hoisted(() => ({ save: vi.fn() }));

vi.mock("../../api", async (importOriginal) => {
  const original = await importOriginal<typeof import("../../api")>();

  return { ...original, api: { ...original.api, save } };
});

const employees: Lookup[] = [{ id: "ivanov", code: "", name: "Иванов И. И." }];
const projects: Lookup[] = [
  { id: "p001", code: "П-001", name: "Реконструкция цеха" },
];

const entry: Entry = {
  id: "e1",
  employeeId: "ivanov",
  employeeName: "Иванов И. И.",
  projectId: "p001",
  projectCode: "П-001",
  date: "2026-03-05",
  hours: 8,
  appliedRate: 600,
  amount: 4800,
  comment: "монтаж",
  isOvertime: false,
  dailyHours: 8,
  version: 3,
};

const renderModal = (existing: Entry | null, onClose = vi.fn()) => {
  const client = new QueryClient({
    defaultOptions: { queries: { retry: false } },
  });

  render(
    <QueryClientProvider client={client}>
      <EntryModal
        entry={existing}
        employees={employees}
        projects={projects}
        month="2026-03"
        onClose={onClose}
      />
    </QueryClientProvider>,
  );

  return onClose;
};

beforeEach(() => {
  vi.clearAllMocks();
  save.mockResolvedValue(entry);
});

describe("EntryModal", () => {
  it("opens a new entry on the first day of the month being viewed", () => {
    renderModal(null);

    expect(screen.getByLabelText("Дата")).toHaveValue("2026-03-01");
    expect(screen.getByText("Новая запись")).toBeInTheDocument();
  });

  it("prefills the form when editing", () => {
    renderModal(entry);

    expect(screen.getByLabelText("Дата")).toHaveValue("2026-03-05");
    expect(screen.getByLabelText("Часы")).toHaveValue(8);
    expect(screen.getByText("Изменить запись")).toBeInTheDocument();
  });

  it("refuses half-hour violations before sending anything", async () => {
    renderModal(null);

    await userEvent.selectOptions(screen.getByLabelText("Сотрудник"), "ivanov");
    await userEvent.selectOptions(screen.getByLabelText("Проект"), "p001");
    await userEvent.clear(screen.getByLabelText("Часы"));
    await userEvent.type(screen.getByLabelText("Часы"), "3.7");
    await userEvent.click(screen.getByRole("button", { name: "Сохранить" }));

    expect(await screen.findByText("Шаг — 0,5 часа")).toBeInTheDocument();
    expect(save).not.toHaveBeenCalled();
  });

  it("says the 24-hour limit belongs to one entry", async () => {
    renderModal(null);

    await userEvent.selectOptions(screen.getByLabelText("Сотрудник"), "ivanov");
    await userEvent.selectOptions(screen.getByLabelText("Проект"), "p001");
    await userEvent.clear(screen.getByLabelText("Часы"));
    await userEvent.type(screen.getByLabelText("Часы"), "25");
    await userEvent.click(screen.getByRole("button", { name: "Сохранить" }));

    expect(
      await screen.findByText("В одной записи нельзя указать больше 24 часов"),
    ).toBeInTheDocument();
  });

  it("asks for the employee and the project", async () => {
    renderModal(null);

    await userEvent.click(screen.getByRole("button", { name: "Сохранить" }));

    expect(await screen.findByText("Выберите сотрудника")).toBeInTheDocument();
    expect(screen.getByText("Выберите проект")).toBeInTheDocument();
    expect(save).not.toHaveBeenCalled();
  });

  it("sends hours as a number and carries the version when editing", async () => {
    const onClose = renderModal(entry);

    await userEvent.clear(screen.getByLabelText("Часы"));
    await userEvent.type(screen.getByLabelText("Часы"), "6");
    await userEvent.click(screen.getByRole("button", { name: "Сохранить" }));

    await waitFor(() =>
      expect(save).toHaveBeenCalledWith("e1", {
        employeeId: "ivanov",
        projectId: "p001",
        date: "2026-03-05",
        hours: 6,
        comment: "монтаж",
        version: 3,
      }),
    );

    await waitFor(() => expect(onClose).toHaveBeenCalled());
  });

  it("keeps the form open and shows what the server refused", async () => {
    save.mockRejectedValue(
      new ApiError("PERIOD_CLOSED", "Период закрыт для изменений."),
    );

    const onClose = renderModal(entry);

    await userEvent.click(screen.getByRole("button", { name: "Сохранить" }));

    expect(
      await screen.findByText("Период закрыт для изменений."),
    ).toBeInTheDocument();
    expect(onClose).not.toHaveBeenCalled();
  });
});
