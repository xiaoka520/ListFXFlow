# HID Virtual Device 申请准备

当前尚未获得授权，也未向 Apple 提交申请。下面是可用于开发者账号 Capability Request 的草稿。

在 Apple Developer 的 Certificates, Identifiers & Profiles 中选择相应 App ID，再进入 Capability Requests 查找 **HID Virtual Device**。建议将该能力放在独立虚拟触控板辅助程序的 App ID 上，使普通键鼠共享主程序可以继续独立运行。获批后需要对应的 provisioning profile 与签名；单独修改 entitlement plist 不等于授权。

[Apple 申请流程](https://developer.apple.com/help/account/capabilities/capability-requests/)

## 英文用途草稿

ListFXFlow is a macOS and Windows input-sharing application for a user-owned Mac and Windows computer connected by a wired Thunderbolt IP link. We request `com.apple.developer.hid.virtual.device` for a planned, separately signed macOS helper that exposes remote touchpad contact frames as a virtual HID Touch Pad.

Touchpad sharing is optional and disabled by default. Both endpoints must explicitly enable it. Input is forwarded only while the pointer controls the receiving computer. The intended helper supports up to five normalized contacts, preserves contact identities and release/cancel transitions, and releases its virtual device when sharing stops or its owning session ends. The application does not record typed text or touchpad input contents. It does not require kernel modification or changes to the user's system gesture configuration.

The current public CoreHID registration experiment fails without the requested entitlement. Existing Quartz event submission does not establish native system gesture recognition. We would also appreciate confirmation of whether a CoreHID `HIDVirtualDevice` with Digitizers / Touch Pad usage (0x0D / 0x05) can participate in macOS native multitouch and system gesture recognition. If it cannot, please advise the supported HIDDriverKit or other device/service architecture and entitlements for a virtual touchpad.

## 获批后需要验证

1. 使用获批辅助程序的 profile、App ID 和签名，确认设备可以创建并在退出后移除。
2. 检查系统绑定的设备/驱动类别和实际多点触控识别。
3. 只有原生识别可用后，再接入当前 Windows 触点流并验收缩放、旋转、三/四指桌面手势及取消/断线释放。

申请或获批本身均不能证明 macOS 已支持此设备的系统手势。
