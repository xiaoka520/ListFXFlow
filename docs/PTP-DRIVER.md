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

WDK / SDK NuGet 固定为 10.0.28000.2526，KMDF 1.33，Desktop / x64。产物在 `artifacts/PTP-Driver-Experimental/`，是 **未签名开发产物**。CI 不安装驱动，构建二进制、验证公共 ABI，并对使用测试硬件 ID 生成的 INF 执行 `InfVerif /w`。旧构建目标默认查找 x86 ApiValidator，而此包提供 x64 / ARM64 版本；当前 Desktop 目标没有宣称通过 Universal API layering 校验，HLK 和运行时 Driver Verifier 尚待进行。

```powershell
./Prepare-PtpDriver.ps1 -DiagnosticsFile '实际路径\touchpad-hid.json'
```

准备工具只生成目标硬件 ID 的 INF 和哈希清单，不安装、不修改注册表、不重启设备。不接受 `HID_DEVICE` 通用类匹配，不对整个 HIDClass / Mouse 类添加过滤器。输出 INF / CAT 还需 WDK 工具验证与微软签名，才能在正常安全配置的 Windows 分发加载。

### ASUF1209 / Col02 专用配置

收到的 Windows 26300 诊断已确认 `HID\VEN_ASUF&DEV_1209&Col02` 是 Usage Page 13 / Usage 5 的 PTP collection，输入报告长度为 50 字节。`Observers: []` 表示尚未枚举到观察驱动的旁路接口；本次未请求采样，零帧不是触点读取失败的证据。

仓库仅保存型号级硬件 ID、usage 和报告长度，不保存实例 ID、序列号或诊断时间。构建会在开发产物的 `Profiles/ASUF1209-Col02/` 生成精确匹配的 INF、原驱动、许可和 `target.json`，并执行 `InfVerif /w`。50 字节不能代替实际 preparsed capabilities，也不能据此确定坐标偏移或证明触点可读。

Unsigned 配置包是未签名开发产物，没有签名 CAT，不能当作可直接安装的发行包。当前没有可用的 Microsoft Hardware 签名账号及关联证书，默认安全配置下的部署因此尚未完成；没有加载观察驱动时，普通应用继续使用 SDK。另行构建的 TestSigned 包用于手动配置的测试签名环境，见下节。[微软内核驱动签名策略](https://learn.microsoft.com/en-us/windows-hardware/drivers/install/kernel-mode-code-signing-policy--windows-vista-and-later-)及[签名账号与证书要求](https://learn.microsoft.com/en-us/windows-hardware/drivers/dashboard/code-signing-reqs)说明了正式提交与加载条件。

### TestSigned 专用测试包

Windows 构建端先执行 `scripts/build-ptp-driver.ps1`，再执行 `scripts/sign-ptp-test-package.ps1`。输出 `artifacts/PTP-Driver-TestSigned/ASUF1209-Col02/`。过程创建约 90 天有效、仅代码签名 EKU 的自签 RSA 证书：先以 SHA-256 为 SYS 签名，再用 Inf2Cat 生成 CAT，再为 CAT 签名。SignTool 校验两份签名及 CAT 对 SYS / INF 的成员绑定。私钥不可导出，不进 ZIP / 缓存，构建结束从签名机移除。

包里仅包含公开 CER，`test-signing.json` 记录证书指纹、期限、文件 SHA-256 和源码修订。`target.json` 的 DriverHash 对应签名后的 SYS。`RequiresMicrosoftSigning` 仍表示默认安全配置的正式分发条件；`SigningMode=self-signed-test` 和 `RequiresTestSigning=true` 明确这个包仅供测试配置使用。

用户完整解压到 Windows 本机目录，接好外接鼠标、保存设备加密恢复密钥，再以管理员身份运行 `Import-TestCertificate.cmd`，仅把包内指纹匹配的证书导入本机 Root / TrustedPublisher。随后由用户手动配置 TESTSIGNING 并重启，再运行 `Try-Install.cmd`。证书导入、启动配置、驱动安装是三个独立动作；脚本不修改 BCD / Secure Boot / 内存完整性，不自动重启。具体顺序和十秒采样命令在包内 `Test-README.txt`。

清理时先运行 `Rollback.cmd` 卸载本项目扩展包，再运行 `Remove-TestCertificate.cmd`；清理脚本只移除本包指纹，并在仍检测到本项目 PTP 驱动包时拒绝先移除信任。之后由用户关闭测试模式、重启并恢复其修改过的固件设置。CI 在隔离 Windows 中验证证书导入、清理和篡改拒绝，不执行驱动安装或改变启动配置。实际设备的加载、报告读取和稳定性仍需实机验证。[微软测试签名说明](https://learn.microsoft.com/en-us/windows-hardware/drivers/install/the-testsigning-boot-configuration-option)

### 手动尝试安装与回滚

专用配置包附带 `Try-Install.cmd`。完整解压并接好外接鼠标后，右键以管理员身份运行，会验证驱动哈希和精确 INF，再实际执行 `pnputil /add-driver ListFXFlowPtp.inf /install`，把原始输出、退出码和匹配设备的 stack / drivers 查询写入同目录 `ptp-install-时间.log`。缺少签名 CAT 不会被脚本提前拦截，最终由 Windows 当前策略决定。脚本不更改启动签名策略、Secure Boot 或内存完整性，不自动重启。

通用 Observer 包根目录没有 target.json；入口会定位 `Profiles` 中唯一的专用配置，而专用包仍直接使用根目录配置。缺少配置或存在多个型号时显示明确错误，不任意选择设备。日志仍写入入口所在目录。旧包若在根目录报 target.json 不存在，可进入 `Profiles\ASUF1209-Col02` 运行该目录的入口。

PnPUtil 接受安装不能证明驱动加载或触点可读。若安装失败或观察器仍为空，请先提供安装日志。若被接受，再以管理员身份进行十秒只读采样。过滤器失败可能让设备栈无法启动，测试时应有外接鼠标作为退路。[微软开发测试说明](https://learn.microsoft.com/en-us/windows-hardware/drivers/install/installing-an-unsigned-driver-during-development-and-test)解释了加载签名策略和过滤器影响。

回滚运行 `Rollback.cmd`，输入安装输出中的发布名称（`oem数字.inf`）。它核对 Windows INF 中本项目的 ExtensionId、服务和本包硬件 ID 后，仅执行该包的 `pnputil /delete-driver oem数字.inf /uninstall`，不使用 `/force` 或 `/reboot`。安装被拒绝且未生成发布名称时，不必执行回滚。命令依据见 [PnPUtil 文档](https://learn.microsoft.com/en-us/windows-hardware/drivers/devtest/pnputil-command-syntax)。

## 尚需实机验证

当前 Mac 无法加载 Windows 内核驱动。Windows CI 构建、缓冲区测试和模拟帧测试不能证明这台笔记本的过滤器附着层级、Read 完成回调、实际 HID report 布局、拔插 / 睡眠 / 恢复、卸载与原输入放行可靠。拿到设备诊断、有效签名和测试环境后，先完成这些验证，再开发内核输入接管；当前包没有提供内核屏蔽开关。

## 原始参考

- [Microsoft HID architecture](https://learn.microsoft.com/en-us/windows-hardware/drivers/hid/hid-architecture)
- [Microsoft Kbfiltr raw PDO sideband sample](https://github.com/microsoft/Windows-driver-samples/tree/main/input/kbfiltr)
- [Obtaining HID reports](https://learn.microsoft.com/en-us/windows-hardware/drivers/hid/obtaining-hid-reports)
- [Precision Touchpad report contract](https://learn.microsoft.com/en-us/windows-hardware/design/component-guidelines/touchpad-windows-precision-touchpad-collection)
- [WDK NuGet setup](https://learn.microsoft.com/en-us/windows-hardware/drivers/install-the-wdk-using-nuget)
