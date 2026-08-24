<template>
  <div class="dash">
    <!-- 顶部条：标题 + 链路状态 + 全局 KPI（ADR-009 D7） -->
    <header class="top-bar">
      <div class="brand">
        <div class="brand-icon">☁️</div>
        <div class="brand-text">
          <div class="brand-name">NitroCloud 设备上云中心</div>
          <div class="brand-sub">多站点实时监控</div>
        </div>
      </div>
      <StatusLight :connected="realtime.connected.value" />
      <div class="kpis">
        <KpiCard label="站点数" :value="sites.length" color="var(--accent)" />
        <KpiCard label="在线率" :value="`${onlineRate}%`" color="var(--green)" />
        <KpiCard label="活跃告警" :value="activeCount" color="var(--red)" />
        <KpiCard label="今日告警" :value="today" color="var(--orange)" />
      </div>
    </header>

    <!-- 中左：站点总览（SiteCard 列表，点击联动曲线） -->
    <section class="panel sites-panel">
      <div class="panel-title">站点总览 <span class="panel-meta">{{ sites.length }} 个现场</span></div>
      <div class="sites-body">
        <div v-if="sites.length === 0" class="panel-empty">
          暂无站点<br /><span class="sub">请先在管理面板创建站点</span>
        </div>
        <SiteCard
          v-for="s in sites"
          :key="s.id"
          :site="s"
          :alarm-count="siteAlarmCount[s.id] ?? 0"
          :selected="s.id === selectedSiteId"
          @select="onSiteSelect"
        />
      </div>
    </section>

    <!-- 中右：实时曲线（站点 → 设备 → 点位 三级选择） -->
    <section class="panel chart-panel">
      <div class="chart-head">
        <span class="panel-title">实时曲线</span>
        <el-select v-model="chartDevice" size="small" placeholder="设备" style="width: 170px" @change="onDeviceChange">
          <el-option v-for="d in siteDevices" :key="d.id" :label="d.name" :value="d.id" />
        </el-select>
        <el-select v-model="chartPointKey" size="small" placeholder="点位" filterable clearable style="width: 190px" @change="onPointChange">
          <el-option v-for="o in sitePointOptions" :key="o.value" :label="o.label" :value="o.value" />
        </el-select>
        <span class="chart-label mono">{{ chartLabel || '未选择点位' }}</span>
      </div>
      <div class="chart-body">
        <RealtimeChart ref="chartRef" :series="chartSeries" :point-label="chartLabel" :accent="'var(--accent)'" />
        <div v-if="!chartPointKey" class="chart-empty">在下方点位或上方下拉中选择点位，此处显示最近 2 小时实时曲线</div>
      </div>
    </section>

    <!-- 右栏：告警滚动（级别着色 + 站点前缀） -->
    <section class="panel alarms-panel">
      <div class="panel-title">实时告警 <span class="panel-meta">{{ feedAlarms.length }}</span></div>
      <AlarmTicker :alarms="feedAlarms" :site-names="siteNames" />
    </section>

    <!-- 左下方：点位快照（实时值 / 质量灰显 / 写值按钮） -->
    <section class="panel points-panel">
      <div class="panel-title">点位快照 <span class="panel-meta">{{ selectedSiteName }}</span></div>
      <div class="points-body">
        <div v-if="snapshotGroups.length === 0" class="panel-empty">
          该站点暂无点位
        </div>
        <template v-for="g in snapshotGroups" :key="g.deviceId">
          <div class="device-head">
            <span class="dh-dot" :class="deviceStatusClass(g.deviceId)"></span>
            <span class="dh-name">{{ g.deviceName }}</span>
            <span class="dh-points">{{ g.points.length }} 点</span>
          </div>
          <PointRow
            v-for="p in g.points"
            :key="p.pointId"
            :point="p"
            :value="latest[selectedSiteId]?.[p.pointId]?.value"
            :quality="latest[selectedSiteId]?.[p.pointId]?.quality"
            :stale="isStale(p.pointId)"
            :active="chartPointKey === `${p.deviceId}:${p.pointId}`"
            @select="onPointRowSelect(p)"
            @write="openWrite(p)"
          />
        </template>
      </div>
    </section>

    <!-- 写值弹窗（云端异步回执） -->
    <CommandDialog
      v-model:visible="cmdDialog"
      :point="cmdPoint"
      :current-value="cmdPoint ? latest[cmdPoint.siteId]?.[cmdPoint.pointId]?.value : undefined"
      :sending="cmdSending"
      @confirm="onWriteConfirm"
    />
  </div>
</template>

<script setup lang="ts">
import { ref, reactive, computed, onMounted, onUnmounted } from 'vue'
import { ElMessage } from 'element-plus'
import { getSites } from '../../api/sites'
import { getSiteDevices } from '../../api/devices'
import { getDevicePoints } from '../../api/points'
import { getHistory } from '../../api/history'
import { onHubEvent } from '../../api/signalr'
import { useRealtimeSite } from '../../composables/useRealtimeSite'
import { useLatestValues } from '../../composables/useLatestValues'
import { useAlarmFeed } from '../../composables/useAlarmFeed'
import { useCommand } from '../../composables/useCommand'
import KpiCard from '../../components/KpiCard.vue'
import StatusLight from '../../components/StatusLight.vue'
import SiteCard from '../../components/SiteCard.vue'
import PointRow from '../../components/PointRow.vue'
import RealtimeChart, { type ChartPoint } from '../../components/RealtimeChart.vue'
import AlarmTicker from '../../components/AlarmTicker.vue'
import CommandDialog from '../../components/CommandDialog.vue'
import { toNum } from '../../utils/format'
import type { Device, MeasurementRecord, PointRowMeta, Site } from '../../api/types'

// ── 数据容器（ADR-009 D5：reactive Map 存 siteId → 站点/最近值/告警 feed，不引 Pinia） ──
const sites = ref<Site[]>([])
const devices = reactive<Record<string, Device[]>>({})
const pointMetas = reactive<Record<string, PointRowMeta[]>>({})

const { latest, loaded, loadSite } = useLatestValues()
const { alarms: feedAlarms, activeCount, today, load: loadAlarms } = useAlarmFeed()
const realtime = useRealtimeSite()
const { sending: cmdSending, send: sendCommand } = useCommand()

// ── 三级选择（站点 → 设备 → 点位） ──
const selectedSiteId = ref('')
const chartDevice = ref('')
const chartPointKey = ref('')
const chartLabel = ref('')
const chartSeries = ref<ChartPoint[]>([])
const chartRef = ref<InstanceType<typeof RealtimeChart> | null>(null)

const MAX_CHART_POINTS = 7200
const STALE_AFTER_MS = 10 * 60 * 1000 // 2 × 心跳 300s（复用网关 ADR-053 结论）
let nowTick = ref(Date.now())
let staleTimer: ReturnType<typeof setInterval> | undefined
let kpiTimer: ReturnType<typeof setInterval> | undefined
let reconnectedOff: (() => void) | undefined

// ── KPI ──
const onlineRate = computed(() => {
  if (sites.value.length === 0) return 0
  const online = sites.value.filter(s => s.status === 'Online').length
  return Math.round((online / sites.value.length) * 100)
})

const siteAlarmCount = computed(() => {
  const map: Record<string, number> = {}
  feedAlarms.value.forEach(a => {
    if (a.state === 'Active') map[a.siteId] = (map[a.siteId] ?? 0) + 1
  })
  return map
})

const siteNames = computed(() => {
  const map: Record<string, string> = {}
  sites.value.forEach(s => { map[s.id] = s.name })
  return map
})

const selectedSiteName = computed(() => {
  return sites.value.find(s => s.id === selectedSiteId.value)?.name ?? '--'
})

// ── 三级选择选项 ──
const siteDevices = computed(() => devices[selectedSiteId.value] ?? [])
const sitePoints = computed(() => {
  const metas = pointMetas[selectedSiteId.value] ?? []
  const filtered = chartDevice.value ? metas.filter(m => m.deviceId === chartDevice.value) : metas
  return filtered.filter(m => m.enabled !== false)
})
const sitePointOptions = computed(() =>
  sitePoints.value.map(m => ({ value: `${m.deviceId}:${m.pointId}`, label: m.name }))
)

function pointLabel(key: string): string {
  const [deviceId, pointId] = key.split(':')
  const m = (pointMetas[selectedSiteId.value] ?? []).find(x => x.deviceId === deviceId && x.pointId === pointId)
  return m ? `${m.name}${m.unit ? ` (${m.unit})` : ''}` : ''
}

// ── 站点/设备/点位加载 ──
async function loadSiteGraph(siteId: string): Promise<void> {
  if (!loaded[siteId]) await loadSite(siteId)
  if (!devices[siteId]) {
    try {
      devices[siteId] = await getSiteDevices(siteId)
    } catch {
      devices[siteId] = []
    }
  }
  if (!pointMetas[siteId]) {
    const metas: PointRowMeta[] = []
    for (const d of devices[siteId]) {
      try {
        const pts = await getDevicePoints(d.id)
        pts.forEach(p => {
          metas.push({
            siteId,
            deviceId: d.id,
            pointId: p.id,
            name: p.name,
            dataType: p.dataType,
            unit: p.unit,
            access: p.access,
            enabled: p.enabled ?? true
          })
        })
      } catch { /* 单设备点位失败不阻塞整站 */ }
    }
    pointMetas[siteId] = metas
  }
}

async function loadSites(): Promise<void> {
  try {
    const list = await getSites()
    sites.value = list
    if (list.length > 0 && !selectedSiteId.value) {
      selectedSiteId.value = list[0].id
    }
    await Promise.all(list.map(s => loadSiteGraph(s.id)))
    await ensureChartSelection()
  } catch { /* 后端未起，保持空态 */ }
}

async function ensureChartSelection(): Promise<void> {
  if (chartPointKey.value) return
  const first = sitePoints.value[0]
  if (first) await selectPoint(`${first.deviceId}:${first.pointId}`)
}

// ── 曲线 ──
async function loadChartHistory(): Promise<void> {
  const [deviceId, pointId] = chartPointKey.value.split(':')
  if (!deviceId || !pointId || !selectedSiteId.value) {
    chartSeries.value = []
    return
  }
  const from = new Date(Date.now() - 2 * 3600 * 1000).toISOString()
  const to = new Date().toISOString()
  chartSeries.value = []
  try {
    const rows = await getHistory({
      siteId: selectedSiteId.value,
      deviceId,
      devicePointId: pointId,
      from,
      to,
      limit: 1000
    })
    chartSeries.value = rows
      .map(s => ({ time: s.timestamp, value: toNum(s.value) }))
      .filter((p): p is ChartPoint => p.value !== null)
  } catch { /* 历史查询失败保持空曲线 */ }
}

async function selectPoint(key: string): Promise<void> {
  const [deviceId] = key.split(':')
  chartDevice.value = deviceId
  chartPointKey.value = key
  chartLabel.value = pointLabel(key)
  await loadChartHistory()
}

function onPointChange(key: string | undefined): void {
  if (!key) {
    chartDevice.value = ''
    chartPointKey.value = ''
    chartLabel.value = ''
    chartSeries.value = []
    return
  }
  selectPoint(key)
}

function onDeviceChange(): void {
  const first = sitePoints.value[0]
  if (first) selectPoint(`${first.deviceId}:${first.pointId}`)
  else {
    chartPointKey.value = ''
    chartLabel.value = ''
    chartSeries.value = []
  }
}

function onPointRowSelect(p: PointRowMeta): void {
  selectPoint(`${p.deviceId}:${p.pointId}`)
}

// 站点卡点击 → 切站点并重置三级选择
async function onSiteSelect(siteId: string): Promise<void> {
  if (siteId === selectedSiteId.value) return
  selectedSiteId.value = siteId
  chartDevice.value = ''
  chartPointKey.value = ''
  chartLabel.value = ''
  chartSeries.value = []
  await loadSiteGraph(siteId)
  await ensureChartSelection()
}

// ── SignalR 增量：命中选中点位 → 追加环形缓冲（渲染节流交给 RealtimeChart 500ms） ──
function onMeasurements(data: MeasurementRecord | MeasurementRecord[]): void {
  const list = Array.isArray(data) ? data : [data]
  list.forEach(m => {
    if (!chartPointKey.value || !selectedSiteId.value) return
    if (m.siteId !== selectedSiteId.value) return
    const [deviceId, pointId] = chartPointKey.value.split(':')
    if (m.deviceId !== deviceId || m.devicePointId !== pointId) return
    const v = toNum(m.value)
    if (v === null) return
    chartSeries.value.push({ time: m.timestamp, value: v })
    const overflow = chartSeries.value.length - MAX_CHART_POINTS
    if (overflow > 0) chartSeries.value.splice(0, overflow)
  })
}

function onDeviceStatus(d: { siteId: string; deviceId: string; status: string }): void {
  const dev = devices[d.siteId]?.find(x => x.id === d.deviceId)
  if (dev) dev.status = d.status as Device['status']
}

// ── 快照列表（按设备分组） ──
const snapshotGroups = computed(() => {
  const metas = pointMetas[selectedSiteId.value] ?? []
  const map: Record<string, PointRowMeta[]> = {}
  metas.forEach(m => {
    if (m.enabled === false) return
    (map[m.deviceId] = map[m.deviceId] ?? []).push(m)
  })
  const nameMap: Record<string, string> = {}
  siteDevices.value.forEach(d => { nameMap[d.id] = d.name })
  return Object.entries(map).map(([deviceId, points]) => ({
    deviceId,
    deviceName: nameMap[deviceId] ?? deviceId,
    points
  }))
})

function deviceStatusClass(deviceId: string): string {
  const d = siteDevices.value.find(x => x.id === deviceId)
  return d?.status === 'Online' ? 'ok' : 'off'
}

function isStale(pointId: string): boolean {
  const lv = latest[selectedSiteId.value]?.[pointId]
  if (!lv || lv.value === undefined || lv.value === null) return true
  const ts = new Date(lv.timestamp).getTime()
  if (Number.isNaN(ts)) return false
  return nowTick.value - ts > STALE_AFTER_MS
}

// ── 写值闭环 ──
const cmdDialog = ref(false)
const cmdPoint = ref<PointRowMeta | null>(null)

function openWrite(p: PointRowMeta): void {
  cmdPoint.value = p
  cmdDialog.value = true
}

async function onWriteConfirm(value: unknown): Promise<void> {
  const p = cmdPoint.value
  cmdDialog.value = false
  if (!p) return
  const status = await sendCommand({
    siteId: p.siteId,
    deviceId: p.deviceId,
    pointId: p.pointId,
    value
  })
  if (status === 'Acked') ElMessage.success(`写入成功：${p.name}`)
  else if (status === 'Failed') ElMessage.error(`写入失败：${p.name}`)
  else if (status === 'Timeout') ElMessage.warning(`写入超时：${p.name}`)
  else ElMessage.error(`提交失败：${p.name}`)
}

// ── KPI 10s 轮询 + SignalR 兜底（ADR-009 D3） ──
async function refreshKpis(): Promise<void> {
  await loadSites()
  await loadAlarms()
}

onMounted(async () => {
  await refreshKpis()
  kpiTimer = window.setInterval(refreshKpis, 10_000)
  staleTimer = window.setInterval(() => { nowTick.value = Date.now() }, 30_000)

  // 大屏只有读：订阅全部站点（ADR-009 D3 站点级订阅）
  await realtime.connect(sites.value.map(s => s.id))
  // 重连后补拉快照 + 刷新 KPI（防白屏）
  reconnectedOff = realtime.refreshOnReconnect(async () => {
    for (const s of sites.value) await loadSite(s.id)
    await refreshKpis()
  })

  // SignalR 事件：曲线追加 / 设备状态（OnMeasurements 的最近值合并已由 useLatestValues 处理）
  onHubEvent<MeasurementRecord | MeasurementRecord[]>('OnMeasurements', onMeasurements)
  onHubEvent<{ siteId: string; deviceId: string; status: string }>('OnDeviceStatus', onDeviceStatus)
})

onUnmounted(() => {
  if (kpiTimer) window.clearInterval(kpiTimer)
  if (staleTimer) window.clearInterval(staleTimer)
  reconnectedOff?.()
  realtime.stopHub()
})
</script>

<style scoped>
/* ADR-009 D7：CSS Grid 三段式大屏布局（16:9 自适应，不做装饰动效） */
.dash {
  height: 100%;
  display: grid;
  grid-template-columns: 300px 1fr 330px;
  grid-template-rows: 64px minmax(0, 42%) minmax(0, 1fr);
  grid-template-areas:
    "top top top"
    "sites chart alarms"
    "points chart alarms";
  gap: 12px;
  padding: 12px;
  background: var(--bg-primary);
  color: var(--text);
  overflow: hidden;
}

/* 顶部条 */
.top-bar { grid-area: top; display: flex; align-items: center; gap: 20px; padding: 0 6px; }
.brand { display: flex; align-items: center; gap: 10px; }
.brand-icon { font-size: 26px; }
.brand-name { font-size: 17px; font-weight: 700; color: var(--text-heading); }
.brand-sub { font-size: 11px; color: var(--text-muted); }
.kpis { margin-left: auto; display: flex; gap: 12px; }

.panel {
  background: var(--bg-card);
  border: 1px solid var(--border);
  border-radius: var(--radius);
  box-shadow: var(--shadow);
  display: flex;
  flex-direction: column;
  min-height: 0;
  overflow: hidden;
}
.sites-panel { grid-area: sites; }
.chart-panel { grid-area: chart; }
.alarms-panel { grid-area: alarms; }
.points-panel { grid-area: points; }

.panel-title {
  flex-shrink: 0;
  padding: 10px 14px;
  border-bottom: 1px solid var(--border);
  font-size: 13px;
  font-weight: 600;
  color: var(--text-heading);
  display: flex;
  align-items: center;
  justify-content: space-between;
}
.panel-meta { font-size: 11px; font-weight: 400; color: var(--text-muted); font-family: var(--font-mono); }
.panel-empty { padding: 24px 12px; text-align: center; color: var(--text-muted); font-size: 13px; line-height: 1.8; }
.panel-empty .sub { font-size: 11px; color: var(--text-muted); opacity: .8; }

/* 站点列表 */
.sites-body { flex: 1; overflow-y: auto; padding: 10px; display: flex; flex-direction: column; gap: 8px; }

/* 曲线 */
.chart-head { flex-shrink: 0; padding: 10px 14px; border-bottom: 1px solid var(--border); display: flex; align-items: center; gap: 10px; }
.chart-head .panel-title { padding: 0; border: none; }
.chart-label { font-size: 12px; color: var(--text-muted); overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.chart-body { flex: 1; position: relative; min-height: 0; }
.chart-empty {
  position: absolute; inset: 0;
  display: flex; align-items: center; justify-content: center;
  color: var(--text-muted); font-size: 13px;
  pointer-events: none; padding: 0 20px; text-align: center;
}

/* 告警 */
.alarms-panel .panel-title { flex-shrink: 0; }
.alarms-panel { padding-bottom: 6px; }

/* 点位快照 */
.points-body { flex: 1; overflow-y: auto; padding: 4px 6px; }
.device-head { display: flex; align-items: center; gap: 8px; padding: 8px 8px 4px; font-size: 12px; font-weight: 600; color: var(--text-muted); }
.dh-dot { width: 8px; height: 8px; border-radius: 50%; }
.dh-dot.ok { background: var(--green); }
.dh-dot.off { background: var(--red); }
.dh-name { color: var(--text-heading); }
.dh-points { font-size: 11px; font-weight: 400; }

@media (max-width: 1400px) {
  .dash { grid-template-columns: 260px 1fr 290px; }
}
</style>
