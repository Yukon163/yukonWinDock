# yukonWinDock

Windows 11 任务栏小工具。默认显示在主任务栏托盘折叠箭头左侧，点击展开控制面板，再次点击收起。

## 当前功能

- 外接显示器硬件亮度：DDC/CI，支持选屏、滑块、预设值和任务栏亮度上的鼠标滚轮；设置后读取确认。不会调节笔记本内屏。
- 每秒更新下载、上传、CPU 平均有效频率、CPU 占用率和内存占用率。网速默认取默认路由的网卡，也可手选，避免多个虚拟网卡重复统计。
- 两只风扇分别显示 RPM，约每三秒采样。支持 Lenovo Capability / GameZone / Fan WMI，并兼容已开启 WMI 的 LibreHardwareMonitor / OpenHardwareMonitor。
- 八项任务栏数据独立可选：下载、上传、CPU 频率、CPU 占用率、内存、风扇 1、风扇 2、外接屏亮度。全部关闭时仅保留一个小入口。
- 自定义快捷操作：打开程序、文件、文件夹或 http/https 网址；可添加、移除并保存。
- 可选登录启动、水平偏移；不默认添加自启。支持单实例、Explorer 重启后重新定位、任务栏隐藏时隐藏、全屏时隐藏。

## 运行

下载构建产物后运行 `yukonWinDock.exe`，无需安装 .NET。

- 单击任务栏小工具或托盘图标展开/收起。
- 亮度区域滚轮每格调节 5%。
- 在控制面板底部的“任务栏显示内容（全部可选）”勾选显示项目；立即保存。
- 联想风扇接口可能要求管理员权限，点击面板中的重启按钮。程序只读取转速，不修改风扇曲线，也不安装驱动。
- 右键托盘图标可退出；打开面板时按 Esc 可收起。
- `yukonWinDock.exe --panel` 启动时打开并保持面板；`--quit` 请求已有实例退出。

已在 Lenovo 83DF / i9-14900HX / Windows 11 26200 / 150% 缩放 / Redmi 27 NU 上验证。管理员模式通过 `LENOVO_OTHER_METHOD.GetFeatureValue` 的 `0x04030001`、`0x04030002` 读取两只风扇。旧 GameZone 方法在这台机型上虽然存在，但不返回有效转速。

## 构建

需要 Windows 和 .NET 10 SDK。

```powershell
dotnet test yukonWinDock.sln -c Release
.\scripts\build.ps1 -Publish
```

自包含单文件输出：`artifacts\win-x64\yukonWinDock.exe`。`scripts/build.ps1` 会优先使用项目内 `.tools\dotnet`，否则使用 PATH 中的 SDK。

GitHub Actions 在 Windows 上构建、运行测试并上传可运行文件。无物理显示器的 CI 会验证读取与错误路径；开发机测试还会把当前亮度写回原值并读取确认。

## 实现和限制

原生 C# / WPF / .NET 10。亮度使用 Windows `dxva2.dll`，CPU 使用 `GetSystemTimes` 和英语性能计数器，网速使用网卡字节差分与实际采样间隔。

Windows 11 不提供传统 DeskBand 扩展。本项目使用独立、不抢焦点的任务栏覆盖窗口，不注入 Explorer，不修改任务栏内部窗口。当前定位主任务栏；该区域不会被系统正式预留，任务栏按钮较多时可关闭部分项目或调整左移量。尚未实现多任务栏分别配置、垂直任务栏和自动避让所有任务栏按钮。

CPU GHz 是 Windows 性能计数器估算的平均有效频率，不表示每个核心瞬时频率。网络单位按 1024 进位；首帧不显示连接以来的累计流量。风扇不可读时显示 `—` 和实际失败状态，不将目标转速冒充实测值。

设置位于 `%LOCALAPPDATA%\yukonWinDock\settings.json`；风扇诊断位于同目录 `fan-diagnostics.json`。没有应用遥测或后台上传；快捷操作只有用户点击时才打开其目标。

## 参考接口

- [Microsoft GetMonitorBrightness](https://learn.microsoft.com/en-us/windows/win32/api/highlevelmonitorconfigurationapi/nf-highlevelmonitorconfigurationapi-getmonitorbrightness)
- [Microsoft PdhAddEnglishCounter](https://learn.microsoft.com/en-us/windows/win32/api/pdh/nf-pdh-pdhaddenglishcounterw)
- [LenovoLegionToolkit WMI capability identifiers](https://github.com/LenovoLegionToolkit-Team/LenovoLegionToolkit/blob/master/LenovoLegionToolkit.Lib/Enums.cs)

只参考公开接口及标识符；没有引入第三方硬件驱动。
