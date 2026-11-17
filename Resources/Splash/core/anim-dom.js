/* ============================================================================
 * 帝皇权杖δ-me13 · 动画运行时 core 2/7：DOM 搭建与页面级工具
 * ----------------------------------------------------------------------------
 * 拆分自原 splash.js：小工具（el / logoMark / typeIn / setIn）、
 * 系统标识位图、档案底板水印、准星 SVG、六格阶段柱、实时时钟，
 * 以及整个动画 DOM 模板与引用表（ui）。
 *
 * 两个入口（splash.html 全时间线 / login.html 序列登录）都调用 buildDom，
 * 保证结构与视觉完全一致；黑幕等"第一幕专属"层由入口按需隐藏。
 * ========================================================================== */
(function () {
  'use strict'

  if (window.__NC_ANIM__ && window.__NC_ANIM__.__m_dom__) return
  var A = window.__NC_ANIM__ = window.__NC_ANIM__ || {}
  A.__m_dom__ = true

  /* ── 小工具 ───────────────────────────────────────────────────────────── */
  function el(tag, cls, text) {
    var n = document.createElement(tag)
    if (cls) n.className = cls
    if (text != null) n.textContent = text
    return n
  }

  /** 系统标识：保留 <svg> 外壳（CSS 尺寸/颜色规则沿用），内部换成吉祥物位图 */
  function logoMark(cls) {
    var ns = 'http://www.w3.org/2000/svg'
    var svg = document.createElementNS(ns, 'svg')
    svg.setAttribute('viewBox', '0 0 50 50')
    svg.setAttribute('aria-hidden', 'true')
    if (cls) svg.setAttribute('class', cls)
    var img = document.createElementNS(ns, 'image')
    img.setAttribute('href', A.LOGO_SRC)
    img.setAttribute('x', '0')
    img.setAttribute('y', '0')
    img.setAttribute('width', '50')
    img.setAttribute('height', '50')
    img.setAttribute('preserveAspectRatio', 'xMidYMid meet')
    svg.appendChild(img)
    return svg
  }

  /** 逐字渐显：比逐字截断更稳（居中排版不会因为字宽变化而抖动） */
  function typeIn(host, text, stagger) {
    host.textContent = ''
    var frag = document.createDocumentFragment()
    var n = 0
    for (var i = 0; i < text.length; i++) {
      var ch = text.charAt(i)
      if (ch === ' ') {
        frag.appendChild(document.createTextNode('\u00a0'))
        continue
      }
      var s = el('span', 'dsu-ch', ch)
      s.style.transitionDelay = Math.round(n * stagger) + 'ms'
      frag.appendChild(s)
      n++
    }
    host.appendChild(frag)
  }

  function setIn(node, on, cls) {
    if (!node) return
    node.classList.toggle(cls || 'dsu-in', !!on)
  }

  A.utils = { el: el, logoMark: logoMark, typeIn: typeIn, setIn: setIn }

  /* ── 实时时钟（品牌块下方 meta 行；原 splash.js slatsAndMeta 内联实现）── */
  var clockTimer = null

  function paintClock(ui) {
    var d = new Date()
    var pad = function (n) { return (n < 10 ? '0' : '') + n }
    ui.meta2.textContent = pad(d.getHours()) + ':' + pad(d.getMinutes()) + ':' + pad(d.getSeconds())
  }

  A.stopClock = function () {
    if (clockTimer) { clearInterval(clockTimer); clockTimer = null }
  }

  /* ── 挂载点：取（或建）root，并按配置设主题与 CSS 变量 ────────────────── */
  A.prepareRoot = function (C) {
    var root = document.getElementById(A.ROOT_ID)
    if (!root) {
      root = el('div')
      root.id = A.ROOT_ID
      root.className = 'dsu-root'
      document.body.appendChild(root)
    }
    root.classList.toggle('dsu-dark', C.theme === 'dark')
    root.style.setProperty('--dsu-speed', String(C.speed))
    root.style.setProperty('--dsu-accent', C.accent)
    root.style.setProperty('--dsu-glitch-a', C.glitchA)
    root.style.setProperty('--dsu-glitch-b', C.glitchB)
    return root
  }

  /* ── DOM 搭建（原 splash.js 476-695：模板 + 引用表 + 内容填充 + 各装饰层）─ */
  A.buildDom = function (root, C, steps) {
    var ui = {}
    var html =
      '<div class="dsu-plate"></div>' +
      '<div class="dsu-vignette"></div>' +
      '<div class="dsu-watermark"></div>' +
      '<div class="dsu-grain"></div>' +
      '<div class="dsu-black">' +
        '<div class="dsu-black-inner"></div>' +
        '<div class="dsu-sweep"></div>' +
      '</div>' +
      '<div class="dsu-wash"></div>' +
      '<header class="dsu-brand">' +
        '<div class="dsu-brand-1"></div><div class="dsu-brand-2"></div>' +
        '<div class="dsu-brand-3"><b></b><span></span></div>' +
      '</header>' +
      '<div class="dsu-stepcount">[ 00 / 06 ] INIT</div>' +
      '<div class="dsu-center">' +
        '<div class="dsu-status"><span class="dsu-status-zh"></span><i class="dsu-caret"></i></div>' +
        '<div class="dsu-sub-en"></div>' +
        '<div class="dsu-rule"></div>' +
        '<div class="dsu-logo"></div>' +
        '<div class="dsu-dots" hidden></div>' +
      '</div>' +
      '<div class="dsu-idline"></div>' +
      '<div class="dsu-reticle"></div>' +
      '<div class="dsu-hex"></div>' +
      '<div class="dsu-welcome">' +
        '<div class="dsu-w1">WELCOME TO</div>' +
        '<div class="dsu-w2"></div>' +
        '<div class="dsu-w3"></div>' +
        '<div class="dsu-w4"></div>' +
      '</div>' +
      '<div class="dsu-action">' +
        '<div class="dsu-card" hidden></div>' +
        '<button class="dsu-btn" type="button">' +
          '<span class="dsu-btn-zh"></span><span class="dsu-btn-en"></span>' +
        '</button>' +
        '<div class="dsu-hint"></div>' +
      '</div>' +
      '<div class="dsu-frame">' +
        '<i class="dsu-corner dsu-c-tl"></i><i class="dsu-corner dsu-c-tr"></i>' +
        '<i class="dsu-corner dsu-c-bl"></i><i class="dsu-corner dsu-c-br"></i>' +
        '<div class="dsu-scanline"></div>' +
      '</div>' +
      '<div class="dsu-slats"></div>' +
      '<div class="dsu-voice"><i></i><i></i><i></i><i></i><i></i></div>' +
      '<div class="dsu-meta"><span class="dsu-meta-1"></span><span class="dsu-meta-2"></span></div>' +
      '<div class="dsu-skip">SKIP ▸</div>' +
      '<footer class="dsu-foot"><span></span><i></i></footer>' +
      '<div class="dsu-subtitle">' +
        '<span class="dsu-sub-zh"></span><span class="dsu-sub-en2"></span>' +
      '</div>' +
      '<div class="dsu-progress"><i></i></div>' +
      '<div class="dsu-bloom"></div>' +
      '<div class="dsu-streak"></div>' +
      '<div class="dsu-flash"></div>'

    root.innerHTML = html

    function q(sel) { return root.querySelector(sel) }
    ui.plate = q('.dsu-plate')
    ui.watermark = q('.dsu-watermark')
    ui.black = q('.dsu-black')
    ui.blackInner = q('.dsu-black-inner')
    ui.sweep = q('.dsu-sweep')
    ui.wash = q('.dsu-wash')
    ui.flash = q('.dsu-flash')
    ui.bloom = q('.dsu-bloom')
    ui.streak = q('.dsu-streak')
    ui.vignette = q('.dsu-vignette')
    ui.brand = q('.dsu-brand')
    ui.center = q('.dsu-center')
    ui.brand1 = q('.dsu-brand-1')
    ui.brand2 = q('.dsu-brand-2')
    ui.brand3b = q('.dsu-brand-3 b')
    ui.brand3s = q('.dsu-brand-3 span')
    ui.stepcount = q('.dsu-stepcount')
    ui.status = q('.dsu-status')
    ui.statusZh = q('.dsu-status-zh')
    ui.caret = q('.dsu-caret')
    ui.subEn = q('.dsu-sub-en')
    ui.rule = q('.dsu-rule')
    ui.logo = q('.dsu-logo')
    ui.dots = q('.dsu-dots')
    ui.idline = q('.dsu-idline')
    ui.reticle = q('.dsu-reticle')
    ui.hex = q('.dsu-hex')
    ui.welcome = q('.dsu-welcome')
    ui.w1 = q('.dsu-w1')
    ui.w2 = q('.dsu-w2')
    ui.w3 = q('.dsu-w3')
    ui.w4 = q('.dsu-w4')
    ui.action = q('.dsu-action')
    ui.card = q('.dsu-card')
    ui.btn = q('.dsu-btn')
    ui.btnZh = q('.dsu-btn-zh')
    ui.btnEn = q('.dsu-btn-en')
    ui.hint = q('.dsu-hint')
    ui.skip = q('.dsu-skip')
    ui.foot = q('.dsu-foot')
    ui.footText = q('.dsu-foot span')
    ui.subZh = q('.dsu-sub-zh')
    ui.subEn2 = q('.dsu-sub-en2')
    ui.progress = q('.dsu-progress')
    ui.bar = q('.dsu-progress i')
    ui.frame = q('.dsu-frame')
    ui.scanline = q('.dsu-scanline')
    ui.slats = q('.dsu-slats')
    ui.voice = q('.dsu-voice')
    ui.meta = q('.dsu-meta')
    ui.meta1 = q('.dsu-meta-1')
    ui.meta2 = q('.dsu-meta-2')

    /* 内容填充 */
    ui.brand1.textContent = C.brand1
    ui.brand2.textContent = C.brand2
    ui.brand3b.textContent = C.brand3a
    ui.brand3s.textContent = C.brand3b
    ui.footText.textContent = C.footer
    ui.w2.textContent = C.welcomeBox
    ui.w4.textContent = C.welcomeText || ('欢迎 ' + C.identity + ' 访问')
    ui.idline.textContent = '· ID CONFIRMED : ' + C.identityId

    /* 第一幕黑幕内容：吉祥物 + 三行品牌 */
    ;(function blackStage() {
      var inner = ui.blackInner
      var whale = logoMark('dsu-whale')
      inner.appendChild(whale)
      inner.appendChild(el('div', 'dsu-black-zh', C.orgZh))
      var en = el('div', 'dsu-black-en', C.orgEn)
      inner.appendChild(en)
      inner.appendChild(el('div', 'dsu-black-sub', C.orgSub))
      ui.blackEn = en
    })()

    /* 中央标识：吉祥物 + logoLabel（终幕前"一笔画成"用） */
    ;(function logoStage() {
      ui.logo.appendChild(logoMark())
      ui.logo.appendChild(el('div', 'dsu-logolabel', C.logoLabel || 'N E W C O S M O S'))
    })()

    /* 终幕欢迎区标识 */
    ;(function welcomeLogo() {
      ui.w3.appendChild(logoMark())
      ui.w3.appendChild(el('span', 'dsu-w3label', C.welcomeLabel || 'N E W C O S M O S · 社会救助'))
    })()

    /* 档案面板水印方块 */
    ;(function watermark() {
      var boxes = [
        [0.60, 0.02, 0.16, 0.96], [0.78, 0.02, 0.16, 0.96], [0.96, 0.02, 0.16, 0.96],
        [0.05, 0.02, 0.10, 0.5]
      ]
      for (var i = 0; i < boxes.length; i++) {
        var b = boxes[i]
        var n = el('div', 'dsu-wm')
        n.style.left = (b[0] * 100) + '%'
        n.style.top = (b[1] * 100) + '%'
        n.style.width = (b[2] * 100) + '%'
        n.style.height = (b[3] * 100) + '%'
        ui.watermark.appendChild(n)
      }
    })()

    /* 左侧六格阶段柱 + 品牌块下方的小字数据行 + 实时时钟 */
    ;(function slatsAndMeta() {
      for (var i = 0; i < steps.length + 1; i++) ui.slats.appendChild(el('i', 'dsu-slat'))
      ui.meta1.textContent = C.metaLine || 'REV 1.0 · DESKTOP'
      paintClock(ui)
      A.stopClock()
      clockTimer = setInterval(function () { paintClock(ui) }, 1000)
    })()

    /* 环形准星 SVG（外环自转 + 内环反向 + 断弧括号 + 定位点） */
    ;(function reticle() {
      var ns = 'http://www.w3.org/2000/svg'
      var svg = document.createElementNS(ns, 'svg')
      svg.setAttribute('viewBox', '0 0 400 400')
      function arc(cx, cy, r, a0, a1, cls, cap) {
        var rad = function (d) { return (d - 90) * Math.PI / 180 }
        var x0 = cx + r * Math.cos(rad(a0)), y0 = cy + r * Math.sin(rad(a0))
        var x1 = cx + r * Math.cos(rad(a1)), y1 = cy + r * Math.sin(rad(a1))
        var large = (a1 - a0) > 180 ? 1 : 0
        var p = document.createElementNS(ns, 'path')
        p.setAttribute('d', 'M' + x0.toFixed(2) + ' ' + y0.toFixed(2) +
          ' A' + r + ' ' + r + ' 0 ' + large + ' 1 ' + x1.toFixed(2) + ' ' + y1.toFixed(2))
        p.setAttribute('class', cls)
        if (cap) p.setAttribute('stroke-linecap', cap)
        return { node: p, x: x1, y: y1, x0: x0, y0: y0 }
      }
      var gA = document.createElementNS(ns, 'g')
      gA.setAttribute('class', 'dsu-ret-rot-a')
      /* 300° 弧，缺口留在正下方 6 点钟方向：底部字幕永远不会被压到，
         弧末端圆点落在 5 点钟位置，和原片一致。 */
      var a = arc(200, 200, 150, 215, 505, 'dsu-ring-a', 'round')
      gA.appendChild(a.node)
      var da = document.createElementNS(ns, 'circle')
      da.setAttribute('class', 'dsu-dot-a')
      da.setAttribute('cx', a.x.toFixed(2)); da.setAttribute('cy', a.y.toFixed(2)); da.setAttribute('r', '7')
      gA.appendChild(da)
      svg.appendChild(gA)

      var gB = document.createElementNS(ns, 'g')
      gB.setAttribute('class', 'dsu-ret-rot-b')
      var b = arc(200, 200, 104, 200, 490, 'dsu-ring-b', 'round')
      gB.appendChild(b.node)
      var db = document.createElementNS(ns, 'circle')
      db.setAttribute('class', 'dsu-dot-b')
      db.setAttribute('cx', b.x0.toFixed(2)); db.setAttribute('cy', b.y0.toFixed(2)); db.setAttribute('r', '4')
      gB.appendChild(db)
      svg.appendChild(gB)

      var gC = document.createElementNS(ns, 'g')
      var bl = arc(200, 200, 62, 118, 242, 'dsu-bracket', 'round')
      var br = arc(200, 200, 62, 298, 62, 'dsu-bracket', 'round')
      gC.appendChild(bl.node); gC.appendChild(br.node)
      svg.appendChild(gC)

      var pts = [[152, 132], [252, 268], [278, 150]]
      for (var i = 0; i < pts.length; i++) {
        var c = document.createElementNS(ns, 'circle')
        c.setAttribute('class', 'dsu-dot-o')
        c.setAttribute('cx', pts[i][0]); c.setAttribute('cy', pts[i][1]); c.setAttribute('r', '2.8')
        svg.appendChild(c)
      }
      ui.reticle.appendChild(svg)
    })()

    return ui
  }
})()
