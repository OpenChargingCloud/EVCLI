#!/bin/bash
#
# Start the vehicle with whatever was passed here, e.g.
#
#   ./run.sh --any --sdp
#
# --help lists the switches.

set -e

cd "$(dirname "$0")"

dotnet run --project EVCLI -- "$@"
