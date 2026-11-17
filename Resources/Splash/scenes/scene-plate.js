/* ============================================================================
 * 帝皇权杖δ-me13 · 场景 3/5：第二幕「暖白档案底板」
 * ----------------------------------------------------------------------------
 * 底板/水印淡入（洗白收尾）→ 品牌块、页脚、进度条、阶段柱、机框、
 * 时钟读数、SKIP 依次点亮 → 扫描线掠一遍。
 *
 * 两个入口共用：
 *   启动动画：play(rt)              —— 含淡入段（承接洗白）；
 *   序列登录：play(rt, { skipFadeIn: true }) —— 底板常显，直接进品牌点亮。
 * 时序与原 splash.js run() 第二幕逐拍一致。
 * ========================================================================== */
(function () {
  'use strict'

  if (window.__NC_ANIM__ && window.__NC_ANIM__.__s_plate__) return
  var A = window.__NC_ANIM__ = window.__NC_ANIM__ || {}
  A.__s_plate__ = true
  A.scenes = A.scenes || {}

  A.scenes.plate = {
    /**
     * @param {{ui:object, tl:object, C:object}} rt
     * @param {{skipFadeIn?: boolean}} [opts] skipFadeIn=跳过底板淡入段
     *        （序列登录：黑幕已隐藏、底板已直接可见，淡入没有来源色）
     */
    play: function (rt, opts) {
      var ui = rt.ui
      var tl = rt.tl
      var C = rt.C
      var skipFadeIn = !!(opts && opts.skipFadeIn)

      var chain = Promise.resolve()

      if (!skipFadeIn) {
        /* 洗白收尾：底板与水印淡入，wash / 黑幕淡出 */
        chain = chain.then(function () {
          ui.plate.style.transition = 'opacity ' + tl.ms(900) + 'ms ease'
          ui.plate.style.opacity = '1'
          ui.watermark.style.transition = 'opacity ' + tl.ms(1100) + 'ms ease'
          ui.watermark.style.opacity = '1'
          ui.wash.style.transition = 'opacity ' + tl.ms(720) + 'ms cubic-bezier(.2,.9,.2,1)'
          ui.wash.style.opacity = '0'
          ui.black.style.transition = 'opacity ' + tl.ms(720) + 'ms ease'
          ui.black.style.opacity = '0'
          return tl.wait(760)
        })
      }

      /* 第二幕：品牌块 / 页脚 / 进度条 / 机框 / 读数 / SKIP */
      chain = chain.then(function () {
        ui.brand.classList.add('dsu-in')
        ui.foot.classList.add('dsu-in')
        ui.stepcount.classList.add('dsu-in')
        ui.idline.classList.add('dsu-in')
        ui.frame.classList.add('dsu-in')
        ui.slats.classList.add('dsu-in')
        ui.meta.classList.add('dsu-in')
        ui.vignette.classList.add('dsu-in')
        ui.voice.classList.add('dsu-in')
        tl.setProgress(0.12)
        tl.setStepCount(0, 'BOOT')
        tl.setSlats(0)
        tl.setSubtitle('系统自检中…', 'SYSTEM SELF-CHECK')
        if (C.allowSkip) ui.skip.classList.add('dsu-in')
        return tl.wait(700)
      })

      /* 收尾：黑幕层摘除 + 扫描线 */
      chain = chain.then(function () {
        ui.black.style.display = 'none'
        tl.sweep()
      })

      return chain
    }
  }
})()
