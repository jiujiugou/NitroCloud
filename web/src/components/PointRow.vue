<template>
  <div class="point-row" :class="{ stale, active }" @click="$emit('select')">
    <span class="pr-name" :title="point.name">{{ point.name }}</span>
    <span class="pr-value mono">{{ text }}</span>
    <span v-if="quality" class="pr-quality">
      <el-tag :type="qualityTag" size="small" effect="plain">{{ quality }}</el-tag>
    </span>
    <el-button
      v-if="point.access && point.access !== 'ReadOnly'"
      class="pr-write"
      size="small"
      text
      type="primary"
      @click.stop="$emit('write')"
    >写值</el-button>
  </div>
</template>

<script setup lang="ts">
import { computed } from 'vue'
import type { PointRowMeta, Quality } from '../api/types'
import { fmtValue } from '../utils/format'

// 点位快照行（ADR-009 D7 左下）：实时值 / 质量灰显 / 写值按钮；点击行 → 联动曲线
const props = defineProps<{
  point: PointRowMeta
  value?: unknown
  quality?: Quality
  stale?: boolean
  active?: boolean
}>()
defineEmits<{ select: []; write: [] }>()

const text = computed(() => fmtValue(props.value))
const qualityTag = computed(() => {
  const q = props.quality
  if (q === 'Good' || !q) return 'success'
  if (q === 'Uncertain') return 'warning'
  return 'danger'
})
</script>

<style scoped>
.point-row {
  display: grid;
  grid-template-columns: 1fr auto auto auto;
  align-items: center;
  gap: 8px;
  padding: 5px 10px;
  border-bottom: 1px solid var(--border);
  cursor: pointer;
  transition: background .12s;
}
.point-row:last-child { border-bottom: none; }
.point-row:hover { background: var(--bg-hover); }
.point-row.active { background: rgba(64, 158, 255, .08); box-shadow: inset 2px 0 0 var(--accent); }
.pr-name { font-size: 12px; color: var(--text-heading); overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.pr-value { font-size: 14px; font-weight: 700; color: var(--accent); text-align: right; min-width: 52px; }
.point-row.stale .pr-value { color: var(--text-muted); }
.pr-write { padding: 0 4px; min-width: 0; font-size: 12px; }
</style>
