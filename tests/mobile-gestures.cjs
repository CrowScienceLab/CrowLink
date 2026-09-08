const fs = require('node:fs');
const vm = require('node:vm');
const assert = require('node:assert/strict');
const source = fs.readFileSync(require('node:path').join(__dirname, '../src/CrowLink.App/Services/Mobile/MobileWebAssets.cs'), 'utf8');
const script = source.match(/<script>([\s\S]*?)<\/script>/)[1];
const elements = new Map(), sent = [], frames = new Map(), intervals = [];
let sequence = 0, socket;
function element(id) {
    if (!elements.has(id)) elements.set(id, {
        value: id === '#code' ? '123456' : '4', checked: true, style: {}, events: {}, children: [], attributes: {},
        classList: { add() {}, remove() {}, toggle() {} },
        addEventListener(type, handler) { this.events[type] = handler; },
        append(child) { this.children.push(child); }, replaceChildren() { this.children = []; },
        setAttribute(key, value) { this.attributes[key] = value; }, focus() {}, setPointerCapture() {}, closest() { return null; },
        getBoundingClientRect() { return { left: 0, top: 0, width: 800, height: 450 }; },
    });
    return elements.get(id);
}
class Socket {
    constructor() { socket = this; this.readyState = 1; }
    send(text) { sent.push(JSON.parse(text)); }
    close() {}
}
const context = {
    document: { body: element('body'), querySelector: element, createElement: element, createElementNS: (_, name) => element(name + ++sequence), addEventListener() {},
        documentElement: { requestFullscreen: async () => {} }, hidden: false },
    window: { addEventListener() {} }, location: { protocol: 'http:', host: 'localhost' },
    navigator: { userAgent: 'Test', vibrate() {} }, screen: { orientation: { unlock() {}, lock: async () => {} } },
    innerWidth: 1000, innerHeight: 500, WebSocket: Socket,
    setTimeout() { return 1; }, clearTimeout() {}, setInterval(fn) { intervals.push(fn); },
    requestAnimationFrame(fn) { frames.set(++sequence, fn); return sequence; },
    cancelAnimationFrame(id) { frames.delete(id); },
};
vm.runInNewContext(script, context);
const pad = element('#pad');
function pointer(type, id, x, y) { pad.events[type]({ type, pointerId:id, clientX:x, clientY:y, target:pad, preventDefault() {} }); }
function flush() { for (const fn of frames.values()) fn(); frames.clear(); }
(async () => {
    await element('#connect').events.click();
    socket.onmessage({data:JSON.stringify({type:'paired', session:'test', monitors:[{width:1920,height:1080}],monitorIndex:0})});
    sent.length = 0;
    pointer('pointerdown',1,100,100); pointer('pointerdown',2,200,100);
    pointer('pointermove',1,100,120); pointer('pointermove',2,200,120); flush();
    assert(sent.some(m => m.type === 'scroll' && m.delta < 0), 'Finger-down must move the vertical scrollbar down (negative Windows wheel).');
    assert(sent.filter(m => m.type === 'scroll').every(m => m.horizontal === 0), 'Vertical gestures must never send horizontal scroll.');
    pointer('pointerup',1,100,120); pointer('pointerup',2,200,120);
    for (const [width, height] of [[390,844], [844,390]]) {
        context.innerWidth = width; context.innerHeight = height;
        for (const [dx,dy,axis,sign] of [[.3,12,'delta',-1],[.3,-12,'delta',1],[12,.3,'horizontal',1],[-12,.3,'horizontal',-1],[0,1,'delta',-1]]) {
            sent.length = 0;
            pointer('pointerdown',1,100,100); pointer('pointerdown',2,200,100);
            for (let n=1;n<=8;n++) { pointer('pointermove',1,100+n*dx,100+n*dy); pointer('pointermove',2,200+n*dx,100+n*dy); flush(); }
            const events = sent.filter(m => m.type === 'scroll');
            assert(events.length > 0, 'Slow finger movement must accumulate past the threshold.');
            assert(events.every(m => Math.sign(m[axis]) === sign), 'Each scrollbar must follow the fingers in both orientations.');
            assert(events.every(m => m[axis === 'delta' ? 'horizontal' : 'delta'] === 0), 'Axis lock must suppress perpendicular jitter.');
            assert(!sent.some(m => m.type === 'move'), 'Scrolling must not move the cursor.');
            pointer('pointerup',1,100+8*dx,100+8*dy);
            sent.length = 0; pointer('pointermove',2,240+8*dx,100+8*dy); flush();
            assert(!sent.some(m => m.type === 'move'), 'Remaining finger must not jump the cursor.');
            pointer('pointerup',2,240+8*dx,100+8*dy);
        }
    }
    sent.length = 0;
    pointer('pointerdown',1,100,100); pointer('pointerdown',2,200,100);
    pointer('pointerup',1,100,100); pointer('pointerup',2,200,100);
    assert.equal(sent.filter(m => m.type === 'click').length, 1, 'Two-finger tap must not append a left click.');
    assert.equal(sent.find(m => m.type === 'click').button, 'right');
    sent.length = 0;
    pointer('pointerdown',1,100,100); pointer('pointercancel',1,100,100);
    assert(!sent.some(m => m.type === 'click'), 'Cancelled touch must not click.');
    await element('#penMode').events.click(); sent.length = 0;
    pointer('pointerdown',1,400,225); pointer('pointerup',1,400,225);
    const pen = sent.find(m => m.type === 'pen' && m.phase === 'down');
    assert.equal(pen.x, 0.5); assert.equal(pen.y, 0.5);
    assert.equal(element('#localInk').children.length, 1, 'Local pen stroke must appear immediately.');
    assert(element('#localInk').children[0].attributes.points.startsWith('500.00,500.00'));
    element('#clearInk').events.click(); assert.equal(element('#localInk').children.length, 0);
    element('#whiteboard').events.click(); assert(sent.some(m => m.type === 'whiteboard' && m.enabled));
    intervals.forEach(fn => fn());
    assert(!sent.some(m => m.type === 'preview'), 'Pen mode must not request screen previews.');
    socket.onmessage({data:JSON.stringify({type:'browserText',text:'새 한글 입력',revision:2})});
    socket.onmessage({data:JSON.stringify({type:'browserText',text:'지연된 입력',revision:1})});
    assert.equal(element('#phoneText').value,'새 한글 입력','Stale text updates must not replace newer text.');
    element('#sendSharedText').events.click();
    assert(sent.some(m=>m.type==='shareText'&&m.text==='새 한글 입력'));
    console.log('PASS portrait/landscape four-direction scroll, axis lock, slow movement, finger release, local ink, whiteboard, no preview, text ordering');
})().catch(error => { console.error(error); process.exitCode = 1; });
