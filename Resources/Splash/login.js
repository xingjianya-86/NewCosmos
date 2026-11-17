/* ============================================================================
 * 帝皇权杖δ-me13 · 序列登录编排器（login.js）
 * ----------------------------------------------------------------------------
 * 整页 WebView 登录：启动动画的「五步授权序列」即登录流程。
 *
 *   底板（档案纸面常显）→ 步骤1 系统访问许可（点「授予访问权限」）
 *   → 步骤2 操作员身份确认（卡片位换真实账号密码表单，提交即发 auth:submit）
 *   → 步骤3 接收请求 / 步骤4 读取权限（宿主真实认证在途，本地库秒回也不跳戏）
 *   → 步骤5 核验通过 → 终幕 WELCOME + 色散故障揭幕 → dsh-startup:done
 *     → 宿主（LoginPage）进主页。
 *
 * 失败：认证结果 ok=false → 中止序列、状态行转红「身份核验未通过」，
 *       短驻留后从步骤2 重入，表单带服务端原因，可反复重试。
 * 回落：脚本异常 / 9 秒自举兜底 / 用户点「切换经典登录 ▸」/ Esc →
 *       上报 login-skip 或 login-fallback，宿主隐藏 WebView 即显经典登录页。
 * 认证成功后一切故障改为直进主页（authOk），不再回落。
 *
 * 与启动动画共用 core/ 与 scenes/ 同一份文件；本文件只做登录编排与桥接。
 * ========================================================================== */
(function () {
  'use strict'

  if (window.__NC_LOGIN_LOADED__) return
  window.__NC_LOGIN_LOADED__ = true

  var A = window.__NC_ANIM__
  var C = A.C
  var ROOT_ID = A.ROOT_ID
  var STEPS = A.scenes.verify.STEPS
  var bridge = window.__NC_BRIDGE__

  /* 登录语境：步骤 1/2 一律等人工确认（忽略 SplashRequireInteraction） */
  C.requireInteraction = true

  A.state = A.newState()
  var state = A.state

  var root = A.prepareRoot(C)
  var ui = A.buildDom(root, C, STEPS)
  var tl = A.timeline.create(ui, C, STEPS)
  A.wireSound(ui, STEPS)

  /* 不进第一二幕：黑幕隐藏、底板/水印直接可见（body 底色与本页 Plate 同源，无跳色） */
  ui.black.style.display = 'none'
  ui.plate.style.opacity = '1'
  ui.watermark.style.opacity = '1'

  /* 右上角：SKIP 语义换成本页的"回落出口" */
  ui.skip.textContent = '切换经典登录 ▸'

  /* ── 认证状态（宿主经 __NC_BRIDGE__.dispatch('auth-result') 回填）──────── */
  var auth = {
    id: null, pending: false, settled: false, ok: false, msg: '',
    promise: null, resolve: null
  }
  var authOk = false        // 认证已成功：此后 skip/故障一律直进主页，不再回落
  var seq = 0

  /* ── 表单元素（buildForm 时落位；重试不重建，保留已输入内容）──────────── */
  var formBuilt = false
  var userInput = null, passInput = null, rememberChk = null, rpassChk = null, errEl = null

  /* ── 预填（宿主在 login-ready 后推送记住的账号/密码）──────────────────── */
  var prefill = { received: false, user: '', pass: '', remember: true, rememberPass: false }

  var rt = A.attach({
    ui: ui, tl: tl, C: C, state: state, mode: 'login',
    hooks: {
      showAction: showLoginAction,
      waitAction: function () { return tl.waitAction({ strict: true }) },
      afterStep: afterLoginStep
    }
  })

  /* ── 第 2 步：身份卡片位 → 账号密码表单 ───────────────────────────────── */

  function showLoginAction(step) {
    ui.btnZh.textContent = step.btnZh
    ui.btnEn.textContent = step.btnEn
    if (step.action === 'confirm') {
      ui.card.hidden = false
      buildForm()
      refreshFormError()
      ui.hint.innerHTML = '输入账号与密码，按 <kbd>Enter</kbd> 或点击按钮授权'
      ui.status.classList.remove('dsu-err')   // 重入时摘掉上次的失败红字
    } else {
      ui.card.hidden = true
      ui.hint.innerHTML = '按 <kbd>Enter</kbd> / <kbd>Space</kbd>，或点击按钮继续'
    }
    ui.action.classList.add('dsu-in')
    ui.btn.classList.add('dsu-pulse')
  }

  function buildForm() {
    if (formBuilt) return
    formBuilt = true

    ui.card.innerHTML =
      '<div class="dsu-form">' +
        '<div class="dsu-form-row"><label for="nc-user">账号</label>' +
        '<input id="nc-user" type="text" autocomplete="username" spellcheck="false" placeholder="OPERATOR"></div>' +
        '<div class="dsu-form-row"><label for="nc-pass">密码</label>' +
        '<input id="nc-pass" type="password" autocomplete="current-password" placeholder="••••••"></div>' +
        '<div class="dsu-form-checks">' +
          '<label><input id="nc-remember" type="checkbox" checked> 记住账号</label>' +
          '<label><input id="nc-rpass" type="checkbox"> 记住密码</label>' +
        '</div>' +
        '<div class="dsu-form-err" hidden></div>' +
      '</div>'

    userInput = document.getElementById('nc-user')
    passInput = document.getElementById('nc-pass')
    rememberChk = document.getElementById('nc-remember')
    rpassChk = document.getElementById('nc-rpass')
    errEl = ui.card.querySelector('.dsu-form-err')

    /* 回车提交；输入法组合期（中文候选）不触发 */
    function onFieldKey(e) {
      if (e.key === 'Enter' && !e.isComposing) {
        e.preventDefault()
        ui.btn.click()
      }
    }
    userInput.addEventListener('keydown', onFieldKey)
    passInput.addEventListener('keydown', onFieldKey)
    ui.btn.addEventListener('click', onSubmit)

    applyPrefill()
    refreshFormError()
  }

  function applyPrefill() {
    if (!prefill.received || !formBuilt) return
    if (userInput && prefill.user) userInput.value = prefill.user
    if (passInput && prefill.pass) passInput.value = prefill.pass
    if (rememberChk) rememberChk.checked = prefill.remember !== false
    if (rpassChk) rpassChk.checked = !!prefill.rememberPass
  }

  function setFormError(text) {
    if (!errEl) return
    if (text) {
      errEl.textContent = text
      errEl.hidden = false
    } else {
      errEl.textContent = ''
      errEl.hidden = true
    }
  }

  function refreshFormError() {
    setFormError(auth.settled && !auth.ok && auth.msg ? auth.msg : '')
  }

  /** 表单提交：本地校验 → 置 pending → 桥发 auth:submit（凭据仅进程内传递） */
  function onSubmit(e) {
    if (auth.pending) { e.stopImmediatePropagation(); return }

    var user = userInput ? String(userInput.value || '').trim() : ''
    var pass = passInput ? String(passInput.value || '') : ''
    if (!user || !pass) {
      setFormError('请输入账号与密码')
      e.stopImmediatePropagation()   // 校验不过：不让 waitAction 推进步骤
      return
    }

    auth.pending = true
    auth.settled = false
    auth.ok = false
    auth.msg = ''
    auth.id = 'a' + (++seq)
    auth.promise = new Promise(function (resolve) { auth.resolve = resolve })

    ui.status.classList.remove('dsu-err')
    setFormError('')
    tl.setSubtitle(STEPS[1].log, STEPS[1].logEn)

    if (bridge) {
      bridge.send('login-submit', {
        id: auth.id,
        user: user,
        pass: pass,
        remember: !rememberChk || rememberChk.checked,
        rememberPass: !!(rpassChk && rpassChk.checked)
      })
    }
  }

  /* ── 认证关卡（stepLoop 的 afterStep 钩子）────────────────────────────── */

  function afterLoginStep(i) {
    // 步骤2/3 收尾：认证已失败立刻回表单，别把失败拖到"读权限"演完
    if (i === 1 || i === 2) {
      if (auth.settled && !auth.ok) return 'abort'
      return 'continue'
    }
    // 步骤4（读取权限）收尾：hex 转完就等认证结果，出来才决定去留
    if (i === 3) {
      if (!auth.promise) return 'continue'
      return auth.promise.then(function () { return auth.ok ? 'continue' : 'abort' })
    }
    return 'continue'
  }

  /* ── 失败重试环 ───────────────────────────────────────────────────────── */

  function playOnce(from) {
    return A.scenes.verify.stepLoop(rt, from).then(
      function () { return 'verified' },
      function (err) {
        if (err === A.scenes.verify.ABORT) return 'aborted'
        throw err
      }
    )
  }

  function playWithRetries(from) {
    return playOnce(from).then(function (r) {
      if (r !== 'aborted') return 'verified'

      /* 失败态：状态行转红重打「身份核验未通过」，字幕给服务端原因，短驻留后从步骤2 重入 */
      ui.status.classList.remove('dsu-in')
      void ui.status.offsetWidth
      A.utils.typeIn(ui.statusZh, '身份核验未通过', 40)
      A.utils.typeIn(ui.subEn, 'IDENTITY CHECK FAILED', 12)
      ui.status.classList.add('dsu-in', 'dsu-err')
      tl.setSubtitle('核验未通过 · ' + (auth.msg || '请检查账号与密码'), 'IDENTITY CHECK FAILED')

      return tl.wait(1100).then(function () {
        ui.idline.classList.add('dsu-in')   // 与首趟步骤2 一致（首趟在步骤3 已摘掉）
        return playWithRetries(1)
      })
    })
  }

  /* ── 宿主 → 页面 ─────────────────────────────────────────────────────── */

  if (bridge) {
    bridge.on('auth-result', function (data) {
      if (!data || !auth.id || data.id !== auth.id) return   // 过期结果（重试后旧回执）
      auth.settled = true
      auth.ok = !!data.ok
      auth.msg = String(data.msg || '').slice(0, 200)
      auth.pending = false
      if (auth.ok) authOk = true
      var resume = auth.resolve
      auth.resolve = null
      if (resume) resume()
    })

    bridge.on('prefill', function (data) {
      if (!data) return
      prefill.received = true
      prefill.user = String(data.user || '')
      prefill.pass = String(data.pass || '')
      prefill.remember = data.remember !== false
      prefill.rememberPass = !!data.rememberPass
      applyPrefill()
    })
  }

  /* ── 主时间线 ─────────────────────────────────────────────────────────── */

  function run() {
    A.chromeSync.hide()
    return Promise.resolve()
      .then(function () { return A.scenes.plate.play(rt, { skipFadeIn: true }) })
      .then(function () { return playWithRetries(0) })
      .then(function () { return A.scenes.finale.play(rt) })   // finish → done → 宿主导航
      .catch(function (err) {
        try { console.warn('[login] 序列登录异常，回落经典登录：', err) } catch (e2) {}
        fallback('脚本异常: ' + ((err && err.message) || err))
      })
  }

  /** 回落经典登录页：终止本页一切活动并通知宿主（不派发 done） */
  function fallback(reason) {
    if (state.finished) return
    state.finished = true
    tl.releaseAll()
    tl.stopHex()
    A.voice.stop()
    A.stopClock()
    if (bridge) bridge.report('login-fallback', String(reason || ''))
  }

  /** SKIP / Esc：认证前 = 切经典登录页；认证后 = 跳过剩余动画直接进主页 */
  function requestSkip() {
    if (state.skipped || state.finished) return
    state.skipped = true
    tl.releaseAll()
    if (authOk) {
      A.finish()                       // 派发 done → 宿主导航
    } else {
      A.finish({ silent: true, after: function () { if (bridge) bridge.report('login-skip', '') } })
    }
  }

  if (ui.skip) {
    ui.skip.hidden = false
    ui.skip.addEventListener('click', function (e) { e.stopPropagation(); requestSkip() })
    document.addEventListener('keydown', function (e) {
      if (e.key === 'Escape') requestSkip()
    })
  }

  /* 最后一道保险：自举阶段抛异常时不许把用户留在黑幕/白屏里 */
  setTimeout(function () {
    try {
      var r = document.getElementById(ROOT_ID)
      if (r && r.classList && r.classList.contains('dsu-boot')) {
        if (r.parentNode) r.parentNode.removeChild(r)
        document.documentElement.classList.remove('dsu-lock')
        if (document.body) document.body.style.overflow = ''
        window.__NC_LOGIN_FAILED__ = true
        if (bridge) bridge.report('login-fallback', 'bootstrap-timeout')
      }
    } catch (e) { /* 兜底本身不许再抛 */ }
  }, 9000)

  /* 诊断把手（bridge.js 的 audioDiag 读这里） */
  window.__DSH_STARTUP_API__ = {
    audioDiag: function () { return A.audio.diag() },
    skip: requestSkip,
    finish: function () { return A.finish() },
    config: C
  }

  /* 锁滚动 + 通知宿主"页面就绪，可以推 prefill" */
  document.documentElement.classList.add('dsu-lock')
  document.documentElement.style.overflow = 'hidden'
  if (document.body) document.body.style.overflow = 'hidden'
  tl.setProgress(0.05)
  root.classList.remove('dsu-boot')

  if (bridge) bridge.send('login-ready', {})

  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', function () { run() })
  } else {
    run()
  }
})()
