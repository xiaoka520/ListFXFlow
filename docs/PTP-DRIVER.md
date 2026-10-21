# Windows 原始 PTP 触点观察驱动（开发试验）

build 42 提供 KMDF 只读过滤驱动、独立旁路接口、HID 描述信息解析和三指触点转发接入。驱动复制系统已经读取成功的 PTP 输入报告，保留原请求的字节、状态和长度。它没有屏蔽输入的 IOCTL，也不会改变触控板模式或原始报告。

普通安装包不会安装、加载、注册或下载这个驱动，也不会修改启动、安全设置或设备过滤器。当前 Windows SDK 继续负责本机多指手势屏蔽，因此 **本阶段仍依赖 SDK 的前台所有权**。原始 HID 采集和内核接管是两个阶段，不能把只读观察说成已经解决后台手势接管。

## 当前数据路径

PTP HID collection → 原有系统 Read 请求完成 → 驱动环形缓冲中的副本 → 受保护的 raw PDO → 独立工作进程 → HidP_GetData / 描述信息映射 → 完整帧组装 → 已有雷雳加密连接 → Mac 系统手势事件。

- 原设备 Read 请求照常完成；观察器没有取消、截断、修改或吞掉它们。
- 环形缓冲为 64 个、每个至多 4096 字节的报告；生产者不等待读取程序，满时丢弃最旧的观察副本并记录序号缺口。
- 旁路仅允许管理员 / SYSTEM 读取，且同一设备仅允许一个读取者。没有观察者句柄时不收集报告副本。
- 驱动核实 collection 的 Usage Page=0x0D、Usage=0x05 才标记可用。应用按真实 HID value / button capabilities 读取字段，不写死某家厂商的报告偏移。
- 帧组装支持并行报告与携带相同 Scan Time 的混合分包，检查 Contact Count、触点编号、Tip Switch、Confidence、坐标范围和报告连续性。丢失、矛盾或不支持的报告结束当前手势并回退 SDK。
- 原始报告的 QPC 时间随手势传给 Mac，避免把网络到达间隔当作设备采样间隔；旧版本忽略可选时间字段。
- 当前生产转发仍限三指 / 四指滑动；原始采样诊断可以统计一至五指，双指滚动保持现有通道。

## 在 Windows 获取设备信息（不安装驱动）

```powershell
& "$env:LOCALAPPDATA\Programs\ListFXFlow\ListFXFlow.exe" --touchpad-hid-diagnostics
```

若安装路径不同，请使用实际 exe 路径。诊断保存到 `%LOCALAPPDATA%\ListFXFlow\Diagnostics\touchpad-hid.json`。其中 `PhysicalTouchpads` 的 hardware ID 用于生成单设备 INF；不采集设备序列号、按键、坐标或剪贴板。没有驱动时 `Observers` 为空是预期结果。

驱动在经过审查、签名并正确加载后，可以以管理员身份执行下面的 **只读采样**，在十秒内用一至五指触摸触控板：

```powershell
& '实际路径\ListFXFlow.exe' --touchpad-hid-diagnostics --capture-seconds 10
```

同一时间不要让另一实例占用旁路接口。诊断只记录完整帧数与最大触点数量，不记录运动轨迹。日常工作进程没有旁路访问权限或驱动时，继续使用 SDK。

## 构建和准备单设备包

在 Windows、Visual Studio 2026 C++ 工具链、MSBuild 与 NuGet 环境运行：

```powershell
./scripts/build-ptp-driver.ps1
```

WDK / SDK NuGet 固定为 10.0.28000.2526，KMDF 1.33，Desktop / x64。产物在 `artifacts/PTP-Driver-Experimental/`，是 **未签名开发产物**。CI 不安装驱动，构建二进制、验证公共 ABI，并对使用测试硬件 ID 生成的 INF 执行 `InfVerif /w`。此 NuGet 包不包含其旧构建目标调用的 ApiValidator，当前没有宣称通过 Universal API layering 校验；完整签名、HLK 和运行时 Driver Verifier 尚待进行。

```powershell
./Prepare-PtpDriver.ps1 -DiagnosticsFile '实际路径\touchpad-hid.json'
```

准备工具只生成目标硬件 ID 的 INF 和哈希清单，不安装、不修改注册表、不重启设备。不接受 `HID_DEVICE` 通用类匹配，不对整个 HIDClass / Mouse 类添加过滤器。输出 INF / CAT 还需 WDK 工具验证与微软签名，才能在正常安全配置的 Windows 分发加载。

## 尚需实机验证

当前 Mac 无法加载 Windows 内核驱动。Windows CI 构建、缓冲区测试和模拟帧测试不能证明这台笔记本的过滤器附着层级、Read 完成回调、实际 HID report 布局、拔插 / 睡眠 / 恢复、卸载与原输入放行可靠。拿到设备诊断、有效签名和测试环境后，先完成这些验证，再开发内核输入接管；当前包没有提供内核屏蔽开关。

## 原始参考

- [Microsoft HID architecture](https://learn.microsoft.com/en-us/windows-hardware/drivers/hid/hid-architecture)
- [Microsoft Kbfiltr raw PDO sideband sample](https://github.com/microsoft/Windows-driver-samples/tree/main/input/kbfiltr)
- [Obtaining HID reports](https://learn.microsoft.com/en-us/windows-hardware/drivers/hid/obtaining-hid-reports)
- [Precision Touchpad report contract](https://learn.microsoft.com/en-us/windows-hardware/design/component-guidelines/touchpad-windows-precision-touchpad-collection)
- [WDK NuGet setup](https://learn.microsoft.com/en-us/windows-hardware/drivers/install-the-wdk-using-nuget)
