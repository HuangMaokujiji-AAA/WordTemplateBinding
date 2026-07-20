import { describe, it, expect } from "vitest";
import { normalizeChartNumber, formatChartValue } from "../features/docx/chart-recognition/utils/numberUtils";

describe("normalizeChartNumber", () => {
  it("eliminates floating-point noise: 9.1999999999999993 → 9.2", () => {
    expect(normalizeChartNumber("9.1999999999999993")).toBe(9.2);
  });

  it("eliminates floating-point noise: 32.700000000000003 → 32.7", () => {
    expect(normalizeChartNumber("32.700000000000003")).toBe(32.7);
  });

  it("passes through clean integers", () => {
    expect(normalizeChartNumber("543")).toBe(543);
    expect(normalizeChartNumber("0")).toBe(0);
  });

  it("returns null for invalid input", () => {
    expect(normalizeChartNumber("")).toBeNull();
    expect(normalizeChartNumber("abc")).toBeNull();
    expect(normalizeChartNumber("NaN")).toBeNull();
  });

  it("handles negative numbers", () => {
    expect(normalizeChartNumber("-5")).toBe(-5);
    expect(normalizeChartNumber("-3.14")).toBe(-3.14);
  });

  it("handles decimal numbers correctly", () => {
    expect(normalizeChartNumber("0.5")).toBe(0.5);
    expect(normalizeChartNumber("100.0")).toBe(100);
  });
});

describe("formatChartValue", () => {
  it("shows integers as whole numbers", () => {
    expect(formatChartValue(543, 1)).toBe("543");
    expect(formatChartValue(0, 1)).toBe("0");
  });

  it("shows decimals with given precision", () => {
    expect(formatChartValue(9.2, 1)).toBe("9.2");
    expect(formatChartValue(32.7, 1)).toBe("32.7");
  });
});
