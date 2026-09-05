/* ------------------------------------------------------------------
   PremiumMotors progressive enhancement.

   Every behaviour here is additive: with JavaScript disabled the site
   still navigates, submits and validates exactly as before. Nothing in
   this file is required for a page to function.
   ------------------------------------------------------------------ */
(function () {
    'use strict';

    var root = document.documentElement;
    var reduceMotion = window.matchMedia('(prefers-reduced-motion: reduce)');

    /* Cross-document View Transitions are the real page animation. When
       the browser lacks them we fall back to a CSS fade driven by the
       classes below, so Firefox still gets movement. */
    var hasViewTransitions =
        typeof document.startViewTransition === 'function' &&
        CSS.supports('selector(:active-view-transition)');

    if (!hasViewTransitions && !reduceMotion.matches) {
        root.classList.add('pm-nav-fallback');
    }

    /* --- Arrival intro -------------------------------------------- */

    /* The intro is entirely CSS: the head script decides whether it runs,
       and the curtain animates itself out with fill-mode forwards. Nothing
       below is load-bearing -- if this file never executes, the intro still
       plays and still clears. All it adds is the ability to skip it and the
       removal of a dead overlay from the DOM afterwards. */
    if (root.hasAttribute("data-pm-intro")) {
        var introDone = false;

        var retireIntro = function () {
            root.removeAttribute("data-pm-intro");
        };

        var skipIntro = function () {
            if (introDone) { return; }
            introDone = true;

            /* Skipping has to reveal the CONTENT, not just uncover the
               curtain: the entrance choreography holds elements invisible
               behind delays of up to 900ms, so dropping pm-arrive is what
               actually makes the page appear. */
            root.classList.remove("pm-arrive");
            root.setAttribute("data-pm-intro", "out");
            window.setTimeout(retireIntro, 200);
        };

        ["pointerdown", "keydown", "wheel", "touchstart"].forEach(function (type) {
            window.addEventListener(type, skipIntro, { once: true, passive: true });
        });

        /* Runs a little past the 1000ms curtain so the attribute is gone by
           the time anyone can interact. pm-arrive deliberately stays: it is
           what tells motion.css that this page used the timed stagger rather
           than the scroll-driven reveal. */
        window.setTimeout(function () {
            if (introDone) { return; }
            introDone = true;
            retireIntro();
        }, 1100);
    }

    /* --- Navigation progress + leave animation ------------------- */

    var progress = document.createElement('div');
    progress.className = 'pm-progress';
    progress.setAttribute('aria-hidden', 'true');
    document.body.appendChild(progress);

    var progressTimer = null;

    function startProgress() {
        window.clearTimeout(progressTimer);
        progress.classList.remove('is-done');
        // Delay slightly: instant navigations should not flash a bar.
        progressTimer = window.setTimeout(function () {
            progress.classList.add('is-active');
        }, 120);
    }

    function endProgress() {
        window.clearTimeout(progressTimer);
        if (!progress.classList.contains('is-active')) { return; }
        progress.classList.remove('is-active');
        progress.classList.add('is-done');
        window.setTimeout(function () {
            progress.classList.remove('is-done');
        }, 400);
    }

    function isInternalNavigation(link) {
        if (!link || link.target === '_blank' || link.hasAttribute('download')) { return false; }
        if (link.origin !== window.location.origin) { return false; }
        var href = link.getAttribute('href') || '';
        if (href.charAt(0) === '#' || /^(mailto|tel|javascript):/i.test(href)) { return false; }
        // Same page, different hash: no navigation happens.
        if (link.pathname === window.location.pathname &&
            link.search === window.location.search && link.hash) { return false; }
        return true;
    }

    document.addEventListener('click', function (e) {
        if (e.defaultPrevented || e.button !== 0 ||
            e.metaKey || e.ctrlKey || e.shiftKey || e.altKey) { return; }

        var link = e.target.closest('a[href]');
        if (!isInternalNavigation(link)) { return; }

        root.removeAttribute('data-pm-nav');
        startProgress();
        if (root.classList.contains('pm-nav-fallback')) {
            root.classList.add('pm-nav-leaving');
        }
    });

    /* Back/forward should animate in the opposite direction; motion.css
       reads this attribute. */
    window.addEventListener('popstate', function () {
        root.setAttribute('data-pm-nav', 'back');
    });

    /* Fires on normal loads and on restores from the back/forward cache,
       where the leave classes would otherwise stay stuck on. */
    window.addEventListener('pageshow', function () {
        root.classList.remove('pm-nav-leaving');
        endProgress();
    });

    window.addEventListener('beforeunload', function () {
        root.classList.remove('pm-nav-leaving');
    });

    /* --- Busy buttons -------------------------------------------- */

    /* Guards against double-submitting an offer or a listing on a slow
       mobile connection, and gives instant feedback on the tap. */
    document.addEventListener('submit', function (e) {
        var form = e.target;
        if (e.defaultPrevented || form.noValidate === false && !form.checkValidity()) { return; }

        var button = form.querySelector('button[type="submit"], input[type="submit"]');
        if (!button || button.classList.contains('is-busy')) { return; }

        startProgress();
        // Deferred so the button still counts as the submitter and its
        // name/value still reach the server.
        window.setTimeout(function () {
            button.classList.add('is-busy');
            button.setAttribute('aria-busy', 'true');
        }, 0);
    });

    /* --- Image fade-in -------------------------------------------- */

    function markLoaded(img) {
        var wrap = img.closest('.pm-media');
        if (wrap) { wrap.classList.add('is-loaded'); }
    }

    document.querySelectorAll('.pm-media img').forEach(function (img) {
        if (img.complete && img.naturalWidth > 0) {
            markLoaded(img);
        } else {
            // Only an image that is genuinely still loading gets held at
            // opacity 0; see the .is-pending note in motion.css.
            var wrap = img.closest('.pm-media');
            if (wrap) { wrap.classList.add('is-pending'); }
            img.addEventListener('load', function () { markLoaded(img); }, { once: true });
            // A broken photo must not leave the shimmer running forever.
            img.addEventListener('error', function () { markLoaded(img); }, { once: true });
        }
    });

    /* --- Staggered reveal ----------------------------------------- */

    document.querySelectorAll('.pm-stagger').forEach(function (list) {
        Array.prototype.forEach.call(list.children, function (child, i) {
            // Capped so a full page of listings finishes arriving fast.
            child.style.setProperty('--pm-i', Math.min(i, 10));
        });
    });

    /* --- Auto-dismissing alerts ----------------------------------- */

    document.querySelectorAll('[data-pm-autodismiss]').forEach(function (alert) {
        window.setTimeout(function () {
            if (window.bootstrap && window.bootstrap.Alert) {
                window.bootstrap.Alert.getOrCreateInstance(alert).close();
            } else {
                alert.remove();
            }
        }, 6000);
    });

    /* --- Toast ---------------------------------------------------- */

    var toastEl = null;
    var toastTimer = null;

    window.pmToast = function (message) {
        if (!toastEl) {
            toastEl = document.createElement('div');
            toastEl.className = 'pm-toast';
            toastEl.setAttribute('role', 'status');
            toastEl.setAttribute('aria-live', 'polite');
            document.body.appendChild(toastEl);
        }
        toastEl.textContent = message;
        // Reflow so a repeat message re-runs the entrance transition.
        void toastEl.offsetWidth;
        toastEl.classList.add('is-visible');
        window.clearTimeout(toastTimer);
        toastTimer = window.setTimeout(function () {
            toastEl.classList.remove('is-visible');
        }, 2600);
    };
    /* The browse filters are a plain Bootstrap collapse now, promoted to a static
       sidebar at lg by d-lg-block. That needs no JavaScript at all — the offcanvas
       version did, to dispose its instance when crossing the breakpoint. */

    /* --- Listing photos: downscale in the browser ------------------ */

    /* The input has always had "multiple", so selecting several photos worked.
       Sending them did not: a phone camera writes 3-8 MB per shot, so six
       pictures from a camera roll exceeded the request body limit and came
       back as a bare 413 with no explanation. The server limit is now sized to
       the storage policy (see UploadLimits), but transferring 60 MB over
       mobile data is still a bad experience even when it succeeds.

       So resize before sending. A 4000px phone photo becomes 1920px at quality
       0.82 - typically 6 MB down to about 400 KB - which is still more detail
       than the largest place the photo is ever displayed.

       Entirely optional: if any part of this is unavailable or throws, the
       original files are left exactly as chosen and the upload proceeds. */

    var photoInput = document.getElementById('photos');

    if (photoInput && window.DataTransfer && window.createImageBitmap &&
        HTMLCanvasElement.prototype.toBlob) {

        var MAX_EDGE = 1920;
        var QUALITY = 0.82;

        var photoStatus = document.createElement('div');
        photoStatus.className = 'form-text';
        photoStatus.setAttribute('role', 'status');
        photoStatus.setAttribute('aria-live', 'polite');
        // After the static hint if there is one, otherwise straight after the input.
        var hint = photoInput.parentNode.querySelector('.form-text');
        var anchor = hint || photoInput;
        anchor.parentNode.insertBefore(photoStatus, anchor.nextSibling);

        var photoForm = photoInput.form;
        var working = false;
        var pendingSubmit = false;

        var mb = function (bytes) { return (bytes / 1048576).toFixed(1) + ' MB'; };

        var setBusy = function (busy) {
            working = busy;
            if (!photoForm) { return; }
            var submit = photoForm.querySelector('button[type="submit"], input[type="submit"]');
            if (submit) { submit.disabled = busy; }
        };

        var shrink = function (file) {
            if (file.type.indexOf('image/') !== 0) { return Promise.resolve(file); }

            // imageOrientation matters: without it a portrait photo from a phone
            // is re-encoded sideways, because the EXIF rotation is dropped when
            // the bitmap is drawn to a canvas.
            return createImageBitmap(file, { imageOrientation: 'from-image' })
                .then(function (bmp) {
                    var scale = Math.min(1, MAX_EDGE / Math.max(bmp.width, bmp.height));
                    if (scale === 1 && file.size < 900000) { bmp.close(); return file; }

                    var canvas = document.createElement('canvas');
                    canvas.width = Math.round(bmp.width * scale);
                    canvas.height = Math.round(bmp.height * scale);
                    canvas.getContext('2d').drawImage(bmp, 0, 0, canvas.width, canvas.height);
                    bmp.close();

                    return new Promise(function (resolve) {
                        canvas.toBlob(function (blob) {
                            // Re-encoding can enlarge an already-small or already-optimised
                            // image. Keep whichever is smaller.
                            if (!blob || blob.size >= file.size) { resolve(file); return; }
                            // No regex here on purpose: the extension is the last dot.
                            var dot = file.name.lastIndexOf('.');
                            var name = (dot > 0 ? file.name.slice(0, dot) : file.name) + '.jpg';
                            resolve(new File([blob], name, {
                                type: 'image/jpeg', lastModified: Date.now()
                            }));
                        }, 'image/jpeg', QUALITY);
                    });
                })
                .catch(function () { return file; });
        };

        photoInput.addEventListener('change', function () {
            var chosen = Array.prototype.slice.call(photoInput.files || []);
            if (!chosen.length) { photoStatus.textContent = ''; return; }

            var before = chosen.reduce(function (n, f) { return n + f.size; }, 0);
            photoStatus.textContent =
                'Preparing ' + chosen.length + ' photo' + (chosen.length === 1 ? '' : 's') + '...';
            setBusy(true);

            Promise.all(chosen.map(shrink)).then(function (files) {
                try {
                    var dt = new DataTransfer();
                    files.forEach(function (f) { dt.items.add(f); });
                    photoInput.files = dt.files;
                } catch (e) {
                    /* Assigning .files is refused in a few environments. The
                       originals are still selected, so the upload still works. */
                    files = chosen;
                }

                var after = files.reduce(function (n, f) { return n + f.size; }, 0);
                var line = files.length + ' photo' + (files.length === 1 ? '' : 's') +
                           ' ready, ' + mb(after);
                if (after < before * 0.9) { line += ' (reduced from ' + mb(before) + ')'; }
                photoStatus.textContent = line;

                setBusy(false);
                if (pendingSubmit) {
                    pendingSubmit = false;
                    if (photoForm) { photoForm.requestSubmit ? photoForm.requestSubmit() : photoForm.submit(); }
                }
            });
        });

        /* Somebody who taps Submit while the resizing is still running must not
           post a half-prepared form. Hold the submit and replay it when the
           photos are ready. */
        if (photoForm) {
            photoForm.addEventListener('submit', function (e) {
                if (!working) { return; }
                e.preventDefault();
                pendingSubmit = true;
            });
        }
    }

    /* --- Share sheet ---------------------------------------------- */

    /* Uses the OS share sheet on mobile where it exists, which is what a
       user reaching for "share" on a phone actually expects. */
    document.querySelectorAll('[data-pm-share]').forEach(function (button) {
        var url = button.getAttribute('data-pm-share');
        var title = button.getAttribute('data-pm-share-title') || document.title;

        if (!navigator.share) {
            button.hidden = true;
            return;
        }

        button.addEventListener('click', function () {
            navigator.share({ title: title, url: url }).catch(function () {
                /* The user dismissed the sheet; nothing to report. */
            });
        });
    });

    document.querySelectorAll('[data-pm-copy]').forEach(function (button) {
        button.addEventListener('click', function () {
            var target = document.querySelector(button.getAttribute('data-pm-copy'));
            if (!target) { return; }

            var done = function () { window.pmToast('Link copied'); };

            if (navigator.clipboard && window.isSecureContext) {
                navigator.clipboard.writeText(target.value).then(done, function () {
                    target.select();
                    document.execCommand('copy');
                    done();
                });
            } else {
                target.select();
                target.setSelectionRange(0, 99999);
                document.execCommand('copy');
                done();
            }
        });
    });
})();

/* ---------------------------------------------------------------------------
   Landing pages: hand the "Browse the marketplace" call to action over to the
   header once the hero button has scrolled out of view.

   The two buttons are the same destination deliberately - the point is that
   the invitation is never off screen, not that there are two of them. Only
   one is visible at a time.

   Watching the hero button itself rather than a scroll offset means the
   handover happens exactly when it disappears, at any viewport size, with no
   magic pixel numbers to get wrong on a phone.
   --------------------------------------------------------------------------- */
(function () {
    var header = document.querySelector('[data-pm-header-cta]');
    if (!header) { return; }

    var anchor = document.querySelector('[data-pm-cta-anchor]');

    /* No anchor, or a browser without IntersectionObserver: show the header
       button and leave it there. A permanently visible call to action is a
       far smaller failure than one that never appears. */
    if (!anchor || !('IntersectionObserver' in window)) {
        document.documentElement.classList.add('pm-cta-on');
        return;
    }

    new IntersectionObserver(function (entries) {
        document.documentElement.classList.toggle('pm-cta-on', !entries[0].isIntersecting);
    }, { threshold: 0 }).observe(anchor);
})();
