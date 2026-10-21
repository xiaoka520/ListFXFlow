# Windows build 43: administrator startup and UAC recovery

The desktop app and installer now request `requireAdministrator`, with
`uiAccess=false`. The installer needs the same integrity level to signal and
update the running application. Touchpad workers inherit the elevated parent
token; no system service or UAC policy change is installed.

Previously, any rejected SendInput called FailInjection, which disabled input
and MainWindow.Stop also disabled automatic reconnection. This made a temporary
secure-desktop transition permanently stop sharing.

The input dispatcher now probes the input desktop every 100 ms and the native
worker checks before each injection. A different or inaccessible input desktop
pauses capture, injection and touchpad ownership while authenticated networking
and heartbeats continue. The probe reads desktop identity and never switches
desktops. Genuine injection failures on the normal desktop still stop sharing.

Desktop epochs discard old queued work across pause/resume, including Caps
transitions otherwise retained by queue cancellation. A record of successful
native key/button downs supplies release-only cleanup on return. Cleanup survives
ownership-related queue cancellation and runs before new input. Hooks are
reinstalled and the previous routing owner is retained. Lock, sleep, disconnect
and deliberate stop keep their existing behavior.

Portable tests cover epoch invalidation, repeated pause/resume, extended modifier
and side-button release, normally released keys, downs completing during a
transition, and cleanup surviving a stalled worker plus ownership cancellation.
These tests do not change the system clipboard, invoke UAC or inject native input.

Local results: 331 portable core checks passed. Self-contained Windows x64 app
and installer publishing succeeded. Both PE resource manifests were extracted
and verified as `requireAdministrator`, `uiAccess=false`, with file version
`0.2.3.43`. Portable ZIP contents, embedded payload input and SHA-256 matched.

Target Windows validation is still required: launch elevation; control of an
elevated app; UAC accept/cancel and repeated prompts; no stale clicks or held
modifiers on return; touchpad recovery; explicit stop and disconnect during UAC;
installer update/uninstall while the elevated app is running. Cross-compilation
does not validate these desktop behaviors.

Direct control of the UAC secure desktop is not implemented. The Windows user
must approve or cancel locally. Administrator startup alone cannot provide that
access; Microsoft describes SYSTEM execution on the UAC desktop separately:
https://learn.microsoft.com/en-us/windows/win32/winauto/uiauto-securityoverview
