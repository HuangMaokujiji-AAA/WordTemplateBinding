import { downloadDocx } from "./httpClient";

export async function downloadReport(
  templateId: string,
  values?: Record<string, unknown>
): Promise<string> {
  return downloadDocx(
    "/api/reports/generate",
    {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(values ? { templateId, values } : { templateId }),
    },
    "report_generated.docx"
  );
}
export async function downloadBindingSetReport(
  bindingSetId: string
): Promise<string> {
  return downloadDocx(
    `/api/binding-sets/${encodeURIComponent(bindingSetId)}/reports`,
    { method: "POST" },
    "report_generated.docx"
  );
}

export async function downloadBindingSetReusableTemplate(
  bindingSetId: string
): Promise<string> {
  return downloadDocx(
    `/api/binding-sets/${encodeURIComponent(bindingSetId)}/export-reusable`,
    { method: "POST" },
    "template-template.docx"
  );
}

