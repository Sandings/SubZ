define(['emby-button'], function () {
    'use strict';

    var timer = null;
    var refreshIntervalMs = 3000;
    var tokenDaysFilter = 0;
    var tokenRecordLimit = 200;
    var taskPageSize = 10;
    var taskPageIndex = 0;
    var lastStatusData = null;
    var lang = 'en';

    var i18n = {
        en: {
            refresh: 'Refresh',
            pause: 'Pause',
            resume: 'Resume',
            stop: 'Stop',
            language: 'Language',
            autoRefresh: 'Auto Refresh',
            taskStatus: 'Task Status',
            tokenUsage: 'Token Usage',
            range: 'Range',
            recentLogs: 'Recent Logs',
            noTasks: 'No tasks',
            noLogs: 'No logs',
            noTokenRecords: 'No token usage records',
            filteredHint: ' (filtered by current range)',
            tasks: 'Tasks',
            prompt: 'Prompt',
            completion: 'Completion',
            total: 'Total',
            cues: 'Cues',
            latest: 'Latest',
            time: 'Time',
            fileName: 'File Name',
            running: 'Running',
            completed: 'Completed',
            failed: 'Failed',
            stopped: 'Stopped',
            queued: 'Queued',
            confirmStop: 'Stop current task and clear queue?',
            loading: 'Loading SubZ status...',
            loadFailed: 'Failed to load SubZ status. Check Emby server logs or browser console.',
            loadFailedWithError: 'Failed to load SubZ status: ',
            previous: 'Previous',
            next: 'Next',
            pageStatus: 'Page {0} / {1}'
        },
        zh: {
            refresh: '\u5237\u65b0',
            pause: '\u6682\u505c',
            resume: '\u7ee7\u7eed',
            stop: '\u505c\u6b62',
            language: '\u8bed\u8a00',
            autoRefresh: '\u81ea\u52a8\u5237\u65b0',
            taskStatus: '\u4efb\u52a1\u72b6\u6001',
            tokenUsage: 'Token \u4f7f\u7528',
            range: '\u8303\u56f4',
            recentLogs: '\u6700\u8fd1\u65e5\u5fd7',
            noTasks: '\u6682\u65e0\u4efb\u52a1',
            noLogs: '\u6682\u65e0\u65e5\u5fd7',
            noTokenRecords: '\u6682\u65e0 Token \u4f7f\u7528\u8bb0\u5f55',
            filteredHint: '\uff08\u5f53\u524d\u8303\u56f4\u7b5b\u9009\u540e\u4e3a\u7a7a\uff09',
            tasks: '\u4efb\u52a1\u6570',
            prompt: '\u8f93\u5165',
            completion: '\u8f93\u51fa',
            total: '\u603b\u8ba1',
            cues: '\u5b57\u5e55\u6761\u6570',
            latest: '\u6700\u65b0\u65f6\u95f4',
            time: '\u65f6\u95f4',
            fileName: '\u6587\u4ef6\u540d',
            running: '\u8fd0\u884c\u4e2d',
            completed: '\u5df2\u5b8c\u6210',
            failed: '\u5931\u8d25',
            stopped: '\u5df2\u505c\u6b62',
            queued: '\u6392\u961f\u4e2d',
            confirmStop: '\u505c\u6b62\u5f53\u524d\u4efb\u52a1\u5e76\u6e05\u7a7a\u961f\u5217\uff1f',
            loading: '\u6b63\u5728\u52a0\u8f7d SubZ \u72b6\u6001...',
            loadFailed: '\u52a0\u8f7d SubZ \u72b6\u6001\u5931\u8d25\u3002\u8bf7\u68c0\u67e5 Emby \u670d\u52a1\u65e5\u5fd7\u6216\u6d4f\u89c8\u5668\u63a7\u5236\u53f0\u3002',
            loadFailedWithError: '\u52a0\u8f7d SubZ \u72b6\u6001\u5931\u8d25\uff1a',
            previous: '\u4e0a\u4e00\u9875',
            next: '\u4e0b\u4e00\u9875',
            pageStatus: '\u7b2c {0} / {1} \u9875'
        }
    };

    function t(key) {
        var dict = i18n[lang] || i18n.en;
        return dict[key] || key;
    }

    function esc(t) {
        return String(t || '')
            .replace(/&/g, '&amp;')
            .replace(/</g, '&lt;')
            .replace(/>/g, '&gt;')
            .replace(/"/g, '&quot;');
    }

    function fmtTime(iso) {
        if (!iso) return '-';
        try {
            var d = new Date(iso);
            return isNaN(d.getTime()) ? iso : d.toLocaleString();
        } catch (_) {
            return iso;
        }
    }

    function badgeCls(state) {
        if (state === 'Running') return 'sz-badge sz-running';
        if (state === 'Succeeded') return 'sz-badge sz-succeeded';
        if (state === 'Failed') return 'sz-badge sz-failed';
        if (state === 'Stopped') return 'sz-badge sz-stopped';
        return 'sz-badge sz-queued';
    }

    function stateLbl(state) {
        if (state === 'Running') return t('running');
        if (state === 'Succeeded') return t('completed');
        if (state === 'Failed') return t('failed');
        if (state === 'Stopped') return t('stopped');
        return t('queued');
    }

    function applyStaticTexts(view) {
        var el;
        el = view.querySelector('#refreshBtn'); if (el) el.textContent = t('refresh');
        el = view.querySelector('#pauseBtn'); if (el) el.textContent = t('pause');
        el = view.querySelector('#resumeBtn'); if (el) el.textContent = t('resume');
        el = view.querySelector('#stopBtn'); if (el) el.textContent = t('stop');
        el = view.querySelector('#languageLabel'); if (el) el.textContent = t('language');
        el = view.querySelector('#taskStatusTitle'); if (el) el.textContent = t('taskStatus');
        el = view.querySelector('#tokenUsageTitle'); if (el) el.textContent = t('tokenUsage');
        el = view.querySelector('#rangeLabel'); if (el) el.textContent = t('range');
        el = view.querySelector('.sectionTitle'); // keep existing order, set by ids where possible.

        el = view.querySelector('#autoRefresh');
        if (el && el.parentNode && el.parentNode.lastChild && el.parentNode.lastChild.nodeType === 3) {
            el.parentNode.lastChild.nodeValue = ' ' + t('autoRefresh');
        }

        el = view.querySelector('#statusList');
        if (el && el.innerHTML.indexOf('sz-empty') >= 0) {
            el.innerHTML = '<div class="sz-empty">' + esc(t('noTasks')) + '</div>';
        }

        var titles = view.querySelectorAll('h2.sectionTitle');
        if (titles && titles.length >= 3) {
            if (!view.querySelector('#taskStatusTitle')) titles[0].textContent = t('taskStatus');
            if (!view.querySelector('#tokenUsageTitle')) titles[1].textContent = t('tokenUsage');
            titles[titles.length - 1].textContent = t('recentLogs');
        }

        var uiLang = view.querySelector('#uiLanguage');
        if (uiLang) {
            uiLang.value = lang === 'zh' ? 'zh' : 'en';
        }
    }

    function statusUrl() {
        return window.ApiClient.getUrl('SubZ/Translate/Status', {
            LogLimit: 80,
            LogSource: 'file',
            TokenDays: tokenDaysFilter,
            TokenLimit: tokenRecordLimit
        });
    }

    function controlUrl(cmd) {
        return window.ApiClient.getUrl('SubZ/Translate/Status', {
            LogLimit: 80,
            LogSource: 'file',
            Cmd: cmd,
            TokenDays: tokenDaysFilter,
            TokenLimit: tokenRecordLimit
        });
    }

    function ensureStyle() {
        if (document.getElementById('szStyle')) return;

        var s = document.createElement('style');
        s.id = 'szStyle';
        s.textContent = [
            '.sz-page{background:var(--theme-background-level0,var(--backgroundColor,#fff));min-height:100vh;}',
            '.sz-badge{display:inline-block;padding:2px 8px;border-radius:999px;color:#fff;font-size:11px;margin-right:8px;vertical-align:middle}',
            '.sz-queued{background:#6b7280}',
            '.sz-running{background:#0ea5e9}',
            '.sz-succeeded{background:#16a34a}',
            '.sz-failed{background:#dc2626}',
            '.sz-stopped{background:#f59e0b}',
            '.sz-item{padding:.8em 1em;border-top:1px solid var(--cardBorderColor,#eee)}',
            '.sz-item:first-child{border-top:0}',
            '.sz-target{font-weight:600;word-break:break-all}',
            '.sz-title-time{display:inline-block;margin-left:.7em;font-size:.88em;font-weight:400;opacity:.62;white-space:nowrap}',
            '.sz-meta{opacity:.65;margin-top:.3em;font-size:.88em}',
            '.sz-log{padding:1em;font-family:monospace;font-size:.85em;white-space:pre-wrap;overflow-x:hidden;max-height:36em;overflow-y:auto}',
            '.sz-log-line{padding:.3em 0;border-bottom:1px solid var(--cardBorderColor,#f1f1f1);word-break:break-all}',
            '.sz-log-line:last-child{border-bottom:0}',
            '.sz-empty{padding:1.5em;text-align:center;opacity:.5}',
            '.sz-token-row{display:grid;grid-template-columns:1.4fr 2.6fr .8fr .9fr .8fr .7fr;gap:.6em;padding:.55em 1em;border-top:1px solid var(--cardBorderColor,#f1f1f1);font-family:monospace;font-size:.82em}',
            '.sz-token-head{font-weight:700;opacity:.75;background:var(--theme-background-level1,rgba(0,0,0,.02));}',
            '.sz-token-cell{text-align:right}',
            '.sz-token-time{text-align:left;font-family:inherit;word-break:break-all}',
            '.sz-task-pager{display:flex;align-items:center;justify-content:flex-end;gap:.45em;min-height:2.4em}',
            '.sz-page-info{font-size:.88em;opacity:.7;white-space:nowrap}',
            '.sz-pager-btn{min-width:5.6em;padding:.45em .7em;border:1px solid var(--cardBorderColor,#d1d5db);border-radius:6px;background:var(--cardBackground,#fff);color:inherit;cursor:pointer}',
            '.sz-pager-btn:disabled{opacity:.38;cursor:default}'
        ].join('');
        document.head.appendChild(s);
    }

    function setStatusError(view, message) {
        var el = view.querySelector('#statusError');
        if (!el) return;

        if (!message) {
            el.style.display = 'none';
            el.textContent = '';
            return;
        }

        el.style.display = '';
        el.textContent = message;
    }

    function formatText(template) {
        var value = String(template || '');
        for (var i = 1; i < arguments.length; i++) {
            value = value.replace('{' + (i - 1) + '}', String(arguments[i]));
        }
        return value;
    }

    function drawTaskPager(view, totalItems, totalPages) {
        var pager = view.querySelector('#taskPager');
        if (!pager) return;

        if (totalItems <= taskPageSize) {
            pager.innerHTML = '';
            return;
        }

        pager.innerHTML = ''
            + '<button id="taskPrevBtn" class="sz-pager-btn" type="button"' + (taskPageIndex <= 0 ? ' disabled' : '') + '>' + esc(t('previous')) + '</button>'
            + '<span class="sz-page-info">' + esc(formatText(t('pageStatus'), taskPageIndex + 1, totalPages)) + '</span>'
            + '<button id="taskNextBtn" class="sz-pager-btn" type="button"' + (taskPageIndex >= totalPages - 1 ? ' disabled' : '') + '>' + esc(t('next')) + '</button>';
    }

    function draw(view, data) {
        lastStatusData = data;
        var statusEl = view.querySelector('#statusList');
        var logEl = view.querySelector('#logList');
        var tokenUsageEl = view.querySelector('#tokenUsagePanel');
        if (!statusEl || !logEl || !tokenUsageEl) return;

        setStatusError(view, '');

        var items = data && data.Items || [];
        var logs = data && data.Logs || [];
        var running = 0;
        var succeeded = 0;
        var failed = 0;

        if (items.length === 0) {
            statusEl.innerHTML = '<div class="sz-empty">' + esc(t('noTasks')) + '</div>';
            drawTaskPager(view, 0, 1);
        } else {
            var sh = '';
            var totalPages = Math.max(1, Math.ceil(items.length / taskPageSize));
            if (taskPageIndex >= totalPages) taskPageIndex = totalPages - 1;
            if (taskPageIndex < 0) taskPageIndex = 0;
            var start = taskPageIndex * taskPageSize;
            var pageItems = items.slice(start, start + taskPageSize);

            for (var i = 0; i < pageItems.length; i++) {
                var it = pageItems[i] || {};
                var st = String(it.State || '');
                var timeHtml = '<div class="sz-meta">' + esc(fmtTime(it.UpdatedAt)) + '</div>';
                var titleTimeHtml = (st === 'Succeeded' || st === 'Failed')
                    ? '<span class="sz-title-time">' + esc(fmtTime(it.UpdatedAt)) + '</span>'
                    : '';
                var messageHtml = '<div class="sz-meta">' + esc(it.Message || '') + '</div>';

                sh += '<div class="sz-item">'
                    + '<div class="sz-target"><span class="' + badgeCls(st) + '">' + esc(stateLbl(st)) + '</span>' + esc(it.Target || '') + titleTimeHtml + '</div>';

                if (st === 'Failed') {
                    sh += messageHtml;
                } else if (st !== 'Succeeded') {
                    sh += messageHtml + timeHtml;
                }

                sh += '</div>';
            }
            statusEl.innerHTML = sh;
            drawTaskPager(view, items.length, totalPages);
        }

        for (var c = 0; c < items.length; c++) {
            var cs = String((items[c] || {}).State || '');
            if (cs === 'Running') running++;
            if (cs === 'Succeeded') succeeded++;
            if (cs === 'Failed') failed++;
        }

        var el;
        el = view.querySelector('#cRunning'); if (el) el.textContent = String(running);
        el = view.querySelector('#cSucceeded'); if (el) el.textContent = String(succeeded);
        el = view.querySelector('#cFailed'); if (el) el.textContent = String(failed);

        if (logs.length === 0) {
            logEl.innerHTML = '<div class="sz-log"><div class="sz-empty" style="padding:.3em 0">' + esc(t('noLogs')) + '</div></div>';
        } else {
            var lh = '<div class="sz-log">';
            for (var j = 0; j < logs.length; j++) {
                var lg = logs[j] || {};
                var msg = String(lg.Message || '');
                if (msg.length > 2000) {
                    msg = msg.substring(0, 2000) + ' ... [truncated in dashboard]';
                }

                lh += '<div class="sz-log-line"><span style="opacity:.5">[' + esc(lg.Level || 'Info') + ']</span> ' + esc(msg) + '</div>';
            }
            lh += '</div>';
            logEl.innerHTML = lh;
        }

        var usage = data && data.TokenUsage || {};
        var records = data && data.TokenRecords || [];
        var debug = data && data.Debug || {};
        var taskCount = Number(usage.TaskCount || 0);
        var prompt = Number(usage.PromptTokens || 0);
        var completion = Number(usage.CompletionTokens || 0);
        var total = Number(usage.TotalTokens || 0);
        var cues = Number(usage.Cues || 0);

        if (taskCount <= 0) {
            var noDataText = t('noTokenRecords');
            var beforeCount = Number(debug.TokenEntriesBeforeFilter || 0);
            var afterCount = Number(debug.TokenEntriesAfterFilter || 0);
            if (beforeCount > 0 && afterCount === 0) {
                noDataText += t('filteredHint');
            }
            tokenUsageEl.innerHTML = '<div class="sz-empty">' + esc(noDataText) + '</div>';
        } else {
            var tu = '';
            tu += '<div style="padding:1em;display:grid;grid-template-columns:repeat(auto-fit,minmax(140px,1fr));gap:.8em;">';
            tu += '<div class="card" style="background:var(--cardBackground);border:1px solid var(--cardBorderColor,#e5e7eb);border-radius:8px;padding:.8em;"><div style="font-size:.8em;opacity:.7;">' + esc(t('tasks')) + '</div><div style="font-size:1.2em;font-weight:700;">' + esc(String(taskCount)) + '</div></div>';
            tu += '<div class="card" style="background:var(--cardBackground);border:1px solid var(--cardBorderColor,#e5e7eb);border-radius:8px;padding:.8em;"><div style="font-size:.8em;opacity:.7;">' + esc(t('prompt')) + '</div><div style="font-size:1.2em;font-weight:700;">' + esc(String(prompt)) + '</div></div>';
            tu += '<div class="card" style="background:var(--cardBackground);border:1px solid var(--cardBorderColor,#e5e7eb);border-radius:8px;padding:.8em;"><div style="font-size:.8em;opacity:.7;">' + esc(t('completion')) + '</div><div style="font-size:1.2em;font-weight:700;">' + esc(String(completion)) + '</div></div>';
            tu += '<div class="card" style="background:var(--cardBackground);border:1px solid var(--cardBorderColor,#e5e7eb);border-radius:8px;padding:.8em;"><div style="font-size:.8em;opacity:.7;">' + esc(t('total')) + '</div><div style="font-size:1.2em;font-weight:700;">' + esc(String(total)) + '</div></div>';
            tu += '<div class="card" style="background:var(--cardBackground);border:1px solid var(--cardBorderColor,#e5e7eb);border-radius:8px;padding:.8em;"><div style="font-size:.8em;opacity:.7;">' + esc(t('cues')) + '</div><div style="font-size:1.2em;font-weight:700;">' + esc(String(cues)) + '</div></div>';
            tu += '</div>';
            tu += '<div class="sz-token-row sz-token-head">'
                + '<div class="sz-token-time">' + esc(t('time')) + '</div>'
                + '<div class="sz-token-time">' + esc(t('fileName')) + '</div>'
                + '<div class="sz-token-cell">' + esc(t('prompt')) + '</div>'
                + '<div class="sz-token-cell">' + esc(t('completion')) + '</div>'
                + '<div class="sz-token-cell">' + esc(t('total')) + '</div>'
                + '<div class="sz-token-cell">' + esc(t('cues')) + '</div>'
                + '</div>';

            for (var k = 0; k < records.length; k++) {
                var rec = records[k] || {};
                tu += '<div class="sz-token-row">'
                    + '<div class="sz-token-time">' + esc(fmtTime(rec.Timestamp)) + '</div>'
                    + '<div class="sz-token-time">' + esc(rec.FileName || '-') + '</div>'
                    + '<div class="sz-token-cell">' + esc(String(rec.PromptTokens || 0)) + '</div>'
                    + '<div class="sz-token-cell">' + esc(String(rec.CompletionTokens || 0)) + '</div>'
                    + '<div class="sz-token-cell">' + esc(String(rec.TotalTokens || 0)) + '</div>'
                    + '<div class="sz-token-cell">' + esc(String(rec.Cues || 0)) + '</div>'
                    + '</div>';
            }
            tokenUsageEl.innerHTML = tu;
        }

        var lr = view.querySelector('#lastRefresh');
        if (lr) lr.textContent = new Date().toLocaleString();
    }

    function load(view) {
        try {
            window.ApiClient.getJSON(statusUrl()).then(
                function (d) { draw(view, d); },
                function () { setStatusError(view, t('loadFailed')); }
            );
        } catch (err) {
            setStatusError(view, t('loadFailedWithError') + (err && err.message ? err.message : err));
        }
    }

    function control(view, cmd) {
        try {
            window.ApiClient.getJSON(controlUrl(cmd)).then(
                function () { load(view); },
                function () { load(view); }
            );
        } catch (_) {}
    }

    function stopTimer() {
        if (timer) {
            clearInterval(timer);
            timer = null;
        }
    }

    function startTimer(view) {
        stopTimer();
        timer = setInterval(function () {
            load(view);
        }, refreshIntervalMs);
    }

    function initialize(view) {
        if (!view) return;

        ensureStyle();
        lang = localStorage.getItem('subz_status_lang') || 'en';
        setStatusError(view, t('loading'));
        applyStaticTexts(view);

        if (view.dataset.subzInitialized !== '1') {
            view.dataset.subzInitialized = '1';

            var refreshBtn = view.querySelector('#refreshBtn');
            var pauseBtn = view.querySelector('#pauseBtn');
            var resumeBtn = view.querySelector('#resumeBtn');
            var stopBtn = view.querySelector('#stopBtn');
            var autoChk = view.querySelector('#autoRefresh');
            var tokenDays = view.querySelector('#tokenDays');
            var uiLang = view.querySelector('#uiLanguage');
            var taskPager = view.querySelector('#taskPager');

            if (refreshBtn) refreshBtn.addEventListener('click', function () { load(view); });
            if (pauseBtn) pauseBtn.addEventListener('click', function () { control(view, 'pause'); });
            if (resumeBtn) resumeBtn.addEventListener('click', function () { control(view, 'resume'); });
            if (stopBtn) stopBtn.addEventListener('click', function () {
                if (confirm(t('confirmStop'))) control(view, 'stop');
            });
            if (autoChk) autoChk.addEventListener('change', function () {
                if (autoChk.checked) startTimer(view);
                else stopTimer();
            });
            if (tokenDays) tokenDays.addEventListener('change', function () {
                tokenDaysFilter = parseInt(tokenDays.value || '0', 10) || 0;
                load(view);
            });
            if (uiLang) {
                uiLang.value = lang;
                uiLang.addEventListener('change', function () {
                    lang = uiLang.value === 'zh' ? 'zh' : 'en';
                    localStorage.setItem('subz_status_lang', lang);
                    applyStaticTexts(view);
                    if (lastStatusData) draw(view, lastStatusData);
                    else load(view);
                });
            }
            if (taskPager) taskPager.addEventListener('click', function (e) {
                var target = e.target || {};
                if (target.id === 'taskPrevBtn' && taskPageIndex > 0) {
                    taskPageIndex--;
                    if (lastStatusData) draw(view, lastStatusData);
                } else if (target.id === 'taskNextBtn') {
                    taskPageIndex++;
                    if (lastStatusData) draw(view, lastStatusData);
                }
            });
        }

        load(view);
        var autoRefresh = view.querySelector('#autoRefresh');
        if (!autoRefresh || autoRefresh.checked) startTimer(view);
    }

    return function (view) {
        initialize(view);

        view.addEventListener('viewshow', function () {
            initialize(view);
        });

        view.addEventListener('viewhide', function () {
            stopTimer();
        });
    };
});
