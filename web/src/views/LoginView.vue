<template>
  <div class="login-page">
    <div class="login-card">
      <div class="login-brand">
        <div class="brand-icon">☁️</div>
        <div class="brand-title">NitroCloud</div>
        <div class="brand-sub">设备上云中心平台 · 管理面板登录</div>
      </div>

      <el-form ref="formRef" :model="form" :rules="rules" label-position="top" size="large">
        <el-form-item label="用户名" prop="username">
          <el-input v-model="form.username" placeholder="请输入用户名" autocomplete="username" />
        </el-form-item>
        <el-form-item label="密码" prop="password">
          <el-input
            v-model="form.password"
            type="password"
            show-password
            placeholder="请输入密码"
            autocomplete="current-password"
            @keyup.enter="onSubmit"
          />
        </el-form-item>
        <el-button type="primary" class="login-btn" :loading="loading" @click="onSubmit">
          登 录
        </el-button>
      </el-form>

      <div class="login-tip">默认账号 admin / admin123（仅本地开发，部署须环境变量覆盖）</div>
    </div>
  </div>
</template>

<script setup lang="ts">
import { reactive, ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import type { FormInstance, FormRules } from 'element-plus'
import { login } from '../api/auth'
import { setSession } from '../auth/useAuth'

const router = useRouter()
const route = useRoute()

const formRef = ref<FormInstance>()
const loading = ref(false)
const form = reactive({ username: '', password: '' })

const rules: FormRules = {
  username: [{ required: true, message: '请输入用户名', trigger: 'blur' }],
  password: [{ required: true, message: '请输入密码', trigger: 'blur' }]
}

/**
 * 提交登录：成功 → setSession 写入会话并跳回 redirect（原访问路径）或管理面板；
 * 失败由 client.ts 拦截器统一提示（登录页 401 显示后端错误，不跳转）。
 */
async function onSubmit(): Promise<void> {
  if (!formRef.value) return
  const valid = await formRef.value.validate().catch(() => false)
  if (!valid) return

  loading.value = true
  try {
    const res = await login({ username: form.username.trim(), password: form.password })
    if (res) {
      setSession({ token: res.token, username: res.username, role: res.role })
      const redirect = typeof route.query.redirect === 'string' ? route.query.redirect : '/admin/sites'
      router.push(redirect)
    }
  } finally {
    loading.value = false
  }
}
</script>

<style scoped>
.login-page {
  height: 100%;
  display: flex;
  align-items: center;
  justify-content: center;
  background: var(--bg-primary);
}
.login-card {
  width: 380px;
  padding: 36px 32px 24px;
  background: var(--bg-card);
  border: 1px solid var(--border);
  border-radius: var(--radius);
  box-shadow: var(--shadow);
}
.login-brand {
  text-align: center;
  margin-bottom: 26px;
}
.brand-icon { font-size: 40px; }
.brand-title {
  margin-top: 8px;
  font-size: 22px;
  font-weight: 700;
  color: var(--text-heading);
}
.brand-sub {
  margin-top: 6px;
  font-size: 12px;
  color: var(--text-muted);
}
.login-btn {
  width: 100%;
  margin-top: 4px;
}
.login-tip {
  margin-top: 18px;
  text-align: center;
  font-size: 12px;
  color: var(--text-muted);
}
</style>
