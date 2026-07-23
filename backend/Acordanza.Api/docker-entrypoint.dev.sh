#!/bin/sh
set -e

# bin/obj are separate named volumes (not the /src bind mount) so they start
# out root-owned on first creation; fix that up, then drop to the non-root
# app user for the actual process. Requires the container to run as root
# (see docker-compose.override.yml) despite the image's default USER.
chown -R app:app /src/bin /src/obj

export HOME=/home/app
exec setpriv --reuid=app --regid=app --init-groups "$@"
