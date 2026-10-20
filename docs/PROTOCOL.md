# Protocol v1

TCP 49234 carries length-prefixed records; UDP 49235 is discovery only. Clipboard uses a separate authenticated TCP 49236 stream. There is no screen stream or input logging.

## Discovery and interface selection

If the user specifies a Mac IPv4 address, Windows validates it against the selected adapter and connects directly to TCP 49234, skipping UDP discovery. The same identity validation and encryption still apply. Connection preferences are saved locally; no system network settings are changed.

In automatic mode, Windows sends ASCII `ListFXFlow.discover.v1` to the selected adapter's IPv4 subnet broadcast address. macOS binds the discovery socket to `bridge0` using `IP_BOUND_IF` and replies with `{ "service": "ListFXFlow/1", "address": "…", "port": 49234 }`. Replies are bound to an advertised bridge address in the sender's subnet. Windows validates the response source address and subnet, then binds the TCP socket to its selected adapter's local IP. The Mac TCP listeners bind each bridge IPv4 address and prohibit Wi-Fi, cellular and loopback routes. The explicit development self-test uses loopback instead.

The application does not change routes, addresses, adapter settings, or firewall policies. UDP discovery can be blocked independently from TCP; current UI reports a timeout and requires the user to resolve the network condition.

## Automatic identity exchange

Each device generates a persistent P-256 ECDH identity. macOS stores its raw private key in Keychain, Windows stores its PKCS#8 private key encrypted with the current user's DPAPI. Public identities are X9.63 uncompressed 65-byte points, encoded as hex.

First connection uses **trust on first use over the selected physical link**, with no pairing code or confirmation dialog. This assumes the first cable connection and local hosts are trusted; the initial exchange does not authenticate a human or resist an active attacker already on that link. Subsequent sessions require the pinned public identity. Forgetting a peer removes its pin and requires another trusted first connection.

1. Client sends JSON `hello`: `version:1`, a random 32-byte `nonce`, and `publicKey`.
2. Server checks the stored client identity, calculates ECDH, and responds with `challenge`: `version:1`, a fresh random 32-byte `nonce`, its `publicKey`, and a server HMAC proof.
3. Client checks the stored server identity and HMAC, then sends `finish` containing its client HMAC proof.
4. Server validates, saves the client's identity and sends the first encrypted `ping`. Client validates this ping before saving the server's identity and enabling input.

ECDH result is the raw 32-byte shared X coordinate, preserving leading zeros. Master secret is SHA256 of `UTF8("ListFXFlow/1/ECDH") || rawSecret`. Proof is HMAC-SHA256(master, `UTF8("ListFXFlow/1/" + role) || clientNonce || serverNonce`). Roles are `server`, `client`, `c2s`, `s2c`. The latter two produce separate AES-256-GCM direction keys. Private identities are static, so this protocol does **not** provide forward secrecy. It is a small first-version protocol, not an independently audited security protocol.

## Framing and encryption

Every frame begins with a 4-byte unsigned big-endian body length. Reject zero or more than 16,384 bytes. Pre-authentication frames contain JSON. The handshake has a five-second total deadline and only one pending peer is admitted.

Encrypted bodies are `nonce[12] || ciphertext[n] || tag[16]`. Nonce is four zero bytes followed by an unsigned 64-bit big-endian counter starting at 1. Direction keys are distinct, counters strictly increase, and random handshake nonces derive new keys on every connection. AAD is `UTF8("ListFXFlow/1")`. Reject tampering, replay, sequence gaps, malformed fields, nonfinite coordinates, unsupported keys, and excessive sizes before delivering any input.

## Messages

All field names use camelCase, with absent optional fields omitted or null.

| type | Fields | Meaning |
| --- | --- | --- |
| `display` | width, height, features? | Primary display dimensions; each display is normalized to 1000 logical units high. |
| `layout` | side, offset | Windows relative to Mac: left/right/top/bottom; offset limited to ±900. Mac applies and rebroadcasts requests. Both routers rebase the active normalized screen coordinates without changing focus, pointer position, or held input. |
| `pointer` | active, x, y | Active device (`mac`/`windows`) and normalized coordinates 0…1 within its primary screen. All physical movement is synchronized, including movement within the active local screen. |
| `key` | key, down | USB HID usage from canonical keymap and press/release state. No text translation or input method synchronization. |
| `button` | button, down | 0 left, 1 right, 2 middle, 3/4 extra buttons. |
| `caps` | down | Desired Caps Lock state. Only sent after the peer advertises `caps-v1` in `display.features`. |
| `scroll` | dx, dy | Pixel-like scroll units. Positive dy is upward, positive dx is leftward. Windows wheel notch (120) maps to 40 units. |
| `clipboardReady` | nonce | Session-only 32-byte clipboard secret (hex), sent only after the peer advertises `clipboard-v1` in `display.features`. |
| `ping` | — | Heartbeat, once per second. |
| `reset` | — | Stop and release ownership. |

Caps Lock is a system toggle rather than a held key. Each side lets the physical Caps event reach its own OS, then exchanges the resulting state through `caps` so both desktops and keyboard LEDs agree. The Mac sends its current state when it receives the Windows `display` capabilities. Peers that do not advertise `caps-v1` keep Caps local only.

TCP preserves event order. On Windows, a dedicated input thread consumes a bounded queue; only adjacent pointer samples for the same active device can be coalesced. Buttons, keys, layout messages and focus transitions remain ordering barriers. Queue overflow stops sharing rather than silently dropping releases. No automatic reconnect or input resumption occurs after a disconnect. Mac starts listening automatically on application launch, waiting for permissions and a bridge address if necessary; a manual stop cancels this pending launch start. A three-second receive heartbeat timeout releases injected held keys/buttons and returns each machine to local control. Injected events are marked and excluded from retransmission.

The most recent mouse movement determines focus when both physical mice are used; simultaneous competing movement is not arbitrated with a distributed lock. Modifier/button holds block an edge crossing from that source. A focus change releases known held states to avoid leaving them latched on the old desktop.

## Clipboard extension v1

The optional `clipboard-v1` display feature negotiates clipboard support without breaking older input-only peers. TCP 49236 is bound to the same Mac bridge IPv4 addresses; Windows binds its selected adapter address and connects to the authenticated control peer address. The server accepts one clipboard connection at a time, with a five-second authentication deadline. The capability is random per control session and never sent in plaintext on the clipboard socket; stopping the control session closes clipboard listeners, sockets and incomplete transfers.

Client sends a plaintext hello containing client nonce C (32 bytes hex) and `HMAC(capability, UTF8("ListFXFlow/1/clipboard-client") || C)`. Server verifies and returns fresh server nonce S plus `HMAC(capability, UTF8("ListFXFlow/1/clipboard-server") || C || S)`. Client must then return a `finish` with `HMAC(capability, UTF8("ListFXFlow/1/clipboard-finish") || C || S)` before the server enables clipboard monitoring, preventing replayed hellos from authorizing a connection. Both derive `SHA256(UTF8("ListFXFlow/Clipboard/1") || capability)` and use the existing directional AES-GCM key derivation, nonce counter and AAD with C/S. Each new clipboard connection has fresh nonces, keeping keys distinct from the input channel and previous connections.

Encrypted clipboard frames are length-prefixed, max 1 MiB, and contain JSON `ClipboardPacket`. Binary `bytes` use base64. Packet kinds are `text`, `begin`, `entry`, `data`, `fileEnd`, `commit`, `cancel`, `done`, each with a UUID `id`. `begin` includes top-level `roots`, entry `count`, and total file bytes. Each `entry` carries a validated portable relative `path`, `size`, and `directory`; directories precede descendants. Non-directory entries are followed by bounded 192 KiB `data` packets and a SHA-256 `fileEnd`. `commit` requires exact entry/byte counts, completed hashes and all declared roots. Only then are cached local paths placed on the OS clipboard; `done` acknowledges actual clipboard publication. There is no remote shell invocation, archive extraction, direct overwrite of user-selected destinations, or remote deletion.

Receivers reject traversal, absolute paths, missing parents, duplicate/case-colliding paths, symlinks/reparse points, non-portable names and conflicting existing entries. Incomplete staging folders are removed on cancellation/disconnect; completed files persist so clipboard URLs remain usable after disconnect. Local clipboard sequence/change counters prevent echoes and protect a newer local copy from an in-flight older transfer. Copies made concurrently on both machines can resolve by arrival order; no distributed clipboard history is maintained.

Mac ordinary wheel events are line-based: three lines map to 40 logical scroll units, then 120 Windows wheel units. Continuous pixel events retain pixel-like logical units; fractional fixed-point deltas are carried instead of truncated. Both peers schedule pointer frames at 250 Hz and scroll frames at 125 Hz; pointer events are injected before the frame's scroll event. The Windows input queue also lets a newer pointer sample supersede an older one across queued wheel messages, while clicks and keys remain barriers. Windows has two receive modes: smooth mode spreads an impulse over eight display frames at up to 250 Hz using normal sub-detent wheel deltas, while native mode emits whole 120-unit detents at up to 125 Hz. Windows hover movement uses direct cursor positioning so wheel injection cannot hold the pointer behind it. Smooth mode can be disabled in the Windows connection card for applications that do not handle sub-detent wheel messages.

Development self-tests use loopback TCP 49334/49336 and UDP 49335, ephemeral identities and a test-only clipboard echo/cache. They do not inspect or replace the real system clipboard, capture input, or modify network settings.


## Touchpad feedback extension (build 30)

`touchpad-v1` plus `touchpad-native-receive-v1` enables normalized contact frames; `touchpadControl` carries each side's explicit opt-in. `touchpad` carries `phase` (begin/change/end/cancel), 2–5 unique contacts with IDs 0–4 and normalized finite coordinates; end/cancel have empty contacts. `gestureEpoch` is an optional nonnegative Int64 stroke identifier.

Both sides advertise `touchpad-feedback-v1` before sending `touchpadFeedback`. Fields: `gestureEpoch`, `frames`, `events`, `backend` (quartz/virtual-hid/synthetic-touchpad), `phase` (received/submitted/unavailable). Counters are bounded at 1e9. The receiver acknowledges actual receipt and native API submissions, never OS/application acceptance. Continuous feedback is throttled to four updates/second; begin/end/cancel bypass throttling. The source ignores acknowledgements from a different stroke or ownership context. Old peers may ignore the optional epoch field and receive no feedback messages. The current Mac backend is Quartz, not virtual-hid.


## Build 32 production behavior

Gesture forwarding is hidden and disabled. Both production apps advertise only clipboard-v1 and caps-v1 (the Mac login service still advertises only caps-v1). The old touchpad wire formats remain decodable for compatibility, but production input routing does not forward or inject touchpad messages. Saved experimental preferences do not enable reception.

Windows suppression is local and follows authenticated sharing plus Mac pointer ownership. It does not require peer touchpad capabilities or touchpadControl opt-in. Its private child-process configuration sets SuppressOnly=true; the child consumes gestures without reading contact frames or creating a synthetic touchpad, and returns only ownership/status diagnostics. This mode is not a new network protocol.


## build 34 游戏保护与相对运动

双端 display 协商 game-mode-v1。Windows 是游戏保护状态的唯一发布方：gameMode { down, phase, active:"windows", x, y }；phase 为 automatic/manual（down=true）或 off（down=false），x/y 必须为有限的 0..1 快照。Mac 仅在已协商能力的普通会话接收，原子更新锁定与 Windows 位置；off 在当前 Mac 所有权下不改变所有权。Mac 的 gameModeRequest { down } 请求由 Windows 输入线程处理，开启只允许当前控制 Windows。登录前会话不协商此功能。

Mac 在 Windows 游戏锁定下发送 relativePointer { dx,dy }，有限绝对值不超过 1,000,000；不计算或发送虚拟跨屏坐标。Windows 仅在协商能力、Windows 活跃且已锁定时注入相对鼠标；其他时段丢弃。连续相对采样相加，按键/点击/模式变化仍为顺序屏障。解锁清掉待发旧模式采样与未注入移动并使用当前 Windows 坐标同步，断线清除全部游戏状态。


## Clipboard image extension (build 37)

The clipboard channel hello/challenge exchange advertises `clipboard-image-v1` in `features`. Send image payloads only to a peer advertising this feature; older peers retain text/file support. An image reuses the bounded file stream with `begin.imageFormat="png"`, exactly one root and one regular entry, 1...134217728 encoded bytes. It then uses the same entry, 192 KiB chunks, SHA-256 fileEnd and exact commit. Receivers reject unknown image formats, empty/oversized images and directory entries. The completed result retains image semantics instead of becoming a file-only clipboard. Decode and verify dimensions before modifying the clipboard (maximum 32768 per dimension and 100 million pixels). Native Mac publication includes PNG and TIFF; Windows includes Bitmap, lossless PNG and a file-drop path for Explorer. Sender conversion and receiver decoding run outside input/UI dispatch, with revision checks before publication. A newer local copy still wins; locally received contents are not echoed. Existing file URLs/drop lists take priority over image previews, preserving ordinary file copy semantics.


## Windows build 39 two-finger scroll while suppressing local gestures

The existing suppress-only worker still owns three-or-more-finger global gestures without reading contacts or injecting a device. Its foreground input window now passes two-finger WM_POINTER lifecycle messages to DefWindowProc, as described by [Microsoft](https://learn.microsoft.com/en-us/windows/win32/input-precisiontouchpad/registertouchpadcapable). Windows recognizes the gesture and promotes ordinary WM_MOUSEWHEEL/WM_MOUSEHWHEEL messages; the window consumes these locally and publishes a private IPC scroll packet with the current ownership epoch. It does not pass scrolling to Windows applications. The parent accepts it only during confirmed capture and authenticated Mac ownership, accumulates fractional deltas with the existing input frame timer, and sends existing scroll messages to the Mac. No new network capability or Mac version is needed.

The device-less raw compatibility wheel remains suppressed only during confirmed native ownership, avoiding a duplicate of the worker scroll; physical-mouse raw wheel remains unchanged. Only consecutive scrolls of the same epoch/modifier/direction can sum, preserving displacement, direction reversals and ownership barriers. A newer configure clears queued old scroll, and packets from an old epoch cannot reach the parent router. The private Ctrl marker distinguishes default pinch-to-Ctrl-wheel from genuine held Ctrl scrolling; it is stripped before network forwarding. Native gesture mapping remains hidden.

## Windows build 40 correction after hardware feedback

Build 39 did not restore two-finger scrolling on the tested Windows 26300 machine, despite confirmed native foreground ownership. In suppress-only mode, the foreground input window no longer registers for two-finger pointer capture; only the separate global-gesture controller owns three-or-more-finger gestures. Windows keeps its default two-finger recognition. Zero-device raw wheel reports are now accepted through the existing raw mouse path rather than discarded merely because the worker is owned. The physical-mouse path and local legacy-event suppression are retained. The worker window still consumes any wheel delivered to it; its private scroll channel remains available for messages produced directly inside that window.

Event counters, without input values or text, are written once per second off the input thread to `touchpad-scroll-main.log` and `touchpad-scroll-worker.log` in the normal Windows Diagnostics directory. They distinguish raw touchpad/mouse wheel, low-level wheel, window/global pointers, window wheel, worker accepted/rejected scroll, and frames submitted to the existing send callback. They are pipeline diagnostics, not proof of network delivery or macOS/application acceptance. See [RAWINPUTHEADER](https://learn.microsoft.com/en-us/windows/win32/api/winuser/ns-winuser-rawinputheader) for valid zero-device precision-touchpad input.
