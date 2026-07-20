export const EMU_PER_PIXEL_AT_96_DPI = 9525;

export function emuToPixels(emu: number): number {
  return emu / EMU_PER_PIXEL_AT_96_DPI;
}
