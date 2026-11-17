/* ============================================================================
 * 帝皇权杖δ-me13 · 场景 2/5：过场「过曝洗白」
 * ----------------------------------------------------------------------------
 * 黑幕内容退场 → 灰白闪一下（wash）→ 暖白档案底板接管。
 * 仅启动动画播放；序列登录不进本幕（底板常显）。
 * 时序与原 splash.js run() 中段逐拍一致。
 * ========================================================================== */
(function () {
  'use strict'

  if (window.__NC_ANIM__ && window.__NC_ANIM__.__s_wash__) return
  var A = window.__NC_ANIM__ = window.__NC_ANIM__ || {}
  A.__s_wash__ = true
  A.scenes = A.scenes || {}

  A.scenes.wash = {
    /** @param {{ui:object, tl:object}} rt */
    play: function (rt) {
      var ui = rt.ui
      var tl = rt.tl
      return Promise.resolve()
        .then(function () {
          ui.blackInner.classList.add('dsu-out')
          A.audio.sweep()
          ui.wash.style.transition = 'opacity ' + tl.ms(220) + 'ms linear'
          ui.wash.style.opacity = '1'
          return tl.wait(260)
        })
    }
  }
})()
