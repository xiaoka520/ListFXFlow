# Windows 键盘到 Mac：Caps 与中文输入法

2026-10-18 查阅。build 10、11 均已被用户真实输入确认失败，不能标记为修复。以下 build 12 为先前实验记录，已被后续代码取代。build 18 以磁盘上的 build 17 为基础，保留现有 Caps 实现，没有重新加入这些实验路径；本轮只处理登录组件、指定状态文案与指针停放。

| 项目 | 公开实现 | 对本项目的意义 |
| --- | --- | --- |
| Deskflow / Input Leap | 修饰键通过 IOHIDPostEvent 的 NX_FLAGSCHANGED 更新全局标志，普通键不重设这些标志 | build 11 已采用相近方式，但用户仍复现，说明这不足以解决当前问题 |
| RustDesk / rdev | 在 VirtualInput 中复用一个 CombinedSessionState CGEventSource，通过该源生成 Caps 按下/释放和后续键盘事件；不把键盘事件的 flags 一律替换成几个修饰位 | 改为复用键盘源，保留系统生成的其余标志；不使用 Unicode 替代文本 |
| ckb-next macOS legacy | Caps 除 NX_FLAGSCHANGED 外，还发送 NX_SYSDEFINED、subtype 8、special-key ID 4 的按下/抬起通知 | 补齐当前缺少的系统事件；它不是中文混输已通过的证据 |

来源：

- [Deskflow OSXKeyState.cpp](https://github.com/deskflow/deskflow/blob/master/src/lib/platform/OSXKeyState.cpp)
- [Input Leap OSXKeyState.cpp](https://github.com/input-leap/input-leap/blob/master/src/lib/platform/OSXKeyState.cpp)
- [RustDesk 服务端输入与 Caps 状态](https://github.com/rustdesk/rustdesk/blob/master/src/server/input_service.rs)
- [RustDesk rdev VirtualInput](https://github.com/rustdesk-org/rdev/blob/master/src/macos/simulate.rs)
- [ckb-next Caps 系统事件](https://github.com/ckb-next/ckb-next/blob/master/src/daemon/input_mac_legacy.c)
- [Apple IOHIKeyboard.cpp](https://github.com/apple-oss-distributions/IOHIDFamily/blob/main/IOHIDSystem/IOHIKeyboard.cpp)

## 不能从参考项目推断已经解决

Deskflow 的 [#9984](https://github.com/deskflow/deskflow/issues/9984) 报告 Windows 控制 Mac 时中文组合状态失效；Input Leap 的 [#2057](https://github.com/input-leap/input-leap/issues/2057) 报告远端 Caps 与本地输入源切换语义不同。它们并非本问题的直接修复，也不能作为本应用实际行为通过的证据。

本机仅构造事件、不发布输入的检查发现：CGEvent 创建的按键还有修饰位之外的标记，先前工厂整体赋值 flags 会清掉这些位。新工厂仅替换程序管理的五种修饰位。尚未证实被清除的标记与当前 IME 故障存在直接因果关系。

## build 12 历史实验（已取代）

- 复用键盘 CGEventSource，鼠标继续使用独立源；键盘发布不经过会整体重设 flags 的鼠标辅助方法。
- Caps 状态同步同时补发系统按下、锁定状态变化、系统抬起通知，不更改用户输入源。
- 不发送大写 Unicode 替代文本，不读取或改写目标应用的输入文本。
- 29 项 Swift 检查通过，新增系统事件载荷和键盘标志保留检查。测试只构造事件，不对桌面模拟输入。
- 等待用户在真实连接下连续输入验证大写及候选保留；不使用逐步点击按钮的测试结论。
