import { describe, expect, it } from "vitest";
import { entriesLabel, formatDate, money, number, plural } from "./format";

describe("formatting", () => {
  it("formats money in roubles", () => {
    expect(money(7600).replace(/ /g, " ")).toBe("7 600,00 ₽");
  });

  it("keeps halves in hours", () => {
    expect(number(7.5)).toBe("7,5");
  });

  it("reads a plain date as a local day", () => {
    expect(formatDate("2026-03-05")).toContain("05");
  });

  it("picks the Russian plural form", () => {
    const forms: [string, string, string] = ["запись", "записи", "записей"];

    expect(plural(1, forms)).toBe("запись");
    expect(plural(3, forms)).toBe("записи");
    expect(plural(5, forms)).toBe("записей");
    expect(plural(11, forms)).toBe("записей");
    expect(plural(21, forms)).toBe("запись");
  });

  it("labels a count", () => {
    expect(entriesLabel(2)).toBe("2 записи");
  });
});
