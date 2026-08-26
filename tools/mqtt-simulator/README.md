# NitroGateway 数据模拟器（Python）

向 MQTT Broker 持续发送 **BatchMeasurements v1** 上行测量载荷，载荷格式与真实
`NitroGateway` 边缘网关一致（DESIGN.md §4.1 / `NitroGateway.Forwarder` 序列化结果）。
用于在没有真实网关时，把 NitroCloud 的 `Ingest → InfluxDB → SignalR → 大屏` 链路跑通。

## 安装

```bash
cd tools/mqtt-simulator
python -m pip install -r requirements.txt   # 仅需 paho-mqtt>=2.0
```

## 快速开始

```bash
# 先复制一份配置并按需修改
copy config.example.json config.json

# 连默认 Broker 127.0.0.1:1883，每秒上报一轮
python mqtt_sim.py --config config.json

# 只看生成的载荷（不连 Broker），验证格式
python mqtt_sim.py --config config.json --dry-run --dry-rounds 2

# 只发一轮即退出
python mqtt_sim.py --config config.json --once
```

常用覆盖参数：`--host` / `--port` / `--username` / `--password` / `--qos` /
`--interval-ms` / `--verbose`（逐条打印完整 JSON）。

## 上报契约（与真实网关一致）

### Topic

```
nitrogateway/{siteId}/{deviceId}/measurements      # QoS 1
```

### Payload（JSON camelCase，UTF-8）

```json
{
  "siteId": "site-web-1",
  "v": 1,
  "id": "4f841674-f9cf-47fc-aec6-010c63bdf52f",
  "deviceId": "23763b14-88c2-4f1d-873c-16202d2092df",
  "scanStartedAt": "2026-08-25T13:50:10.4543661Z",
  "scanCompletedAt": "2026-08-25T13:50:10.4719247Z",
  "records": [
    {
      "id": "228eff00-8bcd-47d9-b8d5-bc1dacaa5af0",
      "deviceId": "23763b14-88c2-4f1d-873c-16202d2092df",
      "devicePointId": "241e9c46-ce8e-49d3-9055-f68b19f1af43",
      "pointName": "空压机_H006",
      "value": 514214,
      "dataType": 4,
      "timestamp": "2026-08-25T13:50:10.4543661Z",
      "receivedAt": "2026-08-25T13:50:10.4721093Z",
      "quality": 0
    }
  ],
  "successCount": 1,
  "failCount": 0
}
```

要点：
- `dataType` / `quality` 是**整数枚举码**（对应 `NitroGateway.Domain.Devices`），不是字符串。
- 时间戳为 UTC 的 ISO-8601，`Z` 结尾、7 位小数秒（与 System.Text.Json 的 `O` 格式一致）。
- `id` / `records[].id` 每轮重新生成 GUID（与真实网关一致，云端按 batch id 去重）。
- `successCount` = `quality == 0(Good)` 的记录数；`failCount` = 其余记录数。
- `records[].devicePointId` 与 payload 顶层 `siteId` 参与云端 Ingest 的冗余校验，建议保持与真实元数据一致。

## 配置说明

`config.json` 顶层结构：

```jsonc
{
  "broker": {
    "host": "127.0.0.1",          // Broker 地址
    "port": 1883,                 // Broker 端口
    "username": "",               // 可选
    "password": "",               // 可选
    "client_id_prefix": "nitro-sim-",  // 客户端 ID 前缀，脚本追加随机后缀避免会话抢占
    "qos": 1,                     // 发布 QoS（0/1/2）
    "keepalive": 30
  },
  "interval_ms": 1000,            // 两轮扫描/上报间隔（毫秒）
  "gateways": [
    {
      "site_id": "site-web-1",    // 站点 ID（topic 第三段）
      "device_id": "23763b14-...",// 设备 ID（topic 第四段）
      "points": [
        {
          "point_id": "241e9c46-...", // 点位 ID（devicePointId），缺省自动生成
          "name": "空压机_H006",       // 点位名称（pointName）
          "data_type": "Int32",        // Bool/Byte/Int16/UInt16/Int32/UInt32/Int64/UInt64/Float/Double/String
          "kind": "sine",              // constant/sine/ramp/walk/random/step/cycle
          "min": 500000,               // 波形下限
          "max": 530000,               // 波形上限
          "period_s": 60,              // 波形周期（秒）
          "step": 3,                   // walk 步长 / 其他可忽略
          "decimals": 1,               // 浮点保留小数位（Float/Double）
          "quality": 0,                // 0=Good 1=Uncertain 2=Bad
          "timestamp_offset_ms": 0,    // 该点位时间戳相对扫描开始的偏移（毫秒）
          "value": 42,                 // kind=constant 时的固定值
          "values": ["a", "b", "c"]    // kind=cycle 时的轮流取值（String）
        }
      ]
    }
  ]
}
```

### 取值模型（`kind`）

| kind | 说明 |
| --- | --- |
| `constant` | 固定值（用 `value`，缺省取 min/max 中点） |
| `sine` | 正弦波，min..max 之间，周期 `period_s` |
| `ramp` | 三角波，min..max 往复 |
| `walk` | 随机游走，每轮 ±step，越界反弹 |
| `random` | min..max 均匀随机 |
| `step` | 方波：半个周期 max、半个周期 min（适合 Bool 开关量） |
| `cycle` | 从 `values` 依次取值（适合 String 枚举） |

### DataType 枚举码（`data_type` 序列化后的整数）

| 码 | 类型 | 码 | 类型 |
| --- | --- | --- | --- |
| 0 | Bool | 6 | Int64 |
| 1 | Byte | 7 | UInt64 |
| 2 | Int16 | 8 | Float |
| 3 | UInt16 | 9 | Double |
| 4 | Int32 | 10 | String |
| 5 | UInt32 | | |

### Quality 枚举码

| 码 | 含义 |
| --- | --- |
| 0 | Good（计成功） |
| 1 | Uncertain |
| 2 | Bad |

## 订阅验证

另开一个终端订阅同一 Topic 即可看到上报数据：

```bash
mosquitto_sub -h 127.0.0.1 -p 1883 -t 'nitrogateway/+/+/measurements' -v
```

## 与 NitroCloud 联调

`docker compose up -d --build` 拉起 EMQX + InfluxDB + center + web 后，直接运行本模拟器，
大屏即可看到实时曲线；改某个点位的 `quality` 为 1/2 可观察“质量差/灰显”效果。
