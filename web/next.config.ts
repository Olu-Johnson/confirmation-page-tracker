import type { NextConfig } from "next";

const nextConfig: NextConfig = {
  poweredByHeader: false,
  // Self-contained server bundle for the Docker image (.next/standalone).
  output: "standalone",
  // Dev only: let `next dev` serve hot reload (/_next/hmr) when opened through a tunnel.
  allowedDevOrigins: ["*.trycloudflare.com", "*.ngrok-free.dev", "*.ngrok-free.app"],
  async headers() {
    return [
      {
        // tracker.js is loaded by the client's website (cross-origin).
        source: "/tracker.js",
        headers: [
          { key: "Access-Control-Allow-Origin", value: "*" },
          { key: "Cache-Control", value: "public, max-age=300" },
        ],
      },
      {
        source: "/admin/:path*",
        headers: [
          { key: "X-Frame-Options", value: "DENY" },
          { key: "Referrer-Policy", value: "same-origin" },
          { key: "X-Robots-Tag", value: "noindex, nofollow" },
        ],
      },
    ];
  },
};

export default nextConfig;
