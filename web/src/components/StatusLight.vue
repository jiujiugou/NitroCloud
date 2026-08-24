<template>
  <span class="status-light" :class="connected ? 'ok' : 'fail'">
    <span class="dot"></span>
    <span class="text">{{ label ?? (connected ? '实时链路正常' : '链路断开') }}</span>
  </span>
</template>

<script setup lang="ts">
// 链路状态灯（ADR-009 D7 顶部条）：绿 = SignalR 已连接，红 = 断开/未连接
defineProps<{ connected: boolean; label?: string }>()
</script>

<style scoped>
.status-light { display: inline-flex; align-items: center; gap: 6px; font-size: 12px; }
.dot { width: 8px; height: 8px; border-radius: 50%; }
.status-light.ok .dot { background: var(--green); box-shadow: 0 0 6px var(--green); }
.status-light.fail .dot { background: var(--red); box-shadow: 0 0 6px var(--red); }
.text { color: var(--text-muted); }
</style>
