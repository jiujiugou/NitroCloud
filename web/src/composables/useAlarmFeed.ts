// 告警 feed（ADR-009 D3/D5）：告警列表 + KPI 统计 + SignalR OnAlarm 追加。
import { computed, ref } from 'vue'
import { getAlarms, getAlarmSummary } from '../api/alarms'
import { onHubEvent } from '../api/signalr'
import type { AlarmRecord } from '../api/types'

let registered = false
const FEED_CAP = 200

function isToday(iso: string): boolean {
  if (!iso) return false
  const d = new Date(iso)
  const now = new Date()
  return d.getFullYear() === now.getFullYear() &&
    d.getMonth() === now.getMonth() &&
    d.getDate() === now.getDate()
}

export function useAlarmFeed() {
  /** 告警列表（新→旧） */
  const alarms = ref<AlarmRecord[]>([])
  const activeCount = computed(() => alarms.value.filter(a => a.state === 'Active').length)
  /** 今日发生数（本地统计兜底；优先用后端 summary.today） */
  const todayCount = computed(() => alarms.value.filter(a => isToday(a.occurredAt)).length)
  const today = ref(0)

  if (!registered) {
    registered = true
    onHubEvent<AlarmRecord>('OnAlarm', a => append(a))
  }

  /** 快照拉取（10s 轮询走这里，配合 KPI 刷新） */
  async function load(): Promise<void> {
    try {
      alarms.value = await getAlarms({})
      if (alarms.value.length > FEED_CAP) alarms.value.length = FEED_CAP
    } catch { /* 后端未起，保持空态 */ }
    try {
      const s = await getAlarmSummary()
      if (s) today.value = s.today
    } catch { /* summary 端点缺失时用 todayCount 兜底 */ }
  }

  /** SignalR 实时追加（去重：同一告警已存在则移到头部） */
  function append(a: AlarmRecord): void {
    const i = alarms.value.findIndex(x => x.id === a.id)
    if (i >= 0) alarms.value.splice(i, 1)
    alarms.value.unshift(a)
    if (alarms.value.length > FEED_CAP) alarms.value.length = FEED_CAP
  }

  return { alarms, activeCount, todayCount, today, load, append }
}
