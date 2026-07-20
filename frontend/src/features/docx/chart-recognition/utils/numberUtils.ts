/**
 * Normalize a chart number string to eliminate floating-point noise.
 *
 * OOXML chart cache values sometimes contain artifacts like:
 *   9.1999999999999993
 *   32.700000000000003
 *
 * This function uses toPrecision(12) to round off the noise,
 * then returns a clean number. Returns null for non-finite input.
 */
export function normalizeChartNumber(raw: string): number | null {
  const trimmed = raw.trim();
  if (trimmed === "") {
    return null;
  }

  const parsed = Number(trimmed);
  if (!Number.isFinite(parsed)) {
    return null;
  }

  // toPrecision(12) eliminates IEEE-754 noise while preserving
  // meaningful digits for chart data (up to ~10 significant figures).
  return Number(parsed.toPrecision(12));
}

/**
 * Format a number for display, preferring integer format when the value
 * is close to an integer (within 1e-6).
 */
export function formatChartValue(value: number, decimals = 1): string {
  if (Math.abs(value - Math.round(value)) < 1e-6) {
    return String(Math.round(value));
  }
  return value.toFixed(decimals);
}
