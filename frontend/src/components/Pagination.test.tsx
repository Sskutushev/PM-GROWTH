import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it, vi } from "vitest";
import { Pagination } from "./Pagination";

const setup = (page: number, totalCount: number) => {
  const onPage = vi.fn();
  const onPageSize = vi.fn();

  render(
    <Pagination
      page={page}
      pageSize={25}
      totalCount={totalCount}
      onPage={onPage}
      onPageSize={onPageSize}
    />,
  );

  return { onPage, onPageSize };
};

describe("Pagination", () => {
  it("shows the range of the current page", () => {
    setup(2, 60);

    expect(screen.getByText("26–50 из 60 записей")).toBeInTheDocument();
    expect(screen.getByText("Страница 2 из 3")).toBeInTheDocument();
  });

  it("disables the edges", () => {
    setup(1, 10);

    expect(screen.getByLabelText("Предыдущая страница")).toBeDisabled();
    expect(screen.getByLabelText("Следующая страница")).toBeDisabled();
  });

  it("asks for the next page", async () => {
    const { onPage } = setup(1, 60);

    await userEvent.click(screen.getByLabelText("Следующая страница"));

    expect(onPage).toHaveBeenCalledWith(2);
  });

  it("changes the page size", async () => {
    const { onPageSize } = setup(1, 60);

    await userEvent.selectOptions(screen.getByLabelText("На странице"), "50");

    expect(onPageSize).toHaveBeenCalledWith(50);
  });
});
