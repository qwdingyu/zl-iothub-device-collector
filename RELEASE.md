# 发布说明（RELEASE.md）

## 发布前检查（必须逐项过）

1. **清雷**（docs/188 §5.3 定义）：全目录无客户名 / 内网 IP / 逆向工程出处表述；
   `docs/NNN` 编号保留（官方溯源标记，README 已声明）；
2. **编译**：`dotnet build examples/CollectorExamples.csproj -c Debug` → 0 errors / 0 warnings；
3. **运行**（可选，需 S7 仿真器）：`dotnet run --project examples/CollectorExamples.csproj` → M1–M5 闭环；
4. **地址核对**：`address/` 每份速查与 17 协议地址格式规范章节一致（新增协议先补速查再发布）。

## 发布流程（GitHub）

```bash
git tag v1.0.0              # 版本号随变更语义递增
git push origin v1.0.0      # 触发 Actions build
# 确认 build 通过后创建 GitHub Release（附 changelog）
```

## 版本对照（每版包含什么）

| 版本 | 内容 |
|------|------|
| v1.0.1 | **Modbus 使用验证迭代**：新增 `examples/ModbusExample`（第二协议示例，编译+模拟器运行验证 M1–M5 闭环）；修 `address/modbus.md`（补「〇、三栈差异」：自研驱动=纯数字寄存器索引 / HSL=纯数字+富地址 / 文本前缀两路径均不可用——官方 ModbusUnified demo 的 `COIL/HR + HslDriverFactory` 组合实测「地址解析失败」）；SKILL/README/checklist 补模拟器 `--stay-alive` 常驻要点 + 事件双入口 + CI 编译双示例 |
| v1.0.0 | 首个发布：SKILL 5 步方法论 + address 9 协议速查 + examples（S7 全流程验证）+ checklist + MIT 开源门面 + CI |

## Changelog

- v1.0.1（2026-09-29）：Modbus 使用验证迭代（见版本对照）。
- v1.0.0（2026-09-29）：首个发布。