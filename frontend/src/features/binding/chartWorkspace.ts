import type { ChartItem } from "../../api/types";
import type { ParsedWordChart } from "../docx/chart-analysis/models/types";

export interface ChartWorkspaceItem {
  locatorId: string | null;

  parsed: ParsedWordChart;
  backend: ChartItem | null;

  isBound: boolean;
  boundDataPath: string | null;

  canPreview: boolean;
  canBind: boolean;

  mergeWarnings: string[];
}

function normalizePartKey(path: string): string {
  const normalized = path.replace(/\\/g, "/");
  return normalized.startsWith("/") ? normalized : `/${normalized}`;
}

/**
 * Joins the frontend's own deep chart analysis (ParsedWordChart[], produced
 * independently by chart-analysis from the DOCX the user just uploaded)
 * with the backend's authoritative binding metadata (ChartItem[], from
 * TemplateResponse). The backend Locator remains the only identifier used
 * to save bindings or generate the final DOCX — this merge only builds a
 * combined view model for the UI.
 *
 * Match priority:
 *   1. normalize(partKey) + relationshipId
 *   2. normalize(partKey)
 *   3. documentOrder
 */
export function buildChartWorkspace(
  parsedCharts: ParsedWordChart[],
  backendCharts: ChartItem[]
): ChartWorkspaceItem[] {
  const usedBackendIds = new Set<string>();

  const byPartKeyAndRel = new Map<string, ChartItem>();
  const byPartKey = new Map<string, ChartItem[]>();
  const byDocumentOrder = new Map<number, ChartItem>();

  for (const backend of backendCharts) {
    const partKey = normalizePartKey(backend.locator.partKey);
    byPartKeyAndRel.set(`${partKey}::${backend.locator.relationshipId}`, backend);
    const list = byPartKey.get(partKey) ?? [];
    list.push(backend);
    byPartKey.set(partKey, list);
    byDocumentOrder.set(backend.locator.documentOrder, backend);
  }

  return parsedCharts.map((parsed) => {
    const partKey = normalizePartKey(parsed.source.chartPartPath);
    const warnings: string[] = [];

    let backend: ChartItem | null =
      byPartKeyAndRel.get(`${partKey}::${parsed.identity.relationshipId}`) ?? null;

    if (!backend) {
      const candidates = (byPartKey.get(partKey) ?? []).filter(
        (c) => !usedBackendIds.has(c.locatorId)
      );
      if (candidates.length === 1) {
        backend = candidates[0];
        warnings.push("按 partKey 匹配（relationshipId 不一致）");
      } else if (candidates.length > 1) {
        warnings.push("多个后端图表共享同一 partKey，无法唯一匹配");
      }
    }

    if (!backend) {
      const byOrder = byDocumentOrder.get(parsed.identity.documentOrder);
      if (byOrder && !usedBackendIds.has(byOrder.locatorId)) {
        backend = byOrder;
        warnings.push("按 documentOrder 匹配（partKey 不一致）");
      }
    }

    if (backend) {
      usedBackendIds.add(backend.locatorId);
    } else {
      warnings.push("未能匹配到后端图表，绑定信息不可用");
    }

    return {
      locatorId: backend?.locatorId ?? null,
      parsed,
      backend,
      isBound: backend?.isBound ?? false,
      boundDataPath: backend?.boundDataPath ?? null,
      canPreview: parsed.supportedForPreview,
      canBind: backend?.isBindable ?? false,
      mergeWarnings: warnings,
    };
  });
}
