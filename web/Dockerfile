# NitroCloud 前端镜像：node 构建（vue-tsc + vite build）→ nginx 静态托管 + 同源反代。
# 构建上下文 = web/（web/.dockerignore 剔除 node_modules/dist）。
#
# nginx 承担两个职责（同源规避 CORS：后端 CORS 仅放行 http://localhost:5173 的 dev 源）：
#   1) 托管 dist 静态文件，vue-router history 模式回退 /index.html
#   2) 反代 /api、/hubs → center:5100（/hubs 需 WebSocket Upgrade 头）
# 详见 web/nginx.conf。

FROM node:22-alpine AS build
WORKDIR /app

# 先拷锁文件做 npm ci：package-lock.json 未变时命中层缓存，跳过下载
COPY package.json package-lock.json ./
RUN npm ci

COPY . .
RUN npm run build

FROM nginx:stable-alpine AS final
COPY nginx.conf /etc/nginx/conf.d/default.conf
COPY --from=build /app/dist /usr/share/nginx/html

EXPOSE 80
