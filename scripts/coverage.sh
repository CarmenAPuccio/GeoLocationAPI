#!/usr/bin/env bash
# Runs every test project with code coverage and fails unless line and branch coverage are 100%.
# Generated code (for example the OpenAPI XML comment support) is excluded in each coverage.runsettings.
set -euo pipefail

cd "$(dirname "$0")/.."
results="$(mktemp -d)"
trap 'rm -rf "$results"' EXIT

status=0
for project in GeoLocationAPI.Tests CdkGeoLocationApi/test/CdkGeoLocationApi.Tests; do
  name="$(basename "$project")"
  dotnet test "$project" --collect:"XPlat Code Coverage" --results-directory "$results/$name" > "$results/$name.log" 2>&1 \
    || { cat "$results/$name.log"; echo "FAIL: tests failed in $name"; exit 1; }

  report="$(find "$results/$name" -name coverage.cobertura.xml | head -n 1)"
  line="$(grep -o -m1 'line-rate="[0-9.]*"' "$report" | head -n 1 | cut -d'"' -f2)"
  branch="$(grep -o -m1 'branch-rate="[0-9.]*"' "$report" | head -n 1 | cut -d'"' -f2)"
  printf '%-28s line %.1f%%  branch %.1f%%\n' "$name" "$(echo "$line * 100" | bc -l)" "$(echo "$branch * 100" | bc -l)"

  if [ "$line" != "1" ] || [ "$branch" != "1" ]; then
    echo "FAIL: $name is below 100% coverage"
    status=1
  fi
done
exit $status
