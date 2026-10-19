# ListFXFlow

为桌面 Mac 与 Windows 笔记本打造的雷雳键鼠共享工具。Mac 使用 Swift / SwiftUI；Windows 使用 C# / WPF 与 Win32 原生输入接口。

鼠标跨过相邻屏幕边缘时切换设备，键盘始终跟随当前光标。远控另一端时固定并隐藏本机鼠标，返回或断开时恢复之前的应用和光标。Windows 的钩子、注入与网络处理独立于界面线程，移动事件会合并，点击与按键保持顺序。鼠标和键盘可以分别连接在任一设备上；不要求两者插在同一台电脑。两块屏幕可以拖动排列，支持上下左右与错位对齐。

0.2.0 支持文字、文件和文件夹的双向剪贴板同步。双端均需更新才能使用新功能；只与旧版连接时仍可共享键鼠。调整屏幕排列保留当前控制端及屏内位置。Windows 离开位置的指针会停留并隐藏，后台原始鼠标输入不受屏幕边缘限制。

当前开发构建为 **Mac / Windows 0.2.3 build 34**；未发布期间只增加 `BUILD`，保留 `VERSION`。Mac 常驻菜单栏，Windows 常驻系统托盘。滚轮固定使用精细增量，不额外添加动画；Windows 普通键鼠注入采用独立线程，Windows 本机手势屏蔽运行在独立辅助进程，Mac 加密收发采用独立队列。DMG 的图标区域使用毛玻璃视觉背景。

本构建移除了 Caps 临时切换 ABC 输入源的处理，保持用户选择的输入法。Caps 混输和候选保留等待真实连接下连续键盘输入验收；之前逐次点击测试按钮会打断组合状态，相应结果不能证明原生混输行为。

## 使用

1. 用支持数据传输的雷雳 3 / 4 / 5 线直连两台电脑。Windows 11 设备需要具备可用的 Thunderbolt / USB4 网络驱动；实际驱动兼容性取决于机型。
2. Mac 打开 `ListFXFlow-Mac-arm64-0.2.3-b34.dmg`，把 `ListFXFlow.app` 拖入「应用程序」后启动。应用默认启用共享，并常驻屏幕右上角菜单栏；关闭主窗口后仍在后台运行。在权限向导中分别打开**辅助功能**和**输入监控**设置。设置旁会同时显示一个带应用图标的小窗口，按住图标将当前 `.app` 拖入权限列表，再开启右侧开关；返回应用后会自动刷新状态。两项权限齐备后会继续自动启动；没有雷雳网桥时会等待接线。若系统要求重新启动，点击「重新打开应用」。请给向导中显示的当前应用路径授权。需要登录前共享时，先与 Windows 成功连接一次，再进入「权限设置 → 登录前共享」安装组件；更新、运行状态与移除也在此处。
3. Windows 运行 `ListFXFlow-Setup-x64-0.2.3-b34.exe`，按向导安装到当前用户；安装后可勾选立即运行。应用常驻系统托盘，关闭主窗口或最小化窗口后仍在后台运行。也可直接解压 `ListFXFlow-Windows-x64-0.2.3-b34.zip` 运行免安装版。应用自动选择识别到的 Thunderbolt / USB4 网卡；若驱动使用其他名称，请选择对应的**有线雷雳网卡**，点击「连接 Mac」。滚轮固定启用精细增量，保留小幅滚动，不额外生成动画；无需设置开关。
4. 默认自动交换设备身份、自动加密，无需配对码。优先使用已保存的地址；Windows 的「Mac IP」可填写 Mac 雷雳 IPv4，留空时优先尝试上次认证成功的地址，不可用则自动发现。IP、网卡选择和成功连接地址会保存；已连接时不为切换地址主动断线。Mac 持续检查实际网桥地址，地址变化或监听失败时自动重新监听；手动停止不会自动恢复。首次连接后记住对端身份。
5. 在任一端拖动 Windows 屏幕调整排列。无需偏移滑块，直接拖动即可调整错位。两台设备必须共享一段相邻边缘，光标才能从这段边缘穿过。
6. **Ctrl + Alt + Esc** 紧急停止；Mac 上是 **Control + Option + Esc**。关闭程序、休眠、连接中断或心跳超时都会停止转发并释放程序注入的按键；未安装登录组件时，锁屏也会停止共享。初次连接由 Mac 保持当前位置，修改排列不会改变控制端或将指针居中。手动停止后不会在本次运行中自行恢复共享。
7. 主窗口不是程序的生命周期。Mac 关闭主窗口后从菜单栏图标重新打开或退出；Windows 关闭或最小化主窗口后从系统托盘图标重新打开、共享或退出。托盘/菜单栏菜单里也能直接停止共享。真正退出请使用菜单中的「退出 ListFXFlow」，直接关闭窗口只会转入后台。
8. 在本机复制文字、普通文件或文件夹，连接后自动同步到远端。文件等待状态显示「文件已同步 · 可在远端粘贴」后，按目标系统的粘贴快捷键粘贴：Windows 为 Ctrl+V，Mac 为 Command+V。支持多选、子目录和空目录；停止共享会取消未完成传输。

两端仍显示自己的桌面和应用，不传输画面。Windows 程序为自包含版本，不需要另装 .NET。Mac 首版构建为 Apple Silicon，要求 macOS 14 或更新版本。

## 当前范围与边界

- 支持每台设备的**主显示器**；各设备内部多显示器拓扑和多台远端尚未实现。
- 支持常规键盘、五键鼠标、纵向与横向滚动。Fn、媒体键和厂商专用键尚未支持；跨系统触控板手势映射暂时隐藏；指针在 Mac 时自动屏蔽 Windows 本机多指手势，说明见下文。按物理键位传输，输入法由目标系统处理；Caps 不切换输入源；中文混输仍待真机验收；Win 键对应 Command，Alt 对应 Option，Ctrl 保持 Control。
- 按住键盘按键或鼠标按钮时暂不自动跨屏，避免拖动中断或修饰键残留。显示器配置变化后停止共享，需要重新连接。
- Windows 登录页与锁屏共享已移除，只支持登录后的普通桌面。Mac 登录页与锁屏需要另外安装 Mac 系统组件，见[启用与移除说明](docs/LOGIN-SUPPORT.md)；Windows 此时需已登录并运行普通应用。不包括 FileVault 解锁前；Mac LoginWindow、TCC 和 Secure Input 行为尚待双机验收。
- 仅使用雷雳网络；Mac 监听 `bridge0` 的 IPv4 地址，Windows 连接绑定用户选择的有线网卡。支持 Mac 网桥多个 IPv4 地址，不回退 Wi-Fi，不修改系统网络配置。
- 雷雳端口和线材本身不保证网络可用。Windows 必须出现 Thunderbolt Networking / USB4 P2P 网络适配器，双方需有可互通的 IPv4 地址。若驱动无法建立网桥，应用层无法代替驱动。程序不会自动配置静态 IP 或更改防火墙。
- 本地开发包未做发行证书签名和公证。Mac 构建脚本优先使用本机已有 Apple Development 身份签名，保持后续更新的设备权限身份稳定；没有证书时退回 ad-hoc。Windows 安装包尚无 Authenticode 签名，首次运行可能出现 SmartScreen 提示，需要选择「更多信息」后继续。用于双机联调，不作为已经完成硬件验收的发行版。


## Windows 本机手势屏蔽

跨系统多指手势映射暂时隐藏，两端不显示实验开关，也不宣告原生手势接收能力。已保存的旧设置保留，但不能重新启用被隐藏的映射功能；不向另一端转发多指触点或手势事件。

Windows 的屏蔽与旧开关和 Mac 手势能力无关：连接正常且指针控制 Mac 时自动申请屏蔽本机触控板多指手势；返回 Windows、停止共享、断线或锁屏后恢复本机处理。普通鼠标移动、单指移动、点击和实体鼠标滚轮沿用原输入通道。控制 Mac 时，已被接管的精密触控板兼容滚轮不再通过原始鼠标通道重复转发。

屏蔽使用官方 Windows SDK 的 TouchpadGesturesController 接管三指及以上全局手势，双指消息由自己的触控板窗口消费。独立工作进程不读取触点帧/设备坐标、不建立手势注入设备、不发送触点或接收回执；因此不依赖 Mac 虚拟 HID 权限，也不会等待 Mac 执行手势。现有实体设备需要 Windows 实际提供该接管 API。

每次跨屏都向指定工作进程重新授予前台激活资格，确认实际前台与控制器启用后才显示“本机多指手势已屏蔽”。暂时被拒绝时进行有限次恢复；失败会显示原因，可点击 Windows“本机触控板”区域的“重试”，或返回本机后重新跨屏。未修改系统触控板设置或焦点策略。真正的 SDK/进程故障只停止手势辅助功能，普通键鼠连接继续；不自动循环启动崩溃进程。

诊断日志仍在 Windows %LOCALAPPDATA%/ListFXFlow/Diagnostics/touchpad-main.log 与 touchpad-worker.log，记录 suppressOnly 模式、前台授权和异常，不记录输入内容。Windows 原生屏蔽、返回恢复和不同设备兼容性仍需在目标笔记本验证。Mac 虚拟触控板的实验工具与权限说明保留在 [研究文档](docs/VIRTUAL-TOUCHPAD.md)，未接入日常共享。

## 剪贴板

文字和文件使用独立 TCP 49236 连接，不占用键鼠消息队列。会话密钥经已认证的加密控制连接分发，剪贴板连接再次证明持有密钥；每次连接使用新随机数派生方向密钥，以 AES-GCM 加密。文件分块为 192 KiB，接收后逐文件校验 SHA-256，全部完成才发布系统文件剪贴板。没有自动修改防火墙；若键鼠正常但剪贴板无法连接，请检查该应用的 TCP 49236 是否可通。

文件按复制语义同步，不执行远程删除。新复制的内容会取消本端正在处理的旧任务；接收期间若本机剪贴板改变，不覆盖新内容。文字最大 512 KiB；文件总量最大 1 TiB、最多十万个文件/目录及 256 个顶层项目，还需接收端有足够磁盘空间。普通文件、空文件和文件夹受支持；符号链接、Windows 重解析点、不可跨系统使用的名称、虚拟附件、富文本和位图剪贴板暂不支持。文件内容及目录结构会复制，系统专用扩展属性、权限和资源叉不保留。

收到的文件存放在 Mac 的 `~/Library/Application Support/ListFXFlow/ClipboardReceived` 或 Windows 的 `%LOCALAPPDATA%/ListFXFlow/ClipboardReceived`，使退出程序后仍能粘贴。不完整传输自动删除；已完成缓存保留，需要时可在文件管理器中清理（清理当前剪贴板引用的缓存前应先粘贴或重新复制其他内容）。

## 构建

Mac（安装 Xcode 或支持的 Swift 工具链）：

```sh
swift test
zsh scripts/build-mac.sh
```

输出 `artifacts/ListFXFlow.app`、`artifacts/ListFXFlow-Mac-arm64-<版本>-b<构建>.dmg` 和同名 ZIP。设置 `SIGN_IDENTITY` 可以指定签名身份，设为 `-` 可显式使用 ad-hoc。若系统默认只有 Command Line Tools，脚本会优先使用 `/Applications/Xcode.app`。

Windows（.NET 10 SDK）：

```powershell
dotnet run --project windows/FlowCore.Tests/FlowCore.Tests.csproj -c Release
./scripts/build-windows.ps1
```

输出免安装 ZIP 与 `artifacts/ListFXFlow-Setup-x64-<版本>-b<构建>.exe`。安装包把自包含的 `ListFXFlow.exe` 嵌入自身，安装到当前用户目录并注册卸载入口，不需要管理员权限。

也可以在 Mac 用 .NET SDK 交叉构建 Windows 程序（不能代替 Windows 上的输入和 UI 验收）：

```sh
dotnet publish windows/ListFXFlow/ListFXFlow.csproj -c Release -r win-x64 \
  --self-contained true -p:PublishSingleFile=true \
  -p:IncludeNativeLibrariesForSelfExtract=true -o artifacts/Windows-x64
```

自动化检查见 `.github/workflows/build.yml`。网络协议互通测试见 [验证记录](docs/VALIDATION.md)；消息格式与身份协商见 [协议说明](docs/PROTOCOL.md)。

## 实现依据

- [Apple：IP over Thunderbolt](https://support.apple.com/guide/mac-help/mchld53dd2f5/mac) 说明雷雳网桥的 IP 通信。Apple 页面面向 Mac-to-Mac，Windows 端的条件需结合微软文档和真机检查。
- [Microsoft：USB4 域间连接](https://learn.microsoft.com/zh-cn/windows-hardware/design/component-guidelines/usb4-interdomain-connections) 说明 Windows USB4NET 网络支持。
- [Apple：CGEvent](https://developer.apple.com/documentation/coregraphics/cgevent) 提供 macOS 事件截获与注入接口。
- [Microsoft：SendInput](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-sendinput) 说明 Windows 输入注入与 UIPI 权限限制。
- [Microsoft：Raw Input](https://learn.microsoft.com/en-us/windows/win32/inputdev/about-raw-input) 与 [SetCursor](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setcursor) 提供后台鼠标移动与指针显示控制；不替换系统光标主题。


### Windows 游戏保护

双端更新到 build 34。控制 Windows 时按 Ctrl + Alt + L（Mac 键盘为 Ctrl + Option + L）锁定/解除，也可使用主界面及托盘/状态栏按钮。锁定期间阻止所有输入来源跨回 Mac，并拒绝迟到的其他桌面所有权消息；普通键鼠仍交给 Windows 游戏处理。Ctrl + Alt + Esc 仍停止共享。锁定状态不持久保存，停止、断线或锁屏后清除。

Windows 默认启用可关闭的自动保护：前台非本程序窗口隐藏指针，且覆盖显示器或限制了指针活动区域时启动，返回可见指针或退出该捕获窗口后释放。这是外部状态启发式，不是 Windows 提供的“正在游戏”标志；全屏视频隐藏指针等场景也可能触发。可关闭自动选项并使用手动锁定。自动保护期间手动解除会保持放行，直到该捕获状态结束或前台窗口改变。不会修改游戏的 ClipCursor、焦点或系统鼠标设置。

参考 Deskflow 的锁定及相对运动设计：Mac 鼠标控制已锁定的 Windows 时按物理位移发送 relativePointer，不累计虚拟桌面坐标；Windows 用相对鼠标输入提交，合并保留位移总量和点击顺序。解锁时按 Windows 当前指针位置重新同步，避免旧坐标跳动。软件注入在特定游戏、Raw Input/反作弊环境中的兼容性仍需真机验证；不能把构建、模拟检查或网络互通当作游戏验收。

研究依据：[Deskflow 配置](https://github.com/deskflow/deskflow/blob/master/docs/Configuration.md)、[Server.cpp 锁定与相对运动](https://github.com/deskflow/deskflow/blob/master/src/lib/server/Server.cpp)、[Windows 相对输入](https://github.com/deskflow/deskflow/blob/master/src/lib/platform/MSWindowsDesks.cpp)。实现使用本项目 Swift/C# 路由与输入队列，未复制 Deskflow C++ 实现。
