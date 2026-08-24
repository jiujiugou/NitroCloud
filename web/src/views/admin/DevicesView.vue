<template>
  <div class="admin-page">
    <div class="page-head">
      <div>
        <div class="page-title">设备管理</div>
        <div class="page-desc">维护站点下的设备元数据（型号/在线状态）。</div>
      </div>
      <el-button type="primary" @click="openCreate">
        <el-icon style="margin-right: 4px"><Plus /></el-icon>新建设备
      </el-button>
    </div>

    <el-card shadow="never">
      <div class="filter-bar">
        <el-select v-model="siteFilter" placeholder="全部站点" clearable style="width: 220px">
          <el-option v-for="s in sites" :key="s.id" :label="s.name" :value="s.id" />
        </el-select>
      </div>
      <el-table :data="filtered" v-loading="loading" stripe>
        <el-table-column prop="name" label="设备名称" min-width="150">
          <template #default="{ row }"><span class="cell-name">{{ row.name }}</span></template>
        </el-table-column>
        <el-table-column prop="model" label="型号" min-width="120">
          <template #default="{ row }">{{ row.model || '-' }}</template>
        </el-table-column>
        <el-table-column label="所属站点" min-width="150">
          <template #default="{ row }">{{ siteName(row.siteId) }}</template>
        </el-table-column>
        <el-table-column label="状态" width="100">
          <template #default="{ row }">
            <el-tag :type="onlineTag(row.status)" size="small">{{ statusText(row.status) }}</el-tag>
          </template>
        </el-table-column>
        <el-table-column label="最后在线" width="180">
          <template #default="{ row }"><span class="mono">{{ fmtTime(row.lastSeenAt) }}</span></template>
        </el-table-column>
        <el-table-column label="操作" width="150" fixed="right">
          <template #default="{ row }">
            <el-button link type="primary" @click="openEdit(row)">编辑</el-button>
            <el-button link type="danger" @click="remove(row)">删除</el-button>
          </template>
        </el-table-column>
      </el-table>
    </el-card>

    <el-dialog v-model="dialog.visible" :title="dialog.isEdit ? '编辑设备' : '新建设备'" width="480">
      <el-form :model="form" label-width="80px">
        <el-form-item label="所属站点" required>
          <el-select v-model="form.siteId" placeholder="选择站点" style="width: 100%">
            <el-option v-for="s in sites" :key="s.id" :label="s.name" :value="s.id" />
          </el-select>
        </el-form-item>
        <el-form-item label="名称" required>
          <el-input v-model="form.name" placeholder="例如：PLC-1" />
        </el-form-item>
        <el-form-item label="型号">
          <el-input v-model="form.model" placeholder="可选，例如：S7-1200" />
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
import { computed, onMounted, reactive, ref } from 'vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import { Plus } from '@element-plus/icons-vue'
import { createDevice, deleteDevice, getAllDevices, updateDevice } from '../../api/devices'
import { getSites } from '../../api/sites'
import type { Device, Site } from '../../api/types'
import { fmtTime, onlineTag, statusText } from '../../utils/format'

const sites = ref<Site[]>([])
const rows = ref<Device[]>([])
const siteFilter = ref('')
const loading = ref(false)
const saving = ref(false)
const dialog = reactive({ visible: false, isEdit: false, id: '' })
const form = reactive({ siteId: '', name: '', model: '' })

const filtered = computed(() =>
  siteFilter.value ? rows.value.filter(d => d.siteId === siteFilter.value) : rows.value
)

function siteName(id: string): string {
  return sites.value.find(s => s.id === id)?.name ?? id
}

async function load(): Promise<void> {
  loading.value = true
  try {
    sites.value = await getSites()
    rows.value = await getAllDevices()
  } catch { /* 拦截器已提示 */ }
  loading.value = false
}

function openCreate(): void {
  dialog.isEdit = false
  dialog.id = ''
  form.siteId = siteFilter.value
  form.name = ''
  form.model = ''
  dialog.visible = true
}

function openEdit(d: Device): void {
  dialog.isEdit = true
  dialog.id = d.id
  form.siteId = d.siteId
  form.name = d.name
  form.model = d.model ?? ''
  dialog.visible = true
}

async function save(): Promise<void> {
  if (!form.siteId) {
    ElMessage.warning('请选择所属站点')
    return
  }
  if (!form.name.trim()) {
    ElMessage.warning('请输入设备名称')
    return
  }
  saving.value = true
  try {
    if (dialog.isEdit) {
      await updateDevice(dialog.id, { siteId: form.siteId, name: form.name.trim(), model: form.model.trim() || undefined })
    } else {
      await createDevice({ siteId: form.siteId, name: form.name.trim(), model: form.model.trim() || undefined })
    }
    ElMessage.success('已保存')
    dialog.visible = false
    await load()
  } catch { /* 拦截器已提示 */ }
  saving.value = false
}

async function remove(d: Device): Promise<void> {
  try {
    await ElMessageBox.confirm(`确定删除设备「${d.name}」？其下点位元数据将一并删除。`, '删除确认', {
      type: 'warning',
      confirmButtonText: '删除',
      cancelButtonText: '取消'
    })
  } catch {
    return
  }
  try {
    await deleteDevice(d.id)
    ElMessage.success('已删除')
    await load()
  } catch { /* 拦截器已提示 */ }
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
