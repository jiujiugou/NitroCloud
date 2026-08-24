<template>
  <div class="alarm-ticker">
    <div v-if="alarms.length === 0" class="at-empty">暂无告警</div>
    <div v-for="a in alarms" :key="a.id" class="at-item">
      <el-tag :type="severityTag(a.severity)" size="small" effect="dark" class="at-tag">{{ a.severity }}</el-tag>
      <span class="at-site">{{ siteName(a.siteId) }}</span>
      <span class="at-msg" :title="a.message">{{ a.message }}</span>
      <span class="at-time mono">{{ fmtTime(a.occurredAt) }}</span>
    </div>
  </div>
</template>

<script setup lang="ts">
import type { AlarmRecord } from '../api/types'
import { fmtTime, severityTag } from '../utils/format'

// 告警滚动（ADR-009 D7 右栏）：级别着色 + 站点前缀，新→旧列表
const props = defineProps<{
  alarms: AlarmRecord[]
  siteNames: Record<string, string>
}>()

function siteName(siteId: string): string {
  return props.siteNames[siteId] ?? siteId
}
</script>

<style scoped>
.alarm-ticker { height: 100%; overflow-y: auto; display: flex; flex-direction: column; gap: 6px; padding: 4px 2px; }
.at-empty { color: var(--text-muted); font-size: 12px; text-align: center; padding: 24px 0; }
.at-item {
  display: grid;
  grid-template-columns: auto auto 1fr auto;
  align-items: center;
  gap: 8px;
  padding: 7px 10px;
  background: var(--bg-card-2);
  border: 1px solid var(--border);
  border-radius: 8px;
}
.at-tag { flex-shrink: 0; }
.at-site { font-size: 11px; color: var(--text-muted); white-space: nowrap; max-width: 90px; overflow: hidden; text-overflow: ellipsis; }
.at-msg { font-size: 12px; color: var(--text-heading); overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.at-time { font-size: 11px; color: var(--text-muted); white-space: nowrap; }
</style>
