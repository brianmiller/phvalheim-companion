#!/usr/bin/env bash
# Execute the production Update/click/button-policy bodies with deterministic menu inputs.
set -eu
cd "$(dirname "$0")/.."
task_dir=$(mktemp -d)
trap 'rm -rf "$task_dir"' EXIT
python3 - "$task_dir" <<'PY'
import pathlib, re, sys
source = pathlib.Path('ConnectDialog.cs').read_text()
def method(signature):
    start = source.index(signature)
    end = source.index('\n        }', start) + len('\n        }')
    return source[start:end]
closed = re.search(r'private bool _closedByPlayer[^;]*;', source).group()
methods = '\n'.join(method(s) for s in ['private void Update()', 'private void OnReopenRequested()', 'internal static bool WantsReopenButton('])
code = '''using System;
class Dialog {
private bool _shown, _nativeButtonOk;
CLOSED
private string _lastDecline;
private bool menu = true, ready = true;
private int displays;
private bool button;
private bool _modListBuilt;
private Refresh _connectionRefresh = new();
class Refresh { public bool Tick(object p) => false; }
class TMP_Text { public string text = ""; }
static class Utils { public static bool TryGetFieldValue(object p, string k, out object v) { v = null; return false; } }
static class LaunchPayload { public static bool Present = true; public static object Current = new(); public static object InstalledPlugins() => null; }
static class ClientManifest { public static bool Present = false; }
static class ConnectFlow { public static bool Connecting, Fail; public static void NoticeCharacterSelect(bool b) {} public static bool NoticeMainMenu(bool b) { if (!Fail) return false; Connecting = false; return true; } }
static class MenuButton { public static void Remove() {} }
bool IsMainMenuActive() => menu;
bool IsCharacterSelectActive() => false;
bool IsReadyToShow() => menu && ready;
string WhyNotReady() => "waiting";
void NoteDecline(string s) {}
object PopupInstance() => null;
string BuildBodyText(object p, object mods, bool built) => "";
void ManageReopenButton() { button = WantsReopenButton(_closedByPlayer, _shown, ConnectFlow.Connecting, false, menu); }
void Show() { displays++; }
METHODS
static void Check(bool ok, string name) { if (!ok) throw new Exception(name); Console.WriteLine("PASS " + name); }
void Close() { _closedByPlayer = true; }
public static void Main() {
foreach (bool payload in new[] {true, false}) {
LaunchPayload.Present = payload; ClientManifest.Present = !payload;
var d = new Dialog();
d.Update(); d.Update(); Check(d.displays == 0 && d.button, "startup offers only CONNECT (payload=" + payload + ")");
d.menu = false; d.Update(); d.menu = true; d.Update(); Check(d.displays == 0 && d.button, "returning to menu stays closed");
d.ready = false; d.OnReopenRequested(); d.Update(); Check(d.displays == 0, "click waits for popup readiness");
d.ready = true; d.Update(); d.Update(); Check(d.displays == 1, "click opens once when ready");
d.Close(); d.Update(); d.menu = false; d.Update(); d.menu = true; d.Update(); Check(d.displays == 1 && d.button, "close and menu navigation do not reopen");
d.OnReopenRequested(); d.Update(); Check(d.displays == 2, "second click reopens");
ConnectFlow.Connecting = true; ConnectFlow.Fail = true; d.Update(); d.Update(); Check(d.displays == 2 && d.button, "failed or cancelled join returns to button");
d.OnReopenRequested(); d.Update(); Check(d.displays == 3, "click after failed join opens retry dialog");
ConnectFlow.Fail = false;
}
}
}
'''.replace('CLOSED', closed).replace('METHODS', methods)
path = pathlib.Path(sys.argv[1]); (path/'Program.cs').write_text(code)
(path/'test.csproj').write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework><NoWarn>CS0414;CS0649</NoWarn></PropertyGroup></Project>')
PY
dotnet run --project "$task_dir/test.csproj" -v quiet
