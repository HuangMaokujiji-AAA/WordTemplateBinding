import { describe, expect, it } from "vitest";
import { formatImportSummary } from "../features/binding/importSummaryStatus";

describe("formatImportSummary", () => {
  it("shows restored counts and unresolved paths without blocking the workspace", () => {
    const message = formatImportSummary({
      textBindingsRestored: 10,
      chartBindingsRestored: 2,
      unresolvedPlaceholders: ["OldModule.RemovedField"],
      warnings: [],
    });

    expect(message).toContain("自动恢复 10 个文本绑定和 2 个图表绑定");
    expect(message).toContain("OldModule.RemovedField");
  });
});
