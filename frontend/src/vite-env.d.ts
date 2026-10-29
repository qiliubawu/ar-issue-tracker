/// <reference types="vite/client" />

interface ImportMetaEnv {
  /** 后端地址，可在 .env 中通过 VITE_API_BASE 覆盖 */
  readonly VITE_API_BASE?: string;
}

interface ImportMeta {
  readonly env: ImportMetaEnv;
}
