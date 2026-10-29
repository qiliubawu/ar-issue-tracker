# 园区巡检 AR 标记与问题管理原型

巡检人员在现场用手机对准地面放置 AR 标记并上报问题，管理人员在电脑网页上查看问题、流转处理状态。

```
Unity AR 放置标记并上报  →  Go 后端保存（issues.json）  →  React 页面查看  →  React 修改处理状态
```

## 1. 使用版本

| 端 | 技术栈 |
| --- | --- |
| Unity 移动端 | Unity 2022.3 LTS（或 Unity 6）+ AR Foundation 5.x + ARCore / ARKit XR Plugin |
| Go 后端 | Go 1.22+，**纯标准库**，无第三方依赖，本地 `issues.json` 持久化 |
| React 管理端 | Node.js 18+、React 18、TypeScript 5、Vite 5 |

## 2. 目录结构

```
ar-issue-tracker/
├── backend/
│   ├── main.go                 # 全部后端逻辑（约 300 行，零依赖）
│   ├── go.mod
│   └── start_backend.bat       # Windows 双击启动
├── frontend/
│   ├── src/App.tsx             # 管理端页面（列表 / 状态流转 / 错误提示）
│   ├── src/index.css
│   └── start_frontend.bat      # Windows 双击启动
├── unity-ar/
│   └── Assets/Scripts/ARIssueReporter.cs   # 唯一脚本：自动建 UI + 放置标记 + 上报
├── .github/workflows/build-ios-ipa.yml     # 无 Mac 时用云端 Mac 编译 .ipa
├── README.md
└── AI_LOG.md
```

## 3. 启动方式

### 3.1 启动 Go 后端

```bash
cd backend
go run main.go
```

Windows 也可以直接双击 `backend/start_backend.bat`。启动后终端会打印：

```
====================================================
Go 后端服务启动成功！
电脑端 React 默认连接地址: http://localhost:8080
手机端 Unity 真机请填写以下局域网地址之一:
  -> http://192.168.10.11:8080
数据文件: .../backend/issues.json
====================================================
```

> 首次启动如果 Windows 弹出防火墙提示，**"专用网络"和"公用网络"都要勾选并允许**，否则手机连不上。
> 端口被占用时可换端口：`set PORT=8090 && go run main.go`（PowerShell 用 `$env:PORT=8090`）。

### 3.2 启动 React 管理端

```bash
cd frontend
npm install     # 首次运行
npm run dev
```

浏览器打开 <http://localhost:5173>。也可双击 `frontend/start_frontend.bat`。

### 3.3 运行 Unity AR 移动端

1. Unity Hub 新建项目，模板选 **AR Mobile**（已自带 AR Foundation / ARKit / ARCore）。
2. 把 `unity-ar/Assets/Scripts/ARIssueReporter.cs` 拖进项目的 `Assets/Scripts` 目录。
3. 在 Hierarchy 里选中 **XR Origin (AR Rig)**，把脚本拖到它身上（Inspector 出现 `AR Issue Reporter` 组件即成功）。
4. 点 **Play**：屏幕底部自动出现中文面板 → 点地面出现红球 → 填标题 → 提交 → 电脑网页刷新即可看到。

> **不需要**手动建 Canvas、不需要做预制体：`AR Plane Manager`、`AR Raycast Manager`、整套 UI 都由脚本在运行时自动创建。
>
> **不需要**改 `Active Input Handling`：脚本用 `#if ENABLE_LEGACY_INPUT_MANAGER / ENABLE_INPUT_SYSTEM` 同时兼容新旧输入系统，触摸与鼠标都能用，因此也不必重启编辑器。
>
> **打真机包时**才需要去 `Player → Other Settings` 把 **Allow downloads over HTTP** 设为 **Always allowed**（否则真机会拦截 `http://` 请求，报 `Cannot connect to destination host`；编辑器内调试不受此限）。

**真机（iOS/Android）打包前还要做：**

- `Project Settings → XR Plug-in Management`：iOS 勾 **ARKit**，Android 勾 **ARCore**；
- `Player → iOS → Other Settings → Camera Usage Description` 必须填写（如"需要使用相机进行园区AR巡检平面检测"），否则一开相机就闪退；
- `File → Build Settings` 切到 iOS/Android 后 Build 即可。

### 3.4 手机怎么连电脑

1. 手机和电脑连同一个 Wi-Fi（最稳的是**手机开热点、电脑连热点**，避免校园网/公司网隔离设备）；
2. 电脑上 `ipconfig` 查本机 IPv4（如 `192.168.10.11`）；
3. App 面板第一行的 **Go后端地址** 直接改成 `http://192.168.10.11:8080`，**不用重新打包**。

### 3.5 没有 Mac 时怎么把包装进 iPhone

1. Windows 上 `File → Build Settings → iOS → Switch Platform → Build`，导出 `ios-build` 目录；
2. 推送到 GitHub，`.github/workflows/build-ios-ipa.yml` 会用 GitHub 免费的 macOS runner 编译出未签名 `.ipa`（在 Actions 页面的 Artifacts 里下载）；
3. Windows 用 [Sideloadly](https://sideloadly.io/) 插数据线把 `.ipa` 装进 iPhone（需先装 iTunes / iCloud 官方驱动），首次打开前在手机"设置 → 通用 → VPN 与设备管理"信任开发者证书，并在"隐私与安全性 → 开发者模式"打开开发者模式。

> 仓库体积提醒：`ios-build` 里有 `libiPhone-lib.a`(≈197MB)、`libil2cpp.a`(≈65MB)，超过 GitHub 单文件 100MB 限制，需要打包成分卷或用 Git LFS。

## 4. 接口说明

| 方法 | 路径 | 说明 |
| --- | --- | --- |
| GET | `/api/health` | 健康检查，返回 `{"status":"ok","time":"..."}` |
| GET | `/api/issues` | 问题列表，按创建时间倒序 |
| POST | `/api/issues` | 上报问题；校验 `title` 非空（≤120 字）、`priority ∈ {low,medium,high}`，`status` 固定为 `open` |
| GET | `/api/issues/{id}` | 查询单条 |
| PATCH / PUT | `/api/issues/{id}/status` | 修改状态；校验 `status ∈ {open,in_progress,resolved}` |

统一错误格式：`{"error": "中文错误说明"}`。

状态码：`200` 成功、`201` 创建成功、`400` 参数错误、`404` 路径或 ID 不存在、`405` 方法不允许、`500` 落盘失败。

```bash
curl -X POST http://localhost:8080/api/issues \
  -H "Content-Type: application/json" \
  -d '{"title":"入口墙面破损","description":"左侧墙体存在裂缝","priority":"high","position":{"x":0.42,"y":0.03,"z":1.26}}'

curl -X PATCH http://localhost:8080/api/issues/<id>/status \
  -H "Content-Type: application/json" -d '{"status":"in_progress"}'
```

## 5. 基础数据结构（三端字段一致）

```json
{
  "id": "b1c2d3e4f5a60718",
  "title": "入口墙面破损",
  "description": "左侧墙体存在裂缝",
  "priority": "high",
  "status": "open",
  "position": { "x": 0.42, "y": 0.03, "z": 1.26 },
  "createdAt": "2026-10-29T11:20:31+08:00",
  "updatedAt": "2026-10-29T11:20:31+08:00"
}
```

- `priority`：`low` / `medium` / `high`
- `status`：`open` / `in_progress` / `resolved`
- `position`：AR 世界坐标（米），Unity 侧四舍五入到小数点后两位

## 6. 完成情况与已知限制

**已完成**

- 水平面检测 + AR 标记放置（红色球体，Unity 基础对象）；
- 点击输入框/按钮时不误放标记（`IsPointerOverGameObject` + `RaycastAll` 双重判断）；
- 未放置标记就提交、标题为空、后端连不上等场景均有明确中文提示；
- Go 后端 JSON 文件持久化、互斥锁保护、原子写盘、参数校验、CORS；
- React 列表展示、状态下拉流转、后端断开时红色错误横幅（不白屏）；
- 手机端后端地址可现场修改，无需重新打包。

**已知限制**

- 按题目说明，应用重启后不通过云锚点（Cloud Anchor）把历史标记恢复到真实物理位置，只保留坐标数据；
- 后端用单文件 JSON + 内存切片，适用于单机原型演示，不是生产级存储；
- AR 识别依赖光照与纹理，纯白墙面/玻璃桌面识别较慢。
