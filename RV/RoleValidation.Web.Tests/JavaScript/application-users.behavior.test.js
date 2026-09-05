const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");
const test = require("node:test");
const vm = require("node:vm");

const script = fs.readFileSync(path.resolve(
    __dirname, "../../RoleValidation.Web/wwwroot/js/application-users.js"), "utf8");

function loadFilter(width, height, anchor) {
    const windowEvents = {};
    const documentEvents = {};
    const elements = {};
    for (const name of ["toggle", "panel", "search", "clear", "mode", "select-all",
        "select-all-label", "counter", "summary"]) {
        elements[name] = {
            dataset: {}, style: {}, events: {}, attributes: {}, hidden: name === "panel",
            addEventListener(type, handler) { this.events[type] = handler; },
            setAttribute(name, value) { this.attributes[name] = value; },
            focus() { this.focused = true; }
        };
    }
    elements.toggle.getBoundingClientRect = () => anchor;
    elements.panel.getBoundingClientRect = () => ({
        height: Math.min(360, parseFloat(elements.panel.style.maxHeight) || 360)
    });
    const filter = {
        dataset: {},
        querySelector(selector) { return elements[selector.slice(13, -1)]; },
        querySelectorAll() { return []; },
        contains(target) { return Object.values(elements).includes(target); }
    };
    const page = {
        querySelector() { return null; },
        querySelectorAll(selector) {
            return selector === "[data-multi-select-filter]" ? [filter] : [];
        }
    };
    const viewport = {
        width, height, offsetLeft: 0, offsetTop: 0, events: {},
        addEventListener(type, handler) { this.events[type] = handler; }
    };
    const window = {
        innerHeight: height, visualViewport: viewport,
        addEventListener(type, handler) { windowEvents[type] = handler; }
    };
    const document = {
        documentElement: { clientWidth: width },
        querySelector() { return page; },
        addEventListener(type, handler) { documentEvents[type] = handler; }
    };
    vm.runInNewContext(script, { window, document });
    return { elements, viewport, windowEvents, documentEvents };
}

for (const width of [375, 768, 1150, 1200, 1274, 1440]) {
    test("popup stays inside viewport at " + width + "px", () => {
        const { elements } = loadFilter(width, 800, {
            left: width - 210, top: 220, bottom: 262
        });
        elements.toggle.events.click();
        const left = parseFloat(elements.panel.style.left);
        const panelWidth = parseFloat(elements.panel.style.width);
        assert.ok(left >= 12);
        assert.ok(left + panelWidth <= width - 12);
        assert.equal(elements.panel.hidden, false);
        assert.equal(elements.toggle.attributes["aria-expanded"], "true");
    });
}

test("popup opens above a low trigger and tracks viewport resize", () => {
    const { elements, viewport } = loadFilter(1200, 800, {
        left: 1040, top: 680, bottom: 722
    });
    elements.toggle.events.click();
    assert.ok(parseFloat(elements.panel.style.top) < 680);
    assert.ok(parseFloat(elements.panel.style.maxHeight) <= 776);
    viewport.width = 375;
    viewport.height = 420;
    viewport.events.resize();
    assert.equal(elements.panel.hidden, true);
});

test("Escape closes the popup and restores trigger focus", () => {
    const { elements } = loadFilter(375, 800, { left: 35, top: 200, bottom: 242 });
    elements.toggle.events.click();
    elements.panel.events.keydown({ key: "Escape", preventDefault() {} });
    assert.equal(elements.panel.hidden, true);
    assert.equal(elements.toggle.attributes["aria-expanded"], "false");
    assert.equal(elements.toggle.focused, true);
});
