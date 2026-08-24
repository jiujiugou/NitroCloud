# ADR-003：反向控制走命令 topic + 回执
- 问题：云 → 网关 → PLC 写值如何保证可靠、不阻塞、可重试？
- 可选方案：A. 同步 REST 调网关；B. 命令 topic + 回执（异步）；C. 命令 topic 无回执。
- 决定：B——异步可靠、可重试、不阻塞；命令 topic（type=WritePoint、pointId、value）+ `commands/ack` 回执。
- ⚠️ 载荷墙：命令契约以 ADR/接口为准，需 NitroGateway 补 MQTT 订阅处理器（小改动）；命令超时重试不能重复执行（幂等/去重）；回执 result/error 必填。
- 变更记录：2026-08-23 自 DESIGN.md C-003 转正，状态：草案待评审（DESIGN.md v0.1）。
