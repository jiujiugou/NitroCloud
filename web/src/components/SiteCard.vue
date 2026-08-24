<template>
  <div
    class="site-card"
    :class="{ selected, online: site.status === 'Online' }"
    @click="$emit('select', site.id)"
  >
    <div class="sc-top">
      <span class="sc-name">{{ site.name }}</span>
      <span class="sc-status" :class="site.status === 'Online' ? 'ok' : 'off'">{{ statusText }}</span>
    </div>
    <div class="sc-bottom">
      <span class="sc-time mono">最后上报 {{ timeAgo(site.lastReportAt) }}</span>
      <span class="sc-alarms" :class="{ has: alarmCount > 0 }">告警 {{ alarmCount }}</span>
    </div>
  </div>
</template>

<script setup lang="ts">
import { computed } from 'vue'
import type { Site } from '../api/types'
import { timeAgo } from '../utils/format'

// 站点卡（ADR-009 D7 中左）：在线/离线、最后上报、告警数；点击联动曲线
const props = defineProps<{ site: Site; alarmCount: number; selected?: boolean }>()
defineEmits<{ select: [siteId: string] }>()

const statusText = computed(() => {
  const s = props.site.status
  if (s === 'Online') return '在线'
  if (s === 'Maintenance') return '维护'
  if (s === 'Unknown') return '未知'
  return '离线'
})
</script>

<style scoped>
.site-card {
  background: var(--bg-card);
  border: 1px solid var(--border);
  border-left: 3px solid var(--red);
  border-radius: var(--radius);
  padding: 10px 14px;
  cursor: pointer;
  transition: border-color .15s, box-shadow .15s;
}
.site-card:hover { box-shadow: 0 2px 10px rgba(0, 0, 0, .25); }
.site-card.online { border-left-color: var(--green); }
.site-card.selected { border-color: var(--accent); box-shadow: 0 0 0 1px var(--accent); }
.sc-top { display: flex; align-items: center; justify-content: space-between; gap: 8px; }
.sc-name { font-weight: 600; font-size: 14px; color: var(--text-heading); overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.sc-status { font-size: 11px; padding: 1px 8px; border-radius: 10px; white-space: nowrap; }
.sc-status.ok { background: rgba(63, 185, 80, .15); color: var(--green); }
.sc-status.off { background: rgba(248, 81, 73, .15); color: var(--red); }
.sc-bottom { display: flex; align-items: center; justify-content: space-between; margin-top: 6px; font-size: 11px; color: var(--text-muted); }
.sc-alarms.has { color: var(--orange); font-weight: 600; }
</style>
