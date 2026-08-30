<template>
  <div class="admin-page">
    <div class="page-head">
      <div>
        <div class="page-title">站点管理</div>
        <div class="page-desc">站点由网关上行数据自动注册（ADR-013），仅可改名/补全，不可新增/删除（ADR-017）。</div>
      </div>
    </div>

    <el-card shadow="never">
      <el-table :data="rows" v-loading="loading" stripe>
        <el-table-column prop="name" label="站点名称" min-width="160">
          <template #default="{ row }">
            <span class="cell-name">{{ row.name }}</span>
          </template>
        </el-table-column>
        <el-table-column prop="location" label="位置" min-width="150">
          <template #default="{ row }">{{ row.location || '-' }}</template>
        </el-table-column>
        <el-table-column label="状态" width="110">
          <template #default="{ row }">
            <el-tag :type="onlineTag(row.status)" size="small">{{ statusText(row.status) }}</el-tag>
          </template>
        </el-table-column>
        <el-table-column label="最后上报" width="180">
          <template #default="{ row }"><span class="mono">{{ fmtTime(row.lastReportAt) }}</span></template>
        </el-table-column>
        <el-table-column label="创建时间" width="180">
          <template #default="{ row }"><span class="mono">{{ fmtTime(row.createdAt) }}</span></template>
        </el-table-column>
        <el-table-column label="操作" width="150" fixed="right">
          <template #default="{ row }">
            <el-button link type="primary" @click="openEdit(row)">编辑</el-button>
          </template>
        </el-table-column>
      </el-table>
    </el-card>

    <el-dialog v-model="dialog.visible" title="编辑站点" width="460">
      <el-form :model="form" label-width="70px">
        <el-form-item label="名称" required>
          <el-input v-model="form.name" placeholder="例如：上海一厂" />
        </el-form-item>
        <el-form-item label="位置">
          <el-input v-model="form.location" placeholder="可选，例如：上海市嘉定区" />
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
import { getSites, updateSite } from '../../api/sites'
import type { Site } from '../../api/types'
import { fmtTime, onlineTag, statusText } from '../../utils/format'

const rows = ref<Site[]>([])
const loading = ref(false)
const saving = ref(false)
const dialog = reactive({ visible: false, id: '' })
const form = reactive({ name: '', location: '' })

async function load(): Promise<void> {
  loading.value = true
  try {
    rows.value = await getSites()
  } catch { /* 拦截器已提示 */ }
  loading.value = false
}

function openEdit(s: Site): void {
  dialog.id = s.id
  form.name = s.name
  form.location = s.location ?? ''
  dialog.visible = true
}

async function save(): Promise<void> {
  if (!form.name.trim()) {
    ElMessage.warning('请输入站点名称')
    return
  }
  saving.value = true
  try {
    await updateSite(dialog.id, { name: form.name.trim(), location: form.location.trim() || undefined })
    ElMessage.success('已保存')
    dialog.visible = false
    await load()
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
.cell-name { font-weight: 600; color: var(--text-heading); }
</style>
