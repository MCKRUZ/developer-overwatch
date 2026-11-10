import { describe, it, expect } from "vitest";
import template from "../nginx.conf.template?raw";
import legacyConf from "../nginx.conf?raw";

// The Dockerfile copies only nginx.conf.template; nginx.conf is kept in step so the two never drift.
const configs = [
  ["nginx.conf.template", template],
  ["nginx.conf", legacyConf],
] as const;

// Ends at the block's own closing-brace line, not the first "}" (the template's ${API_UPSTREAM} has one).
function locationBlock(conf: string, path: string): string {
  const match = conf.match(new RegExp(`location ${path} \\{[\\s\\S]*?\\n\\s*\\}`));
  return match?.[0] ?? "";
}

describe.each(configs)("%s", (_name, conf) => {
  const hubs = locationBlock(conf, "/hubs/");

  it("should proxy /hubs/ so the dashboard's SignalR connection reaches the API instead of index.html", () => {
    expect(hubs).toMatch(/proxy_pass\s+\S+;/);
  });

  it("should upgrade /hubs/ to a WebSocket, which needs HTTP/1.1 and the Upgrade/Connection hop headers", () => {
    expect(conf).toMatch(/map \$http_upgrade \$connection_upgrade \{[^}]*default\s+upgrade;[^}]*''\s+close;[^}]*\}/);
    expect(hubs).toContain("proxy_http_version 1.1;");
    expect(hubs).toContain("proxy_set_header Upgrade $http_upgrade;");
    expect(hubs).toContain("proxy_set_header Connection $connection_upgrade;");
  });

  it("should keep an idle hub connection open for an hour rather than nginx's 60s default", () => {
    expect(hubs).toContain("proxy_read_timeout 3600s;");
  });

  it("should not buffer /hubs/ so the Server-Sent Events fallback transport streams", () => {
    expect(hubs).toContain("proxy_buffering off;");
  });

  it("should not write /hubs/ requests to the access log, because the access_token query value would land there", () => {
    expect(hubs).toContain("access_log off;");
  });

  it("should log only crit and worse inside /hubs/, because a failed upstream logs the request line, token included, at error", () => {
    expect(hubs).toMatch(/\n\s*error_log \/dev\/stderr crit;/);
  });

  it("should not set headers inside /hubs/, which would drop the server-level security headers there", () => {
    expect(hubs).not.toContain("add_header");
  });

  it("should state connect-src 'self' in the CSP so fetch and same-origin ws/wss stay restricted to this origin", () => {
    expect(conf).toMatch(/Content-Security-Policy "[^"]*connect-src 'self';[^"]*"/);
  });
});
