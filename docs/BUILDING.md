# 开发与构建

本页面向开发者。安装与日常使用请查看 [README](../README.md)。版本号由仓库根目录的 `VERSION` 与 `BUILD` 决定；未发布期间只增加 `BUILD`。

## 本地构建

Mac（安装 Xcode 或支持的 Swift 工具链）：

```sh
swift test
zsh scripts/build-mac.sh
```

输出 `artifacts/ListFXFlow.app`、`artifacts/ListFXFlow-Mac-arm64-<版本>-b<构建>.dmg` 和同名 ZIP。设置 `SIGN_IDENTITY` 可以指定签名身份，设为 `-` 可显式使用 ad-hoc。若系统默认只有 Command Line Tools，脚本会优先使用 `/Applications/Xcode.app`。

Windows（.NET 10 SDK）：

```powershell
dotnet run --project windows/FlowCore.Tests/FlowCore.Tests.csproj -c Release
dotnet run --project windows/ClipboardNative.Tests/ClipboardNative.Tests.csproj -c Release
./scripts/build-windows.ps1
```

输出免安装 ZIP 与 `artifacts/ListFXFlow-Setup-x64-<版本>-b<构建>.exe`。安装包把自包含的 `ListFXFlow.exe` 嵌入自身，安装到当前用户目录并注册卸载入口，不需要管理员权限。

也可以在 Mac 用 .NET SDK 交叉构建 Windows 程序（不能代替 Windows 上的输入和 UI 验收）：

```sh
dotnet publish windows/ListFXFlow/ListFXFlow.csproj -c Release -r win-x64 \
  --self-contained true -p:PublishSingleFile=true \
  -p:IncludeNativeLibrariesForSelfExtract=true -o artifacts/Windows-x64
```

自动化检查见 [GitHub Actions 配置](../.github/workflows/build.yml)。网络协议互通测试见 [验证记录](VALIDATION.md)；消息格式与身份协商见 [协议说明](PROTOCOL.md)。

## 实现依据

- [Apple：IP over Thunderbolt](https://support.apple.com/guide/mac-help/mchld53dd2f5/mac) 说明雷雳网桥的 IP 通信。Apple 页面面向 Mac-to-Mac，Windows 端的条件需结合微软文档和真机检查。
- [Microsoft：USB4 域间连接](https://learn.microsoft.com/zh-cn/windows-hardware/design/component-guidelines/usb4-interdomain-connections) 说明 Windows USB4NET 网络支持。
- [Apple：CGEvent](https://developer.apple.com/documentation/coregraphics/cgevent) 提供 macOS 事件截获与注入接口。
- [Microsoft：SendInput](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-sendinput) 说明 Windows 输入注入与 UIPI 权限限制。
- [Microsoft：Raw Input](https://learn.microsoft.com/en-us/windows/win32/inputdev/about-raw-input) 与 [SetCursor](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setcursor) 提供后台鼠标移动与指针显示控制；不替换系统光标主题。
