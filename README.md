# KOF 6 键映射工具

## 功能

1. 在 `WASD` 的基础上增加两个组合方向键：`Q` 触发一轮 `A,S`，`E` 触发一轮 `D,S`。
2. 采用按键精灵兼容模式：组合键按串行点击方式执行，当前一轮未完成前不会开始新的 `Q/E` 处理。
3. `A/S` 或 `D/S` 之间的第二个按键使用可配置的随机延迟发送。
4. 支持全局启停热键，默认是 `Right Arrow`，可通过配置文件修改。
5. `Pause` 用于显示或隐藏主窗口，双击托盘图标也可以切换窗口显示状态。
6. 内置状态恢复逻辑，尽量在异常后恢复工作。
7. 支持回车键切换功能：按回车键可手动切换按键映射的启用/禁用状态，方便在游戏和聊天窗口之间切换。

## 配置文件

程序启动时会在可执行文件同目录查找 `kof6key.ini`。

- 如果找到配置文件，就按文件内容加载。
- 如果没有找到配置文件，就使用程序内置默认值运行。
- 每次通过启停热键重新开启映射时，程序都会重新读取一次 `kof6key.ini`，方便边改配置边测试，无需重启程序。

当前支持的配置项：

- `ComboDelayBaseMs`：组合键延迟的基础值（推荐使用此配置），单位毫秒。
- `ComboDelayVarianceMs`：组合键延迟的随机变化范围，单位毫秒。
- `ToggleHotkey`：全局启停热键，键名使用 `System.Windows.Forms.Keys` 枚举名称。
- `VerboseLogging`：是否启用详细日志记录，设置为 `true` 时会输出详细的调试信息到 `kof6key.log` 文件。
- `EnterKeyToggle`：是否启用回车键切换功能，设置为 `true` 时，按回车键可切换按键映射的启用/禁用状态。

### ToggleHotkey 配置说明

`ToggleHotkey` 用于设置启用/禁用映射功能的全局热键，按下此键可切换映射的开启和关闭状态。

#### 支持的按键名称

键名使用 `.NET Framework` 的 `System.Windows.Forms.Keys` 枚举值，以下是常用示例：

| 按键类型 | 示例 | 说明 |
|---------|------|------|
| 方向键 | `Right`, `Left`, `Up`, `Down` | 方向键 |
| 字母键 | `A`, `B`, `Q`, `E`, `F1`-`F12` | 字母键和功能键 |
| 数字键 | `D0`-`D9`, `NumPad0`-`NumPad9` | 主键盘数字和小键盘数字 |
| 控制键 | `Ctrl`, `Shift`, `Alt` | 控制键 |
| 特殊键 | `Space`, `Enter`, `Tab`, `Escape`, `Pause` | 特殊功能键 |
| 符号键 | `Oemcomma`(,), `OemPeriod`(.), `OemQuestion`(?) | 符号键 |

#### 配置示例

```ini
ComboDelayMinMs=25
ComboDelayMaxMs=30
ToggleHotkey=Right
```

```ini
# 使用 F8 作为开关热键
ComboDelayMinMs=40
ComboDelayMaxMs=45
ToggleHotkey=F8
```

```ini
# 使用空格键作为开关热键
ComboDelayMinMs=10
ComboDelayMaxMs=20
ToggleHotkey=Space
```

```ini
# 完整配置示例
ComboDelayBaseMs=40
ComboDelayVarianceMs=5
ToggleHotkey=Right
VerboseLogging=false
EnterKeyToggle=true
```

#### 说明

- `10` 和 `30` 表示 `A/S` 或 `D/S` 之间的第二个按键会在 `10-30ms` 内随机发送。
- 每轮执行流程是：先点击第一个键，等待一段随机延迟，再点击第二个键，再等待一段同范围随机延迟后才允许进入下一轮。
- 如果 `ComboDelayMinMs` 大于 `ComboDelayMaxMs`，程序会自动交换这两个值。
- 配置文件中以 `#` 或 `;` 开头的行为注释行，不会被解析。

### EnterKeyToggle 配置说明

`EnterKeyToggle` 用于启用或禁用回车键切换功能。

#### 功能说明

当此功能启用时：
1. 每次按下回车键，程序会直接切换按键映射的启用/禁用状态
2. 右下角托盘图标会同步更新当前状态（绿色=启用，红色=暂停）

#### 配置示例

```ini
# 启用回车键切换功能
EnterKeyToggle=true

# 禁用回车键切换功能
EnterKeyToggle=false
```

#### 使用场景

| 场景 | 当前状态 | 按回车键后的效果 |
|------|---------|-----------------|
| 游戏中 | 启用 | 按键映射暂停（图标变红） |
| 聊天窗口 | 暂停 | 按键映射恢复（图标变绿） |
| 游戏中 | 暂停 | 按键映射恢复（图标变绿） |
| 聊天窗口 | 启用 | 按键映射暂停（图标变红） |

#### 说明

- 默认值为 `true`，即启用回车键切换功能
- 此功能对于需要频繁在游戏和聊天之间切换的用户非常有用
- 即使启用了此功能，用户仍然可以使用全局启停热键手动控制映射状态

## 实现说明
1. 使用 Win32 全局低级键盘钩子监听按键。
2. 使用 `keybd_event` 发送键盘按下和松开事件。
3. 使用 WinForms 定时器轮询物理按键状态，并调度组合键的串行点击流程。
4. 项目兼容 `.NET Framework 4.x` 编译环境，便于在常见 Windows 环境直接运行。

## 构建
在项目目录执行：

```powershell
.\build.ps1
```

构建完成后会生成：

- `dist\kof6key.exe`
- `dist\kof6key.ini`
