---
name: zl-iothub-device-collector
description: 编写 ZL.IotHub 设备采集 C# 程序的可仿写方法论。覆盖：连接初始化、标签表与地址维护（17 协议，防地址灾难）、批量读取与地址合并、消息与质量码、写值与完整程序骨架。供开发者仿写或 AI 辅助生成/审查采集程序。
---

# ZL.IotHub 设备采集程序编写 Skill

> 本 Skill 教"怎么写对"一台 PLC 采集程序——花 5 步产出可运行、地址正确、带质量码和事件处理的 C# 程序。
> 参考范例：官方批量采集示例 FleetS7（demos 目录，注释完备）。

## 何时使用

- 要写"连接一台 PLC 并周期采集标签"的 C# 程序；
- 要为新协议/新设备配置标签地址；
- AI 辅助：用户给出"连 XX 协议 / 采集 XX 标签"，按本 Skill 生成或审查代码。

## 前置

- NuGet：`ZL.IotHub`（免费版 10 台、协议全开，见 docs/193 口径；>10 台走正式授权）；
- 环境：.NET 8+；目标 PLC 可达（或本地仿真器）。

## 工作流（5 步，每步都有"照抄骨架"）

### 第 1 步 · 连接与初始化（M1）

- [ ] 引用 `ZL.IotHub` / `ZL.Tag`；
- [ ] 定连接常量：`ConnectTimeout` / `ReceiveTimeout` / `ReadInterval` / 合并参数 `MaxGap` / `MaxBlockSize`（参考值：8s / 5s / 300ms / 50 / 220）；
- [ ] 授权初始化：免费版 10 台内直接可用；**>10 台必须走正式授权——禁止复制官方示例中的演示授权路径进产品代码**（授权门禁要求）。
- 📎 参考：`examples/Program.cs`（M1+M3 合并示例，已编译 + 模拟器运行验证）；demo：官方示例 FleetS7（连接常量区）。

### 第 2 步 · 标签表与地址维护（M2）—— ⚠️ 地址错 = 现场灾难

- [ ] **地址格式先对照 `address/` 速查（源于 docs/189，17 协议）验证，禁止凭记忆写**；
- [ ] 四维检查：**进制**（如 MC X/Y 以 0 开头按 8 进制）/ **临界值**（如 Panasonic R 分界 14400、D 分界 90000）/ **位寻址模式**（字.位 vs 独立位区）/ **仅字访问**（如 S7 的 T/C）；
- [ ] 新增协议地址 → 先写进 `address/` 速查并标注"以厂商最新文档为准 + 对照源码验证"。
- 📎 速查：`address/` 目录（17 份，待建）；权威来源：`docs/189`。

### 第 3 步 · 数据读取（M3）

- [ ] **批量读取由采集引擎自动完成**：配置标签表 → 设备（DeviceRoot）周期执行（引擎内部 `BuildPlan` 做地址合并）→ 应用侧订阅 **`device.DataCollected` 事件 / 快照**拿值（demo 实证：FleetS7 用 `DataCollected` 统计批次）；
- [ ] 地址合并参数开起来（`BatchMonitor.ConfigureBatchRead(MaxGap, MaxBlockSize)`，参考值 50/220）——**不要**手工逐标签 `ReadAsync`（`device.ReadAsync<T>(addr)` 是**单地址**诊断用）；
- [ ] 多设备/多协议：用 `MultiDeviceOrchestrator`（编排 + 全局连接限流 + 并发启动 + 优雅停止）。
- 📎 参考：`examples/Program.cs` 的 DataCollected 模式（已运行验证）。

### 第 4 步 · 消息与质量（M4）

- [ ] **质量码**：质量码在**事件层**（`TagValueChange.Quality`：Good/Bad）与 **OPC UA 层**（`IotOpcUaServer.UpdateTagWithQuality`）；读取结果 `DeviceResult<T>` 用 `IsSuccess` 判断（**无质量字段**）——业务侧消费事件先看 `Quality`，不裸信值；
- [ ] **标签变更事件**：订阅 `PlcNotificationHub`（事件 → Channel → 消费，**含背压**，防快于慢）；
- [ ] **断线/重连事件**：状态机（Connected/Degraded/Recovering/Disconnected/…），恢复自动回归，不手工轮询；
- [ ] 若做 OPC UA 发布：用 `IotOpcUaServer.UpdateTagWithQuality(...)` 带质量推值（复用点见 docs/212 §三）。
- 📎 骨架：`examples/03_events.cs`（待建）。

### 第 5 步 · 写值与完整程序（M5）

- [ ] 写值用 **typed 扩展**（`plc.WriteAsync(address, value)`，address=**PLC 地址**、value 为 bool/byte/short/float 等**具体类型**，**禁止裸字节**——类型保真是硬要求；不存在 `WriteBool`/`WriteInt32` 命名方法；**⚠️ DeviceRoot 自身不实现 IPlcDevice，写操作走其 `device.Device` 字段**——examples 验证踩坑（CS1929））；
- [ ] 位写遵循"读-改-写"（字节是 S7 传输单位，位不是；见 `address/s7.md`）；
- [ ] 资源清理（Dispose / 取消订阅 / 释放连接）；
- [ ] 整体过一遍 `checklist.md`（已建，必过）。
- 📎 骨架：`examples/04_full_program.cs`（待建，最小可运行）。

## 陷阱清单（节选，完整见 address/*-traps.md 与 checklist.md）

- S7：T/C **仅字访问**（不支持位）；V 区隐含 DB1；SM/AI/AQ 仅 200/300 系列；
- Modbus：**40001 式解析未实现**（`ModbusAddress.Parse` 只认纯数字/富地址）；`64H` 十六进制后缀；
- MC：X/Y/DX/DY 以 0 开头按 **8 进制**；S*/C*/T* 子类型白名单（写错抛异常）；
- Omron：`字*16+bit`；CV 与非 CV 的 DR/AR/TIM/CNT 偏移不同；EM 银行号 16 进制；
- AB：`N7:0/5` 位访问；ST 字符串头=[MaxLen,ActualLen]；
- Keyence：R/CR/MR/LR **16 进制位压缩**（`high*16+low`）；
- Vigor：M/D 临界 9000、C 临界 200（跨临界换 DataCode）；
- XinJE：X/Y 8 进制。

## 验收（写完必过）

- [ ] `dotnet build` 0 errors；
- [ ] 每个地址都对照 `address/` 速查核过（快捷键：`grep "地址" address/`）；
- [ ] 质量码/事件/断线路径有处理（不是只读值）；
- [ ] 位写路径走"读-改-写"；
- [ ] >10 台时授权方式合规（正式授权，非 demo 路径）；
- [ ] 资源清理完整。

## 资源索引

| 资源 | 位置 | 状态 |
|------|------|------|
| 17 协议地址速查 | `address/` | 🟡 README 总表已建；分协议详情待拆（来源 docs/189） |
| 可仿写模板 | `examples/` | ✅ M1–M5 示例已建并**编译+运行验证**（写值回读 / 变更事件 / 质量码） |
| 检查清单 | `checklist.md` | ✅ 已建（0–7 步勾选 + 陷阱速查 + 验证方法） |
| 方向/复用点说明 | docs/212 | ✅ |