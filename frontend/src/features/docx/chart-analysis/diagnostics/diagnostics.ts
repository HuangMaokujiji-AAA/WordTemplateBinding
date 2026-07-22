// Diagnostics collected while analyzing a single chart part. A single
// unresolved/unsupported detail must never throw — it is recorded here
// instead so the rest of the chart can still parse and render.

export type ChartDiagnosticLevel = "info" | "warning" | "error";

export interface ChartDiagnostic {
  code: string;
  level: ChartDiagnosticLevel;
  message: string;

  path?: string;
  seriesKey?: string;
  recoverable: boolean;
}

export type ChartDiagnosticModuleStatus = "complete" | "partial" | "missing";

export interface ChartDiagnosticModules {
  identity: ChartDiagnosticModuleStatus;
  data: ChartDiagnosticModuleStatus;
  axes: ChartDiagnosticModuleStatus;
  style: ChartDiagnosticModuleStatus;
  workbook: ChartDiagnosticModuleStatus;
}

export interface ChartDiagnostics {
  items: ChartDiagnostic[];

  hasErrors: boolean;
  hasWarnings: boolean;

  completenessScore: number;
  modules: ChartDiagnosticModules;
}

/** Mutable collector used while walking chart XML; produces an immutable ChartDiagnostics snapshot. */
export class ChartDiagnosticsCollector {
  private readonly items: ChartDiagnostic[] = [];

  add(diagnostic: ChartDiagnostic): void {
    this.items.push(diagnostic);
  }

  info(code: string, message: string, extra?: Partial<Pick<ChartDiagnostic, "path" | "seriesKey">>): void {
    this.add({ code, level: "info", message, recoverable: true, ...extra });
  }

  warn(code: string, message: string, extra?: Partial<Pick<ChartDiagnostic, "path" | "seriesKey">>): void {
    this.add({ code, level: "warning", message, recoverable: true, ...extra });
  }

  error(code: string, message: string, extra?: Partial<Pick<ChartDiagnostic, "path" | "seriesKey" | "recoverable">>): void {
    this.add({ code, level: "error", message, recoverable: extra?.recoverable ?? true, ...extra });
  }

  build(modules: ChartDiagnosticModules): ChartDiagnostics {
    const hasErrors = this.items.some((d) => d.level === "error");
    const hasWarnings = this.items.some((d) => d.level === "warning");
    return {
      items: [...this.items],
      hasErrors,
      hasWarnings,
      completenessScore: computeCompletenessScore(modules),
      modules,
    };
  }
}

const MODULE_WEIGHTS: Record<keyof ChartDiagnosticModules, number> = {
  identity: 15,
  data: 40,
  axes: 20,
  style: 10,
  workbook: 15,
};

const STATUS_FACTOR: Record<ChartDiagnosticModuleStatus, number> = {
  complete: 1,
  partial: 0.5,
  missing: 0,
};

/**
 * Rough, weighted completeness indicator (0-100) — a hint for the UI,
 * not a mathematically precise metric.
 */
function computeCompletenessScore(modules: ChartDiagnosticModules): number {
  let score = 0;
  for (const key of Object.keys(MODULE_WEIGHTS) as Array<keyof ChartDiagnosticModules>) {
    score += MODULE_WEIGHTS[key] * STATUS_FACTOR[modules[key]];
  }
  return Math.round(score);
}
