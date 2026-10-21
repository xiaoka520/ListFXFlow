ListFXFlow ASUF1209 / Col02 测试签名驱动

这个 ZIP 包含已测试签名的 SYS、CAT 和公开 CER。没有私钥，不是微软正式签名发行驱动。证书有效期约 90 天，实际截止时间和指纹见 test-signing.json。

按顺序操作：
1. 完整解压到 Windows 本机目录。接好外接鼠标，保存 BitLocker／设备加密恢复密钥。
2. 右键 Import-TestCertificate.cmd，以管理员身份运行。它只导入这个包指纹对应的测试证书到本机 Root 和 TrustedPublisher。
3. 管理员终端执行 bcdedit /set testsigning on，然后由你手动重启。如果提示受 Secure Boot 保护，需在保存好恢复密钥后另行处理 BIOS 安全启动。脚本不会自动更改这些设置，也不自动关闭内存完整性。
4. 重启后，右键 Try-Install.cmd，以管理员身份运行。把 ptp-install-时间.log 留好；安装被接受不等于驱动已加载。
5. 先保持多指转发试验关闭，用管理员 PowerShell 运行：
   & "$env:LOCALAPPDATA\Programs\ListFXFlow\ListFXFlow.exe" --touchpad-hid-diagnostics --capture-seconds 10
   十秒内用一至五指触摸，提供 touchpad-hid.json。先确认 Observers、采样帧和错误，再开启转发试验。

回滚：
1. 管理员运行 Rollback.cmd，输入安装输出中本项目的 oem数字.inf。只移除本项目扩展包。
2. 管理员运行 Remove-TestCertificate.cmd，移除这个包的测试证书。它会先检查本项目驱动包是否已卸载。
3. 管理员终端执行 bcdedit /set testsigning off，手动重启。若改过 Secure Boot，恢复原设置。

证书导入／清理和驱动安装都是独立入口；签名包验证不能证明实体触控板上的加载、稳定性或手势效果。此驱动仍只观察报告，不提供内核手势屏蔽；现有 SDK 继续负责本机屏蔽。

安装入口会只读查询当前正在生效的 Code Integrity 标志；仅修改 BCD 而未重启不算开启成功。若当前会话尚未允许测试签名驱动，入口会在修改设备栈前停止。不会自动关闭内存完整性。

微软测试签名说明：https://learn.microsoft.com/en-us/windows-hardware/drivers/install/the-testsigning-boot-configuration-option
