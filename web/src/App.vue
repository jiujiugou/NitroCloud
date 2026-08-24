<template>
  <div :class="['app-root', isAdmin ? 'theme-light is-admin' : 'theme-dark']">
    <!-- 管理面板外壳：侧边导航 + 顶栏 + 内容区（ADR-009 D1 浅色 CRUD） -->
    <template v-if="isAdmin">
      <aside class="sidebar">
        <div class="sidebar-brand">
          <div class="brand-icon">☁️</div>
          <div class="brand-text">
            <div class="brand-name">NitroCloud</div>
            <div class="brand-sub">设备上云中心平台</div>
          </div>
        </div>
        <nav class="sidebar-nav">
          <router-link to="/admin/sites" class="nav-item" active-class="nav-active">
            <span class="nav-icon">📌</span><span>站点</span>
          </router-link>
          <router-link to="/admin/devices" class="nav-item" active-class="nav-active">
            <span class="nav-icon">🔌</span><span>设备</span>
          </router-link>
          <router-link to="/admin/points" class="nav-item" active-class="nav-active">
            <span class="nav-icon">📊</span><span>点位</span>
          </router-link>
          <router-link to="/admin/alarms" class="nav-item" active-class="nav-active">
            <span class="nav-icon">🔔</span><span>告警</span>
          </router-link>
          <router-link to="/admin/history" class="nav-item" active-class="nav-active">
            <span class="nav-icon">🕘</span><span>历史</span>
          </router-link>
        </nav>
        <div class="sidebar-footer">
          <router-link to="/dashboard" class="nav-item nav-dash">
            <span class="nav-icon">🖥️</span><span>实时大屏</span>
          </router-link>
          <div class="version-tag">v0.0 · M1 前端初版</div>
        </div>
      </aside>
      <main class="main-area">
        <header class="topbar">
          <div class="topbar-title">{{ pageTitle }}</div>
          <div class="topbar-right">
            <span class="status-dot" :class="hubConnected ? 'online' : 'offline'"></span>
            <span>{{ hubConnected ? '实时链路已连接' : '实时链路未连接' }}</span>
          </div>
        </header>
        <div class="content-area">
          <router-view />
        </div>
      </main>
    </template>

    <!-- 大屏：全屏只读，无导航 chrome（ADR-009 D7） -->
    <router-view v-else />
  </div>
</template>

<script setup lang="ts">
import { computed } from 'vue'
import { useRoute } from 'vue-router'
import { hubConnected } from './api/signalr'

const route = useRoute()

const isAdmin = computed(() => route.meta.layout === 'admin')
const pageTitle = computed(() => (route.meta.title as string) ?? 'NitroCloud')
</script>

<style scoped>
/* ADR-009 D1：外壳用 CSS variables，不写死颜色（theme-light/theme-dark 切换） */
.app-root { height: 100%; }
.app-root.is-admin { display: flex; overflow: hidden; }

/* ── 侧边导航 ── */
.sidebar {
  width: 220px;
  flex-shrink: 0;
  background: var(--bg-card);
  border-right: 1px solid var(--border);
  display: flex;
  flex-direction: column;
}
.sidebar-brand { padding: 22px 18px 18px; display: flex; align-items: center; gap: 10px; border-bottom: 1px solid var(--border); }
.brand-icon { font-size: 26px; }
.brand-name { color: var(--text-heading); font-size: 15px; font-weight: 700; }
.brand-sub { color: var(--text-muted); font-size: 11px; margin-top: 1px; }
.sidebar-nav { flex: 1; padding: 12px 10px; display: flex; flex-direction: column; gap: 2px; overflow-y: auto; }
.nav-item {
  display: flex; align-items: center; gap: 10px;
  padding: 10px 14px; border-radius: 8px;
  color: var(--text); text-decoration: none; font-size: 14px;
  transition: background .15s;
}
.nav-item:hover { background: var(--bg-hover); color: var(--text-heading); }
.nav-active { background: var(--bg-hover); color: var(--accent) !important; font-weight: 600; }
.nav-icon { font-size: 15px; width: 20px; text-align: center; }
.sidebar-footer { padding: 14px 18px; border-top: 1px solid var(--border); display: flex; flex-direction: column; gap: 10px; }
.nav-dash { background: var(--bg-card-2); }
.version-tag { display: inline-block; padding: 2px 10px; background: var(--bg-card-2); border: 1px solid var(--border); border-radius: 12px; color: var(--text-muted); font-size: 11px; }

/* ── 主区 ── */
.main-area { flex: 1; display: flex; flex-direction: column; overflow: hidden; }
.topbar {
  height: 52px; flex-shrink: 0;
  background: var(--bg-card);
  border-bottom: 1px solid var(--border);
  display: flex; align-items: center; justify-content: space-between;
  padding: 0 24px;
}
.topbar-title { color: var(--text-heading); font-weight: 600; font-size: 14px; }
.topbar-right { color: var(--text-muted); font-size: 12px; display: flex; align-items: center; gap: 8px; }
.status-dot { width: 8px; height: 8px; border-radius: 50%; }
.status-dot.online { background: var(--green); }
.status-dot.offline { background: var(--orange); }
.content-area { flex: 1; overflow-y: auto; padding: 24px; background: var(--bg-primary); }
</style>
