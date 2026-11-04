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
# Set PHVALHEIM_REFS_DIR to a directory containing the required reference DLLs from
# your own modded Valheim installation, including assembly_valheim_publicized.dll.
# This script copies prepared references; it does not download or publicize game files.
# The legacy default is ../thorskist/lib (THORSKIST can override that checkout).

set -u

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
LIBS="$SCRIPT_DIR/libs"
CONTAINER_NAME="${CONTAINER_NAME:-phvalheim-dev}"
THORSKIST="${THORSKIST:-$SCRIPT_DIR/../thorskist}"
REFS_DIR="${PHVALHEIM_REFS_DIR:-$THORSKIST/lib}"

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

if [ -d "$REFS_DIR" ]; then
	echo "  Source: $REFS_DIR"
	for f in $NEEDED; do
		if [ -f "$REFS_DIR/$f" ]; then
			cp -n "$REFS_DIR/$f" "$LIBS/" 2>/dev/null
		fi
	done
else
	echo "  WARNING: $REFS_DIR not found. Set PHVALHEIM_REFS_DIR to your prepared reference directory."
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
