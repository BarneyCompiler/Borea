// Runs site/description-images.js on a small browser document with a fake network, one page per test.
"use strict";

const assert = require("node:assert/strict");
const crypto = require("node:crypto");
const fs = require("node:fs");
const path = require("node:path");
const test = require("node:test");
const vm = require("node:vm");
const zlib = require("node:zlib");
const { resolveObjectURL } = require("node:buffer");

const FILE = path.resolve(__dirname, "../../site/description-images.js");
const SOURCE = fs.readFileSync(FILE, "utf8");
const PAGE = "https://ksamodding.github.io/Borea/mod/Example/";
const SETTING = "borea.author-images";

class ClassList {
  constructor() {
    this.names = new Set();
  }

  add(name) {
    this.names.add(name);
  }

  remove(name) {
    this.names.delete(name);
  }

  contains(name) {
    return this.names.has(name);
  }
}

// A tag, classes and attributes, which is all the script asks of a selector.
function matches(node, selector) {
  const parts = /^([a-z]*)((?:\.[\w-]+)*)((?:\[[\w-]+(?:=[\w-]+)?\])*)$/.exec(selector);
  if (!parts) {
    throw new Error("the fake document does not read the selector " + selector);
  }
  if (parts[1] && node.tagName !== parts[1]) {
    return false;
  }
  const classes = parts[2].split(".").filter(Boolean);
  const attributes = [...parts[3].matchAll(/\[([\w-]+)(?:=([\w-]+))?\]/g)];
  return classes.every((name) => node.classList.contains(name)) &&
    attributes.every(([, name, value]) => name === "type" ? node.type === value : node.getAttribute(name) !== null);
}

class Element {
  constructor(tagName, attributes) {
    this.tagName = tagName;
    this.attributes = Object.assign({}, attributes);
    this.classList = new ClassList();
    (this.attributes.class || "").split(" ").filter(Boolean).forEach((name) => this.classList.add(name));
    this.children = [];
    this.parentNode = null;
    this.listeners = {};
  }

  set className(value) {
    this.classList = new ClassList();
    value.split(" ").filter(Boolean).forEach((name) => this.classList.add(name));
  }

  set textContent(value) {
    this.children = [{ data: String(value) }];
  }

  getAttribute(name) {
    return Object.prototype.hasOwnProperty.call(this.attributes, name) ? this.attributes[name] : null;
  }

  appendChild(child) {
    return this.insertBefore(child, null);
  }

  insertBefore(child, before) {
    const at = before ? this.children.indexOf(before) : -1;
    this.children.splice(at < 0 ? this.children.length : at, 0, child);
    child.parentNode = this;
    return child;
  }

  removeChild(child) {
    this.children.splice(this.children.indexOf(child), 1);
    child.parentNode = null;
    return child;
  }

  addEventListener(type, handler) {
    (this.listeners[type] = this.listeners[type] || []).push(handler);
  }

  dispatch(type) {
    (this.listeners[type] || []).forEach((handler) => handler());
  }

  querySelectorAll(selector) {
    const found = [];
    const visit = (node) => {
      (node.children || []).forEach((child) => {
        if (child instanceof Element) {
          if (matches(child, selector)) {
            found.push(child);
          }
          visit(child);
        }
      });
    };
    visit(this);
    return found;
  }

  querySelector(selector) {
    return this.querySelectorAll(selector)[0] || null;
  }

  get text() {
    return this.children.map((child) => child instanceof Element ? child.text : child.data).join("");
  }
}

function figure(record, caption) {
  const node = new Element("figure", {
    "data-image": record.url, "data-sha256": record.sha256, "data-width": String(record.width),
    "data-height": String(record.height), "data-size": String(record.size)
  });
  node.appendChild(new Element("div", { class: "frame" }));
  const below = node.appendChild(new Element("figcaption"));
  below.appendChild({ data: caption });
  return node;
}

// The description of a share page as the generator writes it, with the switch when `withSwitch` is set.
function description(records, withSwitch) {
  const section = new Element("section", { class: "description" });
  if (withSwitch) {
    const line = section.appendChild(new Element("p", { class: "image-switch" }));
    const label = line.appendChild(new Element("label"));
    const box = label.appendChild(new Element("input"));
    box.type = "checkbox";
    box.checked = false;
  }
  const prose = section.appendChild(new Element("div", { class: "prose" }));
  records.forEach((record, index) => prose.appendChild(figure(record, "Image " + (index + 1))));
  return section;
}

// A still PNG of the given size, so the image the script decodes has the pixels its record names.
function png(width, height) {
  const chunk = (kind, data) => {
    const length = Buffer.alloc(4);
    length.writeUInt32BE(data.length);
    const body = Buffer.concat([Buffer.from(kind, "ascii"), data]);
    const check = Buffer.alloc(4);
    check.writeUInt32BE(zlib.crc32(body));
    return Buffer.concat([length, body, check]);
  };
  const header = Buffer.alloc(13);
  header.writeUInt32BE(width, 0);
  header.writeUInt32BE(height, 4);
  header[8] = 8;
  header[9] = 2;
  const rows = Buffer.alloc((width * 3 + 1) * height);
  return Buffer.concat([Buffer.from([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a]), chunk("IHDR", header),
    chunk("IDAT", zlib.deflateSync(rows)), chunk("IEND", Buffer.alloc(0))]);
}

// An image a host serves and the record that names it.
function image(url, width) {
  const bytes = png(width, 2);
  return {
    bytes: bytes,
    record: { url: url, sha256: crypto.createHash("sha256").update(bytes).digest("hex"), width: width, height: 2,
              size: bytes.length }
  };
}

// Answers from `hosts`, a map of address to bytes or to { redirect: address }, and keeps every request it
// sent, which is what the tests read to see that a host was never asked.
function network(hosts) {
  const sent = [];
  function answer(url, options) {
    sent.push({ url: url, options: options });
    const served = hosts[url];
    if (served && served.redirect) {
      // The browser gives up at the redirect when the page asked it to, and sends no second request.
      if (options.redirect === "error") {
        return Promise.reject(new TypeError("Failed to fetch"));
      }
      return answer(served.redirect, options);
    }
    if (!served) {
      return Promise.resolve({ ok: false, status: 404, url: url, headers: new Headers(), body: null });
    }
    return Promise.resolve({
      ok: true, status: 200, url: url, headers: new Headers({ "Content-Length": String(served.length) }),
      body: new Response(served).body
    });
  }
  return { sent: sent, fetch: (url, options) => answer(url, options) };
}

// The image a browser decodes from the blob, with the size its PNG header names.
class FakeImage {
  constructor(width, height) {
    this.width = width;
    this.height = height;
    this.src = "";
  }

  decode() {
    return resolveObjectURL(this.src).arrayBuffer().then((buffer) => {
      const bytes = Buffer.from(buffer);
      this.naturalWidth = bytes.readUInt32BE(16);
      this.naturalHeight = bytes.readUInt32BE(20);
    });
  }
}

// Runs the script as the page runs it, before the body, once the section is in the document.
function open(options) {
  const root = new Element("html");
  const section = description(options.records.map((entry) => entry.record), options.withSwitch !== false);
  const net = network(options.hosts || {});
  const stored = {};
  if (options.setting) {
    stored[SETTING] = options.setting;
  }
  // The built-ins of this realm, so the bytes the script reads are the ones Node hashes and wraps in a blob.
  const page = {
    location: { href: PAGE },
    localStorage: {
      getItem: (key) => Object.prototype.hasOwnProperty.call(stored, key) ? stored[key] : null,
      setItem: (key, value) => { stored[key] = String(value); }
    },
    fetch: net.fetch,
    Image: FakeImage,
    document: {
      documentElement: root,
      readyState: "complete",
      createElement: (tag) => new Element(tag),
      createTextNode: (data) => ({ data: data }),
      querySelector: (selector) => matches(section, selector) ? section : section.querySelector(selector)
    },
    URL: URL, crypto: globalThis.crypto, Blob: Blob, AbortController: AbortController, Promise: Promise, Map: Map,
    Uint8Array: Uint8Array, DataView: DataView, Error: Error, Number: Number, String: String, Object: Object,
    Array: Array, setTimeout: setTimeout, clearTimeout: clearTimeout
  };
  page.window = page;
  vm.runInNewContext(SOURCE, page, { filename: FILE });
  return { root: root, section: section, sent: net.sent, stored: stored, figures: section.querySelectorAll("figure") };
}

// Every answer of the fake network and every digest has arrived once the event loop is idle this long.
function settle() {
  return new Promise((resolve) => setTimeout(resolve, 100));
}

function shown(node) {
  return !node.classList.contains("no-image") && node.querySelector(".frame").children.length === 1;
}

function caption(node) {
  return node.classList.contains("no-image") && node.querySelector(".frame").children.length === 0;
}

// The same bytes with one byte of the last checksum changed, which no check but the digest reads.
function changed(bytes) {
  const copy = Buffer.from(bytes);
  copy[copy.length - 1] ^= 0xff;
  return copy;
}

const GITHUB = image("https://raw.githubusercontent.com/author/mod/0123abc/images/shot.png", 5);
const OTHER = image("https://example.org/shot.png", 3);
const REDIRECT = "https://raw.githubusercontent.com/author/mod/main/images/moved.png";

test("with the switch off an image on another host gets no request", async () => {
  const page = open({ records: [OTHER], hosts: { [OTHER.record.url]: OTHER.bytes } });
  await settle();

  assert.deepEqual(page.sent, []);
  assert.equal(page.figures[0].querySelector(".frame").children.length, 0);
});

test("turning the switch on loads an image on another host the way the record names it", async () => {
  const page = open({ records: [OTHER], hosts: { [OTHER.record.url]: OTHER.bytes } });
  const box = page.section.querySelector("input");
  box.checked = true;
  box.dispatch("change");
  await settle();

  assert.equal(page.stored[SETTING], "on");
  assert.ok(page.root.classList.contains("author-images"));
  assert.ok(shown(page.figures[0]));
  assert.deepEqual(page.sent.map((request) => request.url), [OTHER.record.url]);
  const options = page.sent[0].options;
  assert.equal(options.mode, "cors");
  assert.equal(options.credentials, "omit");
  assert.equal(options.referrerPolicy, "no-referrer");
});

test("a reader who turned the switch on keeps it", async () => {
  const page = open({ records: [OTHER], hosts: { [OTHER.record.url]: OTHER.bytes }, setting: "on" });
  await settle();

  assert.ok(page.section.querySelector("input").checked);
  assert.ok(shown(page.figures[0]));
});

test("an image on another host whose bytes do not match its record shows its caption", async () => {
  const page = open({ records: [OTHER], hosts: { [OTHER.record.url]: changed(OTHER.bytes) }, setting: "on" });
  await settle();

  assert.ok(caption(page.figures[0]));
});

test("with the switch off an image on GitHub shows and an image on another host gets no request", async () => {
  const page = open({
    records: [GITHUB, OTHER],
    hosts: { [GITHUB.record.url]: GITHUB.bytes, [OTHER.record.url]: OTHER.bytes }
  });
  await settle();

  assert.ok(shown(page.figures[0]));
  assert.ok(page.figures[0].classList.contains("github"));
  assert.equal(page.figures[1].querySelector(".frame").children.length, 0);
  assert.ok(!page.figures[1].classList.contains("github"));
  assert.ok(!page.root.classList.contains("author-images"));
  assert.deepEqual(page.sent.map((request) => request.url), [GITHUB.record.url]);
  const options = page.sent[0].options;
  assert.equal(options.redirect, "error");
  assert.equal(options.mode, "cors");
  assert.equal(options.credentials, "omit");
  assert.equal(options.referrerPolicy, "no-referrer");
  assert.ok(page.section.querySelector(".image-switch"));
});

test("with the switch off a GitHub address that redirects shows its caption and asks no other host", async () => {
  const moved = { url: REDIRECT, sha256: OTHER.record.sha256, width: 3, height: 2, size: OTHER.record.size };
  const page = open({
    records: [{ record: moved }],
    hosts: { [REDIRECT]: { redirect: OTHER.record.url }, [OTHER.record.url]: OTHER.bytes }
  });
  await settle();

  assert.ok(caption(page.figures[0]));
  assert.deepEqual(page.sent.map((request) => request.url), [REDIRECT]);
});

test("with the switch on a GitHub address that redirects loads as before", async () => {
  const moved = { url: REDIRECT, sha256: OTHER.record.sha256, width: 3, height: 2, size: OTHER.record.size };
  const page = open({
    records: [{ record: moved }, OTHER], setting: "on",
    hosts: { [REDIRECT]: { redirect: OTHER.record.url }, [OTHER.record.url]: OTHER.bytes }
  });
  await settle();

  assert.ok(shown(page.figures[0]));
  assert.equal(page.sent[0].options.redirect, "follow");
});

test("turning the switch on asks again for a GitHub image that failed and follows its redirect", async () => {
  const target = image("https://example.net/moved.png", 4);
  const moved = Object.assign({}, target.record, { url: REDIRECT });
  const page = open({
    records: [{ record: moved }, OTHER],
    hosts: {
      [REDIRECT]: { redirect: target.record.url }, [target.record.url]: target.bytes,
      [OTHER.record.url]: OTHER.bytes
    }
  });
  await settle();
  assert.ok(caption(page.figures[0]));

  const box = page.section.querySelector("input");
  box.checked = true;
  box.dispatch("change");
  await settle();

  assert.ok(shown(page.figures[0]));
  assert.ok(shown(page.figures[1]));
  const again = page.sent.filter((request) => request.url === REDIRECT);
  assert.deepEqual(again.map((request) => request.options.redirect), ["error", "follow"]);
  assert.ok(page.sent.some((request) => request.url === target.record.url));
});

test("a page that has only GitHub images shows no switch", async () => {
  const page = open({ records: [GITHUB], hosts: { [GITHUB.record.url]: GITHUB.bytes } });
  await settle();

  assert.equal(page.section.querySelector(".image-switch"), null);
  assert.ok(shown(page.figures[0]));
});

test("an image on GitHub whose bytes do not match its record shows its caption", async () => {
  const page = open({ records: [GITHUB], hosts: { [GITHUB.record.url]: changed(GITHUB.bytes) } });
  await settle();

  assert.ok(caption(page.figures[0]));
});

test("only the exact GitHub host names count, compared on the parsed address", async () => {
  const hosts = {};
  const records = [
    "https://raw.githubusercontent.com.evil.example/shot.png",
    "https://images.raw.githubusercontent.com/shot.png",
    "https://evilraw.githubusercontent.com/shot.png",
    "https://RAW.GitHubUserContent.com/author/mod/0123abc/images/shot.png"
  ].map(function (url) {
    hosts[url] = GITHUB.bytes;
    return { record: Object.assign({}, GITHUB.record, { url: url }) };
  });
  const page = open({ records: records, hosts: hosts });
  await settle();

  assert.deepEqual(page.figures.map((node) => node.classList.contains("github")), [false, false, false, true]);
  assert.deepEqual(page.sent.map((request) => request.url), [records[3].record.url]);
  assert.ok(shown(page.figures[3]));
});

test("turning the switch off hides the images of other hosts and keeps the ones on GitHub", async () => {
  const page = open({
    records: [GITHUB, OTHER], setting: "on",
    hosts: { [GITHUB.record.url]: GITHUB.bytes, [OTHER.record.url]: OTHER.bytes }
  });
  await settle();
  const box = page.section.querySelector("input");
  box.checked = false;
  box.dispatch("change");

  assert.equal(page.stored[SETTING], "off");
  assert.ok(!page.root.classList.contains("author-images"));
  assert.ok(page.figures[0].classList.contains("github"));
  assert.ok(!page.figures[1].classList.contains("github"));
});

test("a page written without the switch gets one that names the other hosts", () => {
  const page = open({ records: [OTHER], withSwitch: false });
  const line = page.section.querySelector(".image-switch");

  assert.equal(line.querySelector("label").text, " Show images from other hosts");
  assert.equal(line.querySelector(".meta").text, "Images on GitHub show at once. An image on another host comes " +
    "from the server of its author, which then sees your IP address, as every website does.");
});
