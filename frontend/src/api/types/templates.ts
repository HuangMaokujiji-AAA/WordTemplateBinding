import type { ChartItem, MockItem } from "./chart";

export interface PreviewHighlight {
  locatorId: string;
  startOffset: number;
  length: number;
  mockValue: string;
}

export interface PreviewParagraph {
  paragraphIndex: number;
  text: string;
  highlights: PreviewHighlight[];
}

export interface TemplateResponse {
  templateId: string;
  fileName: string;
  contentHash: string;
  mockItemCount: number;
  chartCount: number;
  bindingCount: number;
  mockItems: MockItem[];
  charts: ChartItem[];
  preview: { paragraphs: PreviewParagraph[] };
  importSummary: TemplateImportSummary;
  createdAt: string;
  updatedAt: string;
}

export interface TemplateImportSummary {
  textBindingsRestored: number;
  chartBindingsRestored: number;
  unresolvedPlaceholders: string[];
  warnings: string[];
}

export interface TemplateRecord {
  id: string;
  templateCode: string;
  templateName: string;
  templateType: string;
  categoryCode: string | null;
  templateStatus: string;
  description: string | null;
  currentVersionNo: number;
  createdAt: string;
  updatedAt: string;
}

export interface TemplateElementRecord {
  id: string;
  templateVersionId: string;
  segmentId: string | null;
  elementKey: string;
  elementType: "TEXT" | "CHART" | string;
  locatorType: string;
  displayName: string | null;
  locator: Record<string, unknown> & { locatorId?: string };
  bindingSchema: unknown;
  defaultValue: unknown;
  isRequired: boolean;
  sortNo: number;
  segmentLocalOrder: number;
  parseStatus: string;
  parseMessage: string | null;
}

export interface TemplateSegmentRecord {
  id: string;
  templateVersionId: string;
  parentSegmentId: string | null;
  segmentKey: string;
  segmentName: string;
  segmentType: string;
  anchorType: string;
  documentOrderStart: number;
  documentOrderEnd: number;
  segmentStatus: string;
  previewStatus: string;
  previewErrorMessage: string | null;
  sortNo: number;
  elementCount: number;
  bindingProgress: {
    total: number;
    bound: number;
    requiredMissing: number;
  };
  rowVersion: number;
}

export interface TemplateOutlineBlock {
  blockId: string;
  blockType: string;
  displayText: string;
  segmentKey: string | null;
  canSelect: boolean;
  depth: number;
  children: TemplateOutlineBlock[];
}

export interface TemplateSegmentOutline {
  templateVersionId: string;
  contentHash: string;
  blocks: TemplateOutlineBlock[];
}

export interface TemplateVersionView {
  template: TemplateRecord;
  version: {
    id: string;
    templateId: string;
    versionNo: number;
    fileObjectId: string;
    versionStatus: string;
    elementCount: number;
    createdAt: string;
  };
  file: {
    id: string;
    originalName: string;
    mimeType: string;
    fileSize: number;
    sha256: string | null;
    objectStatus: string;
  };
  elements: TemplateElementRecord[];
  parseResult: {
    scanResult: {
      contentHash: string;
      mockItems: MockItem[];
      charts: ChartItem[];
      preview: { paragraphs: PreviewParagraph[] };
      warnings?: Array<{ code: string; message: string }>;
    };
    importSummary: TemplateImportSummary;
    warnings: Array<{ code: string; message: string }>;
  };
}

export interface TemplateVersionRecord {
  id: string;
  templateId: string;
  versionNo: number;
  fileObjectId: string;
  versionStatus: string;
  parserName?: string | null;
  parserVersion?: string | null;
  elementCount: number;
  styleFingerprint?: string | null;
  createdAt: string;
}

export interface TemplateStudioSummary {
  segmentCount: number;
  elementCount: number;
  validElementCount: number;
  warningElementCount: number;
  unsupportedElementCount: number;
  chartCount: number;
  boundElementCount: number;
  requiredMissingCount: number;
}

export interface TemplateStudioWorkspace {
  versionView: TemplateVersionView;
  segments: TemplateSegmentRecord[];
  outline: TemplateSegmentOutline;
  summary: TemplateStudioSummary;
}

export interface TemplateSegmentBoundaryDraft {
  segmentKey: string;
  segmentName: string;
  startBlockId: string;
  endBlockId: string;
}

export interface PublishedTemplateList {
  items: Array<{
    releaseId: string;
    templateId: string;
    templateVersionId: string;
    versionLabel: string;
    publishedAt: string;
  }>;
  publishingAvailable: boolean;
  message: string;
}
