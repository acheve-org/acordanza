FROM oven/bun:1.4.0 AS build
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
ARG VITE_AMPLITUDE_API_KEY=""
ENV VITE_AMPLITUDE_API_KEY=$VITE_AMPLITUDE_API_KEY
RUN bun run --cwd design-system build
RUN bun run --cwd frontend build

# The CSP's connect-src has to name the API origin, and this is the one place
# that knows it: VITE_API_URL is already baked into the bundle by the build
# above, so deriving the origin from the same value is what keeps the policy
# and the requests it governs from ever disagreeing. Done here rather than in
# the nginx stage because that image runs as a non-root user which can't write
# to /etc/nginx.
#
# The last line fails the build if the placeholder survived: a CSP quietly
# shipping the literal "__API_ORIGIN__" as its connect-src would block every
# API call in production, and the image would still look fine here.
RUN API_ORIGIN=$(printf '%s' "$VITE_API_URL" | sed -E 's#^(https?://[^/]+).*#\1#') \
    && sed -e "s#__API_ORIGIN__#${API_ORIGIN}#g" frontend/nginx.conf > /tmp/default.conf \
    && ! grep -q '__API_ORIGIN__' /tmp/default.conf

# nginx-unprivileged: non-root by default, listens on 8080 by default —
# matches the backend's non-root/8080 conventions (see
# backend/Acordanza.Api/Dockerfile).
FROM nginxinc/nginx-unprivileged:1-alpine AS final
COPY --from=build /src/frontend/dist /usr/share/nginx/html
COPY --from=build /tmp/default.conf /etc/nginx/conf.d/default.conf
EXPOSE 8080
# The base image already runs as the unprivileged "nginx" user; restated so
# the non-root guarantee is visible (and scannable) in this Dockerfile
# rather than inherited invisibly.
USER nginx
