# 第三方素材与许可声明（启动动画）

本目录是 [dsh-startup-screen](https://github.com/6shenhonghong9/dsh-startup-screen)
（MIT License，© 沈宏宏）的**二次开发版本**，供「帝皇权杖δ-me13 社会救助管理系统」启动画面使用。
原始许可证全文见同目录 `UPSTREAM-LICENSE.txt`。

## 本仓库相对上游的改动

| 文件 | 改动 |
|---|---|
| `splash.js` | ① 移除上游 DeepSeek 鲸鱼 SVG 路径标识，改为本系统吉祥物 `img/mascot.png`；② 品牌块 / 终幕标识 / 页脚 / 元信息行改为 `window.__DSH_STARTUP__` 可配置；③ 音频素材加载路径由 Host 路由 `/dsh-startup/asset/` 改为同目录相对路径 `audio/`（离线环境用 https 虚拟主机同源加载）；④ 五步序列的中文主字与日志改为本系统语境，**英文副标题未改**（与真声素材内容对齐）；⑤ 默认配置改为本系统文案，配置缺失时也不会露出第三方品牌。 |
| `splash.css` | 原样（仅注释保留）。 |
| `app.css` | 新增：铺满窗口、黑底兜底、揭幕时露出登录页背景图。 |
| `bridge.js` | 新增：动画结束 / 脚本异常 → `newcosmos-splash://` 自定义协议通知 MAUI 宿主。 |
| `splash.html` | 新增：挂载点与脚本顺序（结构沿用上游 `preview/index.html`，去掉设置面板与假界面）。 |
| `audio/*.mp3` | 原样（台词 6 段 + 音效 2 段）。 |

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
