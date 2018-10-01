load '/opt/bats-support/load.bash'
load '/opt/bats-assert/load.bash'

@test "push private package baget-test1" {
  run /bin/bash -c "cd ../input/baget-test1/bin/Debug/ && dotnet nuget push baget-test1.1.0.0.nupkg --source http://baget:9090/v3/index.json --api-key NUGET-SERVER-API-KEY"
  assert_output --partial "Your package was pushed"
  assert_equal "$status" 0
}

@test "nuget install exact package version" {
  run /bin/bash -c "cd nuget && nuget install baget-test1 -Version 1.0.0 -DisableParallelProcessing -NoCache -Source http://baget:9090/v3/index.json"
  assert_output --partial "http://baget:9090/v3/index.json"
  assert_equal "$status" 0
}

@test "paket install pinned package version" {
  run /bin/bash -c "cd paket-pinned && mono /ide/work/e2e/.paket/paket.exe install"
  refute_output --partial 'Could not download'
  refute_output --partial 'went wrong'
  assert_equal "$status" 0
}

@test "paket restore pinned package version" {
  run /bin/bash -c "cd paket-locked && mono /ide/work/e2e/.paket/paket.exe restore"
  refute_output --partial 'Could not download'
  refute_output --partial 'went wrong'
  assert_equal "$status" 0
}

@test "dotnet restore exact package version" {
  run /bin/bash -c "cd dotnet && dotnet restore --no-cache"
  assert_output --partial "Restore completed"
  assert_equal "$status" 0
}
