# APT FPGA UART Receiver

Windows 串口图像与遥测接收助手，以及配套 FPGA 传输 RTL。基于 APT 项目 v18 / PC 接收助手 v3 拆分整理，聚焦图像传输、协议解析和数据显示。

**仓库只包含接收助手和发送配套代码，不包含画质增强、Sobel、质心计算算法、MIPI/HDMI/DDR 控制器 IP 或完整板卡工程。** FPGA 输入图像和坐标需要由集成工程提供；本仓库本身不能直接生成完整系统的可烧写固件。

## 功能

| 功能 | 实现与限制 |
|---|---|
| 流畅预览 | 160×90、RGB565，HXP1，标称 3 Mbaud |
| 清晰预览 | 320×180、RGB565，DDR 快照中心采样，HXP1 |
| 高清抓图 | 1280×720、RGB888，HXF2，完成后保留画面并自动保存 PNG |
| 遥测面板 | 显示外部提供的 XC / YC / DX / DY / N、模式、AGC 和增益 |
| 完整性校验 | CRC16-CCITT-FALSE；失败帧丢弃，保留最近成功图像 |
| 记录与回放 | PNG、遥测 CSV、可选原始流记录、离线回放、带标记的演示数据 |
| 显示改进 | 预览说明每秒统一刷新、双缓冲绘图、可选电脑端数据叠加 |

3M 8N1 的有效载荷带宽约 300 KB/s，无法支持 720p60。720p RGB888 单张像素数据为 2,764,800 字节，标称传输时间约 9.22 秒，另需采集、拷贝和调度时间。清晰档预计约 1–2 fps，尚未实板测得。

## 快速开始：Windows 接收助手

需要 Windows 和 .NET Framework 4.x。源码为 C# / WinForms，无 NuGet 运行依赖。

```powershell
powershell -ExecutionPolicy Bypass -File .\receiver\src\Build.ps1
.\receiver\src\build\APT_Receiver.exe
```

1. 退出占用板卡串口的其它程序。
2. 选择开发板 COM 口，设置 **3000000**，点击连接。
3. FPGA 回传 `V:18` 后，助手请求所选预览档位，默认 320×180。
4. 点击“高清抓图 1280×720”，等待 CRC 校验和自动保存；点击“恢复预览”继续。
5. “叠加板子数据”可显示质心等遥测。保存 PNG 保留图像数据本身，不包含电脑叠加层。

没有硬件时，可用 `APT_Receiver.exe --demo` 检查界面；演示图像不是板子实拍。旧版仅坐标行也兼容，但不会猜测没有回传的模式、AGC 或增益。

## FPGA 集成

入口为 [`firmware/rtl/apt_transport.v`](firmware/rtl/apt_transport.v)。固定接入 1280×720 数据源：每拍四个 RGB888 像素、配套有效/帧首/行末信号，以及外部计算好的遥测。

封装依赖调用方提供时钟、稳定数据、快照源槽位、冻结控制和 DDR 请求仲裁。`grab_ddr_arbiter.v` 是原工程的可选仲裁参考，具有原工程 FIFO 余量等前提，不能作为任意 DDR 控制器的通用即插即用模块。

详细说明：

- [使用说明书](docs/USER_MANUAL.md)
- [FPGA 接入说明](docs/FPGA_INTEGRATION.md)
- [传输协议](docs/PROTOCOL.md)
- [测试与已知限制](docs/VALIDATION.md)
- [责任说明与代码来源](NOTICE.md)

## 目录

```text
receiver/src/          WinForms 界面、协议解析、图像解码与测试
receiver/verification/ 合成/RTL 仿真协议样本，没有用户实拍记录
firmware/rtl/          图像预览、快照拷贝、串口发送/接收、遥测和调度
firmware/tb/           可独立运行的传输测试平台
firmware/constraints/  HX1P35A UART 管脚示例
scripts/              固件仿真与校验脚本
docs/                 说明书、协议、集成和验证边界
```

## 验证状态

完整项目此前的 160×90 预览由用户上板确认，窗口显示约 8.5 fps。该数值不代表本仓库拆分版的上板验证结果。

接收助手协议回归 3959 项、新增检查 552 项通过。完整 320×180 地址与 CRC 仿真检查全部 57600 个采样像素。传输独立封装经过编译和命令调度仿真，**尚未在用户工程中重新综合、布线和上板验证**。原完整工程的时序报告不等于本仓库封装的时序签核。

## 来源与许可状态

仓库由项目持有人发布，代码含历史项目实现、修改和 AI 辅助整理；不声明所有代码均为原创。公开版没有包含厂商 Logo、加密 IP、完整开发板资料、完整系统 bitstream 或个人抓图日志。

本次未替项目所有者选择 MIT 等统一开源许可证；许可证状态和第三方来源见 [NOTICE.md](NOTICE.md)。需要复用或再分发时，应先确认相关代码的许可情况。
