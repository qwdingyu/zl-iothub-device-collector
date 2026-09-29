# zl-iothub-device-collector

用 **ZL.IotHub** 编写"设备采集 C# 程序"的可仿写技能包（Skill）—— 教你怎么**一次写对**：连接、标签地址、批量读取、消息质量、写值，全部有可编译可运行的示例背书。

> 技术内核（ZL.IotHub）以 NuGet 引用（闭源产品）；本目录为**方法论 + 模板 + 地址速查**，开源发布。

## 这是什么

| 文件 | 作用 |
|------|------|
| `SKILL.md` | 5 步工作流（连接初始化 → 标签与地址 → 批量读取 → 消息质量 → 写值与清理） |
| `address/` | **17 协议地址速查**（四维检查法：进制 / 临界值 / 位寻址模式 / 仅字位）——地址错 = 现场灾难，必须先查再写 |
| `examples/` | 可编译、可运行的采集示例（M1–M5 全流程，模拟器验证过：连接 / 批量读 / 变更事件+质量码 / typed 写值回读）—— `CollectorExamples/`（S7）+ `ModbusExample/`（Modbus）+ `MitsubishiMcExample/`（三菱 MC） |
| `checklist.md` | 仿写后逐条勾选的验收清单（含高频地址陷阱速查） |

## 快速开始（一条命令跑通最小采集）

前置：.NET 8 SDK；仿真器**必须常驻**（`--headless --stay-alive`，缺 `--stay-alive` 服务 1 秒即退）或真实 PLC 可达。

```bash
# S7 示例（默认连 127.0.0.1:2002；S7 模拟器先起，如 --protocols siemens-s7 --ports s7=2002 --headless --stay-alive --auto-start）
dotnet run --project examples/CollectorExamples/CollectorExamples.csproj

# Modbus 示例（默认连 127.0.0.1:31502；Modbus 模拟器：--protocols modbus-tcp --ports modbus=31502 --headless --stay-alive --auto-start）
dotnet run --project examples/ModbusExample/ModbusExample.csproj

# 三菱 MC 示例（默认连 127.0.0.1:35000；MC 模拟器：--protocols mc --ports mc=35000 --headless --stay-alive --auto-start）
dotnet run --project examples/MitsubishiMcExample/MitsubishiMcExample.csproj
```

输出应包含：连接成功、`DataCollected` 快照（每周期标签值，类型保真）、变更事件（含质量码）、typed 写值后读回生效。

## 仿写路径

1. 读 `SKILL.md`（5 步）；
2. 写标签表前先查 `address/<协议>.md`（地址四维核对；Modbus 先看「〇、三栈差异」）；
3. 抄 `examples/` 对应协议示例的结构（引擎批量读 + 事件拿值，不要逐标签轮询）；
4. 写完逐条过 `checklist.md`。

## 授权

- 内核：`ZL.IotHub`（NuGet）——免费版 **≤10 台、协议全开、可读可写、完整编排**（官方免费版额度）；>10 台走正式授权；
- **禁止**把官方示例中的"演示授权路径"复制进产品代码（授权门禁要求）。

## 溯源说明

文中 `docs/NNN` 为**官方文档溯源标记**（对应 ZL.IotHub 官方文档目录），仅用于查证；本发布物**不包含任何逆向工程出处**。

## 许可

开源发布（许可证待定）；技术内核 `ZL.IotHub` 为商业闭源产品。
