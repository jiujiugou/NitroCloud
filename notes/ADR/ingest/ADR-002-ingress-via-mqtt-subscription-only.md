# ADR-002：接入只走 MQTT 订阅，不直连网关 API
- 问题：云侧如何接入多网关数据？直连网关 REST 会耦合、难扩展、网关离线即断。
- 可选方案：A. 拉取网关 REST API；B. MQTT 订阅上行 topic；C. 两者都做。
- 决定：B——MQTT 解耦、多网关易扩展、网关可离线缓存重发。
- ⚠️ 载荷墙：云侧只实现 MQTT 订阅端，不新增网关 API 依赖；topic/载荷契约以网关侧为准（上行 topic 第三段 = siteId，载荷内 siteId 冗余校验）；QoS1 + 幂等去重。
- 变更记录：2026-08-23 自 DESIGN.md C-002 转正，状态：草案待评审（DESIGN.md v0.1）。
