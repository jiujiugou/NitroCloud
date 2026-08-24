<template>
  <div class="kpi-card">
    <div class="kpi-value" :style="color ? { color } : undefined">{{ value }}</div>
    <div class="kpi-label">{{ label }}</div>
  </div>
</template>

<script setup lang="ts">
// 全局 KPI 卡（ADR-009 D3/D7）：站点数 / 在线率 / 今日告警等
defineProps<{ value: number | string; label: string; color?: string }>()
</script>

<style scoped>
.kpi-card {
  background: var(--bg-card);
  border: 1px solid var(--border);
  border-radius: var(--radius);
  padding: 10px 18px;
  text-align: center;
  min-width: 110px;
  box-shadow: var(--shadow);
}
.kpi-value { font-size: 28px; font-weight: 700; color: var(--text-heading); font-variant-numeric: tabular-nums; line-height: 1.2; }
.kpi-label { font-size: 12px; color: var(--text-muted); margin-top: 4px; }
</style>
