<template>
  <div class="admin-page">
    <div class="page-head">
      <div>
        <div class="page-title">告警记录</div>
        <div class="page-desc">云端汇总各站点告警，支持按站点/级别/状态过滤与人工确认。</div>
      </div>
    </div>

    <div class="kpi-row">
      <div class="kpi-box">
        <div class="kpi-num red">{{ activeCount }}</div>
        <div class="kpi-label">活跃告警</div>
      </div>
      <div class="kpi-box">
        <div class="kpi-num orange">{{ today }}</div>
        <div class="kpi-label">今日告警</div>
      </div>
    </div>

    <el-card shadow="never">
      <div class="filter-bar">
        <el-select v-model="q.siteId" placeholder="全部站点" clearable style="width: 200px">
          <el-option v-for="s in sites" :key="s.id" :label="s.name" :value="s.id" />
        </el-select>
        <el-select v-model="q.severity" placeholder="全部级别" clearable style="width: 140px">
          <el-option v-for="sev in severities" :key="sev" :label="severityText(sev)" :value="sev" />
        </el-select>
        <el-select v-model="q.state" placeholder="全部状态" clearable style="width: 140px">
          <el-option v-for="st in states" :key="st" :label="stateText(st)" :value="st" />
        </el-select>
        <el-button type="primary" :icon="Refresh" @click="load">刷新</el-button>
      </div>

      <el-table :data="rows" v-loading="loading" stripe>
        <el-table-column label="级别" width="90">
          <template #default="{ row }">
            <el-tag :type="severityTag(row.severity)" size="small">{{ severityText(row.severity) }}</el-tag>
          </template>
        </el-table-column>
        <el-table-column prop="message" label="告警内容" min-width="220">
          <template #default="{ row }">{{ row.message }}</template>
        </el-table-column>
        <el-table-column label="站点 / 设备" min-width="170">
          <template #default="{ row }">
            <span class="mono small">{{ siteName(row.siteId) }}</span>
            <span class="sep">/</span>
            <span class="mono small">{{ deviceName(row.deviceId) }}</span>
          </template>
        </el-table-column>
        <el-table-column label="点位" min-width="110">
          <template #default="{ row }"><span class="mono small">{{ row.pointId }}</span></template>
        </el-table-column>
        <el-table-column label="状态" width="110">
          <template #default="{ row }">
            <el-tag :type="stateTag(row.state)" size="small">{{ stateText(row.state) }}</el-tag>
          </template>
        </el-table-column>
        <el-table-column label="触发时间" width="180">
          <template #default="{ row }"><span class="mono">{{ fmtTime(row.occurredAt) }}</span></template>
        </el-table-column>
        <el-table-column label="确认时间" width="180">
          <template #default="{ row }"><span class="mono">{{ fmtTime(row.ackedAt) }}</span></template>
        </el-table-column>
        <el-table-column label="操作" width="110" fixed="right">
          <template #default="{ row }">
            <el-button v-if="row.state === 'Active'" link type="primary" @click="ack(row)">确认</el-button>
            <span v-else class="muted">-</span>
          </template>
        </el-table-column>
      </el-table>
    </el-card>
  </div>
</template>

<script setup lang="ts">
import { computed, onMounted, onUnmounted, reactive, ref } from 'vue'
import { ElMessage } from 'element-plus'
import { Refresh } from '@element-plus/icons-vue'
import { ackAlarm, getAlarms } from '../../api/alarms'
import { getSites } from '../../api/sites'
import { getSiteDevices } from '../../api/devices'
import { onHubEvent, offHubEvent, startHub, stopHub } from '../../api/signalr'
import { useAlarmFeed } from '../../composables/useAlarmFeed'
import type { AlarmRecord, AlarmSeverity, AlarmState, Device, Site } from '../../api/types'
import { fmtTime, severityTag, severityText, stateTag } from '../../utils/format'

const severities: AlarmSeverity[] = ['Critical', 'Major', 'Warning', 'Info']
const states: AlarmState[] = ['Active', 'Acknowledged', 'Resolved', 'Cleared']

const sites = ref<Site[]>([])
const devices = ref<Device[]>([])
const rows = ref<AlarmRecord[]>([])
const loading = ref(false)
const q = reactive<{ siteId?: string; severity?: AlarmSeverity; state?: AlarmState }>({})

const { activeCount, today, load: loadFeed } = useAlarmFeed()

const siteNames = computed(() => {
  const m: Record<string, string> = {}
  sites.value.forEach(s => { m[s.id] = s.name })
  return m
})
const deviceNames = computed(() => {
  const m: Record<string, string> = {}
  devices.value.forEach(d => { m[d.id] = d.name })
  return m
})

function siteName(id: string): string {
  return siteNames.value[id] ?? id
}
function deviceName(id: string): string {
  return deviceNames.value[id] ?? id
}

function stateText(s: AlarmState): string {
  return { Active: '活跃', Acknowledged: '已确认', Resolved: '已恢复', Cleared: '已清除' }[s] ?? s
}

async function load(): Promise<void> {
  loading.value = true
  try {
    rows.value = await getAlarms({
      siteId: q.siteId || undefined,
      severity: q.severity || undefined,
      state: q.state || undefined,
      limit: 500
    })
  } catch {
    rows.value = []
  }
  loading.value = false
  await loadFeed()
}

// 实时追加：符合当前过滤条件的新告警插到顶部（去重 + 上限）
function onAlarm(a: AlarmRecord): void {
  if (q.siteId && a.siteId !== q.siteId) return
  if (q.severity && a.severity !== q.severity) return
  if (q.state && a.state !== q.state) return
  const i = rows.value.findIndex(x => x.id === a.id)
  if (i >= 0) rows.value.splice(i, 1)
  rows.value.unshift(a)
  if (rows.value.length > 500) rows.value.length = 500
}

async function ack(a: AlarmRecord): Promise<void> {
  try {
    await ackAlarm(a.id)
    ElMessage.success('已确认')
    await load()
  } catch { /* 拦截器已提示 */ }
}

onMounted(async () => {
  try {
    sites.value = await getSites()
  } catch { /* 忽略 */ }
  try {
    devices.value = await getSiteDevices(q.siteId ?? '')
  } catch { /* 忽略 */ }
  onHubEvent<AlarmRecord>('OnAlarm', onAlarm)
  // 建立实时链路（仅本页），失败静默，走 10s KPI 轮询兜底
  await startHub()
  await load()
})

onUnmounted(() => {
  offHubEvent('OnAlarm', onAlarm as (...args: unknown[]) => void)
  stopHub()
})
</script>

<style scoped>
.admin-page { display: flex; flex-direction: column; gap: 16px; }
.page-head { display: flex; align-items: flex-end; justify-content: space-between; gap: 16px; }
.page-title { font-size: 18px; font-weight: 700; color: var(--text-heading); }
.page-desc { margin-top: 6px; font-size: 12px; color: var(--text-muted); }
.kpi-row { display: flex; gap: 16px; }
.kpi-box { flex: 1; max-width: 220px; background: var(--bg-card); border: 1px solid var(--border); border-radius: var(--radius); box-shadow: var(--shadow); padding: 16px 20px; }
.kpi-num { font-size: 30px; font-weight: 700; font-family: var(--font-mono); }
.kpi-num.red { color: var(--red); }
.kpi-num.orange { color: var(--orange); }
.kpi-label { margin-top: 4px; font-size: 12px; color: var(--text-muted); }
.filter-bar { margin-bottom: 14px; display: flex; gap: 12px; align-items: center; flex-wrap: wrap; }
.small { font-size: 12px; }
.sep { margin: 0 4px; color: var(--text-muted); }
.muted { color: var(--text-muted); }
</style>
