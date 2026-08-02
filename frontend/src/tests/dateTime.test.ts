import { describe, expect, it } from "vitest";
import { formatDateTime } from "../shared/utils/dateTime";

describe("formatDateTime", () => {
  it("formats an ISO timestamp as local date and time without milliseconds", () => {
    const local = new Date(2026, 7, 1, 3, 21, 47);
    expect(formatDateTime(local.toISOString())).toBe("2026-08-01 03:21:47");
  });

  it("handles empty and invalid values", () => {
    expect(formatDateTime("")).toBe("-");
    expect(formatDateTime("not-a-date")).toBe("not-a-date");
  });
});
