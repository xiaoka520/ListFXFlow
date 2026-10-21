ListFXFlow PTP 安装尝试

1. 完整解压专用 ZIP，先接好外接鼠标。未验证的过滤驱动可能让触控板暂时不可用。
2. 右键 Try-Install.cmd，选择“以管理员身份运行”。
3. 记录 Windows 返回的原始结果，把同目录 ptp-install-时间.log 发来。

通用 Observer ZIP 的根目录入口会自动定位 Profiles 中唯一的专用配置；专用 ZIP 直接使用根目录配置。存在多个型号时，请进入所需型号的 Profiles 子目录运行入口。日志保存在你启动入口的目录。

当前包未签名，缺少签名 CAT，Windows 默认策略可能拒绝。入口仍会实际调用 PnPUtil 尝试安装，不会因为这个已知条件提前拒绝执行。
PnPUtil 接受安装不代表驱动加载，也不代表已经读到触点。脚本不会自动重启，不更改 Secure Boot、内存完整性、启动签名策略或永久 PowerShell 策略；启动器的 ExecutionPolicy 参数仅用于本次 PowerShell 进程。

如果需要回滚：右键 Rollback.cmd，以管理员身份运行，输入安装输出中的 Published Name / 发布名称（例如 oem123.inf）。脚本核实该 INF 属于本项目和本触控板后，只执行该包的 /delete-driver /uninstall；不会移除原厂触控板驱动，也不会自动重启。安装被拒绝且没有产生 OEM INF 时，无需回滚。

若安装被接受，以管理员身份运行：
& "$env:LOCALAPPDATA\Programs\ListFXFlow\ListFXFlow.exe" --touchpad-hid-diagnostics --capture-seconds 10
采样期间先不要打开多指转发试验开关。用一至五指触摸，随后提供 touchpad-hid.json；只有 Observers、采样帧与错误结果能进一步确认读取情况。

官方命令说明：https://learn.microsoft.com/en-us/windows-hardware/drivers/devtest/pnputil-command-syntax
