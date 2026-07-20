/**
 * Parse an sRGB color string (e.g. "4472C4") into CSS hex format ("#4472C4").
 */
export function parseSrgbColor(val: string): string | null {
  const hex = val.trim();
  if (/^[0-9A-Fa-f]{6}$/.test(hex)) {
    return `#${hex}`;
  }
  return null;
}

/**
 * Theme color scheme → sRGB hex mapping.
 *
 * The actual sRGB values are read from word/theme/theme1.xml at runtime.
 * These are fallback defaults used when the theme file is absent.
 */
export const DEFAULT_THEME_COLORS: Record<string, string> = {
  dk1: "#000000",
  lt1: "#FFFFFF",
  dk2: "#44546A",
  lt2: "#E7E6E6",
  accent1: "#4472C4",
  accent2: "#ED7D31",
  accent3: "#A5A5A5",
  accent4: "#FFC000",
  accent5: "#5B9BD5",
  accent6: "#70AD47",
};

export const DEFAULT_OFFICE_PALETTE = [
  "#4472C4",
  "#ED7D31",
  "#A5A5A5",
  "#FFC000",
  "#5B9BD5",
  "#70AD47",
];

/**
 * Resolve a theme color name (e.g. "accent1") to its sRGB hex value.
 * Returns null if the theme name is not recognized.
 */
export function resolveThemeColor(
  schemeVal: string,
  themeColors?: Record<string, string>
): string | null {
  const merged = { ...DEFAULT_THEME_COLORS, ...themeColors };
  const key = schemeVal.trim().toLowerCase();
  return merged[key] ?? null;
}
