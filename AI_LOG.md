# AI 使用记录（AI_LOG.md）

> 按实际提问顺序记录本次作业中由我主动发给 AI 的全部指令（含追问与纠错），以及每条结果是否采纳、如何验证或修改。

---

## P-001

- 目标：确定三端（Unity AR + Go + React）的最简实现方案与核心代码框架

### Prompt 原文

> 园区巡检人员发现墙面破损、设备异常或安全隐患时，希望用手机在现场放置一个 AR 标记并上报问题。管理人员随后在电脑网页中查看这些问题并更新处理状态。
> 请完成一个小型原型，打通以下流程：
> Unity AR 放置标记并上报 → Go 后端保存 → React 页面查看问题 → React 修改处理状态
> 2. 固定技术栈：移动端 Unity LTS、C#、AR Foundation（检测平面、放置标记、上报问题）；管理端 React、TypeScript（查看问题、修改状态）；后端 Go（提供 API 并持久化数据）
> 3. 必做功能：3.1 Unity AR 移动端（标记可直接使用立方体、球体等 Unity 基础对象，不考察美术效果）；3.2 React 管理端；3.3 Go 后端需要为 Unity 和 React 提供相应能力，接口路径和响应格式可自行设计，并在 README 中简要说明
> 4. 基础数据结构（三端字段名称保持一致）：id / title / description / priority / status / position{x,y,z} / createdAt / updatedAt，id 与时间戳由后端生成；不要求应用重启后把历史标记恢复到完全相同的真实物理位置
> 5. AI 使用记录：在项目根目录提交 AI_LOG.md，按实际顺序保存本题中所有由你主动发给 AI 的问题或指令，包括追问和纠错
> 6. 交付内容：不强制提交 APK，不强制部署到公网；完成后通过仓库或文件访问链接分享全部交付内容

### 结果

- **修改后采用**。
- 采纳的部分：Go 端用 `net/http` + 本地 `issues.json`（零第三方依赖）；React 端用 Vite + TypeScript + 原生 `fetch`；Unity 端只用 `AR Raycast Manager` + `AR Plane Manager` + `UnityWebRequest`；手机与电脑同 Wi-Fi，用局域网 IP 直连。
- 验证与修改说明：
  1. **接口自测**：用 `curl` 依次验证 `GET /api/health`、`POST /api/issues`、`GET /api/issues`、`PATCH /api/issues/{id}/status`，确认返回码分别为 200 / 201 / 200 / 200。
  2. **边界校验**：`title` 传空字符串、`priority` 传 `urgent`、`status` 传 `done`，均返回 400 且带中文 `error` 字段；不存在的 ID 返回 404。
  3. **持久化验证**：提交 3 条数据后按 `Ctrl+C` 停掉 Go 进程，重新 `go run main.go`，列表数据与状态修改结果依然存在（`issues.json` 落盘成功）。
  4. **跨域验证**：React dev server（5173）直接请求 8080 未出现 CORS 报错，说明 `Access-Control-Allow-Origin: *` 与 OPTIONS 预检处理生效。

---

## P-002

- 目标：确认测试真机（iPhone 17）是否支持 Unity AR Foundation / ARKit，以及真机打包注意事项

### Prompt 原文

> iPhone17支持ar模块吗

### 结果

- **采用**。
- 验证说明：iPhone 17 原生支持 ARKit，Unity 侧需在 `XR Plug-in Management → iOS` 勾选 **ARKit**，并在 `Player → iOS → Other Settings` 填写 **Camera Usage Description**（不填打开相机必闪退）、把 **Allow downloads over HTTP** 设为 **Always allowed**（否则 ATS 会拦截 `http://` 局域网请求）。上述三项已写入 README 第 3.3 节。

---

## P-003

- 目标：解决「Windows 开发 + iPhone 17 真机」的跨平台构建限制

### Prompt 原文

> 我是Windows 电脑，但是我的手机是iPhone17

### 结果

- **采用**。
- 验证说明：确认 Windows 版 Unity 只能导出 Xcode 工程，无法直接完成 iOS 签名安装。因此开发阶段全部在 Windows 上完成（Go + React + Unity 的 XR Simulation 模拟器），真机部署阶段再单独处理。

---

## P-004

- 目标：确定真机部署路线

### Prompt 原文

> 我准备用方案二

### 结果

- **采用（分阶段方案）**：Windows 上完成 100% 代码与联调 → 导出 `ios-build` → 云端 macOS 编译 `.ipa` → Windows 用 Sideloadly 装机。
- 验证说明：Unity 的 `XR Simulation` 在 Windows 编辑器里可直接模拟水平面检测与点击放置，无需真机即可完成三端联调，已在本地跑通。

---

## P-005

- 目标：要一份「完全不懂也能做出来」的完整落地步骤

### Prompt 原文

> ok,现在重新给我完整落地这一整套的方案，要非常的详细因为我什么也不懂但是我要做出来

### 结果

- **修改后采用**。
- 修改点：AI 给的方案里有一步是「双击 `start_backend.bat`」，实测在 Windows 上因为 `.bat` 文件编码问题，中文会变成乱码，且 `nul` 重定向写法报 `'nul' 不是内部或外部命令`。已重写两个启动脚本：文件头加 `chcp 65001 >nul`，并把 `cd /d %~dp0` 放在第一行，实测双击可正常启动并显示中文。
- 验证说明：`start_backend.bat` 启动后终端正确打印局域网 IP 列表；`start_frontend.bat` 首次运行会自动 `npm install`，随后打印 `Local: http://localhost:5173/`。

---

## P-006

- 目标：改成一步一步的闯关式指导，每步等反馈再继续

### Prompt 原文

> 非常好，但是我需要你教我一步一步的去完成它

### 结果

- **采用**。
- 验证说明：按「检查环境 → 装 Go → 起后端 → 起前端 → 建 Unity 工程 → 挂脚本 → 编辑器联调 → 导出 iOS → 云编译 → 装机」十步推进，每步都有可验证的中间产物（终端输出、浏览器页面、Unity 画面）。

---

## P-007

- 目标：把之前的对话记录整理复现，并按记录把整套工程真正实现出来

### Prompt 原文

> 将这个文件里的内容完全不变的复现出来
>
> 我要你将这些步骤一步一步的实现出来

### 结果

- **采用**。
- 验证说明：见下节「检查与修改 AI 结果」。

---

## 检查、修改或放弃 AI 结果的过程

### 1. Unity UI 防误触（检查后修改）

AI 初版只调用 `EventSystem.current.IsPointerOverGameObject(touch.fingerId)`。真机测试发现：在部分 Input System 配置下，点击输入框仍会穿透触发 AR 平面射线，标记被瞬间移动到输入框后面。

**修改**：`IsPointerOverUI()` 增加 `EventSystem.current.RaycastAll(eventData, results)` 兜底判断（`results.Count > 0` 即视为点在 UI 上），并对面板本身设置 `raycastTarget = true`。真机上反复点击输入框、优先级按钮、提交按钮，确认红球不再移动或重复生成。

### 2. Go 启动报 `bind: Only one usage of each socket address`（检查后处理）

原因是重复双击启动脚本，前一个进程已占用 8080。

**处理**：先访问 `http://localhost:8080/api/health` 判断后端是否已在运行；确实被其他程序占用时改用 `PORT=8090` 启动，并在 README 中补充说明。

### 3. `.gitignore` 里被写入了空格（检查后修正）

用 `echo [Ll]ibrary/ & echo ...` 生成 `.gitignore` 时，Windows 把行尾空格也写了进去，导致 `unity-ar/Library` 缓存没被忽略，`git push` 体积暴涨到 634MB 并超时。

**修改**：改用 `Set-Content -Encoding ascii` 一次性写入规则，并追加 `ios-build/`、`*.tar.gz`。最终只上传源码（几 MB）。

### 4. GitHub 单文件 100MB 限制（放弃原方案，改用分卷）

`ios-build/Libraries/libiPhone-lib.a` 197MB、`libil2cpp.a` 65MB，`git push` 被 `pre-receive hook declined` 拒绝。

**处理**：把 `ios-build` 打成 tar.gz 并按 45MB 切成 `ios-build.part0…partN`，workflow 里 `cat ios-build.part* > ios-build.tar.gz` 再解压。第一次切分漏了最后 50MB 的尾巴（只有 part0~part3），云端报 `truncated gzip input`，补齐 part4/part5 后编译通过。

### 5. 云端编译产物下载（保留）

Actions 运行成功后，在 `Artifacts` 里下载 `ar-issue-unsigned-ipa`，解压得到 `ar-issue-unsigned.ipa`，再用 Sideloadly 装机。

### 6. 输入系统兼容（改进，去掉了“必须改设置 + 重启编辑器”）

AI 方案要求把 `Active Input Handling` 改成 `Both` 再重启编辑器，否则脚本里的 `Input.touchCount` 会抛 `InvalidOperationException`。但 AR Mobile 模板默认就是 `Input System Package (New)`，改设置 + 重启这一步对新手很不友好。

**修改**：脚本改用编译宏双路实现——`#if ENABLE_LEGACY_INPUT_MANAGER` 走 `Input.touchCount` / `Input.GetMouseButtonDown`，`#elif ENABLE_INPUT_SYSTEM` 走 `EnhancedTouch` / `Mouse.current`，两条路径都能同时支持真机触摸与编辑器鼠标。同时把防误触判断从 `IsPointerOverGameObject(fingerId)` 改成用屏幕坐标自己做 `GraphicRaycaster.RaycastAll`，避免新旧输入系统下 fingerId 映射不一致的问题。改完后**无需修改任何 Player Settings、无需重启编辑器**。

### 7. git push 被重置（curl 55 / Connection was reset）—— 代理问题

第一次推分卷时报：

```
error: RPC failed; curl 55 Send failure: Connection was reset
send-pack: unexpected disconnect while reading sideband packet
fatal: unable to access ...: Failed to connect to github.com:443 after 21280 ms
```

排查结论：git 在**直连** github.com，而本机直连被重置。实测三条通道：

| 通道 | 结果 |
|---|---|
| 直连 `github.com:443` | 15 秒超时，`000` |
| 环境默认代理 `127.0.0.1:65233` | 10 秒超时，`000` |
| 本机 Clash `127.0.0.1:7897` | **200，3.1 秒** |

**修改**：仓库级写入 `http.proxy=http://127.0.0.1:7897`、`http.version=HTTP/1.1`（HTTP/2 经代理时极易触发 RPC failed）、`http.postBuffer=524288000`、`http.lowSpeedLimit/lowSpeedTime` 让卡死时能快速失败重试。改完后 6 个分卷（233MB）全部一次成功，用时 81 秒。

`tools/pack-ios.ps1` 也加了自动探测：依次试 7897/7890/10809/10808/1080/2080/8888，用 `curl -x` 探一次 `https://github.com` 返回 200 才采用，可用 `-SkipProxyDetect` 跳过。

### 8. 触发方式：从“手动点按钮”改成“推标记文件”

`workflow_dispatch` 只能人工在网页点，本机没有 `gh` CLI 也没有 PAT。改成同时监听 `push: paths: ['.ci/trigger-ipa-build']`，分卷全部推完后再推这个标记文件即可自动开跑；标记文件在分卷之前不会存在，因此也不会出现“分卷没传齐就自动跑出一串红叉”。

### 9. 首次云端编译结果（成功）

run #1 `Build Unsigned iOS IPA`，10 个步骤全绿，用时约 6 分钟：

| 检查项 | 结果 |
|---|---|
| ipa 体积 | 34.0 MB |
| 签名状态 | 未签名（无 `_CodeSignature`、无 embedded 描述文件），符合预期 |
| 架构 | arm64（`UnityFramework` 86MB） |
| 包名 | `com.unity.template.armobile`（Unity 模板默认值，装机时改掉） |
| 最低系统 | iOS 12.0 |
| 设备要求 | `arm64 / metal / arkit` |
| 相机文案 | 需要使用相机进行园区AR巡检平面检测 |
| ATS | `NSAllowsArbitraryLoads = true`，手机可直连 `http://` 后端 |

顺带把脚本里的 `serverUrl` 默认值从占位的 `192.168.1.100` 改成实际局域网地址 `192.168.10.11`，以后重新打包不用再手动改。
