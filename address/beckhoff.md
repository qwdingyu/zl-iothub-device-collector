# Beckhoff ADS 地址速查（beckhoff.md）

> **来源**：驱动实现（`BeckhoffAdsNet`）+ 交叉测试实证（`BeckhoffCrossTest`，HSL 与自研驱动双向）。
> ⚠️ **ADS 是符号/句柄寻址体系，不在 17 协议的数字地址类内**（17 协议地址格式规范未含 Beckhoff）；驱动当前以 **`M{字节偏移}`** 形式读写（交叉测试实证）。

## 一、地址形式（实测）

| 形式 | 示例 | 说明 |
|------|------|------|
| `M{offset}` | `M0` / `M2` | M 内存区 + **字节偏移**；交叉测试用 `M0`/`M2` 双向读写 Int16 / Int32 / UInt32（HSL `BeckhoffAdsNet` 与自研驱动一致） |

> **说明**：ADS 原生寻址是 **TwinCAT 符号名/句柄**（如 `MAIN.iVar1`）；当前示例/交叉测试覆盖的是 **M 区字节偏移**形式（简单内存区，无解析类）。**符号寻址是否支持以驱动实现为准**，不在本速查假设范围内。

## 二、注意

- 数据类型按 TwinCAT 类型（Int16 / Int32 / UInt32 / Float…），无 189 的数字地址解析类；
- M 区偏移按**字节**计（Int16 占 2 字节，故 `M0`→`M2` 相邻）。

## 三、陷阱

| 症状 | 原因 | 自查 |
|------|------|------|
| 地址解析失败 | ADS 非数字地址类体系（符号/句柄寻址） | 用 `M{偏移}` 或符号名（以驱动实现为准） |
| `M2` 与 `M0` 数据串位 | 偏移按字节（Int16 占 2B、Int32 占 4B） | 按字节对齐算偏移 |

## 四、典型示例

```
读 Int16@M0   → M0
读 UInt32@M2  → M2
写 Int32@M0   → M0
（对照 BeckhoffCrossTest：HSL↔自研双向，含边界值 Max/Min）
```

## 五、对照与验证

- 对照：驱动 `BeckhoffAdsNet` + 交叉测试 `BeckhoffCrossTest`（M0/M2 × Int16/Int32/UInt32 × 边界值，已验证双向一致）；
- ADS 协议规范以 TwinCAT 官方为准；仿真器覆盖范围以 `PlcSimulatorFixture` 为准。