import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it, vi } from "vitest";
import { Modal } from "./Modal";

const open = (onClose = vi.fn()) => {
  render(
    <Modal eyebrow="Раздел" title="Заголовок" onClose={onClose}>
      <input aria-label="Поле" />
      <button>Действие</button>
    </Modal>,
  );

  return onClose;
};

describe("Modal", () => {
  it("is announced as a dialog with its title", () => {
    open();

    expect(screen.getByRole("dialog")).toHaveAttribute(
      "aria-label",
      "Заголовок",
    );
  });

  it("moves focus into the dialog", () => {
    open();

    expect(screen.getByLabelText("Поле")).toHaveFocus();
  });

  it("closes on Escape", async () => {
    const onClose = open();

    await userEvent.keyboard("{Escape}");

    expect(onClose).toHaveBeenCalled();
  });

  it("closes on the close button but not on a click inside", async () => {
    const onClose = open();

    await userEvent.click(screen.getByRole("button", { name: "Действие" }));
    expect(onClose).not.toHaveBeenCalled();

    await userEvent.click(screen.getByLabelText("Закрыть"));
    expect(onClose).toHaveBeenCalledTimes(1);
  });

  it("locks the page behind it and gives scrolling back on close", () => {
    const { unmount } = render(
      <Modal eyebrow="Раздел" title="Заголовок" onClose={vi.fn()}>
        <input aria-label="Поле" />
      </Modal>,
    );

    expect(document.body.style.overflow).toBe("hidden");

    unmount();
    expect(document.body.style.overflow).not.toBe("hidden");
  });
});
