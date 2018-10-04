load '/opt/bats-support/load.bash'
load '/opt/bats-assert/load.bash'

@test "/ return SPA index.html" {
  run /bin/bash -c "curl http://baget:9090"
  assert_output --partial "You need to enable JavaScript to run BaGet UI."
  assert_equal "$status" 0
}
