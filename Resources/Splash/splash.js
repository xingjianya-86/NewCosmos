/* ============================================================================
 * dsh-startup-screen — 启动动画编排器（NewCosmos 拆分版）
 * ----------------------------------------------------------------------------
 * 上游：https://github.com/6shenhonghong9/dsh-startup-screen（MIT © 沈宏宏）
 * 2026-11-15 起本文件只做「装配 + 时序编排」，实际逻辑分层在：
 *   core/anim-config.js    配置与共享状态（A.C / A.state）
 *   core/anim-dom.js       DOM 模板与工具（A.buildDom / A.utils）
 *   core/anim-audio.js     音效引擎（A.audio）
 *   core/anim-voice.js     台词引擎（A.voice / A.wireSound）
 *   core/anim-chrome.js    桌面端标题栏同步（A.chromeSync）
 *   core/anim-timeline.js  时间线引擎（A.timeline）
 *   core/anim-runtime.js   统一收尾（A.finish）
 *   scenes/scene-*.js      五幕场景（brand / wash / plate / verify / finale）
 * 改动清单见 THIRD-PARTY-NOTICES.md。
 *
 * 片头时序（两分支共用）与分流（2026-11-18 起，去重复播）：
 *   第一幕  黑幕 + 系统标识／"民政社会救助"／"NEWCOSMOS"（字距收紧 + 扫光）
 *   过场    过曝洗白（黑 → 灰 → 暖白底板）
 *   第二幕  暖白"技术档案"底板：品牌块、页脚、进度条、六格阶段柱
 *   分流    askIntent 问宿主（splash-intent）：
 *           full（会话有效）→ 序列 系统访问许可 → 操作员身份确认 → 接收启动请求
 *                 → 读取操作权限 → 权限核验通过 → 终幕 WELCOME（色散故障后揭幕）→ 进主页
 *           skip（需要登录/重试页）→ 片头收尾（同 SKIP），交序列登录在同一底板接手
 *                 （授权/账号密码停下等待），五步序列只演一遍
 *
 * 配置来自 window.__DSH_STARTUP__（config.js 由 C# 每次启动生成）。
 * 对外契约不变：window.__DSH_STARTUP_API__ / dsh-startup:done /
 *               newcosmos-splash:// 桥协议（bridge.js）。
 * ========================================================================== */
(function () {
  'use strict'

  if (window.__DSH_STARTUP_LOADED__) return
  window.__DSH_STARTUP_LOADED__ = true

  var A = window.__NC_ANIM__
  var C = A.C
  var ROOT_ID = A.ROOT_ID
  var bridge = window.__NC_BRIDGE__

  /* Host 侧已判定"这次不播"（未启用 / 本会话已播 / 今日已播）：
     这里不再建 DOM，但仍然暴露 API，方便按需重播。 */
  if (window.__DSH_STARTUP_SKIP__ === true) {
    window.__DSH_STARTUP_API__ = {
      replay: injectSelf,
      skip: function () {},
      finish: function () {},
      config: C
    }
    return
  }

  /* 最后一道保险：无论前面哪一步抛异常，都不许把用户留在黑幕里 */
  setTimeout(function () {
    try {
      var r = document.getElementById(ROOT_ID)
      if (r && r.classList && r.classList.contains('dsu-boot')) {
        if (r.parentNode) r.parentNode.removeChild(r)
        document.documentElement.classList.remove('dsu-lock')
        if (document.body) document.body.style.overflow = ''
        window.__DSH_STARTUP_FAILED__ = true
      }
    } catch (e) { /* 兜底本身不许再抛 */ }
  }, 9000)

  /* ── 装配：root → DOM → 时间线 → 声音接线 ─────────────────────────────── */
  var STEPS = A.scenes.verify.STEPS
  A.state = A.newState()
  var state = A.state

  var root = A.prepareRoot(C)
  var ui = A.buildDom(root, C, STEPS)
  var tl = A.timeline.create(ui, C, STEPS)
  A.wireSound(ui, STEPS)

  var rt = A.attach({ ui: ui, tl: tl, C: C, state: state, mode: 'splash', hooks: null })

  /* ── 重播（settings 预览 / 冒烟把手）：拆分后只重挂编排器，
       core 与 scenes 是无状态工厂，保留缓存的音频素材与已选音色 ────────── */
  function injectSelf() {
    window.__DSH_STARTUP_LOADED__ = false
    var old = document.getElementById(ROOT_ID)
    if (old && old.parentNode) old.parentNode.removeChild(old)
    window.__DSH_STARTUP_SKIP__ = false
    /* 旧时间线整体作废：清定时器、放行等待者、判死，防止旧链路继续操作游离 DOM */
    state.finished = true
    A.timeline.releaseState(state)
    var s = document.createElement('script')
    if (window.__DSH_STARTUP_SCRIPT_URL__) {
      s.src = window.__DSH_STARTUP_SCRIPT_URL__
    } else {
      s.src = 'splash.js?t=' + Date.now()
    }
    document.head.appendChild(s)
  }

  /* ── 时间线 ───────────────────────────────────────────────────────────── */
  function run() {
    /* 第一幕就是纯黑：先把桌面端标题栏整层透明化，按钮与色块一并消失 */
    A.chromeSync.hide()
    return Promise.resolve()
      .then(function () { return A.scenes.brand.play(rt) })
      .then(function () { return A.scenes.wash.play(rt) })
      .then(function () { return A.scenes.plate.play(rt) })
      .then(function () {
        /* 分流点：底板就位后问宿主这次启动往哪走（会话恢复结果）——
           full = 会话有效，接着自动演完五步序列与终幕（全片）；
           skip = 需要登录/进连接重试页，片头就此收尾（与 SKIP 按钮同一收尾路径），
                  由序列登录在同一块底板上接手：授权/账号密码步骤停下等待输入，
                  五步序列只演一遍，不再"启动动画 + 序列登录"重复两趟。 */
        return askIntent().then(function (mode) {
          if (mode === 'skip') { requestSkip(); return null }
          return A.scenes.verify.stepLoop(rt)
            .then(function () { return A.scenes.finale.play(rt) })
        })
      })
      .catch(function (err) {
        try { console.warn('[dsh-startup-screen] 动画异常，直接揭幕：', err) } catch (e2) {}
        return A.finish()
      })
  }

  /* ── 启动分流问路：一次，先到先得（宿主 SplashPage 双路桥各到一次，dispatch 侧 settle 幂等）──
     宿主按会话恢复结果答 full/skip；无桥 / 15 秒未答复 → 兜底 full（与既往"每次启动播全片"
     行为一致）；宿主另有 SplashTimeoutSeconds 闸门看门狗，任何一侧都不会把用户卡住。 */
  function askIntent() {
    return new Promise(function (resolve) {
      var settled = false
      var timer = setTimeout(function () { settle('full') }, 15000)

      function settle(mode) {
        if (settled) return
        settled = true
        clearTimeout(timer)
        resolve(mode)
      }

      if (!bridge) { settle('full'); return }
      bridge.on('splash-intent-result', function (data) {
        settle(data === 'skip' ? 'skip' : 'full')
      })
      bridge.send('splash-intent', {})
    })
  }

  function requestSkip() {
    if (state.skipped || state.finished) return
    state.skipped = true
    tl.releaseAll()
    A.finish()
  }

  if (C.allowSkip && ui.skip) {
    ui.skip.hidden = false
    ui.skip.addEventListener('click', function (e) { e.stopPropagation(); requestSkip() })
    document.addEventListener('keydown', function (e) {
      if (e.key === 'Escape') requestSkip()
    })
  } else if (ui.skip) {
    ui.skip.hidden = true
  }

  /* 对外 API：设置页试听 / 冒烟测试用（宿主桥 bridge.js 也读 audioDiag） */
  window.__DSH_STARTUP_API__ = {
    /* 设置页试听与冒烟测试用：直接念一行 */
    speak: function (en, zh) { return A.voice.say(en, zh) },
    speakName: function (n) { return A.voice.speakName(n) },
    get voice() { return A.voice.current() },
    audioDiag: function () { return A.audio.diag() },
    replay: injectSelf,
    skip: requestSkip,
    finish: function () { return A.finish() },
    config: C
  }

  /* 锁滚动，动画期间不露出下面的应用 */
  document.documentElement.classList.add('dsu-lock')
  document.documentElement.style.overflow = 'hidden'
  if (document.body) document.body.style.overflow = 'hidden'
  tl.setProgress(0.05)
  root.classList.remove('dsu-boot')

  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', function () { run() })
  } else {
    run()
  }
})()
