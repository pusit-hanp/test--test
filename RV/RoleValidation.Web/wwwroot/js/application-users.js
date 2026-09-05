(() => {
    const page = document.querySelector("[data-review-page]");
    if (!page) return;

    const table = page.querySelector("[data-user-table]");
    const loadingMask = page.querySelector("[data-loading-mask]");
    const multiSelectFilters = Array.from(
        page.querySelectorAll("[data-multi-select-filter]"));
    const applicationSelect = page.querySelector("[data-application-select]");
    const loadForm = applicationSelect?.form;

    if (applicationSelect && loadForm) {
        applicationSelect.addEventListener("change", () => {
            const url = new URL(
                loadForm.action || window.location.href,
                window.location.href);
            url.search = "";
            url.searchParams.set("applicationId", applicationSelect.value);
            if (loadingMask) loadingMask.hidden = false;
            window.location.assign(url);
        });
    }

    multiSelectFilters.forEach(filter => {
        const toggle = filter.querySelector("[data-filter-toggle]");
        const panel = filter.querySelector("[data-filter-panel]");
        const search = filter.querySelector("[data-filter-search]");
        const clear = filter.querySelector("[data-filter-clear]");
        const mode = filter.querySelector("[data-filter-mode]");
        const selectAll = filter.querySelector("[data-filter-select-all]");
        const selectAllLabel = filter.querySelector(
            "[data-filter-select-all-label]");
        const counter = filter.querySelector("[data-filter-counter]");
        const summary = filter.querySelector("[data-filter-summary]");
        const options = Array.from(
            filter.querySelectorAll("[data-filter-option]"));

        if (!toggle || !panel || !search || !clear || !mode ||
            !selectAll || !selectAllLabel || !counter || !summary) {
            return;
        }

        const positionPanel = () => {
            if (panel.hidden) return;
            const viewport = window.visualViewport;
            const viewportLeft = viewport?.offsetLeft || 0;
            const viewportTop = viewport?.offsetTop || 0;
            const viewportWidth = viewport?.width || document.documentElement.clientWidth;
            const viewportHeight = viewport?.height || window.innerHeight;
            const gap = 8;
            const margin = 12;
            const anchor = toggle.getBoundingClientRect();
            if (anchor.bottom < viewportTop || anchor.top > viewportTop + viewportHeight) {
                setOpen(false);
                return;
            }
            const width = Math.min(420, Math.max(0, viewportWidth - margin * 2));
            panel.style.width = width + "px";
            panel.style.left = Math.max(viewportLeft + margin,
                Math.min(anchor.left, viewportLeft + viewportWidth - width - margin)) + "px";
            const below = viewportTop + viewportHeight - margin - anchor.bottom - gap;
            const above = anchor.top - viewportTop - margin - gap;
            const openAbove = below < 220 && above > below;
            const available = Math.min(viewportHeight - margin * 2,
                Math.max(0, openAbove ? above : below));
            panel.style.maxHeight = available + "px";
            panel.style.top = (openAbove
                ? Math.max(viewportTop + margin, anchor.top - gap - panel.getBoundingClientRect().height)
                : Math.max(viewportTop + margin, anchor.bottom + gap)) + "px";
        };

        const setOpen = open => {
            panel.hidden = !open;
            toggle.setAttribute("aria-expanded", String(open));
            filter.dataset.open = String(open);
            if (open) {
                positionPanel();
                search.focus({ preventScroll: true });
            }
        };

        window.addEventListener("resize", positionPanel);
        window.addEventListener("scroll", positionPanel, true);
        window.visualViewport?.addEventListener("resize", positionPanel);
        window.visualViewport?.addEventListener("scroll", positionPanel);

        const setOptionState = option => {
            const label = option.closest("[data-filter-option-label]");
            label.dataset.selected = String(option.checked);
        };

        const updateSelection = () => {
            const selectedCount = options.filter(option => option.checked).length;
            const totalCount = options.length;

            options.forEach(setOptionState);

            if (totalCount === 0) {
                selectAll.checked = true;
                selectAll.indeterminate = false;
                selectAll.disabled = true;
                selectAllLabel.dataset.selected = "true";
                mode.value = "All";
                counter.textContent = "0 selected";
                summary.textContent = filter.dataset.unavailableSummary;
                return;
            }

            selectAll.disabled = false;
            selectAll.checked = selectedCount === totalCount;
            selectAll.indeterminate =
                selectedCount > 0 && selectedCount < totalCount;
            selectAllLabel.dataset.selected =
                String(selectAll.checked || selectAll.indeterminate);

            if (selectedCount === totalCount) {
                mode.value = "All";
                summary.textContent = filter.dataset.allSummary;
            } else if (selectedCount === 0) {
                mode.value = "None";
                summary.textContent = filter.dataset.noneSummary;
            } else {
                mode.value = "Selected";
                summary.textContent =
                    selectedCount + " " + filter.dataset.selectedSuffix;
            }

            counter.textContent = selectedCount + " selected";
        };

        toggle.addEventListener("click", () => {
            setOpen(panel.hidden);
        });

        selectAll.addEventListener("change", () => {
            options.forEach(option => {
                option.checked = selectAll.checked;
            });
            updateSelection();
        });

        options.forEach(option => {
            option.addEventListener("change", updateSelection);
        });

        search.addEventListener("input", () => {
            const query = search.value.trim().toLocaleLowerCase();
            filter
                .querySelectorAll("[data-filter-option-label]")
                .forEach(label => {
                    label.hidden =
                        query.length > 0 &&
                        !label.textContent.toLocaleLowerCase().includes(query);
                });
        });

        clear.addEventListener("click", () => {
            search.value = "";
            filter
                .querySelectorAll("[data-filter-option-label]")
                .forEach(label => {
                    label.hidden = false;
                });
            options.forEach(option => {
                option.checked = true;
            });
            updateSelection();
            search.focus();
        });

        panel.addEventListener("keydown", event => {
            if (event.key === "Escape") {
                event.preventDefault();
                setOpen(false);
                toggle.focus();
            }
        });

        document.addEventListener("click", event => {
            if (!filter.contains(event.target)) {
                setOpen(false);
            }
        });

        updateSelection();
    });

    table?.querySelectorAll("button[data-sortable]").forEach(button => {
        const sort = () => {
            const sortKey = button.dataset.sortKey;
            const nextDirection = button.dataset.nextSortDirection;
            if (!sortKey || !nextDirection) return;

            const url = new URL(window.location.href);
            url.searchParams.set("sortBy", sortKey);
            url.searchParams.set("sortDirection", nextDirection);
            url.searchParams.set("page", "1");

            if (loadingMask) loadingMask.hidden = false;
            window.location.assign(url);
        };

        button.addEventListener("click", sort);
    });

    page.querySelectorAll("[data-load-form]").forEach(form => {
        form.addEventListener("submit", () => {
            if (loadingMask) loadingMask.hidden = false;
        });
    });

    page.querySelector("[data-export-form]")?.addEventListener(
        "submit",
        event => {
            const button = event.currentTarget.querySelector("button");
            if (!button) return;
            button.disabled = true;
            button.textContent = "Preparing .xlsx…";
            window.setTimeout(() => {
                button.disabled = false;
                button.textContent = "Export filtered .xlsx";
            }, 2500);
        });
})();
