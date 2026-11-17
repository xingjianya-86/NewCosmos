/* ============================================================================
 * 帝皇权杖δ-me13 · 场景 5/5：终幕「WELCOME + 色散故障揭幕」
 * ----------------------------------------------------------------------------
 * 拆分自原 splash.js 的 finale()：欢迎版式 → 分片色散故障 → 光晕炸开 →
 * 极短高光 → 等欢迎语播完 → 整体后撤失焦（dsu-handoff）→ 收尾派发 done。
 * 两个入口共用（序列登录认证成功后走同一条终幕）。
 * ========================================================================== */
(function () {
  'use strict'

  if (window.__NC_ANIM__ && window.__NC_ANIM__.__s_finale__) return
  var A = window.__NC_ANIM__ = window.__NC_ANIM__ || {}
  A.__s_finale__ = true
  A.scenes = A.scenes || {}

  A.scenes.finale = {
    /** @param {{ui:object, tl:object}} rt */
    play: function (rt) {
      var ui = rt.ui
      var tl = rt.tl
      var root = document.getElementById(A.ROOT_ID)
      if (A.state.finished) return Promise.resolve()

      tl.setStepCount(6, 'WELCOME')
      tl.setSlats(6)
      tl.setProgress(1)
      /* 底部日志和终幕里的「欢迎 xxx 访问」区分开，避免同一句出现两遍 */
      tl.setSubtitle('身份验证完成，会话已建立', 'IDENTITY VERIFIED · SESSION ESTABLISHED')
      A.audio.chime()
      /* 终幕欢迎语：用真声素材（line-6）。素材缺失 / voiceSource=tts / voice
         关闭时自动静音，不退回 TTS 念白 —— 这条素材是「欢迎访问」整句，
         和动态身份名称无关。 */
      var welcome = A.voice.line(null, null, 'line-6')

      ui.status.classList.remove('dsu-in')
      ui.subEn.classList.remove('dsu-in')
      ui.rule.classList.remove('dsu-in')
      ui.reticle.classList.remove('dsu-in')
      tl.stopHex()
      /* 中央区（状态行/分隔线/标识/虚线）整体退场：不清掉会和 WELCOME 版式叠字 */
      ui.center.classList.add('dsu-off')
      ui.dots.hidden = true

      return tl.wait(360)
        .then(function () {
          ui.welcome.classList.add('dsu-in')
          ui.idline.classList.remove('dsu-in')
          return tl.wait(420)
        })
        .then(function () {
          /* 结尾色散故障：分片位移 + 0.5px 模糊 + 混合模式，读起来像镜头色散 */
          var targets = [ui.w1, ui.w2, ui.w4]
          targets.forEach(function (t) {
            t.classList.add('dsu-glitch')
            t.setAttribute('data-text', t.textContent)
          })
          ui.w2.classList.add('dsu-glitch-box')   /* 黑底白字那块换加色混合 */
          void ui.welcome.offsetWidth
          targets.forEach(function (t) { t.classList.add('dsu-on') })
          return tl.wait(640)
        })
        /* 光晕从中心炸开 + 变形光条横扫 + 文字青色 halation */
        .then(function () {
          if (root) root.classList.add('dsu-peak')
          ui.bloom.classList.add('dsu-go')
          ui.streak.classList.add('dsu-go')
          return tl.wait(430)
        })
        /* 光晕最亮那一下补一记极短高光（是"眨一下"，不是长时间糊白） */
        .then(function () {
          ui.flash.style.transition = 'opacity ' + tl.ms(70) + 'ms linear'
          ui.flash.style.opacity = '0.72'
          return tl.wait(90)
        })
        .then(function () {
          ui.flash.style.transition = 'opacity ' + tl.ms(340) + 'ms cubic-bezier(0.4, 0, 0.2, 1)'
          ui.flash.style.opacity = '0'
          return tl.wait(320)
        })
        /* 欢迎语没播完就不揭幕（素材缺失时这个 Promise 立刻 resolve，不影响节奏） */
        .then(function () { return welcome })
        /* 揭幕：整个机位后撤 + 轻微失焦，比单纯淡出更有镜头感 */
        .then(function () {
          if (root) root.classList.add('dsu-handoff')
          return tl.wait(780)
        })
        .then(function () { return A.finish() })
    }
  }
})()
