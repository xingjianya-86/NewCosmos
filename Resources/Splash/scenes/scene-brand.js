/* ============================================================================
 * 帝皇权杖δ-me13 · 场景 1/5：第一幕「黑幕品牌」
 * ----------------------------------------------------------------------------
 * 黑幕 → 吉祥物与三行品牌浮现（字距收紧）→ 扫光掠过。
 * 仅启动动画（splash.js）播放；序列登录直接从档案底板开始，不进本幕。
 * 时序与原 splash.js run() 前段逐拍一致。
 * ========================================================================== */
(function () {
  'use strict'

  if (window.__NC_ANIM__ && window.__NC_ANIM__.__s_brand__) return
  var A = window.__NC_ANIM__ = window.__NC_ANIM__ || {}
  A.__s_brand__ = true
  A.scenes = A.scenes || {}

  A.scenes.brand = {
    /** @param {{ui:object, tl:object}} rt */
    play: function (rt) {
      var ui = rt.ui
      var tl = rt.tl
      return Promise.resolve()
        .then(function () { return tl.wait(820) })
        .then(function () {
          ui.blackInner.classList.add('dsu-in')
          A.audio.rise()
          return tl.wait(240)
        })
        .then(function () {
          ui.blackEn.classList.add('dsu-tighten')
          return tl.wait(880)
        })
        .then(function () {
          ui.sweep.classList.add('dsu-go')
          return tl.wait(760)
        })
    }
  }
})()
