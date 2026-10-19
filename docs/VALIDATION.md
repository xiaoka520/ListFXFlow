# 验证记录 · 0.1.0–0.2.1

日期：2026-10-17，Asia/Shanghai。

## 已完成

| 检查 | 结果 | 证据边界 |
| --- | --- | --- |
| Swift 核心测试 | 9 项通过 | 四方向跨屏与返回、错位边缘、按键保持、超大移动、帧拆分/合并、认证/防篡改/重放、字段验证、键位映射。 |
| Windows 核心与跨语言测试 | 23 项检查通过 | 实际使用 Windows 项目的 Wire、Topology、Transport 源码在 Mac 的 .NET 10 运行；不能证明 Win32 输入接口在 Windows 上的效果。 |
| Swift ↔ .NET 网络互通 | 通过 | 真实 UDP 自动发现、TCP 连接、P-256 ECDH、双方认证、首次信任保存回调、七类加密消息双向传输。网络是本机 loopback。 |
| 无效认证 | 拒绝并断开 | TCP 逐字节拆分握手正常；错误认证不能建立会话。 |
| 心跳超时 | 通过 | 已认证但不再发送数据的客户端在约 3–4 秒后被 Swift 服务端关闭；使用单调时钟。未注入系统输入。 |
| Windows WPF 发行构建 | 通过，0 警告、0 错误 | 在 macOS 使用 .NET 10.0.402 交叉编译，产物为自包含 Windows x64 PE GUI 程序；未在 Windows 启动。 |
| Mac 发行构建与签名验证 | 通过 | Apple Silicon app，ad-hoc 签名；非 Developer ID 公证包。 |
| Mac UI 实跑 | 通过 | 实际启动打包 app，主界面完整，左右排列按钮改变屏幕顺序，可恢复右侧布局；已保存 `artifacts/Mac-UI.png`。未授权全局输入或启用真实共享。 |
| 本机雷雳接口只读检查 | bridge0 存在、active | 两个 IPv4 地址为 192.168.3.1 与 10.0.0.2；实现监听所有桥接 IPv4 并按对端子网回复发现请求。没有更改网络配置。 |

## 复现

```sh
DEVELOPER_DIR=/Applications/Xcode.app/Contents/Developer swift test
zsh scripts/test-interop.sh
zsh scripts/build-mac.sh
# Windows 上构建免安装 ZIP 与安装包：
./scripts/build-windows.ps1
# 在 macOS 上只交叉构建 Windows 主程序（不能生成安装包 UI 验收）：
dotnet publish windows/ListFXFlow/ListFXFlow.csproj -c Release -r win-x64 \
  --self-contained true -p:PublishSingleFile=true \
  -p:IncludeNativeLibrariesForSelfExtract=true -o artifacts/Windows-x64
```

互通脚本启动 Mac app 的显式 `--transport-self-test` 模式，仅监听 loopback，使用临时身份，不读写正式配对数据，不捕获或注入键鼠。脚本结束自动关闭测试服务。项目内 `.tools/dotnet` 是本次验证下载的本地构建 SDK，不属于发行包。

## Windows 设备 与 Mac 真机验收仍待进行

1. Windows 设备 Windows 11 目标版本、Thunderbolt 4 网卡与线材建立真实 IP 链路；自动发现以及正常防火墙条件下的连接。
2. macOS 用户授权辅助功能与输入监控后，真实 CGEventTap 捕获、屏蔽、注入与 Windows 低级钩子 / SendInput 的双向联调。
3. 键鼠均接 Windows、均接 Mac，以及鼠标/键盘分接两台设备三类组合。
4. 四方向跨屏、错位边缘、不同分辨率与 DPI；在目标系统中文输入法中输入；双击、拖动、滚动、修饰键、重复按键。
5. 按住修饰键和鼠标按钮、拔线、进程退出、锁屏、休眠、权限撤销等情况下，两端恢复本机控制并释放按键。
6. Windows UAC 与管理员窗口、macOS Secure Input 等系统限制的实际提示与停止行为。
7. 两端长时间运行与高轮询率鼠标下的延迟、CPU 使用和稳定性。

本机协议测试、成功交叉编译与 Mac UI 可见均不能替代上述硬件验收。

## 0.1.1 修正验证

- 修复两项权限申请被串行短路的问题；新增独立授权入口、当前应用路径、返回应用和定时刷新、未齐备时禁用启用按钮、重新打开应用与旧授权记录排查提示。
- 12 项 Swift 测试通过，其中 3 项验证输入监控在辅助功能未授权时仍可申请、异步授权后刷新/撤销，以及已授权时不重复弹窗。
- .NET 核心与 Swift 互通共 35 项检查通过，包含手动 IPv4 校验及跳过 UDP 发现后的真实 TCP 加密通信。
- Windows WPF 及 x64 自包含包交叉构建通过，0 警告、0 错误。所有图标改为嵌入矢量绘制；选择框、下拉列表、悬停/选中项、滚动条、输入框与标题栏使用显式深色样式。Windows 原生界面的实际显示仍待 Windows 设备 上复核。
- Mac 0.1.1 应用实际启动并确认滑块已移除。实际打开权限向导：两项未授权状态分别可见，两个「去授权」入口均可用，「启用共享」正确禁用。测试没有代替用户授予系统权限。
- 本次 Mac 包使用本机已有 Apple Development 身份签名并通过签名校验；后续使用相同身份构建，以避免 ad-hoc 更新导致身份变化。没有修改或重置系统隐私授权记录。
- 后续实跑观察到用户完成授权后，向导分别自动刷新为「辅助功能已开启」「输入监控已开启」；再次更新相同开发签名的构建后，两项授权仍被正确识别，「启用共享」恢复可点击。本次验证未自动启用真实键鼠共享。

## 0.1.2 授权拖动小窗口与排列简化

- 两端移除上下左右排列按钮，拖动屏幕完成位置调整，保留居中对齐。
- Mac 打开辅助功能或输入监控设置时，创建不抢焦点、切换应用后仍显示的小浮窗，显示当前运行的 app 图标和目标权限名称；尝试定位在系统设置旁，空间不足时置于屏幕边缘。
- 图标使用 AppKit 原生 NSDraggingItem / NSDraggingSource，拖动数据为当前 Bundle 的真实 public.file-url；只允许 copy 操作，取消拖动时图标返回。
- AppKit 独立拖动数据校验通过：将 NSDraggingItem.item 写入独立测试粘贴板，可读取匹配当前打包 .app 的 public.file-url。没有使用通用剪贴板或向系统设置执行授权拖放。
- Mac 与 Windows 自包含包构建通过；现有 12 项 Swift 测试通过。UI 改动后的编译通过，实际向系统权限列表拖放仍待用户操作确认。
- 开发期间观察到旧版已连接 Windows 设备，因此 Mac 新版构建在 artifacts/Mac-0.1.2/ListFXFlow.app；没有替换、关闭或重新启动正在共享的旧版。

## 0.1.3 自动启动与输入延迟修正

- Mac 每次启动 App 自动开启共享；权限未齐备时显示授权向导，雷雳网桥未就绪时等待。两项条件满足后自动继续；手动停止后本次运行不会自行重启共享。
- Mac 控制权切到 Windows 时使用透明前台捕获窗口，分离物理鼠标与本机指针并隐藏光标；返回、断线或停止时恢复关联、光标与原前台应用。该修正针对鼠标接 Mac 时两端指针同时移动的问题，实际跨设备行为仍需复测。
- Windows 低级钩子、消息处理及 SendInput 迁入独立输入线程；网络连接与解码离开 WPF UI 线程，光标遮罩改为预先建立的原生窗口。连续同一焦点的指针更新可以合并，按钮、键盘及焦点变化保持顺序。
- 12 项 Swift 测试及 42 项 .NET 核心/跨语言互通检查通过。新增检查涵盖 10 万条指针突发后的点击、拖动释放、移动与按下/抬起顺序、焦点及键盘屏障、队列溢出与断线清空。
- Mac arm64 与 Windows x64 自包含发行构建完成；Windows 构建无警告、无错误。Mac 使用与前版相同的 Apple Development 身份签名。
- 已退出 0.1.2 并实际启动最终路径的 0.1.3 App；没有点击启用按钮，界面即显示「等待设备」「等待 Windows · 雷雳直连已就绪」及「停止共享」，验证启动默认共享生效。旧包和中间构建移入系统废纸篓，artifacts 只保留新版 App、双端 ZIP 与校验文件。
- 互通测试仅在 loopback 传输协议消息；没有更改系统网络配置。Windows 设备 上首次点击延迟、真实 CGEventTap/SendInput 跨屏与长时间高轮询率输入仍需两端更新后验收。

## 0.1.4 Windows 指针停留与隐藏

- 移除反复将指针移至屏幕中央的逻辑。Windows 本机鼠标跨屏时指针停在实际离开位置；Mac 鼠标引发跨屏时，根据最后一个 Windows 坐标与第一个 Mac 坐标计算边缘交点，避免快速跨屏时停在屏幕内部。首次连接未有 Windows 移动样本时保留当前位置。
- 原生遮罩窗口注册 RIDEV_INPUTSINK，在独立输入线程处理 WM_INPUT。控制 Mac 时屏蔽本机传统鼠标事件，通过原始设备相对移动更新远端指针，避免屏幕边缘限制继续向外移动；按钮与滚轮在同一消息中按移动、按钮、滚轮的顺序处理。
- 展示遮罩时立即 SetCursor(NULL)，同位置发送带标记的更新让系统重新命中遮罩；返回、停止、断线或销毁遮罩时恢复指针。不修改系统光标主题或全局显示计数。
- 55 项 .NET 核心检查通过，新增 x64 原始输入 ABI 布局、相对移动、绝对输入独立基线、时间戳回绕、四方向停留后继续移动及跨屏返回检查；快速跨屏的边缘交点另有五项检查。
- Windows 0.1.4 x64 自包含发行构建通过，0 警告、0 错误；Mac 后续构建的版本标签改从 Bundle 读取，Swift 编译通过，现有 Mac 0.1.3 运行包保持原样。
- Mac 输入逻辑与协议未变，兼容现有 0.1.3 Mac；本次无需重启正在运行的 Mac 共享。Windows 实际隐藏显示、原始输入送达与停留位置仍需 Windows 设备 真机复核。

## 0.2.0 排列、图标、滚轮与文件剪贴板

- 产品界面移除设备品牌名，屏幕卡片改为「本机 / 远端」。Mac 连接状态同样使用远端称呼。
- 两端排列更新仅重建逻辑坐标，保存当前活动设备与屏内归一化位置，不再 reset 或发送回中间的指针消息。涵盖两台设备、四个方向及错位排列的回归检查通过。
- Windows 矢量图标按包含笔画的真实边界等比缩放并留出边距；屏幕卡片内容使用 Viewbox，减少排列方向/卡片大小变化时的裁剪。WPF 编译通过，Windows 原生渲染仍待目标设备复核。
- Mac 普通滚轮改为三行对应一格 Windows 滚轮，FixedPt 字段以浮点读取、像素字段以整数读取；Windows 保留不足一个滚轮单位的余量。核心换算检查通过，实际鼠标手感仍待 Windows 端复测。
- 20 项 Swift 测试通过；.NET 核心与真实 Swift 网络互通共 94 项检查通过。
- 独立加密剪贴板 TCP 连接已完成 Unicode 文字往返、1.2 MB 随机文件分块往返与内容一致性、多选顶层文件、空文件和空目录、错误会话认证拒绝，以及发送文件中再次复制文字取消旧任务的检查。
- 测试覆盖路径穿越与不可移植名称拒绝、缺少父目录/重复项目、部分文件不可提交、SHA-256 错误不可提交、断开清理临时文件、已完成文件在断开后仍可用。修复了 macOS `/var` 与 `/private/var` 路径别名导致的相对路径计算错误。
- AppKit 私有测试粘贴板实跑通过：多文件 URL、文件夹 URL 与 Unicode 文字可写入并读回；未读取或替换用户的通用剪贴板。测试中系统输出 sandbox_extension_consume 提示，URL/文字读写及断言通过；不据此推断跨应用授权或 Windows 原生粘贴已通过。
- macOS 与 Windows 0.2.0 自包含发行构建完成，无编译警告或错误；Mac 开发签名通过严格校验。新剪贴板功能需要两端均更新。没有修改网络配置或防火墙。
- 已退出旧 Mac App 并启动最终路径的 0.2.0；实际界面显示「Mac 本机 / Windows 远端」、剪贴板状态与自动启动的「等待 Windows · 雷雳直连已就绪」。最终 App 可执行文件与 Mac ZIP 内容一致；双端 ZIP 完整性与 SHA-256 校验通过。旧构建及中间目录移入系统废纸篓。
- 尚待真机验收：Finder ↔ Explorer 的实际复制/粘贴与剪贴板权限、Windows 图标完整显示、滚轮速度手感、双向长时间大文件传输吞吐，以及真实双机排列时鼠标位置不跳动。

## 0.2.1 后台运行、安装包与输入帧合并

- 指针与滚轮拆成不同帧率：两端指针按 4 ms（250 Hz）调度，网络滚轮帧限制在 8 ms（125 Hz）；指针总是先于同帧滚轮注入。Mac 在 4 ms 槽位空闲时直接从事件回调发出最新指针，不再只等主线程定时器。Windows 输入队列允许更新的指针覆盖较旧样本，即使两者之间已经排入滚轮消息；按钮、键盘、Caps 和布局变化仍是不可跨越的顺序屏障。Windows 悬停移动改用 `SetCursorPos` 直接移动系统光标（拖拽仍用 SendInput 保留按钮状态），绕开可能被滚轮消息占据的注入队列。Windows 滚轮接收提供平滑与原生两种模式：默认平滑模式把一个 Mac 齿格等步长分摊到 8 个显示帧，平滑时以 250 Hz（约 32 ms 完成一格）发送标准子齿格 wheel delta；连接卡片里的「平滑滚轮」关闭后回到 125 Hz、每帧最多一个原生 120 单位齿格。
- 滚动小数余量保留，不再因逐事件截断而丢失；连续触控板事件中的非零 FixedPt 小数也会保留。22 项 Swift 测试通过；.NET 核心检查 95 项通过（覆盖平滑模式逐帧分摊、距离守恒、原生齿格、进位、反向抵消、指针跨越滚轮队列的合并以及 Caps 消息校验），运行完整互通脚本时共 108 项检查通过。
- Mac 关闭主窗口后继续以 `LSUIElement` 状态栏应用运行；状态栏提供打开主界面、权限设置、开始/停止共享和退出。单实例使用 `flock` 加 `LSMultipleInstancesProhibited`：重复启动会退出第二个进程并请求已运行实例显示主界面。实测状态栏菜单项存在；关闭窗口后窗口数为 0、进程仍在；再次启动二进制退出码为 0，已运行实例重新显示 1 个窗口。实际截图因系统屏幕录制权限不可用而未生成。
- 两端都移除「居中对齐」和「忘记设备」入口；排列仍通过拖动 Windows 屏幕并保留错位偏移完成。Windows 排列区加入与 Mac 一致的点阵背景，主标题末尾的句号已移除。
- Mac 权限面板在已共享或已连接时显示「键鼠共享正在运行」和「完成」，不再显示「启用共享」；远端连接成功时也会自动关闭该面板。
- Mac 远控不再创建透明捕获窗口，也不再调用 `NSApp.activate`；`CursorCapture` 只做 `CGAssociateMouseAndMouseCursorPosition(0)`、尽力尝试 `CGDisplayHideCursor`（仅在本应用原本就在前台时才调用 `NSCursor.hide`），并用 8 ms 的 `CGWarpMouseCursorPosition` 定时器把可见指针锁在离开 Mac 屏幕时的边缘位置。整个路径不会激活应用或改变前台窗口。
- Caps Lock 改为状态同步而不是普通按键：物理按键先由本机系统处理，随后通过 `caps-v1` 能力交换状态，另一端设置相同的开启状态。macOS 上用带标记的 `flagsChanged` 事件设置 Caps，本地实测可切换并恢复系统状态；注入的键盘事件会带上 `.maskAlphaShift`。中文输入法仍会对合成事件输出小写，因此 Caps 打开且无 Shift/Ctrl/Option/Command 时，字母键还会把 `NSEvent.characters` 算出的大写字符写入 Unicode 负载，强制中文输入法插入大写字母。Windows 端在状态不一致时注入一次 Caps 按下/抬起。旧版对端不支持该能力时保持本地行为。
- Mac 构建产出 `artifacts/ListFXFlow.app`、`ListFXFlow-Mac-arm64-0.2.1.dmg` 与 ZIP。DMG 使用自定义点阵背景、开口箭头、固定的 Finder 窗口和图标位置，已实际挂载检查：包含 `ListFXFlow.app`、指向 `/Applications` 的符号链接、`.background` 与写入后的 `.DS_Store`，不再包含安装说明文本；卷图标为应用图标。应用 Info.plist 含 `LSUIElement` 与 `LSMultipleInstancesProhibited`，Apple Development 签名通过 `codesign --verify --deep --strict`，`hdiutil verify` 通过。DMG/ZIP 的 SHA-256 见 `artifacts/SHA256SUMS.txt`。
- Windows 自包含 x64 单文件程序、便携 ZIP 和当前用户安装包构建通过，WPF 编译 0 警告、0 错误。安装包把 `ListFXFlow.exe` 嵌入自身；已从安装程序程序集元数据中读出内嵌 `ListFXFlow.Payload.zip` 与 `ListFXFlow.Payload.json`，其 SHA-256 与构建产物一致。安装包未做 Authenticode 签名，首次运行可能触发 SmartScreen。
- Windows 界面移除「Windows 位于 Mac 某侧」提示文字；FlowIcon 改为按显式 Width/Height 测量、按实际渲染尺寸居中缩放，并新增 16/20/24/32/40/48/64/96/128/256 十种尺寸的 Windows ICO，改善标题栏、任务栏、托盘和安装器的小图标清晰度。Windows 剪贴板同步完成状态改为「剪贴板已就绪」。上述 WPF 视觉效果无法在 macOS 上渲染确认，需 Windows 真机复核。
- Windows 主窗口默认在用户主动启动时显示；带 `--background` 的安装后启动在有保存配置时留在系统托盘并自动连接；关闭或最小化窗口转入托盘。Windows 托盘、安装、卸载、后台连接、WPF 点阵与图标渲染、Caps 指示灯以及滚轮/指针手感无法在 macOS 上执行，仍待 Windows 真机复核。
- 尚待双机真机验收：Windows 端滚轮手感和边滚动边移动鼠标、两端真实 CGEventTap / WM_INPUT / SendInput 行为、托盘和安装器在 Windows 上的显示、安装升级与卸载流程。

## 0.2.2 滚轮转发与 Mac 中文输入法 Caps 修正

- 对照 Deskflow 的原生滚轮转发方式（MSWindowsComputer / MSWindowsDesks）以及微软 WM_MOUSEWHEEL 说明后，移除 0.2.1 每格拆成 8 帧的滚轮动画。两种模式均最多按 8 ms 合并一次真实距离，不再生成没有新输入的动画尾巴；指针保持独立 4 ms 调度并先于滚轮处理。由目标应用处理自身的滚动动画。
- 原“平滑滚轮”更名为“精细滚轮”，默认关闭以使用原生 120 单位齿格；开启时只保留高精度小幅增量，不额外插值。已有选项值继续保存。小数跨空闲帧保留，余量不足一个注入单位时不持续唤醒计时器；合并的多个齿格一次提交，不再逐帧排队造成尾随延迟。
- Mac 远控期间取消每个物理移动回调中的同步 CGWarpMouseCursorPosition，保留脱离系统光标和 8 ms 的位置维护，降低高轮询鼠标占用输入线程的开销。
- 针对 Windows 键盘控制 Mac、中文输入法已有拼音候选时开启 Caps 的情形，移除 keyboardSetUnicodeString 强制字符补丁。新键盘通道通过 IOKit 发布物理 keyCode，修饰键和 Caps 使用包含正确 keyCode 的 NX_FLAGSCHANGED 更新驱动标志，普通按键由系统布局和 IME 翻译。Swift 负责状态和路由，小型 C 互操作层隔离旧版 IOHIDPostEvent API；驱动不可用时保留无强制 Unicode 的 Quartz 退路。原生回显按键码、事件类型、修饰键和重复标志匹配，防止焦点切换后回传；Windows Caps 物理重复按下不再反复切换状态。
- Swift 25 项测试通过，包含只构造、不注入的 HID 结构测试（Caps 开关、普通字母、Shift、重复和释放，以及无字符负载），并检查原生回显匹配/过期。跨语言互通脚本共 111 项检查通过；新增 1 万组移动与滚轮混合消息不扩张队列、保留滚动距离和点击/Caps 屏障，以及原生/精细滚轮无动画尾巴、正负小数累计检查。
- Mac arm64、Windows x64 自包含应用与 Windows 安装程序构建成功。Mac 开发签名严格校验通过，0.2.2 DMG 校验、挂载后 app 版本与签名、Applications 链接和图标布局元数据检查通过。DMG 元数据改为直接写入，不调用 Finder UI 脚本。
- 真实 Mac 输入法候选行为以及 Windows 滚轮/同时移动的流畅度仍待双机复测；结构测试、协议互通与交叉编译不视为这些体验已验收。

实现参考（只参考原生 API 路径，未复制 Deskflow 的实现代码）：
- https://github.com/deskflow/deskflow/blob/master/src/lib/platform/OSXKeyState.cpp
- https://github.com/deskflow/deskflow/blob/master/src/lib/platform/MSWindowsDesks.cpp
- https://learn.microsoft.com/en-us/windows/win32/inputdev/wm-mousewheel

- 0.2.2 Windows 安装器程序集内嵌 payload ZIP / JSON 读回检查通过，版本、长度和 SHA-256 与发布的 ListFXFlow.exe 一致；双端 ZIP 完整性、Mac ZIP 与 app 二进制一致性、Windows x64 PE 架构检查通过。已替换 `/Applications/ListFXFlow.app` 并实际重新打开，界面显示 0.2.2 与「等待 Windows · 雷雳直连已就绪」，两个 bridge0 IPv4 地址监听正常，旧 0.2.1 app 和安装包移入系统废纸篓。

## 0.2.3 固定精细滚轮、DMG 背景与中文输入法 Caps

- Windows 删除滚轮选项，统一保留小幅增量；旧 connection.json 中的 SmoothScroll=false 被忽略，手动 IP 和本地网卡地址继续读取。源端按最多约 4 ms 的窗口合并样本，Mac 槽位空闲时立即发送第一笔滚动；接收端收到真实滚轮增量后直接注入，移除两端串联的第二个滚轮等待计时器，不补动画尾巴。Mac 不足一个像素的小数继续累计，但不再持续唤醒输入计时器。
- Caps 增加中文输入源处理：Windows 控制 Mac 时，Caps 开启会临时选择用户已有的 ASCII 键盘布局；关闭、停止共享或离开 Mac 时，只有当前输入源仍是本程序临时选择的源才恢复原中文输入源，避免覆盖用户随后手动选择的输入源。没有启用或安装新输入法，也没有修改系统键盘设置。
- 普通字母改回由 Quartz 构造键盘事件，使用系统输入源翻译字符；IOHID 路径仅处理修饰键和锁定标志，避免零 charCode 的普通 HID 事件被输入法忽略。没有重新加入强制 Unicode 字符补丁。
- 使用临时原生编辑器窗口，通过与 Windows 消息完全相同的 InputEngine.receive 路径实测（界面按钮经 CUA 操作，无网络、键鼠捕获或剪贴板）：先在系统拼音输入法注入 nihao，AX 显示未提交的 ni hao；开启远端 Caps 后输入 abc，编辑器结果为 nihaoABC，输入源为 com.apple.keylayout.ABC 且 Caps=true；关闭 Caps 后输入 nihao，输入源恢复 com.apple.inputmethod.SCIM.ITABC，编辑器显示 nihaoABCni hao。测试结束恢复原输入源与 Caps 状态。此项验证覆盖本机真实 IME 行为，不等于 Windows 物理键盘与网络整链路已验收。
- Swift 26 项测试通过；.NET 核心 94 项检查通过，其中新增旧配置迁移及精细滚轮固定启用检查。完整互通脚本在加入两项配置检查前通过 105 项检查，涵盖 92 项核心检查及 13 项网络/剪贴板互通检查；新增两项检查随后随核心测试单独通过。移除了已不适用的原生整格/切换模式测试，测试总数变化不代表失败。
- 0.2.2 DMG 白色背景来自缺少现代 Finder 背景书签。0.2.3 保留图标布局和传统别名，并通过 NSURL 生成可解析的 pBBk 原生书签；封装为只读镜像后实际在 Finder 打开验证。最终窗口显示深色点阵背景、紫色箭头、两张浅色图标卡片与清楚的黑色文件名，底部显示拖入 Applications 的安装说明。已检查实际最终 DMG，不只检查背景 PNG 或元数据。
- Mac app、Mac DMG/ZIP、Windows x64 便携 ZIP 与当前用户安装程序构建成功；Mac 开发签名严格校验、镜像校验、双端 ZIP 完整性、Windows x64 PE 架构、安装器内嵌应用版本/长度/SHA-256 与发布应用一致性均通过。
- 已实际更新 /Applications/ListFXFlow.app 为 0.2.3 并重新打开，界面显示 0.2.3 与「等待 Windows · 雷雳直连已就绪」，两个 bridge0 IPv4 地址监听正常。旧版 app、DMG、ZIP 和安装器移入废纸篓；中间构建放在 .build/release-0.2.3。Windows 新版滚轮手感仍需安装后通过真实双机复测，Windows 托盘和安装器运行也仍需 Windows 环境验收。

## 0.2.3 build 10 — 输入队列拆分与待人工验收

- 保留 VERSION=0.2.3，增加 BUILD=10；macOS CFBundleVersion=10，Windows FileVersion=0.2.3.10。安装包文件名加入 b10，主界面显示构建号。
- Windows 的 SendInput/SetCursorPos 从捕获和路由的 Dispatcher 线程迁移至有界的独立注入线程；合并过时移动和同向滚轮，键盘/按钮维持顺序。取消旧焦点的待处理输入时保留全局 Caps 状态转换和必要释放。每五秒仅写入次数及耗时统计，不记录按键或位置。1 万组移动/滚轮、模拟阻塞的系统注入、点击顺序与 Caps 取消检查通过。该测试验证线程隔离，不能证明 Windows 真机手感。
- Mac 的连接状态、加解密、收发和消息解码改用独立高优先级串行队列。会话代际与有界投递阻止断线前排队消息进入下一会话。UI 和输入回调仍在主线程。停放光标的看门狗改为仅在实际漂移时回位，避免无条件每 8ms 同步调用 WindowServer。
- 移除临时切换 ABC/恢复中文输入源的 Caps 绕行。使用驱动锁定状态及原生按键字段，不向 Quartz 设置 Unicode 替代文本。此前逐步点击测试按钮会改变输入框焦点并打断 marked text，相关“混输丢失”判断不成立；同样不能据此宣布混输已修复。最终以用户真实连接、连续键盘操作的反馈为准，等待人工测试。
- Swift 28 项测试通过。此前本构建跨语言互通 114 项检查通过；这些检查不执行用户桌面输入。Windows 交叉构建无法替代目标设备验收。
- DMG 图标卡片改为背景模糊、半透明渐变与高光边缘的毛玻璃视觉；Finder 使用静态背景图，并非实时系统材质。新包校验通过，最终 Finder 外观尚待检查。

## 0.2.3 Mac build 11 — Caps 按键通道修订（待人工验收）

- 用户通过 Windows 键盘控制 Mac 实测确认：build 10 在中文输入法中不能输入大写，开启 Caps 会打断候选。这是实际失败，不能归因于测试按钮焦点。
- 移除 Caps 字母专用的 UCKeyTranslate/charCode 转发。普通键和修饰键统一经 IOHIDPostEvent；仅修饰键设置全局标志，普通键以 flags=0、options=0 继承驱动状态。保留无 Unicode 替代文本的 Quartz 失败退路。不切换输入源。
- Swift 27 项检查通过，包含普通键不重设驱动状态、修饰键转换、重复/释放与回显过滤。这不代表真实 IME 行为已验收。Mac 应用签名、DMG 校验和 ZIP CRC 通过，已更新 /Applications 应用。Windows build 10 无需更新。
- 参考 Deskflow 公开平台源文件中普通键与修饰键的 IOHIDPostEvent 选项区别，未复制其实现：https://github.com/deskflow/deskflow/blob/master/src/lib/platform/OSXKeyState.cpp 。

## 0.2.3 build 18 — 登录组件、指针停放与指定文案

- 按用户要求以磁盘上的 build 17 为基础，从 BUILD=18 开始；VERSION 保持 0.2.3。保留 build 17 的 Caps 路径，未重新加入先前的输入源切换或字符替代实验。
- 两端连接状态仅删除“剪贴板已连接”后面的“· 文字和文件”，其余现有界面文案保留。
- Mac 在 HID 事件层抑制本机鼠标移动，按实际共享边缘投影停放位置。WindowServer 的隐藏、分离和漂移修正集中到独立队列，回到 Mac 时按顺序恢复位置和指针；退出进程前同步释放。四边、负显示器原点和大幅越界的几何检查通过，真实鼠标行为尚待人工确认。
- Mac 增加 LoginWindow 专用代理和正常登录后的启动代理。Windows 增加 LocalSystem 服务、控制台会话监督进程和登录桌面输入进程，通过 Job Object 管理子进程。两端只接受已经配对的设备，登录界面不启动剪贴板；Mac 锁屏暂停剪贴板。Mac 非当前控制台用户会话停止转发，避免快速切换用户后继续控制后台会话。
- Swift 31 项测试通过；Windows 核心 108 项检查通过；跨语言互通共 122 项检查通过（包含核心检查），新增固定身份重连及拒绝不同设备检查。测试不执行桌面输入，不能证明登录、锁屏、Caps 或滚动体验已通过。
- Mac arm64、Windows x64 自包含应用和安装器构建成功。Mac 签名、DMG 校验、挂载镜像内 build 18 和签名、Applications 链接与背景元数据检查通过；ZIP CRC 通过。Windows 安装器程序集资源和最终 EXE 均含正确载荷，版本 0.2.3.18、长度、SHA-256 与发布应用一致，AMD64 GUI PE 检查通过。
- Mac 系统 PKG 已展开检查：载荷归属 root:wheel，内嵌应用签名通过，不包含设备身份或连接配置。pkgbuild 输出四条 write: Permission denied；空载荷的独立最小包同样复现，命令仍成功，生成包结构可展开验证。PKG 本身未使用发行安装器证书签名。
- 已更新 /Applications/ListFXFlow.app 为 build 18；实际界面显示已连接远端及“剪贴板已连接”。旧 build 17 应用和 Windows 成品已移入废纸篓，中间产物保留在 .build。主安装方式仍为 DMG，系统组件 PKG 内嵌于应用，仅在启用登录前共享时打开。
- Mac 组件安装器已打开至“安装”页面，尚未执行管理员安装。此步骤会增加登录页输入权限，需要用户本人完成系统授权。Windows 新包尚未在目标机器运行；LoginWindow、Winlogon、锁屏和 TCC / Secure Input 尚未完成真机验收，不包括 FileVault 解锁前。启用与移除方法见 LOGIN-SUPPORT.md。

## 0.2.3 build 20 — 移除 Windows 登录共享、保存地址优先与网桥恢复

- 按用户要求删除 Windows 登录共享按钮、服务、监督进程、登录桌面工作进程及机器范围身份加密代码。只保留迁移清理入口：检测到旧组件时请求 UAC，停止并删除旧服务、配置和组件目录；没有安装过旧组件则不请求管理员权限。Windows 仍在锁屏时停止共享、解锁后恢复。迁移清理尚待 Windows 真机执行确认。
- 本次重启后的实际观察：系统保存的雷雳地址为 10.0.0.2，但互联网共享启动后 bridge0 的实际地址为 192.168.3.1。用户关闭雷雳互联网共享后，桥上曾暂时没有 IPv4，随后恢复 10.0.0.2，应用与 Windows 10.0.0.3 的 TCP 连接恢复。没有通过程序强行添加 IP、修改防火墙或改写系统网络配置。
- 修复 Mac 监听成功后停止观察地址的缺陷。现在持续检查网桥的地址及掩码，地址消失时释放输入、等待地址恢复；失效地址和异步监听失败会自动重试。过期监听器的回调不能取消新监听。用户主动停止后，即使网卡恢复也不会自动重启共享。
- Mac 记住认证成功使用的本机地址，存在时优先广播该地址，不存在则使用实际可用且与对端同网段的地址。Windows 保存手动 IP、网卡和认证成功的远端地址；优先直连，TCP 不可达或超时后回退到雷雳广播发现，旧地址不在当前子网时直接发现。认证身份校验保留，未通过认证不会更新成功地址。
- 不为优先地址重新出现而主动中断仍然可用的连接；在连接丢失或当前地址消失后，重新按保存地址的优先顺序选择。
- Swift 34 项测试通过，覆盖地址优先、启动等待、地址变化、活动连接保留、失败退避和手动停止。Windows 核心 105 项检查、Swift/.NET 互通共 121 项检查通过；其中两个真实 TCP 用例验证保存地址直接认证，以及不可达保存地址回退到发现并认证。移除 Windows 登录测试后总数下降，新增测试不操作用户桌面输入。
- Mac arm64 应用、DMG/ZIP、Windows x64 自包含应用、便携 ZIP 和安装器已构建。Mac 严格签名和 DMG 校验、ZIP CRC、Windows AMD64 GUI PE、安装器程序集及最终 EXE 的载荷版本/长度/SHA-256 与发布应用一致性检查通过。版本保持 0.2.3，最终 BUILD=20。
- 已更新 /Applications/ListFXFlow.app 和受保护的 Mac 登录组件为 build 20，安装器显示安装成功，磁盘版本检查一致；实际连接为 10.0.0.2 → 10.0.0.3，本机 lastBridgeAddress 保存为 10.0.0.2。尚未再次重启进行登录窗口验收，也未在 Windows 执行新版安装器、UAC 清理或真实登录测试。

## Mac 0.2.3 build 21 — 对照 ThunderDisplay 修正登录窗口生命周期

- 用户重启后反馈登录前无法连接。本次从真实系统日志确认：15:59:22 登录窗口代理以 root、PID 295 启动，辅助功能及 ListenEvent 预检允许；16:00:47 登录交接时退出，并非没有被 launchd 启动。
- 经系统管理员授权导出的组件状态日志直接给出故障顺序：等待网桥 → 显示器配置已变化，请重新启用共享 → 共享已停止。configd 日志显示 bridge0 到 15:59:57 才设置 10.0.0.2，晚于启动期间的多次显示器重配。桌面模型的显示器通知错误地取消了登录代理的自动启动意图，后续地址出现也不再重试。诊断证据保存在 .build/login-boot-b20-diagnostics.txt 和 .build/login-boot-b20-unified.log；未导出配对配置、输入或密码。
- 按用户要求，直接对照当前 ThunderDisplay 的 LoginWindowHost、LoginWindowInput、HIDBridge、LoginWindowState 和启动 plist。新增独立 PreLoginService，不再创建桌面 FlowModel、剪贴板和权限面板。核对 root / LoginWindow 上下文，持续等待网桥和有效显示器；显示器初始化仅更新几何，不执行用户停止。真实 NWListener ready 回调才发布监听就绪。
- 迁入登录前 Mach-O Quartz 标记、会话鼠标投递与键盘 session tap 路径，仍检查事件发送及监控权限；正常桌面保持现有 HID/Caps 路径。登录前不初始化全局键盘状态源，也不在启用时同步查询全局 HID 按键状态。启动系统调用有超时看门狗；临时输入服务故障可重试，用户紧急停止仍保持停止。
- 增加 root 所有、0644 的运行状态心跳，仅包含阶段、时间、PID 和退出前原因。历史记录与实时新鲜度分开，普通应用通过“登录前共享状态…”或 --login-status 只读查看，不需要再次读取 root 私有日志。确认用户登录后释放旧输入/端口，按配置的 UID 请求已有 Aqua 启动任务；全局启动代理增加用户归属检查。
- Swift 37 项检查通过，包含缺少 console 用户的登录判定、退出原因跨交接保留、历史状态不冒充实时状态，以及无效会话鼠标事件不投递输入。Swift/.NET 121 项互通检查通过。生产应用和系统 PKG 内的应用均检查到 Pre-login marker=true；普通 Aqua 用户直接运行 --login-window 被拒绝，退出码 1，没有建立输入捕获或注入。
- Mac app、DMG、ZIP 构建成功；开发签名严格校验、镜像校验、ZIP CRC、安装器脚本和 plist 检查通过。版本保持 0.2.3；本轮只更新 Mac build 21，Windows 继续使用兼容的 build 20。
- Mac 桌面应用及系统登录组件均已更新为 build 21，安装器显示成功，磁盘版本、开发签名和启动用户参数 501 检查通过；桌面应用实际重新连上 Windows。登录前真实连接与输入需要本次新构建安装后再次重启确认，不能由桌面连接、结构测试或打包检查代替。


## Mac 0.2.3 build 22 — 后台指针冻结与边缘停放

- 修复 CursorCapture 把一次 CGAssociateMouseAndMouseCursorPosition 成功当作持续冻结保证的问题。参考 Deskflow 当前 OSXComputer::hideCursor / leave 的后台连接属性，动态解析可用的 WindowServer 符号并设置 SetsCursorInBackground，先允许后台控制、隐藏，再断开鼠标与指针关联，不激活应用、不改变输入焦点。此属性属于非公开 SPI；若系统不提供对应符号，继续执行边缘停放与漂移修复。
- pinIfNeeded 不再为空：物理移动只写入合并标记，独立停放队列每 8 ms 处理一次。即使位置快照暂时仍在锚点，只要有新移动也会执行停放；检测到漂移时重新申请后台控制和冻结。WindowServer 调用不在原始鼠标事件回调中执行，1000 次移动不会产生 1000 次同步停放。
- 返回本机、断线和退出按同一队列顺序取消定时器、恢复关联和可见性；已结束捕获的旧失败回调不能停止新的捕获。隐藏调用保持平衡，沿用原来的四边锚点及跨屏位置，不改 Caps、输入法、滚动协议或产品文案。
- Swift 40 项检查通过，其中新增 3 项使用可注入的 WindowServer 驱动验证后台调用顺序、漂移后重新冻结、返回后不再执行旧停放、移动合并、过时位置快照和旧失败回调隔离。测试不会向用户桌面发送鼠标或键盘事件。Swift/.NET 互通 121 项检查通过。
- Mac build 22 应用、DMG 和 ZIP 已构建，严格签名、镜像校验、ZIP CRC 及系统 PKG 中应用签名通过；生产应用及内嵌组件均保留登录前 Quartz 标记。Windows 保持 build 20，不需要为此次 Mac 指针修复更新。
- 本次读取到上次启动的 root 状态：PID 296 退出前为 connected（已连接配对设备），记录时间 2026-10-18T08:29:51Z，确认 build 21 曾在登录窗口连上远端。该记录不能证明输入手感和每次停放通过；build 22 仍需真实双机物理鼠标验收。
- 已替换 /Applications/ListFXFlow.app 并确认界面显示 build 22、已连接远端；用户完成系统组件安装，安装器显示“安装成功”，受保护组件磁盘版本为 22，严格签名通过。旧 Mac build 21 成品已移入废纸篓，artifacts 仅保留最新 Mac build 22 和兼容的 Windows build 20；最终 SHA-256 全部匹配。

## Mac 0.2.3 build 23 — 登录前共享入口移入权限设置

- 将安装、更新、运行状态和移除统一放入主界面“权限设置”的“登录前共享”卡片，删除应用菜单中的对应入口。首次未安装显示“安装组件…”，已安装显示“更新组件…”；回到应用时刷新安装状态。首次配对要求、配置导出、管理员安装流程及登录输入行为保持不变。
- 更新使用文档中的入口。版本保持 0.2.3，仅 Mac BUILD 增至 23，Windows 仍为 build 20；本次不更改鼠标、滚轮、Caps 和网络逻辑。
- Mac arm64 构建成功，应用与内嵌组件严格签名、DMG 校验、ZIP CRC 及最终 SHA-256 检查通过。此 UI 调整通过实际界面验收，没有新增镜像式 UI 单元测试或重复执行未变更的协议测试。
- 已更新 /Applications/ListFXFlow.app 为 build 23，实际打开权限页并检查完整卡片布局、已安装状态和管理按钮；点击“更新组件…”成功打开应用内置的系统 PKG 安装器，随后关闭安装器返回权限页。实际菜单只保留关于、隐藏和退出，不再提供登录组件入口。本次未执行管理员安装，已安装的 build 22 登录组件无需为此桌面 UI 调整重装。
- 旧 Mac build 22 成品已移入废纸篓，artifacts 仅保留 Mac build 23、Windows build 20 和校验文件；新应用保持已连接远端。

## Mac / Windows 0.2.3 build 24 — 可选原生触控板手势通道

- 按用户补充，传递多指输入及连续阶段，不用按键组合代替手势。新增 touchpad-v1 / touchpad-native-receive-v1 能力协商、touchpadControl 双端开关协商，以及 touchpad 触点帧。坐标归一化、稳定 ID、最多五个有效触点，严格验证重复 ID、非有限数、坐标范围和 begin/change/end/cancel 阶段。旧版没有宣告能力时不发送新消息，普通键鼠仍可连接。
- 默认关闭且保存选择；Mac 开关位于权限设置，Windows 位于主界面。只有两端同意、光标控制远端、会话未锁定时生效。设置关闭、控制权变化、断线和锁屏会结束/取消手势；新的控制端不会从已按住的触点中途开始。手势帧是鼠标位置合并的顺序屏障。
- Mac 从 Quartz 通用手势事件的 Cocoa 间接触点中取得原始位置、方向与接触阶段。Windows 解析标准 Precision Touchpad HID 报告，按扫描时间组装并过滤非接触/低置信触点；不更改驱动模式、注册表或系统手势设置。默认关闭时保留原始鼠标消息的单次读取路径。
- Windows 接收动态检测 CreateSyntheticPointerDevice2，创建 PT_TOUCHPAD + SDCO_PHYSICAL_SIZE + SDCO_TOUCHPAD_GESTURE_ONLY 设备，使用 InjectSyntheticPointerInput 注入触点，由系统识别手势。手势专用设备不能制造单指指针移动或点击。注入在独立线程执行，更新可合并，开始与结束不丢弃；失败仅关闭手势通道并提示原因，不停止普通键鼠。实际系统未导出接口时不宣告接收能力。
- Mac 接收从触点识别连续平移、缩放、旋转和多指 Dock 滑动，生成带阶段的原生 Cocoa/Quartz 手势事件；没有模拟快捷键。Mac 识别与事件格式采用 Quartz 的非公开手势字段，系统更新后的兼容性仍需核对。源端活动手势不再重复走旧滚动通道。
- Swift 44 项检查通过，其中新增触点校验、开关/控制权切换时的新手势门控、连续手势几何以及原生事件构造测试。直接用 NSEvent 读取构造出的 magnify / rotate 事件，确认类型、数值和阶段；测试不向用户桌面投递输入。Windows x64 原生结构布局、PT_TOUCHPAD 触点与释放帧、默认设置迁移和队列顺序检查通过。核心与 Swift/.NET 互通合计 137 项通过，真实 TCP 加密往返核对十类消息及触点 ID、坐标和三阶段保真。
- 双端应用、Mac DMG/ZIP、Windows 自包含便携 ZIP 和安装器构建成功。Mac 严格签名、DMG 校验、ZIP CRC、内嵌登录组件签名和 Quartz 标记检查通过。Windows app / Setup 均为 AMD64 GUI PE；安装器嵌入载荷的版本 0.2.3.24、长度与 SHA-256 和发布 exe 一致；最终 Setup exe 与构建输出完全一致。
- 当前阶段是实验功能。没有在实体 Windows 26H2 上调用新注入接口，也没有用实体触控板验收双向手势。Windows Raw Input 的后台监听不能保证阻止三指/四指同时触发来源电脑的全局操作，且部分驱动可能不提供标准触点；没有通过改变前台窗口或系统配置绕过此限制。轻点、按压、力度与触觉反馈不在本轮范围。方向、惯性、旋转、全局手势、应用响应及驱动兼容性仍待双机验证。
- 依据：Microsoft Precision Touchpad Programming Guide、CreateSyntheticPointerDevice2 / SYNTHETIC_DEVICE_CREATION_PARAMS / POINTER_TYPE_INFO；Mac 参考 Hammerspoon 的 eventtap / TouchEvents 原生字段及 InstantSpaceSwitcher 的 Dock swipe 阶段。实际运行只连接用户已配对的雷雳远端，结构测试和协议测试不等同目标设备验收。

- 最终已安装 /Applications/ListFXFlow.app build 24，权限页实际确认手势卡片完整显示，开关切换与状态刷新正常；保留用户已有的开启选择，新安装/无旧设置时仍默认关闭。当前远端旧版本时显示“远端版本不支持，请更新两端应用”，未向旧端发送新协议消息。最后一次检查 Mac 监听已就绪、等待远端；尚未完成新版 Windows 安装及原生手势硬件验收。
- 修复空闲控制权切换不应吞掉第一次手势的问题，并检查活动触点仍需先抬起；Windows 取消帧携带 POINTER_FLAG_CANCELED。Windows 安装器、便携 ZIP 与最终发布 app 的载荷一致；旧成品已移入废纸篓，artifacts 仅保留 build 24。已安装的 build 22 登录代理不参与此次桌面手势实验，无需为此重装系统组件。

## Windows 0.2.3 build 25 — 接管本机触控板手势

- 用户确认 build 24 的 Windows 触控板手势仍作用于来源电脑。根因是 Raw Input 只观察触点，不消费 Windows 的精密触控板识别和 shell 全局手势；低级鼠标钩子不能覆盖所有手势。
- 新增 TouchpadGesturesController WinRT 原生 ABI 包装，按 Microsoft windows.ui.input.idl 的 UniversalApiContract 19.0 接口与 GUID 运行时激活、检查 IsSupported。声明消费三/四/五指动作及连续手势；无需新增 SDK、驱动、系统组件或更改触控板设置。该控制器只在调用进程为前台时路由全局手势。
- 两端开关同意且光标在 Mac 时，临时启用现有输入覆盖窗口的前台接管，并用 RegisterTouchpadCapableWindow 接收双指触控板消息；这些消息不再交给 DefWindowProc 转回本机滚轮，旧 WM_GESTURE / 滚轮 / APPCOMMAND 也在接管时消费。原始 HID 触点继续按原协议发给 Mac，源端只有确认接管成功才转发。
- 返回 Windows、停止、断线或关闭开关时禁用控制器、注销触控板窗口、释放手势并恢复被替代的原窗口。原窗口已关闭或用户已主动切换到其他窗口时不抢回焦点；逐帧确认前台进程仍属本应用，失去所有权时取消并暂停转发，显示具体提示。接口缺失或前台操作被拒绝时显示接管失败，不伪装成成功监听。
- 新增 10 项检查，覆盖重复接管/释放、精确恢复窗口、外部前台变化、原窗口关闭、权限拒绝、部分激活失败，以及 WinRT HRESULT 映射为托管异常时的安全降级。Windows 核心 130 项检查通过，含真实 TCP 的 Swift/.NET 互通共 147 项检查通过。WPF 应用构建通过，0 警告、0 错误。此轮未改 Mac 源码及协议，不重复运行无关的 Mac 单元测试。
- 保持 VERSION=0.2.3，仅 Windows build 增至 25；Mac 继续用 build 24，登录组件不变。新的 Windows 程序、便携 ZIP 与安装器已构建，安装器载荷版本 0.2.3.25、尺寸和 SHA-256 与发布应用一致。Windows 原生控制器、实际手势拦截、窗口激活权限和输入法状态仍需在用户 Windows 笔记本验收；本机交叉构建与状态测试不代表硬件通过。
- 参考 Microsoft RegisterTouchpadCapableWindow / TouchpadGesturesController 文档及 win32metadata 中的 windows.ui.input.idl；窗口注册按名称解析，未导出名称时使用文档记录的 user32 ordinal 2689，仅在全局控制器 IsSupported 成功后调用。保持默认关闭、双端同意和会话退出清理。

## Mac / Windows 0.2.3 build 26 — 补齐接管后的手势链路

- 用户确认 build 25 能阻止 Windows 本机手势，但 Mac 无明显反应。审计发现控制器没有订阅 PointerPressed / PointerMoved / PointerReleased，仍依赖 Raw HID 旁听。现在订阅这三类事件，并从双指 WM_POINTER 与多指控制器回调读取 GetPointerFrameTouchpadInfo。依据 Microsoft IDL 和 WinRT 类型签名算法生成代理 IID，通过 CurrentPoint.PointerId 关联原生帧，不跳过控制器需要处理的指针消息。
- 使用 ptHimetricLocation 与设备范围归一化，不使用手势期间固定的鼠标坐标。整帧去重、32 位 ID 映射为稳定线缆 ID、过滤非接触和低置信触点；释放、取消和捕获变化结束手势。接管时只使用系统路由触点，不与 Raw HID 混流；读取失败释放接管并显示原因。原有前台租约与窗口恢复保留。
- 本机为 macOS 27.0.1。缩放/旋转此前只有表层 CGEvent 字段，现在构造 hand collection 和 vendor token，交给运行中系统序列化后附入事件。C 桥只创建对象和数据，不注册设备或向 HID 系统派发输入。macOS 27 的 Dock 手势附加 28 字节队列头、40 字节 fluid-touch 与可选 28 字节 velocity 数据，镜像阶段、进度、方向及抬手速度；取消不携带提交速度，较早系统保留旧 Dock 路径。
- 测试发现 CGEvent 重建会清除 eventSourceUserData，现于重建后恢复位置和标记，避免捕获回环。滚动补入浮点增量。Windows 状态显示等待触点或已转发帧数，Mac 显示收到几指及识别的手势；这些状态表示程序链路进度，不表示目标应用已经响应。没有模拟快捷键、注册虚拟触控板或修改系统手势设置。
- Swift 46 项通过，实际调用本机 IOHID 对象构造与序列化，核对 hand/vendor 类型、CGEvent 类型与阶段、回环标记、Dock 负进度位模式、载荷尺寸和抬手/取消速度。测试不向桌面投递事件。Windows 核心 138 项、含真实 TCP 的双端互通合计 155 项通过，新增八项覆盖物理坐标、32 位 ID、去重、触点重排、释放、取消、控制权重置及无效设备尺寸。
- VERSION 保持 0.2.3，双端 BUILD=26。Mac 应用与 PKG 内应用严格签名、DMG 校验、Mac ZIP CRC 和第三方许可检查通过；PKG 本身沿用未签名本地开发包。Windows 应用与安装器构建通过，嵌入载荷的 0.2.3.26 版本、尺寸及 SHA-256 与发布应用一致，便携 ZIP CRC 和文件一致性通过。已安装的登录组件不参与桌面手势，无需为此重装。
- Windows 26 原生订阅、实体触控板帧读取，以及 macOS 27 和具体应用对构造手势的接受情况仍待双机实测，自动检查不能代替硬件验收。只有 build 25 的本机拦截已获用户确认。Mac 此路径仍使用非公开事件字段。参考 Microsoft TouchpadGesturesController / GetPointerTouchpadInfo / WinRT type system、InstantSpaceSwitcher 和 macos-trackpad-companion；许可已随 Mac 应用分发，见 THIRD_PARTY_NOTICES.txt。

## Windows 0.2.3 build 27 — 手势故障隔离与连接回归

- 用户报告 build 26 在开启手势后无法连接，连接后开启会严重卡顿直至 Windows 程序崩溃。没有收到目标 Windows 的异常栈，最终崩溃点尚未确认。代码确认存在连接前同步初始化原生控制器、在普通鼠标钩子调用链中启用接管、在原生指针回调中同步撤销控制器等阻塞和重入风险；build 26 的跨平台数据测试不能证明这些 Windows 调用通过。
- 删除 build 26 新增的托管 WinRT TypedEventHandler CCW 和 PointerEventArgs/PointerPoint 手工回调解包。改为辅助进程内的线程局部 WH_GETMESSAGE 观察器：仅在 PM_REMOVE 时复制当前触点帧，不修改消息、不跳过控制器正常派发。原始触控板 HID 解析退出运行路径，旧解析代码仅归档为 docs/references/PrecisionTouchpad-b24.cs.txt。
- 同一 Windows exe 新增隐藏 --touchpad-worker 模式，在普通单实例和登录清理流程之前分流。原生控制器、触控板窗口和合成触控板设备均只在辅助进程运行。主程序的开关、连接和鼠标钩子仅异步提交配置；认证和两端开关同意后才启动辅助模式，控制 Mac 时才初始化捕获。普通鼠标路径保留单次 Raw Input API 读取，不注册触控板 HID。
- 随机名称、当前用户限定的本地管道传输经过验证的触点消息。IPC 帧限制 8192 字节，消息队列限制 128 项，同指连续更新合并，阶段和配置作为屏障；主输入分批最多处理 64 项。控制权/设置变化更新 epoch 并丢弃旧帧，防止关闭后或重新开启时误发旧触点。
- 辅助进程 STA 心跳每 250 ms 产生，3 秒不响应暂停手势；原生注入超过 1.5 秒未返回也报告失败。停止请求异步提交，最多等待 0.5 秒后结束辅助进程。原生错误撤销排入消息循环，避免在自己的消息回调内拆除控制器。失败不停止普通键鼠连接、不自动循环重启；关闭再开启开关才能显式重试。辅助进程监视父进程退出，防止主应用退出后残留。阶段与退出码写入 touchpad-main.log / touchpad-worker.log，不记录输入内容。
- 新增 22 项实际 IPC/子进程及队列检查，覆盖一万次更新合并、阶段保留、过大帧拒绝、握手前不启动、调用方不等待启动、真实管道转发与接收、卡死/退出/启动失联三种回收、禁止失败重试循环、显式重试、关闭时取消、快速关闭再开启的会话竞争。辅助子进程故意失联期间独立 TCP 连接持续往返，测试父进程存活，子进程已退出。Windows 核心 160 项、含 Swift/.NET 互通共 177 项通过。故障子进程为测试替身，没有调用 Windows 原生接口。
- VERSION 保持 0.2.3，仅 Windows BUILD=27；Mac 应用、DMG 和登录组件保持不变，继续使用 Mac build 26。Windows 发布构建成功，原始 HID 类退出编译后无编译警告。本次不重复运行未变更的 Mac 单元测试。
- 尚未在用户 Windows 26H2 设备验证新辅助进程的实际原生接管、窗口恢复和手势效果，也没有重现原崩溃栈。上述检查验证进程隔离、非阻塞和互通行为，不代表 Windows 实体触控板验收通过。参考 Microsoft GetMsgProc / WH_GETMESSAGE 与 TouchpadGesturesController 消息循环说明。
- Windows build 27 自包含应用、便携 ZIP 与安装器已构建；发布日志无编译警告或错误。安装器载荷版本 0.2.3.27、尺寸与 SHA-256 和最终发布 exe 一致，便携 ZIP CRC、exe 字节及使用说明一致性通过。Mac build 26 成品保留；Windows build 26 成品已移入废纸篓。

## Windows 0.2.3 build 28 — 修正接管后的触点读取和错误处理

- 用户提供 build 27 的真实运行记录：辅助进程成功完成 configure remote=True 与 acquire native foreground；首次读取触点返回“拒绝访问”，主进程随后以 IOException 结束辅助进程，辅助进程退出码为 0。这解释了为何开启后 Windows 又会响应本机手势、Mac 没有收到触点。旧日志未分别标记 GetPointerFrameTouchpadInfo 与 GetPointerDeviceRects，不能仅凭旧记录确定失败的是其中哪一个。
- 删除 WH_GETMESSAGE 提前读取。双指在自己的窗口实际处理 WM_POINTER 时读取；多指通过 TouchpadGesturesController 的 PointerPressed / PointerMoved / PointerReleased 回调同步读取完整触点帧，不跳过控制器处理。此修改针对微软所要求的读取上下文；修复后的实际 Windows API 结果仍待目标机确认。
- 回调使用 ComWrappers、明确的 IUnknown + Invoke 四槽 ABI、参数化 TypedEventHandler IID 和 IAgileObject，未恢复 build 26 的 classic COM CCW。原生引用负责保持委托存活，订阅 token 逐项取消并释放；异常不向 user32/WinRT 展开。捕获和注入仍只在隔离辅助进程运行，不在普通键鼠钩子或连接握手中初始化。
- 将单帧读取异常与前台接管丢失分开。读取异常通过 captureWarning 传回，取消当前远端手势并保留已生效的 Windows 拦截和辅助进程，后续帧继续重试；恢复后清除提示并继续转发。前台丢失、关闭开关、返回本机、断线仍按原路径释放接管。重复相同读取异常的提示和日志最多每两秒一次；日志含 API 名、读取位置及 Win32 错误码。
- 通过真实非托管函数指针调用验证回调槽位、IUnknown/TypedEventHandler/IAgileObject 查询、订阅持有时的存活、异常隔离及连续一万次 PointerEventArgs.CurrentPoint.PointerId 访问；测试对象为 ABI 替身，不创建 Windows 控制器。另用真实子进程与本地管道模拟 Win32 5 读取告警、保持 Owned、后续帧恢复及返回本机释放。含原有核心与 Swift/.NET 网络互通共 188 项检查通过（171 项核心加 17 项互通）。没有以这些检查代替 Windows 实体触控板验收。
- VERSION 保持 0.2.3，仅 Windows 更新为 build 28；Mac 保持 build 26。本轮不修改 Mac 鼠标跨屏路径，按用户要求暂缓该偶发问题。Windows 发布构建成功，无编译警告或错误。
- 参考：[GetPointerTouchpadInfo 读取时机](https://learn.microsoft.com/en-us/windows/win32/input-precisiontouchpad/getpointertouchpadinfo)、[GetPointerInfo 所有权和 ERROR_ACCESS_DENIED](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getpointerinfo)、[TouchpadGesturesController 事件回调](https://learn.microsoft.com/en-us/windows/win32/input-precisiontouchpad/touchpadgesturescontroller)、[WinRT agile 委托](https://learn.microsoft.com/en-us/windows/apps/develop/cpp-winrt/agile-objects)。接口顺序与 IID 对照 Microsoft win32metadata 的 windows.ui.input.idl / windows.ui.core.idl。
- Windows build 28 自包含应用、安装器和便携 ZIP 已完成打包。安装器载荷版本 0.2.3.28、尺寸及 SHA-256 与最终应用一致，便携 ZIP CRC、exe 与使用说明一致性、Windows x64 PE 以及文件版本检查通过。Windows build 27 两个成品已移入废纸篓；Mac build 26 两个包的 SHA-256 与原记录一致。最终四个成品的 SHA256SUMS.txt 已更新并复核。

## Windows 0.2.3 build 29 — 控制器初始化失败与接管状态复核

- 用户提供 2026-10-19 08:26（UTC+8）的 build 28 日志：辅助进程 pid 51672 在 initialize native capture 阶段的 TouchpadGestureController 构造函数抛出 InvalidCastException，随后主进程停止辅助模式。记录没有进入 acquire native foreground。这次并非“接管后又失效”，而是尚未建立接管；因此 Windows 仍处理本机手势。原日志缺少具体初始化 API 与 HRESULT，无法唯一判定构造函数中哪次接口调用失败。
- 移除 build 28 新增的 WinRT TypedEventHandler / ComWrappers 实现及三个事件订阅，保留此前已运行过的控制器创建、SupportedGestures 和 Enabled 接口。启用后枚举隔离辅助线程的普通窗口和 HWND_MESSAGE 窗口，对同线程、同进程窗口安装 SetWindowSubclass；排除已经处理双指的鼠标遮罩窗口。这样兼容控制器延迟创建或复用窗口，读取发生在实际 WM_POINTER 窗口过程内，不再提前读取 GetMessage，也不需要订阅委托。
- 观察窗口消息后总是调用 DefSubclassProc，保留系统控制器正常处理；非触控板消息直接继续原处理。读取异常沿用非致命告警，不撤销有效拦截。捕获丢失时取消远端手势；销毁窗口和退出时移除观察过程。原生移除失败时保留 GC 根至 WM_NCDESTROY，避免原生函数指针悬空；当前回调转发完成前保持托管实例存活。
- 启用控制器后读回 Enabled 和 SupportedGestures 并核对前台进程，再报告接管。辅助线程心跳每 250 ms 复核原生状态和前台，补上前台切走后再无触点回调、界面却仍沿用接管状态的缺口。WM_ACTIVATE 内不直接判定最终前台，避免查询过早。没有增加抢回其他程序前台的循环，也没有修改系统触控板设置。
- 初始化日志逐步记录 RoInitialize、工厂、IsSupported、CreateForProcess、QueryInterface、手势范围、Enabled 和窗口捕获结果以及 HRESULT；两端诊断记录包含运行构建和系统版本。失败的具体步骤与错误码会直接送到 Windows 状态文本，不再只显示通用暂停提示。E_NOINTERFACE 映射成 InvalidCastException 时按可恢复的可选功能错误处理。
- 新增实际 SUBCLASSPROC 原生函数指针调用检查：同线程/进程过滤、重复窗口去重、读取顺序、始终继续原窗口过程、跳过普通消息、捕获丢失取消、销毁清理、读取异常与移除失败后的委托存活。另通过真实辅助子进程/本地管道检查初始化接口失败只停止辅助进程，并保留具体步骤和 HRESULT。更新后 176 项核心检查、含 Swift/.NET 互通 193 项检查通过；这些使用窗口驱动替身，并未在 Windows 原生控制器上运行，不能证明实体触控板已成功拦截或 Mac 已收到手势。
- VERSION 保持 0.2.3，仅 Windows BUILD=29；Mac build 26 和鼠标跨屏路径未修改。参考：[TouchpadGesturesController 创建窗口及前台要求](https://learn.microsoft.com/en-us/windows/win32/input-precisiontouchpad/touchpadgesturescontroller)、[触控板窗口注册](https://learn.microsoft.com/en-us/windows/win32/input-precisiontouchpad/registertouchpadcapable)、[SetWindowSubclass](https://learn.microsoft.com/en-us/windows/win32/api/commctrl/nf-commctrl-setwindowsubclass)。实际 Windows 初始化及跨端手势验收仍需目标机验证。
- Windows build 29 自包含发布与安装器构建成功；载荷版本 0.2.3.29、尺寸及 SHA-256 和最终 exe 一致，便携 ZIP CRC、程序与使用说明一致性、PE x64 和文件版本检查通过。193 项互通检查通过后，对委托释放期间存活的补充保护再次完成 176 项核心检查和发布构建。Windows build 28 成品已移入废纸篓，Mac build 26 包与原 SHA-256 记录一致；最终四个成品的校验清单已更新并复核。


## Mac / Windows 0.2.3 build 30 — 触控板捕获重构与接收回执

日期：2026-10-19，Asia/Shanghai。

- 用户报告 build 29 仍无效。本次移除手写 TouchpadGestureController、隐藏窗口枚举和子类化，改用 Microsoft.Windows.SDK.NET.Ref 10.0.28000.87 官方 C#/WinRT 投影。对照 SDK 编译元数据确认此前手写 TypedEventHandler 基础 GUID 的末位为 535，正确为 534；旧检查重复了同一错误常量，不能验证接口兼容性。新实现由 SDK 负责 ABI 和事件封送。
- 独立工作进程持有专用前台手势窗口；普通 CursorShield 只处理原始鼠标，不在手势工作进程重复注册 Raw Input。双指在自己的输入消息内读取；多指从 SDK PointerPoint 的 PhysicalPosition / PhysicalDeviceRect 归一化，并按硬件 frame 汇总，保持触点 ID 和整帧一致。读取错误保持拦截，关闭/失去前台仍释放。
- 新增协商能力 touchpad-feedback-v1、每次手势独立 epoch，以及触点收到/事件已提交/生成失败回执。源端忽略过期回执；高频回执最多每 250ms，结束和取消立即反馈。Mac 识别和输出从普通鼠标路由分离。API 提交成功并不证明目标应用或系统执行了动作。
- Mac 当前仍使用 Quartz 事件，没有注册真实虚拟触控板。新增 --virtual-touchpad-status 只读检查运行程序签名；账号尚未获得虚拟 HID entitlement。Apple 源码要求 com.apple.developer.hid.virtual.device 或内部 entitlement，用户审批和 root 不替代该检查。获得授权后仍需验证设备识别与系统手势，说明见 VIRTUAL-TOUCHPAD.md。
- Swift 50 项测试通过；Windows 核心和实际 loopback 加密互通共 202 项检查通过，覆盖整帧汇总、取消/抬起、过期编号、未提交不报告成功、回执节流与结束屏障，以及新字段在 Swift/.NET 中的真实加密往返。没有捕获用户输入或点击输入法测试窗口。
- Windows WPF 编译通过，0 警告/错误。上述检查不执行目标笔记本上的原生 API，不能证明 Windows 接管已经正常，也不能证明 Mac 全局手势已接受。
- 双端 build 30 成品已完成校验：Windows 单文件 bundle 中的 Microsoft.Windows.SDK.NET.dll 与 WinRT.Runtime.dll 字节和官方 SDK 包一致；安装器载荷版本 0.2.3.30、尺寸和 SHA-256 对应最终程序。Mac 签名、DMG 校验、背景/图标位置/Applications 链接与应用字节一致性通过；ZIP CRC 和程序/说明一致性通过。嵌入的可选登录 PKG 版本为 0.2.3.30。运行打包程序 --virtual-touchpad-status 确认缺少签名 entitlement，未请求权限或发送输入。
- artifacts 仅保留 build 30 双端成品与校验清单；旧成品移入废纸篓。没有替换或重启 /Applications 中用户正在使用的应用，没有安装登录组件或修改网络设置。


## Windows 0.2.3 build 31 — 再次跨屏时的前台授权交接

日期：2026-10-19，Asia/Shanghai。

- 用户真实日志：Windows 10.0.26300.0，build 30 在 01:05:01 UTC 创建 SDK 控制器、订阅事件并确认 foreground=True / enabled=True / supported=全部手势；01:05:03 返回本机后，01:05:14 再次切向 Mac 时因“Windows 拒绝手势窗口取得前台”退出。SDK 初始化与元数据探测在本次日志中成功，未修改这两处。build 29 的另一段日志是运行中失去前台后退出。
- 新增父子进程的前台交接请求：辅助进程准备完窗口后发送 foregroundRequest，主进程在输入 dispatcher 中向此辅助进程 PID 调用 AllowSetForegroundWindow（不用 ASFW_ANY），随后回复 activate。每个请求校验当前会话编号和远端控制状态，过期请求不激活。
- 移除对外部前台线程的 AttachThreadInput；SetForegroundWindow 请求和实际确认分开，允许消息循环完成激活。每次跨屏最多四次、间隔至少 250ms 的授权请求。暂时拒绝或失去前台时取消活动手势并做有限次恢复，不把它视为 SDK 崩溃或直接结束辅助进程；持续拒绝后暂停捕获，返回本机再跨屏可以重新请求。真正的 SDK/IPC 故障仍隔离并停止工作进程。
- 前台快照在整次尝试中保留，重试不会覆盖原应用；返回、关闭或取消时仅在持有手势窗口前台时恢复原窗口。未注入 Alt、点击或其他模拟按键来解锁前台，没有修改系统焦点策略、网络或触控板设置。
- 新增对重复跨屏、拒绝后新授权、异步激活确认、原窗口恢复、四次重试上限、返回本机取消和旧会话失效的检查，并使用真实子进程/命名管道验证两次交接都会先授权给准确 PID 再激活。Windows 核心共 203 项检查通过；WPF 编译 0 警告/错误。模拟进程不执行真实 Win32 前台策略，仍需目标笔记本验证两次跨屏及实际手势接收。
- 仅 Windows 升至 build 31，网络协议与 Mac 应用未改，Mac build 30 保持兼容。
- Windows build 31 自包含程序和安装器发布成功，载荷版本 0.2.3.31、尺寸、SHA-256 和最终程序一致；两份 PE 均为 x64 GUI。便携 ZIP CRC、程序和文档字节一致性通过。Mac build 30 两个安装包保持原 SHA-256；Windows build 30 成品移入废纸篓，最终 artifacts 校验清单已更新。

## 虚拟触控板实际注册实验 — 尚未可用

日期：2026-10-19，Asia/Shanghai。

- 用户反馈 Windows 显示触点和提交事件计数后，Mac 的多指手势仍无实际响应。计数证明传输与 API 提交，不能据此认定 macOS 接受了手势。
- 新增独立 Tools/VirtualTouchpadProbe.swift 和 scripts/probe-virtual-touchpad.sh。描述符包含五个 Finger 集合及 Contact Count / 最大触点数 Feature，34 字节输入报告；独立遍历 HID 短项检查集合、位数、截断和错误 Report ID，--self-test 通过。该检查不证明 Apple 驱动接受设备。
- 在 macOS 27.0.1 / 26A434 调用实际 CoreHID HIDVirtualDevice 创建：返回 nil。IOHIDResource type=0 的用户连接在未提交任何描述符之前返回 0xe00002c2（invalid argument）。签名检查显示实验程序及已安装 ListFXFlow 均无 com.apple.developer.hid.virtual.device；Apple SDK 的 IOHIDUserDevice.h 和 IOHIDResourceUserClient 实现明确要求它。没有把错误码解读为它未表达的明确权限错误。
- 未注册成功，未发送任何输入报告，未接管或操作用户输入窗口，未伪造 Apple VID/PID、受限签名权限或修改系统安全策略。尚未实现可用的虚拟触控板共享；Apple 授权和原生多点触控驱动识别仍是待满足条件。
- 不递增 BUILD，不替换安装包或正在运行的应用；Mac build 30 / Windows build 31 成品保留。


## Mac / Windows 0.2.3 build 32 — 隐藏映射，仅自动屏蔽 Windows 手势

日期：2026-10-19，Asia/Shanghai。

- 按用户要求隐藏跨系统手势映射：移除 Mac 权限页与 Windows 的实验开关；Mac 运行门控固定禁用转发/接收，普通事件 tap 不订阅通用多指事件。双方不再宣告 touchpad-v1 / native-receive / feedback 能力；Windows 主输入路由不转发触点、不注入旧版手势消息。旧偏好仅保留在配置，不能激活隐藏功能。
- Windows 本机屏蔽改为自动随共享与 Mac 指针所有权启停，完全不依赖对端能力、开关或旧 TouchpadEnabled 值。屏蔽状态与“重试”在 Windows 主界面可见。返回本机、停止、断线和锁屏仍释放窗口与前台。
- 新增隔离工作进程 SuppressOnly 配置：SDK 指针回调设置 Handled 后直接返回；双指窗口消息直接消费，不调用 GetPointerFrameTouchpadInfo / GetPointerDeviceRects / PointerPoint 坐标读取；手势注入器延迟创建，屏蔽路径不会创建它。接管的精密触控板兼容滚轮不再重复走鼠标通道，实体鼠标滚轮保留。没有修改一般键鼠、Caps/IME、剪贴板、网络或屏幕排列逻辑。
- Swift 51 项测试通过，Windows 核心 211 项检查通过；新增覆盖自动启停、实体鼠标滚轮隔离、未接管时不谎报成功、SuppressOnly 跨真实 IPC 保留、不转发触点及拒绝旧输入注入。WPF 编译 0 警告/错误。模拟工作进程不验证 Windows 原生拦截；实体笔记本仍需验收多指屏蔽与返回恢复。
- VERSION 保持 0.2.3，仅 BUILD 增至 32。本次双端均打包，Mac 虚拟设备实验代码保留为独立开发工具，未加入日常程序。

- 最终构建验证：Swift 51 项测试通过；.NET 核心 211 项、含真实 Swift/.NET TCP 互通共 229 项检查通过。Windows WPF 与双端 Release 构建成功。
- Windows 安装器嵌入载荷的版本 0.2.3.32、大小及 SHA-256 与程序一致，便携 ZIP 的 CRC、程序及说明匹配。Mac staged/DMG 应用严格签名校验通过，CFBundleVersion=32；DMG 毛玻璃背景、Finder 图标位置、Applications 链接与 ZIP 内容已核对，内嵌可选登录 PKG 的版本与程序均为 build 32。旧成品已移至废纸篓，artifacts 仅保留双端 build 32 及四包 SHA-256。未替换已安装应用、未安装登录组件，也没有 Windows 实体触控板验收。


## Mac 0.2.3 build 33 — Mac 实体鼠标跨屏与来源切换

日期：2026-10-19，Asia/Shanghai。

- 用户确认 Mac 鼠标推向连接边缘时一直显示 Mac，而 Windows 触控板仍可从 Mac 移入 Windows。已安装 Mac 为 build 32；读取到当前排列为 bottom、offset 约 95，主屏为 2560×1440、2×。只读事件观察未捕获边缘样本，未复现用户的实际阻塞，不能唯一判定本次运行原因。
- Mac 本机鼠标运动前对已有按键/鼠标按钮缓存核对 HID 当前状态，只移除已释放项，不从全局注入状态添加按下项。避免错过释放事件后 mayCross 长期为 false；真实持有仍阻止跨屏。远端控制期间继续使用原始事件缓存，避免被抑制的物理事件与系统状态不同导致误清除。
- 修复 Mac 本机运动不发送位置而 Windows 仍持有旧虚拟坐标的问题：同一 4ms 合并通道同步本机位置，两种输入来源依据同一位置和屏幕重叠区域判断边界；收到对端位置会清除本机待发旧采样。跨屏所有权消息仍立即发送。Windows 源码和 build 32 手势屏蔽保留。
- 增加 pointer-seam 系统日志：记录连接方向、位置/位移、是否跨过或被持有状态/非重叠区域阻止，限频，不记录键码或文本。日志可用 log show --predicate 'subsystem == "com.listfxflow.mac" AND category == "pointer-seam"' --last 10m 查看。
- 新增四方向漏释放回归、真实持有保留、不添加全局注入按下状态，以及 bottom+offset=95 下切换 Mac 鼠标/Windows 触控板位置同步与非重叠区域一致性检查。真机来回跨屏仍待用户验证。

- 最终检查：54 项 Swift 测试通过（含新增 3 项）；Mac build 33 Release 编译及 staged/DMG 应用严格签名校验通过，DMG 校验、背景/图标布局/应用一致性、ZIP CRC 和程序字节匹配，内嵌可选登录 PKG 为 0.2.3.33 且应用 build=33。Mac build 32 成品移至废纸篓；保留 Windows build 32，并重新生成四个包的 SHA-256。没有替换正在运行的已安装应用或安装登录组件。


## Mac / Windows 0.2.3 build 34 — 游戏防误跨屏、锁定与相对运动

日期：2026-10-19，Asia/Shanghai。

- 研究 Deskflow 当前官方 docs/Configuration.md、Server.cpp 和 MSWindowsDesks.cpp：配置锁定（默认 Scroll Lock/可自定义）；relativeMouseMoves 仅在锁定远端时发送相对位移并提前返回，避免假设游戏内指针位置；相对注入走 Windows 鼠标输入。没有从所检查的路径发现通用游戏捕获识别；本项目自动判断是独立的可关闭启发式，不宣称等同 Deskflow。
- Windows 作为唯一游戏状态发布方；Ctrl+Alt+L / Mac Ctrl+Option+L 或双端界面、Windows 托盘、Mac 状态栏手动锁定/解除。快捷键首按处理，连发不重复切换，释放在修饰键先释放后仍消费；无能力/非 Windows 当前控制时保留普通键输入。断线/停止/锁屏清空锁定与快捷键闩锁，紧急停止不受锁定影响。
- 默认可关闭的自动保护：只读 GetForegroundWindow / GetCursorInfo / GetClipCursor / GetWindowRect / GetMonitorInfo，排除本程序与手势辅助进程，要求隐藏指针（排除触控抑制态）且窗口覆盖显示器或指针范围受限。定时 100ms 读取、普通输入回调限频 50ms，候选跨屏时即时复核。必须完成双端能力与初始指针所有权后才开启，避免初始连接抢走 Mac 所有权；自动判断存在全屏视频等误判，手动解除在同一次捕获期间不会立刻重锁。
- 游戏锁定阻止两端 PointerRouter 跨屏及迟到的外来所有权；Windows 本机移动直接继续系统钩子，不抢焦点、不回中、不改变游戏的原始输入注册/裁剪。Mac 实体鼠标在锁定时绕过虚拟边缘累计，按 4ms 合并真实相对位移，Windows 独立注入线程提交 MOUSEEVENTF_MOVE。连续相对采样合并为位移之和，按键/点击/模式为屏障；不按绝对指针的“保留最后位置”丢失总位移。退出时清除旧模式待注入移动并按 Windows 当前位置同步。
- 新增四方向锁定、迟到所有权、停止重置、捕获/窗口切换、手动放行、关闭自动、快捷键连发/普通输入、协议非法值、队列屏障与阻塞原生调用期间 10,000 次相对采样守恒检查。Swift/.NET 真实 TCP 新增 gameMode、gameModeRequest、relativePointer 的字段往返校验。上述测试使用纯状态与原生驱动替身，未在 Windows 真正全屏游戏、反作弊或实体鼠标 Raw Input 模式验收。

- 最终验证：57 项 Swift 测试、249 项 .NET 核心检查通过，包含真实 Swift/.NET TCP 互通共 268 项检查通过；WPF 编译 0 警告/错误。双端 Release 构建成功，Windows 嵌入安装器载荷版本 0.2.3.34/大小/SHA-256 匹配程序，便携 ZIP 的 CRC、程序和说明匹配；Mac staged/DMG 应用严格签名通过，DMG 背景、图标布局、Applications 链接、build 34 程序及 ZIP 内容一致，可选登录 PKG 为 0.2.3.34、内置 app build=34。旧成品移至废纸篓，artifacts 仅保留 build 34 及四包 SHA-256；未替换正在运行的已安装应用或安装登录组件。真实游戏与反作弊兼容性尚未验收。
