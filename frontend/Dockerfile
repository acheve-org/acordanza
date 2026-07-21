FROM oven/bun:1.3.14 AS build
WORKDIR /src

COPY package.json bun.lock .
RUN bun install --frozen-lockfile

COPY . .
ARG VITE_API_URL=http://localhost:5073/api
ENV VITE_API_URL=$VITE_API_URL
RUN bun run build

# nginx-unprivileged: non-root by default, listens on 8080 by default —
# matches the backend's non-root/8080 conventions (see
# backend/Acordanza.Api/Dockerfile).
FROM nginxinc/nginx-unprivileged:1-alpine AS final
COPY --from=build /src/dist /usr/share/nginx/html
COPY nginx.conf /etc/nginx/conf.d/default.conf
EXPOSE 8080
