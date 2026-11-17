/* ============================================================================
 * 帝皇权杖δ-me13 · 动画运行时 core 1/7：配置与共享状态
 * ----------------------------------------------------------------------------
 * 拆分自原 splash.js（dsh-startup-screen 二次开发版，MIT © 沈宏宏），
 * 2026-11-15 起动画按「core 引擎 + scenes 五幕场景」分层，供启动动画与
 * 序列登录（login.html）共用。改动清单见 THIRD-PARTY-NOTICES.md。
 *
 * 加载顺序（splash.html / login.html 均同）：
 *   config.js → bridge.js → core/* → scenes/* → 入口编排器（splash.js / login.js）
 *
 * 本文件无依赖，最先加载；导出到 window.__NC_ANIM__（下称 A）。
 * ========================================================================== */
(function () {
  'use strict'

  if (window.__NC_ANIM__ && window.__NC_ANIM__.__m_config__) return
  var A = window.__NC_ANIM__ = window.__NC_ANIM__ || {}
  A.__m_config__ = true

  /* ── 挂载点与系统标识（原 splash.js 的 ROOT_ID / LOGO_SRC）────────────── */
  A.ROOT_ID = 'dsh-startup-root'
  A.LOGO_SRC = 'img/mascot.png'

  /* ── 默认配置（config.js 未加载时的兜底，已是本系统文案，绝不露出第三方品牌）── */
  A.DEFAULTS = {
    enabled: true,
    mode: 'session',            // always | session | daily
    theme: 'light',             // light | dark
    speed: 1,
    requireInteraction: false,  // 默认自动连播；true = 第 1、2 步等待用户确认
    allowSkip: true,
    sound: true,                // 界面音效
    soundVolume: 0.5,
    voice: true,                // 女声播报
    voiceLang: 'en',            // en | zh | both
    voiceName: '',              // '' = 自动挑女声（见 FEMALE_HINTS）
    voiceRate: 0.88,            // 电影旁白档：比朗读慢，字与字之间才有重量
    voicePitch: 0.85,           // 压低音调，深沉的 AI 女声
    voiceVolume: 0.9,
    voiceCue: true,             // 念台词前的一声轻提示
    sfx: 'audio',               // audio=真素材（用户音频里抠的音效）/ synth=现场合成
    voiceSource: 'asset',       // asset=优先用真声素材 / tts=全部 TTS 合成
    voiceLead: 240,             // 提示音之后留一小段空白再开口
    identity: '操作员',          // ← 上次登录用户名（C# 生成 config.js 时注入）
    identityId: '',             // 留空 = 由身份名称推导（字母数字，中文名退回 OPERATOR）
    accessLevel: '3',
    brand1: 'NEWCOSMOS',
    brand2: 'SOCIAL ASSISTANCE MANAGEMENT',
    brand3a: '帝皇权杖',
    brand3b: 'δ-me13',
    orgZh: '民政社会救助',
    orgEn: 'NEWCOSMOS',
    orgSub: 'SOCIAL ASSISTANCE SYSTEM',
    logoLabel: 'N E W C O S M O S',
    welcomeLabel: 'N E W C O S M O S · 社会救助',
    welcomeBox: '帝皇权杖δ-me13',
    footer: '© 2026 帝皇权杖δ-me13 社会救助管理系统',
    metaLine: 'REV 1.0 · DESKTOP',
    accent: '#1a73e8',
    glitchA: '#ff3b30',
    glitchB: '#2971b8'
  }

  /** 合并 window.__DSH_STARTUP__（config.js 每次启动由 C# 生成）与默认值 */
  A.cfg = function () {
    var raw = (window.__DSH_STARTUP__ && typeof window.__DSH_STARTUP__ === 'object')
      ? window.__DSH_STARTUP__
      : {}
    var out = {}
    for (var k in A.DEFAULTS) if (Object.prototype.hasOwnProperty.call(A.DEFAULTS, k)) out[k] = A.DEFAULTS[k]
    for (var k2 in raw) {
      if (Object.prototype.hasOwnProperty.call(raw, k2) && raw[k2] !== undefined && raw[k2] !== null) {
        out[k2] = raw[k2]
      }
    }
    out.speed = Math.min(3, Math.max(0.35, Number(out.speed) || 1))
    out.identity = String(out.identity || A.DEFAULTS.identity).slice(0, 48)
    /* 代号取身份名称里的字母数字；纯中文名字取不到时退回 OPERATOR */
    out.identityId = String(out.identityId || out.identity).toUpperCase().replace(/[^A-Z0-9]/g, '').slice(0, 12)
    if (!out.identityId) out.identityId = 'OPERATOR'
    return out
  }

  /* 本次页面运行的合并配置（config.js 先于本脚本加载，此处即可求值） */
  A.C = A.cfg()

  /** 数值夹取（原 splash.js 的 clamp01，audio / voice 共用） */
  A.clamp01 = function (v, lo, hi) {
    var n = Number(v)
    return isFinite(n) ? Math.min(hi, Math.max(lo, n)) : lo
  }

  /**
   * 每次运行的时间线状态（拆分前是 splash.js 闭包里的自由变量）。
   * 跨场景共享：时间线引擎写、序列/终幕/跳过读；重播时整体换新。
   */
  A.newState = function () {
    return {
      timers: [],
      waiters: [],
      skipped: false,
      finished: false,
      hexTimer: null
    }
  }

  /* 当前运行状态（入口编排器创建后写入；core/scene 模块一律经 A.state 访问） */
  A.state = A.newState()
})()
