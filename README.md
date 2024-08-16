# 游戏键盘6向设置
## 功能
1. 在WASD键盘的基础上，支持1、3方向键，分别用QE代表。
2. 启停开关切换使用 → （键盘上的方向键）
3. 显示隐藏窗口使用热键Pause，键盘上这个按钮一般没什么用。
## 开发相关
1. 使用了MouseKeyHook插件，可以使用如下命令安装
   ```cmd
     nuget install MouseKeyHook
   ```
2. 使用visual studio默认的运行目录下，直接提取exe就可以使用了，如果想发布，用vs的发布功能发布新的exe。
