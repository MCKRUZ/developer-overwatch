/// <reference types="vite/client" />

interface ImportMetaEnv {
  readonly VITE_OIDC_AUTHORITY: string | undefined;
  readonly VITE_OIDC_CLIENT_ID: string | undefined;
  readonly VITE_OIDC_REDIRECT_URI: string | undefined;
  readonly VITE_OIDC_SCOPE: string | undefined;
}

interface ImportMeta {
  readonly env: ImportMetaEnv;
}
