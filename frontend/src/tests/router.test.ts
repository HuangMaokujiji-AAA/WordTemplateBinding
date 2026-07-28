import { describe, expect, it } from "vitest";
import router from "../router";

describe("application routes", () => {
  it("exposes the home page and both business centers", () => {
    expect(router.resolve("/").name).toBe("home");
    expect(router.resolve("/template-center/templates").name).toBe(
      "template-center-library"
    );
    expect(router.resolve("/template-center/studio").name).toBe(
      "template-center-studio"
    );
    expect(router.resolve("/report-center/new").name).toBe(
      "report-center-new"
    );
  });

  it("keeps the former bookmarked routes available", () => {
    expect(router.resolve("/projects").name).toBe("legacy-projects");
    expect(router.resolve("/templates/10").name).toBe(
      "legacy-template-detail"
    );
    expect(router.resolve("/workspace?segmentId=4").name).toBe(
      "legacy-workspace"
    );
  });
});
