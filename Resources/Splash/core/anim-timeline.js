/* ============================================================================
 * 帝皇权杖δ-me13 · 动画运行时 core 6/7：时间线引擎
 * ----------------------------------------------------------------------------
 * 拆分自原 splash.js：速度换算（ms）、可释放等待（wait / waitAction）、
 * 进度条 / 六格阶段柱 / 底部字幕 / 阶段计数、扫描线、十六进制流、
 * 中央标识"一笔画成"。全部读写 A.state（入口编排器每次运行新建）。
 *
 * waitAction 的两处本地化增强（序列登录需要，对启动动画行为不变）：
 *   - 焦点在 INPUT/TEXTAREA 里的按键不触发继续（表单里打空格不再误推进）；
 *   - opts.strict = 只认主按钮点击（表单区域内点击/勾选不误推进）。
 * ========================================================================== */
(function () {
  'use strict'

  if (window.__NC_ANIM__ && window.__NC_ANIM__.__m_timeline__) return
  var A = window.__NC_ANIM__ = window.__NC_ANIM__ || {}
  A.__m_timeline__ = true

  A.timeline = {
    /** 为一次运行创建时间线（ui/C/steps 均来自当次入口） */
    create: function (ui, C, steps) {
      var state = A.state

      function ms(base) { return Math.round(base / C.speed) }

      function wait(base) {
        return new Promise(function (resolve) {
          if (state.skipped) return resolve()
          var done = false
          function fin() {
            if (done) return
            done = true
            var i = state.waiters.indexOf(fin)
            if (i >= 0) state.waiters.splice(i, 1)
            resolve()
          }
          var t = setTimeout(fin, Math.max(0, ms(base)))
          state.timers.push(t)
          state.waiters.push(fin)
        })
      }

      function waitAction(opts) {
        var strict = !!(opts && opts.strict)
        return new Promise(function (resolve) {
          if (state.skipped || !C.requireInteraction) return resolve()
          var done = false
          function fin() {
            if (done) return
            done = true
            cleanup()
            resolve()
          }
          function onKey(e) {
            /* 表单焦点内的按键不推进（Enter 由表单自己转成按钮点击） */
            if (e.target && (e.target.tagName === 'INPUT' || e.target.tagName === 'TEXTAREA')) return
            if (e.key === 'Enter' || e.key === ' ' || e.key === 'Spacebar') { e.preventDefault(); fin() }
          }
          function onClick(e) { e.preventDefault(); fin() }
          function cleanup() {
            ui.btn.removeEventListener('click', onClick)
            if (!strict) ui.action.removeEventListener('click', onClick)
            document.removeEventListener('keydown', onKey)
            var i = state.waiters.indexOf(fin)
            if (i >= 0) state.waiters.splice(i, 1)
          }
          ui.btn.addEventListener('click', onClick)
          if (!strict) ui.action.addEventListener('click', onClick)
          document.addEventListener('keydown', onKey)
          state.waiters.push(fin)
        })
      }

      function releaseAll() {
        for (var i = 0; i < state.timers.length; i++) clearTimeout(state.timers[i])
        state.timers.length = 0
        var list = state.waiters.slice()
        state.waiters.length = 0
        for (var j = 0; j < list.length; j++) { try { list[j]() } catch (err) { /* noop */ } }
      }

      function setProgress(p) {
        var pct = Math.max(0, Math.min(1, p))
        ui.bar.style.width = Math.round(pct * 100) + '%'
      }

      /** 六格阶段柱：走过的压暗、当前的拉满并点亮主题色 */
      function setSlats(active) {
        var items = ui.slats.children
        for (var i = 0; i < items.length; i++) {
          var idx = i + 1
          items[i].classList.toggle('dsu-on', idx === active)
          items[i].classList.toggle('dsu-past', idx < active)
        }
      }

      /** 扫描线自上而下扫一遍（底板洗白后一次，验准时再一次） */
      function sweep() {
        if (!ui.scanline) return
        ui.scanline.classList.remove('dsu-go')
        void ui.scanline.offsetWidth
        ui.scanline.classList.add('dsu-go')
      }

      function setSubtitle(zh, en) {
        ui.subZh.classList.remove('dsu-in')
        ui.subEn2.classList.remove('dsu-in')
        setTimeout(function () {
          ui.subZh.textContent = zh || ''
          ui.subEn2.textContent = en || ''
          ui.subZh.classList.add('dsu-in')
          if (en) ui.subEn2.classList.add('dsu-in')
        }, ms(140))
      }

      function setStepCount(i, code) {
        var totalSteps = steps.length + 1
        ui.stepcount.textContent = '[ ' + String(i).padStart(2, '0') + ' / ' +
          String(totalSteps).padStart(2, '0') + ' ] ' + code
      }

      function startHex() {
        var lines = 12
        function line() {
          var s = ''
          for (var i = 0; i < 26; i++) {
            s += '0123456789ABCDEF'.charAt(Math.floor(Math.random() * 16))
            if (i % 2 === 1) s += ' '
          }
          return s
        }
        function paint() {
          var out = []
          for (var i = 0; i < lines; i++) out.push(line())
          ui.hex.innerHTML = out.join('<br>')
        }
        paint()
        ui.hex.classList.add('dsu-in')
        A.audio.tickRun()
        state.hexTimer = setInterval(paint, 110)
      }

      function stopHex() {
        if (state.hexTimer) { clearInterval(state.hexTimer); state.hexTimer = null }
        ui.hex.classList.remove('dsu-in')
      }

      /** 中央标识"一笔画成"：先勾线，再落墨成面（Web Animations 不可用时直接落墨） */
      function assembleLogo() {
        ui.logo.classList.add('dsu-in')
        ui.dots.hidden = false
        var path = ui.logo.querySelector('path')
        if (path && typeof path.getTotalLength === 'function') {
          try {
            var len = path.getTotalLength()
            path.style.fill = 'none'
            path.style.stroke = 'currentColor'
            path.style.strokeWidth = '1.1'
            path.style.strokeDasharray = len
            path.style.strokeDashoffset = len
            path.animate(
              [{ strokeDashoffset: len }, { strokeDashoffset: 0 }],
              { duration: ms(1400), easing: 'cubic-bezier(.55,.06,.24,1)', fill: 'forwards' }
            )
            setTimeout(function () {
              path.style.transition = 'fill ' + ms(420) + 'ms ease, stroke-width ' + ms(420) + 'ms ease'
              path.style.fill = ''
              path.style.strokeWidth = '0'
            }, ms(1450))
          } catch (err) { /* Web Animations 不可用时直接落墨 */ }
        }
        return wait(300)
      }

      return {
        ms: ms,
        wait: wait,
        waitAction: waitAction,
        releaseAll: releaseAll,
        setProgress: setProgress,
        setSlats: setSlats,
        sweep: sweep,
        setSubtitle: setSubtitle,
        setStepCount: setStepCount,
        startHex: startHex,
        stopHex: stopHex,
        assembleLogo: assembleLogo
      }
    },

    /** 结束一份历史状态（重播时旧时间线整体作废：清定时器 + 放行等待者） */
    releaseState: function (state) {
      if (!state) return
      for (var i = 0; i < state.timers.length; i++) clearTimeout(state.timers[i])
      state.timers.length = 0
      var list = state.waiters.slice()
      state.waiters.length = 0
      for (var j = 0; j < list.length; j++) { try { list[j]() } catch (err) { /* noop */ } }
      if (state.hexTimer) { clearInterval(state.hexTimer); state.hexTimer = null }
    }
  }
})()
