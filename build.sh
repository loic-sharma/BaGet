#!/bin/bash

set -e

function revert_paket_targets {
  git checkout .paket/Paket.Restore.targets
}
#PATCH: fake keeps modifying the Paket.Restore.targets to use mono
trap revert_paket_targets EXIT

dotnet restore .paket/

fake run build.fsx "$@"
