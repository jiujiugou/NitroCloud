#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
NitroGateway 数据模拟器 — 向 MQTT Broker 发送 BatchMeasurements v1 上行载荷。

用途:
    在 NitroCloud 开发/联调阶段模拟边缘网关(NitroGateway)上报测量数据,
    让 Ingest -> InfluxDB -> SignalR -> 大屏 的链路在没有真实网关时也能跑通。

契约(以 NitroGateway 侧为准, 见 DESIGN.md §4.1):
    Topic:  nitrogateway/{siteId}/{deviceId}/measurements   (QoS 1)
    Payload: BatchMeasurements v1, JSON camelCase, UTF-8 编码
        - dataType / quality 序列化为整数枚举码(NitroGateway.Domain.Devices)
        - timestamps 为 UTC 的 ISO-8601, 形如 2026-08-25T13:50:10.4543661Z
        - successCount / failCount 由 records 的 quality 计算(Good=0 计成功)
    devicePointId 与 payload 内 siteId 均与真实网关保持一致, 供云端 Ingest 冗余校验。

用法:
    python mqtt_sim.py --config config.json
    python mqtt_sim.py --dry-run --config config.example.json   # 只生成打印, 不连 Broker
    python mqtt_sim.py --once --config config.json              # 发一轮即退出

依赖:
    paho-mqtt>=2.0   (pip install -r requirements.txt)

说明:
    数据上报由 config.json 描述: broker(连接) + gateways(站点/设备/点位 + 取值模型)。
    点位 value 由 kind 波形生成(constant/sine/ramp/walk/random/step/cycle),
    并按 dataType 转成对应类型后写入 payload。
"""

from __future__ import annotations

import argparse
import json
import math
import random
import sys
import threading
import time
import uuid
from dataclasses import dataclass, field
from datetime import datetime, timedelta, timezone
from typing import Any, Optional

try:
    import paho.mqtt.client as mqtt
    from paho.mqtt.client import CallbackAPIVersion
except ImportError:  # pragma: no cover - 仅 --dry-run 可不依赖 paho
    mqtt = None


# ── 与 NitroGateway.Domain.Devices.DataType 一致的枚举码 ──────────────
# 注意: 云侧解析按整数码识别, 不得改为字符串, 否则与真实网关契约不一致。
DATA_TYPE_CODES: dict[str, int] = {
    "Bool": 0,
    "Byte": 1,
    "Int16": 2,
    "UInt16": 3,
    "Int32": 4,
    "UInt32": 5,
    "Int64": 6,
    "UInt64": 7,
    "Float": 8,
    "Double": 9,
    "String": 10,
}
DATA_TYPE_NAMES: dict[int, str] = {v: k for k, v in DATA_TYPE_CODES.items()}

# 各整数类型取值范围, 用于 clamp 防止波形越界写出异常值
_INT_RANGES: dict[str, tuple[int, int]] = {
    "Byte": (0, 0xFF),
    "Int16": (-0x8000, 0x7FFF),
    "UInt16": (0, 0xFFFF),
    "Int32": (-0x80000000, 0x7FFFFFFF),
    "UInt32": (0, 0xFFFFFFFF),
    "Int64": (-0x8000000000000000, 0x7FFFFFFFFFFFFFFF),
    "UInt64": (0, 0xFFFFFFFFFFFFFFFF),
}


def fmt_utc(dt: datetime) -> str:
    """按 .NET \"O\" 风格输出 UTC 时间戳: 2026-08-25T13:50:10.4543661Z(7 位小数秒)。

    真实网关由 System.Text.Json 序列化 DateTime(100ns 刻度), 固定输出 7 位小数;
    这里在 Python 6 位微秒后补一个 0, 保持字节级一致, 便于云侧 Ingest 解析与演示对照。
    """
    if dt.tzinfo is None:
        dt = dt.replace(tzinfo=timezone.utc)
    else:
        dt = dt.astimezone(timezone.utc)
    return f"{dt:%Y-%m-%dT%H:%M:%S}.{dt.microsecond:06d}0Z"


def resolve_data_type(raw: Any) -> str:
    """把配置里的 data_type 归一为枚举名(接受整数码或名称字符串)。"""
    if isinstance(raw, int):
        name = DATA_TYPE_NAMES.get(raw)
        if name is None:
            raise ValueError(f"未知 dataType 枚举码: {raw}")
        return name
    name = str(raw)
    if name not in DATA_TYPE_CODES:
        raise ValueError(f"未知 dataType 名称: {name!r}, 可选: {', '.join(DATA_TYPE_CODES)}")
    return name


@dataclass
class SimulatedPoint:
    """单个点位(devicePoint)的取值模型。

    每次扫描(每轮)由 next_record() 生成一条 MeasurementRecord 所需的字段。
    kind 波形:
        constant 固定值(value) | sine 正弦 | ramp 三角波 | walk 随机游走
        random 均匀随机 | step 方波(高/低) | cycle 字符串轮流取值(values)
    """

    cfg: dict[str, Any]
    device_id: str
    point_id: str = field(init=False)
    name: str = field(init=False)
    data_type: str = field(init=False)
    data_type_code: int = field(init=False)
    kind: str = field(init=False)
    min: float = field(init=False)
    max: float = field(init=False)
    period_s: float = field(init=False)
    step: float = field(init=False)
    constant_value: Any = field(init=False)
    values: list[Any] = field(init=False)
    decimals: int = field(init=False)
    offset_ms: int = field(init=False)
    quality: int = field(init=False)
    phase: float = field(init=False)
    _state: dict[str, Any] = field(default_factory=dict, init=False)

    def __post_init__(self) -> None:
        self.point_id = str(self.cfg.get("point_id") or uuid.uuid4())
        self.name = str(self.cfg.get("name") or self.point_id)
        self.data_type = resolve_data_type(self.cfg.get("data_type", "Float"))
        self.data_type_code = DATA_TYPE_CODES[self.data_type]
        self.kind = str(self.cfg.get("kind", "random")).lower()
        self.min = float(self.cfg.get("min", 0))
        self.max = float(self.cfg.get("max", 1000))
        self.period_s = float(self.cfg.get("period_s", 60))
        self.step = float(self.cfg.get("step", (self.max - self.min) / 10.0))
        self.constant_value = self.cfg.get("value")
        self.values = list(self.cfg.get("values") or [])
        self.decimals = int(self.cfg.get("decimals", 2))
        self.offset_ms = int(self.cfg.get("timestamp_offset_ms", 0))
        self.quality = int(self.cfg.get("quality", 0))  # 0=Good 1=Uncertain 2=Bad
        self.phase = random.random()  # 每点位随机初相, 避免同站点波形完全同步

    def _raw_value(self, now_ts: float) -> float:
        """按波形生成原始数值(未按 dataType 转型)。"""
        lo, hi = self.min, self.max
        span = max(hi - lo, 1e-9)
        period = max(self.period_s, 1e-6)
        kind = self.kind

        if kind == "constant":
            if self.constant_value is not None:
                return float(self.constant_value)
            return (lo + hi) / 2.0
        if kind == "sine":
            return lo + span * (0.5 + 0.5 * math.sin(2 * math.pi * now_ts / period + self.phase))
        if kind == "ramp":
            f = (now_ts / period + self.phase) % 1.0
            return lo + span * (2 * f if f < 0.5 else 2 * (1 - f))
        if kind == "walk":
            cur = self._state.get("walk", (lo + hi) / 2.0)
            cur += random.uniform(-self.step, self.step)
            if cur < lo or cur > hi:  # 越界反弹, 保持边界内随机游走
                cur = 2 * hi - cur if cur > hi else 2 * lo - cur
            self._state["walk"] = cur
            return cur
        if kind == "random":
            return random.uniform(lo, hi)
        if kind == "step":
            f = (now_ts / period + self.phase) % 1.0
            return hi if f < 0.5 else lo
        if kind == "cycle":
            vals = self.values or [""]
            idx = int(now_ts / period) % len(vals)
            return float(vals[idx]) if str(vals[idx]).replace(".", "", 1).isdigit() else 0.0
        raise ValueError(f"未知 kind: {self.kind!r}, 可选: constant/sine/ramp/walk/random/step/cycle")

    def _cast(self, raw: float, now_ts: float) -> Any:
        """按 dataType 把原始数值转成最终 value 类型, 并 clamp 到类型范围。"""
        if self.data_type == "Bool":
            return bool(raw >= (self.min + self.max) / 2.0)
        if self.data_type == "String":
            if self.kind == "cycle" and self.values:
                idx = int(now_ts / max(self.period_s, 1e-6)) % len(self.values)
                return self.values[idx]
            return str(self.constant_value if self.constant_value is not None else "")
        if self.data_type in ("Float", "Double"):
            return round(float(raw), self.decimals)
        lo, hi = _INT_RANGES[self.data_type]
        return max(lo, min(hi, int(round(raw))))

    def next_record(self, scan_start: datetime, now_ts: float) -> dict[str, Any]:
        """生成一条 MeasurementRecord 字段; timestamp 相对 scan_start 叠加 offset_ms。"""
        value = self._cast(self._raw_value(now_ts), now_ts)
        ts = scan_start + timedelta(milliseconds=self.offset_ms)
        return {
            "id": str(uuid.uuid4()),            # 每轮新 GUID, 与真实网关一致
            "deviceId": self.device_id,
            "devicePointId": self.point_id,
            "pointName": self.name,
            "value": value,
            "dataType": self.data_type_code,
            "timestamp": fmt_utc(ts),
            "receivedAt": fmt_utc(scan_start),
            "quality": self.quality,
        }


@dataclass
class SimulatedDevice:
    """一台网关设备 = 一个 site/device 下的点位集合, 每轮产出一个 BatchMeasurements。"""

    cfg: dict[str, Any]
    site_id: str = field(init=False)
    device_id: str = field(init=False)
    points: list[SimulatedPoint] = field(init=False)

    def __post_init__(self) -> None:
        self.site_id = str(self.cfg.get("site_id") or "site-default")
        self.device_id = str(self.cfg.get("device_id") or uuid.uuid4())
        self.points = [SimulatedPoint(p, self.device_id) for p in self.cfg.get("points", [])]

    def topic(self) -> str:
        return f"nitrogateway/{self.site_id}/{self.device_id}/measurements"

    def next_batch(self) -> tuple[str, dict[str, Any]]:
        """生成一整批载荷; 返回 (topic, payload_dict)。"""
        now = datetime.now(timezone.utc)
        now_ts = now.timestamp()
        records = [p.next_record(now, now_ts) for p in self.points]
        tss = [r["timestamp"] for r in records]
        success = sum(1 for r in records if r["quality"] == 0)  # Good=0 计成功
        payload = {
            "siteId": self.site_id,
            "v": 1,
            "id": str(uuid.uuid4()),
            "deviceId": self.device_id,
            "scanStartedAt": min(tss) if tss else fmt_utc(now),
            "scanCompletedAt": max(tss) if tss else fmt_utc(now),
            "records": records,
            "successCount": success,
            "failCount": len(records) - success,
        }
        return self.topic(), payload


class Simulator:
    """MQTT 发布主控: 连接 Broker -> 按 interval 循环为每个 device 发一批测量数据。"""

    def __init__(self, cfg: dict[str, Any], args: argparse.Namespace) -> None:
        self.cfg = cfg
        self.args = args
        broker = cfg.get("broker", {})
        self.host = args.host or broker.get("host", "127.0.0.1")
        self.port = int(args.port or broker.get("port", 1883))
        self.username = args.username if args.username is not None else broker.get("username", "")
        self.password = args.password if args.password is not None else broker.get("password", "")
        self.qos = int(args.qos if args.qos is not None else broker.get("qos", 1))
        self.keepalive = int(broker.get("keepalive", 30))
        self.interval_ms = int(args.interval_ms if args.interval_ms else cfg.get("interval_ms", 1000))
        self.once = bool(args.once or cfg.get("once", False))
        self.verbose = bool(args.verbose)
        self.devices = [SimulatedDevice(g) for g in cfg.get("gateways", [])]
        if not self.devices:
            raise ValueError("config 中 gateways 为空, 至少需要一台设备")
        self._connected = threading.Event()

    # ── 连接回调 ──────────────────────────────────────────────
    def _on_connect(self, client, userdata, flags, reason_code, properties) -> None:
        if reason_code == 0:
            self._connected.set()
            print(f"[MQTT] 已连接 {self.host}:{self.port}")
        else:
            print(f"[MQTT] 连接失败, reason_code={reason_code}")

    def _on_disconnect(self, client, userdata, flags, reason_code, properties) -> None:
        self._connected.clear()
        if reason_code != 0:
            print("[MQTT] 连接断开, paho 将自动重连")

    def _connect(self) -> mqtt.Client:
        if mqtt is None:
            raise RuntimeError("缺少依赖 paho-mqtt, 请先: pip install -r requirements.txt")
        client = mqtt.Client(
            callback_api_version=CallbackAPIVersion.VERSION2,
            client_id=self.cfg.get("broker", {}).get("client_id_prefix", "nitro-sim-") + str(uuid.uuid4())[:8],
            protocol=mqtt.MQTTv311,
        )
        client.on_connect = self._on_connect
        client.on_disconnect = self._on_disconnect
        if self.username:
            client.username_pw_set(self.username, self.password or None)
        client.connect(self.host, self.port, self.keepalive)
        client.loop_start()
        if not self._connected.wait(timeout=10):
            raise ConnectionError(f"无法连接 MQTT Broker {self.host}:{self.port}")
        return client

    # ── 单轮上报 ──────────────────────────────────────────────
    def _round(self, client: mqtt.Client) -> None:
        for dev in self.devices:
            topic, payload = dev.next_batch()
            body = json.dumps(payload, ensure_ascii=False, separators=(",", ":")).encode("utf-8")
            if self.args.dry_run:
                print(f"[{topic}]")
                print(body.decode("utf-8"))
                print()
                continue
            info = client.publish(topic, body, qos=self.qos)
            info.wait_for_publish(timeout=10)
            if self.verbose:
                print(f"[{topic}] {body.decode('utf-8')}")
            else:
                vals = ", ".join(f"{r['pointName']}={r['value']}" for r in payload["records"])
                print(f"[{topic}] batch={payload['id'][:8]} 成功={payload['successCount']} 失败={payload['failCount']} | {vals}")

    # ── 主循环 ────────────────────────────────────────────────
    def run(self) -> None:
        client = self._connect()
        print(f"上报间隔 {self.interval_ms}ms, 设备数 {len(self.devices)}, QoS={self.qos} (Ctrl+C 退出)")
        next_tick = time.monotonic()
        try:
            while True:
                self._round(client)
                if self.once:
                    break
                next_tick += self.interval_ms / 1000.0
                delay = next_tick - time.monotonic()
                if delay > 0:
                    time.sleep(delay)
                else:  # 单轮耗时超过间隔, 追赶下一拍, 避免漂移堆积
                    next_tick = time.monotonic()
        except KeyboardInterrupt:
            print("\n[退出] 收到 Ctrl+C, 正在断开...")
        finally:
            client.loop_stop()
            client.disconnect()
            print("[MQTT] 已断开")

    def dry_run(self) -> None:
        """只生成并打印一批载荷(模拟一次扫描), 不连接 Broker。"""
        print(f"[DRY-RUN] 生成 {len(self.devices)} 台设备的测量批次(不发送)")
        for _ in range(self.args.dry_rounds):
            self._round(None)  # type: ignore[arg-type]  # dry-run 分支不 publish


def parse_args(argv: Optional[list[str]] = None) -> argparse.Namespace:
    p = argparse.ArgumentParser(
        prog="mqtt_sim",
        description="NitroGateway 测量数据模拟器(向 MQTT 发送 BatchMeasurements v1)",
    )
    p.add_argument("--config", default="config.json", help="JSON 配置文件路径(默认 config.json)")
    p.add_argument("--host", help="MQTT Broker 地址(覆盖配置)")
    p.add_argument("--port", type=int, help="MQTT Broker 端口(覆盖配置)")
    p.add_argument("--username", help="MQTT 用户名(覆盖配置)")
    p.add_argument("--password", help="MQTT 密码(覆盖配置)")
    p.add_argument("--qos", type=int, choices=(0, 1, 2), help="发布 QoS(覆盖配置, 默认 1)")
    p.add_argument("--interval-ms", type=int, help="两轮上报间隔毫秒(覆盖配置)")
    p.add_argument("--once", action="store_true", help="只发一轮即退出")
    p.add_argument("--dry-run", action="store_true", help="只生成打印, 不连 Broker")
    p.add_argument("--dry-rounds", type=int, default=1, help="--dry-run 时打印几批(默认 1)")
    p.add_argument("--verbose", action="store_true", help="逐条打印完整 JSON 载荷")
    return p.parse_args(argv)


def load_config(path: str) -> dict[str, Any]:
    try:
        with open(path, "r", encoding="utf-8") as f:
            return json.load(f)
    except FileNotFoundError:
        print(f"找不到配置文件: {path}", file=sys.stderr)
        print("可参考 tools/mqtt-simulator/config.example.json 复制一份 config.json", file=sys.stderr)
        raise SystemExit(2)


def main(argv: Optional[list[str]] = None) -> None:
    args = parse_args(argv)
    cfg = load_config(args.config)
    sim = Simulator(cfg, args)
    if args.dry_run:
        sim.dry_run()
    else:
        sim.run()


if __name__ == "__main__":
    main()
