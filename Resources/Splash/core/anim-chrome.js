/* ============================================================================
 * 帝皇权杖δ-me13 · 动画运行时 core 5/7：桌面端标题栏同步
 * ----------------------------------------------------------------------------
 * 拆分自原 splash.js 的 chromeSync（Windows Controls Overlay）：
 * 动画播放期间把标题栏 overlay 整层透明化（按钮与色块一起消失），
 * 揭幕 / 跳过 / 自举失败 / 硬上限到点一律还原；任何一步失败都静默降级。
 * 两个入口（启动动画、序列登录）都需要——暖白底板阶段深色按钮同样刺眼。
 * ========================================================================== */
(function () {
  'use strict'

  if (window.__NC_ANIM__ && window.__NC_ANIM__.__m_chrome__) return
  var A = window.__NC_ANIM__ = window.__NC_ANIM__ || {}
  A.__m_chrome__ = true

  var ROOT_ID = A.ROOT_ID

  A.chromeSync = (function () {
    var CLEAR = 'rgba(0, 0, 0, 0)'
    var VARS = ['--dsw-specific-sidebar-fill', '--dsw-alias-label-primary']
    var MAX_HOLD_MS = 60000          /* 硬上限：万一动画卡住，别把按钮永久藏住 */
    var rootEl = document.documentElement
    var state = { applied: false, probe: null, settledHits: 0 }
    var savedVars = {}
    var retryTimers = []
    var keepAlive = null
    var holdTimer = null

    function isDesktopWin() {
      try {
        var nav = (typeof window !== 'undefined' && window.navigator) ||
          (typeof navigator !== 'undefined' ? navigator : null)
        if (!nav || !/Win/.test(nav.userAgent || '')) return false
        /* 判据不只认协议字符串：桌面端还会在文档上打 data-windows-titlebar 标记，
           任一个命中即认为是 Windows 桌面端，避免协议改名后整段静默失效 */
        var loc = (typeof window !== 'undefined' && window.location) ||
          (typeof location !== 'undefined' ? location : null)
        if (loc && loc.protocol === 'dsh-app:') return true
        var de = document.documentElement
        return !!(de && de.hasAttribute && de.hasAttribute('data-windows-titlebar'))
      } catch (e) { return false }
    }

    /* preload 的探针是 span，内联 background-color 的声明值里带这个 token 名 */
    function findProbe() {
      try {
        if (!document.querySelector) return null
        var el = document.querySelector('span[style*="dsw-specific-sidebar-fill"]')
        if (el) return el
        /* 兜底：某些实现不把内联样式暴露给属性选择器 */
        if (!document.querySelectorAll) return null
        var spans = document.querySelectorAll('span')
        for (var i = 0; i < spans.length; i++) {
          var s = spans[i]
          if (!s.style || typeof s.style.getPropertyValue !== 'function') continue
          var bg = s.style.getPropertyValue('background-color')
          if (bg && bg.indexOf('--dsw-specific-sidebar-fill') !== -1) return s
        }
      } catch (e) { /* 兜底不再抛 */ }
      return null
    }

    /* preload 观察 head 的 childList 与 body 的 style；两条一起打，任一条生效即可 */
    function poke() {
      try {
        if (document.head) {
          var st = document.createElement('style')
          st.setAttribute('data-dsu-chrome-poke', '1')
          st.textContent = '/*dsu-poke*/'
          document.head.appendChild(st)
          if (st.parentNode) st.parentNode.removeChild(st)
        }
      } catch (e) { /* 兜底不再抛 */ }
      try {
        if (document.body && document.body.style) {
          document.body.style.setProperty('--dsu-chrome-poke', String(Date.now() % 1000000))
          document.body.style.removeProperty('--dsu-chrome-poke')
        }
      } catch (e) { /* 兜底不再抛 */ }
    }

    /** 探针是否已经真的透明了；拿不到计算样式就当作「未确认」，继续补打。 */
    function settled() {
      try {
        var p = state.probe
        if (!p || p.isConnected === false) return false
        if (typeof getComputedStyle !== 'function') return false
        return getComputedStyle(p).backgroundColor === CLEAR
      } catch (e) { return false }
    }

    function apply() {
      try {
        /* 探针可能被应用重挂（主题切换 / 热重载）：缓存节点一旦脱离文档就重找，
           否则会对死节点空打，标题栏在动画中途突然回到主题色 */
        var probe = state.probe
        if (!probe || probe.isConnected === false) probe = state.probe = findProbe()
        if (probe) {
          probe.style.setProperty('background-color', CLEAR)
          probe.style.setProperty('color', CLEAR)
        }
        if (rootEl) for (var j = 0; j < VARS.length; j++) rootEl.style.setProperty(VARS[j], CLEAR)
        poke()
      } catch (e) { /* 兜底不再抛 */ }
    }

    function stopTimers() {
      for (var r = 0; r < retryTimers.length; r++) clearTimeout(retryTimers[r])
      retryTimers = []
      if (keepAlive) { clearInterval(keepAlive); keepAlive = null }
      if (holdTimer) { clearTimeout(holdTimer); holdTimer = null }
    }

    function hide() {
      if (!isDesktopWin()) return
      try {
        if (!state.applied) {
          state.applied = true
          state.settledHits = 0
          for (var i = 0; i < VARS.length; i++) savedVars[VARS[i]] = rootEl.style.getPropertyValue(VARS[i])
          state.probe = findProbe()
          /* 万一动画卡住不揭幕，到点强制还原，绝不把三个按钮永久藏住 */
          holdTimer = setTimeout(show, MAX_HOLD_MS)
        }
        apply()
        /* defer 注入路径下 splash.js 可能早于 preload 装探针：补打几次 */
        for (var r = 0; r < retryTimers.length; r++) clearTimeout(retryTimers[r])
        retryTimers = []
        var delays = [140, 520, 1400]
        for (var d = 0; d < delays.length; d++) retryTimers.push(setTimeout(apply, delays[d]))
        /* 自愈：应用挂载过程中若它自己重发（主题就绪）也拉回透明；
           已经连续两次确认透明就收手，别一直空打 IPC */
        if (!keepAlive) {
          keepAlive = setInterval(function () {
            if (!state.applied) return
            if (settled()) {
              if (++state.settledHits >= 2) { clearInterval(keepAlive); keepAlive = null }
              return
            }
            state.settledHits = 0
            apply()
          }, 600)
        }
      } catch (e) { /* 兜底不再抛 */ }
    }

    function show() {
      if (!state.applied) return
      state.applied = false
      try {
        stopTimers()
        var probe = state.probe
        if (probe) {
          probe.style.removeProperty('background-color')
          probe.style.removeProperty('color')
        }
        if (rootEl) {
          for (var k = 0; k < VARS.length; k++) {
            if (savedVars[VARS[k]]) rootEl.style.setProperty(VARS[k], savedVars[VARS[k]])
            else rootEl.style.removeProperty(VARS[k])
          }
        }
        poke()
        state.probe = null
        state.settledHits = 0
      } catch (e) { /* 兜底不再抛 */ }
    }

    /* 双保险：无论从哪条路退出（揭幕 / 跳过 / 自举失败 / 硬上限到点），
       #dsh-startup-root 一被移除就还原标题栏 */
    var observerInstalled = false
    function installObserver() {
      if (observerInstalled) return
      observerInstalled = true
      try {
        if (typeof MutationObserver !== 'function' || !document.body) return
        var mo = new MutationObserver(function (muts) {
          for (var i = 0; i < muts.length; i++) {
            var nodes = muts[i].removedNodes
            for (var n = 0; nodes && n < nodes.length; n++) {
              if (nodes[n] && nodes[n].nodeType === 1 && nodes[n].id === ROOT_ID) { show(); return }
            }
          }
        })
        mo.observe(document.body, { childList: true })
      } catch (e) { /* 兜底不再抛 */ }
    }
    if (document.body) installObserver()
    else if (document.addEventListener) document.addEventListener('DOMContentLoaded', installObserver)

    return { hide: hide, show: show, isDesktopWin: isDesktopWin }
  })()

  /* 排查 / 冒烟测试用的把手：只读开关，不改变插件行为 */
  try { window.__DSH_STARTUP_CHROME__ = A.chromeSync } catch (e) { /* noop */ }
})()
