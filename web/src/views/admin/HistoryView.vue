<template>
  <div class="admin-page">
    <div class="page-head">
      <div>
        <div class="page-title">历史数据</div>
        <div class="page-desc">按 站点 → 设备 → 点位 三级定位查询时序数据（仅存 InfluxDB，DESIGN.md §5.2）。</div>
      </div>
    </div>

    <el-card shadow="never">
      <div class="filter-bar">
        <el-select v-model="siteId" placeholder="选择站点" clearable filterable style="width: 200px" @change="onSiteChange">
          <el-option v-for="s in sites" :key="s.id" :label="s.name" :value="s.id" />
        </el-select>
        <el-select v-model="deviceId" placeholder="先选站点" clearable filterable style="width: 200px" @change="onDeviceChange">
          <el-option v-for="d in devices" :key="d.id" :label="d.name" :value="d.id" />
        </el-select>
        <el-select v-model="pointId" placeholder="先选设备" clearable filterable style="width: 200px">
          <el-option v-for="p in points" :key="p.id" :label="p.name" :value="p.id" />
        </el-select>
        <el-date-picker
          v-model="range"
          type="datetimerange"
          range-separator="至"
          start-placeholder="开始时间"
          end-placeholder="结束时间"
          style="width: 380px"
        />
        <el-button type="primary" :icon="Search" :loading="loading" @click="query">查询</el-button>
        <el-button :icon="Download" :disabled="!siteId" @click="doExport">导出 CSV</el-button>
      </div>

      <el-table :data="rows" v-loading="loading" stripe max-height="60vh">
        <el-table-column label="时间" width="190">
          <template #default="{ row }"><span class="mono">{{ fmtTime(row.timestamp) }}</span></template>
        </el-table-column>
        <el-table-column label="点位" min-width="150">
          <template #default="{ row }">
            <span class="mono small">{{ pointName(row.devicePointId) }}</span>
            <span class="muted small" style="margin-left: 6px">{{ row.devicePointId }}</span>
          </template>
        </el-table-column>
        <el-table-column label="值" min-width="120">
          <template #default="{ row }"><span class="mono strong">{{ fmtValue(row.value) }}</span></template>
        </el-table-column>
        <el-table-column label="质量" width="100">
          <template #default="{ row }">
            <el-tag :type="qualityTag(row.quality)" size="small">{{ row.quality }}</el-tag>
          </template>
        </el-table-column>
      </el-table>
      <div v-if="!loading && rows.length === 0" class="table-empty">无数据：请选择时间范围与点位后点击「查询」。</div>
    </el-card>
  </div>
</template>

<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { Download, Search } from '@element-plus/icons-vue'
import { getSites } from '../../api/sites'
import { getSiteDevices } from '../../api/devices'
import { getDevicePoints } from '../../api/points'
import { exportHistory, getHistory } from '../../api/history'
import type { Device, Point, PointSnapshot, Quality, Site } from '../../api/types'
import { fmtTime, fmtValue } from '../../utils/format'

const sites = ref<Site[]>([])
const devices = ref<Device[]>([])
const points = ref<Point[]>([])
const siteId = ref('')
const deviceId = ref('')
const pointId = ref('')
const range = ref<[Date, Date] | null>(null)
const rows = ref<PointSnapshot[]>([])
const loading = ref(false)

function qualityTag(q: Quality): 'success' | 'warning' | 'danger' | 'info' {
  if (q === 'Good') return 'success'
  if (q === 'Uncertain') return 'warning'
  if (q === 'Bad') return 'danger'
  return 'info'
}

function pointName(id: string): string {
  return points.value.find(p => p.id === id)?.name ?? id
}

async function onSiteChange(v: string | undefined): Promise<void> {
  deviceId.value = ''
  pointId.value = ''
  points.value = []
  if (!v) return
  try {
    devices.value = await getSiteDevices(v)
  } catch {
    devices.value = []
  }
}

async function onDeviceChange(v: string | undefined): Promise<void> {
  pointId.value = ''
  if (!v) return
  try {
    points.value = await getDevicePoints(v)
  } catch {
    points.value = []
  }
}

function buildQuery(): { siteId: string; deviceId?: string; devicePointId?: string; from: string; to: string; limit?: number } | null {
  if (!siteId.value) return null
  if (!range.value || range.value.length !== 2 || !range.value[0] || !range.value[1]) return null
  return {
    siteId: siteId.value,
    deviceId: deviceId.value || undefined,
    devicePointId: pointId.value || undefined,
    from: range.value[0].toISOString(),
    to: range.value[1].toISOString(),
    limit: 2000
  }
}

async function query(): Promise<void> {
  const q = buildQuery()
  if (!q) {
    rows.value = []
    return
  }
  loading.value = true
  try {
    rows.value = await getHistory(q)
  } catch {
    rows.value = []
  }
  loading.value = false
}

async function doExport(): Promise<void> {
  const q = buildQuery()
  if (!q) return
  try {
    await exportHistory(q)
  } catch { /* 拦截器已提示 */ }
}

onMounted(async () => {
  // 默认查询最近 1 小时
  range.value = [new Date(Date.now() - 3600 * 1000), new Date()]
  try {
    sites.value = await getSites()
  } catch { /* 拦截器已提示 */ }
})
</script>

<style scoped>
.admin-page { display: flex; flex-direction: column; gap: 16px; }
.page-head { display: flex; align-items: flex-end; justify-content: space-between; gap: 16px; }
.page-title { font-size: 18px; font-weight: 700; color: var(--text-heading); }
.page-desc { margin-top: 6px; font-size: 12px; color: var(--text-muted); }
.filter-bar { margin-bottom: 14px; display: flex; gap: 12px; align-items: center; flex-wrap: wrap; }
.small { font-size: 12px; }
.strong { font-weight: 700; color: var(--text-heading); }
.muted { color: var(--text-muted); }
.table-empty { padding: 28px; text-align: center; color: var(--text-muted); font-size: 13px; }
</style>
