/** Error types for file validation. */

export interface FileValidationResult {
  valid: boolean;
  error?: string;
}

export interface DocxIntegrityResult {
  valid: boolean;
  error?: string;
}
