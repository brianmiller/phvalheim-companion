#!/bin/bash
#
# Populate ./libs with the reference assemblies this project builds against.
#
# The old project file pointed every HintPath at absolute Windows paths inside a Steam
# install and a roaming PhValheim world directory, so it could only be built on one
# particular desktop. libs/ replaces that; this script fills it.
#
# libs/ is .gitignore'd on purpose: these are Iron Gate's and Unity's assemblies and this
# repository is public. Never commit them.
#
# Two sources, in order of preference:
#
#   1. A running phvalheim-server container that has at least one BUILT MODDED WORLD.
#      This is the authoritative source -- the assemblies are exactly what the worlds run.
#      A vanilla world will NOT do: it has no game/BepInEx tree.
#
#   2. A sibling checkout that already carries a vetted set. phvalheim-tickmonitor keeps
#      three DLLs under libs/; thorskist keeps the full set under lib/, including
#      assembly_valheim_publicized.dll, which is the one this project actually needs.
#
# NOTE ON THE PUBLICIZED ASSEMBLY
# There is no publicizer on this box and no publicized_assemblies directory in any
# container, so route 1 cannot produce assembly_valheim_publicized.dll on its own. If you
# take route 1 you still need the publicized assembly from route 2, or you need to run a
# publicizer over the raw assembly_valheim.dll yourself.

set -u

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
LIBS="$SCRIPT_DIR/libs"
CONTAINER_NAME="${CONTAINER_NAME:-phvalheim-dev}"
THORSKIST="${THORSKIST:-$SCRIPT_DIR/../thorskist}"

mkdir -p "$LIBS"

# Everything the csproj references, by filename.
NEEDED="
0Harmony.dll
BepInEx.dll
netstandard.dll
Splatform.dll
System.Memory.dll
System.Runtime.CompilerServices.Unsafe.dll
UnityEngine.AnimationModule.dll
UnityEngine.CoreModule.dll
UnityEngine.dll
UnityEngine.IMGUIModule.dll
UnityEngine.InputLegacyModule.dll
UnityEngine.PhysicsModule.dll
UnityEngine.TextRenderingModule.dll
UnityEngine.UI.dll
UnityEngine.UIModule.dll
Unity.TextMeshPro.dll
assembly_guiutils.dll
assembly_utils.dll
assembly_valheim_publicized.dll
"

echo "Populating $LIBS"

if [ -d "$THORSKIST/lib" ]; then
	echo "  Source: $THORSKIST/lib"
	for f in $NEEDED; do
		if [ -f "$THORSKIST/lib/$f" ]; then
			cp -n "$THORSKIST/lib/$f" "$LIBS/" 2>/dev/null
		fi
	done
else
	echo "  WARNING: $THORSKIST/lib not found. Set THORSKIST=/path/to/thorskist."
fi

# Report what is still missing rather than letting the build fail with a wall of CS0246.
# A missing assembly here produces dozens of unrelated-looking type errors, so naming the
# file is worth more than the build log is.
missing=0
for f in $NEEDED; do
	if [ ! -f "$LIBS/$f" ]; then
		echo "  MISSING: $f"
		missing=$((missing + 1))
	fi
done

if [ "$missing" -gt 0 ]; then
	echo ""
	echo "$missing assembly/assemblies missing. The build will fail with CS0246 errors that"
	echo "will NOT name the missing file. Resolve these first."
	exit 1
fi

echo ""
echo "All $(echo $NEEDED | wc -w) reference assemblies present."
echo "Build with: dotnet build -c Release"
echo "Output:     bin/Release/net472/PhValheimCompanion.dll"
