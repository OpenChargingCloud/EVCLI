#!/bin/bash
#
# Start the vehicle with whatever was passed here, e.g.
#
#   ./run.sh --any --sdp
#
# --help lists the switches.

cd libs
cd Styx;   versionHash_Styx=$(git rev-list --max-count=1 HEAD);   cd ..
cd Hermod; versionHash_Hermod=$(git rev-list --max-count=1 HEAD); cd ..
cd ..

set -e
cd "$(dirname "$0")"

dotnet run --no-build --no-restore --project ChargingStationCLI -- $versionHash_Styx $versionHash_Hermod
