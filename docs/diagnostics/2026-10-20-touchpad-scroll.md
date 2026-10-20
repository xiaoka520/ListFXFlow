# Windows build 39 two-finger scroll report

Evidence supplied by the user on 2026-10-20:

- Windows physical-mouse scrolling works on the Mac; two-finger scrolling affects neither machine.
- The displayed native touchpad state is “本机多指手势已屏蔽”.
- Worker log identifies `0.2.3+build.39` on Windows `10.0.26300.0`.
- At 09:06:54 UTC the worker confirms foreground ownership and an enabled SDK controller supporting all configured three/four/five-finger kinds. It remains in remote mode until 09:08:26 UTC without a foreground-loss/error entry in that interval.
- The parent log confirms a successful foreground grant for that epoch. Earlier short-lived worker exits have exit code zero; these entries do not establish a crash.
- Build 39 logs do not count pointer/wheel/forwarding events, so they cannot establish which scrolling path ran. Foreground ownership alone cannot establish that scrolling was generated or forwarded.

Code findings and build 40 response:

- The raw mouse path deliberately dropped all device-zero wheel reports while native capture was owned. A valid touchpad wheel could therefore be discarded without any working replacement. The new raw conversion accepts these reports with the same units as physical mice.
- The suppress-only foreground window was registered to receive two-finger raw pointers, although only three-or-more-finger global suppression was needed. Build 40 leaves that window on ordinary Windows two-finger recognition.
- The physical-mouse forwarding path stays in use. Three-or-more-finger SDK interception remains enabled. Low-level event suppression still prevents ordinary input from reaching Windows applications while controlling the Mac.
- New background counters distinguish native window messages, raw wheel reports, worker forwarding and frames submitted to the parent send callback. They contain counts and state only; no pointer coordinates or key/text contents.
- This is a correction plus instrumentation. Actual two-finger behavior must be verified on the Windows machine; core and loopback tests cannot establish native gesture recognition.

Reference: [Microsoft RAWINPUTHEADER](https://learn.microsoft.com/en-us/windows/win32/api/winuser/ns-winuser-rawinputheader).
