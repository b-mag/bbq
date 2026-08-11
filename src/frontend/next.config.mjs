/**
 * Next.js Configuration for Carcosa Frontend
 *
 * WHY output: 'export':
 * The frontend is served as static files from the .NET server's wwwroot/ directory.
 * Static export means Next.js pre-renders all pages to HTML/JS/CSS files at build time.
 * No Node.js server is needed at runtime — the .NET server handles all HTTP serving.
 * This is what makes the single-exe distribution possible.
 *
 * WHY images.unoptimized:
 * Next.js image optimization requires a running Node.js server (for on-demand resizing).
 * Since we use static export, image optimization is disabled. Game assets will be
 * loaded as-is (PNG sprites at their native resolution).
 *
 * WHY trailingSlash:
 * Static file servers (like Kestrel serving wwwroot/) work better with trailing slashes
 * because each "route" maps to a directory with an index.html inside it.
 * Without this, client-side routing links would 404 on hard refresh.
 */

/** @type {import('next').NextConfig} */
const nextConfig = {
  output: 'export',
  images: {
    unoptimized: true,
  },
  trailingSlash: true,
};

export default nextConfig;
