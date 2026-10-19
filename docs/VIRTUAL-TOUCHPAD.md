# Mac 虚拟触控板与授权

build 32 已隐藏并停用跨系统手势映射；本页保留为研究记录。当前只启用 Windows 在控制 Mac 时自动屏蔽本机手势，屏蔽不依赖这里的虚拟 HID 权限。

build 30 的 Mac 接收后端是 Quartz 手势事件（build 32 已停用），**没有注册真实的虚拟触控板**。接收到触点、成功提交事件、系统实际响应手势是三个不同的结果。窗口切换、桌面切换等系统手势仍需真机验收。

## 为什么系统设置中的审批不够

辅助功能、输入监控、系统扩展批准属于用户对本机的授权。虚拟 HID 的签名 entitlement 属于可执行程序的能力，两者不能互相替代。

Apple 的 `IOHIDResourceDeviceUserClient::initWithTask` 检查 `com.apple.developer.hid.virtual.device` 或 Apple 内部的 user-access-device entitlement。缺少二者就拒绝创建设备；该检查没有管理员/root 豁免。把 entitlement 写进 plist、临时签名或用户批准辅助功能都不能据此声称已获得该能力。

需要在 Apple 开发者账号确认相应能力可用，按账号要求申请，并使用包含授权的 provisioning profile 和签名。普通主程序暂不加入受限 entitlement，以免整个键鼠共享程序因签名授权不匹配而无法启动。

## 当前检测入口

```sh
/Applications/ListFXFlow.app/Contents/MacOS/ListFXFlow --virtual-touchpad-status
```

它只读检查当前可执行文件的实际签名，不弹权限请求、不创建设备、不发送输入。输出 `unavailable` 表示缺少签名 entitlement；`signing entitlement present` 也仅表示签名声明存在，不代表设备成功创建或系统手势识别通过。

## 实际设备注册实验

新增独立的 `Tools/VirtualTouchpadProbe.swift`，不在日常共享程序中加载。使用标准 Digitizer / Touch Pad 顶层集合、五个 Finger 集合、Confidence / Tip Switch / Contact ID / 物理 X/Y、100µs 扫描时间、Contact Count 和最大触点数 Feature Report。输入报告含 ID 共 34 字节，描述符 377 字节；使用本地实验 VID `0xFFFF`，不冒用 Apple 设备身份。

```sh
# 仅检查描述符的短项结构、五指集合、报告位数和错误布局拒绝：
zsh scripts/probe-virtual-touchpad.sh --self-test
# 真正调用 CoreHID 注册。不会发送手势、点击或按键报告：
zsh scripts/probe-virtual-touchpad.sh
```

若注册成功，工具激活只读报告回调，打印设备 Registry ID 与系统解析的 Usage，保留设备三秒，进程退出时释放连接。成功注册不代表已绑定多点触控手势驱动；需要另行验证原生手势。此工具没有接入普通共享或改变已有的 Quartz 接收后端。

2026-10-19 在当前 Mac（macOS 27.0.1 / 26A434）实测：

```text
Descriptor: 377 bytes; Digitizer 0x0D / Touch Pad 0x05; five fingers
Signing entitlement present: false
IOHIDResource device-user-client open: 0xe00002c2
User-client open failed before any report descriptor was submitted: (iokit/common) invalid argument
RESULT: CoreHID returned nil. No virtual touchpad was registered; no input reports sent.
```

`0xe00002c2` 的含义是 invalid argument，错误码本身并不是明确的“缺少 entitlement”诊断；但该失败发生在提交描述符之前。结合当前程序缺少签名 entitlement、Apple SDK 头文件明确要求该 entitlement、Apple 开源实现对它的检查，可以确定当前构建不满足公开虚拟设备 API 的签名要求。不能通过修改触点报告字段来补齐授权。未向主程序签入未经授权的受限 entitlement，也没有修改 SIP/AMFI 或安装系统扩展。

运行证据在 `.build/virtual-touchpad-probe-layout.log` 与 `.build/virtual-touchpad-probe-registration.log`。本次只新增实验工具与文档，BUILD 保持 31，保留 Mac build 30 / Windows build 31 安装包。

## 虚拟设备后端的验收条件

获得授权后，CoreHID 的 `HIDVirtualDevice` 可作为独立后端。还需确认 HID 描述符、触点报告、全抬起/取消、会话退出时释放、系统设备识别及系统手势响应。通用 Digitizer/Touch Pad usage 成功注册，不等于 macOS 会把它当作支持系统手势的多点触控板。不能用创建键盘/鼠标的成功经验来替代验证。

Windows 捕获继续使用官方 Windows SDK 的 `TouchpadGesturesController` 投影，位于独立进程。双方协商 `touchpad-feedback-v1` 后才发送回执；回执按每次手势编号隔离，区分触点接收与本机 API 提交，不能证明目标应用或系统最终执行了动作。

## 原始资料

- [Apple 虚拟设备文档](https://developer.apple.com/documentation/corehid/creatingvirtualdevices)
- [HID virtual device entitlement](https://developer.apple.com/documentation/bundleresources/entitlements/com.apple.developer.hid.virtual.device)
- [Apple IOHIDResourceUserClient 源码](https://github.com/apple-oss-distributions/IOHIDFamily/blob/main/IOHIDFamily/IOHIDResourceUserClient.cpp)
- [Apple 受管理能力申请流程](https://developer.apple.com/help/account/capabilities/capability-requests/)
- [Microsoft TouchpadGesturesController](https://learn.microsoft.com/en-us/windows/win32/input-precisiontouchpad/touchpadgesturescontroller)
