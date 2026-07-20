import { renderAsync } from "docx-preview";

/**
 * Render a DOCX blob using docx-preview.
 *
 * Configuration notes:
 *  - renderAltChunks: false — prevents loading external HTML content
 *  - useBase64URL: true — use base64 for images
 *  - renderComments: false — skip comment rendering for cleaner output
 *  - breakPages: true — preserve page breaks
 *
 * @param docxBlob - The modified DOCX blob (with chart markers injected).
 * @param documentContainer - The DOM element to render the document into.
 * @param styleContainer - The DOM element for CSS styles.
 * @param options - Additional render options.
 */
export async function renderDocx(
  docxBlob: Blob,
  documentContainer: HTMLElement,
  styleContainer: HTMLElement,
  options?: {
    className?: string;
  }
): Promise<void> {
  await renderAsync(docxBlob, documentContainer, styleContainer, {
    className: options?.className ?? "docx",
    inWrapper: true,
    breakPages: true,
    ignoreLastRenderedPageBreak: false,
    renderHeaders: true,
    renderFooters: true,
    renderFootnotes: true,
    renderEndnotes: true,
    renderComments: false,
    renderChanges: true,
    renderAltChunks: false,
    useBase64URL: true,
  });
}
