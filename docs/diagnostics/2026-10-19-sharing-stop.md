# 2026-10-19 Mac sharing stop investigation

Timezone: Asia/Shanghai. Inspected installed Mac 0.2.3 build 34, pid 28067,
which started at 13:49:44 and remained running. Read-only inspection did not
restart the app, change permissions/network configuration, or modify input text.

- 20:34:42.912: WindowServer applied a display configuration, added display 0xa,
  marked it main and removed display 0x1. At 20:34:42.950 ListFXFlow cancelled
  its main TCP listener L10. This strongly matches the existing display-change
  stop branch in FlowModel; the precise app-level stop reason was not logged.
- Several later connection closures (20:36:02, 20:37:21, 20:37:59, 20:38:16)
  received READ_CLOSE from the remote endpoint; the main listener L13 remained
  alive. These were connection losses rather than turning off Mac listening.
  The underlying reason on Windows is not available from this Mac-side record.
- 20:40:27.291: AppKit/HIToolbox MBDisplays messages occurred. At 20:40:27.300,
  Mac cancelled main listener L13 and released the application's power assertions;
  the network path still reported satisfied/viable. The clipboard connection then
  closed. This confirms a local listener stop, not a process crash. It does not
  uniquely distinguish a display notification, input safeguard, user command, or
  remote reset. No system sleep occurred in this interval.
- 20:41:39: listeners and connections became ready again. The same process was
  still present. At inspection around 20:42 the UI showed connected and control Mac.

Evidence saved locally under .build/: mac-sharing-last10m.log,
mac-windowserver-last10m.log, mac-stop-204027-system.log, mac-power-recent.log.
No input contents or credentials were collected for this investigation.

Mac build 35 adds sharing-state / input-state / sharing-connection unified logs
with specific stop causes and preserves a deliberate stop reason against a queued
 generic disconnect status. Stop policy and thresholds remain unchanged.
