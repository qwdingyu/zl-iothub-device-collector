# 设备采集程序仿写检查清单（checklist.md）

> **用法**：对照 SKILL 五步写完程序后，**逐条勾选**；任何"不确定"必须停下来查（对照 `address/` 速查 → 编译 → 模拟器跑通）。**不确定 ≠ 通过。**
> **关联**：`SKILL.md`（方法论）、`address/`（地址速查）、`examples/CollectorExamples.csproj`（S7 示例）+ `examples/ModbusExample/`（Modbus 示例，均已编译+运行验证，M1–M5）。

## 0. 写代码前（5 分钟自检）

- [ ] 协议路由键：`DeviceConfig` 用 **`Protocol`**（如 `siemens-s7`），**不要写 `DeviceType`**（被 `MultiDeviceOrchestrator` 独占为业务壳类名——examples/Program.cs 与官方示例 FleetS7 注释实证）；
- [ ] 授权边界：≤10 台免费版可用（官方免费版额度）；>10 台走正式授权；**禁止复制官方示例中的演示授权路径**（授权门禁要求）；
- [ ] 目标可达：IP / 端口 / 型号确认；**模拟器必须 `--stay-alive` 常驻**（headless 默认启动即退，实测踩坑）；

## 1. 连接与初始化（M1）

- [ ] 连接常量齐全：`ConnectTimeout`(参考 8s) / `ReceiveTimeout`(5s) / `ReadInterval`(300ms)；
- [ ] 合并参数：`MaxGap`(50) / `MaxBlockSize`(220)；
- [ ] `DeviceConfig` 键完整：`DeviceId` / `DeviceIp` / `Port` / `Rack` / `Slot` / `Protocol` / `ConnectTimeOut` / `ReceiveTimeOut` / `Tags`。

## 2. 标签表与地址（M2）—— ⚠️ 地址错 = 现场灾难

- [ ] 每个地址对照 `address/<协议>.md` 核过**四维**：**进制 / 临界值 / 位寻址模式 / 仅字位**；
- [ ] 边界地址**真跑过**（8↔16 进制、临界两侧、位/字切换）—— **不跑不算数**（address/README §四）；
- [ ] `DataTypeCode` 与实际类型一致（bool/short/int/float/ushort…），无裸字节降级；
- [ ] 新增协议地址 → 先补 `address/` 速查（标注"以厂商最新为准 + 对照源码验证"），再进点表。

## 3. 数据读取（M3）

- [ ] 批量读取走引擎：标签表 → `DeviceRoot` → **`DataCollected` 快照**拿值（examples/Program.cs 实证；快照每轮读取完成触发，值稳定也触发）；
- [ ] **不要**逐标签 `ReadAsync`（`device.ReadAsync<T>(address)` 是**单点诊断**用）；
- [ ] 多设备/多协议 → `MultiDeviceOrchestrator`（全局连接限流 + 并发启动 + 优雅停止）。

## 4. 消息与质量（M4）

- [ ] 消费事件**先看 `TagValueChange.Quality`**（Good=0xC0 正常 / Uncertain=0x40 陈旧 / Bad 不可信），不裸信值；
- [ ] 用途分清：`TagChanges` = **值变化**事件；`DataCollected` = **读取完成**快照——统计/取数用后者；
- [ ] 高频场景用背压管道（`SubscribeChannel`）防快于慢（SKILL M4 进阶）。

## 5. 写值（M5）

- [ ] typed 写：`plc.WriteAsync(address, value)`，value 为 bool/byte/short/float 等**具体类型**，**禁裸字节**；
- [ ] ⚠️ `WriteAsync` 第一参数是 **PLC 地址**（不是标签 Id）——examples 编译实证；
- [ ] ⚠️ **`DeviceRoot` 不实现 `IPlcDevice`** → 写操作走 `device.Device.WriteAsync(...)`（CS1929 实证，SKILL M5 踩坑说明）；
- [ ] 位写走**读-改-写**（字节是 S7 传输单位，位不是；`address/s7.md`）。

## 6. 资源与生命周期（M5）

- [ ] 取消订阅（`TagChanges -=` / `DataCollected -=`）；
- [ ] 优雅停止顺序：先 `StopAsync`（停数据流）→ 再取消订阅/释放（examples/Program.cs 顺序）；
- [ ] `Dispose` / 释放连接。

## 7. 最终验收（必过；一项不过 = 不算完成）

- [ ] `dotnet build` **0 errors / 0 warnings**；
- [ ] 模拟器（S7Simulator / PlcSimulatorFixture）**跑通**：连接成功、读值类型保真、**写生效读回**、事件+质量码触发（= examples/Program.cs 已验证的闭环）；
- [ ] 授权合规（>10 台正式授权；README 声明免费版边界）；
- [ ] 资源清理完整（订阅/事件/连接全释放）。

## 8. 常见地址陷阱速查（症状 → 定位，完整见 address/*.md）

| 症状 | 大概率原因 | 定位 |
|------|-----------|------|
| `T0.0`/`C0.0` 位操作失败 | S7 T/C **仅字** | `address/s7.md` |
| 按 `40001` 写、读到的不是"寄存器 1" | **40001 式未实现**（HSL 按纯偏移解析） | `address/modbus.md` |
| Modbus 写 `HR100`/`COIL1` 报**地址解析失败** | 文本前缀两条路径都不可用（自研=纯数字寄存器索引；HSL=纯数字/富地址） | `address/modbus.md` §〇 三栈差异 |
| `X010` 与 `X10` 值不同 | MC X/Y 以 0 开头按 **8 进制** | `address/mc.md` |
| `CIO100.03` 按 103 读 | Omron 位 = **字×16+位号**（=1603） | `address/omron.md` |
| CV 机上 DR/TIM/CNT 值错 | CV/非 CV 偏移不同（型号选错） | `address/omron.md` |
| AB 地址解析报错 | **冒号必填**（`N7:0`，缺冒号报错） | address/README 总表（AB 待拆） |
| `M100`/`D100` 超临界读错区 | Vigor M/D≥9000、C≥200 换区 | address/README 总表 |

## 9. 对照与验证方法（address/README §四）

1. 每个地址在 `address/` 速查核过（含边界四维）；
2. 边界用例优先跑（进制切换 / 临界两侧 / 位字切换 / 地址 0），不跑不算数；
3. 模拟器读回**值必须对**（能连上 ≠ 地址对）；
4. 新增格式先补速查再进点表。