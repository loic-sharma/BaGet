/** BaGet base URL without a trailing slash; empty = same origin. */
export const baseUrl = (import.meta.env.VITE_BAGET_URL ?? '').replace(/\/$/, '');

/** Where the build is served (Vite `base`, BaGet's Frontend:SpaBasePath), without the trailing slash. */
export const basePath = import.meta.env.BASE_URL.replace(/\/$/, '');
