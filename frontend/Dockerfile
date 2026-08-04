FROM oven/bun:1.3.14 AS build
WORKDIR /src

# Built from the repo root context (this is a Bun workspace: the lockfile
# lives at the root and frontend depends on the design-system package).
COPY package.json bun.lock ./
COPY design-system/package.json ./design-system/package.json
COPY frontend/package.json ./frontend/package.json
RUN bun install --frozen-lockfile

COPY design-system ./design-system
COPY frontend ./frontend
ARG VITE_API_URL=http://localhost:5073/api
ENV VITE_API_URL=$VITE_API_URL
RUN bun run --cwd design-system build
RUN bun run --cwd frontend build

# nginx-unprivileged: non-root by default, listens on 8080 by default —
# matches the backend's non-root/8080 conventions (see
# backend/Acordanza.Api/Dockerfile).
FROM nginxinc/nginx-unprivileged:1-alpine AS final
COPY --from=build /src/frontend/dist /usr/share/nginx/html
COPY frontend/nginx.conf /etc/nginx/conf.d/default.conf
EXPOSE 8080
# The base image already runs as the unprivileged "nginx" user; restated so
# the non-root guarantee is visible (and scannable) in this Dockerfile
# rather than inherited invisibly.
USER nginx
