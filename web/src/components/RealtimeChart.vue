<template>
  <div ref="chartRef" class="chart-canvas"></div>
</template>

<script setup lang="ts">
import { ref, watch, onMounted, onUnmounted, nextTick } from 'vue'
// ADR-009 D4：ECharts 按需引入（Line + Grid/Tooltip + Canvas），替代整包引入（沿用网关）
import * as echarts from 'echarts/core'
import { LineChart } from 'echarts/charts'
import { GridComponent, TooltipComponent } from 'echarts/components'
import { CanvasRenderer } from 'echarts/renderers'
echarts.use([LineChart, GridComponent, TooltipComponent, CanvasRenderer])

export interface ChartPoint { time: string; value: number }

const props = withDefaults(defineProps<{
  series: ChartPoint[]
  pointLabel?: string
  accent?: string
}>(), { pointLabel: '', accent: '#409eff' })

const chartRef = ref<HTMLElement>()
let chart: ReturnType<typeof echarts.init> | null = null
let redrawTimer: ReturnType<typeof setTimeout> | undefined

// 读当前主题的文本色（主题变量定义在 .theme-dark/.theme-light 根容器上，chart 元素继承）
function cssVar(name: string): string {
  if (!chartRef.value) return '#8b949e'
  return getComputedStyle(chartRef.value).getPropertyValue(name).trim() || '#8b949e'
}

// canvas 的 addColorStop / lineStyle 无法解析 CSS 的 var(--x)，必须先用 getComputedStyle 解析出实际色值；解析失败回退默认强调色。
function resolveColor(raw: string | undefined): string {
  if (!raw) return '#409eff'
  const v = raw.trim()
  if (v.startsWith('var(')) {
    const resolved = cssVar(v.slice(4, -1).trim())
    return /^#[0-9a-fA-F]{3,6}$/.test(resolved) ? resolved : '#409eff'
  }
  return v
}

// hex 色 → rgba 字符串；非法输入（名称色 / var() / 空串）回退 #409eff，保证不会输出 rgba(NaN,...) 引发 addColorStop 抛错。
function hexA(hex: string, alpha: number): string {
  const m = (hex ?? '').replace('#', '').trim()
  if (!/^(?:[0-9a-fA-F]{3}|[0-9a-fA-F]{6})$/.test(m)) return hexA('#409eff', alpha)
  const full = m.length === 3 ? m.split('').map(c => c + c).join('') : m
  const r = parseInt(full.slice(0, 2), 16)
  const g = parseInt(full.slice(2, 4), 16)
  const b = parseInt(full.slice(4, 6), 16)
  return `rgba(${r}, ${g}, ${b}, ${alpha})`
}

function render(): void {
  if (!chartRef.value) return
  if (!chart) chart = echarts.init(chartRef.value)
  const color = resolveColor(props.accent)
  const muted = cssVar('--text-muted')
  chart.setOption({
    tooltip: { trigger: 'axis' },
    xAxis: { type: 'time', axisLabel: { color: muted } },
    yAxis: { type: 'value', scale: true, axisLabel: { color: muted } },
    grid: { left: 52, right: 20, top: 30, bottom: 32 },
    series: [{
      name: props.pointLabel || '实时值',
      data: props.series.map(p => [p.time, p.value]),
      // ADR-009 D4：step:'end' 防长静默段假连线（复用网关 ADR-053 结论）
      type: 'line', step: 'end', showSymbol: false,
      lineStyle: { color, width: 2 },
      areaStyle: {
        color: {
          type: 'linear', x: 0, y: 0, x2: 0, y2: 1,
          colorStops: [
            { offset: 0, color: hexA(color, 0.18) },
            { offset: 1, color: hexA(color, 0) }
          ]
        }
      }
    }]
  }, { notMerge: true })
}

// 500ms 节流重绘（ADR-009 D4）：避免每条推送都全量重绘
function scheduleRedraw(): void {
  if (redrawTimer) return
  redrawTimer = setTimeout(() => {
    redrawTimer = undefined
    render()
  }, 500)
}

function onResize(): void {
  chart?.resize()
}

watch(() => props.series, scheduleRedraw, { deep: true })
watch(() => props.pointLabel, render)

onMounted(() => {
  nextTick(render)
  window.addEventListener('resize', onResize)
})

onUnmounted(() => {
  if (redrawTimer) clearTimeout(redrawTimer)
  window.removeEventListener('resize', onResize)
  chart?.dispose()
  chart = null
})

defineExpose({ resize: onResize })
</script>

<style scoped>
.chart-canvas { width: 100%; height: 100%; }
</style>
