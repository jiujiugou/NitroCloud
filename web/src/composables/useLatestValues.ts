// 最近值（ADR-009 D3/D5）：reactive Map 存 siteId → devicePointId → LatestValue。
// 快照 REST /api/sites/{id}/latest + 增量 SignalR OnMeasurements 合并。
import { reactive } from 'vue'
import { getSiteLatest } from '../api/sites'
import { onHubEvent } from '../api/signalr'
import type { LatestValue, MeasurementRecord } from '../api/types'

/** 全局只注册一次 OnMeasurements 处理器（多个实例共享同一事件流） */
let registered = false

export function useLatestValues() {
  /** siteId → devicePointId → LatestValue */
  const latest = reactive<Record<string, Record<string, LatestValue>>>({})
  /** 站点快照是否已拉取（决定 PointRow 是否显示「--」） */
  const loaded = reactive<Record<string, boolean>>({})

  if (!registered) {
    registered = true
    onHubEvent<MeasurementRecord | MeasurementRecord[]>('OnMeasurements', data => {
      const list = Array.isArray(data) ? data : [data]
      list.forEach(applyMeasurement)
    })
  }

  /** REST 快照：拉取该站点最近值并覆盖本地（重连补拉也走这里） */
  async function loadSite(siteId: string): Promise<void> {
    try {
      const rows = await getSiteLatest(siteId)
      latest[siteId] = {}
      rows.forEach(r => {
        latest[siteId][r.devicePointId] = r
      })
      loaded[siteId] = true
    } catch {
      if (!latest[siteId]) latest[siteId] = {}
      // 快照失败不置 loaded=true，PointRow 保持占位
    }
  }

  /** SignalR 增量合并：保留点位元数据（pointName/dataType/unit）不被推送覆盖丢 */
  function applyMeasurement(m: MeasurementRecord): void {
    if (!latest[m.siteId]) latest[m.siteId] = {}
    const prev = latest[m.siteId][m.devicePointId]
    latest[m.siteId][m.devicePointId] = {
      siteId: m.siteId,
      deviceId: m.deviceId,
      devicePointId: m.devicePointId,
      pointName: m.pointName ?? prev?.pointName ?? '',
      dataType: m.dataType ?? prev?.dataType,
      unit: m.unit ?? prev?.unit,
      value: m.value,
      quality: m.quality,
      timestamp: m.timestamp
    }
  }

  function getSiteValues(siteId: string): LatestValue[] {
    const map = latest[siteId]
    return map ? Object.values(map) : []
  }

  return { latest, loaded, loadSite, applyMeasurement, getSiteValues }
}
