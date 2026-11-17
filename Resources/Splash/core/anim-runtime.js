/* ============================================================================
 * 帝皇权杖δ-me13 · 动画运行时 core 7/7：运行时收尾（揭幕 / 结束）
 * ----------------------------------------------------------------------------
 * 拆分自原 splash.js 的 finish()：无论从哪条路退出（正常揭幕 / 跳过 /
 * 脚本异常兜底），都走同一个收尾——还原标题栏、释放全部等待与定时器、
 * 掐断语音、解锁滚动、摘掉挂载点，最后派发 dsh-startup:done（宿主桥
 * 据此放行）。入口编排器在建好 ui/timeline 后调用 A.attach(rt)。
 * ========================================================================== */
(function () {
  'use strict'

  if (window.__NC_ANIM__ && window.__NC_ANIM__.__m_runtime__) return
  var A = window.__NC_ANIM__ = window.__NC_ANIM__ || {}
  A.__m_runtime__ = true

  /** 登记当前运行的上下文（{ ui, tl, C, state, mode, hooks }），finish 据此收尾 */
  A.attach = function (rt) {
    A.rt = rt
    return rt
  }

  /**
   * 统一收尾。
   * @param {object} [opts]
   *   silent  — 不派发 dsh-startup:done（序列登录"切经典登录"用）
   *   after   — 摘除挂载点之后的回调（与派发互斥时机，接收 silent 场景）
   */
  A.finish = function (opts) {
    var rt = A.rt
    if (!rt) return
    var state = rt.state
    if (state.finished) return
    state.finished = true

    /* 揭幕即还原桌面端标题栏（root 被移除时 MutationObserver 还会再兜一次） */
    if (A.chromeSync) A.chromeSync.show()
    if (rt.tl) { rt.tl.releaseAll(); rt.tl.stopHex() }
    if (A.voice) A.voice.stop()
    if (A.stopClock) A.stopClock()

    try {
      if (rt.C.mode === 'session') window.sessionStorage.setItem('dsh-startup-screen:shown', '1')
      if (rt.C.mode === 'daily') {
        window.localStorage.setItem('dsh-startup-screen:day', new Date().toISOString().slice(0, 10))
      }
    } catch (err) { /* 隐私模式下不可写，忽略 */ }

    /* 解锁：Host 注入的关键样式用 html.dsu-lock 锁滚动，这里必须一并摘掉，
       否则揭幕后主界面会永远滚不动。 */
    document.documentElement.classList.remove('dsu-lock')
    document.documentElement.style.overflow = ''
    if (document.body) document.body.style.overflow = ''

    var root = document.getElementById(A.ROOT_ID)
    if (root) {
      root.classList.remove('dsu-boot')
      root.classList.add('dsu-gone')
      setTimeout(function () {
        if (root && root.parentNode) root.parentNode.removeChild(root)
        try {
          if (!(opts && opts.silent)) window.dispatchEvent(new CustomEvent('dsh-startup:done'))
          if (opts && opts.after) opts.after()
        } catch (err) { /* noop */ }
      }, Math.round(900 / rt.C.speed))
    } else {
      try {
        if (!(opts && opts.silent)) window.dispatchEvent(new CustomEvent('dsh-startup:done'))
        if (opts && opts.after) opts.after()
      } catch (err) { /* noop */ }
    }
  }
})()
