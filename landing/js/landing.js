/* ============================================================
   Lindrake — Landing interactions
   Vanilla-first (works without GSAP); GSAP enhances if present.
   Everything degrades gracefully under prefers-reduced-motion.
   ============================================================ */
(function () {
    "use strict";

    var REDUCE = window.matchMedia("(prefers-reduced-motion: reduce)").matches;
    var TOUCH = window.matchMedia("(hover: none)").matches;

    /* ── Nav: stuck state on scroll ───────────────────────── */
    var nav = document.getElementById("nav");
    var onScroll = function () {
        if (nav) nav.classList.toggle("is-stuck", window.scrollY > 24);
    };
    window.addEventListener("scroll", onScroll, { passive: true });
    onScroll();

    /* ── Reveal on enter (IntersectionObserver, no deps) ──── */
    var reveals = Array.prototype.slice.call(document.querySelectorAll("[data-reveal]"));
    if (REDUCE || !("IntersectionObserver" in window)) {
        reveals.forEach(function (el) { el.classList.add("is-in"); });
    } else {
        var io = new IntersectionObserver(function (entries) {
            entries.forEach(function (e) {
                if (e.isIntersecting) {
                    var el = e.target;
                    // stagger siblings that share a parent
                    var sibs = el.parentElement ? el.parentElement.querySelectorAll(":scope > [data-reveal]") : [el];
                    var idx = Array.prototype.indexOf.call(sibs, el);
                    el.style.transitionDelay = (Math.max(0, idx) * 70) + "ms";
                    el.classList.add("is-in");
                    io.unobserve(el);
                }
            });
        }, { threshold: 0.18, rootMargin: "0px 0px -8% 0px" });
        reveals.forEach(function (el) { io.observe(el); });
    }

    /* ── Custom cursor + magnetic buttons ─────────────────── */
    var cursor = document.querySelector(".cursor");
    var magnets = Array.prototype.slice.call(document.querySelectorAll(".magnetic"));

    if (!TOUCH && !REDUCE && cursor) {
        var cx = window.innerWidth / 2, cy = window.innerHeight / 2;
        var tx = cx, ty = cy;
        window.addEventListener("mousemove", function (e) { tx = e.clientX; ty = e.clientY; }, { passive: true });
        (function loop() {
            cx += (tx - cx) * 0.18; cy += (ty - cy) * 0.18;
            cursor.style.transform = "translate(" + cx + "px," + cy + "px) translate(-50%,-50%)";
            requestAnimationFrame(loop);
        })();

        var hot = "a, button, .btn, .frag, .cap, .who-tile, .source-tags li";
        document.querySelectorAll(hot).forEach(function (el) {
            el.addEventListener("mouseenter", function () { cursor.classList.add("is-hot"); });
            el.addEventListener("mouseleave", function () { cursor.classList.remove("is-hot"); });
        });
    }

    if (!TOUCH && !REDUCE) {
        magnets.forEach(function (btn) {
            btn.addEventListener("mousemove", function (e) {
                var r = btn.getBoundingClientRect();
                var mx = e.clientX - (r.left + r.width / 2);
                var my = e.clientY - (r.top + r.height / 2);
                btn.style.transform = "translate(" + mx * 0.25 + "px," + my * 0.35 + "px)";
            });
            btn.addEventListener("mouseleave", function () { btn.style.transform = ""; });
        });
    }

    /* ── Optional GSAP parallax enhancement ───────────────── */
    function enhanceWithGsap() {
        if (REDUCE || !window.gsap || !window.ScrollTrigger) return;
        gsap.registerPlugin(ScrollTrigger);
        // subtle parallax drift on each section's blueprint grid
        gsap.utils.toArray(".section").forEach(function (sec) {
            gsap.fromTo(sec, { backgroundPositionY: "0px" }, {
                backgroundPositionY: "64px", ease: "none",
                scrollTrigger: { trigger: sec, start: "top bottom", end: "bottom top", scrub: true }
            });
        });
    }
    // GSAP is loaded with defer; run after load
    window.addEventListener("load", enhanceWithGsap);

    /* ── Hero constellation ───────────────────────────────── */
    var canvas = document.getElementById("constellation");
    if (!canvas) return;
    var ctx = canvas.getContext("2d");
    var hero = document.getElementById("hero");
    var DPR = Math.min(window.devicePixelRatio || 1, 2);
    var W = 0, H = 0;
    var nodes = [];
    var pointer = { x: -9999, y: -9999, active: false };
    var running = false, raf = 0, t = 0;

    var AMBER = "232,162,61";
    var PARCH = "236,229,214";

    function size() {
        var r = hero.getBoundingClientRect();
        W = r.width; H = r.height;
        canvas.width = Math.round(W * DPR);
        canvas.height = Math.round(H * DPR);
        canvas.style.width = W + "px";
        canvas.style.height = H + "px";
        ctx.setTransform(DPR, 0, 0, DPR, 0, 0);
        build();
    }

    function build() {
        nodes = [];
        // central "answer" node
        var cx = W * 0.66, cy = H * 0.5;
        nodes.push({ x: cx, y: cy, vx: 0, vy: 0, r: 7, core: true });
        // ambient nodes — density scales with area, capped for perf
        var count = Math.max(14, Math.min(46, Math.round((W * H) / 32000)));
        for (var i = 0; i < count; i++) {
            nodes.push({
                x: Math.random() * W,
                y: Math.random() * H,
                vx: (Math.random() - 0.5) * 0.22,
                vy: (Math.random() - 0.5) * 0.22,
                r: Math.random() * 1.6 + 0.8,
                core: false,
                src: i < 4 // first four are brighter "source" anchors
            });
        }
    }

    function step() {
        ctx.clearRect(0, 0, W, H);
        t += 0.016;
        var LINK = Math.min(190, W * 0.16);

        for (var i = 0; i < nodes.length; i++) {
            var n = nodes[i];
            if (!n.core) {
                n.x += n.vx; n.y += n.vy;
                // pointer parallax nudge
                if (pointer.active) {
                    var pdx = n.x - pointer.x, pdy = n.y - pointer.y;
                    var pd = Math.sqrt(pdx * pdx + pdy * pdy);
                    if (pd < 150) { n.x += (pdx / pd) * (150 - pd) * 0.006; n.y += (pdy / pd) * (150 - pd) * 0.006; }
                }
                if (n.x < 0 || n.x > W) n.vx *= -1;
                if (n.y < 0 || n.y > H) n.vy *= -1;
                n.x = Math.max(0, Math.min(W, n.x));
                n.y = Math.max(0, Math.min(H, n.y));
            }
        }

        // links
        for (var a = 0; a < nodes.length; a++) {
            for (var b = a + 1; b < nodes.length; b++) {
                var p = nodes[a], q = nodes[b];
                var dx = p.x - q.x, dy = p.y - q.y;
                var d = Math.sqrt(dx * dx + dy * dy);
                var toCore = p.core || q.core;
                var max = toCore ? LINK * 2.1 : LINK;
                if (d < max) {
                    var alpha = (1 - d / max) * (toCore ? 0.5 : 0.26);
                    ctx.strokeStyle = "rgba(" + (toCore ? AMBER : PARCH) + "," + alpha.toFixed(3) + ")";
                    ctx.lineWidth = toCore ? 0.9 : 0.6;
                    ctx.beginPath();
                    ctx.moveTo(p.x, p.y); ctx.lineTo(q.x, q.y);
                    ctx.stroke();
                }
            }
        }

        // nodes
        for (var k = 0; k < nodes.length; k++) {
            var m = nodes[k];
            if (m.core) {
                var pr = m.r + Math.sin(t * 1.6) * 1.8;
                ctx.save();
                ctx.translate(m.x, m.y);
                ctx.rotate(Math.PI / 4);
                // glow
                ctx.shadowColor = "rgba(" + AMBER + ",0.9)";
                ctx.shadowBlur = 24;
                ctx.fillStyle = "rgba(" + AMBER + ",1)";
                ctx.fillRect(-pr, -pr, pr * 2, pr * 2);
                ctx.restore();
            } else {
                ctx.beginPath();
                ctx.arc(m.x, m.y, m.r, 0, Math.PI * 2);
                ctx.fillStyle = m.src ? "rgba(" + AMBER + ",0.9)" : "rgba(" + PARCH + ",0.55)";
                ctx.fill();
            }
        }

        raf = requestAnimationFrame(step);
    }

    function start() { if (!running) { running = true; raf = requestAnimationFrame(step); } }
    function stop() { running = false; cancelAnimationFrame(raf); }

    size();
    canvas.classList.add("is-live");

    if (REDUCE) {
        step(); stop(); // draw a single static frame
    } else {
        // pause when hero scrolls out of view
        if ("IntersectionObserver" in window) {
            new IntersectionObserver(function (es) {
                es.forEach(function (e) { e.isIntersecting ? start() : stop(); });
            }, { threshold: 0 }).observe(hero);
        } else { start(); }

        if (!TOUCH) {
            hero.addEventListener("mousemove", function (e) {
                var r = canvas.getBoundingClientRect();
                pointer.x = e.clientX - r.left; pointer.y = e.clientY - r.top; pointer.active = true;
            });
            hero.addEventListener("mouseleave", function () { pointer.active = false; pointer.x = pointer.y = -9999; });
        }
    }

    var rt;
    window.addEventListener("resize", function () {
        clearTimeout(rt);
        rt = setTimeout(function () { size(); if (REDUCE) { step(); stop(); } }, 180);
    });
})();
