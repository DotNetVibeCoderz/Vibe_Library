#!/usr/bin/env bash
# Proves the dotnet new templates work end to end: packs LVGL.Net and the templates into a local
# feed, installs the templates into a throw-away hive, creates an app from each layout choice plus
# a screen from the item template, builds them against the local packages, and runs each one
# off-screen until it has rendered and saved a PNG.
#
#   bash templates/test-templates.sh            # from the LVGL.Net folder
#
# Needs the native library built first (native/build.sh or native/build.ps1): the LVGL.Net package
# carries it, and the generated apps render with it.
set -euo pipefail

root="$(cd "$(dirname "$0")/.." && pwd)"
work="$(mktemp -d 2>/dev/null || mktemp -d -t lvgltemplates)"
feed="$work/feed"
hive="$work/hive"
trap 'rm -rf "$work"' EXIT

# A private package cache: a stale LVGL.Net of the same version in ~/.nuget must not be used.
export NUGET_PACKAGES="$work/packages"

version=$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' "$root/Directory.Build.props" | head -1)
echo "LVGL.Net $version - packing into $feed"

for project in src/LVGL.Net src/LVGL.Net.Ui src/LVGL.Net.Sdl src/LVGL.Net.Linux templates; do
  dotnet pack "$root/$project" -c Release -o "$feed" -p:IncludeSymbols=false --nologo -v quiet
done

dotnet new install "$feed"/LVGL.Net.Templates.*.nupkg --debug:custom-hive "$hive" > /dev/null

# NuGet reads this file natively: under Git Bash on Windows a /tmp path must be translated.
feed_native="$(cygpath -m "$feed" 2>/dev/null || echo "$feed")"

cat > "$work/nuget.config" <<EOF
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="local" value="$feed_native" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
</configuration>
EOF

for layout in code json; do
  app="Demo_$layout"
  echo "--- dotnet new lvgl --layout $layout"
  dotnet new lvgl -n "$app" -o "$work/$app" --layout "$layout" --debug:custom-hive "$hive" --no-restore > /dev/null

  if [ "$layout" = json ]; then
    # The item template, added the way a user would: from inside the project.
    (cd "$work/$app" && dotnet new lvgl-screen -n Settings --debug:custom-hive "$hive" > /dev/null)
    test -f "$work/$app/Settings.cs" && test -f "$work/$app/Screens/Settings.lvgl.json"
  fi

  cp "$work/nuget.config" "$work/$app/"
  dotnet build "$work/$app" -c Release --nologo -v quiet -warnaserror

  if ! dotnet run --project "$work/$app" -c Release --no-build -- --headless --screenshot "$work/$app.png"; then
    # Enough to diagnose a failure on a CI runner without reproducing it there.
    echo "::group::$app - project files"; find "$work/$app" -not -path '*/obj/*' -not -path '*/bin/*' | sort; echo "::endgroup::"
    echo "::group::$app - build output"; find "$work/$app/bin" -maxdepth 4 | sort; echo "::endgroup::"
    echo "::group::$app - project file"; cat "$work/$app"/*.csproj; echo "::endgroup::"
    exit 1
  fi
  test -s "$work/$app.png"
  echo "ok: $app built, rendered and saved a screenshot"

  if [ -n "${LVGLNET_TEMPLATE_SCREENSHOTS:-}" ]; then
    mkdir -p "$LVGLNET_TEMPLATE_SCREENSHOTS"
    cp "$work/$app.png" "$LVGLNET_TEMPLATE_SCREENSHOTS/template-$layout.png"
  fi
done

echo "Templates OK"
