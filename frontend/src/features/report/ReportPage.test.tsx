import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { beforeEach, describe, expect, it, vi } from "vitest";
import type { Report } from "../../api";
import { ReportPage } from "./ReportPage";

const { report } = vi.hoisted(() => ({ report: vi.fn() }));

vi.mock("../../api", async (importOriginal) => {
  const original = await importOriginal<typeof import("../../api")>();

  return { ...original, api: { ...original.api, report } };
});

const row = (over: Partial<Report["items"][number]> = {}) => ({
  projectId: "p001",
  projectCode: "П-001",
  projectName: "Реконструкция цеха",
  hours: 12,
  amount: 7600,
  budget: 20000,
  percent: 38,
  isAtRisk: false,
  isOverspent: false,
  ...over,
});

const renderPage = () => {
  const client = new QueryClient({
    defaultOptions: { queries: { retry: false } },
  });

  render(
    <QueryClientProvider client={client}>
      <ReportPage />
    </QueryClientProvider>,
  );
};

beforeEach(() => {
  vi.clearAllMocks();
  report.mockResolvedValue({
    items: [row()],
    totalHours: 12,
    totalAmount: 7600,
  });
});

describe("ReportPage", () => {
  it("shows the acceptance numbers of the month", async () => {
    report.mockResolvedValue({
      items: [
        row(),
        row({
          projectId: "p002",
          projectCode: "П-002",
          projectName: "Инженерные сети",
          hours: 10,
          amount: 7000,
          budget: 5000,
          percent: 140,
          isOverspent: true,
        }),
      ],
      totalHours: 22,
      totalAmount: 14600,
    });

    renderPage();

    expect(await screen.findByText("38%")).toBeInTheDocument();
    expect(screen.getByText("140%")).toBeInTheDocument();
    expect(screen.getAllByText("22 ч").length).toBeGreaterThan(0);
  });

  it("marks an overspent project apart from a healthy one", async () => {
    report.mockResolvedValue({
      items: [
        row(),
        row({
          projectId: "p002",
          projectCode: "П-002",
          percent: 140,
          isOverspent: true,
        }),
      ],
      totalHours: 22,
      totalAmount: 14600,
    });

    renderPage();

    const bars = await screen.findAllByRole("img");
    expect(bars[0].firstElementChild).not.toHaveClass("danger");
    expect(bars[1].firstElementChild).toHaveClass("danger");
  });

  it("shows a dash instead of a percentage when there is no budget", async () => {
    report.mockResolvedValue({
      items: [row({ budget: 0, percent: null })],
      totalHours: 12,
      totalAmount: 7600,
    });

    renderPage();

    expect(await screen.findByText("—")).toBeInTheDocument();
    expect(screen.getByRole("img")).toHaveAttribute(
      "aria-label",
      "Освоено неизвестно бюджета",
    );
  });

  it("reloads when the period changes", async () => {
    renderPage();

    await screen.findByText("38%");
    expect(report).toHaveBeenLastCalledWith(2026, 3);

    const period = screen.getByLabelText("Период");
    await userEvent.clear(period);
    await userEvent.type(period, "2026-02");

    await waitFor(() => expect(report).toHaveBeenLastCalledWith(2026, 2));
  });

  it("reports an empty month instead of an empty panel", async () => {
    report.mockResolvedValue({ items: [], totalHours: 0, totalAmount: 0 });

    renderPage();

    expect(
      await screen.findByText("За выбранный период данных нет"),
    ).toBeInTheDocument();
  });
});
