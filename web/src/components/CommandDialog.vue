<template>
  <el-dialog
    :model-value="visible"
    title="云端写值"
    width="420"
    :append-to-body="true"
    @update:model-value="$emit('update:visible', $event)"
  >
    <div v-if="point" class="cmd-info">
      <span class="cmd-name">{{ point.name }}</span>
      <span class="cmd-meta mono">{{ point.dataType }}{{ point.unit ? ` · ${point.unit}` : '' }}</span>
    </div>

    <div class="cmd-input">
      <!-- 类型感知：Bool 用 switch、数值用 input-number、String 用 input（ADR-009 D6） -->
      <el-switch
        v-if="point?.dataType === 'Bool'"
        v-model="boolValue"
        active-text="ON"
        inactive-text="OFF"
      />
      <el-input-number
        v-else-if="point && isNumericType(point.dataType)"
        v-model="numValue"
        :step="step"
        controls-position="right"
        style="width: 100%"
      />
      <el-input
        v-else
        v-model="strValue"
        placeholder="输入值"
      />
    </div>

    <template #footer>
      <el-button @click="$emit('update:visible', false)">取消</el-button>
      <el-button type="primary" :loading="sending" @click="confirm">确认下发</el-button>
    </template>
  </el-dialog>
</template>

<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import type { PointRowMeta } from '../api/types'
import { isNumericType } from '../utils/format'

// 写值弹窗（ADR-009 D6）：类型感知输入 + 云端异步回执（发送逻辑由父组件 useCommand 处理）
const props = withDefaults(defineProps<{
  visible: boolean
  point: PointRowMeta | null
  currentValue?: unknown
  sending?: boolean
}>(), { currentValue: undefined, sending: false })

const emit = defineEmits<{
  'update:visible': [visible: boolean]
  confirm: [value: unknown]
}>()

const boolValue = ref(false)
const numValue = ref<number | undefined>(undefined)
const strValue = ref('')

const step = computed(() =>
  props.point?.dataType === 'Float' || props.point?.dataType === 'Double' ? 0.1 : 1
)

// 打开时以当前实时值为默认输入，减少重复输入（复用网关交互）
watch(() => props.visible, v => {
  if (!v || !props.point) return
  const cur = props.currentValue
  if (props.point.dataType === 'Bool') {
    boolValue.value = cur === true || cur === 1 || cur === '1'
  } else if (isNumericType(props.point.dataType)) {
    numValue.value = toNumber(cur) ?? 0
  } else {
    strValue.value = cur != null ? String(cur) : ''
  }
})

function toNumber(v: unknown): number | null {
  if (typeof v === 'number' && Number.isFinite(v)) return v
  if (typeof v === 'string' && v.trim() !== '') {
    const n = Number(v)
    return Number.isFinite(n) ? n : null
  }
  return null
}

function confirm(): void {
  if (!props.point) return
  const p = props.point
  if (p.dataType === 'Bool') emit('confirm', boolValue.value)
  else if (isNumericType(p.dataType)) emit('confirm', numValue.value ?? 0)
  else emit('confirm', strValue.value)
}
</script>

<style scoped>
.cmd-info { display: flex; align-items: center; justify-content: space-between; gap: 8px; margin-bottom: 16px; }
.cmd-name { font-weight: 600; font-size: 14px; color: var(--text-heading); }
.cmd-meta { font-size: 11px; color: var(--text-muted); }
.cmd-input { margin-bottom: 4px; }
</style>
