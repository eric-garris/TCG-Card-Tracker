/**
 * True when the page runs inside an embedding viewer that blocks file downloads
 * (the single-file build sets window.TCGCT_EMBEDDED before the app loads).
 */
export const EMBEDDED =
  typeof window !== 'undefined' && (window as Window & { TCGCT_EMBEDDED?: boolean }).TCGCT_EMBEDDED === true;
