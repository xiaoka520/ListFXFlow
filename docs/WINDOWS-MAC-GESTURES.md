# Windows → Mac 多指手势试验

0.2.3 build 41 新增默认关闭的单向试验。更新两端后，在 Mac 打开“接收 Windows 多指手势（实验）”，在 Windows 打开“向 Mac 转发多指手势（实验）”。指针在 Mac 且双方都开启时才发送三指及以上的触点；返回 Windows、关闭开关或断开连接会结束当前手势。

当前试验范围是三指 / 四指横向与纵向滑动。Windows 使用官方 `TouchpadGesturesController` 的全局触点回调；Mac 根据触点轨迹构造 Quartz / Dock 手势事件。没有改成键盘快捷键，也没有注册虚拟 HID 触控板。捏合、旋转、多指轻点和完整原生触控板识别不属于这次试验范围。

双指滚动继续使用 build 40 的系统滚轮识别与独立传输通道。Windows 手势窗口不注册双指触点接管；手势转发关闭时，原来的本机多指手势屏蔽仍然工作。旧的 `TouchpadEnabled` 偏好不会自动开启本次试验。

## Mac 接收差异

macOS 27 的合成 Dock 滑动需要 IOHID 数据附件、相应的通用手势事件，并与早期系统采用相反的横向方向编码。build 41 根据 strafe 的实现补齐这一序列，同时将横纵速度分开。早期系统保留原有单事件格式。

“Mac 已收 N 帧 / 已提交 M 个事件”区分网络接收和本机 API 提交，不能证明 WindowServer、Dock 或目标应用已经执行了手势。需要在真实 Windows 触控板和 Mac 上验证横向桌面 / 全屏应用切换、纵向调度中心、抬指结束、返回 Windows 后恢复，以及滚动和指针并行流畅度。

本机显示“触控板”设置页只是设备存在缓存的变化，不等于已注册设备，不作为手势接收成功的判据。这里的试验路径也不依赖该缓存更改。

## 同类实现与资料

- [Deskflow 多点触控讨论](https://github.com/deskflow/deskflow/discussions/9437)：项目答复未提供多点触控直接互通；不能把普通滚轮支持当作多指手势支持。
- [Synergy 历史手势讨论](https://forums.symless.com/topic/6834-mac-gestures-on-second-macbook-pro/)：该历史答复将手势列为未来功能，不足以证明当前产品已支持 Windows → Mac 原生多指。
- [macos-trackpad-companion](https://github.com/scottlamb/macos-trackpad-companion)：把直连 Mac 的 PTP 触控板触点转换成 Mac 手势事件的开源原型，并非 Windows 电脑到 Mac 的网络共享产品。
- [strafe](https://github.com/rileycx/strafe)：本机 Mac 合成系统桌面切换手势的实现，包含 macOS 27 兼容处理；它本身不是跨电脑共享软件。参考修订和许可证见 `THIRD_PARTY_NOTICES.txt`。
- [Microsoft TouchpadGesturesController](https://learn.microsoft.com/en-us/windows/win32/input-precisiontouchpad/touchpadgesturescontroller)：捕获三指及以上系统手势的官方 API，需要前台所有权。

截至本次调查，没有核实到 Deskflow / Synergy 已提供符合本项目要求的 Windows → Mac 完整原生多点触控转发；不据此声称所有同类软件都没有实现。

## 本次本地验证

Mac 69 项测试通过，包括三指触点到完整 Dock 事件 / 配套事件序列、结束取消及旧事件拒绝；Windows 291 项检查通过，包括全局三指发送的抬指重置、默认关闭和独立 worker 模式切换。真实 .NET / Swift 回环 TCP 验证 312 项检查通过。Windows WPF 编译、x64 发布、Mac 签名和 DMG 完整性检查通过。以上不替代两台实机的手势执行验收。
