/// <reference types="vite/client" />
interface ImportMetaEnv {
  /** Base URL of the API when it is not served from the same origin. Empty by default (relative URLs). */
  readonly VITE_API_BASE?: string;
}
