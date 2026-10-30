/// <reference types="vite/client" />
interface ImportMetaEnv {
  /** BaGet base URL; empty = same origin (the build served by BaGet, or the dev proxy). */
  readonly VITE_BAGET_URL?: string;
}
