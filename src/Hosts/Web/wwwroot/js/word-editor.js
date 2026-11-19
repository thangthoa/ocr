// Hosts the OnlyOffice Docs editor inside a container Blazor rendered empty. The editor replaces
// its placeholder element with an iframe, so the placeholder is created here, outside Blazor's DOM.
let editor = null;
let apiLoad = null;
let startTimer = null;

// The page may already be gone (navigated away, circuit closed); nothing is left to tell then.
function notify(page, method) {
    page.invokeMethodAsync(method).catch(() => { });
}

function loadApi(url) {
    if (window.DocsAPI) {
        return Promise.resolve();
    }
    apiLoad ??= new Promise((resolve, reject) => {
        const script = document.createElement('script');
        script.src = url;
        script.async = true;
        script.onload = () => resolve();
        script.onerror = () => {
            apiLoad = null;
            script.remove();
            reject(new Error('The document server script could not be loaded.'));
        };
        document.head.appendChild(script);
    });
    return apiLoad;
}

// The editor app never starts when one of its scripts is blocked (ad/tracker blockers block its
// Analytics.js) and it reports nothing: the page only learns of it by onAppReady not arriving in time.
export async function open(apiUrl, container, config, page, startTimeoutMs) {
    await loadApi(apiUrl);
    close();
    const placeholder = document.createElement('div');
    placeholder.id = `word-editor-${Date.now()}`;
    container.replaceChildren(placeholder);
    startTimer = setTimeout(() => {
        startTimer = null;
        notify(page, 'EditorStalled');
    }, startTimeoutMs);
    editor = new window.DocsAPI.DocEditor(placeholder.id, {
        ...config,
        events: {
            onAppReady: () => {
                clearTimeout(startTimer);
                startTimer = null;
                notify(page, 'EditorStarted');
            },
        },
    });
}

export function close() {
    clearTimeout(startTimer);
    startTimer = null;
    if (editor) {
        editor.destroyEditor();
        editor = null;
    }
}
