/* ============================================================================
 * 帝皇权杖δ-me13 · 启动动画 ↔ MAUI 宿主 桥接脚本
 * ----------------------------------------------------------------------------
 * 动画运行时（splash.js）不感知宿主，本脚本负责把它的两个时机告诉 C#：
 *   1) dsh-startup:done  → 动画播完/被跳过 → 跳 newcosmos-splash://done
 *      C# 侧 SplashPage 的 Navigating（Windows）/ WebViewClient（Android）捕获，
 *      取消这次导航并放行启动闸门，切换到登录页或主界面；
 *   2) window.onerror / unhandledrejection → newcosmos-splash://error?msg=…
 *      C# 记录日志后同样放行，任何脚本异常都不许把用户留在黑幕里。
 * （脚本本身加载失败时没有本桥，由 SplashGate 的超时看门狗兜底。）
 * ========================================================================== */
(function () {
  'use strict';

  function ping(uri) {
    try { window.location.href = uri; } catch (e) { /* noop */ }
  }

  /* 第二路信号：WebView2 的 postMessage（Android WebView 没有此对象，自动跳过）。
     个别宿主对自定义协议导航的拦截不可靠，两路任一到即可，Complete() 幂等。 */
  function notifyHost(text) {
    try {
      if (window.chrome && window.chrome.webview && window.chrome.webview.postMessage) {
        window.chrome.webview.postMessage(text);
      }
    } catch (e) { /* noop */ }
  }

  function report(kind, msg) {
    try {
      var text = String(msg || '').slice(0, 500);
      notifyHost('newcosmos-splash:' + kind + ':' + text);
      ping('newcosmos-splash://' + kind + '?msg=' + encodeURIComponent(text));
    } catch (e) { /* noop */ }
  }

  window.addEventListener('dsh-startup:done', function () {
    /* 等 finish() 把 root 摘掉、页面淡出后再通知宿主，切换更顺 */
    setTimeout(function () {
      notifyHost('newcosmos-splash:done');
      ping('newcosmos-splash://done');
    }, 120);
  });

  /* 脚本已加载 → 立即回执（仅 Windows 的 WebView2 消息通道；不触发导航，
     避免解析期被自定义协议导航打断）。宿主收不到回执时 12 秒后直接放行。 */
  notifyHost('newcosmos-splash:boot');

  /* 音频状态诊断：4 秒后回报 AudioContext 状态与已解码素材数，
     宿日志据此判断自动播放是否被策略拦截、mp3 是否加载成功。 */
  setTimeout(function () {
    try {
      var api = window.__DSH_STARTUP_API__
      var d = api && api.audioDiag ? api.audioDiag() : { state: 'n/a', assets: -1 }
      notifyHost('newcosmos-splash:audio:' + (d.state || 'unknown') + '|assets=' + d.assets)
    } catch (e) {
      notifyHost('newcosmos-splash:audio:diag-error')
    }
  }, 4000);

  window.addEventListener('error', function (e) {
    var msg = e && (e.message || (e.error && e.error.message)) || 'script-error';
    report('error', msg);
  }, true);

  window.addEventListener('unhandledrejection', function (e) {
    var reason = e && e.reason;
    report('error', (reason && (reason.message || reason)) || 'unhandledrejection');
  });
})();
