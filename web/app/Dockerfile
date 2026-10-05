# P6: Next.js three-stage build (deps -> builder -> runner), standalone output, non-root.
# Context is the web/ workspace root (`docker build -f web/app/Dockerfile web`).
FROM node:22-alpine AS base
RUN npm install -g pnpm@12.9.1

FROM base AS deps
WORKDIR /repo
COPY package.json pnpm-workspace.yaml pnpm-lock.yaml ./
COPY app/package.json app/
RUN pnpm install --frozen-lockfile

FROM base AS builder
WORKDIR /repo
COPY --from=deps /repo/node_modules ./node_modules
COPY --from=deps /repo/app/node_modules ./app/node_modules
COPY package.json pnpm-workspace.yaml pnpm-lock.yaml ./
COPY app ./app
ENV NEXT_TELEMETRY_DISABLED=1
RUN pnpm --filter @exit-interview-agent/web build

FROM node:22-alpine AS runner
LABEL org.opencontainers.image.source="https://github.com/konradcinkusz/exit-interview-agent"
LABEL org.opencontainers.image.description="exit-interview-agent web (Next.js BFF)"
LABEL org.opencontainers.image.licenses="MIT"
WORKDIR /srv
ENV NODE_ENV=production NEXT_TELEMETRY_DISABLED=1 PORT=3000 HOSTNAME=0.0.0.0
RUN addgroup -S nodejs && adduser -S nextjs -G nodejs
# Standalone output nests the app under its workspace folder (tracing root = web/).
COPY --from=builder --chown=nextjs:nodejs /repo/app/.next/standalone ./
COPY --from=builder --chown=nextjs:nodejs /repo/app/.next/static ./app/.next/static
USER nextjs
EXPOSE 3000
CMD ["node", "app/server.js"]
