#!/bin/bash
#
# Pull everything and build it.
#
# The ISO 15118 schemas are not in any of these repositories and are not
# fetched here either - that is a licence you accept yourself, once:
#
#   bash libs/WWCP_ISO15118/tools/download-schemas.sh

set -e

cd "$(dirname "$0")"

git pull --ff-only
git submodule update --init --recursive
git submodule foreach git checkout master
git submodule foreach git pull
npm --prefix /home/ahzf/EVCLI/libs/EV/EV/Frontend ci
dotnet build EVCLI.slnx
