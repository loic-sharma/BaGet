load '/opt/bats-support/load.bash'
load '/opt/bats-assert/load.bash'

@test "push private package baget-two v1.0.0" {
  run /bin/bash -c "cd ../input/baget-two/bin/Debug/ && dotnet nuget push baget-two.1.0.0.nupkg --source http://baget:9090/v3/index.json --api-key NUGET-SERVER-API-KEY"
  assert_output --partial "Your package was pushed"
  assert_equal "$status" 0
}

@test "nuget install latest package version (1.0.0)" {
  run /bin/bash -c "cd nuget && nuget install baget-two -DisableParallelProcessing -NoCache -DirectDownload -Source http://baget:9090/v3/index.json"
  assert_output --partial "http://baget:9090/v3/index.json"
  assert_equal "$status" 0
  assert [ -e 'nuget/baget-two.1.0.0' ]
}

@test "paket update latest package version (1.0.0)" {
  run /bin/bash -c "cd paket && mono /ide/work/e2e/.paket/paket.exe update"
  refute_output --partial 'Could not download'
  refute_output --partial 'went wrong'
  assert_equal "$status" 0
  assert [ -e 'paket/packages/baget-two/baget-two.1.0.0.nupkg' ]
}

# Publish newer version
@test "push private package baget-two v2.1.0" {
  run /bin/bash -c "cd ../input/baget-two/bin/Debug/ && dotnet nuget push baget-two.2.1.0.nupkg --source http://baget:9090/v3/index.json --api-key NUGET-SERVER-API-KEY"
  assert_output --partial "Your package was pushed"
  assert_equal "$status" 0
}

@test "check registration endpoint includes v2.1.0" {
  run /bin/bash -c "curl http://baget:9090/v3/registration/baget-two/index.json"
  assert_output --partial "2.1.0"
  assert_equal "$status" 0
}

@test "nuget install latest package version (2.1.0)" {
  run /bin/bash -c "cd nuget && nuget locals http-cache -clear && nuget install baget-two -DisableParallelProcessing -NoCache -DirectDownload -Source http://baget:9090/v3/index.json"
  refute_output --partial 'Could not download'
  refute_output --partial 'went wrong'
  assert_output --partial "Successfully installed 'baget-two 2.1.0'"
  assert_output --partial "http://baget:9090/v3/index.json"
  assert_equal "$status" 0
  assert [ -e 'nuget/baget-two.2.1.0' ]
}

@test "paket update latest package version (2.1.0)" {
  run /bin/bash -c "cd paket && mono /ide/work/e2e/.paket/paket.exe update"
  refute_output --partial 'Could not download'
  refute_output --partial 'went wrong'
  assert_equal "$status" 0
  assert [ -e 'paket/packages/baget-two/baget-two.2.1.0.nupkg' ]
}

@test "paket install with constraint (< 2.0.0)" {
  run /bin/bash -c "cd paket-constraint && mono /ide/work/e2e/.paket/paket.exe install"
  refute_output --partial 'Could not download'
  refute_output --partial 'went wrong'
  assert_equal "$status" 0
  assert [ -e 'paket-constraint/packages/baget-two/baget-two.1.0.0.nupkg' ]
}
