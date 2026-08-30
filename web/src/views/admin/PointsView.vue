<template>
  <div class="admin-page">
    <div class="page-head">
      <div>
        <div class="page-title">点位管理</div>
        <div class="page-desc">点位由网关上行数据自动注册（ADR-013），仅可改名/补全，不可新增/删除（ADR-017）；写值在大屏操作。</div>
      </div>
    </div>

    <el-card shadow="never">
      <div class="filter-bar">
        <el-select v-model="siteId" placeholder="选择站点" clearable filterable style="width: 220px" @change="onSiteChange">
          <el-option v-for="s in sites" :key="s.id" :label="s.name" :value="s.id" />
        </el-select>
        <el-select v-model="deviceId" placeholder="先选站点" clearable filterable style="width: 220px" @change="onDeviceChange">
          <el-option v-for="d in devices" :key="d.id" :label="d.name" :value="d.id" />
        </el-select>
      </div>
      <el-table :data="points" v-loading="loading" stripe>
        <el-table-column prop="name" label="点位名称" min-width="150">
          <template #default="{ row }"><span class="cell-name">{{ row.name }}</span></template>
        </el-table-column>
        <el-table-column prop="dataType" label="数据类型" width="110">
          <template #default="{ row }"><span class="mono">{{ row.dataType }}</span></template>
        </el-table-column>
        <el-table-column prop="unit" label="单位" width="90">
          <template #default="{ row }">{{ row.unit || '-' }}</template>
        </el-table-column>
        <el-table-column label="访问权限" width="120">
          <template #default="{ row }">
            <el-tag :type="accessTag(row.access)" size="small">{{ accessText(row.access) }}</el-tag>
          </template>
        </el-table-column>
        <el-table-column label="告警" width="90" align="center">
          <template #default="{ row }">
            <el-tag :type="row.alarmEnabled ? 'warning' : 'info'" size="small">{{ row.alarmEnabled ? '开' : '关' }}</el-tag>
          </template>
        </el-table-column>
        <el-table-column label="启用" width="90" align="center">
          <template #default="{ row }">
            <el-tag :type="row.enabled !== false ? 'success' : 'info'" size="small">{{ row.enabled !== false ? '启用' : '停用' }}</el-tag>
          </template>
        </el-table-column>
        <el-table-column label="操作" width="150" fixed="right">
          <template #default="{ row }">
            <el-button link type="primary" @click="openEdit(row)">编辑</el-button>
          </template>
        </el-table-column>
      </el-table>
    </el-card>

    <el-dialog v-model="dialog.visible" title="编辑点位" width="500">
      <el-form :model="form" label-width="90px">
        <el-form-item label="所属设备" required>
          <el-select v-model="form.deviceId" placeholder="选择设备" style="width: 100%">
            <el-option v-for="d in devices" :key="d.id" :label="d.name" :value="d.id" />
          </el-select>
        </el-form-item>
        <el-form-item label="名称" required>
          <el-input v-model="form.name" placeholder="例如：温度" />
        </el-form-item>
        <el-form-item label="数据类型" required>
          <el-select v-model="form.dataType" style="width: 100%">
            <el-option v-for="t in dataTypes" :key="t" :label="t" :value="t" />
          </el-select>
        </el-form-item>
        <el-form-item label="单位">
          <el-input v-model="form.unit" placeholder="可选，例如：℃" />
        </el-form-item>
        <el-form-item label="访问权限">
          <el-select v-model="form.access" style="width: 100%">
            <el-option v-for="a in accesses" :key="a" :label="a" :value="a" />
          </el-select>
        </el-form-item>
        <el-form-item label="告警使能">
          <el-switch v-model="form.alarmEnabled" />
        </el-form-item>
        <el-form-item label="启用">
          <el-switch v-model="form.enabled" />
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="dialog.visible = false">取消</el-button>
        <el-button type="primary" :loading="saving" @click="save">保存</el-button>
      </template>
    </el-dialog>
  </div>
</template>

<script setup lang="ts">
import { onMounted, reactive, ref } from 'vue'
import { ElMessage } from 'element-plus'
import { getSites } from '../../api/sites'
import { getSiteDevices } from '../../api/devices'
import { getDevicePoints, updatePoint } from '../../api/points'
import type { DataType, Device, Point, PointAccess, Site } from '../../api/types'

const dataTypes: DataType[] = ['Bool', 'Byte', 'Int16', 'UInt16', 'Int32', 'UInt32', 'Int64', 'UInt64', 'Float', 'Double', 'String']
const accesses: PointAccess[] = ['ReadOnly', 'WriteOnly', 'ReadWrite']

const sites = ref<Site[]>([])
const devices = ref<Device[]>([])
const points = ref<Point[]>([])
const siteId = ref('')
const deviceId = ref('')
const loading = ref(false)
const saving = ref(false)
const dialog = reactive({ visible: false, pointId: '' })
const form = reactive({
  deviceId: '',
  name: '',
  dataType: 'Float' as DataType,
  unit: '',
  access: 'ReadOnly' as PointAccess,
  alarmEnabled: false,
  enabled: true
})

function accessText(a?: PointAccess): string {
  return { ReadOnly: '只读', WriteOnly: '只写', ReadWrite: '读写' }[a ?? 'ReadOnly'] ?? a ?? '-'
}

function accessTag(a?: PointAccess): 'info' | 'warning' | 'success' {
  if (a === 'ReadWrite') return 'success'
  if (a === 'WriteOnly') return 'warning'
  return 'info'
}

async function onSiteChange(v: string | undefined): Promise<void> {
  deviceId.value = ''
  points.value = []
  if (!v) return
  try {
    devices.value = await getSiteDevices(v)
  } catch {
    devices.value = []
  }
}

async function onDeviceChange(v: string | undefined): Promise<void> {
  points.value = []
  if (!v) return
  loading.value = true
  try {
    points.value = await getDevicePoints(v)
  } catch {
    points.value = []
  }
  loading.value = false
}

async function load(): Promise<void> {
  try {
    sites.value = await getSites()
  } catch { /* 拦截器已提示 */ }
}

function openEdit(p: Point): void {
  dialog.pointId = p.id
  form.deviceId = p.deviceId
  form.name = p.name
  form.dataType = p.dataType
  form.unit = p.unit ?? ''
  form.access = p.access ?? 'ReadOnly'
  form.alarmEnabled = p.alarmEnabled ?? false
  form.enabled = p.enabled ?? true
  dialog.visible = true
}

async function save(): Promise<void> {
  if (!form.deviceId) {
    ElMessage.warning('请选择所属设备')
    return
  }
  if (!form.name.trim()) {
    ElMessage.warning('请输入点位名称')
    return
  }
  saving.value = true
  try {
    await updatePoint(form.deviceId, dialog.pointId, {
      name: form.name.trim(),
      dataType: form.dataType,
      unit: form.unit.trim() || undefined,
      access: form.access,
      alarmEnabled: form.alarmEnabled,
      enabled: form.enabled
    })
    ElMessage.success('已保存')
    dialog.visible = false
    if (form.deviceId === deviceId.value) await onDeviceChange(deviceId.value)
  } catch { /* 拦截器已提示 */ }
  saving.value = false
}

onMounted(load)
</script>

<style scoped>
.admin-page { display: flex; flex-direction: column; gap: 16px; }
.page-head { display: flex; align-items: flex-end; justify-content: space-between; gap: 16px; }
.page-title { font-size: 18px; font-weight: 700; color: var(--text-heading); }
.page-desc { margin-top: 6px; font-size: 12px; color: var(--text-muted); }
.filter-bar { margin-bottom: 14px; display: flex; gap: 12px; align-items: center; }
.cell-name { font-weight: 600; color: var(--text-heading); }
</style>
