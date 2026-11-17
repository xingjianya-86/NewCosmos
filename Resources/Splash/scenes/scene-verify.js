/* ============================================================================
 * 帝皇权杖δ-me13 · 场景 4/5：核心「五步验证序列」
 * ----------------------------------------------------------------------------
 * 拆分自原 splash.js 的 STEPS / stepLoop / showAction / hideAction。
 * 这是整个动画的心脏，也是序列登录（login.html）复用的主体：
 *
 *   系统访问许可 → 操作员身份确认 → 接收启动请求 → 读取操作权限
 *   → 权限核验通过 → （终幕）欢迎访问
 *
 * stepLoop(rt, from) 支持：
 *   from      —— 从第几步开始（序列登录认证失败后从"身份确认"重入）
 *   rt.hooks  —— 可选覆盖（序列登录注入表单交互与认证关卡）：
 *       showAction(step) / waitAction(step) / hideAction(step)
 *       afterStep(index, step) → Promise<'continue'|'abort'>
 *         abort 时以 ABORT 为哨兵拒绝，由调用方捕获转错误态。
 * 启动动画不传 hooks，行为与拆分前逐拍一致。
 * ========================================================================== */
(function () {
  'use strict'

  if (window.__NC_ANIM__ && window.__NC_ANIM__.__s_verify__) return
  var A = window.__NC_ANIM__ = window.__NC_ANIM__ || {}
  A.__s_verify__ = true
  A.scenes = A.scenes || {}

  /* ── 序列脚本：中文主字 + 英文等宽字幕 + 底部日志 ───────────────────────
     文字与真声素材一一对应（来自原始音频）：
       line-1.mp3 = V1 需要访问许可      line-2.mp3 = V2 身份资料确认
       line-3.mp3 = V4 接收访问需求      line-4.mp3 = V5 开始读取权限
       line-5.mp3 = V6 读取完毕 · 核验通过
     V3 是用户名字独白，按原作者要求不用。
     `say` 是素材拿不到时的 TTS 兜底文本；`audio` 是素材文件名（不含扩展名）。 */
  var STEPS = [
    {
      zh: '系统访问许可',
      en: 'ACCESS PERMISSION REQUIRED',
      say: 'Access permission required.',
      audio: 'line-1',
      log: '等待操作员授权…',
      logEn: 'AWAITING OPERATOR CONSENT',
      badge: 'AUTHORIZATION',
      action: 'grant',
      btnZh: '授予访问权限',
      btnEn: 'GRANT ACCESS',
      hold: 900,
      progress: 0.34
    },
    {
      zh: '操作员身份确认',
      en: 'IDENTITY VERIFICATION',
      say: 'Identity verification.',
      audio: 'line-2',
      log: '核对操作员身份资料…',
      logEn: 'MATCHING OPERATOR RECORD',
      badge: 'IDENTITY',
      action: 'confirm',
      btnZh: '确认身份',
      btnEn: 'CONFIRM IDENTITY',
      hold: 700,
      progress: 0.5
    },
    {
      zh: '接收启动请求',
      en: 'ACCESS REQUEST RECEIVED',
      say: 'Access request received.',
      audio: 'line-3',
      log: '接收启动请求，建立数据通道…',
      logEn: 'HANDSHAKE ESTABLISHED',
      badge: 'REQUEST',
      action: 'auto',
      hold: 1200,
      progress: 0.66,
      assembleLogo: true
    },
    {
      zh: '正在读取操作权限',
      en: 'PERMISSION READ STARTED',
      say: 'Beginning permission read.',
      audio: 'line-4',
      log: '读取操作权限，索引工作区与档案…',
      logEn: 'READING PERMISSION SET',
      badge: 'READ',
      action: 'auto',
      hold: 1200,
      progress: 0.8,
      hex: true
    },
    {
      zh: '权限核验通过',
      en: 'READ COMPLETE · VERIFIED',
      say: 'Read complete. Verification passed.',
      audio: 'line-5',
      log: '权限读取完成，核验通过，欢迎使用',
      logEn: 'PERMISSION AUTHORIZED',
      badge: 'VERIFY',
      action: 'auto',
      hold: 1600,
      progress: 0.93,
      reticle: true
    }
  ]

  /** 认证失败等场景的中止哨兵：stepLoop 以它 reject，调用方按身份捕获 */
  var ABORT = {}

  /* ── 身份卡展示（第二步默认内容；序列登录以 hooks.showAction 换成表单）── */
  function showAction(step) {
    var ui = A.rt.ui
    var C = A.C
    ui.btnZh.textContent = step.btnZh
    ui.btnEn.textContent = step.btnEn
    ui.card.hidden = step.action !== 'confirm'
    if (step.action === 'confirm') {
      ui.card.innerHTML = ''
      /* 纯中文标签 + 数字/名称，去掉英文双写与主机行，别堆信息 */
      var rows = [
        ['身份名称', C.identity, true],
        ['身份编号', C.identityId, false],
        ['权限等级', C.accessLevel, false]
      ]
      rows.forEach(function (r) {
        var row = A.utils.el('div', 'dsu-card-row')
        row.appendChild(A.utils.el('span', null, r[0]))
        row.appendChild(A.utils.el('b', r[2] ? 'dsu-zhname' : null, r[1]))
        ui.card.appendChild(row)
      })
    }
    ui.hint.innerHTML = '按 <kbd>Enter</kbd> / <kbd>Space</kbd>，或点击按钮继续'
    ui.action.classList.add('dsu-in')
    ui.btn.classList.add('dsu-pulse')
  }

  function hideAction() {
    var ui = A.rt.ui
    ui.action.classList.remove('dsu-in')
    ui.btn.classList.remove('dsu-pulse')
  }

  A.scenes.verify = {
    STEPS: STEPS,
    ABORT: ABORT,
    showAction: showAction,
    hideAction: hideAction,

    /**
     * 五步序列主循环。
     * @param {object} rt 入口上下文 { ui, tl, C, state, hooks? }
     * @param {number} [from] 从第几步（0 起）开始；缺省 0 = 全序
     * @returns {Promise<void>} 全部走完 resolve；hooks.afterStep 返回
     *          'abort' 时以 ABORT reject；其余异常原样上抛
     */
    stepLoop: function (rt, from) {
      var ui = rt.ui
      var tl = rt.tl
      var hooks = rt.hooks || {}
      var state = A.state
      var audio = A.audio
      var voice = A.voice
      var start = from || 0

      var chain = Promise.resolve()
      STEPS.forEach(function (step, i) {
        if (i < start) return
        chain = chain.then(function () {
          if (state.finished) return
          var n = i + 1
          /* 身份读数讲的是「身份已确认」，只属于前两幕。第 3 幕起是「接收访问需求」，
             这句信息已经过期，跟着身份核验一起退场，不再一路挂到终幕。 */
          if (i >= 2) ui.idline.classList.remove('dsu-in')
          tl.setStepCount(n, step.badge)
          tl.setSlats(n)
          tl.setProgress(step.progress)
          tl.setSubtitle(step.log, step.logEn)
          audio.step()
          /* 语音在这里起头，但**不阻塞**下面的文字渐显 —— 两者同时发生 */
          var spoke = voice.line(step.say, step.zh, step.audio)

          ui.status.classList.toggle('dsu-zhline', true)
          /* 先摘掉 dsu-in 并强制一次样式计算，新的逐字 span 才会重新走渐显动画 */
          ui.status.classList.remove('dsu-in')
          ui.subEn.classList.remove('dsu-in')
          void ui.status.offsetWidth
          A.utils.typeIn(ui.statusZh, step.zh, 46)
          A.utils.typeIn(ui.subEn, step.en, 13)
          ui.caret.classList.toggle('dsu-on', !!step.action && step.action !== 'auto')
          requestAnimationFrame(function () {
            if (state.finished) return
            ui.status.classList.add('dsu-in')
            ui.subEn.classList.add('dsu-in')
          })
          ui.rule.classList.add('dsu-in')

          if (step.assembleLogo) tl.assembleLogo()
          if (step.reticle) {
            /* 照原片：准星出现时中央只留状态字，标识和虚线收掉，别挤成一团 */
            ui.logo.classList.remove('dsu-in')
            ui.dots.hidden = true
            ui.reticle.classList.add('dsu-in')
            tl.sweep()
          }
          if (step.hex) tl.startHex()

          /* 这一步停多久 = max(视觉基准, 实际播报时长 + 收尾留白) —— 台词长就多停，
             台词短也不会干等。用户点得快时同样等这一句念完，不掐断。 */
          function holdFor(spoken) {
            var target = Math.max(tl.ms(step.hold), (spoken || 0) + tl.ms(240))
            return tl.wait(Math.max(0, target - (spoken || 0)))
          }

          var p
          if (step.action === 'grant' || step.action === 'confirm') {
            var show = hooks.showAction || showAction
            var hide = hooks.hideAction || hideAction
            var waitAct = hooks.waitAction || function () { return tl.waitAction() }
            show(step)
            p = tl.wait(520).then(function () { return waitAct(step) }).then(function () {
              hide(step)
              audio.confirm()
              ui.caret.classList.remove('dsu-on')
              return spoke
            }).then(function () { return tl.wait(tl.ms(320)) })
          } else if (step.hex) {
            p = spoke.then(holdFor).then(function () { tl.stopHex() })
          } else {
            p = spoke.then(holdFor)
          }

          if (!hooks.afterStep) return p
          /* 认证关卡（仅序列登录）：每步收尾时让宿主逻辑裁决继续 / 中止 */
          return p.then(function () { return hooks.afterStep(i, step) }).then(function (verdict) {
            if (verdict === 'abort') return Promise.reject(ABORT)
          })
        })
      })
      return chain
    }
  }
})()
