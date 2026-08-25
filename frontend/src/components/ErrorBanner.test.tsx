import { render, screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { ApiError } from "../api";
import { ErrorBanner } from "./ErrorBanner";

describe("ErrorBanner", () => {
  it("shows the server message and the hint for a known code", () => {
    render(
      <ErrorBanner error={new ApiError("PERIOD_CLOSED", "Период закрыт.")} />,
    );

    expect(screen.getByRole("alert")).toHaveTextContent("Период закрыт.");
    expect(screen.getByText(/Месяц закрыт/)).toBeInTheDocument();
  });

  it("keeps the server message when the code has no hint", () => {
    render(
      <ErrorBanner
        error={new ApiError("SOMETHING_NEW", "Неизвестная ошибка сервера.")}
      />,
    );

    const alert = screen.getByRole("alert");
    expect(alert).toHaveTextContent("Неизвестная ошибка сервера.");
    expect(alert.textContent).toBe("Неизвестная ошибка сервера.");
  });

  it("does not invent a reason for a non-API failure", () => {
    render(<ErrorBanner error={new Error("TypeError: fetch failed")} />);

    const alert = screen.getByRole("alert");
    expect(alert).toHaveTextContent("Не удалось выполнить запрос");
    expect(alert).not.toHaveTextContent("TypeError");
  });
});
