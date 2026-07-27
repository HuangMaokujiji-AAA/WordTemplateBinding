export interface RadarScaleResult {
  min: number;
  max: number;
  warnings: string[];
}

/** Resolves one shared, readable range for every radar indicator. */
export function resolveRadarScale(
  values: Array<number | null>,
  explicitMin?: number,
  explicitMax?: number
): RadarScaleResult {
  const warnings: string[] = [];
  const finiteValues = values.filter(
    (value): value is number => value != null && Number.isFinite(value)
  );

  let min = Number.isFinite(explicitMin) ? explicitMin : undefined;
  let max = Number.isFinite(explicitMax) ? explicitMax : undefined;
  if (min != null && max != null && max <= min) {
    warnings.push(`显式轴范围无效（${min}–${max}），已根据数据重新推导`);
    min = undefined;
    max = undefined;
  }

  if (finiteValues.length === 0) {
    warnings.push("没有有效数值，使用安全范围 0–100");
    if (min != null && max != null && max > min) return { min, max, warnings };
    return { min: min ?? 0, max: max ?? 100, warnings };
  }

  const dataMin = Math.min(...finiteValues);
  const dataMax = Math.max(...finiteValues);
  min ??= dataMin < 0 ? -niceCeiling(Math.abs(dataMin)) : 0;
  max ??= niceCeiling(dataMax > min ? dataMax : min + 1);

  if (max <= min) {
    const padding = niceCeiling(Math.max(Math.abs(min) * 0.1, 1));
    max = min + padding;
  }

  return { min, max, warnings };
}

function niceCeiling(value: number): number {
  if (!Number.isFinite(value) || value <= 0) return 0;
  const magnitude = 10 ** Math.floor(Math.log10(value));
  const normalized = value / magnitude;
  const nice = normalized <= 1 ? 1 : normalized <= 2 ? 2 : normalized <= 6 ? 6 : 10;
  return nice * magnitude;
}
