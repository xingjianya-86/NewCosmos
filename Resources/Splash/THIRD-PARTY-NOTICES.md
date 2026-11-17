# 第三方素材与许可声明（启动动画）

本目录是 [dsh-startup-screen](https://github.com/6shenhonghong9/dsh-startup-screen)
（MIT License，© 沈宏宏）的**二次开发版本**，供「帝皇权杖δ-me13 社会救助管理系统」启动画面使用。
原始许可证全文见同目录 `UPSTREAM-LICENSE.txt`。

## 本仓库相对上游的改动

| 文件 | 改动 |
|---|---|
| `splash.js` | **2026-11-15 拆分为编排器**（逻辑移入 `core\*.js` 与 `scenes\*.js`，逐字迁移，播放时序与对外 API 不变）。原改动保留：① 移除上游 DeepSeek 鲸鱼 SVG 路径标识，改为本系统吉祥物 `img/mascot.png`；② 品牌块 / 终幕标识 / 页脚 / 元信息行改为 `window.__DSH_STARTUP__` 可配置；③ 音频素材加载路径由 Host 路由 `/dsh-startup/asset/` 改为同目录相对路径，**现优先读嵌入包 `core\anim-audio-assets.js`**；④ 五步序列的中文主字与日志改为本系统语境，**英文副标题未改**（与真声素材内容对齐）；⑤ 默认配置改为本系统文案，配置缺失时也不会露出第三方品牌。 |
| `core\anim-config.js` 等 7 个 | **新增（2026-11-15 拆分）**：核心引擎——配置/DOM/音效/台词/标题栏同步/时间线/统一收尾；逻辑自 `splash.js` 逐字迁移。 |
| `scenes\scene-*.js`（5 个） | **新增（2026-11-15 拆分）**：五幕场景（黑幕 / 洗白 / 档案底板 / **五步验证序列（含 STEPS 台词脚本）** / 终幕），启动动画与序列登录共用。 |
| `core\anim-audio-assets.js` | **生成文件（2026-11-15）**：`Scripts\prepare_splash_audio.ps1` 将 `audio\*.mp3` 转 base64 data URI 内嵌（mp3 零请求）。**素材字节未改，本文件与 `audio\` 内素材适用同一权利声明**；删除音频时同步删除本文件即可。 |
| `login.html` / `login.js` / `login.css` | **新增（2026-11-15 序列登录）**：整页 WebView 登录——复用 core/scenes，五步验证序列即登录流程，第 2 步嵌账号密码表单，认证结果由 MAUI 宿主桥回执驱动；失败回表单重试，故障回落后经典登录页。 |
| `splash.css` | 原样（仅注释保留）。 |
| `app.css` | 新增：铺满窗口、黑底兜底、揭幕底板与登录页同源。 |
| `bridge.js` | 新增：动画结束 / 脚本异常 → `newcosmos-splash://` 自定义协议通知 MAUI 宿主；**2026-11-15 扩展 `__NC_BRIDGE__` 双向通道**（web→host 结构化 `send`、host→web `dispatch/on`，序列登录用）。 |
| `splash.html` | 新增：挂载点与脚本顺序（结构沿用上游 `preview/index.html`，去掉设置面板与假界面；2026-11-15 起按 core/scenes 顺序引入）。 |
| `audio/*.mp3` | 原样（台词 6 段 + 音效 2 段）；2026-11-15 起运行时默认走嵌入包，本目录为生成来源。 |

## 权利与风险提示（来自上游 README，务必知悉）

1. 上游的 MIT 许可**仅覆盖作者原创的代码与文案**；
2. 上游 README 明确说明：视觉风格参考自动画作品，**音频素材（人声与音效）取自第三方视频，
   相关权利归原权利人所有，未获得任何授权或背书**；
3. 上游 README 声明**未授权任何商业用途**，二次创作与商业使用的风险由使用者自行承担。

本系统的处理方式：

- 代码与文案：可依 MIT 使用，保留上游署名（见上表与 `UPSTREAM-LICENSE.txt`）；
- 品牌：已全量替换为本系统文案，不再出现 DeepSeek / Harness 品牌字样与标识；
- 音频：按使用方决定打包原 mp3，**可通过 `config/app.ini` 的 `SplashSound=0` 一键静音**，
  以便在需要时以纯视觉方式分发；
- 若权利方提出异议，删除 `Resources/Splash/audio/` 并把 `SplashSound=0` 即可完全去除音频，
  动画其余部分不受影响（脚本在素材缺失时会自动退回 WebAudio 合成音效）。
