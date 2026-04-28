# KOF 6-Key Mapper

## 功能
1. 为 `WASD` 增加两个组合方向键：`Q = A + S`，`E = D + S`。
2. `Q -> A,S` 和 `E -> D,S` 的第二个按键使用可配置的随机延迟发送。
3. 支持全局启停热键，默认是 `Right Arrow`，可通过配置文件修改。
4. 支持 `Auto Fire` 连击功能，默认开启，默认连击键是 `U`。
5. 连击键支持配置多个；按下时先触发一次，持续按住超过设定时长后开始自动连发。
6. `Pause` 可显示或隐藏主窗口，双击托盘图标也可以切换显示状态。
7. 内置状态恢复逻辑，尽量在组合键状态异常或钩子异常后自动释放残留按键并恢复工作。

## 配置文件
程序启动时会在可执行文件同目录查找 `kof6key.ini`。

- 找到配置文件：按文件内容加载。
- 没找到配置文件：使用程序内置默认值运行。

当前支持的配置项：

- `ComboDelayMinMs`：组合键随机延迟的最小值，单位毫秒。
- `ComboDelayMaxMs`：组合键随机延迟的最大值，单位毫秒。
- `ToggleHotkey`：全局启停热键，键名使用 `System.Windows.Forms.Keys` 枚举名称。
- `AutoFireEnabled`：是否默认开启连击功能，`true` 或 `false`。
- `AutoFireKeys`：连击键列表，支持多个键。
- `AutoFireHoldDelayMs`：按住多久后开始自动连发，单位毫秒。

`AutoFireKeys` 支持以下分隔方式：

- 逗号：`U,I,O`
- 竖线：`U|I|O`
- 空格：`U I O`

示例：

```ini
ComboDelayMinMs=10
ComboDelayMaxMs=30
ToggleHotkey=Right
AutoFireEnabled=true
AutoFireKeys=U,I
AutoFireHoldDelayMs=500
```

说明：

- `10` 和 `30` 表示 `A/S` 或 `D/S` 之间的第二个按键会在 `10-30ms` 内随机发送。
- 多个连击键同时按住时，各自独立计时、独立连发。
- 连击开始后的重复间隔目前固定为 `50ms`。

## 实现说明
1. 使用 Win32 全局低级键盘钩子监听按键。
2. 使用 `keybd_event` 发送键盘按下和松开事件。
3. 使用 WinForms 定时器统一调度组合键第二段发送、连击和异常恢复检查。
4. 项目兼容 `.NET Framework 4.x` 编译环境，便于在常见 Windows 环境直接运行。

## 构建
在项目目录执行：

```powershell
.\build.ps1
```

构建完成后：

- 可执行文件输出到 `dist\kof6key.exe`
- 示例配置文件会复制到 `dist\kof6key.ini`
