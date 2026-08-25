import { afterEach, describe, expect, it, vi } from "vitest";
import { api, ApiError } from "./api";
afterEach(() => vi.restoreAllMocks());
describe("typed API client", () => {
  it("parses ProblemDetails into ApiError", async () => {
    vi.stubGlobal(
      "fetch",
      vi.fn().mockResolvedValue(
        new Response(
          JSON.stringify({
            code: "PERIOD_CLOSED",
            title: "Период закрыт",
            details: { month: 2 },
          }),
          {
            status: 409,
            headers: { "Content-Type": "application/problem+json" },
          },
        ),
      ),
    );
    const error = await api.report(2026, 3).catch((x) => x);
    expect(error).toBeInstanceOf(ApiError);
    expect(error.code).toBe("PERIOD_CLOSED");
    expect(error.details).toEqual({ month: 2 });
  });
  it("keeps month filters in the query", async () => {
    const fetch = vi
      .fn()
      .mockResolvedValue(
        new Response(
          JSON.stringify({ items: [], totalHours: 0, totalAmount: 0 }),
          { status: 200 },
        ),
      );
    vi.stubGlobal("fetch", fetch);
    await api.report(2026, 3);
    expect(fetch.mock.calls[0][0]).toContain("year=2026&month=3");
  });
});
